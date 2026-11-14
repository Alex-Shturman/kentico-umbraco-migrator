# Research: Kentico to Umbraco Content Migrator

Each entry: Decision, Rationale, Alternatives considered. Entries marked **Verify** depend on
Umbraco behaviour that MUST be confirmed against a running Umbraco 17 host by a dedicated task
before the dependent code is considered done (Constitution IV).

## R1. Source access: Dapper over Microsoft.Data.SqlClient

- **Decision**: Plain SQL with Dapper; no Kentico API, no Kentico DLLs; version check on
  `CMS_SettingsKey.CMSDBVersion` (must start with `11.`).
- **Rationale**: The brief requires direct SQL; Kentico 11 DLLs target .NET Framework and are not
  redistributable.
- **Alternatives**: EF Core scaffolding (heavy for read-only, schema-coupled); Kentico API
  (.NET Framework only).

## R2. Target access: Management API over HTTP with client credentials

- **Decision**: The console tool calls `/umbraco/management/api/v1/...` with a bearer token from
  `/umbraco/management/api/v1/security/back-office/token` (grant `client_credentials`, client id
  of an API user, ids start with `umbraco-back-office-`). Tokens last about 300 s; the client
  caches the token until 30 s before expiry and retries once on 401.
- **Rationale**: Owner decision W1; keeps the tool independent of the Umbraco process.
- **Alternatives**: In-process Umbraco services (needs the full Umbraco runtime inside the tool).

## R3. Stable keys

- **Decision**: Documents use `NodeGUID`; attachment media use `AttachmentGUID`; media library
  files use `FileGUID`; everything else (document types, data types, templates, media folders,
  unlinked rows, folders created by the tool) uses UUID v5 with a fixed namespace GUID and the name
  `"<kind>:<natural key>"` (for example `doctype:brand`, `unlinked:PO.Make:17`,
  `mediafolder:attachments/products`).
- **Rationale**: Same source gives the same keys on every run (FR-009); Umbraco Management API
  create requests accept a client-supplied `id`.
- **Alternatives**: A mapping table stored in the target (extra state to keep in sync).

## R4. Redirects: host endpoint calling `IRedirectUrlService`

- **Decision**: `POST /umbraco/management/api/v1/kentico-migration/redirects`, body
  `[{ "url": "/old", "contentKey": "<guid>" }]`, implemented in the host with Umbraco's
  `IRedirectUrlService.Register(url, contentKey)`, secured like other Management API controllers.
- **Rationale**: Native redirects are visible in "URL Redirect Management" (FR-024); the
  Management API has no create endpoint.
- **Verified** (Umbraco 17.8.1, host `host/UmbracoSite/Migration/RedirectsController.cs`):
  - Route format for a site without domains: the plain path, `"/old-path"`, no root node id prefix
    (`SELECT url FROM umbracoRedirectUrl` shows `/test-old`). `IRedirectUrlService.Register(url, contentKey, culture: null)`
    stores the string as given; the back office list shows it with a trailing slash (`/test-old/`).
  - The lookup uses the request path lower-cased, percent-decoded, and without a trailing slash, so
    a route stored with capitals (`/Mixed-Case-Old`) is never matched (404 for every request casing). The endpoint therefore lower-cases the URL and
    trims a trailing slash before storing. Percent-encoded requests work: `/Old%20Name` matches a
    route stored as `/old name`.
  - `/old.aspx` returns 404 even with a redirect registered, but `/old.aspx/` returns 301: a path
    ending in a file extension never reaches the content finders. The host therefore appends a slash
    to request paths ending in `.aspx` (middleware in `Program.cs`); after that `/old.aspx`,
    `/OLD.ASPX` and `/Fixture-Page.aspx` all answer 301 to the registered target. Without a
    registered redirect `/never.aspx` stays 404.
  - Redirect responses: HTTP 301 with `Location: /fixture-page/` (relative, trailing slash).
  - `GET /redirect-management?skip=0&take=10` returns
    `{ total, items: [ { id, originalUrl: "/old-name/", destinationUrl, created, document: { id }, culture } ] }`;
    `DELETE /redirect-management/{id}` removes one.
  - The endpoint answers 401 without a token, 200 with
    `{ "created": n, "existing": n, "failed": [ { "url", "reason" } ] }`, 400 for more than 500 entries,
    and is idempotent per `(url, contentKey)` (the second identical batch reports everything as `existing`).
