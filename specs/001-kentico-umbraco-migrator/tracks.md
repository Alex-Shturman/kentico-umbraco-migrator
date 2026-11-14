# Parallel Development Tracks

> Source plan: `plan.md`, task list: `tasks.md` (T001 to T063)
> Generated: 2026-11-14
> Total tracks: 6 (Track 0 to Track 5)

Every track works on its own files. A file that several stories touch has exactly one owning
track (see "File ownership"); other tracks do not edit it.

## Dependency Graph

```text
Wave 0   Track 0  Foundation (T001-T010)
            |
Wave 1   +--------------------+----------------------+----------------------+
         | Track 1 Host + API | Track 2 Source       | Track 3 Pure core    |
         | T013-T019, T049    | T011,T012,T020-T024, | T025-T027,T032,T034, |
         |                    | T039,T040,T044,T050, | T036,T041,T045,T046, |
         |                    | T053                 | T051,T052            |
         +---------+----------+-----------+----------+-----------+----------+
                   |  T016,T018 fixtures  |  T023 reader         |  T026,T027 plan
                   |  needed by T032,T046 |                      |
Wave 2                      Track 4 Sync and runner
                            T028-T031,T033,T035,T037,T038,T042,T043,T047,T048,T054,T055
                                         |
Wave 3                      Track 5 Reference run, docs, release (T056-T063)
```

- Track 0 runs alone and blocks everything.
- Tracks 1, 2, 3 run in parallel after Track 0.
- Inside Wave 1, Track 3 pauses before T032 and T046 until Track 1 has committed the T018
  fixtures (`tests/KenticoMigrator.Tests/Fixtures/umbraco/`). Everything else in Track 3 is
  independent.
- Track 4 starts after Tracks 1 and 3 are done and Track 2 has finished T024 (reader for pages).
  Track 4's end-to-end tasks for media, links and URLs (T043, T048, T055) need Track 2's T040,
  T044, T050, T053.
- Track 5 starts after Track 4.

## File ownership

| Path | Owner |
|---|---|
| `KenticoMigrator.slnx`, `Directory.Build.props`, `docker-compose.yml`, `.gitignore`, `scripts/check-repo.sh` | Track 0 (Track 1 adds the host project to the slnx in T013) |
| `src/KenticoMigrator/Kentico/IKenticoReader.cs`, `Migration/MigrationPlan.cs`, `Migration/SiteProfile.cs`, `Migration/Keys.cs`, `Umbraco/SyncResults.cs`, `Run/RunModel.cs` | Track 0 (later tracks may add members, never rename) |
| `host/**`, `src/KenticoMigrator/Umbraco/ManagementApiClient.cs`, `tests/.../Fixtures/umbraco/**`, `research.md` R4/R6/R7/R9/R10 | Track 1 |
| `sample/**`, `scripts/restore-sample.sh`, `src/KenticoMigrator/Kentico/**` (except the interface file) | Track 2 |
| `profiles/sample.json`, `src/KenticoMigrator/Migration/**` (except Track 0 files), `Umbraco/Fingerprint.cs`, `Run/Counts.cs`, `Run/Report.cs` | Track 3 |
| `src/KenticoMigrator/Umbraco/*Sync.cs`, `ApiModels.cs`, `MissingInSourceFinder.cs`, `Templates/**`, `Run/MigrationRunner.cs`, `Program.cs`, `tests/.../EndToEnd/**`, `scripts/check-urls.sh` | Track 4 |
| `profiles/pontoonsonly.json`, `docs/**`, `README.md` | Track 5 |

Exception: T039 (Track 2) needs `notUploadedPaths` and `mediaRoot` in `profiles/sample.json`.
Track 2 edits only those two keys, after Track 3 has committed T025.

## Rules for every track

- Read `.specify/memory/constitution.md`, `spec.md`, `plan.md`, `data-model.md` and the
  contracts the task links before coding. Task text in `tasks.md` is the source of truth.
