# Proposed mapping: Kentico 11 to Umbraco 17 (LTS)

Status: proposal for review. Items marked **Q** need a decision (see the
end of this document).

Based on the inventory in [DISCOVERY.md](DISCOVERY.md).

## 1. Principles

- **Clean target.** Only content moves. Templates, web parts, layouts,
  transformations and application data stay behind.
- **Deterministic and re-runnable.** Each migrated item keeps its Kentico
  identity (NodeGUID / AttachmentGUID) in a hidden property or a mapping
  table, so a second run updates items instead of duplicating them.
- **Read-only source.** The tool reads the Kentico database through SQL only.
  It needs no Kentico API and no Kentico DLLs.
- **Write through the Umbraco Management API.** Data types, document types,
  templates, content and media are created over HTTP with an API user
  (client credentials), never by writing to the Umbraco database. The
  Kentico GUIDs are passed as the item keys, so a second run finds and
  updates the same items.
- **Everything skipped is reported** with a reason.

## 2. Document types

### 2.1 Compositions

| Composition | Properties | Used by |
|---|---|---|
| `seoComposition` | `metaTitle` (TextBox), `metaDescription` (TextArea), `metaKeywords` (TextBox) | all page types |
| `navigationComposition` | `umbracoNaviHide` (TrueFalse), `hideFromSitemap` (TrueFalse) | all page types |
| `legacyComposition` | `legacyNodeGuid` (TextBox, read-only label), `legacyAliasPath` (TextBox, read-only label) | all migrated types |

Source of the values: `DocumentPageTitle`, `DocumentPageDescription`,
`DocumentPageKeyWords`, `DocumentMenuItemHideInNavigation`,
`DocumentShowInSiteMap`, `NodeGUID`, `NodeAliasPath`. Brands override the
meta fields with their own `MetaTitle`/`MetaDescription`/`MetaKeywords`.

### 2.2 Page types

| Kentico page type | Umbraco Document Type | Allowed children |
|---|---|---|
| `CMS.Root` + `/Home` | `home` (root node) | all top-level types |
| `CMS.MenuItem` (content pages, including `/Listings/Brands`) | `contentPage` | `contentPage` |
| `CMS.MenuItem` `/Articles` | `articleList` | `article` |
| `CMS.Article` | `article` | none |
| `CMS.MenuItem` `/Listings/Search-Featured` (live alias `/search-featured`) | `brandList`, placed under `home` | `brand` |
| `PO.Make` | `brand` | none (10 brand rows without a node are created unpublished, decision Q5) |
| `CMS.MenuItem` `/Products/*` | `productList` | `product` |
| `PO.Product` | `product` | none |
| `CMS.Folder`, and `CMS.MenuItem` containers without text (`/Listings`, `/Products`) | `folder` (no template) | any migrated type |
| `CMS.File` | not a document; becomes a **media item** | |
| Folder `/settings` (new) | `settingsFolder` | the four lookup types below |
| `PO.ListingCategory` | `listingCategory` | none |
| `PO.Horsepower` | `horsepowerRange` | none |
| `PO.ListingType` | `listingPlan` | none |
| `PO.ListingRenew` | `renewalPlan` | none |

### 2.3 Lookup types (decision Q1)

Placed under `home/settings` (hidden from navigation, no template).

| Umbraco type | Properties (editor) |
|---|---|
| `listingCategory` | `title` (TextBox), `legacyId` (Integer) |
| `horsepowerRange` | `title` (TextBox), `min` (Integer), `max` (Integer), `legacyId` (Integer); sort order from `Order` |
| `listingPlan` | `title` (TextBox), `description` (TextArea), `price` (Decimal), `daysVisible` (Integer), `maxPhotos` (Integer), `featured` (TrueFalse), `legacyId` (Integer) |
| `renewalPlan` | `title` (TextBox), `price` (Decimal), `days` (Integer), `legacyId` (Integer) |

