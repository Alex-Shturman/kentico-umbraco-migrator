# Contract: Umbraco Management API calls made by the tool

Base path `/umbraco/management/api/v1`. All calls carry `Authorization: Bearer <token>`. Exact
request and response shapes are taken from the running host's Swagger (`/umbraco/swagger`) in the
task that implements each call; `Umbraco/ApiModels.cs` keeps only the fields the tool uses.
Items marked **Verify** must be confirmed against the host (research R6 to R10).

| Purpose | Method and path | Notes |
|---|---|---|
| Token | `POST /security/back-office/token` (form: `grant_type=client_credentials`, `client_id`, `client_secret`) | Cache until 30 s before `expires_in`; retry once on 401 |
| Find built-in data types | `GET /filter/data-type?name=<name>` or `GET /item/data-type/search?query=<name>` | **Verify** which exists in 17 |
| Data type | `GET /data-type/{id}`, `POST /data-type`, `PUT /data-type/{id}` | Only for tool-created types (Decimal, dropdowns) |
| Document type | `GET /document-type/{id}`, `POST /document-type`, `PUT /document-type/{id}` | Compositions and allowed children by key |
| Template | `GET /template/{id}`, `POST /template`, `PUT /template/{id}` | Razor content from embedded views |
| Temporary file | `POST /temporary-file` (multipart: `Id`, `File`) | Used for every media upload (R9) |
| Media | `GET /media/{id}`, `POST /media`, `PUT /media/{id}`, `GET /tree/media/children?parentId=` | `umbracoFile` value from temporary file id (**Verify**) |
| Document | `GET /document/{id}`, `POST /document`, `PUT /document/{id}` | Invariant variant `{ culture: null, segment: null, name }` |
| Publish | `PUT /document/{id}/publish` with `publishSchedules: [{ culture: null }]` | Only after `Created`/`Updated` with `Publish = true` |
| Sort | `PUT /document/sort` | Only when order differs |
| Read URLs | `GET /document/urls?id=<id>&id=<id>` | **Verify** path and shape in 17 (R10) |
| Tree walk (missing in source, counts) | `GET /tree/document/root`, `GET /tree/document/children?parentId=&skip=&take=` | Paged, 100 per page |
| Redirects (count only) | `GET /redirect-management?skip=0&take=1` | `total` used in `TargetCounts` |
| Redirects (create) | Host endpoint, see [redirect-endpoint.md](redirect-endpoint.md) | Not part of the Management API |

## Error handling

- 401: refresh token, retry once, then fail the run with exit code 2.
- 404 on `GET` by key: item does not exist (normal path to create).
- 400/422 on a write: `ItemResult` with `Failed` and the response's `title`/`detail`; continue.
- 5xx or network error: retry up to 2 times with 1 s and 3 s delay, then `Failed`; if the token or
  the first schema call fails, exit code 2 before any write.
