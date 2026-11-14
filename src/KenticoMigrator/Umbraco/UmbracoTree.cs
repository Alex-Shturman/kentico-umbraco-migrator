using System.Text.Json.Nodes;

namespace KenticoMigrator.Umbraco;

/// <summary>A document or media node as the tree endpoints list it.</summary>
public sealed record TreeNode(Guid Key, Guid? ParentKey, Guid TypeKey, bool HasChildren, string Name);

/// <summary>Reads the document and media trees page by page (100 per page).</summary>
public static class UmbracoTree
{
    public const string DocumentKind = "document";
    public const string MediaKind = "media";
    private const int PageSize = 100;

    /// <summary>
    /// All children of <paramref name="parentKey"/> in tree (sort) order, or the root nodes when it is null.
    /// A parent that does not exist has no children.
    /// </summary>
    public static async Task<IReadOnlyList<TreeNode>> ChildrenAsync(IUmbracoApi api, string kind, Guid? parentKey, CancellationToken ct)
    {
        var nodes = new List<TreeNode>();
        for (var skip = 0; ; skip += PageSize)
        {
            ct.ThrowIfCancellationRequested();
            var path = parentKey is null
                ? $"/tree/{kind}/root?skip={skip}&take={PageSize}"
                : $"/tree/{kind}/children?parentId={parentKey.Value:D}&skip={skip}&take={PageSize}";
            var page = await api.GetAsync(path, ct).ConfigureAwait(false);
            var items = page?["items"]?.AsArray() ?? [];
            nodes.AddRange(items.OfType<JsonObject>().Select(i => ToNode(i, kind, parentKey)));
            if (items.Count < PageSize)
            {
                return nodes;
            }
        }
    }

    private static TreeNode ToNode(JsonObject item, string kind, Guid? parentKey)
    {
        var typeNode = item[kind == DocumentKind ? "documentType" : "mediaType"];
        var name = item["variants"]?.AsArray().FirstOrDefault()?["name"]?.GetValue<string>() ?? string.Empty;
        return new TreeNode(
            Guid.Parse(item["id"]!.GetValue<string>()),
            parentKey,
            typeNode?["id"]?.GetValue<string>() is { } type ? Guid.Parse(type) : Guid.Empty,
            item["hasChildren"]?.GetValue<bool>() ?? false,
            name);
    }
}
