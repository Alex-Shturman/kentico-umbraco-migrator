using KenticoMigrator.Run;
using KenticoMigrator.Tests.Support;

namespace KenticoMigrator.Tests.EndToEnd;

/// <summary>
/// Needs the Umbraco host running, <c>Umbraco:ClientSecret</c> in the migrator's user secrets and the sample database
/// (<c>KENTICO_SAMPLE_CS</c>). The tests share one target, so they run one after the other.
/// </summary>
[Trait("Category", "Integration")]
[Collection("EndToEnd")]
public sealed class PagesEndToEndTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sample_pages_exist_under_expected_parents()
    {
        using var host = EndToEndHost.Connect();

        var run = await host.RunAsync(CliArguments.Migrate, ct: Ct);

        Assert.True(run.ExitCode == ExitCodes.Success, run.Output);
        var tree = await TargetSnapshot.ReadAsync(host.Api, Ct);

        var home = tree.Find("Home");
        Assert.Null(home.ParentKey);
        Assert.Equal("contentPage", home.TypeAlias);
        Assert.Equal("Published", home.State);
        Assert.Equal("Home", home.Text("title"));
        Assert.Equal("Kayak Shop: touring, river and sea kayaks", home.Text("metaTitle"));
        Assert.Contains("Welcome to the Kayak Shop", home.Text("bodyText"));

        var story = tree.Find("Home", "Story");
        Assert.Equal(home.Key, story.ParentKey);
        Assert.Equal("Our story", story.Text("metaTitle"));
        Assert.Contains("A short history of the Kayak Shop", story.Text("bodyText"));
        Assert.Contains("The shop started in a garage", story.Text("additionalContent"));

        var article = tree.Find("Home", "Articles", "Paddling basics");
        Assert.Equal("article", article.TypeAlias);
        Assert.Equal(tree.Find("Home", "Articles").Key, article.ParentKey);
        Assert.Equal("Published", article.State);
        Assert.Equal("Paddling basics", article.Text("title"));
        Assert.Equal("2018-07-01 09:00:00", article.Text("publishDate"));

        var brandsFolder = tree.Find("Home", "Catalog", "Brands");
        Assert.Equal("contentFolder", tree.Find("Home", "Catalog").TypeAlias);
        var brand = tree.Find("Home", "Catalog", "Brands", "Aquila");
        Assert.Equal("brand", brand.TypeAlias);
        Assert.Equal(brandsFolder.Key, brand.ParentKey);
        Assert.Equal("Published", brand.State);
        Assert.Equal("Aquila", brand.Text("title"));

        var unlinkedBrand = tree.Find("Home", "Catalog", "Brands", "Driftwood");
        Assert.Equal("brand", unlinkedBrand.TypeAlias);
        Assert.Equal(brandsFolder.Key, unlinkedBrand.ParentKey);
        Assert.Equal("Draft", unlinkedBrand.State);
        Assert.Equal("Driftwood", unlinkedBrand.Text("title"));

        var product = tree.Find("Home", "Catalog", "Products", "Pacific Touring Kayak");
        Assert.Equal("product", product.TypeAlias);
        Assert.Equal("Published", product.State);
        Assert.Equal("Pacific Touring Kayak", product.Text("title"));
        Assert.Equal("KX-PT-100", product.Text("sku"));
        Assert.Equal(1299m, decimal.Parse(product.Text("price"), System.Globalization.CultureInfo.InvariantCulture));

        var draft = tree.Find("Home", "Draft guide");
        Assert.Equal(home.Key, draft.ParentKey);
        Assert.Equal("Draft", draft.State);
        Assert.Equal("Draft guide", draft.Text("title"));
    }
}
