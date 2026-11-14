namespace KenticoMigrator.Migration;

/// <summary>Planned redirects plus warnings for old URLs claimed by more than one document.</summary>
public sealed record RedirectPlanResult(
    IReadOnlyList<PlannedRedirect> Redirects,
    IReadOnlyList<PlanIssue> Warnings);

/// <summary>
/// Plans redirects from the old URL forms of published documents to their actual Umbraco URLs (FR-023).
/// A redirect is planned only when the old URL would not be served as is. Umbraco routing is case-insensitive
/// (verified, research R10), so a difference in case alone needs no redirect. Pure: the actual URLs are read back
/// from Umbraco by the caller. An old URL that is the actual URL of a page without a template (a folder) is planned like any
/// other and reported as a warning: Umbraco answers 404 for such a page before its redirect finder runs, so the redirect only
/// works with the host's <c>TemplateLessRedirectFinder</c>.
/// </summary>
public static class RedirectPlanner
{
    public static RedirectPlanResult Plan(
        IReadOnlyList<TargetDocument> documents,
        IReadOnlyDictionary<Guid, string> actualUrls,
        IReadOnlySet<string>? templateLessTypeAliases = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(actualUrls);

        var redirects = new List<PlannedRedirect>();
        var warnings = new List<PlanIssue>();
        var claimedBy = new Dictionary<string, TargetDocument>(StringComparer.Ordinal);
        var templateLess = TemplateLessRoutes(documents, actualUrls, templateLessTypeAliases);

        foreach (var doc in ClaimOrder.Sort(documents.Where(d => d.Publish && actualUrls.ContainsKey(d.Key))))
        {
            var actual = actualUrls[doc.Key];
            var actualRoute = Route(actual);
            var planned = new HashSet<string>(StringComparer.Ordinal);

            foreach (var oldUrl in doc.OldUrls)
            {
                var oldRoute = Route(oldUrl);
                if (oldRoute == actualRoute || !planned.Add(oldRoute))
                {
                    continue;
                }

                if (claimedBy.TryGetValue(oldRoute, out var winner))
                {
                    // FR-028: name pages by old path and key only, never by the URL text (it can contain a content value).
                    warnings.Add(new PlanIssue(
                        PlanSeverities.Warning,
                        doc.OldPath,
                        $"An old URL of this page is also claimed by '{winner.OldPath}' (key {winner.Key}); the redirect goes there."));
                    continue;
                }

                if (templateLess.TryGetValue(oldRoute, out var blocker) && blocker.Key != doc.Key)
                {
                    warnings.Add(new PlanIssue(
                        PlanSeverities.Warning,
                        doc.OldPath,
                        $"An old URL of this page is the URL of '{blocker.OldPath}' (key {blocker.Key}), a page with no template: Umbraco answers 404 there before redirects are tried. The redirect is registered and works only with the host's TemplateLessRedirectFinder."));
                }

                claimedBy.Add(oldRoute, doc);
                redirects.Add(new PlannedRedirect(oldUrl, doc.Key, actual, ReasonFor(doc, oldUrl, oldRoute, actualRoute)));
            }
        }

        return new RedirectPlanResult(redirects, warnings);
    }

    // Umbraco reports no URL ("cannot be routed") for a page without a template, so the route of such a published page is that of
    // its nearest routable descendant minus the segments in between (the URL names of the pages are single segments).
    private static Dictionary<string, TargetDocument> TemplateLessRoutes(
        IReadOnlyList<TargetDocument> documents,
        IReadOnlyDictionary<Guid, string> actualUrls,
        IReadOnlySet<string>? templateLessTypeAliases)
    {
        var routes = new Dictionary<string, TargetDocument>(StringComparer.Ordinal);
        if (templateLessTypeAliases is not { Count: > 0 })
        {
            return routes;
        }

        var children = documents.Where(d => d.ParentKey is not null).ToLookup(d => d.ParentKey!.Value);

        // The route of a page, or null when it and everything below it is not routable.
        string? RouteOf(TargetDocument doc, int depth)
        {
            if (actualUrls.TryGetValue(doc.Key, out var url))
            {
                return Route(url);
            }

            if (depth > documents.Count)
            {
                return null;
            }

            foreach (var child in children[doc.Key].Where(c => c.Publish))
            {
                if (RouteOf(child, depth + 1) is { } childRoute && childRoute.LastIndexOf('/') is var cut and > 0)
                {
                    return childRoute[..cut];
                }
            }

            return null;
        }

        foreach (var page in ClaimOrder.Sort(documents.Where(d => d.Publish && templateLessTypeAliases.Contains(d.TypeAlias))))
        {
            if (RouteOf(page, 0) is { } route)
            {
                routes.TryAdd(route, page);
            }
        }

        return routes;
    }

    // Comparison keeps ".aspx": Umbraco does not serve it, so "/About.aspx" is never the same URL as "/about".
    private static string Route(string url) => UrlIndex.NormalizeRoute(url, null, ignoreCase: true, stripAspx: false);

    private static string ReasonFor(TargetDocument doc, string oldUrl, string oldRoute, string actualRoute)
    {
        if (actualRoute == "/")
        {
            return RedirectReasons.Home;
        }

        if (oldRoute.EndsWith(".aspx", StringComparison.Ordinal))
        {
            return RedirectReasons.Aspx;
        }

        if (oldRoute == Route(doc.OldPath))
        {
            return RedirectReasons.UrlPath;
        }

        // A name with whitespace cannot be an Umbraco URL segment, so the page was renamed (for example a brand).
        return oldRoute.Any(char.IsWhiteSpace) ? RedirectReasons.Renamed : RedirectReasons.Alias;
    }
}
