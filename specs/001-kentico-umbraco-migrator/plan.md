# Implementation Plan: Kentico to Umbraco Content Migrator

**Branch**: `main` | **Date**: 2026-11-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-kentico-umbraco-migrator/spec.md`

## Summary

A .NET 10 console tool reads a Kentico 11 database with direct SQL behind `IKenticoReader`, builds
an in-memory `MigrationPlan` with stable keys derived from Kentico GUIDs (pure code, unit-tested),
and applies it to a fresh Umbraco 17 site through the Management API: data types, document types,
minimal templates, media (stored in Azure Blob Storage, Azurite locally), pages, then redirects.
Old paths are kept by giving every page its Kentico node alias as URL name; redirects are created
only where a path cannot be kept, through one small endpoint in the host site. Every run snapshots
counts before and after, stops on a count drop, and writes a report. A fictional Kentico 11 sample
database makes the repository runnable without client data.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`), SDK 10.0.201 installed

**Primary Dependencies**: Microsoft.Data.SqlClient, Dapper (reader); AngleSharp (rich text);
Microsoft.Extensions.Configuration (JSON, environment, command line, user secrets) and
Microsoft.Extensions.Logging.Console; Umbraco.Cms 17.x and Umbraco.StorageProviders.AzureBlob 17.x
(host only). No Umbraco packages in the console tool: it talks HTTP.

**Storage**: Source SQL Server 2022 (Kentico 11 database, read-only). Target SQL Server 2022
database `PO_Umbraco` (owned by Umbraco). Media in Azure Blob container `media` (Azurite on port
10000 locally). Run output in `runs/<timestamp>/` (git-ignored).

**Testing**: xUnit v3. Unit tests need nothing external. Integration tests are tagged
`[Trait("Category","Integration")]` or use `IntegrationFact` and skip when their environment is missing:
`KENTICO_SAMPLE_CS` (connection string of the restored sample database) for the reader and end-to-end tests, and
the migrator's `Umbraco:ClientSecret` (user secrets of project `KenticoMigrator`, or the environment variables
`Umbraco__ClientSecret`, `Umbraco__BaseUrl`, `Umbraco__ClientId`) for the tests that call a running host.

**Target Platform**: macOS on Apple Silicon for development; any OS with .NET 10 for the tool.
SQL Server and Azurite in Docker (any SQL Server 2022 container named by `$SQL_CONTAINER`, or the
`sqlserver` service in `docker-compose.yml` under profile `sql`).

**Project Type**: CLI tool plus a companion web host (Umbraco site) plus a SQL sample dataset.

**Performance Goals**: Reference site (about 200 pages, 25 attachments) migrates in under 5
minutes (SC-006). No parallel HTTP needed at this size; requests are sequential for
deterministic order and simple error reporting.

**Constraints**: Read-only source; Management API only on the target (plus the redirect endpoint);
idempotent; no secrets in the repo; no em dashes in any text; no client material in the repo.

**Scale/Scope**: Reference site: 172 source documents, 44 page types (11 used), 25 attachments,
0 media libraries, 28 aliases, 106 rich text links. Sample site: about 20 documents.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How the design complies | Status |
|---|---|---|
| I. Client data stays private | Profiles carry section-level paths only; sample dataset is invented; reports hold paths, keys and counts only (FR-028); `_work/`, `runs/`, `Sources/` ignored; final check greps the repo (SC-007) | Pass |
| II. Read-only source, API-only target | Reader issues `SELECT` only on a read-only intent connection (`ApplicationIntent=ReadOnly` plus no write statements); all target writes go through `ManagementApiClient`; the only non-Management call is the host's redirect endpoint, which uses Umbraco's own `IRedirectUrlService`; all Kentico SQL is in `Kentico11SqlReader` | Pass |
| III. Idempotent, verifiable runs | Stable keys (research R3); get-compare-put upsert (R5); `CountGuard` before and after; report on every run | Pass |
| IV. Test-first core | Parsers, `PlanBuilder`, `UrlIndex`, `RichTextRewriter`, `RedirectPlanner`, `CountGuard` are pure and test-first; Umbraco formats verified against a running host (R6, R7) | Pass |
| V. Minimal code | One console project, one host project, one test project; site rules in profile JSON; three interfaces (`IKenticoReader`, `IUmbracoApi`, `ITargetTreeClient`), two of them beyond the one the principle names, recorded in Complexity Tracking | Pass with recorded exceptions |
| VI. Honest numbers | `docs/RESULTS.md` built from the reference run report; README labels scope extensions and gaps | Pass |
| VII. Small verifiable steps | Every task in tasks.md ends with a check and a commit | Pass |

