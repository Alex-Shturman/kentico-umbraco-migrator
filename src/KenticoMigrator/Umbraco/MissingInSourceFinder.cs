using KenticoMigrator.Migration;

namespace KenticoMigrator.Umbraco;

/// <summary>
/// Finds migrated items whose source is gone (FR-015b): items that carry a <c>migrationSourceKey</c> but whose key is not in
/// the plan. The tool only reports them for manual review; there is no delete or unpublish code path anywhere.
/// </summary>
public static class MissingInSourceFinder
{
    /// <summary>
    /// Walks the document tree from the root and the media tree from the root. A planned item is descended into; an item that
    /// is not planned is read once, and when it has a source key it is reported and descended into (its children are
    /// presumably gone too). Items without a source key were made by editors and are left alone.
    /// </summary>
    public static async Task<IReadOnlyList<MissingInSource>> FindAsync(MigrationPlan plan, IUmbracoApi api, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(api);

        var missing = new List<MissingInSource>();
        await WalkAsync(api, UmbracoTree.DocumentKind, ItemKinds.Document, plan.Documents.Select(d => d.Key).ToHashSet(), missing, ct).ConfigureAwait(false);
        await WalkAsync(api, UmbracoTree.MediaKind, ItemKinds.Media, plan.Media.Select(m => m.Key).ToHashSet(), missing, ct).ConfigureAwait(false);
        return missing;
    }

    private static async Task WalkAsync(IUmbracoApi api, string treeKind, string itemKind, HashSet<Guid> planned, List<MissingInSource> missing, CancellationToken ct)
    {
        var pending = new Stack<Guid?>();
        pending.Push(null);
        while (pending.Count > 0)
        {
            foreach (var node in await UmbracoTree.ChildrenAsync(api, treeKind, pending.Pop(), ct).ConfigureAwait(false))
            {
                var descend = node.HasChildren;
                if (!planned.Contains(node.Key))
                {
                    var sourceKey = await ReadSourceKeyAsync(api, treeKind, node.Key, ct).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(sourceKey))
                    {
                        continue;
                    }

                    missing.Add(new MissingInSource(node.Key, itemKind, sourceKey, node.Name));
                }

                if (descend)
                {
                    pending.Push(node.Key);
                }
            }
        }
    }

    private static async Task<string?> ReadSourceKeyAsync(IUmbracoApi api, string treeKind, Guid key, CancellationToken ct)
    {
        var item = await api.GetAsync($"/{treeKind}/{key:D}", ct).ConfigureAwait(false);
        return item?["values"]?.AsArray()
            .FirstOrDefault(v => v?["alias"]?.GetValue<string>() == PlanAliases.SourceKey)?["value"]?.GetValue<string>();
    }
}