`legacyId` keeps the Kentico primary key, because `PO_Listing` refers to
these rows by ID.

### 2.4 Fields to property editors

`home`, `contentPage` (from `CMS.MenuItem` and editable regions)

| Kentico source | Umbraco property | Property editor |
|---|---|---|
| `DocumentName` | node name | |
| `MenuItemName` | `title` | `Umbraco.TextBox` |
| `MenuItemTeaserImage` (0 in use) | `teaserImage` | `Umbraco.MediaPicker3` (single) |
| `MenuItemGroup` | not migrated (menu placement in Portal Engine) | |
| First editable region in `DocumentContent` (template order) | `bodyText` | `Umbraco.RichText` |
| Remaining editable regions, joined in template order | `additionalContent` | `Umbraco.RichText` |

`article`

| Kentico field | Umbraco property | Property editor |
|---|---|---|
| `ArticleName` | node name and `title` | `Umbraco.TextBox` |
| `ArticleTeaserText` | `teaserText` | `Umbraco.RichText` |
| `ArticleTeaserImage` | `teaserImage` | `Umbraco.MediaPicker3` (single) |
| `ArticleText` | `bodyText` | `Umbraco.RichText` |
| `DocumentCreatedWhen` / publish date | `publishDate` | `Umbraco.DateTime` |

`brand`

| Kentico field | Umbraco property | Property editor |
|---|---|---|
| `Name` | node name and `title` | `Umbraco.TextBox` |
| `HeaderText` | `heading` | `Umbraco.TextBox` |
| `TopText` | `introText` | `Umbraco.RichText` |
| `BottomText` | `bodyText` | `Umbraco.RichText` |
| `Synonyms` | `synonyms` | `Umbraco.Tags` (split on comma) |
| `MetaTitle` / `MetaDescription` / `MetaKeywords` | `seoComposition` | as above |
| `Order` | sort order of the node | |
| `MakeID` | `legacyMakeId` | `Umbraco.Integer` (kept for listing search) |

`product`

| Kentico field | Umbraco property | Property editor |
|---|---|---|
| `ProductName` | node name and `title` | `Umbraco.TextBox` |
| `Featured` | `featured` | `Umbraco.TrueFalse` |
| `Images` (attachment group) | `images` | `Umbraco.MediaPicker3` (multiple, in attachment order) |
| `ShippingInformation` | `shippingInformation` | `Umbraco.RichText` |
| `ShippingImage` (radio list) | `shippingImage` | `Umbraco.DropDown.Flexible` with the same values |
| `COM_SKU.SKUNumber` (via `NodeSKUID`) | `sku` | `Umbraco.TextBox` |
| `COM_SKU.SKUPrice` | `price` | `Umbraco.Decimal` |
| `COM_SKU.SKURetailPrice` | `retailPrice` | `Umbraco.Decimal` |
| `COM_SKU.SKUAvailableItems` | `availableItems` | `Umbraco.Integer` |
| `COM_SKU.SKUEnabled` | `available` | `Umbraco.TrueFalse` |

Kentico data types to Umbraco editors (general rule used by the tool):

| Kentico `columntype` / control | Umbraco editor |
|---|---|
| `text` + TextBox | `Umbraco.TextBox` |
| `text` + TextArea | `Umbraco.TextArea` |
| `longtext` + HtmlArea | `Umbraco.RichText` |
| `integer` | `Umbraco.Integer` |
| `decimal`, `double` | `Umbraco.Decimal` |
| `boolean` | `Umbraco.TrueFalse` |
| `datetime` | `Umbraco.DateTime` |
| `file` (single attachment) | `Umbraco.MediaPicker3`, single |
| `docattachments` (attachment group) | `Umbraco.MediaPicker3`, multiple |
| DropDown / RadioButtons with fixed options | `Umbraco.DropDown.Flexible` / `Umbraco.RadioButtonList` |

