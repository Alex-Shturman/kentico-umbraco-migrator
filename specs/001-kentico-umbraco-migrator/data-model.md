# Data Model: Kentico to Umbraco Content Migrator

All types are immutable C# `record`s in the console tool. Namespaces follow folders:
`KenticoMigrator.Kentico`, `KenticoMigrator.Migration`, `KenticoMigrator.Umbraco`,
`KenticoMigrator.Run`. Collections are `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>`.

## 1. Source model (`Kentico/IKenticoReader.cs`)

Produced only by `IKenticoReader`. Nothing outside `Kentico/` knows Kentico table names.

| Record | Fields | Rules |
|---|---|---|
| `SourceSite` | `SiteName`, `KenticoVersion`, `PageTypes`, `Documents`, `Attachments`, `Aliases`, `MediaFiles`, `UnlinkedRows: IReadOnlyDictionary<string, IReadOnlyList<SourceRow>>` | One site, one culture (reader throws otherwise) |
| `SourcePageType` | `ClassName`, `Fields: IReadOnlyList<SourceField>` | Only page types used by documents or listed in the profile |
| `SourceField` | `Column`, `DataType` (`text`, `longtext`, `integer`, `decimal`, `double`, `boolean`, `datetime`, `file`, `docattachments`, `guid`), `Size?`, `Options: IReadOnlyList<string>`, `FieldGuid` | `Options` parsed from the form definition for dropdown/radio controls |
| `SourceDocument` | `NodeGuid`, `NodeId`, `ParentNodeId?`, `ClassName`, `Name`, `NodeAlias`, `AliasPath`, `Order`, `IsPublished`, `PublishFrom?`, `PublishTo?`, `HideInNavigation`, `ShowInSitemap`, `PageTitle?`, `PageDescription?`, `PageKeywords?`, `Created`, `Modified`, `UrlPath?`, `Extension?`, `Fields: IReadOnlyDictionary<string, object?>`, `Regions: IReadOnlyList<EditableRegion>`, `Sku: IReadOnlyDictionary<string, object?>?`, `LinkedNodeId?` | `IsPublished` per research R5a; `Fields` keyed by column name of the coupled table; `Regions` in template order; `LinkedNodeId` is `NodeLinkedNodeID`: a linked node has no fields, regions or SKU and the plan builder skips it (`linked node`) |
| `EditableRegion` | `Id`, `Html` | `Html` decoded exactly once |
| `SourceAttachment` | `AttachmentGuid`, `NodeId`, `FileName`, `Extension`, `MimeType`, `FieldColumn?`, `IsUnsorted`, `Order` | Current versions only; `FieldColumn` set for file fields and attachment group fields |
| `SourceAlias` | `NodeId`, `UrlPath`, `IsWildcard` | `IsWildcard` when the path contains `{` or a wildcard rule is set |
| `SourceMediaFile` | `FileGuid`, `LibraryName`, `LibraryFolder`, `FilePath`, `FileName`, `Extension`, `MimeType` | `FilePath` relative to the library folder |
| `SourceRow` | `Id`, `Values: IReadOnlyDictionary<string, object?>` | Rows of a profile-listed class that have no document |
| `SourceCounts` | `DocumentsByClass: IReadOnlyDictionary<string,int>`, `Attachments`, `MediaFiles`, `Aliases` | Taken before and after a run |

## 2. Site profile (`Migration/SiteProfile.cs`)

Loaded from `profiles/<name>.json`; schema in [contracts/site-profile.md](contracts/site-profile.md).

