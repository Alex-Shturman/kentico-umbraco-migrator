# Kentico to Umbraco Migrator

A command-line tool that reads a Kentico 11 site directly from SQL Server and recreates its pages, media and old URLs in
Umbraco 17 through the Management API. Runs are repeatable (a second run changes nothing), never delete anything, and end with a
report that compares source and target counts.

## The problem

A Kentico 11 (Portal Engine, Web Forms) site holds its content in a database, mixed with application data (listings, accounts,
orders). Moving only the editorial content to a clean Umbraco site by hand means copying hundreds of pages, keeping every old
URL alive for search engines, and fixing the links inside the text. Doing it by hand cannot be repeated or checked.

## What the tool does

- Reads pages, editable regions, coupled page-type fields, SKU data, attachments, media libraries and document aliases from
  the Kentico database with SELECT statements only.
- Plans the target in memory from a JSON site profile (page types, paths, excluded sections, re-parenting). The plan is
  deterministic: the same source gives the same plan and the same item keys.
- Creates data types, document types and templates, media items (stored in Azure Blob Storage), pages (published as in the
  source) and native Umbraco redirects, in that order.
- Rewrites links inside rich text to Umbraco links; external links and links to application routes stay as they are and are counted.
- Keeps the old URLs: the Umbraco tree mirrors the Kentico paths, and a redirect is registered only where the old URL cannot be
  served as it is (`.aspx`, aliases, a changed path).
- Writes a report (`report.json`, `report.md`, `redirects.csv`, `skipped.csv`, `items.csv`) with counts, everything skipped and why,
  link statistics and timings.

## Architecture in five lines

1. `Kentico11SqlReader` reads the source into plain records (read-only SQL, no Kentico DLLs).
2. `PlanBuilder` turns the records and the profile into a `MigrationPlan` (pure code, unit tested).
3. `SchemaSync`, `MediaSync`, `DocumentSync` and `RedirectSync` write the plan through `ManagementApiClient` (client credentials).
4. `MigrationRunner` orders the phases, takes count snapshots before and after, stops when a target count drops, and writes the report.
5. A small Umbraco host in `host/` (SQL Server, Azure Blob media, one redirect endpoint) is the target for the sample and the reference run.

## Results on the reference site

A real Kentico 11 site (PontoonsOnly), measured on a freshly installed Umbraco 17. Aggregate numbers only; details and honest
notes are in [docs/RESULTS.md](docs/RESULTS.md).

| | Kentico 11 | Umbraco 17 |
|---|---|---|
| Brand pages | 88 pages and 10 rows without a page | 98 (10 unpublished for review) |
| Articles / products / lookup items | 6 / 3 / 13 | 6 / 3 / 13 |
| Media | 25 attachments | 22 images (2 host files and 1 orphan reported) |
| Links in rich text | 104 in migrated pages | 95 rewritten, 6 external, 3 to the application, 0 unresolved |
| Old URLs checked | 156 | 156 correct |
| Redirects | | 261 |
| Skipped, each with a reason | | 44 |
| Full first run | | 23 s |
| Second run | | 0 created, 0 updated |

## Quickstart with the sample

The sample is an invented kayak shop in a Kentico 11 database that the repository builds from a SQL script. You need the .NET SDK
from `global.json`, Docker with a SQL Server 2022 container, and Node.js only if Docker cannot pull the Azurite image. The full
walk-through with the expected output of every step is [specs/001-kentico-umbraco-migrator/quickstart.md](specs/001-kentico-umbraco-migrator/quickstart.md);
the host setup is in [host/README.md](host/README.md) and the sample in [sample/README.md](sample/README.md).

1. **Services.** Use an existing SQL Server container (`export SQL_CONTAINER=<name>`) or `docker compose --profile sql up -d sqlserver`
   (`export SQL_CONTAINER=kentico-migrator-sql`, set `MSSQL_SA_PASSWORD` first). Start Azurite with `docker compose up -d azurite`, or without
   Docker: `npx --yes azurite@3.34.0 --silent --location "$TMPDIR/azurite" --blobHost 127.0.0.1 --blobPort 10000 --skipApiVersionCheck`.
   Create the empty target database: `docker exec $SQL_CONTAINER bash -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "CREATE DATABASE PO_Umbraco"'`.
