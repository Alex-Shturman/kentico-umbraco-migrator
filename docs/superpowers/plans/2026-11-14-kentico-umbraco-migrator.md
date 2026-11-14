# Kentico to Umbraco Content Migrator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One console command migrates the in-scope content of a Kentico 11 site into a clean Umbraco 17 site, keeps old URLs working, and writes a verification report.

**Architecture:** A .NET 10 console tool reads Kentico with direct SQL behind `IKenticoReader`, builds an in-memory `MigrationPlan` (pure, unit-tested), then applies it to Umbraco through the Management API using Kentico GUIDs as item keys, so runs are idempotent. A small Umbraco 17 host in the repo provides the target site, SQL Server storage, Azure Blob media (Azurite locally) and one endpoint for registering redirects.

**Tech Stack:** .NET 10, C#, Umbraco 17 LTS (`Umbraco.Templates` 17.x), Microsoft.Data.SqlClient + Dapper, AngleSharp, Umbraco.StorageProviders.AzureBlob, xUnit v3, Docker (SQL Server 2022, Azurite).

**Spec:** The project brief (`_work/BRIEF.md`, git-ignored because it names the client), [docs/DISCOVERY.md](../../DISCOVERY.md), [docs/MAPPING.md](../../MAPPING.md). MAPPING.md is the source of truth for type, field, tree and URL rules; this plan does not repeat them.

## Global Constraints

- C# / .NET 10 (`net10.0`), console application, Umbraco 17 LTS target.
- Read Kentico with direct SQL. Write to Umbraco only through the Management API. Never write to the Umbraco database.
- Kentico-version-specific SQL lives only in `Kentico11SqlReader` behind `IKenticoReader`. No other abstraction layers.
- Idempotent: a second run creates no duplicates (same counts before and after).
- Configuration through `appsettings.json` plus command-line overrides. No connection strings or secrets in the repo (user-secrets or environment variables).
- Development machine: Apple Silicon MacBook, SQL Server and Azurite in Docker.
- Before each run snapshot counts; after the run compare; if a target count drops, stop with a non-zero exit code and report.
- `Sources/`, `sources/`, `_work/`, `runs/` are git-ignored. No client data, code, names of listings, users or brands in the repo, tests or commit messages. Only aggregate numbers.
- No em dashes in any text written (code comments, docs, commit messages, CLI output).
- Nullable enabled, warnings as errors.

## Review Focus

1. **Second run on the same target**: no duplicates, unchanged items not rewritten, counts identical. Pinned in Task 8 (`Second_run_creates_nothing`).
2. **Old brand URLs with spaces and capital letters** (`/search-featured/Sea Spirit`, `%20`, mixed case): resolve to the brand page in links and in redirects. Pinned in Task 4 and Task 5.
3. **Access token expiry during a long run** (tokens live about 300 s): the client refreshes and retries once on 401. Pinned in Task 7 (`Refreshes_token_on_401`).
4. **One item fails mid-run** (API returns 400 for one document): the run records the error with the Kentico path, continues with the rest, and exits with code 1. Pinned in Task 8 (`Failed_item_is_reported_and_run_continues`).
5. **Two siblings with the same URL name** (would make Umbraco append "(1)"): detected in the plan and reported, not silently renamed. Pinned in Task 3 (`Duplicate_sibling_segment_is_reported`).

---

## File Structure

