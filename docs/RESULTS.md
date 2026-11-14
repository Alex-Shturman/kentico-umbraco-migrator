# Reference run results

Aggregate numbers only. The reference site is the PontoonsOnly Kentico 11 database (client property): nothing in this file
is content, a name or a listing. The numbers come from the `report.json` of two consecutive `migrate` runs against a freshly
reinstalled Umbraco 17 site (`runs/` is git-ignored), plus the output of `scripts/check-urls.sh` and a few aggregate SQL counts
on the source (marked as such). Date of the run: 2026-11-14. Profile: `profiles/pontoonsonly.json`.

## Before and after

| Item | Source (Kentico 11) | Target (Umbraco 17) |
|---|---|---|
| Pages in the tree | 172 (including the root) | 139 documents |
| Brand pages (`PO.Make`) | 88 pages plus 10 rows without a page | 98 (88 published, 10 unpublished for review) |
| Articles | 6 | 6 |
| Products | 3 | 3 |
| Lookup items (categories 4, horsepower 6, listing plans 2, renewal plans 1) | 13 | 13 |
| Content pages and lists | 49 `CMS.MenuItem` pages | 9 content pages, 1 article list, 1 brand list, 1 product list (12 pages) |
| Folders | 2 `CMS.Folder` plus 5 `CMS.MenuItem` containers that hold no text | 7 folders (`contentFolder`, no template) |
| Attachments | 25 | 22 images in Azure Blob media (1 skipped orphan, 2 host files) |
| Media folders | | 6 |
| Media libraries | 0 | 0 |
| Document aliases | 28 (3 are wildcard routes) | 3 wildcard routes reported, not redirected; the others are part of the redirects below |
| Redirects | | 306 |

Document types per target type: `brand` 98, `article` 6, `contentPage` 9, `contentFolder` 7, `horsepowerRange` 6,
`listingCategory` 4, `product` 3, `listingPlan` 2, `articleList` 1, `brandList` 1, `productList` 1, `renewalPlan` 1.

Schema written by the first run: 2 data types, 15 document types, 2 templates (19 created, 19 unchanged on the second run).

## Skipped by category (44)

| Category | Count |
|---|---|
| application page (excluded path prefixes: account, listing functions, special pages, login, registration) | 34 |
| unmapped type (the root and 3 plain folders that only hold media) | 4 |
| wildcard route (alias of an application route, not redirected) | 3 |
| host file (the product feed and `ads.txt`, not uploaded) | 2 |
| unreferenced attachment (an attachment of a file page that no field uses) | 1 |

No item failed, no issue was raised, 0 pages are missing in the source and 0 count drops occurred. No page has a publish
window, so no schedules were left unapplied.

## Media

| | Count |
|---|---|
| Media items created | 28 (22 images, 6 folders) |
| Source attachments | 25 |
| Not uploaded | 3 (2 host files, 1 unreferenced attachment) |

Source sizes (aggregate SQL on the source, not part of `report.json`): the 25 attachments are 1,942,580 bytes in total
(23 JPEG, 1 XML, 1 text file); the 22 uploaded images are 1,904,574 bytes.

## Links in rich text

| Outcome | Count |
|---|---|
| Rewritten to a page, brand page or media link | 95 |
| External, unchanged | 6 |
| Pointing to the application, unchanged | 3 |
| Unresolved | 0 |

An aggregate SQL count on the source finds 90 links to the brand search route, all in pages that are migrated; none is
unresolved, so all 90 are among the 95 rewritten links (SC-004). The tool classified 104 links in the migrated pages;
DISCOVERY.md counted 106 in the whole source, which includes pages that are not migrated.

## Redirects by reason (306)

| Reason | Created |
|---|---|
| `aspx`: the `.aspx` form of a page URL | 121 |
| `url-path`: the old tree path of a page whose new URL differs (brand pages moved below the brand list, cleaned segments) | 90 |
| `alias`: any other old URL, including the old brand route of names without a space | 63 |
| `renamed`: an old route that contains a space (brand routes) | 30 |
| `home`: `/Home` and `/Home.aspx` | 2 |

