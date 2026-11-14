# Sample Kentico 11 database

An invented kayak shop that looks like a real Kentico 11 database: same table and column names and
types (limited to what the reader uses), real `ClassFormDefinition` and `DocumentContent` XML
formats. Nothing here comes from a client.

## Restore

```bash
SQL_CONTAINER=oib_sql_container scripts/restore-sample.sh
```

The script copies `kentico11-sample.sql` into the container, runs it with the container's own
`MSSQL_SA_PASSWORD` (never printed) and prints the counts below. It drops and recreates the
database `Kentico11Sample`.

## Expected counts

Documents per page type (the first block of the script output):

| Page type | Documents |
|---|---|
| `CMS.Root` | 1 |
| `CMS.Folder` | 2 |
| `CMS.MenuItem` | 11 |
| `CMS.Article` | 3 |
| `DEMO.Brand` | 6 |
| `DEMO.Product` | 2 |

Pages worth knowing:

- `/Home`, `/About`: one editable region. `/Story`: two regions (`storyintro`, `storybody`); the
  document stores them in the opposite order of the template. `/Contact`: no text.
- `/Draft-Guide` (`DocumentCanBePublished = 0`), `/Old-Offer` (archived, also not publishable),
  `/Spring-Sale` (`DocumentPublishFrom` in 2099): the three pages that are not published.
- `/Account` (folder) and `/Account/Profile`: the excluded application section.
- `/Catalog` (folder), `/Catalog/Brands` with 6 brands (`Sea Spirit` and `Wave Rider` have a space
  in the name) and one brand row, `Driftwood`, without a page.
- `/Catalog/Products` with 2 products, each linked to a `COM_SKU` row.
- `/Articles` with 3 articles.
