using KenticoMigrator.Run;

namespace KenticoMigrator.Umbraco;

/// <summary>The count walk of <see cref="TargetCounter"/> over the Management API.</summary>
public sealed class UmbracoTargetClient(IUmbracoApi api) : ITargetTreeClient
{
    private readonly Dictionary<Guid, string> _typeAliases = [];

    public async Task<IReadOnlyList<TargetTreeNode>> GetDocumentChildrenAsync(Guid? parentKey, CancellationToken ct)
    {
        var nodes = await UmbracoTree.ChildrenAsync(api, UmbracoTree.DocumentKind, parentKey, ct).ConfigureAwait(false);
        var result = new List<TargetTreeNode>(nodes.Count);
        foreach (var node in nodes)
        {
            result.Add(new TargetTreeNode(node.Key, await AliasOfAsync(node.TypeKey, ct).ConfigureAwait(false), node.HasChildren));
        }

        return result;
    }

    /// <summary>Folders and files together: any media item that disappears is a drop.</summary>
    public async Task<int> CountMediaAsync(CancellationToken ct)
    {
        var count = 0;
        var pending = new Stack<Guid?>();
        pending.Push(null);
        while (pending.Count > 0)
        {
            foreach (var node in await UmbracoTree.ChildrenAsync(api, UmbracoTree.MediaKind, pending.Pop(), ct).ConfigureAwait(false))
            {
                count++;
                if (node.HasChildren)
                {
                    pending.Push(node.Key);
                }
            }
        }

        return count;
    }

    public async Task<int> CountRedirectsAsync(CancellationToken ct)
    {
        var page = await api.GetAsync("/redirect-management?skip=0&take=1", ct).ConfigureAwait(false);
        return page?["total"]?.GetValue<int>() ?? 0;
    }

    private async Task<string> AliasOfAsync(Guid typeKey, CancellationToken ct)
    {
        if (!_typeAliases.TryGetValue(typeKey, out var alias))
        {
            var type = await api.GetAsync($"/document-type/{typeKey:D}", ct).ConfigureAwait(false);
            _typeAliases[typeKey] = alias = type?["alias"]?.GetValue<string>() ?? typeKey.ToString("D");
        }

        return alias;
    }
}