2. **Sample source.** `scripts/restore-sample.sh` creates `Kentico11Sample` and prints the counts that
   [sample/README.md](sample/README.md) lists.
3. **Target site.** Set the host secrets once (nothing secret is stored in files), trust the development certificate
   (`dotnet dev-certs https --trust`) and start the host. The first start installs Umbraco into the empty `PO_Umbraco` database
   (about half a minute) and creates the API user. The back office is at `https://localhost:44317/umbraco`. In the connection string use port
   1433 for your own container and 14333 for the compose service.

   ```bash
   cd host/UmbracoSite
   dotnet user-secrets set "ConnectionStrings:umbracoDbDSN" "Server=localhost,1433;Database=PO_Umbraco;User Id=sa;Password=<sa password>;TrustServerCertificate=True;Encrypt=True"
   dotnet user-secrets set "Umbraco:CMS:Unattended:InstallUnattended" "true"
   dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserName" "Migrator Admin"
   dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserEmail" "admin@example.test"
   dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserPassword" "<admin password>"
   dotnet user-secrets set "Migration:ApiClient:Secret" "<client secret>"
   dotnet user-secrets set "Umbraco:CMS:Imaging:HMACSecretKey" "$(openssl rand -base64 64)"
   cd ../..
   dotnet run --project host/UmbracoSite
   ```
