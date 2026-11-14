# Contract: Umbraco Management API calls made by the tool

Base path `/umbraco/management/api/v1`. All calls carry `Authorization: Bearer <token>`. Exact
request and response shapes are taken from the running host's Swagger (`/umbraco/swagger`) in the
task that implements each call; `Umbraco/ApiModels.cs` keeps only the fields the tool uses.
All shapes below were verified against Umbraco 17.8.1 (research R6 to R10).

| Purpose | Method and path | Notes |
|---|---|---|
| Token | `POST /security/back-office/token` (form: `grant_type=client_credentials`, `client_id`, `client_secret`) | Cache until 30 s before `expires_in`; retry once on 401 |
| Find built-in data types | `GET /filter/data-type?skip=0&take=100&name=<name>` (also `GET /item/data-type/search?query=`) | `items[] { id, name, editorAlias, editorUiAlias }`; compare `name` exactly. No built-in Decimal exists (R7) |
| Data type | `GET /data-type/{id}`, `POST /data-type`, `PUT /data-type/{id}` | Only for tool-created types (Decimal, dropdowns) |
| Document type | `GET /document-type/{id}`, `POST /document-type`, `PUT /document-type/{id}` | Compositions and allowed children by key |
| Template | `GET /template/{id}`, `POST /template`, `PUT /template/{id}` | Razor content from embedded views |
| Temporary file | `POST /temporary-file` (multipart: `Id`, `File`) | Used for every media upload (R9) |
| Media | `GET /media/{id}`, `POST /media`, `PUT /media/{id}`, `GET /tree/media/children?parentId=`, `GET /tree/media/root` | `umbracoFile` value is `{ "temporaryFileId": "<id>" }` for `Image` and `File` (R9) |
| Document | `GET /document/{id}`, `POST /document`, `PUT /document/{id}` | Invariant variant `{ culture: null, segment: null, name }` |
| Media type lookup | `GET /item/media-type/search?query=<Folder\|Image\|File>` | Finds the built-in media type key by exact name |
| Media URLs | `GET /media/urls?id=<id>&id=<id>` | `[ { id, urlInfos: [ { url } ] } ]`; the first url becomes the image source in rich text |
| Move | `PUT /document/{id}/move`, `PUT /media/{id}/move` with `{ target: { id } or null }` | Puts an item an editor moved back under its planned parent |
| Recycle bin restore | `PUT /recycle-bin/document/{id}/restore`, `PUT /recycle-bin/media/{id}/restore` with `{ target: { id } or null }` | An item an editor trashed is restored (never recreated), counts as `Created` |
| Tree ancestors | `GET /tree/document/ancestors?descendantId=<id>`, `GET /tree/media/ancestors?descendantId=<id>` | The last entry is the item itself; its `parent.id` is the actual parent, used to detect an item an editor moved |
| Publish | `PUT /document/{id}/publish` with `publishSchedules: [{ culture: null }]` | Only after `Created`/`Updated` with `Publish = true` |
| Sort | `PUT /document/sort` | Only when order differs |
| Read URLs | `GET /document/urls?id=<id>&id=<id>` | `[ { id, urlInfos: [ { culture, url, provider, isExternal, message } ] } ]`; `url` is relative with a trailing slash (R10) |
| Tree walk (missing in source, counts) | `GET /tree/document/root`, `GET /tree/document/children?parentId=&skip=&take=` | Paged, 100 per page |
| Redirects (count only) | `GET /redirect-management?skip=0&take=1` | `{ total, items[] { id, originalUrl, destinationUrl, created, document: { id }, culture } }`; `total` used in `TargetCounts` |
| Redirects (create) | Host endpoint, see [redirect-endpoint.md](redirect-endpoint.md) | Not part of the Management API |

## Error handling

- 401: refresh token, retry once, then fail the run with exit code 2. A 4xx from the token endpoint
  is the same failure (credentials refused); a 5xx or network error on the token endpoint is
  retried like any other call.
- 404 on `GET` by key: item does not exist (normal path to create). An empty body or JSON `null`
  on a `GET` is treated as "no item" too; a body that is not valid JSON is an error naming the path.
- 400/422 on a write: `ItemResult` with `Failed` and the response's `title`/`detail`; continue.
- 5xx or network error on `GET`, `PUT` and `DELETE`: retry up to 2 times with 1 s and 3 s delay,
  then `Failed`. A `POST` is **not** retried after the request may have reached the host (a 5xx, a
  reset or a timeout could hide a created item); it is retried only when the connection could not be
  established. A failed create is found again by key on the next run (R5). If the token or the
  first schema call fails, exit code 2 before any write.
- The untrusted-certificate setting (`Umbraco:AllowUntrustedCertificate`) is honoured only for the
  loopback host (`localhost`, `127.0.0.1`, `::1`) of `Umbraco:BaseUrl`.

## Other endpoints seen on 17.8.1

`PUT /document/{id}/update-and-publish`, `POST /document/create-and-publish`,
`PUT /document/{id}/sort-children` and `PUT /document/root/sort-children` exist besides the calls
above; the tool uses only the calls listed in the table.
