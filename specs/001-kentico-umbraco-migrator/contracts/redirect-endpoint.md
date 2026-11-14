# Contract: Host redirect endpoint

Added to `host/UmbracoSite` because the Management API cannot create redirects (research R4).

```
POST /umbraco/management/api/v1/kentico-migration/redirects
Authorization: Bearer <same token as the Management API>
Content-Type: application/json

[
  { "url": "/aboutus.aspx", "contentKey": "8a3e...-..." },
  { "url": "/contact",      "contentKey": "1f02...-..." }
]
```

Response `200 OK`:

```json
{ "created": 1, "existing": 1, "failed": [ { "url": "/x", "reason": "content not found" } ] }
```

## Rules

- Authorised like other Management API controllers (back-office user or API user with access to
  the Content section); anonymous requests get 401.
- `url` must start with `/`; it is stored the way Umbraco stores redirect routes (**Verify** in the
  host task: with or without the root node id prefix for a site without domains).
- Idempotent: a `(url, contentKey)` pair that already exists is counted in `existing` and not
  added again (FR-024).
- Unknown `contentKey` goes to `failed`; other entries are still processed.
- Maximum 500 entries per request; the tool sends batches of 200.

## Acceptance check

After registering `/test-old` for a published test page, `curl -sI <host>/test-old` returns `301`
with `Location` set to the page URL, and the back office lists the redirect under "URL Redirect
Management".
