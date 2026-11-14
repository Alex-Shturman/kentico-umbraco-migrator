# Contract: `IKenticoReader`

The only seam in the tool (Constitution II, V). Records are defined in
[data-model.md](../data-model.md) section 1.

```csharp
namespace KenticoMigrator.Kentico;

public interface IKenticoReader
{
    /// Reads the whole site. Throws NotSupportedException when the version is not supported.
    Task<SourceSite> ReadAsync(IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct);

    /// Streams the binary of a current attachment version.
    Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct);

    /// Opens a media library file from Kentico:MediaRoot. Throws FileNotFoundException if missing.
    Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct);

    /// Counts used for the before/after snapshot.
    Task<SourceCounts> CountAsync(CancellationToken ct);
}
```

`ReadAsync` takes the class names whose rows without a document must be read (from the profile's
`unlinkedRows`), so `Kentico/` does not depend on `Migration/`.

## Kentico 11 implementation (`Kentico11SqlReader`)

Read-only: only `SELECT` statements; the connection string gets `ApplicationIntent=ReadOnly`.
Tables and columns relied on (the sample script must contain at least these):

| Table | Columns |
|---|---|
| `CMS_SettingsKey` | `KeyName`, `KeyValue`, `SiteID` (key `CMSDBVersion`; the keys `CMSDefaultCultureCode` and `CMSDefaultAliasPath` are not read, see Behaviour) |
| `CMS_Site` | `SiteID`, `SiteName`, `SiteDomainName` |
| `CMS_Class` | `ClassID`, `ClassName`, `ClassTableName`, `ClassIsDocumentType`, `ClassIsCoupledClass`, `ClassFormDefinition` |
| `CMS_Tree` | `NodeID`, `NodeGUID`, `NodeParentID`, `NodeClassID`, `NodeName`, `NodeAlias`, `NodeAliasPath`, `NodeLevel`, `NodeOrder`, `NodeSiteID`, `NodeSKUID`, `NodeLinkedNodeID` |
| `CMS_Document` | `DocumentID`, `DocumentNodeID`, `DocumentName`, `DocumentCulture`, `DocumentForeignKeyValue`, `DocumentPageTitle`, `DocumentPageDescription`, `DocumentPageKeyWords`, `DocumentMenuItemHideInNavigation`, `DocumentShowInSiteMap`, `DocumentCanBePublished`, `DocumentPublishFrom`, `DocumentPublishTo`, `DocumentCreatedWhen`, `DocumentModifiedWhen`, `DocumentUrlPath`, `DocumentExtensions`, `DocumentContent`, `DocumentPageTemplateID` |
| `CMS_PageTemplate` | `PageTemplateID`, `PageTemplateWebParts` |
| `CMS_DocumentAlias` | `AliasID`, `AliasNodeID`, `AliasURLPath`, `AliasWildcardRule`, `AliasCulture` |
| `CMS_Attachment` | `AttachmentGUID`, `AttachmentDocumentID`, `AttachmentName`, `AttachmentExtension`, `AttachmentMimeType`, `AttachmentBinary`, `AttachmentGroupGUID`, `AttachmentIsUnsorted`, `AttachmentOrder` |
| `Media_Library` | `LibraryID`, `LibraryName`, `LibraryFolder`, `LibrarySiteID` |
| `Media_File` | `FileGUID`, `FileLibraryID`, `FilePath`, `FileName`, `FileExtension`, `FileMimeType` |
| `COM_SKU` | `SKUID`, `SKUNumber`, `SKUPrice`, `SKURetailPrice`, `SKUAvailableItems`, `SKUEnabled` |
| Coupled tables | primary key column per form definition, plus all mapped columns |

## Behaviour

- Version: `CMSDBVersion` must start with `11.`; otherwise `NotSupportedException("Kentico <v> is not supported; this reader supports 11.x")`.
- Site and culture: more than one row in `CMS_Site`, or more than one distinct `CMS_Document.DocumentCulture`, is a `NotSupportedException` naming the counts. The `CMSDefaultCultureCode` and `CMSDefaultAliasPath` settings are not read (the profile and the plan builder decide the home path); the culture is whatever the documents carry.
- `ReadAsync` and `CountAsync` run the same version and single-site checks and use the same scope (the site's nodes), so the counts match what the read returns.
- Linked nodes (`NodeLinkedNodeID` not null): returned as `SourceDocument` with `LinkedNodeId` set and no fields, regions or SKU, and they own no attachments or aliases; the plan builder skips them and reports them. A linked node may lack a document row; its name then falls back to `NodeName`.
- File fields: the coupled column holds an `AttachmentGUID`; attachment group fields match `AttachmentGroupGUID` to the field's `FieldGuid`.
- Coupled rows: each coupled table is read once. The reader first reads the table's column names (`sys.columns`), matches the primary key and the mapped columns of the form definition to them case-insensitively, and then selects only those columns; a column named in the form definition but missing in the table (or a table that does not exist) is an `InvalidDataException` naming the page type, table and column. A class listed in `unlinkedRowClasses` that does not exist or has no coupled table is an `InvalidDataException`.
- Editable regions: `DocumentContent` is XML `<content><webpart id="..."><![CDATA[...]]></webpart>...`; HTML is decoded exactly once; order follows the order of `editabletext` web parts in `PageTemplateWebParts`, unknown ids appended in document order. XML from the database is parsed with DTDs prohibited; malformed XML is an `InvalidDataException` naming the page.
- `IsPublished`: research R5a (`PublishWindow.IsPublished`, local time).
- `OpenAttachmentAsync` looks the attachment up by `AttachmentGUID` in `CMS_Attachment` only (never the history table), `SELECT TOP 1 ... ORDER BY AttachmentID`, and buffers the binary in memory (one attachment at a time). A missing row or a NULL binary is a `FileNotFoundException`.
- `OpenMediaFileAsync` resolves `<media root>/<LibraryFolder>/<FilePath>`; rooted values, `..` escapes and a missing file are rejected (`InvalidDataException`, `FileNotFoundException`).
- Values: attachment and media `Extension` are lower case without the dot; media `FileName` includes the extension; media `FilePath` uses `/`.