- **Alternatives**: Rewrite rules file (not visible to editors); Skybrud Redirects package (extra
  dependency, version support uncertain for 17).

## R5. Upsert, change detection and editor protection

- **Decision**: Every migrated document and media item carries two hidden, read-only properties
  in a `migrationComposition`: `migrationSourceKey` (the source identity, for reports) and
  `migrationFingerprint` (SHA-256 of the canonical JSON of the values the tool wrote: name,
  parent key, URL name, owned property values; rich text after rewriting). For each item:
  1. `GET` by key. On 404: `POST` with that key and the fingerprint. Result `Created`.
  2. Compute the fingerprint of the item's current owned values. If it differs from the stored
     `migrationFingerprint`, an editor changed the item: result `EditedSkipped` (no write), or with
     `--overwrite-edited` a `PUT` and result `EditedOverwritten` (FR-015a).
  3. Otherwise compute the fingerprint of the planned values. Equal to the stored one: result
     `Unchanged`. Different: `PUT` with the new fingerprint, result `Updated`.
  Publishing is requested only after `Created` or `Updated` when the plan says publish. Sorting is
  applied per parent only when the current order differs.
- **Missing in source (FR-015b)**: after syncing, the tool lists every item under the migrated
  roots (document tree from `home`, media tree from the import folder) that has a
  `migrationSourceKey` but whose key is not in the plan, and reports it as "missing in source,
  review manually". The tool has no delete or unpublish code path at all.
- **Preview (FR-011)**: the `plan` command runs steps 1 to 3 without the writes and reports the
  would-be result per item and per type; if Umbraco is not configured or not reachable it reports
  plan-only and says so.
- **Rationale**: SC-002 (second run changes nothing), owner decisions on editor changes and
  deletions, and a useful dry run before re-runs.
- **Alternatives**: Compare against Umbraco's update date (fragile: publishing and sorting also
  change it); always `PUT` (bumps versions every run).

## R5a. Unpublished source pages

- **Decision**: A page is published in the plan only if `DocumentCanBePublished = 1` and the run
  time is inside the window (`DocumentPublishFrom` is null or in the past, `DocumentPublishTo` is
  null or in the future). This is the same rule Kentico 11 uses for its published-document
  condition; archived documents have `DocumentCanBePublished = 0`. Everything else migrates
  unpublished; publish-from and publish-to values are copied into the report only (FR-014a).
- **Checked**: on the reference site all 172 documents have `DocumentCanBePublished = 1`, no
  archived documents and no publish window, so all count as published; the sample adds one
  draft, one archived and one scheduled page to exercise the rule.

## R6. Rich text value shape and link markup (Verified)

- **Decision**: Rich text property values are sent as `{ "markup": "<html>", "blocks": null }`.
  Document links are written as `<a href="/{localLink:<key>}" type="document">`; media links as
  `<a href="/{localLink:<key>}" type="media">`; images as `<img src="<media url>"
  data-udi="umb://media/<key without dashes>">`.
