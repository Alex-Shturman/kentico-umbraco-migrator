# Discovery: legacy Kentico site

Status: Step 1 (discovery), no migration code written yet.
Date of analysis: 2026-11-14.

All figures below are aggregate counts taken from a local restore of the
production backup. No content, user data or source code from the client
material is reproduced in this repository.

## 1. Source material

| Item | Archive size | Unpacked | Notes |
|---|---|---|---|
| `po.zip` | 54 MB | 249 MB `.bak` | One full SQL Server backup, single database |
| `sources.zip` | 201 MB | 544 MB, 9,030 files | Web site source, Kentico libraries, NuGet packages |

Layout of `sources.zip`:

| Folder | Size | Contents |
|---|---|---|
| `CMS/` | 269 MB | Kentico web project (Portal Engine, ASP.NET Web Forms) |
| `CMS/bin` | 115 MB | Compiled Kentico and third party assemblies |
| `CMS/App_Data` | 70 MB | Kentico install/templates data, no site content |
| `CMS/App_Code` | ~7.2k lines in custom folders | Custom data layer (LINQ to SQL), controllers, scheduled tasks |
| `CMS/PontoonsOnly` | 0.5 MB | 38 custom user controls (6.2k lines), the site's application logic |
| `CMS/App_Themes/PontoonsOnly` | 2 MB, 179 files | CSS, JS, layout images, banners, dealer feed samples |
| `packages/` | 205 MB | NuGet packages |
| `Lib/` | 63 MB | Kentico reference assemblies |
| root | | `WebSite.sln`, an Access ZIP-code lookup database (29 MB), hotfix log |

The sample sites that ship with Kentico (DancingGoat, CorporateSite,
EcommerceSite, CommunitySite, IntranetPortal) are present on disk but are
not installed in the database; only one site exists.

**Gap: listing photos are not in the archive.** The custom code stores
listing photos as files under `App_Themes/<site>/Photo/{ImageID}.jpeg` (plus
`_small` variants). That folder is not included. Only 24 of 173,288 image
rows carry an explicit URL, and those point to an even older platform. See
section 6.

## 2. Database restore and Kentico version

- Restored into the local SQL Server 2022 Docker container as `PO_Kentico`
  in 6.1 s (31,847 pages). The backup comes from SQL Server 2017
  (database version 869) and was upgraded on restore to version 957.
- `CMS_SettingsKey`:

| Key | Value |
|---|---|
| `CMSDBVersion` | **11.0** |
| `CMSDataVersion` | 11.0 |
| `CMSHotfixVersion` | 10 |
| `CMSDefaultCultureCode` | en-US |

**This is Kentico 11.0 hotfix 10, not 7 or 8.** It is still a Portal Engine
site (Web Forms, page templates, web parts), which Kentico 11 still
supported. The schema is the Kentico 11 one (`CMS_Tree` plus `CMS_Document`,
coupled `CONTENT_*` / custom tables, `CMS_DocumentAlias`, `Media_*`).

Site facts:

- 1 site, 1 culture (en-US), no language prefixes in URLs.
- Friendly URL extension `.aspx`; the custom aliases are extensionless.
- `CMSDefaultAliasPath` = `/Home`; the root node also has an editable region.
- No pages under workflow, no checked-out pages, no scheduled publishing,
  no archived pages. Only 8 rows of version history.
- 7 secured nodes (account area), 2 nodes that require SSL.
- Last content modification: 2025-04. The newest listing is from 2026-11,
  so the application side is still live.

## 3. Content inventory

### 3.1 Page types (CMS_Class, `ClassIsDocumentType = 1`)

44 page types are defined; 11 are in use in the tree. 33 are unused: Kentico
built-ins (Blog, News, Event, FAQ and so on), transformation containers,
and 11 custom `PO.*` classes that are used only as data tables, never as
pages (see 3.3).

| Page type | Storage | Tree nodes | Role |
|---|---|---|---|
| `PO.Make` | coupled `PO_Make` | 88 | Boat brand landing page (SEO text and meta) |
| `CMS.MenuItem` | coupled `CONTENT_MenuItem` | 49 | Generic page; content lives in editable regions |
| `CMS.Folder` | none | 7 | Structural folders |
| `CMS.Article` | coupled `CONTENT_Article` | 6 | Article |
| `PO.Horsepower` | coupled | 6 | Lookup value used by search |
| `CMS.File` | coupled `CONTENT_File` | 5 | File page (images, feed, ads.txt) |
| `PO.ListingCategory` | coupled | 4 | Lookup value used by search and URLs |
| `PO.Product` | coupled `PO_Product` | 3 | Shop product (with image gallery) |
| `PO.ListingType` | coupled | 2 | Listing plan (price, days, photo limit) |
| `PO.ListingRenew` | coupled | 1 | Renewal plan (price, days) |
| `CMS.Root` | none | 1 | Root |
| **Total** | | **172** | |