- Test first for pure code (Constitution IV): write the test, run it, see it fail, implement.
- Each task ends with its **Check** and one commit (Conventional Commits). Tick the task's box in
  `tasks.md` in the same commit. Never leave `dotnet build` broken or with warnings.
- Privacy (Constitution I, NON-NEGOTIABLE): never read, copy or quote anything from `Sources/`,
  `sources/` or `_work/` into the repository, tests, fixtures, commit messages or reports. Only
  aggregate counts may appear. Sample data is invented (a kayak shop).
- No secrets in the repo: connection strings and passwords go to `dotnet user-secrets` or
  environment variables. Never print the SQL SA password; inside the container use
  `docker exec <container> bash -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" ...'`.
- No em dashes (U+2014) in any file or commit message. `scripts/check-repo.sh` must stay OK.
- Never write to the Kentico database; never write to the Umbraco database directly (only the
  Management API and the host redirect endpoint).
- Local services: SQL Server container `oib_sql_container` (restored reference database
  `PO_Kentico`; create `Kentico11Sample` and `PO_Umbraco` there), Azurite from
  `docker-compose.yml`. Docker commands need the sandbox disabled.
- Umbraco 17 APIs: confirm names with Context7 or the official docs before coding; do not rely on
  memory.

---

## Track 0: Foundation

**Skills:** `/superpowers:test-driven-development`, `/superpowers:verification-before-completion`
**Dependencies:** none (start immediately)
**Scope:** buildable solution, local services, repository guard, shared records and the reader
interface, so that later tracks can compile against them.
**Files likely affected:** `KenticoMigrator.slnx`, `Directory.Build.props`, `src/KenticoMigrator/`,
`tests/KenticoMigrator.Tests/`, `docker-compose.yml`, `scripts/check-repo.sh`, `README.md`,
`.gitignore`, `docs/superpowers/` (removed).

### Tasks
1. T001 solution, props, console and test projects.
2. T002 packages and placeholder `appsettings.json`.
3. T003 `docker-compose.yml` (Azurite, optional SQL Server).
4. T004 `scripts/check-repo.sh`.
5. T005 remove `docs/superpowers/`, README stub.
6. T006 source records and `IKenticoReader`.
7. T007 `SiteProfile` with validation (test first).
8. T008 plan records.
9. T009 `Keys` UUID v5 (test first, known vector from Python `uuid.uuid5`).
10. T010 sync and run records, exit codes.

### Agent Prompt
> You are implementing Track 0 (Foundation) of the Kentico to Umbraco migrator in
> `/Users/alekseyshturman/RiderProjects/PO_migration`. Read `specs/001-kentico-umbraco-migrator/tracks.md`
> ("Rules for every track" and "File ownership"), then `tasks.md` tasks T001 to T010, plus
> `.specify/memory/constitution.md`, `plan.md` (Project Structure), `data-model.md` and
> `contracts/kentico-reader.md`, `contracts/site-profile.md`, `contracts/cli.md`.
> Do T001 to T010 in order (T002 to T005 and T006 to T010 may be done in any order after T001).
> Use `/superpowers:test-driven-development` for T007 and T009. Records must match
> `data-model.md` exactly so other tracks can compile against them. After each task run its
> Check, tick its box in `tasks.md`, and commit. Finish with
> `/superpowers:verification-before-completion`: `dotnet build` 0 warnings,
> `dotnet test --filter "Category!=Integration"` green, `scripts/check-repo.sh` OK. Report the
> commits made and anything you had to decide that the docs did not cover.

---

## Track 1: Umbraco host and Management API client

**Skills:** `/feature-dev:feature-dev`, `/superpowers:test-driven-development`, `/context7-mcp`,
`/superpowers:requesting-code-review`, `/superpowers:verification-before-completion`
**Dependencies:** Track 0
**Scope:** Umbraco 17 site with Azure Blob media and an API user, the HTTP client, verified JSON
shapes for every property editor the tool writes, routing facts, and the redirect endpoint.
**Files likely affected:** `host/UmbracoSite/**`, `host/README.md`,
`src/KenticoMigrator/Umbraco/ManagementApiClient.cs`, `tests/KenticoMigrator.Tests/Umbraco/ManagementApiClient*Tests.cs`,
`tests/KenticoMigrator.Tests/Fixtures/umbraco/*.json`, `specs/001-kentico-umbraco-migrator/research.md`.

