---
description: "Task list for the Kentico to Umbraco content migrator"
---

# Tasks: Kentico to Umbraco Content Migrator

**Input**: Design documents from `specs/001-kentico-umbraco-migrator/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution IV makes the pure core test-first: every "Test" task is written
and run (and fails) before the implementation task that follows it. Integration tests are tagged
`[Trait("Category","Integration")]` and skip when their environment variables are missing.

**Organization**: Tasks are grouped by user story. Every task ends with its **Check** (a command
the owner can run) and a commit with a Conventional Commit message (Constitution VII). No task may
leave the solution unbuildable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: User story from spec.md (US1 to US6)
- Common commands: `dotnet build` (0 warnings), `dotnet test --filter "Category!=Integration"`
  (unit), `dotnet test --filter "Category=Integration"` (needs `KENTICO_SAMPLE_CS` and/or
  `UMBRACO_URL`, `UMBRACO_CLIENT_ID`, `UMBRACO_CLIENT_SECRET`).

## Path Conventions

Tool `src/KenticoMigrator/`, tests `tests/KenticoMigrator.Tests/`, host `host/UmbracoSite/`,
sample `sample/`, scripts `scripts/`, profiles `profiles/` (see plan.md Project Structure).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Buildable empty solution, local services, repository guards.

- [x] T001 Create `KenticoMigrator.slnx`, `Directory.Build.props` (`net10.0`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`, `LangVersion latest`), console project `src/KenticoMigrator/KenticoMigrator.csproj` and xUnit v3 project `tests/KenticoMigrator.Tests/KenticoMigrator.Tests.csproj` referencing it. **Check**: `dotnet build` succeeds with 0 warnings; `dotnet test` runs 0 tests.
- [x] T002 [P] Add package references: Microsoft.Data.SqlClient, Dapper, AngleSharp, Microsoft.Extensions.Configuration.Json/EnvironmentVariables/CommandLine/UserSecrets, Microsoft.Extensions.Logging.Console to `src/KenticoMigrator/KenticoMigrator.csproj`; add `UserSecretsId`; add `src/KenticoMigrator/appsettings.json` with the keys from [contracts/cli.md](contracts/cli.md) set to empty strings. **Check**: `dotnet build`; `git grep -n "Password=" -- src` prints nothing.
- [x] T003 [P] Create `docker-compose.yml` with service `azurite` (`mcr.microsoft.com/azure-storage/azurite`, blob port 10000, `--blobHost 0.0.0.0 --skipApiVersionCheck`) and service `sqlserver` (`mcr.microsoft.com/mssql/server:2022-latest`, container name `kentico-migrator-sql`, port 14333, `MSSQL_SA_PASSWORD` from env, profile `sql`). **Check**: `docker compose up -d azurite && curl -s -o /dev/null -w '%{http_code}\n' 'http://127.0.0.1:10000/devstoreaccount1?comp=list'` prints `403` or `200`.
- [x] T004 [P] Create `scripts/check-repo.sh`: fails if `git ls-files` contains paths under `Sources/`, `sources/`, `_work/`, `runs/`, or files ending in `.bak`/`.mdf`; fails if any tracked text file outside the vendored Spec Kit files (`.specify/`, `.claude/skills/speckit-*`) contains U+2014; prints `OK: no client material, no em dashes`. Extend `.gitignore` with `*.bak`, `*.mdf`, `*.ldf`. **Check**: `scripts/check-repo.sh` prints the OK line.
- [x] T005 [P] Remove `docs/superpowers/` (superseded by this spec folder) and add `README.md` stub (title, one-paragraph goal, "work in progress", link to `specs/001-kentico-umbraco-migrator/spec.md`). **Check**: `scripts/check-repo.sh` OK; `ls docs` shows DISCOVERY.md and MAPPING.md only.

**Checkpoint**: solution builds, Azurite runs, repository guard in place.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared records and interfaces, the Umbraco host, the API client, the sample database
skeleton, and verified Umbraco formats. After this phase the tracks for US1 to US5 can run in
parallel.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Shared model (pure code)

- [x] T006 [P] Create the source records exactly as in [data-model.md](data-model.md) section 1 and the `IKenticoReader` interface from [contracts/kentico-reader.md](contracts/kentico-reader.md) in `src/KenticoMigrator/Kentico/IKenticoReader.cs` (records may share the file). **Check**: `dotnet build`.
- [x] T007 [P] Create the site profile records (data-model section 2) and `SiteProfile.Load(string path)` with the validation rules of [contracts/site-profile.md](contracts/site-profile.md) (unknown `editor`, repeated `target` alias in a type) in `src/KenticoMigrator/Migration/SiteProfile.cs`; test first in `tests/KenticoMigrator.Tests/Migration/SiteProfileTests.cs` (`Loads_valid_profile`, `Rejects_unknown_editor`, `Rejects_duplicate_target_alias`, `Accepts_jsonc_comments`). **Check**: unit tests pass.
- [x] T008 [P] Create the plan records (data-model sections 3 and 4: `MigrationPlan`, `TargetDataType`, `TargetDocumentType`, `TargetProperty`, `TargetTemplate`, `TargetMedia`, `MediaSource`, `TargetDocument`, `PlannedValue`, `SkippedItem`, `PlanIssue`, `LinkKind`, `RewriteResult`, `PlannedRedirect`) in `src/KenticoMigrator/Migration/MigrationPlan.cs`. **Check**: `dotnet build`.
- [x] T009 [P] Test then implement `Keys` in `src/KenticoMigrator/Migration/Keys.cs`: `Derived(kind, naturalKey)` is UUID v5 (RFC 9562) with namespace `6f1b6d0e-6c2f-5a8e-9f41-6b2c0b7a4d11` over `"{kind}:{naturalKey}"` lowercased. Tests in `tests/KenticoMigrator.Tests/Migration/KeysTests.cs`: `Same_input_same_key`, `Case_insensitive_natural_key`, `Known_vector` (compare with a value computed by Python `uuid.uuid5` and pasted into the test), `Version_nibble_is_5`. **Check**: unit tests pass.
- [x] T010 [P] Create sync and run records (data-model sections 5 and 6: `ItemOutcome`, `ItemResult`, `MissingInSource`, `TargetCounts`, `CountDrop`, `PhaseTiming`, `RunReport`, `ExitCodes` constants 0/1/2/3) in `src/KenticoMigrator/Umbraco/SyncResults.cs` and `src/KenticoMigrator/Run/RunModel.cs`. **Check**: `dotnet build`.

