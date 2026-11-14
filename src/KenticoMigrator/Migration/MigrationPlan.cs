using KenticoMigrator.Kentico;

namespace KenticoMigrator.Migration;

/// <summary>
/// Output of <c>PlanBuilder.Build</c>; deterministic. <see cref="Issues"/> with severity
/// <see cref="PlanSeverities.Error"/> block all writes (FR-010).
/// </summary>
public sealed record MigrationPlan(
    IReadOnlyList<TargetDataType> DataTypes,
    IReadOnlyList<TargetDocumentType> DocumentTypes,
    IReadOnlyList<TargetTemplate> Templates,
    IReadOnlyList<TargetMedia> Media,
    IReadOnlyList<TargetDocument> Documents,
    IReadOnlyList<SkippedItem> Skipped,
    IReadOnlyList<PlanIssue> Issues);

/// <summary>Built-in data types are looked up by name; only non-built-ins are created.</summary>
public sealed record TargetDataType(
    Guid Key,
    string Name,
    string EditorAlias,
    string EditorUiAlias,
    IReadOnlyDictionary<string, object?> Configuration,
    bool IsBuiltIn);

/// <summary>Keys are UUID v5 over <c>doctype:&lt;alias&gt;</c>.</summary>
public sealed record TargetDocumentType(
    Guid Key,
    string Alias,
    string Name,
    string? TemplateAlias,
    bool AllowedAtRoot,
    IReadOnlyList<string> AllowedChildAliases,
    IReadOnlyList<string> Compositions,
    IReadOnlyList<TargetProperty> Properties,
    bool IsElementOrComposition);

public sealed record TargetProperty(
    string Alias,
    string Name,
    Guid DataTypeKey,
    string Group,
    bool Mandatory);

/// <summary>Content comes from embedded Razor views.</summary>
public sealed record TargetTemplate(
    Guid Key,
    string Alias,
    string Name,
    string? MasterAlias,
    string Content);

public enum MediaType
{
    Folder,
    Image,
    File,
}

/// <summary>Folders have no <paramref name="Source"/>.</summary>
public sealed record TargetMedia(
    Guid Key,
    Guid? ParentKey,
    MediaType MediaType,
    string Name,
    MediaSource? Source,
    string SourcePath);

/// <summary>Exactly one of <paramref name="AttachmentGuid"/> and <paramref name="MediaFile"/> is set.</summary>
public sealed record MediaSource(
    Guid? AttachmentGuid,
    SourceMediaFile? MediaFile,
    string FileName,
    string MimeType);

/// <summary><paramref name="OldUrls"/> are raw old URL forms (research R10).</summary>
public sealed record TargetDocument(
    Guid Key,
    Guid? ParentKey,
    string TypeAlias,
    string Name,
    string UrlName,
    int SortOrder,
    bool Publish,
    IReadOnlyDictionary<string, PlannedValue> Values,
    string OldPath,
    IReadOnlyList<string> OldUrls,
    string? ScheduleNote);

/// <summary>Rich text stays raw until <c>RichTextRewriter</c> runs.</summary>
public sealed record PlannedValue(string Editor, object? Raw);

/// <summary>Why something in the source is not in the plan.</summary>
public sealed record SkippedItem(
    string Category,
    string Identifier,
    string OldPath,
    string Reason);

/// <summary>Values for <see cref="SkippedItem.Category"/>.</summary>
public static class SkipCategories
{
    public const string ApplicationPage = "application page";
    public const string HostFile = "host file";
    public const string AttachmentHistory = "attachment history";
    public const string WildcardRoute = "wildcard route";
    public const string MissingFile = "missing file";
    public const string UnmappedType = "unmapped type";
}

public sealed record PlanIssue(
    string Severity,
    string OldPath,
    string Message);

/// <summary>Values for <see cref="PlanIssue.Severity"/>.</summary>
public static class PlanSeverities
{
    public const string Error = "error";
    public const string Warning = "warning";
}

public enum LinkKind
{
    Document,
    Media,
    Application,
    External,
    Mail,
    Anchor,
    Unknown,
}

/// <summary>Rewriting already rewritten HTML yields the same HTML.</summary>
public sealed record RewriteResult(
    string Html,
    int Rewritten,
    int External,
    int ToApplication,
    IReadOnlyList<string> Unresolved);

/// <summary>No redirect when the normalised old URL equals the normalised new URL.</summary>
public sealed record PlannedRedirect(
    string OldUrl,
    Guid TargetKey,
    string NewUrl,
    string Reason);

/// <summary>Values for <see cref="PlannedRedirect.Reason"/>.</summary>
public static class RedirectReasons
{
    public const string Aspx = "aspx";
    public const string Alias = "alias";
    public const string Home = "home";
    public const string Renamed = "renamed";
    public const string UrlPath = "url-path";
}