### Tasks
1. T013 host from `dotnet new umbraco`, port 44317, unattended install into `PO_Umbraco`.
2. T014 Azure Blob media (Azurite).
3. T015 `ApiClientSeeder` (Development only).
4. T016 `ManagementApiClient` (test first with fake handler).
5. T017 integration test for token and root.
6. T018 verified editor value shapes and fixtures (R6, R7, R9, R10).
7. T019 routing facts (case, `.aspx`, redirect route format).
8. T049 `RedirectsController` per `contracts/redirect-endpoint.md`.

### Agent Prompt
> You are implementing Track 1 (Umbraco host and Management API client) of the Kentico to Umbraco
> migrator in `/Users/alekseyshturman/RiderProjects/PO_migration`. Track 0 is merged. Read
> `specs/001-kentico-umbraco-migrator/tracks.md` (rules, file ownership), then `tasks.md` tasks
> T013 to T019 and T049, `research.md` (R4, R6 to R10), `contracts/umbraco-management-api.md`,
> `contracts/redirect-endpoint.md`, `quickstart.md` step 2.
> Before writing Umbraco code, look up current Umbraco 17 APIs with `/context7-mcp` (API users and
> client credentials, `IRedirectUrlService`, Management API controller base and authorisation,
> `Umbraco.StorageProviders.AzureBlob`). Use `/feature-dev:feature-dev` for the host work and
> `/superpowers:test-driven-development` for T016.
> The SQL Server is the existing container `oib_sql_container`; create the empty database
> `PO_Umbraco` there. Put the connection string, admin password and API client secret in
> `dotnet user-secrets` only, never in files or output. Start Azurite with
> `docker compose up -d azurite`.
> T018: instead of clicking in the back office you may create the test data type, document type,
> page and media through the Management API (or the Playwright MCP browser tools); the fixtures
> must contain invented content only. Write the confirmed shapes into research.md and replace
> every "**Verify**" for R6, R7, R9, R10. Commit the T018 fixtures as soon as they are ready,
> because Track 3 is waiting for them.
> After each task run its Check, tick it in `tasks.md`, commit. End with
> `/superpowers:requesting-code-review` and `/superpowers:verification-before-completion`. Leave
> the host stoppable (do not leave it running). Report: commits, the verified shapes in short, the
> routing results, and anything that contradicts the spec.

---

## Track 2: Source side (sample database and Kentico 11 reader)

**Skills:** `/feature-dev:feature-dev`, `/superpowers:test-driven-development`,
`/superpowers:verification-before-completion`
**Dependencies:** Track 0. T039 waits for Track 3's T025 (profile file exists).
**Scope:** the fictional Kentico 11 sample database built by SQL, the restore script, the XML
parsers and `Kentico11SqlReader` for pages, attachments, media libraries and aliases.
**Files likely affected:** `sample/kentico11-sample.sql`, `sample/README.md`, `sample/media/**`,
`sample/expected-urls.txt`, `scripts/restore-sample.sh`, `src/KenticoMigrator/Kentico/*.cs`,
`tests/KenticoMigrator.Tests/Kentico/**`, `tests/KenticoMigrator.Tests/Fixtures/kentico/**`.

### Tasks
1. T011 sample SQL part 1 (schema, settings, site, base classes, root).
2. T012 `scripts/restore-sample.sh` and `sample/README.md`.
3. T021 `FormDefinition` parser (test first).
4. T022 `EditableRegions` parser (test first).
5. T020 sample SQL part 2 (pages).
6. T023 `Kentico11SqlReader` documents part.
7. T024 reader integration tests.
8. T039 sample SQL part 3 (media) plus `sample/media/**`.
9. T040 reader: attachments, media libraries, file streams.
10. T044 sample SQL part 4 (rich text links).
11. T050 reader: aliases.
12. T053 sample aliases and `sample/expected-urls.txt`.

