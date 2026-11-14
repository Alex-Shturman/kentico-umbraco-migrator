namespace KenticoMigrator.Kentico;

/// <summary>The only seam to the source system. See contracts/kentico-reader.md.</summary>
public interface IKenticoReader
{
    /// <summary>Reads the whole site. Throws <see cref="NotSupportedException"/> when the version is not supported.</summary>
    Task<SourceSite> ReadAsync(IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct);

    /// <summary>Streams the binary of a current attachment version.</summary>
    Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct);

    /// <summary>Opens a media library file from the media root. Throws <see cref="FileNotFoundException"/> if missing.</summary>
    Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct);

    /// <summary>Counts used for the before/after snapshot.</summary>
    Task<SourceCounts> CountAsync(CancellationToken ct);
}

/// <summary>One site, one culture (the reader throws otherwise).</summary>
public sealed record SourceSite(
    string SiteName,
    string KenticoVersion,
    IReadOnlyList<SourcePageType> PageTypes,
    IReadOnlyList<SourceDocument> Documents,
    IReadOnlyList<SourceAttachment> Attachments,
    IReadOnlyList<SourceAlias> Aliases,
    IReadOnlyList<SourceMediaFile> MediaFiles,
    IReadOnlyDictionary<string, IReadOnlyList<SourceRow>> UnlinkedRows);

/// <summary>A page type used by documents or listed in the profile.</summary>
public sealed record SourcePageType(
    string ClassName,
    string? TableName,
    bool IsCoupled,
    IReadOnlyList<SourceField> Fields);

/// <summary>
/// A field of a page type. <paramref name="DataType"/> is one of text, longtext, integer, decimal,
/// double, boolean, datetime, file, docattachments, guid. <paramref name="Options"/> is parsed from the
/// form definition for dropdown and radio controls.
/// </summary>
public sealed record SourceField(
    string Column,
    string DataType,
    int? Size,
    string? Control,
    IReadOnlyList<string> Options,
    Guid FieldGuid);

/// <summary>
/// A page. <paramref name="Fields"/> is keyed by column name of the coupled table; <paramref name="Regions"/>
/// is in template order; <paramref name="IsPublished"/> follows research R5a.
/// </summary>
public sealed record SourceDocument(
    Guid NodeGuid,
    int NodeId,
    int? ParentNodeId,
    string ClassName,
    string Name,
    string NodeAlias,
    string AliasPath,
    int Order,
    bool IsPublished,
    DateTime? PublishFrom,
    DateTime? PublishTo,
    bool HideInNavigation,
    bool ShowInSitemap,
    string? PageTitle,
    string? PageDescription,
    string? PageKeywords,
    DateTime Created,
    DateTime Modified,
    string? UrlPath,
    string? Extension,
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyList<EditableRegion> Regions,
    IReadOnlyDictionary<string, object?>? Sku);

/// <summary>An editable region; <paramref name="Html"/> is decoded exactly once.</summary>
public sealed record EditableRegion(string Id, string Html);

/// <summary>A current attachment version; <paramref name="FieldColumn"/> is set for file and attachment group fields.</summary>
public sealed record SourceAttachment(
    Guid AttachmentGuid,
    int NodeId,
    string FileName,
    string Extension,
    string MimeType,
    long Size,
    int? Width,
    int? Height,
    string? Title,
    string? FieldColumn,
    bool IsUnsorted,
    int Order);

/// <summary>A document alias; <paramref name="IsWildcard"/> when the path contains <c>{</c> or a wildcard rule is set.</summary>
public sealed record SourceAlias(int NodeId, string UrlPath, bool IsWildcard);

/// <summary>A media library file; <paramref name="FilePath"/> is relative to the library folder.</summary>
public sealed record SourceMediaFile(
    Guid FileGuid,
    string LibraryName,
    string LibraryFolder,
    string FilePath,
    string FileName,
    string Extension,
    string MimeType,
    long Size,
    string? Title);

/// <summary>A row of a profile-listed class that has no document.</summary>
public sealed record SourceRow(int Id, IReadOnlyDictionary<string, object?> Values);

/// <summary>Counts taken before and after a run.</summary>
public sealed record SourceCounts(
    IReadOnlyDictionary<string, int> DocumentsByClass,
    int Attachments,
    int MediaFiles,
    int Aliases);
