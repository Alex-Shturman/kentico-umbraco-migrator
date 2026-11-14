namespace KenticoMigrator.Migration;

/// <summary>
/// The one ordering used whenever two documents claim the same old URL: the first in this order wins.
/// <see cref="UrlIndex"/> (link targets) and <see cref="RedirectPlanner"/> (redirect targets) both use it, so a link
/// and a redirect for the same old URL always lead to the same page.
/// </summary>
internal static class ClaimOrder
{
    public static IOrderedEnumerable<TargetDocument> Sort(IEnumerable<TargetDocument> documents) =>
        documents
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.OldPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Key);
}