The second run found all 306 as existing and created none.

## Timings

| Phase | First run | Second run |
|---|---|---|
| read the source | 0.22 s | 0.20 s |
| plan | 0.01 s | 0.01 s |
| snapshot | 0.05 s | 0.14 s |
| schema | 1.25 s | 0.08 s |
| media | 1.69 s | 0.11 s |
| documents | 19.99 s | 0.94 s |
| redirects | 0.97 s | 0.39 s |
| verify | 0.49 s | 0.25 s |
| **Total** | **25.18 s** | **2.29 s** |

## Second run

Exit code 0. Schema 19 unchanged; media 28 unchanged; documents 139 unchanged (0 created, 0 updated); redirects 0 created and
306 existing. Target counts are the same as after the first run.

## URL check

`scripts/check-urls.sh` against `_work/expected-urls.txt` (git-ignored), a list generated from the source with aggregate SQL
that returns paths only: the kept path and its `.aspx` form of every migrated page, the fixed aliases, the old brand route of
each of the 88 published brands, and 404 for template-less pages and folders. 156 lines: 21 answer 200, 116 answer 301, 19
answer 404.

Result: **155 OK, 1 FAIL**. All 88 old brand routes, all `.aspx` forms and all other aliases answer correctly. The one
failure is the alias that used to serve the products landing page: `/products` answers 404 although its redirect is registered.
`/Products` is a template-less folder in the new tree at the same URL, and Umbraco answers a found page without a template
with 404 before the redirect finder runs. Follow-up task T064 in `tasks.md`.

## Comparison with the success criteria

| Criterion | Expected | Result |
|---|---|---|
| SC-001 counts | 88 + 10 brand pages, 6 articles, 3 products, 13 lookup items, 22 media items | 98 brand pages (10 unpublished), 6, 3, 13, 22 images: matches. Everything else is migrated or listed as skipped with a reason |
| SC-002 second run | 0 created, 0 updated | 0 created, 0 updated |
| SC-003 old URLs | all OK | 155 of 156 OK; `/products` fails (T064) |
| SC-004 brand links | 90 resolved, 0 unlisted unresolved | 90 in migrated pages, 0 unresolved |
| SC-006 duration | under 5 minutes | 25.18 s for the full first run |

## Fresh clone

Measured in T060 (see below once recorded).

## Notes and deviations

- **Kentico 11, not 7 or 8.** The database is Kentico 11.0 hotfix 10. The reader targets the version 11 schema; a 7 or 8
  reader would need different SQL for some tables.
- **No media libraries.** The "media library files" part of the scope is 0 items here; the media are the 25 page attachments.
  The reader and the sample cover media libraries.
- **Listing photos are not part of the material.** The classifieds data (about 173 thousand photo rows) is application data
  and out of scope.
- **Thin rich text.** The rich text has no images and no attachment links, so the rewriter mainly handled page links and brand
  links. The image paths of the rewriter are exercised by the sample and the unit tests, not by the reference site.
- **Scope extensions.** Lookup items (13) and the SKU fields of the 3 products are migrated although the brief mentions
  neither; 10 brand rows without a page are created as unpublished pages for review.
- **Lookup items stay in their source containers.** MAPPING.md planned a hidden `settings` folder; the tool does not create
  folders that do not exist in the source tree, so the 13 items stay below their original containers
  (`/listings/listingcategory` and so on), which are template-less folders. The lookup pages have no template and answer 404.
- **Brand pages are below `/listings/search-featured`**, not at `/search-featured`. The live brand URL works through a
  redirect for every brand (follow-up T065). MAPPING.md planned 65 direct brand URLs.
- **Umbraco cleans URL segments.** Characters such as parentheses and dashes are dropped from a URL segment, so a page whose
  node alias contains them gets a different URL than before; its old path is redirected (part of the `url-path` redirects).
- **Folders without a template answer 404**, as in Kentico.
- The 10 unpublished brand pages and the 3 wildcard routes are the only items that are deliberately not served.