### Agent Prompt
> You are implementing Track 2 (source side) of the Kentico to Umbraco migrator in
> `/Users/alekseyshturman/RiderProjects/PO_migration`. Track 0 is merged. Read
> `specs/001-kentico-umbraco-migrator/tracks.md` (rules, file ownership), then `tasks.md` tasks
> T011, T012, T020 to T024, T039, T040, T044, T050, T053, plus `contracts/kentico-reader.md`,
> `data-model.md` section 1, `research.md` R5a and R11, `docs/MAPPING.md`.
> The sample is an invented kayak shop. It must look like a real Kentico 11 database (same table
> and column names and types, real `ClassFormDefinition` and `DocumentContent` XML formats) but
> you must NOT copy anything from `Sources/`, `_work/` or the `PO_Kentico` database. You may look
> at `PO_Kentico` only to learn column types and XML structure (schema), never to copy rows,
> names or text. Run SQL with the container's own `$MSSQL_SA_PASSWORD` inside `docker exec` on
> `oib_sql_container` (set `SQL_CONTAINER=oib_sql_container`); never print it. The reader is
> SELECT only with `ApplicationIntent=ReadOnly`.
> Use `/superpowers:test-driven-development` for the parsers. Integration tests use
> `[Trait("Category","Integration")]` and skip when `KENTICO_SAMPLE_CS` is not set; set it only in
> your shell environment for running them. T039 needs `profiles/sample.json`, which Track 3 owns:
> wait until it exists in `main`, then edit only `notUploadedPaths` and `mediaRoot`. Generate the
> sample images with a throwaway script in `$TMPDIR`, not in the repo.
> After each task run its Check, tick it in `tasks.md`, commit. End with
> `/superpowers:verification-before-completion` (build, unit tests, integration tests against a
> freshly restored sample, `scripts/check-repo.sh`). Report commits and the final expected counts.

---

## Track 3: Pure core (planning, links, redirects, fingerprint, counts, report)

**Skills:** `/superpowers:test-driven-development`, `/feature-dev:feature-dev`,
`/superpowers:requesting-code-review`, `/superpowers:verification-before-completion`
**Dependencies:** Track 0. T032 and T046 wait for Track 1's T018 fixtures.
**Scope:** everything that is pure and unit-tested: the sample profile, `PlanBuilder` (schema,
documents, media, old URLs), `UrlIndex`, `RichTextRewriter`, `RedirectPlanner`, `Fingerprint`,
`CountGuard`, `Report`.
**Files likely affected:** `profiles/sample.json`, `src/KenticoMigrator/Migration/PlanBuilder.cs`,
`UrlIndex.cs`, `RichTextRewriter.cs`, `RedirectPlanner.cs`, `src/KenticoMigrator/Umbraco/Fingerprint.cs`,
`src/KenticoMigrator/Run/Counts.cs`, `src/KenticoMigrator/Run/Report.cs`,
`tests/KenticoMigrator.Tests/Migration/**`, `tests/KenticoMigrator.Tests/Run/{CountGuard,Report}Tests.cs`,
`tests/KenticoMigrator.Tests/Umbraco/FingerprintTests.cs`.

### Tasks
1. T025 `profiles/sample.json`.
2. T026 `PlanBuilder` schema part.
3. T027 `PlanBuilder` documents part.
4. T041 `PlanBuilder` media part.
5. T051 `PlanBuilder` old URLs.
6. T045 `UrlIndex`.
7. T052 `RedirectPlanner` (use T019's case-sensitivity result if available, otherwise the
   default test `No_redirect_when_only_case_differs`, and flag it).
8. T034 `CountGuard` and `TargetCounter` (the counter takes the client by interface; Track 4 wires
   it).
9. T036 `Report`.
10. T032 `Fingerprint` (after T018 fixtures exist).
11. T046 `RichTextRewriter` (after T018 fixtures exist).