- **Verified** (Umbraco 17.8.1, fixture `tests/KenticoMigrator.Tests/Fixtures/umbraco/document-all-editors.json`,
  property `body`): a page was written through `POST /document` and `PUT /document/{id}` with
  exactly that markup and read back through `GET /document/{id}`.
  - The markup string comes back byte for byte as it was sent: attribute order, `type`, `title`,
    `data-udi` (32 hex digits, no dashes) and the plain `src` are all kept. Nothing is rewritten on save.
  - Value shape on read: `{ "markup": "...", "blocks": { "layout": {}, "contentData": [], "settingsData": [], "expose": [] } }`.
    On write `"blocks": null` is accepted. Fingerprints must therefore compare only `markup`
    (an empty `blocks` object on read is equal to `null` on write).
  - Link tracking works: `GET /document/{target}/referenced-by` and `GET /media/{file}/referenced-by`
    list the page that links to them, so the `{localLink:<key>}` form is understood by Umbraco.
  - The rewriter constants (R6 markup above) are those in the fixture: internal page link
    `href="/{localLink:<document key>}" type="document"`, media link `href="/{localLink:<media key>}"
    type="media"`, image `src="<media url>" data-udi="umb://media/<media key without dashes>"`.
  - Not covered: what the back-office editor itself would emit for an image (it adds
    `?rmode=max&width=...` style query strings to `src` in some versions). The tool does not depend
    on it: only the `data-udi` and the markup it writes matter.

## R7. Property value shapes (Verified)

- **Decision and verified shapes** (read back from `GET /document/{id}` for the fixture page; every
  value is sent and read in the same shape unless noted). Property values travel as
  `{ "alias": "<alias>", "culture": null, "segment": null, "value": <value> }`; on read each entry also
  has `"editorAlias"`.

  | Editor (built-in data type name) | editorAlias | Written value | Read value |
  |---|---|---|---|
  | Textstring | `Umbraco.TextBox` | `"text"` | same |
  | Textarea | `Umbraco.TextArea` | `"line1\nline2"` | same (newlines kept) |
  | Richtext editor | `Umbraco.RichText` | `{ "markup": "<p>..</p>", "blocks": null }` | `{ "markup", "blocks": { "layout": {}, "contentData": [], "settingsData": [], "expose": [] } }` |
  | Numeric | `Umbraco.Integer` | `42` | `42` |
  | Decimal (**no built-in**, created by the tool) | `Umbraco.Decimal` | `12.5` (JSON number) | `12.5`; `7` is read back as `7` |
  | True/false | `Umbraco.TrueFalse` | `true` | `true` |
  | Date Picker with time | `Umbraco.DateTime` | `"2026-03-01 10:30:00"` | `"2026-03-01 10:30:00"` (the ISO form `"2026-03-01T10:30:00"` is also accepted and normalised to the space form) |
  | Tags | `Umbraco.Tags` | `["kayak","touring"]` | same |
  | Dropdown (single) | `Umbraco.DropDown.Flexible` | `["Sea kayak"]` | same |
  | Media Picker / Multiple Media Picker | `Umbraco.MediaPicker3` | `[{ "key": "<new guid>", "mediaKey": "<media key>" }]` | the same plus `"mediaTypeAlias": "Image"` (or `"File"`), `"crops": []`, `"focalPoint": null` |
  | `umbracoUrlName` (a Textstring property on the document type) | `Umbraco.TextBox` | `"fixture-page"` | same, and it controls the URL segment (see R10) |

- Data types are found by name with `GET /filter/data-type?skip=0&take=100&name=<name>` (returns
  `items[] { id, name, editorAlias, editorUiAlias }`; compare `name` exactly in case the filter
  returns several); `GET /item/data-type/search?query=` also exists. The built-in names in a fresh
  17.8.1 install are `Textstring`, `Textarea`, `Richtext editor`, `Numeric`, `True/false`,
  `Date Picker with time`, `Date Picker`, `Tags`, `Dropdown`, `Dropdown multiple`, `Media Picker`,
  `Multiple Media Picker`, `Image Media Picker`, `Upload File`, `Content Picker`, `Multi URL Picker`.
  There is no Decimal data type.
- **Verify in T033**: null values on POST and PUT (a planned empty value is written as `value: null` to clear a
  property). Fallback if Umbraco rejects or ignores them: omit the property on POST and send `""` or `[]` on PUT.
