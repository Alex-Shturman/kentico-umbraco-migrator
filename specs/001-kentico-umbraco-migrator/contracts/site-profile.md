# Contract: Site profile JSON

Site-specific rules live here, not in code (Constitution V). Only section-level paths and Kentico
class names appear; never content values (Constitution I). Record shapes:
[data-model.md](../data-model.md) section 2.

```jsonc
{
  "name": "sample",
  "ownHosts": ["kayaks.example", "www.kayaks.example"],
  "mediaRoot": "sample/media",                       // optional, overrides Kentico:MediaRoot
  "excludedPathPrefixes": ["/Account", "/Shop"],     // FR-006: page and descendants skipped
  "folderPaths": ["/Catalog"],                       // FR-007: template-less folder items
  "applicationRoutePrefixes": ["/search", "/cart"],  // links left as is, counted (FR-021)
  "notUploadedPaths": ["/Files/feed"],               // FR-013 / Q6: reported as host files
  "types": [
    {
      "kenticoClass": "CMS.MenuItem",
      "targetAlias": "contentPage",
      "targetName": "Content page",
      "hasTemplate": true,
      "allowedChildren": ["contentPage"],
      "fields": [
        { "source": "doc:DocumentName", "target": "title", "editor": "TextBox", "group": "Content" },
        { "source": "region:first", "target": "bodyText", "editor": "RichText", "group": "Content" },
        { "source": "region:rest", "target": "additionalContent", "editor": "RichText", "group": "Content" }
      ]
    },
    {
      "kenticoClass": "CMS.MenuItem",
      "pathOverride": "/Articles",
      "targetAlias": "articleList",
      "targetName": "Article list",
      "hasTemplate": true,
      "allowedChildren": ["article"],
      "fields": [ { "source": "region:first", "target": "bodyText", "editor": "RichText", "group": "Content" } ]
    }
  ],
  "reparent": [
    {
      "kenticoClass": "DEMO.Brand",
      "newParentPath": "/brands",
      "urlNameFrom": "NodeAlias",
      "oldRoutePattern": "/brand-search/{Name}"
    }
  ],
  "unlinkedRows": [
    { "kenticoClass": "DEMO.Brand", "nameField": "Name", "parentPath": "/brands", "publish": false }
  ],
  "settingsFolder": { "path": "/Settings", "name": "Settings" },   // optional: tool-owned hidden folder for lookup data
  "placements": [                                                  // optional: move a page with everything below it
    { "path": "/Listings/Search-Featured", "newParentPath": "/", "urlName": "search-featured" },
    { "path": "/Listings/ListingCategory", "newParentPath": "/Settings" }
  ]
}
```

## Field `source` syntax

| Form | Meaning |
|---|---|
| `<Column>` | Column of the coupled table |
| `doc:<Column>` | Column of `CMS_Document` (`DocumentName`, `DocumentPageTitle`, ...) |
| `region:first` / `region:rest` | First editable region / remaining regions joined in order |
| `sku:<Column>` | Column of the linked `COM_SKU` row |
| `attachments:<Column>` | Attachment group field as a multi-media picker in attachment order |
| `file:<Column>` | Single file field as a single media picker |

## `editor` values

`TextBox`, `TextArea`, `RichText`, `Integer`, `Decimal`, `TrueFalse`, `DateTime`, `Tags`,
`MediaSingle`, `MediaMultiple`, `DropDown` (options taken from the Kentico field definition).

## `placements` and `settingsFolder`

A placement moves the page at `path` (a source alias path, for example a container such as a brand list) under `newParentPath`
together with everything below it. `newParentPath` is the path of another planned page, the `settingsFolder` path, or `/` for the
top level (below the home page, whose URL segment Umbraco hides). `urlName`, when given, replaces the URL name of the moved
page, so a container can be served at a chosen top-level URL (`/search-featured/<alias>` in the reference profile). A
`reparent` rule still decides the parent of a class of pages; a placement decides the parent of one page. Only a placement can
name the settings folder as a parent (a `reparent` rule or `unlinkedRows` entry cannot), a placement path must be a source
page, not an unlinked row, and a page can be placed only once (a second placement of the same path is a plan error). The settings folder `path` must start with `/` and have exactly one non-empty segment without whitespace, such as `/Settings`.

`settingsFolder` makes the tool create one folder (`contentFolder`, no template, published, `umbracoNaviHide` and
`hideFromSitemap` set) at the top level, sorted after the source pages. Its `path` is only the name placements use to address it;
it must not be the path of a source page. Its key is derived from the path, so re-runs find it.

## Fixed behaviour not configurable

SEO, navigation and migration compositions are added to every page type (MAPPING.md 2.1); every
page gets `umbracoUrlName` from its node alias unless a reparent rule says otherwise.

## Validation

Load-time (`SiteProfile.Load`, any problem is an `InvalidDataException`, exit code 2): unreadable
file or invalid JSON, a key the profile does not know (the error names it, so a misspelled key cannot turn a rule off), missing or empty required values (`name`, `kenticoClass`, `targetAlias`,
`source`, `target`, `editor`, `newParentPath`, `urlNameFrom`, `nameField`, `parentPath`, placement `path` and
`newParentPath`, settings folder `path` and `name`), a settings folder `path` that is not `/` plus exactly one segment without whitespace, null list
entries, an unknown `editor` value, a `target` alias that repeats within a type.

Plan-time (`PlanBuilder`, reported as a `PlanIssue` with severity `error`, exit code 2, nothing is
written): a `kenticoClass` unknown to the source, a path in `newParentPath`/`parentPath` or a placement
`path`/`newParentPath` that does not resolve to a planned item, a second placement of the same `path`, a settings folder path that is a source page, a parent chain (re-parenting or a placement) that leads back to the page itself.
