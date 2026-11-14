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
| `CMS.Folder` | 3 |
| `CMS.MenuItem` | 12 |
| `CMS.Article` | 3 |
| `CMS.File` | 2 |
| `DEMO.Brand` | 6 |
| `DEMO.Product` | 2 |

Other counts:

| Item | Count |
|---|---|
| Documents in total | 29 (26 published, 3 not), one of them a linked node |
| Attachments (current versions, `CMS_Attachment`) | 7 |
| Attachment history rows (`CMS_AttachmentHistory`, must be ignored) | 1 |
| Media files (`Media_File`) | 4 (3 on disk, 1 missing) |
| Links (`href=` in page content and article text) | 12 |
| Document aliases (`CMS_DocumentAlias`) | 3 (1 of them a wildcard) |

Pages worth knowing:

- `/Home`, `/About`: one editable region. `/Story`: two regions (`storyintro`, `storybody`); the
  document stores them in the opposite order of the template. `/Contact`: no text.
- `/Draft-Guide` (`DocumentCanBePublished = 0`), `/Old-Offer` (archived, also not publishable),
  `/Spring-Sale` (`DocumentPublishFrom` in 2099): the three pages that are not published.
- `/Account` (folder) and `/Account/Profile`: the excluded application section.
- `/Catalog` (folder), `/Catalog/Brands` with 6 brands (`Sea Spirit` and `Wave Rider` have a space
  in the name) and one brand row, `Driftwood`, without a page.
- `/Catalog/Products` with 2 products, each linked to a `COM_SKU` row.
- `/Catalog/About-Link` is a linked node (`NodeLinkedNodeID` points at `/About`); the reader returns
  it with `LinkedNodeId` set and no fields, regions or SKU, and the plan builder skips it.
- `/Articles` with 3 articles.

## Media

The 7 current attachments:

| Attachment | Page | Kind |
|---|---|---|
| `pacific-front.jpg`, `pacific-side.png` | Pacific Touring Kayak | gallery (`Images` group), order 1 and 2 |
| `river-front.jpg` (order 2), `river-side.jpg` (order 1) | River Runner Kayak | gallery, inserted in reverse order |
| `about-team.png` | `/About` | unsorted attachment of the page |
| `paddling-teaser.jpg` | `/Articles/Paddling-Basics` | file field `ArticleTeaserImage` |
| `paddle-size-guide.pdf` | `/Downloads/Paddle-Size-Guide` | file field of a `CMS.File` node |

`/Downloads/Paddling-Map` is a `CMS.File` node without an attachment: the file lives on the web
server, so the profile lists it as not uploaded.

The media library `Gallery` has four `Media_File` rows. Three files are committed under
`sample/media/Gallery/` (`dock.jpg`, `boats/kayak-blue.jpg`, `boats/kayak-red.png`); `missing.jpg`
is not on disk. A library file is found at `<mediaRoot>/<LibraryFolder>/<FilePath>`.

## Links in rich text

Twelve `href=` attributes plus one image `src` in total (the restore script prints the count):

| Where | Link | Kind |
|---|---|---|
| `/Story`, region `storybody` | `/About` | page by alias path |
| | `/About.aspx` | page, `.aspx` form |
| | `https://www.kayaks.example/Catalog/Products/Pacific-Touring-Kayak` | page, absolute URL on an own host |
| | `/visit` | page by short document alias (alias row of `/Contact`) |
| | `~/getmedia/<guid>/kayak-blue.jpg` | media library file |
| | `/brand-search/Sea Spirit` | brand route, name with a space |
| | `<img src="~/getattachment/<guid>/pacific-front.jpg?width=96">` | attachment image |
| `/Story`, region `storyintro` | `https://weather.example.org/lakes` | external site |
| | `mailto:info@kayaks.example` | mail |
| | `#rental` | anchor |
| `/Articles/Winter-Storage`, `ArticleText` | `/cart` | application route |
| | `/Seasonal/Deals.aspx` | unknown internal page |
| | `/Story` | page |

Own hosts needed in the profile: `kayaks.example` and `www.kayaks.example`.

## Aliases and URLs

- `/contact` and `/visit` are aliases of `/Contact`. `/{title}-i{ID}` is a wildcard alias on the
  excluded `/Account/Profile` page (reported, not migrated).
- `/About` has a hand-set `DocumentUrlPath` of `/about-the-shop`.
- `expected-urls.txt` lists 25 old URLs with the expected status and final path
  (`<old url> <status> <final path>`). Final paths for redirects follow the verified Umbraco 17 routing (lower case, relative, trailing slash)
  and the catalog layout `/catalog/brands/<alias>`; adjust them if a run shows otherwise.

## Profile values for Track 3

`profiles/sample.json` is owned by Track 3. The sample needs these two values in it:

```json
"notUploadedPaths": [ "/Downloads/Paddling-Map" ],
"mediaRoot": "sample/media",
"ownHosts": [ "kayaks.example", "www.kayaks.example" ]
```
