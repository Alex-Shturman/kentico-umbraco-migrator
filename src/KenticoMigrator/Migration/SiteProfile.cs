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
    /// <summary>Allowed values of <see cref="FieldMapping.Editor"/>.</summary>
    public static readonly IReadOnlySet<string> KnownEditors = new HashSet<string>(StringComparer.Ordinal)
    {
        "TextBox", "TextArea", "RichText", "Integer", "Decimal", "TrueFalse", "DateTime", "Tags",
        "MediaSingle", "MediaMultiple", "DropDown",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads and validates a profile. Throws <see cref="InvalidDataException"/> when it is invalid.</summary>
    public static SiteProfile Load(string path)
    {
        SiteProfile? raw;
        try
        {
            raw = JsonSerializer.Deserialize<SiteProfile>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Profile '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (raw is null)
        {
            throw new InvalidDataException($"Profile '{path}' is empty.");
        }

        var profile = raw.Normalize();
        profile.Validate();
        return profile;
    }

    // System.Text.Json leaves missing collections null on positional records.
    private SiteProfile Normalize() => this with
    {
        OwnHosts = OwnHosts ?? [],
        ExcludedPathPrefixes = ExcludedPathPrefixes ?? [],
        FolderPaths = FolderPaths ?? [],
        ApplicationRoutePrefixes = ApplicationRoutePrefixes ?? [],
        NotUploadedPaths = NotUploadedPaths ?? [],
        Types = (Types ?? []).Select(t => t with
        {
            AllowedChildren = t.AllowedChildren ?? [],
            Fields = t.Fields ?? [],
        }).ToList(),
        Reparent = Reparent ?? [],
        UnlinkedRows = UnlinkedRows ?? [],
    };

    private void Validate()
    {
        foreach (var type in Types)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in type.Fields)
            {
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