| Record | Fields |
|---|---|
| `SiteProfile` | `Name`, `OwnHosts`, `ExcludedPathPrefixes`, `FolderPaths`, `ApplicationRoutePrefixes`, `NotUploadedPaths`, `Types: IReadOnlyList<TypeMapping>`, `Reparent: IReadOnlyList<ReparentRule>`, `UnlinkedRows: IReadOnlyList<UnlinkedRowRule>`, `MediaRoot?`, `SettingsFolder?`, `Placements: IReadOnlyList<PlacementRule>` (empty when the profile has none) |
| `TypeMapping` | `KenticoClass`, `TargetAlias`, `TargetName`, `HasTemplate`, `AllowedChildren`, `Fields: IReadOnlyList<FieldMapping>`, `PathOverride?` (a specific alias path mapped to another target type, e.g. `/Articles` to `articleList`) |
| `FieldMapping` | `Source` (column, `region:first`, `region:rest`, `sku:<column>`, `doc:<metadata>`), `TargetAlias`, `Editor`, `Group` |
| `ReparentRule` | `KenticoClass`, `NewParentPath`, `UrlNameFrom` (`NodeAlias` or a field), `OldRoutePattern?` (e.g. `/search-featured/{Name}`) |
| `UnlinkedRowRule` | `KenticoClass`, `NameField`, `ParentPath`, `Publish` (false for Q5) |
| `PlacementRule` | `Path` (source alias path of the page to move), `NewParentPath` (a planned page, the settings folder, or `/` for the top level below the home page), `UrlName?` (replaces the URL name of the moved page) |
| `SettingsFolderRule` | `Path` (`/` plus one segment, the name placements address the folder by; not the path of a source page), `Name` |

## 3. Migration plan (`Migration/MigrationPlan.cs`)

Produced by `PlanBuilder.Build(SourceSite, SiteProfile)`. Deterministic: same input, same plan.

| Record | Fields | Rules |
|---|---|---|
| `MigrationPlan` | `DataTypes`, `DocumentTypes`, `Templates`, `Media`, `Documents`, `Skipped`, `Issues` | `Issues` with severity `error` block all writes (FR-010) |
| `TargetDataType` | `Key`, `Name`, `EditorAlias`, `EditorUiAlias`, `Configuration: IReadOnlyDictionary<string, object?>`, `IsBuiltIn` | Built-ins are looked up by name; only non-built-ins are created |
| `TargetDocumentType` | `Key`, `Alias`, `Name`, `TemplateAlias?`, `AllowedAtRoot`, `AllowedChildAliases`, `Compositions`, `Properties: IReadOnlyList<TargetProperty>`, `IsElementOrComposition` | Keys are UUID v5 `doctype:<alias>` |
| `TargetProperty` | `Alias`, `Name`, `DataTypeKey`, `Group`, `Mandatory` | |
| `TargetTemplate` | `Key`, `Alias`, `Name`, `MasterAlias?`, `Content` | Content from embedded Razor views |
| `TargetMedia` | `Key`, `ParentKey?`, `MediaType` (`Folder`, `Image`, `File`), `Name`, `Source: MediaSource?`, `SourcePath` | Folders have no source |
| `MediaSource` | `AttachmentGuid?`, `MediaFile: SourceMediaFile?`, `FileName`, `MimeType` | Exactly one of the two sources is set |
| `TargetDocument` | `Key`, `ParentKey?`, `TypeAlias`, `Name`, `UrlName`, `SortOrder`, `Publish`, `Values: IReadOnlyDictionary<string, PlannedValue>`, `OldPath`, `OldUrls`, `ScheduleNote?` | `OldUrls` are raw old URL forms (research R10) |
| `PlannedValue` | `Editor`, `Raw: object?` | Rich text kept raw until `RichTextRewriter` runs |
| `SkippedItem` | `Category` (`application page`, `host file`, `wildcard route`, `missing file`, `unmapped type`, `linked node`, `unreferenced attachment`), `Identifier`, `OldPath`, `Reason` | |
| `PlanIssue` | `Severity` (`error`, `warning`), `OldPath`, `Message` | |

Key derivation (`Migration/Keys.cs`): `Keys.ForDocument(SourceDocument) = NodeGuid`,
`Keys.ForAttachment(Guid) = AttachmentGuid`, `Keys.ForMediaFile(Guid) = FileGuid`,
`Keys.Derived(string kind, string naturalKey)` = UUID v5 with namespace
`6f1b6d0e-6c2f-5a8e-9f41-6b2c0b7a4d11` and name `"{kind}:{naturalKey}"` (lowercase).

## 4. Links and redirects (`Migration/`)

| Record | Fields | Rules |
|---|---|---|
| `LinkKind` (enum) | `Document`, `Media`, `Application`, `External`, `Mail`, `Anchor`, `Unknown` | |
| `RewriteResult` | `Html`, `Rewritten`, `External`, `ToApplication`, `Unresolved: IReadOnlyList<string>` | Rewriting rewritten HTML yields the same HTML |
| `PlannedRedirect` | `OldUrl`, `TargetKey`, `NewUrl`, `Reason` (`aspx`, `alias`, `home`, `renamed`, `url-path`) | No redirect when normalised old equals normalised new |

