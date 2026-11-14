namespace KenticoMigrator.Migration;

/// <summary>
/// Plans redirects from the old URL forms of published documents to their actual Umbraco URLs (FR-023).
/// A redirect is planned only when the old URL would not be served as is. Pure: the actual URLs are read back
/// from Umbraco by the caller.
/// </summary>
public static class RedirectPlanner
{
    /// <summary>
    /// Whether Umbraco routing distinguishes upper and lower case. The default is the case-insensitive behaviour
    /// (<c>No_redirect_when_only_case_differs</c>); research R10 and task T019 confirm it against a running host.
    /// Flip this one constant, or pass the argument, when the host turns out to be case-sensitive.
    /// </summary>
    public const bool DefaultCaseSensitiveRouting = false;

    public static IReadOnlyList<PlannedRedirect> Plan(
        IReadOnlyList<TargetDocument> documents,
        IReadOnlyDictionary<Guid, string> actualUrls,
        bool caseSensitiveRouting = DefaultCaseSensitiveRouting) =>
        Plan(documents, actualUrls, out _, caseSensitiveRouting);

    /// <summary>As <see cref="Plan(IReadOnlyList{TargetDocument}, IReadOnlyDictionary{Guid, string}, bool)"/>, also returning warnings for old URLs claimed by more than one document.</summary>
    public static IReadOnlyList<PlannedRedirect> Plan(
        IReadOnlyList<TargetDocument> documents,
        IReadOnlyDictionary<Guid, string> actualUrls,
        out IReadOnlyList<PlanIssue> warnings,
        bool caseSensitiveRouting = DefaultCaseSensitiveRouting)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(actualUrls);

        var redirects = new List<PlannedRedirect>();
        var issues = new List<PlanIssue>();
        var claimedBy = new Dictionary<string, TargetDocument>(StringComparer.Ordinal);

        var ordered = documents
            .Where(d => d.Publish && actualUrls.ContainsKey(d.Key))
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.OldPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Key);

        foreach (var doc in ordered)
        {
            var actual = actualUrls[doc.Key];
            var actualRoute = Route(actual, caseSensitiveRouting);
            var planned = new HashSet<string>(StringComparer.Ordinal);

            foreach (var oldUrl in doc.OldUrls)
            {
                var oldRoute = Route(oldUrl, caseSensitiveRouting);
                if (oldRoute == actualRoute || !planned.Add(oldRoute))
                {
                    continue;
                }

                if (claimedBy.TryGetValue(oldRoute, out var winner))
                {
                    issues.Add(new PlanIssue(
                        PlanSeverities.Warning,
                        doc.OldPath,
                        $"Old URL '{oldUrl}' is also claimed by '{winner.OldPath}'; the redirect goes to '{winner.OldPath}'."));
                    continue;
                }

                claimedBy.Add(oldRoute, doc);
                redirects.Add(new PlannedRedirect(oldUrl, doc.Key, actual, ReasonFor(doc, oldUrl, oldRoute, actualRoute, caseSensitiveRouting)));
            }
        }

        warnings = issues;
        return redirects;
    }

    // Comparison keeps ".aspx": Umbraco does not serve it, so "/About.aspx" is never the same URL as "/about".
    private static string Route(string url, bool caseSensitive) =>
        UrlIndex.NormalizeRoute(url, null, ignoreCase: !caseSensitive, stripAspx: false);

    private static string ReasonFor(TargetDocument doc, string oldUrl, string oldRoute, string actualRoute, bool caseSensitive)
    {
        if (actualRoute == "/")
        {
            return RedirectReasons.Home;
        }

        if (oldUrl.TrimEnd('/').EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectReasons.Aspx;
        }

        if (oldRoute == Route(doc.OldPath, caseSensitive))
        {
            return RedirectReasons.UrlPath;
        }

        // A name with whitespace cannot be an Umbraco URL segment, so the page was renamed (for example a brand).
        return oldRoute.Any(char.IsWhiteSpace) ? RedirectReasons.Renamed : RedirectReasons.Alias;
    }
}