### Sample database skeleton

- [x] T011 Create `sample/kentico11-sample.sql` part 1: `CREATE DATABASE Kentico11Sample` (drop if exists), all tables and columns listed in [contracts/kentico-reader.md](contracts/kentico-reader.md) with Kentico 11 column types, `CMS_SettingsKey` rows (`CMSDBVersion` = `11.0`, `CMSDefaultCultureCode` = `en-US`, `CMSDefaultAliasPath` = `/Home`), one `CMS_Site` (`KayakShop`, domain `kayaks.example`), `CMS_Class` rows for `CMS.Root`, `CMS.Folder`, `CMS.MenuItem` with real Kentico 11 `ClassFormDefinition` XML, and the root node. Invented content only (Constitution I). **Check**: `scripts/restore-sample.sh` (T012) creates the database; `SELECT KeyValue FROM CMS_SettingsKey WHERE KeyName='CMSDBVersion'` returns `11.0`.
- [x] T012 [P] Create `scripts/restore-sample.sh`: copies `sample/kentico11-sample.sql` into `$SQL_CONTAINER` (default `kentico-migrator-sql`), runs it with the container's own `MSSQL_SA_PASSWORD` (never printed), then prints document counts per class. Create `sample/README.md` with an "expected counts" table (updated by later tasks). **Check**: script exits 0 and prints the root count `CMS.Root 1`.

### Umbraco host

- [x] T013 Create `host/UmbracoSite` with `dotnet new install Umbraco.Templates::17.*` and `dotnet new umbraco -n UmbracoSite -o host/UmbracoSite`; add it to `KenticoMigrator.slnx`; confirm the `Umbraco.Cms` version in the csproj is 17.x; set HTTPS port 44317 in `Properties/launchSettings.json`; `appsettings.json` without secrets; unattended install keys read from user secrets (`Umbraco:CMS:Unattended:InstallUnattended=true`, `UnattendedUserName`, `UnattendedUserEmail`, `UnattendedUserPassword`); connection string `ConnectionStrings:umbracoDbDSN` from user secrets pointing to database `PO_Umbraco`. Document the secrets in `host/README.md`. **Check**: `dotnet run --project host/UmbracoSite` installs into an empty `PO_Umbraco` and `https://localhost:44317/umbraco` shows the login page.
- [x] T014 Add Azure Blob media to the host: package `Umbraco.StorageProviders.AzureBlob` 17.x, `.AddAzureBlobMediaFileSystem()` and `.AddAzureBlobImageSharpCache()` in `host/UmbracoSite/Program.cs`, `Umbraco:Storage:AzureBlob:Media:ConnectionString=UseDevelopmentStorage=true` and `ContainerName=media` in `appsettings.Development.json`. **Check**: upload an image in the back office; `curl -s 'http://127.0.0.1:10000/devstoreaccount1/media?restype=container&comp=list'` lists it; the image URL renders in the browser.
- [x] T015 Implement `host/UmbracoSite/Migration/ApiClientSeeder.cs` (research R8): Development only, on application start creates an API user named `Kentico migrator` in the administrators group with client credentials `Migration:ApiClient:Id` (default `umbraco-back-office-migrator`) and `Migration:ApiClient:Secret` (user secret), idempotent. Confirm the Umbraco 17 service names in the docs before coding. **Check**: restart the host twice; `curl -sk -d grant_type=client_credentials -d client_id=umbraco-back-office-migrator -d client_secret=$SECRET https://localhost:44317/umbraco/management/api/v1/security/back-office/token` returns an `access_token`; the back office shows one API user.

### Management API client

- [x] T016 Test then implement `ManagementApiClient` in `src/KenticoMigrator/Umbraco/ManagementApiClient.cs` per [contracts/umbraco-management-api.md](contracts/umbraco-management-api.md): `GetAsync<T>(path)` returns null on 404; `SendAsync(method, path, body)`; `PostMultipartAsync(path, content)`; token cached until 30 s before expiry; one retry on 401 with a fresh token; up to 2 retries with 1 s and 3 s delay on 5xx and network errors; `Umbraco:AllowUntrustedCertificate` honoured. Tests with a fake `HttpMessageHandler` in `tests/KenticoMigrator.Tests/Umbraco/ManagementApiClientTests.cs`: `Refreshes_token_on_401`, `Second_401_throws`, `Reuses_token_until_expiry` (1 token request for 10 calls), `Get_returns_null_on_404`, `Retries_5xx_twice_then_throws`. **Check**: unit tests pass.
- [x] T017 Integration test `tests/KenticoMigrator.Tests/Umbraco/ManagementApiClientIntegrationTests.cs`: `Can_get_token_and_read_root` against the running host. **Check**: integration test passes with the host running.

### Verified Umbraco formats (research R6, R7, R9, R10)

- [x] T018 With the host running, create by hand in the back office: one page with a Textstring, a Textarea, a rich text containing an internal link, a media link and an image, a numeric, a decimal, a true/false, a date, tags, a dropdown, a single and a multiple media picker; read it with `GET /document/{id}` and the media with `GET /media/{id}`; save the invented-content JSON as fixtures in `tests/KenticoMigrator.Tests/Fixtures/umbraco/` (`document-all-editors.json`, `media-image.json`, `media-file.json`); write the confirmed shapes into research.md R6, R7, R9 (replace "Verify" with "Verified" and the exact shapes). Also record which endpoint returns document URLs (R10) and how built-in data types are found by name. **Check**: fixtures committed; research.md has no "**Verify**" left for R6, R7, R9.
- [x] T019 Verify routing behaviour (research R4, R10) with the host: is `/Mixed-Case-Path` served for a page at `/mixed-case-path`; is a request to `/old.aspx` reaching content finders (register a redirect by hand in the back office if possible, or note for T060). Record results in research.md R10 and MAPPING.md section 3 (case-insensitivity line). **Check**: research.md R10 says "Verified" with the observed status codes.

**Checkpoint**: Foundation ready. Tracks for US1 (pages), US3 (media), US4 (links), US5 (URLs) can
start in parallel; US2 builds on US1's runner.

---

## Phase 3: User Story 1 - Migrate pages with their hierarchy (Priority: P1) 🎯 MVP

**Goal**: One `migrate` run creates document types and all in-scope sample pages in the right
hierarchy with fields, SEO, navigation flags, order and publish state.