- Tool-created data types: `POST /data-type` with
  `{ id, name, editorAlias, editorUiAlias, values: [...], parent: null }`.
  - Decimal: `editorAlias "Umbraco.Decimal"`, `editorUiAlias "Umb.PropertyEditorUi.Decimal"`, `values: []`.
  - Dropdown: `editorAlias "Umbraco.DropDown.Flexible"`, `editorUiAlias "Umb.PropertyEditorUi.Dropdown"`,
    `values: [ { "alias": "multiple", "value": false }, { "alias": "items", "value": ["A","B"] } ]`.
    The built-in `Dropdown` has no `items`, so each dropdown needs its own data type.
- A media picker value written with only `key` and `mediaKey` is accepted; the server fills in the
  rest on read, so the fingerprint must ignore `mediaTypeAlias`, `crops` and `focalPoint`.
- Document write shape: `POST /document` `{ id, parent: {id}|null, documentType: {id}, template: {id}|null, values: [...], variants: [{ culture: null, segment: null, name }] }`
  (201, `Location` header); `PUT /document/{id}` takes `{ template, values, variants }`; publish is
  `PUT /document/{id}/publish` with `{ "publishSchedules": [ { "culture": null } ] }` (200). The
  read model has `variants[].state` (`Published`, `Draft`, ...), `publishDate`, `updateDate`.

## R8. API user for unattended runs

- **Decision**: In Development, the host seeds one API user with client credentials from
  configuration (`Migration:ApiClient:Id`, `Migration:ApiClient:Secret`) at startup, idempotently.
  Production setups create the API user in the back office (documented in host/README.md).
- **Verified** (17.8.1): `IUserService.CreateAsync(Constants.Security.SuperUserKey, new UserCreateModel { Name, UserName, Email, Kind = UserKind.Api, UserGroupKeys = { Constants.Security.AdminGroupKey } }, approveUser: true)`
  returns `Attempt<UserCreationResult, UserOperationStatus>`; then
  `IBackOfficeUserClientCredentialsManager.SaveAsync(userKey, clientId, clientSecret)` returns
  `Attempt<BackOfficeUserClientCredentialsOperationStatus>`; `FindUserAsync(clientId)` returns the
  user or null (used for idempotency). The client id must start with `umbraco-back-office-`. The
  token endpoint answers `{ access_token, token_type: "Bearer", expires_in: 300 }`. Implemented in
  `host/UmbracoSite/Migration/ApiClientSeeder.cs` (a `UmbracoApplicationStartedNotification` handler).
- **Alternatives**: Manual setup only (slower quickstart).

## R9. Media upload

- **Decision**: Upload file bytes with `POST /temporary-file` (multipart, client-chosen id), then
  create the media item with `umbracoFile` set to `{ "temporaryFileId": "<id>" }` for both
  `Image` and `File` media types. Folders use media type `Folder`.
- **Verified** (fixtures `media-image.json` and `media-file.json`): `POST /temporary-file` is
  multipart with fields `Id` (a client-chosen guid) and `File`, and answers 201. Then
  `POST /media` with
  `{ id, parent: {id}|null, mediaType: {id}, values: [ { alias: "umbracoFile", culture: null, segment: null, value: { "temporaryFileId": "<id>" } } ], variants: [ { culture: null, segment: null, name } ] }`
  answers 201 for both media types. Media type ids are found with
  `GET /item/media-type/search?query=Image` (also `File`, `Folder`; compare the name exactly).
  - `Image` (`editorAlias Umbraco.ImageCropper`): read value `{ "focalPoint": null, "crops": [], "src": "/media/<token>/<file name>" }`;
    the server adds `umbracoWidth`, `umbracoHeight`, `umbracoBytes`, `umbracoExtension` values.
  - `File` (`editorAlias Umbraco.UploadField`): read value `{ "src": "/media/<token>/<file name>" }`;
    the server adds `umbracoExtension` and `umbracoBytes`.
  - The file name in `src` is the uploaded file name; the `<token>` directory is generated by Umbraco.
  - `GET /media/urls?id=<id>` returns `[ { id, urlInfos: [ { culture: null, url: "https://localhost:44317/media/<token>/<file>" } ] } ]`
    (absolute URL), and the same URL serves the file from blob storage (200, `image/png`).