## 3. Tree structure and URLs (decision U1: keep old URLs)

The Umbraco tree mirrors the Kentico paths, so that a page keeps its old
address and needs no redirect. Each migrated page gets its Kentico
`NodeAlias` as `umbracoUrlName` (Umbraco lowercases it).

```
home                          /            (Kentico /Home, the default alias path)
├── contentPage               /aboutus, /testimonials, ...
├── articleList               /articles
│   └── article  x6           /articles/<alias>
├── folder (no template)      /listings
│   └── contentPage           /listings/getaloan, /listings/brands, ...
├── brandList                 /search-featured   (live URL of the brand pages)
│   └── brand    x88 (+10 unpublished, Q5)   /search-featured/<brand>
├── folder (no template)      /products
│   └── productList           /products/pontoon-towers
│       └── product  x3
├── contentPage               /error, /notfound
└── settingsFolder (hidden)   /settings
    ├── listingCategory x4
    ├── horsepowerRange x6
    ├── listingPlan     x2
    └── renewalPlan     x1
```

Rules:

- Brand pages are served on the live site at `/search-featured/{brand
  name}`, not at their tree path, so they are placed under a `brandList`
  node with the URL name `search-featured`. 65 brands keep their exact
  URL; 33 brand names contain spaces, which cannot be an Umbraco URL
  segment, so those get a redirect.
- Kentico folders become a `folder` node without a template, so their
  children keep their paths. A folder URL returns 404, as in Kentico.
- Order: `NodeOrder` is kept.
- Node names: `DocumentName`.
- Publish state: everything published in Kentico is published in Umbraco.
- Hidden from navigation: `umbracoNaviHide`.
- Created and updated dates: Umbraco sets its own create date. The original
  dates are written to `publishDate` (articles) and to the run report.
- Old URLs are compared case-insensitively. Verified on Umbraco 17.8.1: a page at
  `/fixture-page` is served (HTTP 200, no redirect) for `/Fixture-Page` and
  `/FIXTURE-PAGE` too, so a case-only difference needs no redirect. A request
  ending in `.aspx` is not served as content (404) and needs a redirect.

## 4. Media

| Source | Umbraco | Count |
|---|---|---|
| Media libraries | | 0 (none exist) |
| `CMS.File` nodes + their attachment | Media item (`Image` or `File`), folder per Kentico section (`Banners`, `images`) | 3 (2 more go to the report, Q6) |
| Attachments in `PO.Product.Images` | `Image` in `Media/Products/<product>` | 15 |
| Unsorted product attachments | `Image` in the same folder, not linked to a property | 3 |
| `CMS.Article` teaser image | `Image` in `Media/Articles` | 1 |
| Attachment history | not migrated | 8 |

- Binary data is read from `CMS_Attachment.AttachmentBinary` (all 25 are in the DB).
- File name, extension, MIME type, width/height and title come from the
  attachment row. `AttachmentGUID` is stored on the media item so it can be
  matched again later.
- The `.xml` feed and `ads.txt` (2 of the 5 `CMS.File` nodes) are not
  uploaded. They are listed in the report as files for the new host
  (decision Q6), so 3 `CMS.File` nodes become media.

## 5. Rich text rewriting

The rewriter parses HTML (AngleSharp) rather than using regular expressions.

| Pattern in source | Rewritten to |
|---|---|
| `~/getattachment/{guid}/...`, `/getattachment/...` (0 found) | Umbraco media URL / `umb://media` link |
| `~/getmedia/...` (0 found) | same |
| Site-relative link to a migrated page (`/Articles/x`, `/Home.aspx`, alias `/contact`) | `{localLink:umb://document/...}` to the target node |
| Absolute link to the old domain | treated as site-relative, then as above |
| `/search-featured/{brand}` | `{localLink:...}` to the matching `brand` page (decision Q7) |
| Other application routes (`/search`, `/Registration.aspx`) | unchanged, listed in the report as "points to application" |
| External link | unchanged |
| Link that cannot be resolved | unchanged, listed in the report as a warning |