**Independent Test**: Run `migrate --profile profiles/sample.json` against an empty host; the back
office shows every in-scope sample page under the expected parent with the expected values.

### Sample data for US1

- [x] T020 [US1] Extend `sample/kentico11-sample.sql` part 2 (pages): classes `CMS.Article` and `DEMO.Brand` (fields Name, HeaderText, TopText, BottomText, Synonyms, MetaTitle, MetaDescription, MetaKeywords, Order) and `DEMO.Product` (ProductName, Featured, ShippingInformation, ShippingImage radio list with 2 options) with coupled tables and form definitions; pages: Home, About (one editable region), Story (two editable regions), Contact (no text, alias `/contact` added in US5), one excluded page `/Account/Profile`, folder `/Catalog`, articles list `/Articles` with 3 articles, brands container `/Catalog/Brands` with 6 brands (2 names with a space), 1 brand row without a node, products container with 2 products linked to `COM_SKU` rows, 1 draft (`DocumentCanBePublished=0`), 1 archived, 1 scheduled page (`DocumentPublishFrom` in 2099), `DocumentContent` XML and page templates with `editabletext` web parts in a defined order. Update the expected counts in `sample/README.md`. **Check**: `scripts/restore-sample.sh` prints the expected counts.

### Reader (US1 part)

- [x] T021 [P] [US1] Test then implement `FormDefinition.Parse(string xml) -> IReadOnlyList<SourceField>` in `src/KenticoMigrator/Kentico/FormDefinition.cs`: column, `columntype`, `columnsize`, `settings/controlname`, field `guid`, and options for dropdown/radio controls (split on newline; each `value;text` keeps the value). Tests in `tests/KenticoMigrator.Tests/Kentico/FormDefinitionTests.cs` with invented XML fixtures: `Parses_columns_and_types`, `Parses_dropdown_options`, `Ignores_fields_without_column`, `Reads_field_guid`. **Check**: unit tests pass.
- [x] T022 [P] [US1] Test then implement `EditableRegions.Parse(string? documentContent, IReadOnlyList<string> templateOrder) -> IReadOnlyList<EditableRegion>` in `src/KenticoMigrator/Kentico/EditableRegions.cs`, plus `EditableRegions.TemplateOrder(string? pageTemplateWebPartsXml)` returning `editabletext` web part ids in order. Tests in `tests/KenticoMigrator.Tests/Kentico/EditableRegionsTests.cs`: `Returns_regions_in_template_order`, `Unknown_regions_are_appended_in_document_order`, `Html_is_decoded_exactly_once` (input with `&amp;lt;` stays `&lt;`), `Null_or_empty_gives_empty_list`. **Check**: unit tests pass.
- [x] T023 [US1] Implement `Kentico11SqlReader` documents part in `src/KenticoMigrator/Kentico/Kentico11SqlReader.cs`: version check (`NotSupportedException` "Kentico <v> is not supported; this reader supports 11.x"), single site and culture check, page types used by documents plus classes passed in `unlinkedRowClasses`, documents with metadata and `IsPublished` per research R5a (`DocumentCanBePublished = 1` and inside the publish window), coupled fields by `DocumentForeignKeyValue`, regions (T022), SKU fields via `NodeSKUID`, unlinked rows, `CountAsync`. Connection string gets `ApplicationIntent=ReadOnly`; only `SELECT` statements. Attachments, aliases and media files return empty lists until US3 and US5. **Check**: `dotnet build`.
- [x] T024 [US1] Integration tests in `tests/KenticoMigrator.Tests/Kentico/Kentico11SqlReaderTests.cs` against the sample: `Counts_per_class_match_sample_readme`, `Two_region_page_has_regions_in_template_order`, `Draft_archived_and_scheduled_are_not_published`, `Product_has_sku_fields`, `Unlinked_brand_row_is_read`, `Rejects_non_11_version` (temporary copy of the DB with `CMSDBVersion=12.0` created and dropped by the test). **Check**: integration tests pass with `KENTICO_SAMPLE_CS` set.

### Planning (US1 part)

- [x] T025 [P] [US1] Create `profiles/sample.json` for the sample (types for `CMS.MenuItem`, `CMS.Article`, `DEMO.Brand`, `DEMO.Product`, path overrides for `/Articles` and the brands container, excluded `/Account`, folder `/Catalog`, reparent rule for brands with `oldRoutePattern` `/brand-search/{Name}`, unlinked brand rows unpublished). **Check**: `SiteProfile.Load("profiles/sample.json")` in a unit test `Sample_profile_loads` passes.
- [x] T026 [US1] Test then implement `PlanBuilder.Build(SourceSite, SiteProfile) -> MigrationPlan` schema part in `src/KenticoMigrator/Migration/PlanBuilder.cs`: data types (built-ins by name, tool-created Decimal and one dropdown per dropdown/radio field with `Keys.Derived("datatype", ...)`), compositions `seoComposition`, `navigationComposition`, `migrationComposition` (MAPPING.md 2.1 plus `migrationSourceKey`, `migrationFingerprint`), document types from profile `types` with allowed children, a `folder` type without template, one template `migratedPage` with layout `migratedLayout`. Tests in `tests/KenticoMigrator.Tests/Migration/PlanBuilderSchemaTests.cs`: `Creates_one_document_type_per_mapping`, `Adds_three_compositions_to_every_page_type`, `Folder_type_has_no_template`, `Dropdown_options_come_from_kentico_field`, `Keys_are_stable_across_builds`. **Check**: unit tests pass.
- [x] T027 [US1] Test then implement `PlanBuilder` documents part: excluded prefixes with descendants skipped (`SkippedItem` category `application page`), linked nodes skipped, folders, path overrides, reparent rules, `UrlName` = node alias, unlinked rows as unpublished documents with `Keys.Derived("unlinked", "<class>:<id>")`, `Publish` from `IsPublished`, `ScheduleNote` for publish window, field values via `FieldMapping.Source` (`region:first`, `region:rest` joined with a blank line, `sku:`, `doc:`), SEO and navigation values, `migrationSourceKey`, `SortOrder` from `NodeOrder`, sibling URL name collision as `PlanIssue` error. Tests in `tests/KenticoMigrator.Tests/Migration/PlanBuilderDocumentTests.cs`: `Excluded_prefix_and_descendants_are_skipped_with_reason`, `Folder_children_keep_their_path`, `Brand_is_reparented_and_url_name_is_node_alias`, `First_region_to_bodyText_rest_to_additionalContent`, `Unlinked_row_becomes_unpublished_with_stable_key`, `Product_gets_sku_fields`, `Draft_is_planned_unpublished_with_schedule_note`, `Duplicate_sibling_url_name_is_plan_error`, `Build_is_deterministic`. **Check**: unit tests pass.