The tree is shallow: maximum depth 3. There are no linked nodes and no
multilingual variants. 109 of 172 documents are hidden from navigation and
135 are excluded from the sitemap.

Top-level structure (section names only):

```
/ (root)
├── Home, About, Testimonials, Contact, Login, Registration, Error, NotFound ...
├── Articles/            6 articles
├── Account/             9 account pages (secured)
├── Listings/            20 functional pages + lookup folders + 88 brands
│   ├── ListingMake/        88 PO.Make
│   ├── ListingCategory/    4
│   ├── ListingHorsepower/  6
│   ├── ListingTypes/       2
│   └── ListingRenew/       1
├── Products/            product category, 3 products
├── Resources/           files, banners
├── images/              1 file
└── Special-Pages/       6 shop and system pages
```

### 3.2 Where the page text lives

- **Editable regions** (`CMS_Document.DocumentContent`): 23 documents, 28
  `editabletext` regions, about 23 k characters in total. This is where
  almost all text on `CMS.MenuItem` pages lives.
- **Coupled fields**: articles (6, rich text), brands (36 of 98 have
  top/bottom rich text, 86 have a meta title), products (3).
- **Page templates**: 46 ad-hoc and 4 shared Portal Engine templates.
  Their web parts are mostly `usercontrol` (46) and `editabletext` (37),
  plus a few dozen application web parts (search, registration, shopping
  cart, PayPal, forms, comments). Only 2 `statictext` web parts exist.
  Templates and layouts are presentation and are not migrated.
- SEO: 23 documents have a page title and 19 a description. Keywords are empty.

### 3.3 Custom data tables (application data, not pages)

These tables belong to `PO.*` classes, but the rows are not tree nodes.
The custom code reads them through LINQ to SQL.

| Table | Rows | Meaning |
|---|---|---|
| `PO_Image` | 173,288 | Listing photos (14,064 belong to current listings) |
| `PO_Listing` | 2,182 | Boat listings (2,046 active, 136 expired) |
| `PO_ProfileDetails` | 1,868 | Seller and dealer profiles |
| `PO_ListingRemoved` | 1,134 | Sold/removed listings archive |
| `PO_SavedListing` | 288 | User favourites |
| `PO_BlockedForLeads` | 52 | Spam block list |
| `PO_ListingLead` | 28 | Buyer enquiries |
| `PO_ListingAlert` | 10 | Saved search alerts |
| `PO_DocumentVisitCounter`, `PO_BannerStatistic` | 7, 4 | Counters |
| `PO_Make` | 98 | Brands; 88 have a page node, 10 rows have no node |

Kentico custom tables: 1 (`SimplyComments_Comments`, 4 rows).

### 3.4 Media and files

| Store | Count | Size | Notes |
|---|---|---|---|
| Media libraries (`Media_Library` / `Media_File`) | **0** | | The site never used media libraries |
| Page attachments (`CMS_Attachment`) | 25 | 1.9 MB | All binaries stored **in the database** |
| ·  product gallery (field group) | 15 | 1.4 MB | `PO.Product.Images` |
| ·  product unsorted attachments | 3 | 0.2 MB | |
| ·  `CMS.File` file field | 6 | 0.25 MB | 5 nodes; one extra is a superseded version |
| ·  article teaser image | 1 | 35 KB | |
| Attachment history | 8 | | Old versions, not migrated |
| Theme files (`App_Themes`) | 179 | 2 MB | Design assets, not content |
| Listing photos on disk | **missing** | | See section 1 |

By extension: 23 `.jpg`, 1 `.xml` (a product feed), 1 `.txt` (`ads.txt`).

### 3.5 Links inside rich text

Scanned: editable regions, article text and brand text (62 KB).