The resolver works in this order: exact node alias path, then document
aliases, then the same with the `.aspx` extension stripped. Matching is
case-insensitive.

## 6. Redirects

Because the paths are kept, redirects are only needed where the old URL
cannot be served as is:

1. old URLs with the `.aspx` extension (Umbraco does not serve them);
2. fixed document aliases from `CMS_DocumentAlias` whose node is migrated
   (for example `/contact`);
3. `/Home` and `/Home.aspx`, now served at `/`;
4. brand URLs whose name contains a space;
5. any page whose final Umbraco URL differs from its old path.

The new URL of each page is read back from Umbraco after the pages are
created, and a redirect is planned only when the old and new URL differ
after normalisation (lowercase, decoded, no trailing slash).

Outputs:

- **Native Umbraco redirects**, registered through a small endpoint in the
  host site that calls `IRedirectUrlService` (the Management API can list
  and delete redirects but not create them). They show up in the backoffice
  under "URL Redirect Management".
- A `redirects.csv` in the run output (old URL, new URL, reason).
- Wildcard aliases of application routes (listing detail) are not
  redirected; they are listed in the report as application routes.

## 7. Not migrated (reported as skipped, by category)

| Category | Reason |
|---|---|
| Page templates, layouts, web parts, transformations, CSS | Presentation; rebuilt in Umbraco views |
| Listings, photos, profiles, leads, alerts, saved listings | Application data |
| Users, roles, memberships | Accounts feature; needs a separate member migration with password handling |
| Orders, customers, shopping cart | Shop feature (SKU fields of the 3 products are migrated as product properties) |
| Form submissions, comments | Application data / personal data |
| Application shell pages (`/Account/*`, `/Listings/*` functional pages, `/Special-Pages/*`, login, registration) | No editorial content; the function they host is not migrated (**Q3**) |
| Event log, email queue, version history | Operational data |

## 8. Verification report (each run)

- Source count vs target count per type: documents per page type, media items, redirects.
- Skipped items with a reason (category, Kentico ID or GUID, alias path).
- Link rewriting: links rewritten / left unchanged (external) / unresolved.
- Run timing per phase.
- Output: console summary plus `report.json` and `report.md` in the run folder.

## Decisions

| # | Decision |
|---|---|
| Q1 | Lookup page types are migrated into a `settings` folder (hidden from navigation), see 2.3. This goes beyond the brief's scope (listings) and is stated as such in the README |
| Q2 | Products are migrated as content **plus** SKU data (number, price, retail price, availability) as plain properties. Also beyond the brief's scope (e-commerce), stated in the README |
| Q3 | Migrate only pages with editorial text; application shell pages are reported as skipped |
| Q4 | First editable region goes to `bodyText`, the remaining regions to `additionalContent` (`Umbraco.RichText`) |
| Q5 | Create the 10 missing brand pages, unpublished, for editor review |
| Q6 | `ads.txt` and the XML feed are not uploaded; they are listed in the report as files for the new host |
| Q7 | Rewrite `/search-featured/{brand}` links to the brand page |
| Q8 | A fresh Umbraco 17 host in the repo on SQL Server (not SQLite): database `PO_Umbraco` in the same local SQL Server container as the restored Kentico database |
| Q9 | Section-level names in the docs are fine |
| W1 | The tool is a separate console program that writes through the Umbraco Management API (client credentials) |
| U1 | Keep the old URLs; redirects only where a URL cannot be kept (section 6) |
| S1 | Public sample dataset: a fictional, but working Kentico 11 database created by a SQL script |
| M1 | Media files are stored in Azure Blob Storage; locally through the Azurite emulator in Docker |
| M2 | The reader also supports Kentico media libraries (0 on this site, exercised by the sample dataset) |
