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
- **Verify**: the stored route format for a site without domains (Umbraco keeps redirect URLs as
  routes, typically `"{rootNodeId}/path"` when a domain root exists, or `"/path"` otherwise) and
  that `/old.aspx` and `/Old%20Name` requests reach `ContentFinderByRedirectUrl` and return 301.
  Task: host verification with `curl -sI`.
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

## R6. Rich text value shape and link markup (**Verify**)

- **Decision**: Rich text property values are sent as `{ "markup": "<html>", "blocks": null }`.
  Document links are written as `<a href="/{localLink:<key>}" type="document">`; media links as
  `<a href="/{localLink:<key>}" type="media">`; images as `<img src="<media url>"
  data-udi="umb://media/<key without dashes>">` or the v17 equivalent.
- **Rationale**: Umbraco 14+ stores local links by key with a `type` attribute and resolves them
  on render.
- **Verify**: create a page in the back office with an internal link, a media link and an image,
  read it through `GET /document/{id}`, and pin the exact markup in a test fixture. The rewriter's
  constants must match that fixture.

## R7. Property value shapes (**Verify**)

- **Decision**: TextBox/TextArea: string. Integer: number. Decimal: number. TrueFalse: boolean.
  DateTime: `"yyyy-MM-dd HH:mm:ss"`. Tags: array of strings. Dropdown: array with one string.
  Media picker: `[{ "key": "<new guid>", "mediaKey": "<media key>" }]`. `umbracoUrlName`: string.
- **Verify**: read each editor's value from a hand-made page through the API and keep one fixture
  per editor.

## R8. API user for unattended runs

- **Decision**: In Development, the host seeds one API user with client credentials from
  configuration (`Migration:ApiClient:Id`, `Migration:ApiClient:Secret`) at startup, idempotently.
  Production setups create the API user in the back office (documented in host/README.md).
- **Verify**: the Umbraco 17 service names for creating an API user and its client credentials
  (user kind `Api`, back-office client credentials manager).
- **Alternatives**: Manual setup only (slower quickstart).

## R9. Media upload

- **Decision**: Upload file bytes with `POST /temporary-file` (multipart, client-chosen id), then
  create the media item with `umbracoFile` set to `{ "temporaryFileId": "<id>" }` for both
  `Image` and `File` media types. Folders use media type `Folder`.
- **Verify**: exact property alias and value shape for `Image` (image cropper) and `File` (upload
  field) in 17.
- **Storage**: host uses `Umbraco.StorageProviders.AzureBlob` with `AddAzureBlobMediaFileSystem()`
  and `AddAzureBlobImageSharpCache()`; Development connection string `UseDevelopmentStorage=true`,
  container `media`.

## R10. URLs: keep old paths, read actual URLs back

- **Decision**: Every migrated page gets `umbracoUrlName` = Kentico `NodeAlias`. After pages are
  saved and published, the tool reads each page's actual URL from the Management API document
  URL endpoint and plans redirects from the difference (FR-023). Old URL forms per page: alias
  path, alias path plus `.aspx`, `DocumentUrlPath`, fixed document aliases, and for brands the
  old route `/search-featured/<brand name>`.
- **Verify**: whether Umbraco 17 routes `/AboutUs` (mixed case) to `/aboutus`. If it does not, the
  planner adds case-variant redirects; this is recorded in MAPPING.md section 3.
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