4. **Preview.** Set the migrator secrets (`Kentico:ConnectionString` for `Kentico11Sample`, `Umbraco:ClientSecret`, the same value as
   the host's client secret) with `dotnet user-secrets --project src/KenticoMigrator set ...`, then
   `dotnet run --project src/KenticoMigrator -- plan --profile profiles/sample.json`. Nothing is written to Umbraco.
5. **Migrate.** `dotnet run --project src/KenticoMigrator -- migrate --profile profiles/sample.json`.
6. **Check the old URLs.** `scripts/check-urls.sh https://localhost:44317 sample/expected-urls.txt` prints `26 OK, 0 FAIL`.
7. **Run again.** The same `migrate` command reports 0 created and 0 updated. Edit a page title in the back office and run it
   again: the page is listed as edited and left alone; add `--overwrite-edited` to write it again.

## Command line

```text
dotnet run --project src/KenticoMigrator -- <command> --profile <path> [--overwrite-edited] [--run-dir <path>] [--Key=Value]
```

| Command | Effect |
|---|---|
| `counts` | Prints source counts and, when Umbraco is configured, target counts. Writes nothing. |
| `plan` | Builds the plan and shows what a run would create, update or leave alone. Writes nothing. |
| `migrate` | Full run. |

Exit codes: 0 success, 1 finished with failed items, 2 configuration, connection, version or plan error (nothing written),
3 a target count dropped (the run stopped). The contract is [specs/001-kentico-umbraco-migrator/contracts/cli.md](specs/001-kentico-umbraco-migrator/contracts/cli.md).

## Configuration

Keys are read from `appsettings.json` (empty placeholders only), then environment variables with the `KM_` prefix and `__` as
separator, then user secrets (project `KenticoMigrator`), then `--Key=Value` arguments. A later source wins, so a user secret
overrides an environment variable.

| Key | Meaning | Secret |
|---|---|---|
| `Kentico:ConnectionString` | SQL Server connection string of the source (opened read-only) | yes |
| `Kentico:MediaRoot` | Folder of media library files, when the profile does not set `mediaRoot` | no |
| `Umbraco:BaseUrl` | Address of the Umbraco site; default `https://localhost:44317` | no |
| `Umbraco:ClientId` | API user client id (`umbraco-back-office-` prefix); default `umbraco-back-office-migrator` | no |
| `Umbraco:ClientSecret` | API user secret. Setting it enables the target; without it the tool runs plan-only | yes |
| `Umbraco:AllowUntrustedCertificate` | `true` for the local development certificate only | no |

The site profile ([contract](specs/001-kentico-umbraco-migrator/contracts/site-profile.md), example [profiles/sample.json](profiles/sample.json))
holds everything site-specific: own host names, excluded path prefixes, folder paths, application route prefixes, files that
are not uploaded, the page-type mappings with their fields, re-parenting rules and rows that have no page.

## Behaviour on re-runs

- **Editors are protected.** Each written item carries a fingerprint of the values the tool wrote. An item an editor changed in
  Umbraco is not updated and is listed as "edited in Umbraco"; `--overwrite-edited` writes it again.
- **Nothing is deleted or unpublished.** Pages that were migrated earlier and are no longer in the source are listed as "missing in
  source, review manually". Redirects are never removed.
- **Publishing.** A page the run creates (or restores from the recycle bin) is published. A page the run only updates, moves or
  overwrites is published again only when it was published before the write, so a page an editor unpublished stays unpublished
  (an editor may have done it on purpose); such pages are listed for review as "planned as published but not published in
  Umbraco". A page the plan does not publish (for example brand rows without a page) is never published.
- **Media carry no stored fingerprint.** A media item whose name and folder are the planned ones is unchanged; one an editor renamed
  or moved is reported as edited and left alone unless `--overwrite-edited` is given. File bytes are not compared, and media that
  left the source are not listed as missing.
- **Sort order.** The children of a parent are put in the planned order only in a run that creates, updates or overwrites at least
  one child of that parent, and never when a child of that parent was left alone as edited. An order an editor sets is not undone by
  a run that writes nothing under that parent.
- **Redirects** are registered once per old URL and page; a re-run reports them as existing.
- **Count guard.** If a target count is lower after the run than before, the run stops with exit code 3.

The full list of re-run rules (document types, templates, failed publishing) is in the "Notes on re-runs" section of the CLI contract.

## Limitations

- Source: Kentico 11 (database schema 11.0), one site, one culture, Portal Engine pages. Other versions need another reader.
- Only content moves. Templates, layouts, web parts, transformations and CSS are not read; the Umbraco views are minimal.
- One page type can be mapped per Kentico class and path; the profile is JSON, not a user interface.
- The Management API cannot create redirects, so the host adds one endpoint for them; a target without that endpoint gets no redirects.
- Redirects only answer when no page is found at the old URL. An old URL that equals a template-less folder in the new tree is
  answered by the host's `TemplateLessRedirectFinder` (T064, one URL on the reference site); a target site without it answers 404 and the plan warns.
- Publish windows (scheduled publishing) are not applied; pages with a window are listed in the report.
- Rich text is rewritten for links to pages, media and the old brand route. The reference site has no images or attachment links
  in its text, so the media link rewriting is verified by the sample and unit tests only.
- The source is read, never written; the Umbraco database is never written directly.

## Scope extensions

Beyond the brief (content, media and old URLs) the tool also migrates, because the reference site needs them to keep working:

- Lookup items stored as pages (categories, horsepower ranges, listing plans, renewal plans) as template-less pages that keep
  their source id for a later application rebuild.
- The SKU fields of the shop products (number, price, retail price, available items, enabled) as plain product properties.
- Brand rows that have no page in Kentico as unpublished pages for editor review.

## Done per project

Not part of this tool; each project does them for itself:

- Web Forms page templates, web parts and layouts rebuilt as Umbraco views and components.
- Application features: listings, search, user accounts and login, dealers and profiles, e-commerce (cart, orders, payments).
- Forms and their submissions, workflows, permissions and secured sections.
- Files that belong on the new host (feeds, `ads.txt`, theme assets); the report lists the ones named in the profile.

## Development

```bash
dotnet build
dotnet test --filter "Category!=Integration"
scripts/check-repo.sh
```

Integration tests (`dotnet test --filter "Category=Integration"`) need the restored sample database, whose connection string goes
into the environment variable `KENTICO_SAMPLE_CS`, and, for the tests that call the host, a running host with Azurite and the
migrator's `Umbraco:ClientSecret` (user secrets of project `KenticoMigrator`, or the environment variable `Umbraco__ClientSecret`;
`Umbraco__BaseUrl` and `Umbraco__ClientId` are optional and default as above). Tests whose configuration is missing are skipped. The repository guard
`scripts/check-repo.sh` fails when client material or an em dash is tracked. The specification, plan, tasks and contracts are in
[specs/001-kentico-umbraco-migrator/](specs/001-kentico-umbraco-migrator/); the discovery and mapping documents are in
[docs/DISCOVERY.md](docs/DISCOVERY.md) and [docs/MAPPING.md](docs/MAPPING.md).

## License

MIT, see [LICENSE](LICENSE).
