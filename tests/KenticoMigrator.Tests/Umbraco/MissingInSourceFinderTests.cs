using System.Text.Json.Nodes;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class MissingInSourceFinderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MigrationPlan PlanOf(IReadOnlyList<TargetDocument> documents, IReadOnlyList<TargetMedia>? media = null) =>
        new([], [], [], media ?? [], documents, [], []);

    private static TargetDocument Doc(int id, int? parentId) =>
        new(GuidFor(id), parentId is null ? null : GuidFor(parentId.Value), "contentPage", "Page " + id, "page" + id, 1, true,
            new Dictionary<string, PlannedValue>(), "/page" + id, [], null);

    private static JsonObject Item(string name, string? sourceKey, string typeProperty = "documentType") => new()
    {
        [typeProperty] = new JsonObject { ["id"] = Guid.NewGuid().ToString("D") },
        ["variants"] = new JsonArray(new JsonObject { ["name"] = name }),
        ["values"] = sourceKey is null
            ? new JsonArray()
            : new JsonArray(new JsonObject { ["alias"] = PlanAliases.SourceKey, ["value"] = sourceKey }),
    };

    [Fact]
    public async Task Lists_migrated_items_not_in_plan()
    {
        var api = new FakeUmbracoApi();
        api.Seed("document", GuidFor(1), Item("Root", GuidFor(1).ToString()));
        api.Seed("document", GuidFor(2), Item("Child", GuidFor(2).ToString()), parent: GuidFor(1));
        var gone = GuidFor(90);
        var goneChild = GuidFor(91);
        api.Seed("document", gone, Item("Removed in Kentico", "node-90"), parent: GuidFor(1));
        api.Seed("document", goneChild, Item("Removed child", "node-91"), parent: gone);
        var plan = PlanOf([Doc(1, null), Doc(2, 1)]);

        var missing = await MissingInSourceFinder.FindAsync(plan, api, Ct);

        Assert.Equal(
            [(gone, ItemKinds.Document, "node-90", "Removed in Kentico"), (goneChild, ItemKinds.Document, "node-91", "Removed child")],
            missing.Select(m => (m.Key, m.Kind, m.SourceKey, m.Name)));
    }

    [Fact]
    public async Task Ignores_items_without_source_key()
    {
        var api = new FakeUmbracoApi();
        api.Seed("document", GuidFor(1), Item("Root", GuidFor(1).ToString()));
        api.Seed("document", GuidFor(80), Item("Editor page", null), parent: GuidFor(1));
        api.Seed("document", GuidFor(81), Item("Under editor page", "node-81"), parent: GuidFor(80));
        api.Seed("document", GuidFor(82), Item("Other site root", null));

        var missing = await MissingInSourceFinder.FindAsync(PlanOf([Doc(1, null)]), api, Ct);

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Lists_media_not_in_plan()
    {
        var api = new FakeUmbracoApi();
        var folder = new TargetMedia(GuidFor(500), null, MediaType.Folder, "Imported", null, "folder");
        api.Seed("media", folder.Key, Item("Imported", folder.Key.ToString(), "mediaType"));
        api.Seed("media", GuidFor(501), Item("old.jpg", "att-501", "mediaType"), parent: folder.Key);
        api.Seed("media", GuidFor(502), Item("uploaded by hand.jpg", null, "mediaType"), parent: folder.Key);

        var missing = await MissingInSourceFinder.FindAsync(PlanOf([], [folder]), api, Ct);

        var item = Assert.Single(missing);
        Assert.Equal((GuidFor(501), ItemKinds.Media, "att-501", "old.jpg"), (item.Key, item.Kind, item.SourceKey, item.Name));
    }

    [Fact]
    public async Task Never_writes()
    {
        var api = new FakeUmbracoApi();
        api.Seed("document", GuidFor(1), Item("Root", "x"));

        await MissingInSourceFinder.FindAsync(PlanOf([]), api, Ct);

        Assert.Empty(api.Writes);
    }
}