- **Storage**: host uses `Umbraco.StorageProviders.AzureBlob` with `AddAzureBlobMediaFileSystem()`
  and `AddAzureBlobImageSharpCache()`; Development connection string `UseDevelopmentStorage=true`,
  container `media`. Verified against Azurite: blobs land at `media/<token>/<file name>`. The
  provider does not create the container (first upload fails with `ContainerNotFound`, HTTP 500
  from the Management API), so the host creates it on start in Development
  (`host/UmbracoSite/Migration/MediaContainerBootstrapper.cs`). Anonymous container listing
  (`curl .../devstoreaccount1/media?restype=container&comp=list`) is refused by Azurite
  (`AuthorizationFailure`); a shared-key signed list or the blob URL through the host is used instead.

## R10. URLs: keep old paths, read actual URLs back

- **Decision**: Every migrated page gets `umbracoUrlName` = Kentico `NodeAlias`. After pages are
  saved and published, the tool reads each page's actual URL from the Management API document
  URL endpoint and plans redirects from the difference (FR-023). Old URL forms per page: alias
  path, alias path plus `.aspx`, `DocumentUrlPath`, fixed document aliases, and for brands the
  old route `/search-featured/<brand name>`.
- **Verified** (T019, observed status codes): a published page at `/fixture-page` answers 200 for
  `/fixture-page`, `/Fixture-Page`, `/FIXTURE-PAGE` and `/fixture-page/` (no redirect), so Umbraco
  routing is case-insensitive and the planner adds no case-variant redirects. `/fixture-page.aspx`
  answers 404 without a redirect and needs one (see R4 for how `.aspx` reaches the redirect finder).
  The first root page (`fixture-target`) is reported as `/` by `GET /document/urls`, yet
  `/fixture-target` also answers 200. Recorded in MAPPING.md section 3.
- **Verified so far** (T018): `GET /document/urls?id=<id>&id=<id>` returns
  `[ { id, urlInfos: [ { culture: "en-US", url: "/fixture-page/", provider: "umbDocumentUrlProvider", isExternal: false, message: null } ] } ]`.
  The URL is relative, has a trailing slash, and the `umbracoUrlName` property value is used as the
  segment (`fixture-page` for a page named `Fixture page`). With no domains, the first root
  document (lowest sort order) is `"/"`. `culture` is the default language even for an invariant document.
  The data type lookup endpoint is `GET /filter/data-type?name=` (see R7).
- **Normalisation** for comparison: lowercase, percent-decoded, `.aspx` removed, trailing slash
  removed, query and fragment removed, own host removed.

## R11. Sample dataset

- **Decision**: `sample/kentico11-sample.sql` creates database `Kentico11Sample` with only the
  tables and columns the reader queries, filled with an invented kayak shop: home, 4 content pages
  (one with two editable regions, one without text), 1 draft, 1 archived and 1 scheduled page,
  1 excluded application page, 3 articles, 6 brands (2 with a space in the name, 1 brand row
  without a node), 2 products with SKUs and gallery attachments, 1 file node, 1 media library with
  3 files, aliases including an `.aspx` form, a short alias and a wildcard alias, and rich text with
  one link of each kind. Images are tiny generated JPEG/PNG bytes as `0x` literals.
- **Rationale**: Owner decision S1; text format is reviewable in git.
- **Alternatives**: A `.bak` (binary, opaque in review).

## R12. Minimal templates

- **Decision**: The tool creates one layout template and one generic page template from embedded
  Razor views; the page template renders title, main body, additional content, intro text, images
  and product fields when present. Folder and settings types have no template.
- **Rationale**: DoD says "the Umbraco site opens, pages and images are in place"; design is per
  project.
