using System.Text.Json.Nodes;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class UmbracoTargetClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Doc(Guid typeKey, string name) => new()
    {
        ["documentType"] = new JsonObject { ["id"] = typeKey.ToString("D") },
        ["variants"] = new JsonArray(new JsonObject { ["name"] = name }),
    };

    [Fact]
    public async Task Children_carry_the_document_type_alias()
    {
        var api = new FakeUmbracoApi();
        var pageType = GuidFor(700);
        api.Seed("document-type", pageType, new JsonObject { ["alias"] = "contentPage" });
        api.Seed("document", GuidFor(1), Doc(pageType, "Home"));
        api.Seed("document", GuidFor(2), Doc(pageType, "About"), parent: GuidFor(1));
        var client = new UmbracoTargetClient(api);

        var roots = await client.GetDocumentChildrenAsync(null, Ct);
        var children = await client.GetDocumentChildrenAsync(GuidFor(1), Ct);

        var root = Assert.Single(roots);
        Assert.Equal(("contentPage", true), (root.DocumentTypeAlias, root.HasChildren));
        Assert.Equal(GuidFor(2), Assert.Single(children).Key);
    }

    [Fact]
    public async Task Counts_every_media_node_and_the_redirect_total()
    {
        var api = new FakeUmbracoApi { RedirectTotal = 7 };
        var folder = new JsonObject { ["mediaType"] = new JsonObject { ["id"] = Guid.NewGuid().ToString("D") }, ["variants"] = new JsonArray(new JsonObject { ["name"] = "f" }) };
        api.Seed("media", GuidFor(1), folder);
        api.Seed("media", GuidFor(2), folder.DeepClone().AsObject(), parent: GuidFor(1));
        api.Seed("media", GuidFor(3), folder.DeepClone().AsObject(), parent: GuidFor(1));
        var client = new UmbracoTargetClient(api);

        Assert.Equal(3, await client.CountMediaAsync(Ct));
        Assert.Equal(7, await client.CountRedirectsAsync(Ct));
    }
}