### Writing to Umbraco (US1 part)

- [x] T028 [US1] Implement `SchemaSync.EnsureAsync(MigrationPlan, bool preview, CancellationToken) -> IReadOnlyList<ItemResult>` in `src/KenticoMigrator/Umbraco/SchemaSync.cs` and the request models it needs in `src/KenticoMigrator/Umbraco/ApiModels.cs` (shapes from T018): find built-in data types by name, create or update tool data types, templates (content from embedded `src/KenticoMigrator/Umbraco/Templates/MigratedLayout.cshtml` and `MigratedPage.cshtml`, which render title, bodyText, additionalContent, introText, teaserImage, images and product fields when present), compositions, then document types in dependency order. Unit test with a fake client in `tests/KenticoMigrator.Tests/Umbraco/SchemaSyncTests.cs`: `Creates_missing_and_skips_equal`, `Compositions_before_page_types`. **Check**: unit tests pass.
- [x] T029 [US1] Implement `DocumentSync.SyncAsync(IReadOnlyList<TargetDocument>, SyncOptions, CancellationToken) -> IReadOnlyList<ItemResult>` in `src/KenticoMigrator/Umbraco/DocumentSync.cs`: parents before children, `GET` by key, `POST` with key when missing, `PUT` when planned values differ, publish after create or update when `Publish`, sort per parent when order differs, item failures recorded as `Failed` with the API error and the run continues. Rich text values are sent as-is for now (US4 adds rewriting). Unit tests with a fake client in `tests/KenticoMigrator.Tests/Umbraco/DocumentSyncTests.cs`: `Creates_parents_before_children`, `Posts_when_missing_puts_when_changed_skips_when_equal`, `Publishes_only_publish_items`, `Failed_item_does_not_stop_others`. **Check**: unit tests pass.
- [x] T030 [US1] Implement `Program.cs` verb dispatch and configuration binding (cli contract) and `MigrationRunner` minimal path in `src/KenticoMigrator/Run/MigrationRunner.cs`: `migrate` = read, plan (exit 2 on plan errors or profile errors), schema, documents; console summary per phase; exit codes 0/1/2. **Check**: `dotnet run --project src/KenticoMigrator -- migrate --profile profiles/sample.json` against the empty host exits 0.
- [x] T031 [US1] Integration test `tests/KenticoMigrator.Tests/EndToEnd/PagesEndToEndTests.cs` `Sample_pages_exist_under_expected_parents`: after a run, walk the tree through the API and assert parent, type, publish state and three field values for Home, Story, one article, one brand, the unpublished brand, one product (price), the draft. **Check**: integration test passes; manual look in the back office matches.

**Checkpoint**: MVP. Sample pages migrate with hierarchy.

---

## Phase 4: User Story 2 - Verify every run and re-run safely (Priority: P1)

**Goal**: Counts snapshot and guard, full report, fingerprint-based editor protection, missing in
source, preview with diff, `counts` command.

**Independent Test**: Run `migrate` twice on the sample: second report shows 0 created and 0
updated. Edit a page in the back office and re-run: it is reported as edited and left alone.

- [x] T032 [P] [US2] Test then implement `Fingerprint.Compute(TargetDocument or TargetMedia owned values) -> string` (SHA-256 hex over canonical JSON: sorted keys, invariant culture numbers, ISO dates) and `Fingerprint.FromCurrent(apiValues)` in `src/KenticoMigrator/Umbraco/Fingerprint.cs`. Tests in `tests/KenticoMigrator.Tests/Umbraco/FingerprintTests.cs`: `Same_values_any_key_order_same_hash`, `Decimal_formatting_is_culture_invariant`, `Changed_value_changes_hash`, `Planned_and_current_shapes_hash_equal_for_same_content` (using the T018 fixture). **Check**: unit tests pass.
- [x] T033 [US2] Extend `DocumentSync` with the R5 outcome rules: store `migrationFingerprint` on every write; `EditedSkipped` when current fingerprint differs from stored; `EditedOverwritten` with `SyncOptions.OverwriteEdited`; `Unchanged` when planned equals stored; preview mode returns `WouldCreate`/`WouldUpdate`/`Unchanged`/`EditedSkipped` without writes. Tests added to `DocumentSyncTests.cs`: `Edited_item_is_skipped`, `Edited_item_is_overwritten_with_option`, `Preview_makes_no_writes`. **Check**: unit tests pass.
- [x] T034 [P] [US2] Test then implement `CountGuard.FindDrops(TargetCounts before, TargetCounts after) -> IReadOnlyList<CountDrop>` and `TargetCounter.CountAsync(client)` (tree walk by document type, media count, redirect total) in `src/KenticoMigrator/Run/Counts.cs`. Tests in `tests/KenticoMigrator.Tests/Run/CountGuardTests.cs`: `Reports_drop_with_type_and_both_counts`, `Growth_is_not_a_drop`, `New_type_is_not_a_drop`, `Missing_type_after_is_a_drop`. **Check**: unit tests pass.
- [x] T035 [US2] Implement `MissingInSourceFinder.FindAsync(plan, client)` in `src/KenticoMigrator/Umbraco/MissingInSourceFinder.cs`: walk the document tree under `home` and the media import folder, return items with a `migrationSourceKey` whose key is not in the plan. No delete or unpublish code path exists (FR-015b). Unit test with a fake client in `tests/KenticoMigrator.Tests/Umbraco/MissingInSourceFinderTests.cs`: `Lists_migrated_items_not_in_plan`, `Ignores_items_without_source_key`. **Check**: unit tests pass.
- [x] T036 [US2] Test then implement `Report.Write(RunReport, string runDir)` in `src/KenticoMigrator/Run/Report.cs`: `report.json`, `report.md`, `redirects.csv`, `skipped.csv`, `items.csv` per [contracts/cli.md](contracts/cli.md), including the sections "missing in source, review manually", "edited in Umbraco" and "publish schedules not applied" (FR-014a). Tests in `tests/KenticoMigrator.Tests/Run/ReportTests.cs`: `Markdown_lists_every_skipped_category`, `Markdown_lists_missing_edited_and_schedules`, `Markdown_has_no_em_dash`, `Csv_escapes_commas_and_quotes`, `Report_contains_no_field_values` (planted marker string in a field value must not appear in any output file). **Check**: unit tests pass.
- [x] T037 [US2] Complete `MigrationRunner`: phase order read, plan, snapshot (source and target), schema, documents, missing in source, snapshot, `CountGuard` (exit 3 on drop), source counts compare (warning), report; per-phase timings; `plan` command (preview through the same path with `preview: true`, plan-only fallback with a "target not reachable" note when Umbraco is not configured or the token call fails); `counts` command; `--overwrite-edited`; `--run-dir`. Tests with fakes in `tests/KenticoMigrator.Tests/Run/MigrationRunnerTests.cs`: `Count_drop_stops_with_exit_code_3`, `Failed_item_gives_exit_code_1_and_run_finishes`, `Plan_errors_give_exit_code_2_and_no_writes`, `Preview_falls_back_to_plan_only_when_target_unreachable`. **Check**: unit tests pass.
- [x] T038 [US2] Integration test `tests/KenticoMigrator.Tests/EndToEnd/RerunEndToEndTests.cs`: `Second_run_creates_and_updates_nothing` (all counts equal), `Edited_page_is_reported_and_kept` (edit a title through the API between runs), `Preview_reports_would_create_on_empty_target_and_unchanged_after_migrate`. **Check**: integration tests pass; quickstart steps 3 and 6 behave as described.

