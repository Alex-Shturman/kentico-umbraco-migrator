using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Migration;

public sealed class PlanBuilderUrlTests
{
    private static MigrationPlan Build(params SourceAlias[] aliases) =>
        PlanBuilder.Build(SampleSite() with { Aliases = aliases }, SampleProfile());

    private static TargetDocument Find(MigrationPlan plan, string oldPath) =>
        plan.Documents.Single(d => d.OldPath == oldPath);

    [Fact]
    public void Old_urls_include_aspx_form()
    {
        var about = Find(Build(), "/About");

        Assert.Equal(["/About", "/About.aspx"], about.OldUrls);
    }

    [Fact]
    public void Home_gets_old_home_path()
    {
        var home = Find(Build(), "/Home");

        Assert.Equal(["/Home", "/Home.aspx"], home.OldUrls);
    }

    [Fact]
    public void Brand_gets_old_route_with_raw_name()
    {
        var brand = Find(Build(), "/Catalog/Brands/Sea-Spirit");

        Assert.Contains("/brand-search/Sea Spirit", brand.OldUrls);
        Assert.Contains("/Catalog/Brands/Sea-Spirit.aspx", brand.OldUrls);
    }

    [Fact]
    public void Unlinked_brand_row_gets_old_route_only()
    {
        var site = SampleSite() with
        {
            UnlinkedRows = new Dictionary<string, IReadOnlyList<SourceRow>>
            {
                ["DEMO.Brand"] = [new SourceRow(99, Fields(("Name", "Ghost Brand")))],
            },
        };

        var plan = PlanBuilder.Build(site, SampleProfile());

        Assert.Equal(["/brand-search/Ghost Brand"], Find(plan, "unlinked:DEMO.Brand:99").OldUrls);
    }

    [Fact]
    public void Fixed_alias_and_document_url_path_are_old_urls_without_duplicates()
    {
        var site = SampleSite();
        var documents = site.Documents.Select(d => d.NodeId == 2 ? d with { UrlPath = "/custom/about" } : d).ToList();
        SourceAlias[] aliases = [new(2, "/contact", false), new(2, "/contact", false), new(2, "/about", false)];

        var plan = PlanBuilder.Build(site with { Documents = documents, Aliases = aliases }, SampleProfile());

        Assert.Equal(["/About", "/About.aspx", "/custom/about", "/contact"], Find(plan, "/About").OldUrls);
    }

    [Fact]
    public void Document_url_path_without_leading_slash_gets_one()
    {
        var site = SampleSite();
        var documents = site.Documents.Select(d => d.NodeId == 2 ? d with { UrlPath = "custom/about" } : d).ToList();

        var plan = PlanBuilder.Build(site with { Documents = documents }, SampleProfile());

        Assert.Contains("/custom/about", Find(plan, "/About").OldUrls);
    }

    [Fact]
    public void Wildcard_alias_is_reported_not_planned()
    {
        var plan = Build(new SourceAlias(4, "/{title}-i{ID}", true), new SourceAlias(2, "/contact", false));

        var skipped = Assert.Single(plan.Skipped, s => s.Category == SkipCategories.WildcardRoute);
        Assert.Equal("/{title}-i{ID}", skipped.Identifier);
        Assert.Equal("/Account/Profile", skipped.OldPath);
        Assert.All(plan.Documents, d => Assert.DoesNotContain("/{title}-i{ID}", d.OldUrls));
    }

    [Fact]
    public void Folders_and_aliases_of_unplanned_pages_get_no_old_urls()
    {
        var plan = Build(new SourceAlias(4, "/old-profile", false));

        Assert.Empty(Find(plan, "/Catalog").OldUrls);
        Assert.DoesNotContain(plan.Documents, d => d.OldUrls.Contains("/old-profile"));
    }
}
