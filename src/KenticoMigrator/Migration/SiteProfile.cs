using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KenticoMigrator.Migration;

/// <summary>Site-specific rules loaded from <c>profiles/&lt;name&gt;.json</c>. See contracts/site-profile.md.</summary>
public sealed record SiteProfile(
    string Name,
    IReadOnlyList<string> OwnHosts,
    IReadOnlyList<string> ExcludedPathPrefixes,
    IReadOnlyList<string> FolderPaths,
    IReadOnlyList<string> ApplicationRoutePrefixes,
    IReadOnlyList<string> NotUploadedPaths,
    IReadOnlyList<TypeMapping> Types,
    IReadOnlyList<ReparentRule> Reparent,
    IReadOnlyList<UnlinkedRowRule> UnlinkedRows,
    string? MediaRoot)
{
    /// <summary>Allowed values of <see cref="FieldMapping.Editor"/> (case-sensitive).</summary>
    public static readonly FrozenSet<string> KnownEditors = new[]
    {
        "TextBox", "TextArea", "RichText", "Integer", "Decimal", "TrueFalse", "DateTime", "Tags",
        "MediaSingle", "MediaMultiple", "DropDown",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads and validates a profile. Every problem (unreadable file, bad JSON, missing or invalid
    /// values) is reported as <see cref="InvalidDataException"/> so the runner can map it to exit code 2.
    /// Checks that need the source (unknown Kentico class, unresolved parent paths) happen in PlanBuilder.
    /// </summary>
    public static SiteProfile Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // FileNotFoundException and DirectoryNotFoundException are IOExceptions.
            throw new InvalidDataException($"Cannot read profile '{path}': {ex.Message}", ex);
        }

        SiteProfile? raw;
        try
        {
            raw = JsonSerializer.Deserialize<SiteProfile>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Profile '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (raw is null)
        {
            throw new InvalidDataException($"Profile '{path}' is empty.");
        }

        return raw.NormalizeAndValidate();
    }

    // System.Text.Json leaves missing values null on positional records, so check them here.
    private SiteProfile NormalizeAndValidate()
    {
        RequireText(Name, "profile", "name");

        var profile = this with
        {
            OwnHosts = Elements(OwnHosts, "ownHosts"),
            ExcludedPathPrefixes = Elements(ExcludedPathPrefixes, "excludedPathPrefixes"),
            FolderPaths = Elements(FolderPaths, "folderPaths"),
            ApplicationRoutePrefixes = Elements(ApplicationRoutePrefixes, "applicationRoutePrefixes"),
            NotUploadedPaths = Elements(NotUploadedPaths, "notUploadedPaths"),
            Types = Elements(Types, "types").Select(NormalizeType).ToList(),
            Reparent = Elements(Reparent, "reparent").Select(NormalizeReparent).ToList(),
            UnlinkedRows = Elements(UnlinkedRows, "unlinkedRows").Select(NormalizeUnlinked).ToList(),
        };
        return profile;
    }

    private static TypeMapping NormalizeType(TypeMapping type)
    {
        RequireText(type.KenticoClass, "types[]", "kenticoClass");
        var where = $"type '{type.TargetAlias}'";
        RequireText(type.TargetAlias, $"types[] for '{type.KenticoClass}'", "targetAlias");

        var fields = Elements(type.Fields, $"{where} fields");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            RequireText(field.TargetAlias, where, "target");
            RequireText(field.Source, $"{where}, field '{field.TargetAlias}'", "source");
            RequireText(field.Editor, $"{where}, field '{field.TargetAlias}'", "editor");

            if (!KnownEditors.Contains(field.Editor))
            {
                throw new InvalidDataException(
                    $"Type '{type.TargetAlias}': unknown editor '{field.Editor}' for field '{field.TargetAlias}'.");
            }

            if (!seen.Add(field.TargetAlias))
            {
                throw new InvalidDataException(
                    $"Type '{type.TargetAlias}': target alias '{field.TargetAlias}' is used more than once.");
            }
        }

        return type with
        {
            TargetName = string.IsNullOrWhiteSpace(type.TargetName) ? type.TargetAlias : type.TargetName,
            AllowedChildren = Elements(type.AllowedChildren, $"{where} allowedChildren"),
            Fields = fields.Select(f => f with { Group = f.Group ?? string.Empty }).ToList(),
        };
    }

    private static ReparentRule NormalizeReparent(ReparentRule rule)
    {
        RequireText(rule.KenticoClass, "reparent[]", "kenticoClass");
        RequireText(rule.NewParentPath, $"reparent for '{rule.KenticoClass}'", "newParentPath");
        RequireText(rule.UrlNameFrom, $"reparent for '{rule.KenticoClass}'", "urlNameFrom");
        return rule;
    }

    private static UnlinkedRowRule NormalizeUnlinked(UnlinkedRowRule rule)
    {
        RequireText(rule.KenticoClass, "unlinkedRows[]", "kenticoClass");
        RequireText(rule.NameField, $"unlinkedRows for '{rule.KenticoClass}'", "nameField");
        RequireText(rule.ParentPath, $"unlinkedRows for '{rule.KenticoClass}'", "parentPath");
        return rule;
    }

    private static IReadOnlyList<T> Elements<T>(IReadOnlyList<T>? list, string what)
    {
        if (list is null)
        {
            return [];
        }

        if (list.Any(e => e is null))
        {
            throw new InvalidDataException($"'{what}' contains a null entry.");
        }

        return list;
    }

    private static void RequireText(string? value, string where, string property)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"In {where}: required value '{property}' is missing or empty.");
        }
    }
}

/// <summary>Maps a Kentico page type (optionally one path) to an Umbraco document type.</summary>
public sealed record TypeMapping(
    string KenticoClass,
    string TargetAlias,
    string TargetName,
    bool HasTemplate,
    IReadOnlyList<string> AllowedChildren,
    IReadOnlyList<FieldMapping> Fields,
    string? PathOverride);

/// <summary>
/// Maps one source value to one property. <paramref name="Source"/> is a column, <c>region:first</c>,
/// <c>region:rest</c>, <c>sku:&lt;column&gt;</c>, <c>doc:&lt;metadata&gt;</c>, <c>file:&lt;column&gt;</c> or
/// <c>attachments:&lt;column&gt;</c>.
/// </summary>
public sealed record FieldMapping(
    string Source,
    [property: JsonPropertyName("target")] string TargetAlias,
    string Editor,
    string Group);

/// <summary>Moves pages of a class under another parent.</summary>
public sealed record ReparentRule(
    string KenticoClass,
    string NewParentPath,
    string UrlNameFrom,
    string? OldRoutePattern);

/// <summary>Reads rows of a class that have no document and plans them as documents.</summary>
public sealed record UnlinkedRowRule(
    string KenticoClass,
    string NameField,
    string ParentPath,
    bool Publish);
