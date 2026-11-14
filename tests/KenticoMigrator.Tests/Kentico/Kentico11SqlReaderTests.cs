using KenticoMigrator.Kentico;
using Microsoft.Data.SqlClient;

namespace KenticoMigrator.Tests.Kentico;

/// <summary>Runs against the database created by scripts/restore-sample.sh; expected values are in sample/README.md.</summary>
[Trait("Category", "Integration")]
public sealed class Kentico11SqlReaderTests
{
    private static Kentico11SqlReader CreateReader() =>
        new(SampleDatabase.ConnectionString!, SampleDatabase.MediaRoot);

    private static Task<SourceSite> ReadSiteAsync(params string[] unlinked) =>
        CreateReader().ReadAsync(unlinked, CancellationToken.None);

    [IntegrationFact]
    public async Task Counts_per_class_match_sample_readme()
    {
        var counts = await CreateReader().CountAsync(CancellationToken.None);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["CMS.Root"] = 1,
                ["CMS.Folder"] = 2,
                ["CMS.MenuItem"] = 11,
                ["CMS.Article"] = 3,
                ["DEMO.Brand"] = 6,
                ["DEMO.Product"] = 2,
            }.OrderBy(p => p.Key),
            counts.DocumentsByClass.OrderBy(p => p.Key));
    }

    [IntegrationFact]
    public async Task Read_returns_site_and_documents_with_metadata()
    {
        var site = await ReadSiteAsync();

        Assert.Equal("KayakShop", site.SiteName);
        Assert.StartsWith("11.", site.KenticoVersion);
        Assert.Equal(25, site.Documents.Count);

        var root = Assert.Single(site.Documents, d => d.ClassName == "CMS.Root");
        Assert.Null(root.ParentNodeId);

        var story = site.Documents.Single(d => d.AliasPath == "/Story");
        Assert.Equal(Guid.Parse("0de50000-0000-4000-8000-000000000004"), story.NodeGuid);
        Assert.Equal("Story", story.NodeAlias);
        Assert.Equal(3, story.Order);
        Assert.Equal(root.NodeId, story.ParentNodeId);
        Assert.Equal("Our story", story.PageTitle);
        Assert.Equal("story,history", story.PageKeywords);
        Assert.False(story.HideInNavigation);
        Assert.True(story.ShowInSitemap);
        Assert.Equal("Story", story.Fields["MenuItemName"]);
        Assert.DoesNotContain("MenuItemID", story.Fields.Keys);
    }

    [IntegrationFact]
    public async Task Page_types_include_fields_from_form_definitions()
    {
        var site = await ReadSiteAsync();

        var product = Assert.Single(site.PageTypes, t => t.ClassName == "DEMO.Product");
        Assert.True(product.IsCoupled);
        Assert.Equal("DEMO_Product", product.TableName);
        Assert.Equal(["ProductName", "Featured", "ShippingInformation", "ShippingImage", "Images"], product.Fields.Select(f => f.Column));

        var shipping = product.Fields.Single(f => f.Column == "ShippingImage");
        Assert.Equal(["standard", "freight"], shipping.Options);
        Assert.Equal("docattachments", product.Fields.Single(f => f.Column == "Images").DataType);

        Assert.Contains(site.PageTypes, t => t is { ClassName: "CMS.Root", IsCoupled: false });
    }

    [IntegrationFact]
    public async Task Two_region_page_has_regions_in_template_order()
    {
        var site = await ReadSiteAsync();

        var story = site.Documents.Single(d => d.AliasPath == "/Story");

        // The document stores the body first; the template lists the intro first.
        Assert.Equal(2, story.Regions.Count);
        Assert.StartsWith("storyintro", story.Regions[0].Id);
        Assert.StartsWith("storybody", story.Regions[1].Id);
        Assert.Contains("short history", story.Regions[0].Html);

        var about = site.Documents.Single(d => d.AliasPath == "/About");
        Assert.Single(about.Regions);
        Assert.Contains("&amp; we love the water", about.Regions[0].Html);

        Assert.Empty(site.Documents.Single(d => d.AliasPath == "/Contact").Regions);
    }

    [IntegrationFact]
    public async Task Draft_archived_and_scheduled_are_not_published()
    {
        var site = await ReadSiteAsync();

        Assert.False(site.Documents.Single(d => d.AliasPath == "/Draft-Guide").IsPublished);
        Assert.False(site.Documents.Single(d => d.AliasPath == "/Old-Offer").IsPublished);

        var scheduled = site.Documents.Single(d => d.AliasPath == "/Spring-Sale");
        Assert.False(scheduled.IsPublished);
        Assert.Equal(new DateTime(2099, 1, 1), scheduled.PublishFrom);

        Assert.True(site.Documents.Single(d => d.AliasPath == "/Home").IsPublished);
        Assert.Equal(22, site.Documents.Count(d => d.IsPublished));
    }

    [IntegrationFact]
    public async Task Product_has_sku_fields()
    {
        var site = await ReadSiteAsync();

        var product = site.Documents.Single(d => d.AliasPath == "/Catalog/Products/Pacific-Touring-Kayak");

        Assert.NotNull(product.Sku);
        Assert.Equal("KX-PT-100", product.Sku["SKUNumber"]);
        Assert.Equal(1299m, product.Sku["SKUPrice"]);
        Assert.Equal(1499m, product.Sku["SKURetailPrice"]);
        Assert.Equal(12, product.Sku["SKUAvailableItems"]);
        Assert.Equal(true, product.Sku["SKUEnabled"]);
        Assert.Equal("freight", product.Fields["ShippingImage"]);
        Assert.Equal(true, product.Fields["Featured"]);

        var river = site.Documents.Single(d => d.AliasPath == "/Catalog/Products/River-Runner-Kayak");
        Assert.Null(river.Sku!["SKURetailPrice"]);

        Assert.Null(site.Documents.Single(d => d.AliasPath == "/Home").Sku);
    }

    [IntegrationFact]
    public async Task Brand_with_space_keeps_name_and_node_alias()
    {
        var site = await ReadSiteAsync();

        var brand = site.Documents.Single(d => d.AliasPath == "/Catalog/Brands/Sea-Spirit");

        Assert.Equal("Sea Spirit", brand.Fields["Name"]);
        Assert.Equal("Sea-Spirit", brand.NodeAlias);
        Assert.Equal(5, brand.Fields["Order"]);
    }

    [IntegrationFact]
    public async Task Unlinked_brand_row_is_read()
    {
        var site = await ReadSiteAsync("DEMO.Brand");

        var row = Assert.Single(site.UnlinkedRows["DEMO.Brand"]);
        Assert.Equal(7, row.Id);
        Assert.Equal("Driftwood", row.Values["Name"]);
    }

    [IntegrationFact]
    public async Task Unlinked_rows_are_not_read_unless_requested()
    {
        var site = await ReadSiteAsync();

        Assert.Empty(site.UnlinkedRows);
    }

    [IntegrationFact]
    public async Task Rejects_non_11_version()
    {
        const string tempDb = "Kentico11Sample_Version12";
        await using var admin = new SqlConnection(SampleDatabase.ForDatabase("master"));
        await admin.OpenAsync();

        try
        {
            await ExecuteAsync(admin, $"IF DB_ID('{tempDb}') IS NOT NULL DROP DATABASE [{tempDb}]; CREATE DATABASE [{tempDb}];");
            await ExecuteAsync(admin, $"SELECT * INTO [{tempDb}].dbo.CMS_SettingsKey FROM Kentico11Sample.dbo.CMS_SettingsKey;");
            await ExecuteAsync(admin, $"UPDATE [{tempDb}].dbo.CMS_SettingsKey SET KeyValue = N'12.0' WHERE KeyName = N'CMSDBVersion';");

            var reader = new Kentico11SqlReader(SampleDatabase.ForDatabase(tempDb), SampleDatabase.MediaRoot);
            var ex = await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadAsync([], CancellationToken.None));

            Assert.Equal("Kentico 12.0 is not supported; this reader supports 11.x", ex.Message);
        }
        finally
        {
            await ExecuteAsync(admin, $"IF DB_ID('{tempDb}') IS NOT NULL BEGIN ALTER DATABASE [{tempDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{tempDb}]; END");
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class Kentico11SqlReaderConnectionTests
{
    [Fact]
    public void Connection_string_gets_read_only_intent()
    {
        var result = Kentico11SqlReader.ReadOnlyConnectionString("Server=db.example;Database=x;User Id=u;Password=p;ApplicationIntent=ReadWrite");

        Assert.Equal(ApplicationIntent.ReadOnly, new SqlConnectionStringBuilder(result).ApplicationIntent);
    }
}
