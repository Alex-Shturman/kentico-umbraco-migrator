using KenticoMigrator.Run;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Run;

public sealed class CountGuardTests
{
    private static TargetCounts Counts(int media = 0, int redirects = 0, params (string Type, int Count)[] types) =>
        new(types.ToDictionary(t => t.Type, t => t.Count), media, redirects);

    [Fact]
    public void Reports_drop_with_type_and_both_counts()
    {
        var drops = CountGuard.FindDrops(Counts(types: [("brand", 6), ("article", 3)]), Counts(types: [("brand", 4), ("article", 3)]));

        Assert.Equal([new CountDrop("brand", 6, 4)], drops);
    }

    [Fact]
    public void Growth_is_not_a_drop()
    {
        Assert.Empty(CountGuard.FindDrops(Counts(2, 1, ("brand", 6)), Counts(5, 4, ("brand", 9))));
    }

    [Fact]
    public void New_type_is_not_a_drop()
    {
        Assert.Empty(CountGuard.FindDrops(Counts(), Counts(types: [("brand", 6)])));
    }

    [Fact]
    public void Missing_type_after_is_a_drop()
    {
        var drops = CountGuard.FindDrops(Counts(types: [("brand", 6)]), Counts());

        Assert.Equal([new CountDrop("brand", 6, 0)], drops);
    }

    [Fact]
    public void Media_and_redirect_drops_are_reported_after_the_types()
    {
        var drops = CountGuard.FindDrops(Counts(10, 5, ("b", 2), ("a", 2)), Counts(8, 1, ("b", 1), ("a", 2)));

        Assert.Equal(
            [new CountDrop("b", 2, 1), new CountDrop(CountGuard.MediaType, 10, 8), new CountDrop(CountGuard.RedirectsType, 5, 1)],
            drops);
    }

    private sealed class FakeTree : ITargetTreeClient
    {
        private readonly Dictionary<Guid, List<TargetTreeNode>> _children = [];

        public List<Guid?> Requested { get; } = [];

        public FakeTree Add(Guid? parent, int id, string type, bool hasChildren)
        {
            var root = parent ?? Guid.Empty;
            if (!_children.TryGetValue(root, out var list))
            {
                _children[root] = list = [];
            }

            list.Add(new TargetTreeNode(GuidFor(id), type, hasChildren));
            return this;
        }

        public Task<IReadOnlyList<TargetTreeNode>> GetDocumentChildrenAsync(Guid? parentKey, CancellationToken ct)
        {
            Requested.Add(parentKey);
            return Task.FromResult<IReadOnlyList<TargetTreeNode>>(_children.TryGetValue(parentKey ?? Guid.Empty, out var list) ? list : []);
        }

        public Task<int> CountMediaAsync(CancellationToken ct) => Task.FromResult(7);

        public Task<int> CountRedirectsAsync(CancellationToken ct) => Task.FromResult(3);
    }

    [Fact]
    public async Task Counter_walks_the_tree_by_document_type()
    {
        var tree = new FakeTree()
            .Add(null, 1, "home", true)
            .Add(GuidFor(1), 2, "contentPage", false)
            .Add(GuidFor(1), 3, "brandList", true)
            .Add(GuidFor(3), 4, "brand", false)
            .Add(GuidFor(3), 5, "brand", false);

        var counts = await TargetCounter.CountAsync(tree, CancellationToken.None);

        Assert.Equal(new Dictionary<string, int> { ["home"] = 1, ["contentPage"] = 1, ["brandList"] = 1, ["brand"] = 2 }, counts.DocumentsByType);
        Assert.Equal(7, counts.Media);
        Assert.Equal(3, counts.Redirects);
        Assert.DoesNotContain(GuidFor(2), tree.Requested.Select(r => r ?? Guid.Empty));
    }

    [Fact]
    public async Task Counter_of_an_empty_target_is_zero()
    {
        var counts = await TargetCounter.CountAsync(new FakeTree(), CancellationToken.None);

        Assert.Empty(counts.DocumentsByType);
    }
}