### Agent Prompt
> You are implementing Track 3 (pure core) of the Kentico to Umbraco migrator in
> `/Users/alekseyshturman/RiderProjects/PO_migration`. Track 0 is merged. Read
> `specs/001-kentico-umbraco-migrator/tracks.md` (rules, file ownership), then `tasks.md` tasks
> T025, T026, T027, T032, T034, T036, T041, T045, T046, T051, T052, plus `data-model.md`,
> `contracts/site-profile.md`, `contracts/cli.md` (run folder), `research.md` R3, R5, R6, and
> `docs/MAPPING.md` sections 2 to 6.
> Use `/superpowers:test-driven-development` for every task: tests build `SourceSite` objects in
> memory with invented data; nothing here touches a database or HTTP. Plans must be deterministic
> (same input, same output, stable keys from `Keys`). Do the tasks in the order listed in
> tracks.md. T032 and T046 depend on Umbraco JSON shapes that Track 1 commits as fixtures in
> `tests/KenticoMigrator.Tests/Fixtures/umbraco/` (task T018) and documents in `research.md` R6;
> do them last and, if the fixtures are not in `main` yet, stop and report instead of guessing the
> shapes. `TargetCounter` must depend on a small interface over the API client so it is testable
> without HTTP; do not create `ManagementApiClient` yourself (Track 1 owns it).
> After each task run its Check, tick it in `tasks.md`, commit. End with
> `/superpowers:requesting-code-review` and `/superpowers:verification-before-completion`. Report
> commits and any spec ambiguity you resolved.

---

## Track 4: Sync to Umbraco and the runner

**Skills:** `/feature-dev:feature-dev`, `/superpowers:test-driven-development`,
`/superpowers:systematic-debugging` (for end-to-end failures), `/superpowers:requesting-code-review`,
`/superpowers:verification-before-completion`
**Dependencies:** Tracks 0, 1, 3, and Track 2 up to T024. T043 needs T039 and T040; T048 needs
T044; T055 needs T050 and T053 (all Track 2).
**Scope:** everything that writes to Umbraco and the CLI: schema sync, document sync with
fingerprints and editor protection, media sync, redirect sync, missing-in-source, the runner with
count guard and report, `Program.cs`, end-to-end tests, `check-urls.sh`.
**Files likely affected:** `src/KenticoMigrator/Umbraco/{SchemaSync,DocumentSync,MediaSync,RedirectSync,MissingInSourceFinder,ApiModels}.cs`,
`src/KenticoMigrator/Umbraco/Templates/*.cshtml`, `src/KenticoMigrator/Run/MigrationRunner.cs`,
`src/KenticoMigrator/Program.cs`, `tests/KenticoMigrator.Tests/Umbraco/*SyncTests.cs`,
`tests/KenticoMigrator.Tests/Run/MigrationRunnerTests.cs`, `tests/KenticoMigrator.Tests/EndToEnd/**`,
`scripts/check-urls.sh`.

### Tasks
1. T028 `SchemaSync` and templates.
2. T029 `DocumentSync` basic upsert.
3. T030 `Program.cs` and minimal runner.
4. T031 pages end-to-end.
5. T033 fingerprint outcomes and preview in `DocumentSync`.
6. T035 `MissingInSourceFinder`.
7. T037 full runner (snapshots, guard, report, `plan`, `counts`, options).
8. T038 re-run end-to-end.
9. T042 `MediaSync` and media picker values.
10. T043 media end-to-end.
11. T047 rich text rewriting in `DocumentSync`, link stats in the report.
12. T048 links end-to-end.
13. T054 URL read-back and `RedirectSync`.
14. T055 `scripts/check-urls.sh` and URLs end-to-end.