Post-design re-check (after Phase 1): still Pass. The host's `Migration/` classes are the only code
inside Umbraco: the redirect endpoint, the template-less redirect finder, the API client seeder and the
media container bootstrapper. All are required (the Management API cannot create redirects; Umbraco answers
404 for a page without a template before it tries redirects; an API user is needed for unattended runs; the
blob provider does not create its container) and are documented in research R4 and R8 and in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-kentico-umbraco-migrator/
├── spec.md
├── plan.md              # this file
├── research.md          # decisions and things to verify against Umbraco
├── data-model.md        # source model, plan model, report model
├── quickstart.md        # end-to-end validation guide
├── contracts/
│   ├── cli.md                   # commands, options, exit codes, run folder
│   ├── kentico-reader.md        # IKenticoReader and the Kentico 11 SQL it relies on
│   ├── site-profile.md          # profile JSON schema
│   ├── umbraco-management-api.md# Management API calls the tool makes
│   └── redirect-endpoint.md     # host endpoint for redirects
├── checklists/requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
KenticoMigrator.slnx
Directory.Build.props                 # net10.0, nullable, warnings as errors
docker-compose.yml                    # azurite; sqlserver under profile "sql"
src/KenticoMigrator/                  # console tool
├── Program.cs                        # config, verb dispatch: counts | plan | migrate
├── appsettings.json                  # placeholders only
├── Kentico/
│   ├── IKenticoReader.cs             # interface + Source* records
│   ├── Kentico11SqlReader.cs         # all Kentico 11 SQL
│   ├── CoupledColumns.cs             # coupled-table column resolution
│   ├── FormDefinition.cs             # ClassFormDefinition XML parser
│   ├── EditableRegions.cs            # DocumentContent XML parser
│   ├── PublishWindow.cs              # IsPublished rule (R5a)
│   ├── MediaPaths.cs                 # media file path under the media root (guarded)
│   └── SafeXml.cs                    # XML loading without DTDs
├── Migration/
│   ├── SiteProfile.cs                # profile JSON model + loader + EditorNames
│   ├── MigrationPlan.cs              # Target* records, SkipCategories, RedirectReasons
│   ├── Keys.cs                       # UUID v5 helper
│   ├── ClaimOrder.cs                 # deterministic order for claiming URL names
│   ├── PlanBuilder.cs                # SourceSite + profile -> MigrationPlan
│   ├── PlanBuilder.Schema.cs         # data types, document types, templates
│   ├── PlanBuilder.Documents.cs      # tree, re-parenting, placements, settings folder
│   ├── PlanBuilder.Media.cs          # media folders and items
│   ├── PlanBuilder.Values.cs         # field values
│   ├── PlanBuilder.Urls.cs           # URL names and old URLs
│   ├── UrlIndex.cs                   # old URL -> target key
│   ├── RichTextRewriter.cs           # AngleSharp link rewriting
│   └── RedirectPlanner.cs            # old vs actual URLs -> redirects
├── Umbraco/
│   ├── UmbracoApi.cs                 # IUmbracoApi (the seam the sync classes call; test fakes implement it) + its ManagementApiClient implementation
│   ├── ManagementApiClient.cs        # token, HTTP, retries, UmbracoOptions
│   ├── ApiModels.cs                  # request/response records actually used
│   ├── ApiErrors.cs                  # safe error text (status or type only)
│   ├── SyncResults.cs                # ItemOutcome, ItemKinds, ItemResult, MissingInSource
│   ├── Fingerprint.cs                # SHA-256 of owned values (R5)
│   ├── MissingInSourceFinder.cs      # FR-015b
│   ├── UmbracoTree.cs                # paged tree walk
│   ├── UmbracoTargetClient.cs        # ITargetTreeClient over the API (count walk)
│   ├── SchemaSync.cs                 # data types, document types, templates
│   ├── MediaSync.cs                  # folders + files via temporary files
│   ├── DocumentSync.cs               # pages: upsert, publish, sort, read URLs
│   ├── RedirectSync.cs               # calls the host endpoint
│   └── Templates/                    # embedded minimal Razor views
└── Run/
    ├── CliArguments.cs               # command line parsing
    ├── RunModel.cs                   # TargetCounts, CountDrop, RunReport, exit codes, Phases
    ├── Counts.cs                     # ITargetTreeClient, TargetCounter, CountGuard
    ├── MigrationRunner.cs            # phases, timings, error collection
    └── Report.cs                     # report.json, report.md, csv files
