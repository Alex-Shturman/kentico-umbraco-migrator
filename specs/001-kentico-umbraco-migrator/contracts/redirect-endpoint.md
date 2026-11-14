# Contract: Host redirect endpoint

Added to `host/UmbracoSite` because the Management API cannot create redirects (research R4).

```
POST /umbraco/management/api/v1/kentico-migration/redirects
Authorization: Bearer <same token as the Management API>
Content-Type: application/json

[
  { "url": "/aboutus.aspx", "contentKey": "8a3e...-..." },
  { "url": "/search-featured/Old%20Name", "contentKey": "1f02...-..." }
]
```

Response `200 OK`:

```json
{ "created": 1, "existing": 1, "failed": [ { "url": "/x", "reason": "content not found" } ] }
```

## Rules

- Authorised like other Management API controllers (back-office user or API user with access to
  the Content section); anonymous requests get 401.
- `url` is a request path as a browser sends it: it starts with a single `/`, may be
  percent-encoded, and has no query string or fragment. The host stores it **percent-decoded,
  lower-cased and without a trailing slash** (verified, research R4/R10): Umbraco looks redirects
  up by that form, so a route stored with capitals, encoding or a trailing slash is never matched.
  The site root `/` is not accepted as a source.
- Idempotent: a `(url, contentKey)` pair that already exists is counted in `existing` and not
  added again (FR-024). Comparison uses the stored form above.
- Every entry that is not registered goes to `failed` with a reason, the other entries are still
  processed. Reasons: `entry is null`, `url must start with /`, `url must not start with //`,
  `url is longer than 2000 characters`, `url must not contain a query string or fragment`,
  `url must not contain whitespace or control characters (percent-encode them)`,
  `url must not contain control characters`, `url must not contain //` (after decoding, so `/%2fx` is refused too), `url must not be the site root`,
  `content not found` (unknown key or the empty guid), `url already redirects to another document`
  (the stored route belongs to a different `contentKey`; no second row is added and the existing row is left unchanged), or the
  exception type name when Umbraco fails to save (details are in the host log).
- Maximum 500 entries per request, more gets `400` ("Send a JSON array with at most 500
  entries."); the tool sends batches of 200.
- `GET` and `HEAD` requests to paths ending in `.aspx` (outside `/umbraco`) are rewritten by the
  host with a trailing slash before routing, because Umbraco never offers a path with a file
  extension to its redirect finder. After that `/aboutus.aspx` reaches a registered
  `/aboutus.aspx` redirect and answers 301.

## Acceptance check

After registering `/test-old` for a published test page, `curl -sI <host>/test-old` returns `301`
with `Location` set to the page URL, posting the same body twice reports `existing` the second
time, a request without a token gets `401`, and the back office lists the redirect under "URL
Redirect Management". The host has no test project, so these rules are verified by this manual
check (host/README.md).