### Agent Prompt
> You are implementing Track 4 (sync to Umbraco and the runner) of the Kentico to Umbraco migrator
> in `/Users/alekseyshturman/RiderProjects/PO_migration`. Tracks 0, 1 and 3 are merged and Track 2
> has finished at least T024. Read `specs/001-kentico-umbraco-migrator/tracks.md` (rules, file
> ownership), then `tasks.md` tasks T028 to T031, T033, T035, T037, T038, T042, T043, T047, T048,
> T054, T055, plus `contracts/cli.md`, `contracts/umbraco-management-api.md`,
> `contracts/redirect-endpoint.md`, `research.md` R5 to R10 (now verified by Track 1),
> `quickstart.md`, and the fixtures in `tests/KenticoMigrator.Tests/Fixtures/umbraco/`.
> All request bodies must follow the verified fixture shapes, not memory. Unit tests use a fake
> client; end-to-end tests are `[Trait("Category","Integration")]` and need the host running
> (`dotnet run --project host/UmbracoSite`, secrets already in user-secrets), Azurite, and the
> sample restored with `scripts/restore-sample.sh` (`SQL_CONTAINER=oib_sql_container`). For a
> clean end-to-end run, drop and recreate `PO_Umbraco` and let the host reinstall; never write to
> it with SQL otherwise. No code path may delete or unpublish content (FR-015b).
> Do the tasks in the listed order. Before T043, T048 and T055 check that the Track 2 tasks they
> need are in `main`; if not, do the unit-level tasks first and come back. Use
> `/superpowers:systematic-debugging` when an end-to-end test fails. After each task run its Check,
> tick it in `tasks.md`, commit. End with `/superpowers:requesting-code-review` and
> `/superpowers:verification-before-completion`: all unit and integration tests green, quickstart
> steps 3 to 7 behave as written. Stop the host when done. Report commits and the sample run
> numbers (created, unchanged, skipped by category, redirects).

---

## Track 5: Reference run, docs and release

**Skills:** `/superpowers:verification-before-completion`, `/superpowers:requesting-code-review`,
`/code-review:code-review`
**Dependencies:** Tracks 0 to 4.
**Scope:** the PontoonsOnly profile, the reference migration with honest numbers, README and
RESULTS, fresh-clone check, constitution review, push.
**Files likely affected:** `profiles/pontoonsonly.json`, `docs/RESULTS.md`, `docs/MAPPING.md`,
`docs/REVIEW.md`, `README.md`, `_work/expected-urls.txt` (git-ignored).

### Tasks
1. T056 `profiles/pontoonsonly.json`, `plan` with no errors.
2. T057 reference run twice, URL check, compare with SC-001/003/004/006.
3. T058 `docs/RESULTS.md` (aggregate numbers only).
4. T059 `README.md`.
5. T060 fresh-clone validation under 15 minutes.
6. T061 `docs/MAPPING.md` updates.
7. T062 constitution review.
8. T063 full suite, quickstart, push.

### Agent Prompt
> You are implementing Track 5 (reference run, docs and release) of the Kentico to Umbraco migrator
> in `/Users/alekseyshturman/RiderProjects/PO_migration`. Tracks 0 to 4 are merged and the sample
> quickstart passes. Read `specs/001-kentico-umbraco-migrator/tracks.md` (rules), then `tasks.md`
> tasks T056 to T063, `spec.md` success criteria, `docs/DISCOVERY.md`, `docs/MAPPING.md`.
> The reference database `PO_Kentico` in `oib_sql_container` is client property: the profile may
> contain section-level paths and page type names only; RESULTS.md and README may contain
> aggregate numbers only (counts, sizes, timings). Never quote content, names of people, listings
> or user data. Run against a freshly reinstalled `PO_Umbraco`. Put the generated expected-URL
> list in `_work/` (git-ignored). If numbers differ from SC-001, find the cause, record it
> honestly in RESULTS.md, and add a follow-up task to `tasks.md` instead of hiding it. README in
> English, no em dashes. Run `scripts/check-repo.sh` before every commit. Use
> `/code-review:code-review` for T062. Push to `origin main` only in T063 after everything is
> green. End with `/superpowers:verification-before-completion`. Report the reference numbers,
> timings, and the list of deviations from the spec.
