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
                ["CMS.Folder"] = 3,
                ["CMS.MenuItem"] = 12,
                ["CMS.Article"] = 3,
                ["CMS.File"] = 2,
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
        Assert.Equal(29, site.Documents.Count);

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
        Assert.Equal(26, site.Documents.Count(d => d.IsPublished));
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
    public async Task Linked_node_is_returned_with_linked_node_id()
    {
        var site = await ReadSiteAsync();

        var about = site.Documents.Single(d => d.AliasPath == "/About");
        var link = site.Documents.Single(d => d.AliasPath == "/Catalog/About-Link");

        Assert.Equal(about.NodeId, link.LinkedNodeId);
        Assert.Null(about.LinkedNodeId);
        Assert.Equal("CMS.MenuItem", link.ClassName);
        Assert.Empty(link.Fields);
        Assert.Empty(link.Regions);
        Assert.Null(link.Sku);
        Assert.Equal(1, site.Documents.Count(d => d.LinkedNodeId is not null));
        Assert.DoesNotContain(site.Attachments, a => a.NodeId == link.NodeId);
    }

    [IntegrationFact]
    public async Task Counts_match_what_read_returns()
    {
        var reader = CreateReader();
        var site = await reader.ReadAsync([], CancellationToken.None);
        var counts = await reader.CountAsync(CancellationToken.None);

        var fromRead = site.Documents.GroupBy(d => d.ClassName).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(fromRead.OrderBy(p => p.Key), counts.DocumentsByClass.OrderBy(p => p.Key));
        Assert.Equal(site.Attachments.Count, counts.Attachments);
        Assert.Equal(site.MediaFiles.Count, counts.MediaFiles);
        Assert.Equal(site.Aliases.Count, counts.Aliases);
    }

    [IntegrationFact]
    public async Task Unknown_unlinked_class_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ReadSiteAsync("DEMO.NoSuchType"));

        Assert.Contains("DEMO.NoSuchType", ex.Message);
    }

    [IntegrationFact]
    public async Task Unlinked_class_without_a_table_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ReadSiteAsync("CMS.Folder"));

        Assert.Contains("CMS.Folder", ex.Message);
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

    private static readonly Guid HistoryOnlyAttachment = Guid.Parse("a77ac000-0000-4000-8000-000000000090");

    [IntegrationFact]
    public async Task Reads_current_attachments_only()
    {
        var site = await ReadSiteAsync();

        Assert.Equal(7, site.Attachments.Count);
        Assert.DoesNotContain(site.Attachments, a => a.AttachmentGuid == HistoryOnlyAttachment);

        var documentsByNode = site.Documents.ToDictionary(d => d.NodeId);
        string FieldOf(string path) => site.Attachments.Single(a => documentsByNode[a.NodeId].AliasPath == path && a.FieldColumn is not null).FieldColumn!;

        // Attachment group fields match AttachmentGroupGUID to the field guid; file fields match the stored guid.
        Assert.All(site.Attachments.Where(a => documentsByNode[a.NodeId].ClassName == "DEMO.Product"), a => Assert.Equal("Images", a.FieldColumn));
        Assert.Equal("ArticleTeaserImage", FieldOf("/Articles/Paddling-Basics"));
        Assert.Equal("FileAttachment", FieldOf("/Downloads/Paddle-Size-Guide"));

        var unsorted = Assert.Single(site.Attachments, a => a.IsUnsorted);
        Assert.Null(unsorted.FieldColumn);
        Assert.Equal("/About", documentsByNode[unsorted.NodeId].AliasPath);

        var teaser = site.Attachments.Single(a => a.FieldColumn == "ArticleTeaserImage");
        Assert.Equal("paddling-teaser.jpg", teaser.FileName);
        Assert.Equal("jpg", teaser.Extension);
        Assert.Equal("image/jpeg", teaser.MimeType);
        Assert.Equal(96, teaser.Width);
        Assert.Equal(64, teaser.Height);
        Assert.Equal("Paddling teaser", teaser.Title);
        Assert.True(teaser.Size > 0);
    }

    [IntegrationFact]
    public async Task Group_attachments_are_ordered()
    {
        var site = await ReadSiteAsync();
        var river = site.Documents.Single(d => d.AliasPath == "/Catalog/Products/River-Runner-Kayak");

        var gallery = site.Attachments.Where(a => a.NodeId == river.NodeId && a.FieldColumn == "Images").ToList();

        // Inserted as front (order 2) then side (order 1); the reader returns them in attachment order.
        Assert.Equal(["river-side.jpg", "river-front.jpg"], gallery.Select(a => a.FileName));
        Assert.Equal([1, 2], gallery.Select(a => a.Order));
    }

    [IntegrationFact]
    public async Task Reads_media_library_files()
    {
        var site = await ReadSiteAsync();

        Assert.Equal(4, site.MediaFiles.Count);
        Assert.All(site.MediaFiles, f => Assert.Equal("Gallery", f.LibraryName));

        var nested = site.MediaFiles.Single(f => f.FilePath == "boats/kayak-blue.jpg");
        Assert.Equal("Gallery", nested.LibraryFolder);
        Assert.Equal("kayak-blue.jpg", nested.FileName);
        Assert.Equal("jpg", nested.Extension);
        Assert.Equal("image/jpeg", nested.MimeType);
        Assert.Equal(Guid.Parse("f11e0000-0000-4000-8000-000000000002"), nested.FileGuid);
        Assert.Equal(2, site.MediaFiles.Count(f => f.FilePath.StartsWith("boats/", StringComparison.Ordinal)));
    }

    [IntegrationFact]
    public async Task Open_media_file_streams_the_file_and_missing_file_throws_file_not_found()
    {
        var reader = CreateReader();
        var site = await reader.ReadAsync([], CancellationToken.None);

        var dock = site.MediaFiles.Single(f => f.FilePath == "dock.jpg");
        await using (var stream = await reader.OpenMediaFileAsync(dock, CancellationToken.None))
        {
            Assert.Equal(dock.Size, stream.Length);
            Assert.Equal(0xFF, stream.ReadByte()); // JPEG start of image
            Assert.Equal(0xD8, stream.ReadByte());
        }

        var missing = site.MediaFiles.Single(f => f.FilePath == "missing.jpg");
        await Assert.ThrowsAsync<FileNotFoundException>(() => reader.OpenMediaFileAsync(missing, CancellationToken.None));
    }

    [IntegrationFact]
    public async Task Open_media_file_does_not_leave_the_media_root()
    {
        var reader = CreateReader();
        var site = await reader.ReadAsync([], CancellationToken.None);
        var escaping = site.MediaFiles[0] with { FilePath = "../../kentico11-sample.sql" };

        await Assert.ThrowsAsync<InvalidDataException>(() => reader.OpenMediaFileAsync(escaping, CancellationToken.None));
    }

    [IntegrationFact]
    public async Task Open_attachment_streams_the_binary_of_the_current_version()
    {
        var reader = CreateReader();
        var site = await reader.ReadAsync([], CancellationToken.None);
        var teaser = site.Attachments.Single(a => a.FieldColumn == "ArticleTeaserImage");

        await using var stream = await reader.OpenAttachmentAsync(teaser.AttachmentGuid, CancellationToken.None);

        Assert.Equal(teaser.Size, stream.Length);
        Assert.Equal(0xFF, stream.ReadByte());
        Assert.Equal(0xD8, stream.ReadByte());

        // Versions in the history table are not served.
        await Assert.ThrowsAsync<FileNotFoundException>(() => reader.OpenAttachmentAsync(HistoryOnlyAttachment, CancellationToken.None));
    }

    [IntegrationFact]
    public async Task Counts_include_attachments_and_media_files()
    {
        var counts = await CreateReader().CountAsync(CancellationToken.None);

        Assert.Equal(7, counts.Attachments);
        Assert.Equal(4, counts.MediaFiles);
        Assert.Equal(4, counts.Aliases);
    }

    [IntegrationFact]
    public async Task Reads_aliases_and_flags_wildcards()
    {
        var site = await ReadSiteAsync();

        var nodeIds = site.Documents.ToDictionary(d => d.AliasPath, d => d.NodeId);
        Assert.Equal(4, site.Aliases.Count);

        Assert.Contains(new SourceAlias(nodeIds["/Contact"], "/contact", false), site.Aliases);
        Assert.Contains(new SourceAlias(nodeIds["/Contact"], "/visit", false), site.Aliases);
        Assert.Contains(new SourceAlias(nodeIds["/Catalog/Brands"], "/catalog", false), site.Aliases); // the URL of a folder (T064)

        var wildcard = Assert.Single(site.Aliases, a => a.IsWildcard);
        Assert.Equal(nodeIds["/Account/Profile"], wildcard.NodeId);
        Assert.Equal("/{title}-i{ID}", wildcard.UrlPath);

        // The hand-set URL path of a document is part of the document, not an alias.
        Assert.Equal("/about-the-shop", site.Documents.Single(d => d.AliasPath == "/About").UrlPath);
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