profiles/
├── sample.json
└── pontoonsonly.json
host/UmbracoSite/                     # Umbraco 17 site (dotnet new umbraco)
├── Program.cs                        # + AddAzureBlobMediaFileSystem, ImageSharp cache, .aspx rewrite
├── appsettings.json / appsettings.Development.json   # no secrets
└── Migration/
    ├── RedirectsController.cs        # POST kentico-migration/redirects
    ├── TemplateLessRedirectFinder.cs # answers a registered redirect for a page without a template
    ├── TemplateLessRedirectComposer.cs  # registers the finder before the URL finder
    ├── ApiClientSeeder.cs            # Development only: API user from config
    └── MediaContainerBootstrapper.cs # Development only: creates the blob container
sample/
├── kentico11-sample.sql              # fictional Kentico 11 database
├── media/<library>/...               # fictional media library files
├── expected-urls.txt                 # old URL -> expected status and target
└── README.md                         # expected counts
scripts/
├── restore-sample.sh                 # runs the sample script in the SQL container
├── check-urls.sh                     # requests each expected URL, prints OK/FAIL
└── check-repo.sh                     # no client material, no em dashes
tests/KenticoMigrator.Tests/
├── Kentico/  Migration/  Umbraco/  Run/  EndToEnd/   # unit + gated integration tests
├── Support/                          # fakes (reader, Umbraco API), source factory
└── Fixtures/                         # invented HTML and XML fixtures
docs/
├── DISCOVERY.md  MAPPING.md  RESULTS.md  REVIEW.md
README.md  LICENSE
```

**Structure Decision**: Three projects (console tool, Umbraco host, tests) as required by
Principle V. The console tool is split by responsibility into `Kentico/` (source, all
version-specific SQL), `Migration/` (pure planning), `Umbraco/` (target I/O) and `Run/`
(orchestration and reporting). The host is a stock `dotnet new umbraco` project with the five classes of `Migration/` added. `docs/superpowers/plans/` is removed once tasks.md exists, so the spec folder is the only
plan.

## Phase Overview

- **Phase 0 (research.md)**: decisions R1 to R10, including the Umbraco formats that must be
  verified against a running host before the code that depends on them is finished.
- **Phase 1 (data-model.md, contracts/, quickstart.md)**: records and their invariants, the five
  contracts, and the end-to-end validation guide.
- **Phase 2 (tasks.md)**: fine-grained tasks grouped by user story, with a foundational phase that
  fixes all shared records and interfaces first so later tracks can run in parallel.

## Complexity Tracking

Items that go beyond the minimum of the constitution or the brief, recorded here as the constitution requires:

| Item | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Custom redirect endpoint in the host | Management API can list redirects but not create them | A rewrite rule file would work but redirects would not be visible or editable in the back office (FR-024) |
| API client seeder in the host (Development only) | Unattended setup of an API user with client credentials for the quickstart | Manual back-office setup stays documented as the fallback, but it breaks the "under 15 minutes" goal (SC-005) |
| Template-less redirect finder in the host (`TemplateLessRedirectFinder` and its composer) | An old alias can equal the URL of a template-less folder (the sample's `/catalog`, the reference site's `/products`); Umbraco answers 404 for a found page without a template before it reaches redirects (MAPPING.md T064) | Giving the folders a template would turn every folder and template-less 404 into a 200 page and expose the hidden settings folder; dropping the redirect would lose a live URL |
| `IUmbracoApi` (a second interface, Constitution V names only `IKenticoReader`) | The sync classes, the runner and the report logic are tested against an in-memory fake (`FakeUmbracoApi`) that checks real request shapes, so the unit suite needs no running host | Calling `ManagementApiClient` directly would force the whole sync test suite to use an HTTP handler stub per test or a running host |
| `ITargetTreeClient` (a third interface) | Runner and count-guard tests script the target snapshots (a count that drops between the first and second snapshot, a snapshot that fails). The same tree endpoint also feeds `MissingInSourceFinder`, so a scripted `IUmbracoApi` could not tell the snapshots apart | Faking `IUmbracoApi` for these tests would script by call order, not by snapshot: the snapshot and the missing-in-source walk share `/tree/document/root`, so the tests would break when either walk changes |