```
.gitignore, README.md, Directory.Build.props, KenticoMigrator.slnx, docker-compose.yml
src/KenticoMigrator/                    console tool
  Program.cs                            verbs: counts | plan | migrate
  appsettings.json                      placeholders only
  Kentico/IKenticoReader.cs             interface + Source* records
  Kentico/Kentico11SqlReader.cs         all Kentico 11 SQL
  Kentico/FormDefinition.cs             parses ClassFormDefinition XML
  Kentico/EditableRegions.cs            parses DocumentContent XML
  Migration/MigrationOptions.cs         profile settings (type map, excludes)
  Migration/MigrationPlan.cs            Target* records
  Migration/PlanBuilder.cs              source -> plan (pure)
  Migration/UrlIndex.cs                 old URL -> target key lookup
  Migration/RichTextRewriter.cs         AngleSharp link rewriting
  Migration/RedirectPlanner.cs          old vs new URLs -> redirects
  Umbraco/ManagementApiClient.cs        token, HTTP, retries
  Umbraco/SchemaSync.cs                 data types, document types, templates
  Umbraco/ContentSync.cs                media + documents upsert, publish, sort
  Umbraco/Templates/*.cshtml            embedded minimal views
  Run/Counts.cs                         snapshot + CountGuard
  Run/MigrationRunner.cs                orchestration
  Run/Report.cs                         report.json, report.md, redirects.csv
profiles/pontoonsonly.json              site profile (section names only)
profiles/sample.json                    profile for the sample dataset
host/UmbracoSite/                       Umbraco 17 site
  Program.cs                            Azure Blob media, unattended install
  Migration/RedirectsController.cs      POST redirects endpoint
  Migration/ApiClientSeeder.cs          creates API user from config (Development)
sample/kentico11-sample.sql             fictional Kentico 11 database
sample/media/                           fictional media library files
scripts/restore-sample.sh, scripts/check-urls.sh
tests/KenticoMigrator.Tests/            unit + gated integration tests
```

---

### Task 0: Repository bootstrap

