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
| `CMS_SettingsKey` | `KeyName`, `KeyValue`, `SiteID` (keys `CMSDBVersion`, `CMSDefaultCultureCode`, `CMSDefaultAliasPath`) |
| `CMS_Site` | `SiteID`, `SiteName`, `SiteDomainName` |
| `CMS_Class` | `ClassID`, `ClassName`, `ClassTableName`, `ClassIsDocumentType`, `ClassIsCoupledClass`, `ClassFormDefinition` |
| `CMS_Tree` | `NodeID`, `NodeGUID`, `NodeParentID`, `NodeClassID`, `NodeAlias`, `NodeAliasPath`, `NodeOrder`, `NodeSiteID`, `NodeSKUID`, `NodeLinkedNodeID` |
| `CMS_Document` | `DocumentID`, `DocumentNodeID`, `DocumentName`, `DocumentCulture`, `DocumentForeignKeyValue`, `DocumentPageTitle`, `DocumentPageDescription`, `DocumentPageKeyWords`, `DocumentMenuItemHideInNavigation`, `DocumentShowInSiteMap`, `DocumentCanBePublished`, `DocumentPublishFrom`, `DocumentPublishTo`, `DocumentCreatedWhen`, `DocumentModifiedWhen`, `DocumentUrlPath`, `DocumentExtensions`, `DocumentContent`, `DocumentPageTemplateID` |
| `CMS_PageTemplate` | `PageTemplateID`, `PageTemplateWebParts` |
| `CMS_DocumentAlias` | `AliasNodeID`, `AliasURLPath`, `AliasWildcardRule`, `AliasCulture` |
| `CMS_Attachment` | `AttachmentGUID`, `AttachmentDocumentID`, `AttachmentName`, `AttachmentExtension`, `AttachmentMimeType`, `AttachmentSize`, `AttachmentImageWidth`, `AttachmentImageHeight`, `AttachmentTitle`, `AttachmentBinary`, `AttachmentGroupGUID`, `AttachmentIsUnsorted`, `AttachmentOrder` |
| `Media_Library` | `LibraryID`, `LibraryName`, `LibraryFolder`, `LibrarySiteID` |
| `Media_File` | `FileGUID`, `FileLibraryID`, `FilePath`, `FileName`, `FileExtension`, `FileMimeType`, `FileSize`, `FileTitle` |
| `COM_SKU` | `SKUID`, `SKUNumber`, `SKUPrice`, `SKURetailPrice`, `SKUAvailableItems`, `SKUEnabled` |
| Coupled tables | primary key column per form definition, plus all mapped columns |

## Behaviour

- Version: `CMSDBVersion` must start with `11.`; otherwise `NotSupportedException("Kentico <v> is not supported; this reader supports 11.x")`.
- More than one site or more than one culture: `NotSupportedException` naming the counts.
- Linked nodes (`NodeLinkedNodeID` not null): returned as skipped by the plan builder, not read as content.
- File fields: the coupled column holds an `AttachmentGUID`; attachment group fields match `AttachmentGroupGUID` to the field's `FieldGuid`.
- Editable regions: `DocumentContent` is XML `<content><webpart id="..."><![CDATA[...]]></webpart>...`; HTML is decoded exactly once; order follows the order of `editabletext` web parts in `PageTemplateWebParts`, unknown ids appended in document order.
- `IsPublished`: research R5a.