String values are constants, not enums (some contain spaces). `SkippedItem.Category` uses
`SkipCategories` (`ApplicationPage` = `application page`, `HostFile` = `host file`,
`WildcardRoute` = `wildcard route`, `MissingFile` = `missing file`, `UnmappedType` =
`unmapped type`, `LinkedNode` = `linked node`, `UnreferencedAttachment` =
`unreferenced attachment`). Old attachment versions (`CMS_AttachmentHistory`) are never read, so
they have no category. `PlanIssue.Severity` uses `PlanSeverities`
(`Error` = `error`, `Warning` = `warning`). `PlannedRedirect.Reason` uses `RedirectReasons`
(`Aspx` = `aspx`, `Alias` = `alias`, `Home` = `home`, `Renamed` = `renamed`, `UrlPath` =
`url-path`). `MediaType` (`Folder`, `Image`, `File`) is an enum.

## 5. Sync results (`Umbraco/`)

| Record | Fields | Rules |
|---|---|---|
| `ItemOutcome` (enum) | `Created`, `Updated`, `Unchanged`, `EditedSkipped`, `EditedOverwritten`, `WouldCreate`, `WouldUpdate`, `Failed` | `Would*` only in preview |
| `ItemResult` | `Key`, `Kind` (`dataType`, `documentType`, `template`, `media`, `document`, `redirect`), `TypeAlias`, `OldPath`, `Outcome`, `Error?` | |
| `MissingInSource` | `Key`, `Kind`, `SourceKey`, `Name` | Items with a `migrationSourceKey` not in the plan (FR-015b) |

`ItemResult.Kind` uses `ItemKinds`: `DataType` = `dataType`, `DocumentType` = `documentType`,
`Template` = `template`, `Media` = `media`, `Document` = `document`, `Redirect` = `redirect`, `Sort` = `sort` (putting the children of one parent in the planned order; keyed by the parent).

Hidden properties on every migrated document (composition `migrationComposition`; media items carry none, see research R5-media):
`migrationSourceKey` (Textstring, read-only label) and `migrationFingerprint` (Textstring,
read-only label). The fingerprint is SHA-256 (hex) over canonical JSON of the owned values
(research R5).

## 6. Run and report (`Run/`)

| Record | Fields |
|---|---|
| `TargetCounts` | `DocumentsByType: IReadOnlyDictionary<string,int>`, `Media`, `Redirects` |
| `CountDrop` | `Type`, `Before`, `After` |
| `PhaseTiming` | `Phase` (`Phases` constants: `read`, `plan`, `snapshot`, `schema`, `media`, `documents`, `redirects`, `verify`), `Elapsed` |
| `LinkStats` | `Rewritten`, `External`, `ToApplication`, `Unresolved: IReadOnlyList<UnresolvedLink>` |
| `UnresolvedLink` | `OldPath` (page the link is on), `Href` |
| `RedirectSummary` | `Reason` (`RedirectReasons`), `Created`, `Existing`, `Failed` |
| `ScheduleNote` | `Key`, `OldPath`, `Note` (publish window not applied, FR-014a) |
| `RunReport` | `RunId`, `Command`, `StartedUtc`, `Duration`, `KenticoVersion`, `SourceCountsBefore?`, `SourceCountsAfter?`, `TargetCountsBefore?`, `TargetCountsAfter?` (null when the phase did not run, for example `plan` without a target), `CountDrops`, `Items: IReadOnlyList<ItemResult>` (summarised per kind and type), `Skipped`, `Issues`, `Links: LinkStats`, `Redirects: IReadOnlyList<RedirectSummary>`, `MissingInSource`, `Schedules: IReadOnlyList<ScheduleNote>`, `Timings`, `ExitCode`, `PlannedRedirects?: IReadOnlyList<PlannedRedirect>` (marked `JsonIgnore`: written to `redirects.csv` only, not repeated in `report.json`) |

Exit codes: `0` success, `1` item errors, `2` configuration or connection error or plan errors,
`3` count drop. Reports contain paths, keys, counts and timings only (FR-028).
