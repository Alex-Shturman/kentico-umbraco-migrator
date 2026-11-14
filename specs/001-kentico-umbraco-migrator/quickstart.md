# Quickstart: validate the migrator end to end

Runnable scenarios that prove the feature works. Commands assume the repository root, Docker
running, and the .NET 10 SDK. Contracts: [cli](contracts/cli.md),
[redirect endpoint](contracts/redirect-endpoint.md).

## Prerequisites

- A SQL Server 2022 container. Either an existing one (`export SQL_CONTAINER=<its name>`) or
  `docker compose --profile sql up -d sqlserver` (set `MSSQL_SA_PASSWORD` first, then `export SQL_CONTAINER=kentico-migrator-sql`).
  Connection strings below use port 1433 for your own container and 14333 for the compose service.
- An empty target database: `docker exec $SQL_CONTAINER bash -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "CREATE DATABASE PO_Umbraco"'`.
- Azurite: `docker compose up -d azurite`.
- `dotnet new install Umbraco.Templates::17.*` is not needed to run, only to regenerate the host.

## 1. Sample source database

```bash
scripts/restore-sample.sh                       # creates Kentico11Sample from sample/kentico11-sample.sql
```

Expected: the script prints the counts table from `sample/README.md` and they match.

## 2. Target site

The same secret block as in [host/README.md](../../host/README.md) (Secrets) and the README; set the imaging key before the
first start and trust the development certificate once (`dotnet dev-certs https --trust`):

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

Expected: `https://localhost:44317/umbraco` shows the login page; the log says the API client was
created (first start) or already exists.

## 3. Preview

```bash
dotnet user-secrets --project src/KenticoMigrator set "Kentico:ConnectionString" "Server=localhost,1433;Database=Kentico11Sample;User Id=sa;Password=<sa password>;TrustServerCertificate=True;Encrypt=True"
dotnet user-secrets --project src/KenticoMigrator set "Umbraco:ClientSecret" "<the same client secret>"
dotnet run --project src/KenticoMigrator -- plan --profile profiles/sample.json
```

Expected: exit code 0; `runs/<id>/report.md` lists every sample item as "would create", the
excluded application page and the host file as skipped, the wildcard alias as an application
route, and no plan errors. Umbraco is unchanged.

## 4. Migrate

```bash
dotnet run --project src/KenticoMigrator -- migrate --profile profiles/sample.json
```

Expected: exit code 0; the console shows `schema: 15 Created`, `media: 19 Created` (9 folders, 9 images and 1 file), `documents: 23 Created`,
`redirects: 28 planned; 28 created, 0 existing, 0 failed` and `links: 8 rewritten, 1 external, 1 to the application,
1 unresolved`. The site shows the sample pages with their images and files at `https://localhost:44317/media/<token>/<file>`:
the host serves them from the Azurite `media` container (nothing is stored on the host's disk). The back office lists the 28
redirects under "URL Redirect Management".

## 5. Old URLs

```bash
scripts/check-urls.sh https://localhost:44317 sample/expected-urls.txt
```

Expected: every line prints `OK` and the summary says `26 OK, 0 FAIL` (`200` for kept paths and for `/Home`, which is served
at its own URL segment, `301` to the right page for `.aspx`, aliases and the brand with a space in its name; `/Catalog` is an alias that is also the URL of a template-less folder, redirected by the host's `TemplateLessRedirectFinder`).

## 6. Re-run and editor protection

```bash
dotnet run --project src/KenticoMigrator -- migrate --profile profiles/sample.json
```

Expected: report shows 0 created and 0 updated; all counts unchanged.

Then edit one migrated page's title in the back office and run `migrate` again. Expected: that
page is listed as "edited in Umbraco, not updated". Run with `--overwrite-edited`: listed as
"edited in Umbraco, overwritten".

## 7. Count guard

Delete one migrated page in the back office, then run `migrate`. Expected: the page is created
again (the count went up, not down) and exit code 0. The count-drop path (exit code 3) is covered
by the `CountGuard` unit tests and by `MigrationRunnerTests.Count_drop_stops_with_exit_code_3`,
which uses a fake target whose second snapshot is lower than the first.

## 8. Repository hygiene

```bash
scripts/check-repo.sh
```

Expected: `OK: no client material, no em dashes`.

## 9. Reference run (owner machine only)

Same as steps 3 to 6 with `profiles/pontoonsonly.json`, the restored `PO_Kentico` database and
`_work/expected-urls.txt`. Expected numbers: SC-001 in [spec.md](spec.md).
