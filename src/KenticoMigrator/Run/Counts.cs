namespace KenticoMigrator.Run;

/// <summary>A document in the target tree as the count walk needs it.</summary>
public sealed record TargetTreeNode(Guid Key, string DocumentTypeAlias, bool HasChildren);

/// <summary>The part of the Umbraco API client that counting needs; the real client implements it.</summary>
public interface ITargetTreeClient
{
    /// <summary>All children of a document, or the root documents when <paramref name="parentKey"/> is null.</summary>
    Task<IReadOnlyList<TargetTreeNode>> GetDocumentChildrenAsync(Guid? parentKey, CancellationToken ct);

    Task<int> CountMediaAsync(CancellationToken ct);

    Task<int> CountRedirectsAsync(CancellationToken ct);
}

/// <summary>The drop guard of FR-026: no count may go down during a run.</summary>
public static class CountGuard
{
    /// <summary>Pseudo type names for the two totals that are not document types.</summary>
    public const string MediaType = "(media)";
    public const string RedirectsType = "(redirects)";

    /// <summary>
    /// Every type whose count after the run is lower than before. A type missing after counts as zero; a type
    /// that only exists after is not a drop. Document types come first in ordinal order, then media, then redirects.
    /// </summary>
    public static IReadOnlyList<CountDrop> FindDrops(TargetCounts before, TargetCounts after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var drops = new List<CountDrop>();
        foreach (var (type, was) in before.DocumentsByType.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var now = after.DocumentsByType.GetValueOrDefault(type);
            if (now < was)
            {
                drops.Add(new CountDrop(type, was, now));
            }
        }

        if (after.Media < before.Media)
        {
            drops.Add(new CountDrop(MediaType, before.Media, after.Media));
        }

        if (after.Redirects < before.Redirects)
        {
            drops.Add(new CountDrop(RedirectsType, before.Redirects, after.Redirects));
        }

        return drops;
    }
}

/// <summary>Takes the target snapshot used before and after a run.</summary>
public static class TargetCounter
{
    /// <summary>Walks the document tree once, counting documents per document type, and adds the media and redirect totals.</summary>
    public static async Task<TargetCounts> CountAsync(ITargetTreeClient client, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);

        var byType = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var pending = new Stack<Guid?>();
        pending.Push(null);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var node in await client.GetDocumentChildrenAsync(pending.Pop(), ct).ConfigureAwait(false))
            {
                byType[node.DocumentTypeAlias] = byType.GetValueOrDefault(node.DocumentTypeAlias) + 1;
                if (node.HasChildren)
                {
                    pending.Push(node.Key);
                }
            }
        }

        var media = await client.CountMediaAsync(ct).ConfigureAwait(false);
        var redirects = await client.CountRedirectsAsync(ct).ConfigureAwait(false);
        return new TargetCounts(byType, media, redirects);
    }
}
