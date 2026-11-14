using System.Globalization;

namespace KenticoMigrator.Migration;

/// <summary>Reads the values a field mapping asks for (column, region, SKU, document metadata, media).</summary>
public sealed partial class PlanBuilder
{
    private Dictionary<string, PlannedValue> BuildValues(Node node)
    {
        var values = new Dictionary<string, PlannedValue>(StringComparer.Ordinal);
        var doc = node.Document;

        if (node.TypeAlias != PlanAliases.Folder)
        {
            values[PlanAliases.MetaTitle] = new("TextBox", doc?.PageTitle);
            values[PlanAliases.MetaDescription] = new("TextArea", doc?.PageDescription);
            values[PlanAliases.MetaKeywords] = new("TextBox", doc?.PageKeywords);
            values[PlanAliases.NaviHide] = new("TrueFalse", doc?.HideInNavigation ?? false);
            values[PlanAliases.HideFromSitemap] = new("TrueFalse", !(doc?.ShowInSitemap ?? true));
        }

        values[PlanAliases.SourceKey] = new("TextBox", node.Key.ToString());

        foreach (var field in node.Mapping?.Fields ?? [])
        {
            values[field.TargetAlias] = new(field.Editor, ResolveField(node, field));
        }

        return values;
    }

    private object? ResolveField(Node node, FieldMapping field)
    {
        var raw = ReadSource(node, field.Source);
        return field.Editor == "Tags" && raw is string text
            ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
            : raw;
    }

    private object? ReadSource(Node node, string source)
    {
        var values = node.Source;
        if (source.Equals("region:first", StringComparison.Ordinal))
        {
            return values.Regions.Count > 0 ? values.Regions[0].Html : null;
        }

        if (source.Equals("region:rest", StringComparison.Ordinal))
        {
            return values.Regions.Count > 1 ? string.Join("\n\n", values.Regions.Skip(1).Select(r => r.Html)) : null;
        }

        if (source.StartsWith("sku:", StringComparison.Ordinal))
        {
            return values.Sku is null ? null : Lookup(values.Sku, source[4..]);
        }

        if (source.StartsWith("file:", StringComparison.Ordinal))
        {
            var keys = MediaOf(node, source[5..]);
            return keys.Length > 0 ? keys[0] : null;
        }

        if (source.StartsWith("attachments:", StringComparison.Ordinal))
        {
            return MediaOf(node, source[12..]);
        }

        if (source.StartsWith("doc:", StringComparison.Ordinal))
        {
            return ReadDocumentMetadata(node, source[4..]);
        }

        return Lookup(values.Fields, source);
    }

    private object? ReadDocumentMetadata(Node node, string column)
    {
        var doc = node.Source.Document;
        switch (column)
        {
            case "DocumentName":
                return node.Source.Name;
            case "NodeAlias":
                return doc?.NodeAlias;
            case "NodeAliasPath":
                return doc?.AliasPath;
            case "DocumentPageTitle":
                return doc?.PageTitle;
            case "DocumentPageDescription":
                return doc?.PageDescription;
            case "DocumentPageKeyWords":
                return doc?.PageKeywords;
            case "DocumentCreatedWhen":
                return doc?.Created;
            case "DocumentModifiedWhen":
                return doc?.Modified;
            default:
                if (_unknownMetadata.Add(column))
                {
                    Error("profile", $"Unknown document metadata 'doc:{column}' in a field mapping.");
                }

                return null;
        }
    }

    private readonly HashSet<string> _unknownMetadata = new(StringComparer.Ordinal);

    /// <summary>Exact key first; otherwise the case-insensitive match whose key sorts first, so the result never depends on dictionary order.</summary>
    private static object? Lookup(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (values.TryGetValue(key, out var exact))
        {
            return exact;
        }

        return values
            .Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .FirstOrDefault();
    }
}
