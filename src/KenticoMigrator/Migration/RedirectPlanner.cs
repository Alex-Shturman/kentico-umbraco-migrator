namespace KenticoMigrator.Migration;

/// <summary>Planned redirects plus warnings for old URLs claimed by more than one document.</summary>
public sealed record RedirectPlanResult(
    IReadOnlyList<PlannedRedirect> Redirects,
    IReadOnlyList<PlanIssue> Warnings);

/// <summary>
/// Plans redirects from the old URL forms of published documents to their actual Umbraco URLs (FR-023).
/// A redirect is planned only when the old URL would not be served as is. Umbraco routing is case-insensitive
/// (verified, research R10), so a difference in case alone needs no redirect. Pure: the actual URLs are read back
/// from Umbraco by the caller.
/// </summary>
public static class RedirectPlanner
{
    public static RedirectPlanResult Plan(
        IReadOnlyList<TargetDocument> documents,
        IReadOnlyDictionary<Guid, string> actualUrls)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(actualUrls);

        var redirects = new List<PlannedRedirect>();
        var warnings = new List<PlanIssue>();
        var claimedBy = new Dictionary<string, TargetDocument>(StringComparer.Ordinal);

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

                claimedBy.Add(oldRoute, doc);
                redirects.Add(new PlannedRedirect(oldUrl, doc.Key, actual, ReasonFor(doc, oldUrl, oldRoute, actualRoute)));
            }
        }

        return new RedirectPlanResult(redirects, warnings);
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