**Checkpoint**: US1 and US2 together satisfy the core of the Definition of Done for pages.

---

## Phase 5: User Story 3 - Media in cloud storage (Priority: P2)

**Goal**: Attachments and media library files become media items in blob storage, linked from
page fields.

**Independent Test**: After a sample run, every sample attachment and library file is a media item
served from Azurite, and the product gallery shows its images in order.

- [x] T039 [P] [US3] Extend `sample/kentico11-sample.sql` part 3 (media): 4 small generated JPEG/PNG attachments as `0x` literals for the 2 products (attachment group field `Images`, `AttachmentOrder` set), 1 unsorted attachment, 1 article teaser image (file field), 1 historic attachment version in `CMS_AttachmentHistory` (must be ignored), a `CMS.File` node with an attachment and one `CMS.File` node listed as not uploaded in the profile; one media library `Gallery` with 3 files in `Media_File` whose files are committed under `sample/media/Gallery/` (two in a subfolder `boats/`), and one `Media_File` row whose file is missing on disk. Generate images with a throwaway script outside the repo. Update `sample/README.md` and `profiles/sample.json` (`notUploadedPaths`, `mediaRoot`). **Check**: `scripts/restore-sample.sh` prints attachment and media file counts that match the README.
- [x] T040 [US3] Extend `Kentico11SqlReader`: attachments (current versions only, `FieldColumn` resolved for file fields by GUID value and for group fields by `AttachmentGroupGUID` = field GUID), media libraries and files, `OpenAttachmentAsync` (streams `AttachmentBinary`), `OpenMediaFileAsync` (from `Kentico:MediaRoot` or profile `mediaRoot`, `FileNotFoundException` when missing). Integration tests added to `Kentico11SqlReaderTests.cs`: `Reads_current_attachments_only`, `Group_attachments_are_ordered`, `Reads_media_library_files`, `Missing_media_file_throws_file_not_found`. **Check**: integration tests pass.
- [x] T041 [US3] Test then implement `PlanBuilder` media part: media folders (`Kentico import/Attachments/<section>`, `Kentico import/Libraries/<library>/<subfolders>`) with derived keys, `Image` or `File` by extension, `CMS.File` nodes become media (not documents), unsorted attachments become media in the page's folder without a field link, attachments not referenced by any field or unsorted flag are reported as `unreferenced attachment`, not-uploaded paths skipped with category `host file`, missing library files skipped with category `missing file`, field values `file:` as `MediaSingle` and `attachments:` as `MediaMultiple` in attachment order. Tests in `tests/KenticoMigrator.Tests/Migration/PlanBuilderMediaTests.cs`: `Attachment_key_is_attachment_guid`, `Library_folders_mirror_subfolders`, `File_node_becomes_media_not_document`, `Not_uploaded_file_is_reported_as_host_file`, `Gallery_field_keeps_attachment_order`, `Unsorted_attachment_becomes_unlinked_media`, `History_versions_are_not_planned`. **Check**: unit tests pass.
- [x] T042 [US3] Implement `MediaSync.SyncAsync(IReadOnlyList<TargetMedia>, IKenticoReader, SyncOptions, CancellationToken)` in `src/KenticoMigrator/Umbraco/MediaSync.cs`: folders first, `POST /temporary-file` then `POST /media` with key and `umbracoFile` per the T018 fixture, same fingerprint and outcome rules as documents (R5), failures recorded and run continues. Wire into `MigrationRunner` before documents; `DocumentSync` writes media picker values in the T018 shape. Unit tests in `tests/KenticoMigrator.Tests/Umbraco/MediaSyncTests.cs`: `Creates_folders_before_files`, `Uploads_temporary_file_then_creates_media`, `Unchanged_media_is_not_uploaded_again`. **Check**: unit tests pass.
- [x] T043 [US3] Integration test `tests/KenticoMigrator.Tests/EndToEnd/MediaEndToEndTests.cs`: `Sample_media_exist_and_are_served_from_blob` (each media URL, served by the host from Azure Blob storage (Azurite), returns 200, and its blob exists in the `media` container), `Product_gallery_has_images_in_order`, `Second_run_uploads_nothing`. **Check**: integration tests pass.

**Checkpoint**: pages and images are in place.

---

## Phase 6: User Story 4 - Links inside rich text keep working (Priority: P2)

**Goal**: Internal page and media links in rich text become Umbraco item links; the rest are
counted and reported.

**Independent Test**: The sample rich text holds one link of each kind; after a run the page and
media links are local links, external links are unchanged, and the unknown link is in the report.