**Files:**
- Create: `.gitignore` (already exists, extend with `runs/`, `appsettings.*.local.json`), `Directory.Build.props`, `KenticoMigrator.slnx`, `src/KenticoMigrator/KenticoMigrator.csproj`, `tests/KenticoMigrator.Tests/KenticoMigrator.Tests.csproj`, `docker-compose.yml`, `README.md` (stub with the brief's Goal and Scope, rewritten in own words)

**Interfaces:**
- Produces: buildable empty solution; `docker compose up azurite` exposes blob on 10000.

- [ ] **Step 1:** `git init`, verify `git check-ignore Sources/po.zip sources/x _work/x` prints all three paths before anything is added.
- [ ] **Step 2:** Create solution and projects (`dotnet new console`, `dotnet new xunit3`), `Directory.Build.props` with `TargetFramework net10.0`, `Nullable enable`, `TreatWarningsAsErrors true`, `ImplicitUsings enable`.
- [ ] **Step 3:** `docker-compose.yml` with `azurite` (`mcr.microsoft.com/azure-storage/azurite`, blob port 10000) and an optional `sqlserver` service under profile `sql` for people without their own container.
- [ ] **Step 4:** Verify: `dotnet build` succeeds with 0 warnings; `docker compose up -d azurite && curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:10000/devstoreaccount1?comp=list` returns `403` or `200` (service up).
- [ ] **Step 5:** Commit `chore: bootstrap solution`.

### Task 1: Fictional sample Kentico 11 database

**Files:**
- Create: `sample/kentico11-sample.sql`, `sample/media/<library>/...` (3 to 5 small generated images), `scripts/restore-sample.sh`

**Interfaces:**
- Produces: database `Kentico11Sample` with the subset of the Kentico 11 schema that `Kentico11SqlReader` uses: `CMS_Site`, `CMS_SettingsKey`, `CMS_Class` (with real `ClassFormDefinition` XML), `CMS_Tree`, `CMS_Document`, `CMS_DocumentAlias`, `CMS_Attachment`, `CMS_PageTemplate`, `Media_Library`, `Media_File`, `COM_SKU`, coupled tables `CONTENT_MenuItem`, `CONTENT_Article`, `CONTENT_File`, and two custom ones `DEMO_Brand`, `DEMO_Product`.

Fictional content (an invented kayak shop, nothing from `Sources/`): 1 home, 4 content pages (one with two editable regions), 3 articles, 6 brands (2 with a space in the name, 1 brand row without a node), 2 products with SKUs and gallery attachments, 1 file node, 1 media library with 3 files, aliases including one `.aspx`-style old URL, one short alias and one wildcard alias. Rich text contains: a site-relative link, an absolute own-domain link, a `getattachment` image, a `getmedia` link, an external link, a link to an unknown page. `CMSDBVersion` = `11.0`.

- [ ] **Step 1:** Write the script. Images as `0x...` literals generated by a one-off script kept out of the repo.
- [ ] **Step 2:** `scripts/restore-sample.sh` runs the script through `sqlcmd` in the container named by `$SQL_CONTAINER`, using the container's own SA env var (never printed).
- [ ] **Step 3:** Verify: `scripts/restore-sample.sh && <sqlcmd> "SELECT COUNT(*) FROM CMS_Tree"` prints the documented count (write the expected counts table into `sample/README.md` and compare).
- [ ] **Step 4:** Commit `feat(sample): fictional Kentico 11 dataset`.

### Task 2: Kentico reader

**Files:**
- Create: `src/KenticoMigrator/Kentico/IKenticoReader.cs`, `Kentico11SqlReader.cs`, `FormDefinition.cs`, `EditableRegions.cs`
- Test: `tests/KenticoMigrator.Tests/Kentico/FormDefinitionTests.cs`, `EditableRegionsTests.cs`, `Kentico11SqlReaderTests.cs` (integration, `[Trait("Category","Integration")]`, skipped when `KENTICO_SAMPLE_CS` is unset)

**Interfaces:**
- Produces:
```csharp
public interface IKenticoReader
{
    Task<SourceSite> ReadAsync(CancellationToken ct);
    Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct);
    Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct); // reads from MediaRoot on disk
    Task<SourceCounts> CountAsync(CancellationToken ct);
}
public sealed record SourceSite(string SiteName, string KenticoVersion, IReadOnlyList<SourcePageType> PageTypes,
    IReadOnlyList<SourceDocument> Documents, IReadOnlyList<SourceAttachment> Attachments, IReadOnlyList<SourceAlias> Aliases,
    IReadOnlyList<SourceMediaFile> MediaFiles, IReadOnlyDictionary<string, IReadOnlyList<SourceRow>> UnlinkedRows);
public sealed record SourcePageType(string ClassName, string TableName, bool IsCoupled, IReadOnlyList<SourceField> Fields);
public sealed record SourceField(string Column, string DataType, int? Size, string? Control, IReadOnlyList<string> Options, Guid FieldGuid);
public sealed record SourceDocument(Guid NodeGuid, int NodeId, int? ParentNodeId, string ClassName, string Name, string NodeAlias,
    string AliasPath, int Order, bool Published, bool HideInNavigation, bool ShowInSitemap, string? PageTitle,
    string? PageDescription, string? PageKeywords, DateTime Created, DateTime Modified, string? UrlPath, string? Extension,
    IReadOnlyDictionary<string, object?> Fields, IReadOnlyList<EditableRegion> Regions, IReadOnlyDictionary<string, object?>? Sku);
public sealed record EditableRegion(string Id, string Html);   // in template order
public sealed record SourceAttachment(Guid AttachmentGuid, int NodeId, string FileName, string Extension, string MimeType,
    long Size, int? Width, int? Height, string? Title, string? FieldColumn, bool Unsorted, int Order);
public sealed record SourceAlias(int NodeId, string UrlPath, bool IsWildcard);
public sealed record SourceMediaFile(Guid FileGuid, string LibraryName, string LibraryFolder, string FilePath, string FileName,
    string Extension, string MimeType, long Size, string? Title);
public sealed record SourceRow(int Id, IReadOnlyDictionary<string, object?> Values);
public sealed record SourceCounts(IReadOnlyDictionary<string, int> DocumentsByClass, int Attachments, int MediaFiles, int Aliases);
```
- Consumes: `KenticoOptions { string ConnectionString; string? MediaRoot; string[] UnlinkedRowClasses }`.

- [ ] **Step 1: Failing unit tests.** `FormDefinition.Parse(xml)` returns fields with column, type, control and options (radio/dropdown `options` split on newline and `;`). `EditableRegions.Parse(documentContent, templateRegionOrder)` decodes the XML-encoded HTML (entities decoded exactly once), returns regions in the template order, and returns an empty list for null or empty content.
- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~Kentico"`; expect compile failure.
- [ ] **Step 3:** Implement the parsers with `System.Xml.Linq`. Implement `Kentico11SqlReader` with Dapper; detect version from `CMS_SettingsKey.CMSDBVersion` and throw `NotSupportedException` with the found version when it is not `11.x`. Region order comes from the order of `editabletext` web parts in the document's page template (`PageTemplateWebParts`); unknown regions are appended. File field values (a GUID) are matched to `CMS_Attachment.AttachmentGUID`; attachment group fields match `AttachmentGroupGUID` to the field GUID; history rows are ignored.
- [ ] **Step 4:** Integration test against the sample DB: counts per class equal the `sample/README.md` table; the two-region page returns 2 regions in template order; `UnlinkedRows["DEMO.Brand"]` has 1 row.
- [ ] **Step 5:** Run unit and integration tests; expect PASS. Commit `feat(reader): Kentico 11 SQL reader`.

### Task 3: Plan builder

**Files:**
- Create: `src/KenticoMigrator/Migration/MigrationOptions.cs`, `MigrationPlan.cs`, `PlanBuilder.cs`, `profiles/sample.json`, `profiles/pontoonsonly.json`
- Test: `tests/KenticoMigrator.Tests/Migration/PlanBuilderTests.cs`

**Interfaces:**
- Consumes: `SourceSite` (Task 2).
- Produces:
```csharp
public sealed record MigrationPlan(IReadOnlyList<TargetDocumentType> DocumentTypes, IReadOnlyList<TargetMedia> Media,
    IReadOnlyList<TargetDocument> Documents, IReadOnlyList<SkippedItem> Skipped, IReadOnlyList<PlanIssue> Issues);
public sealed record TargetDocumentType(Guid Key, string Alias, string Name, bool HasTemplate, bool AllowedAtRoot,
    IReadOnlyList<string> AllowedChildAliases, IReadOnlyList<TargetProperty> Properties);
public sealed record TargetProperty(string Alias, string Name, string Editor, string Group); // Editor: TextBox|TextArea|RichText|Integer|Decimal|TrueFalse|DateTime|MediaSingle|MediaMultiple|Tags|DropDown:<opt1>|<opt2>
public sealed record TargetDocument(Guid Key, Guid? ParentKey, string TypeAlias, string Name, string UrlName, int SortOrder,
    bool Publish, IReadOnlyDictionary<string, object?> Values, string OldPath, IReadOnlyList<string> OldUrls);
public sealed record TargetMedia(Guid Key, Guid? ParentKey, string MediaType, string Name, MediaSource? Source); // MediaType: Folder|Image|File
public sealed record MediaSource(Guid? AttachmentGuid, SourceMediaFile? MediaFile, string FileName);
public sealed record SkippedItem(string Category, string Identifier, string OldPath, string Reason);
public sealed record PlanIssue(string Severity, string OldPath, string Message);
public static class PlanBuilder { public static MigrationPlan Build(SourceSite site, MigrationOptions options); }
```
`MigrationOptions` holds: class map (Kentico class to target alias and field map), `ExcludePathPrefixes`, `FolderPaths`, `ReparentRules` (for example brand parent to the `brandList` node), `NotUploadedPaths` (Q6), `UnpublishedFromUnlinkedRows` (Q5). The profile files carry only section-level paths (MAPPING decision Q9).

Rich text values stay raw in the plan; Task 4 rewrites them after keys are known. Keys: documents use `NodeGuid`, media use `AttachmentGuid` / `FileGuid`, types, folders and rows without a GUID use a UUID v5 from `"kentico-migrator:" + kind + ":" + natural key`.

- [ ] **Step 1: Failing tests** (sample-shaped in-memory `SourceSite`, no database):
  - `Excluded_prefix_is_skipped_with_reason` (category `application page`).
  - `Folder_becomes_folder_without_template_and_children_keep_path`.
  - `Brand_is_reparented_under_brandList_and_UrlName_is_node_alias`.
  - `First_region_goes_to_bodyText_rest_to_additionalContent` (joined in order).
  - `Unlinked_brand_row_becomes_unpublished_document` with a v5 key that is the same across two `Build` calls.
  - `Product_gets_sku_price_and_gallery_media_in_attachment_order`.
  - `Not_uploaded_file_node_is_skipped_with_category_host_file`.
  - `Duplicate_sibling_segment_is_reported` (Review Focus 5): two siblings whose `UrlName` normalises to the same value produce one `PlanIssue` with severity `error`.
  - `Build_is_deterministic` (two builds produce equal plans).
- [ ] **Step 2:** Run, expect FAIL. **Step 3:** Implement. **Step 4:** Run, expect PASS.
- [ ] **Step 5:** Commit `feat(plan): build migration plan from Kentico model`.

### Task 4: URL index and rich text rewriter

**Files:**
- Create: `src/KenticoMigrator/Migration/UrlIndex.cs`, `RichTextRewriter.cs`
- Test: `tests/KenticoMigrator.Tests/Migration/RichTextRewriterTests.cs`, `UrlIndexTests.cs`

**Interfaces:**
- Consumes: `MigrationPlan` (Task 3), `SourceAlias` (Task 2).
- Produces:
```csharp
public enum LinkKind { Document, Media, Application, External, Unknown }
public sealed class UrlIndex
{
    public static UrlIndex Create(MigrationPlan plan, IReadOnlyList<SourceAlias> aliases, IReadOnlyList<string> ownHosts, IReadOnlyList<string> applicationPrefixes);
    public (LinkKind Kind, Guid? Key) Resolve(string href);
    public static string Normalize(string url); // lowercase, percent-decoded, no .aspx, no trailing slash, no query/fragment, own host removed
}
public sealed record RewriteResult(string Html, int Rewritten, int External, int ToApplication, IReadOnlyList<string> Unresolved);
public sealed class RichTextRewriter
{
    public RichTextRewriter(UrlIndex index, Func<Guid, string> mediaUrl);
    public RewriteResult Rewrite(string html);
}
```

- [ ] **Step 1: Capture the target formats.** With the host from Task 6 not yet available, take the formats from the Umbraco 17 docs (context7, `/websites/umbraco`, query "rich text editor local link format"): document link `href="/{localLink:<key>}"` with `type="document"`, media link and image markup. Write them as constants in `RichTextRewriter` with a comment linking the doc page. Task 7 Step 5 re-checks them against a real saved value.
- [ ] **Step 2: Failing tests:**
  - `Normalize("/Search-Featured/Sea%20Spirit.aspx?x=1")` equals `/search-featured/sea spirit` (Review Focus 2).
  - Site-relative and absolute own-domain links to a migrated page become a local link with that page's key.
  - `/{alias}` from `CMS_DocumentAlias` resolves to its node.
  - `getattachment/{guid}/name.jpg` image and `getmedia/{guid}/x` link resolve to the media key with that GUID.
  - External links are unchanged and counted; `mailto:` and `#anchor` are unchanged and not counted as unresolved.
  - An application prefix (`/search`) is unchanged and counted in `ToApplication`.
  - An unknown internal link is unchanged and listed in `Unresolved`.
  - Rewriting already rewritten HTML is a no-op (idempotency).
  - The rest of the markup is byte-identical outside rewritten attributes (compare with a fixture).
- [ ] **Step 3:** Run, expect FAIL. **Step 4:** Implement with AngleSharp (`HtmlParser.ParseFragment`, then serialise the fragment children). **Step 5:** Run, expect PASS.
- [ ] **Step 6:** Commit `feat(links): rewrite internal and media links in rich text`.

### Task 5: Redirect planner

**Files:**
- Create: `src/KenticoMigrator/Migration/RedirectPlanner.cs`
- Test: `tests/KenticoMigrator.Tests/Migration/RedirectPlannerTests.cs`

**Interfaces:**
- Consumes: `TargetDocument.OldUrls` (Task 3), `UrlIndex.Normalize` (Task 4).
- Produces:
```csharp
public sealed record PlannedRedirect(string OldUrl, Guid TargetKey, string NewUrl, string Reason); // Reason: aspx|alias|home|renamed
public static class RedirectPlanner
{
    public static IReadOnlyList<PlannedRedirect> Plan(IReadOnlyList<TargetDocument> documents, IReadOnlyDictionary<Guid, string> actualUrls);
}
```
`actualUrls` is read back from Umbraco after documents exist (Task 8), so the planner never guesses Umbraco's segment rules.

- [ ] **Step 1: Failing tests:**
  - Old path equal to the new URL after `Normalize` gives no redirect (`/AboutUs` vs `/aboutus`).
  - `/AboutUs.aspx` gives a redirect with reason `aspx`.
  - `/Home` and `/Home.aspx` give redirects to `/` with reason `home`.
  - `/search-featured/Sea Spirit` vs `/search-featured/sea-spirit` gives a redirect with reason `renamed` (Review Focus 2).
  - A document without an actual URL (unpublished) gives no redirect.
  - Duplicate old URLs across documents produce one redirect (first by sort order) and the planner returns them deduplicated.
- [ ] **Step 2:** Run, expect FAIL. **Step 3:** Implement. **Step 4:** Run, expect PASS. **Step 5:** Commit `feat(redirects): plan redirects from old URLs`.

### Task 6: Umbraco 17 host

**Files:**
- Create: `host/UmbracoSite/` from `dotnet new umbraco` (Umbraco.Templates 17.x), `host/UmbracoSite/Migration/RedirectsController.cs`, `host/UmbracoSite/Migration/ApiClientSeeder.cs`, `host/README.md`
- Modify: `host/UmbracoSite/Program.cs`, `host/UmbracoSite/appsettings.json` (no secrets), `KenticoMigrator.slnx`

**Interfaces:**
- Produces:
  - Site on `https://localhost:44317`, database `PO_Umbraco` (connection string from user-secrets key `ConnectionStrings:umbracoDbDSN`).
  - Media in Azure Blob: `.AddAzureBlobMediaFileSystem()` and `.AddAzureBlobImageSharpCache()`, settings `Umbraco:Storage:AzureBlob:Media:ConnectionString` (`UseDevelopmentStorage=true` in `appsettings.Development.json`) and `ContainerName` `media`.
  - Unattended install from user-secrets (`Umbraco:CMS:Unattended:*`).
  - API user with client credentials from config `Migration:ApiClient:Id` / `Secret`, created at startup in Development only by `ApiClientSeeder` (an `INotificationAsyncHandler<UmbracoApplicationStartedNotification>`).
  - `POST /umbraco/management/api/v1/kentico-migration/redirects`, body `[{ "url": "/old", "contentKey": "<guid>" }]`, authorised like other Management API controllers, idempotent (skips a url already registered for that key), returns `{ "created": n, "existing": m }`.

- [ ] **Step 1:** `dotnet new install Umbraco.Templates::17.*` then `dotnet new umbraco -n UmbracoSite -o host/UmbracoSite`. Verify the package version in the csproj is 17.x (LTS).
- [ ] **Step 2:** Add Azure Blob provider and config. Create `PO_Umbraco` in the SQL container. Set user-secrets.
- [ ] **Step 3:** Implement `ApiClientSeeder` and `RedirectsController`. Check the exact service names in the Umbraco 17 docs before coding (API users: `IUserService` with `UserKind.Api`, client credentials manager; redirects: `IRedirectUrlService.Register(string url, Guid contentKey, string? culture)` and the stored route format for sites without domains).
- [ ] **Step 4: Verify:**
  - `dotnet run --project host/UmbracoSite` starts and `/umbraco` shows the login page.
  - `curl -s -d grant_type=client_credentials -d client_id=$ID -d client_secret=$SECRET https://localhost:44317/umbraco/management/api/v1/security/back-office/token` returns an `access_token`.
  - Upload one image in the backoffice; `curl http://127.0.0.1:10000/devstoreaccount1/media?restype=container&comp=list` (Azurite) lists it.
  - Create a test page, call the redirect endpoint for `/test-old`, then `curl -sI https://localhost:44317/test-old` returns `301` to the page. Delete the test page afterwards.
- [ ] **Step 5:** Commit `feat(host): Umbraco 17 site with blob media and redirect endpoint`.

### Task 7: Management API client and sync

**Files:**
- Create: `src/KenticoMigrator/Umbraco/ManagementApiClient.cs`, `SchemaSync.cs`, `ContentSync.cs`, `Umbraco/Templates/Master.cshtml`, `Umbraco/Templates/MigratedPage.cshtml` (embedded resources)
- Test: `tests/KenticoMigrator.Tests/Umbraco/ManagementApiClientTests.cs` (fake `HttpMessageHandler`), `tests/KenticoMigrator.Tests/Umbraco/SyncIntegrationTests.cs` (gated by `UMBRACO_URL`)

**Interfaces:**
- Consumes: `MigrationPlan` (Task 3), `RichTextRewriter` (Task 4), `IKenticoReader.Open*Async` (Task 2), host endpoints (Task 6).
- Produces:
```csharp
public sealed class ManagementApiClient   // UmbracoOptions { Uri BaseUrl; string ClientId; string ClientSecret }
{
    public Task<T?> GetAsync<T>(string path, CancellationToken ct);           // null on 404
    public Task SendAsync(HttpMethod method, string path, object? body, CancellationToken ct);
    public Task<Guid> UploadTemporaryFileAsync(Stream content, string fileName, CancellationToken ct);
}
public sealed class SchemaSync { public Task<SyncStats> EnsureAsync(MigrationPlan plan, CancellationToken ct); }
public sealed class ContentSync
{
    public Task<SyncStats> SyncMediaAsync(IReadOnlyList<TargetMedia> media, CancellationToken ct);
    public Task<SyncStats> SyncDocumentsAsync(IReadOnlyList<TargetDocument> docs, RichTextRewriter rewriter, CancellationToken ct);
    public Task<IReadOnlyDictionary<Guid, string>> ReadUrlsAsync(IEnumerable<Guid> keys, CancellationToken ct);
    public Task<SyncStats> RegisterRedirectsAsync(IReadOnlyList<PlannedRedirect> redirects, CancellationToken ct);
}
public sealed record SyncStats(int Created, int Updated, int Unchanged, IReadOnlyList<ItemError> Errors);
public sealed record ItemError(string OldPath, string Message);
```
Upsert rule for every item: `GET` by key; 404 means `POST` with `id = key`; otherwise compare the values the tool owns and `PUT` only when they differ (counts as `Updated`), else `Unchanged`. Documents are published after save when `Publish` is true, then siblings are sorted by `SortOrder`. Built-in data types are found by name (Textstring, Textarea, Richtext editor, Numeric, True/false, Date Picker with time, Tags, Image Media Picker, Multiple Image Media Picker); the tool creates only Decimal and each dropdown, with v5 keys. Templates are created from the embedded views and assigned to types with `HasTemplate`.

- [ ] **Step 1: Failing unit tests** with a fake handler:
  - `Refreshes_token_on_401` (Review Focus 3): first call returns 401, client requests a new token and retries once; a second 401 throws.
  - `Reuses_token_until_expiry` (one token request for 10 calls within `expires_in`).
  - `Get_returns_null_on_404`.
  - `Upsert_posts_when_missing_puts_when_changed_skips_when_equal` for `ContentSync` (fake client).
- [ ] **Step 2:** Run, expect FAIL. **Step 3:** Implement. Request and response shapes come from the host's Swagger at `/umbraco/swagger` (Management API); keep only the fields used.
- [ ] **Step 4:** Run unit tests, expect PASS.
- [ ] **Step 5: Format check against the real host.** In the backoffice, create a page with a rich text link to another page and an image; read it through the API; assert in `SyncIntegrationTests.Rich_text_formats_match_umbraco` that the constants from Task 4 Step 1 produce the same markup. Fix the constants if not.
- [ ] **Step 6:** Integration test: sync the sample plan twice; the second `SyncStats` has `Created == 0` and `Updated == 0`.
- [ ] **Step 7:** Commit `feat(umbraco): Management API client, schema and content sync`.

### Task 8: Runner, count guard and report

**Files:**
- Create: `src/KenticoMigrator/Program.cs`, `Run/Counts.cs`, `Run/MigrationRunner.cs`, `Run/Report.cs`, `src/KenticoMigrator/appsettings.json`
- Test: `tests/KenticoMigrator.Tests/Run/CountGuardTests.cs`, `ReportTests.cs`, `MigrationRunnerTests.cs` (fakes), `EndToEndTests.cs` (gated by `KENTICO_SAMPLE_CS` and `UMBRACO_URL`)

**Interfaces:**
- Consumes: everything above.
- Produces:
  - CLI: `dotnet run --project src/KenticoMigrator -- <counts|plan|migrate> --profile profiles/<name>.json [--Kentico:ConnectionString=...] [--Umbraco:BaseUrl=...]`. Exit codes: `0` success, `1` item errors, `2` configuration or connection error, `3` count drop.
  - `public sealed record TargetCounts(IReadOnlyDictionary<string,int> DocumentsByType, int Media, int Redirects);`
  - `public static IReadOnlyList<string> CountGuard.FindDrops(TargetCounts before, TargetCounts after);`
  - Run folder `runs/<yyyyMMdd-HHmmss>/` with `report.json`, `report.md`, `redirects.csv`, `skipped.csv`.

Run order: read source and `SourceCounts` → build plan (stop with exit 2 on plan errors) → snapshot `TargetCounts` → schema → media → documents → read URLs → redirects → snapshot again → `FindDrops` (any drop: exit 3) → read `SourceCounts` again (any change: warning in report, source should be read-only) → report. `plan` runs the first two steps and writes the report without calling Umbraco.

Report content: source vs target count per type, skipped items by category with reason, plan issues, link stats (rewritten, external, to application, unresolved with old path), redirects by reason, per-phase timings, total time. No content values, only paths, keys and counts.

- [ ] **Step 1: Failing tests:**
  - `CountGuard` reports `brand: 90 -> 88`, and nothing when counts grow or stay equal.
  - `Failed_item_is_reported_and_run_continues` (Review Focus 4): fake sync returns one `ItemError`; the runner finishes all phases, the report lists the error with its old path, exit code `1`.
  - `Report_md_has_no_em_dash` and lists every skipped category.
  - `EndToEnd: Second_run_creates_nothing` (Review Focus 1): run `migrate` twice on the sample; second report shows `Created 0, Updated 0` for all phases and equal target counts.
- [ ] **Step 2:** Run, expect FAIL. **Step 3:** Implement. **Step 4:** Run all tests, expect PASS.
- [ ] **Step 5: Verify by hand on the sample:** `dotnet run --project src/KenticoMigrator -- migrate --profile profiles/sample.json`; open the site: pages, images (served from Azurite), old URLs from `sample/README.md` redirect (`scripts/check-urls.sh sample/expected-urls.txt` prints `OK` for each line).
- [ ] **Step 6:** Commit `feat(run): migrate command with count guard and report`.

### Task 9: Real run on the client database (local only)

**Files:**
- Modify: `profiles/pontoonsonly.json` (exclude prefixes, folder paths, reparent rule for brands, not-uploaded files)
- Create (git-ignored): `_work/expected-urls.txt` generated from the source aliases

- [ ] **Step 1:** `migrate --profile profiles/pontoonsonly.json --Kentico:ConnectionString=<from user-secrets>` against `PO_Kentico` and a fresh `PO_Umbraco`.
- [ ] **Step 2: Verify:** report counts match DISCOVERY.md (88 published + 10 unpublished brands, 6 articles, 3 products, 13 lookup items, 22 media, 90 brand links rewritten); `scripts/check-urls.sh _work/expected-urls.txt` all `OK`; the case-insensitivity question from MAPPING section 3 answered and written into MAPPING.md.
- [ ] **Step 3:** Run again; second report shows nothing created or updated.
- [ ] **Step 4:** Copy only aggregate numbers and timings into `docs/RESULTS.md`. Commit `docs: results of the reference run`.

### Task 10: README and finish

**Files:**
- Modify: `README.md`; Create: `docs/RESULTS.md` (if not done), `LICENSE` (MIT, confirm with owner)

- [ ] **Step 1:** README in English: the problem, what the tool does, architecture in five lines, before/after table with anonymised numbers from Task 9, how to run (sample in under 10 minutes: compose, restore sample, run host, run tool), configuration, limitations (Kentico 11 only, no media libraries on the reference site, listing photos not provided, thin rich text, wildcard routes), "Done per project" list (Web Forms templates, web parts and layouts; listings, search, user accounts, dealers, e-commerce; forms, workflows, permissions), and the two scope extensions (lookups, SKU fields).
- [ ] **Step 2:** Verify: `grep -rn $'\u2014' --include=*.md --include=*.cs . | grep -v _work` prints nothing; `git ls-files | grep -iE 'sources/|_work/|\.bak$'` prints nothing; a fresh clone plus the README steps works on the sample.
- [ ] **Step 3:** Commit `docs: README`.
