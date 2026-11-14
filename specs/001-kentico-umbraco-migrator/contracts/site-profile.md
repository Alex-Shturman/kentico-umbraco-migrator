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

## Fixed behaviour not configurable

SEO, navigation and migration compositions are added to every page type (MAPPING.md 2.1); every
page gets `umbracoUrlName` from its node alias unless a reparent rule says otherwise.

## Validation

Loading fails with exit code 2 when: a `kenticoClass` is unknown to the source, a `target` alias
repeats within a type, an `editor` value is unknown, or a path in `newParentPath`/`parentPath`
does not resolve to a planned item.