| Pattern | Count |
|---|---|
| `<img>` tags | **0** |
| `getattachment` / `getmedia` / `getdoc` links | **0** |
| `href` total | 106 |
| ·  site-relative | 96 |
| ·  absolute links to the site's own domain | 4 |
| ·  external | 6 |
| ·  of which to the `/search-featured/{brand}` route | 90 |
| ·  of which ending in `.aspx` | 5 |
| Kentico macros `{% %}` / inline widgets `{^ ^}` | 0 / 0 |

Rich text therefore needs **link** rewriting but no image or media rewriting.
Most internal links point at the brand search, which is an application
route (see 3.6 and section 5).

### 3.6 URLs and aliases

- 172 node alias paths. `DocumentUrlPath` overrides: 2. Custom extensions:
  5 (file nodes served as `.jpg`, `.xml`, `.txt`).
- `CMS_DocumentAlias`: 28 aliases on 16 nodes:
  - 25 fixed aliases (for example short marketing URLs and old URLs of the
    login and registration pages), 5 of which are culture-neutral;
  - 3 wildcard aliases, all for application routes: listing detail
    (`/{title}_i{ID}` and `/{title}-i{ID}`) and featured search by brand
    (`/search-featured/{make}`); also 1 `ROUTE:` alias.
- `web.config` contains 3 IIS rewrite rules (www canonicalisation, HTTPS, one
  disabled IP redirect).
- Settings that affect old URLs: trailing slash `NEVER`, case redirect
  `NONE`, aliases not redirected to the main URL.

## 4. Other objects found (application side)

| Area | Objects | Rows |
|---|---|---|
| Accounts | `CMS_User` | 1,594 (1,509 enabled), 20 roles |
| E-commerce | `COM_SKU`, `COM_Order`, `COM_OrderItem`, `COM_Customer` | 9 SKUs, 230 orders, 245 items, 179 customers |
| Forms | `ContactUs` form | 251 submissions |
| Search | 2 search indexes (products) | |
| Categories | `CMS_Category` | 4 (assigned to no document) |
| Tags, relationships | | 0 |
| Email queue / templates | | 957 / 64 |
| Event log, object version history | | 2,130 / 1,338 |
| Scheduled tasks (custom) | dealer feed import, expired listings, sitemap, image import and cleanup | |

## 5. Content vs application features

**Plain content (candidate for migration):**

- Brand pages (`PO.Make`): 88 nodes, the richest content (SEO text, meta).
- Articles: 6.
- Static pages with editable text: Home, About, Testimonials, Products
  landing pages, Brands overview, the "get a loan" page and similar. About
  10 pages carry real editorial text.
- Products (`PO.Product`): 3 pages with gallery images. They are pages, but
  they are also shop SKUs (see questions).
- File nodes (5) and attachments (25) as Umbraco media.
- URL aliases and old URLs, as redirects.

**Application features (not content, to be rebuilt or dropped, not
migrated by a content tool):**

- Classifieds: listings, photos, profiles, leads, alerts, saved listings,
  dealer feed import, listing plans and renewals, PayPal payment flow.
- Search: listing search (category, brand, horsepower filters) and
  featured search by brand, including the wildcard URL routes.
- Accounts: registration, login, profile, password and email change,
  the secured `/Account/*` pages.
- Shop: shopping cart, orders, order and payment result pages.
- Forms (contact form and its 251 submissions) and comments.
- Lookup page types (`PO.ListingCategory`, `PO.Horsepower`,
  `PO.ListingType`, `PO.ListingRenew`): data for the application, stored
  as pages.
- About 30 `CMS.MenuItem` pages that are shells for a user control. Some of
  them hold short helper text in an editable region (for example on the
  account and renewal pages).

## 6. Risks and open points

1. **Version mismatch with the brief**: Kentico 11 rather than 7/8. The
   mapping targets the Kentico 11 schema. A 7/8 reader would need different
   SQL for some tables (for example `CMS_Attachment` and alias columns
   changed between versions).
2. **Listing photos are missing** from the provided files. They do not
   matter for the content migration in scope, but they will matter if
   listings are ever moved.
3. **No media library**: the "media library files" item in the scope maps to
   0 items. The only media are the 25 database attachments.
4. **Thin rich text**: no images and no `getattachment` links inside rich
   text, so the link rewriter will mainly handle page links and `.aspx`
   URLs. To be an honest demo, the README should say so.
5. Brand links point at an application route (`/search-featured/{brand}`),
   so their target in Umbraco depends on whether the search is rebuilt.
6. 10 brand rows have no page node; they are only used by the application.
