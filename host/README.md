# Umbraco host

Umbraco 17 site the migrator writes to. It runs on `https://localhost:44317`, uses the database
`PO_Umbraco` on the local SQL Server container, stores media in Azure Blob storage (Azurite in
development) and adds one endpoint for redirects.

## Prerequisites

- The .NET SDK from `global.json`, the SQL Server container `oib_sql_container` and an empty database:

  ```bash
  docker exec oib_sql_container bash -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "CREATE DATABASE PO_Umbraco"'
  ```

- The development certificate: `dotnet dev-certs https --trust`.
- Azurite on port 10000 (next section).

## Azurite

Option 1, Docker (the image tag is pinned in `docker-compose.yml`):

```bash
docker compose up -d azurite
```

Option 2, npm, when `docker pull` from `mcr.microsoft.com` hangs (Docker Desktop proxy). Equivalent for the host:

```bash
npx --yes azurite@3.34.0 --silent --location "$TMPDIR/azurite" --blobHost 127.0.0.1 --blobPort 10000 --skipApiVersionCheck
```

Check: `curl -s -o /dev/null -w '%{http_code}\n' 'http://127.0.0.1:10000/devstoreaccount1?comp=list'` prints `403` (or `200`).

The host creates the `media` container on start in Development. Anonymous container listing is
refused by Azurite, so to see the uploaded blobs open an image URL through the host
(`https://localhost:44317/media/<token>/<file>`) or list with a shared-key signed request.

## Secrets

Nothing secret is stored in files. Set these with `dotnet user-secrets` (the host has its own
`UserSecretsId`):

```bash
cd host/UmbracoSite
dotnet user-secrets set "ConnectionStrings:umbracoDbDSN" "Server=localhost,1433;Database=PO_Umbraco;User Id=sa;Password=<sa password>;TrustServerCertificate=True;Encrypt=True"
dotnet user-secrets set "Umbraco:CMS:Unattended:InstallUnattended" "true"
dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserName" "Migrator Admin"
dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserEmail" "admin@example.test"
dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserPassword" "<generated admin password>"
dotnet user-secrets set "Migration:ApiClient:Secret" "<generated client secret>"
dotnet user-secrets set "Umbraco:CMS:Imaging:HMACSecretKey" "<random base64, for example from: openssl rand -base64 64>"
```

Set the imaging key before the first start. If it is missing, Umbraco generates one and writes it
into `appsettings.json`, which must never be committed.

On the first start against an empty database Umbraco installs itself and creates the admin user
from the `Unattended` keys.

The same client secret goes to the migrator console:

```bash
cd src/KenticoMigrator
dotnet user-secrets set "Umbraco:ClientSecret" "<the same client secret>"
```

## API user (Development only)

On start, `Migration/ApiClientSeeder.cs` creates the API user `Kentico migrator` (administrators
group) with client id `Migration:ApiClient:Id` (default `umbraco-back-office-migrator`; Umbraco
requires the `umbraco-back-office-` prefix) and the secret above. It does nothing when the client id
already exists (it logs a warning, and a changed secret is not applied), attaches the credentials to
an API user that an interrupted earlier start left without them, and does nothing outside the
Development environment.

To rotate the secret: delete the API user `Kentico migrator` in the back office (Users), change
`Migration:ApiClient:Secret` in both user-secrets stores, and restart the host.

Token check:

```bash
curl -sk -d grant_type=client_credentials -d client_id=umbraco-back-office-migrator \
  --data-urlencode "client_secret=$SECRET" \
  https://localhost:44317/umbraco/management/api/v1/security/back-office/token
```

For any other environment create the API user by hand: back office, Users, Create, API User, then
add its client id and secret there.

## Run

```bash
dotnet run --project host/UmbracoSite
```

Back office: `https://localhost:44317/umbraco`. Swagger: `https://localhost:44317/umbraco/swagger`.

## Redirect endpoint

`POST /umbraco/management/api/v1/kentico-migration/redirects`, authorised like the Management API
(Content section access). The contract is
`specs/001-kentico-umbraco-migrator/contracts/redirect-endpoint.md`. Two behaviours matter:

- URLs are stored percent-decoded, lower-cased and without a trailing slash, because Umbraco looks
  redirects up that way.
- `GET` and `HEAD` requests ending in `.aspx` (outside `/umbraco`) are given a trailing slash by a
  middleware in `Program.cs`; without it they never reach the redirect finder.

The host has no test project. The endpoint rules (401 without a token, the 500 entry limit,
idempotency, the `failed` reasons) are verified by this manual acceptance check, with the host
running and a published page `<key>`:

```bash
# 401 without a token
curl -sk -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/json' -d '[]' \
  https://localhost:44317/umbraco/management/api/v1/kentico-migration/redirects
# register, then repeat the same call: created 1 the first time, existing 1 the second time
curl -sk -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '[{"url":"/test-old","contentKey":"<key>"}]' \
  https://localhost:44317/umbraco/management/api/v1/kentico-migration/redirects
# 301 with Location set to the page URL
curl -skI https://localhost:44317/test-old
```