- [x] T044 [P] [US4] Extend `sample/kentico11-sample.sql` part 4 (rich text): in Story and one article add links to: another page by alias path, the same with `.aspx`, an absolute `https://www.kayaks.example/...` link, a short document alias (alias row added in T053), a `~/getattachment/<guid>/name.jpg` image, a `~/getmedia/<guid>/file` link, `/brand-search/Sea Spirit` (brand with a space), an external site, `mailto:`, `#anchor`, an application route `/cart`, and one unknown internal page. Update `sample/README.md` with the expected link statistics. **Check**: `scripts/restore-sample.sh` succeeds; a SQL `LIKE` count of `href=` in the sample matches the README.
- [x] T045 [P] [US4] Test then implement `UrlIndex` in `src/KenticoMigrator/Migration/UrlIndex.cs`: `Create(MigrationPlan, IReadOnlyList<SourceAlias>, SiteProfile)`, `Resolve(string href) -> (LinkKind, Guid?)`, `Normalize(string url)` (lowercase, percent-decoded, `.aspx` removed, trailing slash removed, query and fragment removed, own hosts removed, `~` removed). Tests in `tests/KenticoMigrator.Tests/Migration/UrlIndexTests.cs`: `Normalize_examples` (`/Search-Featured/Sea%20Spirit.aspx?x=1` becomes `/search-featured/sea spirit`), `Resolves_alias_path_and_aspx_form`, `Resolves_document_alias`, `Resolves_own_host_absolute_url`, `Resolves_brand_route_by_name_any_case`, `Resolves_getattachment_and_getmedia_guids`, `Classifies_external_mail_anchor_application_unknown`. **Check**: unit tests pass.
- [x] T046 [US4] Test then implement `RichTextRewriter.Rewrite(string html) -> RewriteResult` in `src/KenticoMigrator/Migration/RichTextRewriter.cs` with AngleSharp (`HtmlParser.ParseFragment`), markup constants taken from the T018 fixture (research R6). Tests in `tests/KenticoMigrator.Tests/Migration/RichTextRewriterTests.cs`: `Page_link_becomes_local_link`, `Media_link_and_image_become_media_references`, `External_mail_anchor_application_unchanged_and_counted`, `Unknown_internal_link_unchanged_and_listed`, `Rewriting_twice_is_idempotent`, `Unrelated_markup_is_preserved` (fixture comparison). **Check**: unit tests pass.
- [x] T047 [US4] Apply the rewriter in `DocumentSync` before fingerprinting rich text values (a document's own key is known in advance, so one pass is enough); add link statistics and unresolved links (with the page's old path) to `RunReport` and `report.md`. Tests in `DocumentSyncTests.cs`: `Rich_text_is_rewritten_before_write`, `Unresolved_links_are_reported_with_old_path`. **Check**: unit tests pass.
- [x] T048 [US4] Integration test `tests/KenticoMigrator.Tests/EndToEnd/LinksEndToEndTests.cs`: `Rendered_story_page_links_point_to_new_urls` (fetch the rendered page HTML, assert `href` values equal the target pages' URLs and the image `src` is the media URL of the uploaded file, which the host serves from blob storage), `Report_lists_one_unresolved_link`. **Check**: integration tests pass.

**Checkpoint**: no broken internal links in migrated rich text.

---

## Phase 7: User Story 5 - Old URLs keep working (Priority: P2)

**Goal**: Kept paths served directly; `.aspx`, aliases, old home path and renamed brand URLs
redirect with 301; redirects visible in the back office.

**Independent Test**: `scripts/check-urls.sh` prints OK for every line of
`sample/expected-urls.txt`.

- [x] T049 [P] [US5] Implement `host/UmbracoSite/Migration/RedirectsController.cs` per [contracts/redirect-endpoint.md](contracts/redirect-endpoint.md) (Management API controller base and authorisation, batch of up to 500, idempotent per `(url, contentKey)`, `failed` entries for unknown keys) using `IRedirectUrlService`; store routes in the format confirmed in T019. **Check**: the contract's acceptance check: `curl -sI https://localhost:44317/test-old` returns 301 to the test page; posting the same body twice reports `existing`.
- [x] T050 [P] [US5] Extend `Kentico11SqlReader` with aliases (`CMS_DocumentAlias`, `IsWildcard` when the path has `{` or a wildcard rule) and `DocumentUrlPath`/`DocumentExtensions`; integration test `Reads_aliases_and_flags_wildcards` in `Kentico11SqlReaderTests.cs`. **Check**: integration test passes.
- [x] T051 [US5] Extend `PlanBuilder` with `OldUrls` per document (alias path, alias path + `.aspx`, `DocumentUrlPath`, fixed aliases, reparent `oldRoutePattern` filled from the brand name, `/` home handling for `CMSDefaultAliasPath`) and wildcard aliases as `SkippedItem` category `wildcard route`. Tests in `tests/KenticoMigrator.Tests/Migration/PlanBuilderUrlTests.cs`: `Old_urls_include_aspx_form`, `Home_gets_old_home_path`, `Brand_gets_old_route_with_raw_name`, `Wildcard_alias_is_reported_not_planned`. **Check**: unit tests pass.
- [x] T052 [P] [US5] Test then implement `RedirectPlanner.Plan(IReadOnlyList<TargetDocument>, IReadOnlyDictionary<Guid,string> actualUrls) -> IReadOnlyList<PlannedRedirect>` in `src/KenticoMigrator/Migration/RedirectPlanner.cs`. Tests in `tests/KenticoMigrator.Tests/Migration/RedirectPlannerTests.cs`: `No_redirect_when_only_case_differs` (unless T019 found Umbraco is case-sensitive; then `Case_variant_gets_redirect`), `Aspx_form_gets_redirect`, `Old_home_paths_redirect_to_root`, `Brand_name_with_space_gets_renamed_redirect`, `Unpublished_document_gets_no_redirect`, `Duplicate_old_url_goes_to_first_by_sort_order_with_warning`. **Check**: unit tests pass.
- [x] T053 [US5] Extend the sample with aliases: `/contact` for Contact, an old `/Story.aspx` style link already covered by `.aspx`, one wildcard alias `/{title}-i{ID}` on an excluded application page, one `DocumentUrlPath` override; create `sample/expected-urls.txt` (`<old url> <expected status> <expected final path>`) covering kept paths, mixed case, `.aspx`, alias, home, brand with space, and the excluded page (404). **Check**: `scripts/restore-sample.sh` succeeds; the file has one line per case listed.
- [x] T054 [US5] Implement `DocumentSync.ReadUrlsAsync(keys)` (endpoint confirmed in T018) and `RedirectSync.RegisterAsync(IReadOnlyList<PlannedRedirect>, preview)` in `src/KenticoMigrator/Umbraco/RedirectSync.cs` (batches of 200 to the host endpoint, counts created/existing/failed); wire into `MigrationRunner` after documents; add redirects by reason to the report and `redirects.csv`. Unit tests with fakes in `tests/KenticoMigrator.Tests/Umbraco/RedirectSyncTests.cs`: `Sends_batches_of_200`, `Preview_sends_nothing`. **Check**: unit tests pass.
- [x] T055 [US5] Create `scripts/check-urls.sh <base-url> <file>`: requests each old URL without following redirects (`curl -sk -o /dev/null -w '%{http_code} %{redirect_url}'`), compares status and final path, prints `OK`/`FAIL` per line and a summary, exits non-zero on any FAIL. Integration test `tests/KenticoMigrator.Tests/EndToEnd/UrlsEndToEndTests.cs` `Expected_urls_all_ok` runs the same checks in C#. **Check**: `scripts/check-urls.sh https://localhost:44317 sample/expected-urls.txt` prints all OK; the back office lists the redirects.

**Checkpoint**: Definition of Done satisfied on the sample (pages, images, old URLs).

---

## Phase 8: User Story 6 - Try the tool without client data (Priority: P3)

**Goal**: A newcomer runs the sample from the README; the reference numbers are published.

**Independent Test**: Fresh clone, follow README, migrated sample site in under 15 minutes.

- [x] T056 [US6] Create `profiles/pontoonsonly.json` from MAPPING.md (types, path overrides for `/Articles`, `/Listings/Search-Featured` as brand list at `search-featured`, folders `/Listings` and `/Products`, excluded prefixes for account, listing functions, special pages, login, registration; lookups to `settings`; reparent `PO.Make` with `oldRoutePattern` `/search-featured/{Name}`; unlinked `PO.Make` rows unpublished; `notUploadedPaths` for the feed and `ads.txt`; application route prefixes). Section-level paths only. Run `plan` against `PO_Kentico` and fix the profile until the report has no plan errors. **Check**: `plan --profile profiles/pontoonsonly.json` exits 0; `scripts/check-repo.sh` OK.
- [x] T057 [US6] Reference run on the owner's machine: fresh `PO_Umbraco`, `migrate --profile profiles/pontoonsonly.json`, then again; generate `_work/expected-urls.txt` (git-ignored) from the source aliases and old paths and run `scripts/check-urls.sh`. Compare with SC-001, SC-003, SC-004, SC-006. Fix defects through new tasks if numbers differ. **Check**: second run 0 created and 0 updated; all URLs OK; counts match SC-001. **Result (2026-11-14)**: second run 0 created, 0 updated; counts match SC-001; 155 of 156 URLs OK, the one failure is the alias `/products` (shadowed by the template-less folder at that URL), tracked as T064.
- [x] T058 [US6] Write `docs/RESULTS.md` from the reference run report: before/after table (source counts, migrated, skipped by category, media, links rewritten/external/to application/unresolved, redirects by reason), timings, and honest notes (Kentico 11 not 7/8, 0 media libraries, listing photos not provided, thin rich text, scope extensions). Aggregate numbers only. **Check**: `scripts/check-repo.sh` OK; every number in RESULTS.md appears in the run's `report.json`.
- [x] T059 [US6] Write `README.md` in English: problem, what the tool does, architecture in five lines, before/after table from RESULTS.md, quickstart for the sample (link to quickstart.md steps), configuration, limitations, scope extensions, "Done per project" list (Web Forms templates, web parts and layouts; listings, search, user accounts, dealers, e-commerce; forms, workflows, permissions), license. **Check**: `scripts/check-repo.sh` OK; all relative links resolve (`grep -o '](\([^)]*\))' README.md` targets exist).
- [x] T060 [US6] Fresh-clone validation: clone the repository into a temporary folder, follow README and quickstart steps 1 to 6 with a stopwatch, fix any missing step in the docs. **Check**: migrated sample site reached in under 15 minutes (SC-005), noted in RESULTS.md.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [x] T061 [P] Update `docs/MAPPING.md` with anything the implementation verified or changed (research "Verified" items, final media folder names, case-sensitivity result). **Check**: `scripts/check-repo.sh` OK.
- [x] T062 [P] Review all code against the constitution principles I, II, III (no write SQL in `Kentico/`, no direct Umbraco DB access, no delete or unpublish calls, keys from `Keys`), record findings in the PR description or a short `docs/REVIEW.md`, fix violations. **Check**: `git grep -nE "INSERT|UPDATE|DELETE|MERGE" -- src/KenticoMigrator/Kentico` prints nothing; `git grep -n "HttpMethod.Delete" -- src` prints nothing.
- [ ] T063 Run the full suite and quickstart once more; push to the GitHub remote. **Check**: `dotnet build` 0 warnings, all unit and integration tests pass, quickstart steps pass, `git push` succeeds.

---

## Follow-ups from the reference run (T057)

- [ ] T064 [US5] The old URL `/products` (a fixed alias of `/Products/Home`) returns 404 on the migrated reference site: the redirect to `/products/home/` is registered, but `/Products` is a template-less `contentFolder` at the same URL, and Umbraco answers 404 for a found page without a template before the redirect finder runs. Decide and implement a fix (for example: report an alias whose path equals a folder path as a plan warning and let the profile name the page that should answer at the folder URL, or give the folder a template that forwards to its first child). Add a sample case and a unit test for the warning. **Check**: `/products` on the reference run answers 301 or 200 with the products landing page, or the plan lists the collision as a warning with the chosen handling documented.
- [ ] T065 [US5] [P] Brand pages are placed under the brand list at `/listings/search-featured/<name>`, because the profile can move a page only below a page that already exists in the source tree and the brand list lives under the `/Listings` folder. The live brand URL `/search-featured/<Name>` therefore works through a redirect per brand instead of being served directly (MAPPING.md section 3 planned 65 direct URLs). Decide whether a profile rule that places a page at the top level (for example `newParentPath: "/"` for a page with a path override) is worth adding. **Check**: decision recorded in MAPPING.md; if implemented, the reference run serves the live brand URLs with 200 for names without spaces.
- [x] T066 [US2] Decide and implement the publish rule for an update: today an update (source text changed) or a move publishes the page again even when an editor unpublished it, while the rest of FR-015b and the "Notes on re-runs" say a re-run never publishes a page that is not published. Either publish only when the page was published before, or document the update as the one exception; add a test for an unpublished, unedited page whose source text changed. **Check**: the test passes and `contracts/cli.md` states the rule once. **Result (2026-11-14)**: rule implemented in `DocumentSync` (publish after create always; after update, move or overwrite only when published before), unit and end-to-end tests added, rule stated in `contracts/cli.md`.
- [ ] T067 [US2] Write a report for every run: wrap the read phase and the first snapshot in `MigrationRunner` so that a SQL or API failure still writes `report.json` and `report.md` with an error issue (exit code 2). **Check**: a runner test with a failing reader finds the report files.
- [ ] T068 [US2] Evaluate the count guard when a run stops with an exception after the first snapshot: take a best-effort target count in the failure path and keep exit code 3 when a count dropped. **Check**: a runner test whose phase throws after a lowered count returns 3.
- [ ] T069 [US6] The first two commits contain a brand name from the client's data in a document that was deleted later (the old plan under docs/superpowers). Rewrite the history before the first upload to a remote (owner decision on the method) and add a denylist scan kept outside the repository to the release routine. **Check**: `git log --all -S<word>` finds nothing for each denylist word. **Progress (2026-11-14)**: the current tree is clean (the name is replaced by an invented one in UrlIndexTests and T045; a denylist scan of all tracked files finds no other brand name). History rewrite is left to the owner.

---

## Requirement Coverage

| Requirement | Tasks |
|---|---|
| FR-001 read-only source | T023, T040, T062 |
| FR-002 version check | T023, T024 |
| FR-003 source content read | T023, T040, T050 |
| FR-004 unlinked rows | T023, T027 |
| FR-005 type and field mapping | T007, T025, T026, T027, T056 |
| FR-006 excluded paths | T027, T056 |
| FR-007 folders | T027 |
| FR-008 re-parenting | T027, T051 |
| FR-009 stable keys | T009, T026, T027, T041 |
| FR-010 plan errors block writes | T027, T037 |
| FR-011 preview with diff | T033, T037, T038 |
| FR-012 data types, document types, templates | T026, T028 |
| FR-013 media in blob storage | T014, T041, T042, T043 |
| FR-014 pages, publish, sort | T029, T031 |
| FR-014a unpublished source pages | T020, T024, T027, T036 |
| FR-015 upsert | T029, T033 |
| FR-015a editor protection | T032, T033, T038 |
| FR-015b never delete, report missing | T035, T062 |
| FR-016 continue after item failure | T029, T037 |
| FR-017 token renewal | T016 |
| FR-018 page links | T045, T046, T047 |
| FR-019 media links | T045, T046 |
| FR-020 brand route links | T045 |
| FR-021 other links counted, unresolved listed | T046, T047, T048 |
| FR-022 URL name from node alias | T027 |
| FR-023 redirects from actual URLs | T051, T052, T054 |
| FR-024 redirects visible, not duplicated | T049, T055 |
| FR-025 wildcard aliases reported | T051 |
| FR-026 count snapshot and guard | T034, T037 |
| FR-027 report content | T036, T037 |
| FR-028 no field values in reports | T036 |
| FR-029 exit codes | T010, T037 |
| FR-030 configuration and secrets | T002, T013, T015 |
| FR-031 sample dataset | T011, T012, T020, T039, T044, T053 |
| FR-032 host and local services | T003, T013, T014, T015, T049 |
| FR-033 README | T058, T059 |
| SC-001 to SC-004, SC-006 | T057 |
| SC-005 | T060 |
| SC-007 | T004, T059 |

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: after Setup. T013 to T015 (host) and T016 to T017 (client) are
  needed by T018 to T019 (verified formats). T011 to T012 (sample skeleton) are independent of the
  host.
- **US1 (Phase 3)**: after Foundational.
- **US2 (Phase 4)**: after T029 and T030 (it extends `DocumentSync` and `MigrationRunner`).
- **US3 (Phase 5)**: after Foundational for T039 to T041; T042 also needs T029 to T030.
- **US4 (Phase 6)**: after Foundational for T044 to T046; T047 needs T029 (and T033 if done, for
  fingerprint order).
- **US5 (Phase 7)**: T049, T050, T052 after Foundational; T051 after T027; T054 after T029 and T030.
- **US6 (Phase 8)**: after US1 to US5.
- **Polish (Phase 9)**: last.

### Within each story

Test task before implementation (same task when they share a file). Sample data, then reader,
then planner, then sync, then runner wiring, then end-to-end test.

### Shared files touched by several stories (sequence them, do not parallelise)

`sample/kentico11-sample.sql` (T011, T020, T039, T044, T053), `Kentico11SqlReader.cs` (T023, T040,
T050), `PlanBuilder.cs` (T026, T027, T041, T051), `DocumentSync.cs` (T029, T033, T047, T054),
`MigrationRunner.cs` (T030, T037, T042, T054), `sample/README.md`, `profiles/sample.json`.

## Parallel Opportunities

- Phase 1: T002, T003, T004, T005 after T001.
- Phase 2: T006 to T010 together; T011 and T012 alongside the host track T013 to T015; T016 alongside the host.
- After Foundational, four tracks can start at once:
  - **Track A (source)**: T020 to T024, then T040, T050.
  - **Track B (pure core)**: T025 to T027, T045, T046, T052, T041, T051.
  - **Track C (host)**: T049, then helps verify T055.
  - **Track D (sync and runner)**: T028 to T031, then T032 to T038, T042, T047, T054.
- US4 pure parts (T045, T046) and US5 pure parts (T052) do not wait for US1's end-to-end test.

### Parallel example: User Story 1

```text
Together after Foundational:
  T021 FormDefinition parser        (tests/KenticoMigrator.Tests/Kentico/FormDefinitionTests.cs)
  T022 EditableRegions parser       (tests/KenticoMigrator.Tests/Kentico/EditableRegionsTests.cs)
  T025 profiles/sample.json
Then: T023 reader, T026 then T027 plan builder, T028 schema sync, T029 document sync, T030 runner, T031 end to end.
```

## Implementation Strategy

### MVP first

1. Phase 1 and Phase 2.
2. Phase 3 (US1): sample pages migrate. **Stop and validate** with the back office.
3. Phase 4 (US2): re-runs are safe and every run is reported. This is the demo-ready core.

### Incremental delivery

4. US3 media, 5. US4 links, 6. US5 URLs: each adds one visible capability and its end-to-end test.
7. US6: reference run, RESULTS.md, README, fresh-clone check.
8. Polish and push.

## Notes

- [P] tasks touch different files and do not depend on unfinished tasks.
- Every task ends with its Check and a commit; never leave the build broken.
- Formats owned by Umbraco come from the T018 fixtures, not from memory.
- No client material in fixtures, tests, sample data or commit messages (Constitution I).
