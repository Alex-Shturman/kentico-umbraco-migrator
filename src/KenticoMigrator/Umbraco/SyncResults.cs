namespace KenticoMigrator.Umbraco;

/// <summary>Result of syncing one item. <c>Would*</c> values appear only in preview.</summary>
public enum ItemOutcome
{
    Created,
    Updated,
    Unchanged,
    EditedSkipped,
    EditedOverwritten,
    WouldCreate,
    WouldUpdate,
    Failed,
}

/// <summary>Values for <see cref="ItemResult.Kind"/>.</summary>
public static class ItemKinds
{
    public const string DataType = "dataType";
    public const string DocumentType = "documentType";
    public const string Template = "template";
    public const string Media = "media";
    public const string Document = "document";
    public const string Redirect = "redirect";
}

/// <summary>Reports carry keys, paths and outcomes only, never field values (FR-028).</summary>
public sealed record ItemResult(
    Guid Key,
    string Kind,
    string TypeAlias,
    string OldPath,
    ItemOutcome Outcome,
    string? Error);

/// <summary>An item with a <c>migrationSourceKey</c> that is not in the plan (FR-015b).</summary>
public sealed record MissingInSource(
    Guid Key,
    string Kind,
    string SourceKey,
    string Name);
