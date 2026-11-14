using System.Globalization;
using System.Text.RegularExpressions;
using KenticoMigrator.Kentico;

namespace KenticoMigrator.Migration;

public sealed partial class PlanBuilder
{
    /// <summary>Fixed (non-wildcard) aliases per page, in a stable order.</summary>
    private ILookup<int, string> _fixedAliases = Array.Empty<string>().ToLookup(_ => 0);

    private static readonly Regex Placeholder = PlaceholderRegex();

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    /// <summary>Reports wildcard aliases (application routes) and indexes the fixed ones.</summary>
    private void PrepareAliases()
    {
        var sourceById = SourceById();
        var ordered = _site.Aliases.OrderBy(a => a.NodeId).ThenBy(a => a.UrlPath, StringComparer.Ordinal).ToList();

        foreach (var alias in ordered.Where(a => a.IsWildcard))
        {
            var owner = sourceById.TryGetValue(alias.NodeId, out var doc) ? doc.AliasPath : string.Empty;
            _skipped.Add(new SkippedItem(
                SkipCategories.WildcardRoute,
                alias.UrlPath,
                owner,
                "Wildcard alias of an application route; it is not redirected."));
        }

        _fixedAliases = ordered.Where(a => !a.IsWildcard).ToLookup(a => a.NodeId, a => a.UrlPath);
    }

    /// <summary>Raw old URL forms of a page (research R10). Folders have none: their URL answered 404 before too.</summary>
    private IReadOnlyList<string> OldUrlsFor(Node node)
    {
        if (node.TypeAlias == PlanAliases.Folder)
        {
            return [];
        }

        var urls = new List<string>();
        var doc = node.Document;
        if (doc is not null)
        {
            urls.Add(doc.AliasPath);
            urls.Add(doc.AliasPath + ".aspx");
            if (!string.IsNullOrWhiteSpace(doc.UrlPath))
            {
                urls.Add(doc.UrlPath);
            }

            urls.AddRange(_fixedAliases[doc.NodeId]);
        }

        var route = OldRouteFor(node);
        if (route is not null)
        {
            urls.Add(route);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. urls.Select(EnsureLeadingSlash).Where(seen.Add)];
    }

    /// <summary>The re-parent rule's old route with <c>{Field}</c> filled from the raw (unnormalised) field value.</summary>
    private string? OldRouteFor(Node node)
    {
        var className = node.Document?.ClassName ?? node.RowClass;
        var pattern = _profile.Reparent
            .FirstOrDefault(r => string.Equals(r.KenticoClass, className, StringComparison.OrdinalIgnoreCase))?.OldRoutePattern;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var complete = true;
        var route = Placeholder.Replace(pattern, m =>
        {
            var value = Convert.ToString(Lookup(node.Source.Fields, m.Groups[1].Value), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(value))
            {
                complete = false;
                return string.Empty;
            }

            return value;
        });

        if (!complete)
        {
            Warning(node.OldPath, $"Old route pattern '{pattern}' has a placeholder without a value; no old route is planned.");
            return null;
        }

        return route;
    }

    private static string EnsureLeadingSlash(string url) => url.StartsWith('/') ? url : "/" + url;
}
