using System.Text.Json;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Migration;

public sealed class PlanBuilderDocumentTests
{
    private static MigrationPlan Build(SourceSite? site = null, SiteProfile? profile = null) =>
        PlanBuilder.Build(site ?? SampleSite(), profile ?? SampleProfile());

    private static TargetDocument Find(MigrationPlan plan, string oldPath) =>
        plan.Documents.Single(d => d.OldPath == oldPath);

    [Fact]
    public void Excluded_prefix_and_descendants_are_skipped_with_reason()
    {
        var plan = Build();

        Assert.DoesNotContain(plan.Documents, d => d.OldPath.StartsWith("/Account", StringComparison.Ordinal));
        var skipped = plan.Skipped.Where(s => s.Category == SkipCategories.ApplicationPage).Select(s => s.OldPath).Order().ToList();
        Assert.Equal(["/Account", "/Account/Profile"], skipped);
        Assert.All(plan.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
    }

    [Fact]
    public void Folder_children_keep_their_path()
    {
        var plan = Build();

        var folder = Find(plan, "/Catalog");
        Assert.Equal("folder", folder.TypeAlias);
        Assert.Equal(Find(plan, "/Catalog").Key, Find(plan, "/Catalog/Products").ParentKey);
        Assert.Equal(Find(plan, "/Catalog/Products").Key, Find(plan, "/Catalog/Products/Tandem").ParentKey);
    }

    [Fact]
    public void Top_level_pages_go_under_home_and_home_is_allowed_at_root()
    {
        var plan = Build();

        var home = Find(plan, "/Home");
        Assert.Null(home.ParentKey);
        Assert.Equal(home.Key, Find(plan, "/About").ParentKey);
        Assert.Equal(home.Key, Find(plan, "/Catalog").ParentKey);

        var homeType = plan.DocumentTypes.Single(t => t.Alias == home.TypeAlias);
        Assert.True(homeType.AllowedAtRoot);
        Assert.Contains("folder", homeType.AllowedChildAliases);
        var folderType = plan.DocumentTypes.Single(t => t.Alias == "folder");
        Assert.Contains("brandList", folderType.AllowedChildAliases);
    }

    [Fact]
    public void Path_override_selects_the_other_document_type()
    {
        var plan = Build();

        Assert.Equal("articleList", Find(plan, "/Articles").TypeAlias);
        Assert.Equal("brandList", Find(plan, "/Catalog/Brands").TypeAlias);
        Assert.Equal("contentPage", Find(plan, "/About").TypeAlias);
        Assert.Equal("article", Find(plan, "/Articles/First-trip").TypeAlias);
    }

    [Fact]
    public void Brand_is_reparented_and_url_name_is_node_alias()
    {
        var site = SampleSite();
        // The brand sits under About in Kentico; the profile moves it below the brands container.
        var moved = site.Documents.Select(d => d.NodeId == 7 ? d with { ParentNodeId = 2 } : d).ToList();

        var plan = Build(site with { Documents = moved });

        var brand = Find(plan, "/Catalog/Brands/Sea-Spirit");
        Assert.Equal(Find(plan, "/Catalog/Brands").Key, brand.ParentKey);
        Assert.Equal("Sea-Spirit", brand.UrlName);
        Assert.Equal("Sea Spirit", brand.Name);
    }

    [Fact]
    public void First_region_to_bodyText_rest_to_additionalContent()
    {
        var site = Site(
        [
            Doc(1, null, "CMS.MenuItem", "/Home"),
            Doc(2, null, "CMS.MenuItem", "/Story", regions: [Region("a", "<p>one</p>"), Region("b", "<p>two</p>"), Region("c", "<p>three</p>")]),
            Doc(3, null, "CMS.MenuItem", "/Contact"),
        ]);

        var plan = Build(site);

        var story = Find(plan, "/Story");
        Assert.Equal("<p>one</p>", story.Values["bodyText"].Raw);
        Assert.Equal("<p>two</p>\n\n<p>three</p>", story.Values["additionalContent"].Raw);
        Assert.Equal("RichText", story.Values["bodyText"].Editor);
        Assert.Equal("Story", story.Values["title"].Raw);
        Assert.Null(Find(plan, "/Contact").Values["bodyText"].Raw);
    }

    [Fact]
    public void Unlinked_row_becomes_unpublished_with_stable_key()
    {
        var site = SampleSite() with
        {
            UnlinkedRows = new Dictionary<string, IReadOnlyList<SourceRow>>
            {
                ["DEMO.Brand"] = [new SourceRow(99, Fields(("Name", "Ghost Brand"), ("Order", 5)))],
            },
        };

        var plan = Build(site);

        var row = plan.Documents.Single(d => d.OldPath == "unlinked:DEMO.Brand:99");
        Assert.Equal(Keys.Derived("unlinked", "DEMO.Brand:99"), row.Key);
        Assert.False(row.Publish);
        Assert.Equal("brand", row.TypeAlias);
        Assert.Equal("Ghost Brand", row.Name);
        Assert.Equal("ghost-brand", row.UrlName);
        Assert.Equal(5, row.SortOrder);
        Assert.Equal(Find(plan, "/Catalog/Brands").Key, row.ParentKey);
        Assert.Equal(row.Key.ToString(), row.Values["migrationSourceKey"].Raw);
        Assert.Equal(row.Key, Build(site).Documents.Single(d => d.OldPath == row.OldPath).Key);
    }

    [Fact]
    public void Product_gets_sku_fields()
    {
        var plan = Build();

        var product = Find(plan, "/Catalog/Products/Tandem");
        Assert.Equal("K-100", product.Values["sku"].Raw);
        Assert.Equal(999.5m, product.Values["price"].Raw);
        Assert.Equal("Decimal", product.Values["price"].Editor);
        Assert.Equal(true, product.Values["featured"].Raw);
        Assert.Equal("express", product.Values["shippingImage"].Raw);
        Assert.Null(product.Values["availableItems"].Raw);
    }

    [Fact]
    public void Tags_are_split_on_comma()
    {
        var plan = Build();

        Assert.Equal(["ss", "sea"], Assert.IsAssignableFrom<IEnumerable<string>>(Find(plan, "/Catalog/Brands/Sea-Spirit").Values["synonyms"].Raw));
    }

    [Fact]
    public void Seo_and_navigation_values_come_from_document_metadata()
    {
        var site = Site(
        [
            Doc(1, null, "CMS.MenuItem", "/Home"),
            Doc(2, null, "CMS.MenuItem", "/About", pageTitle: "About the shop", hideInNavigation: true, showInSitemap: false),
        ]);

        var about = Find(Build(site), "/About");

        Assert.Equal("About the shop", about.Values["metaTitle"].Raw);
        Assert.Equal(true, about.Values["umbracoNaviHide"].Raw);
        Assert.Equal(true, about.Values["hideFromSitemap"].Raw);
        Assert.Equal(about.Key.ToString(), about.Values["migrationSourceKey"].Raw);
        Assert.Equal(1, about.SortOrder);
    }

    [Fact]
    public void Draft_is_planned_unpublished_with_schedule_note()
    {
        var site = Site(
        [
            Doc(1, null, "CMS.MenuItem", "/Home"),
            Doc(2, null, "CMS.MenuItem", "/Soon", published: false, publishFrom: new DateTime(2099, 1, 1)),
            Doc(3, null, "CMS.MenuItem", "/Draft", published: false),
        ]);

        var plan = Build(site);

        Assert.False(Find(plan, "/Soon").Publish);
        Assert.Contains("2099-01-01", Find(plan, "/Soon").ScheduleNote);
        Assert.False(Find(plan, "/Draft").Publish);
        Assert.Null(Find(plan, "/Draft").ScheduleNote);
        Assert.True(Find(plan, "/Home").Publish);
    }

    [Fact]
    public void Duplicate_sibling_url_name_is_plan_error()
    {
        var site = Site(
        [
            Doc(1, null, "CMS.MenuItem", "/Home"),
            Doc(2, null, "CMS.MenuItem", "/Twin", alias: "Twin", order: 1),
            Doc(3, 1, "CMS.MenuItem", "/Other/Twin", alias: "twin", order: 2),
        ]);

        var plan = Build(site);

        var issue = Assert.Single(plan.Issues, i => i.Severity == PlanSeverities.Error);
        Assert.Equal("/Other/Twin", issue.OldPath);
        Assert.Contains("/Twin", issue.Message);
    }

    [Fact]
    public void Linked_and_unmapped_documents_are_skipped()
    {
        var site = Site(
        [
            Doc(1, null, "CMS.MenuItem", "/Home"),
            Doc(2, null, "CMS.MenuItem", "/Mirror", linkedNodeId: 1),
            Doc(3, null, "CMS.Folder", "/Misc"),
        ], types: [.. SampleTypes(), Type("CMS.Folder")]);

        var plan = Build(site);

        Assert.Equal(["/Home"], plan.Documents.Select(d => d.OldPath));
        Assert.Single(plan.Skipped, s => s.Category == SkipCategories.LinkedNode && s.OldPath == "/Mirror");
        Assert.Single(plan.Skipped, s => s.Category == SkipCategories.UnmappedType && s.OldPath == "/Misc");
    }

    [Fact]
    public void Child_of_unmapped_parent_attaches_to_nearest_planned_ancestor_with_warning()
    {
        var site = Site(
        [
            Doc(1, null, "CMS.MenuItem", "/Home"),
            Doc(2, null, "CMS.Folder", "/Misc"),
            Doc(3, 2, "CMS.MenuItem", "/Misc/Deep"),
        ], types: [.. SampleTypes(), Type("CMS.Folder")]);

        var plan = Build(site);

        Assert.Equal(Find(plan, "/Home").Key, Find(plan, "/Misc/Deep").ParentKey);
        var warning = Assert.Single(plan.Issues);
        Assert.Equal(PlanSeverities.Warning, warning.Severity);
        Assert.Equal("/Misc/Deep", warning.OldPath);
    }

    [Fact]
    public void Parents_come_before_children()
    {
        var plan = Build();
        var seen = new HashSet<Guid>();

        foreach (var doc in plan.Documents)
        {
            Assert.True(doc.ParentKey is null || seen.Contains(doc.ParentKey.Value), doc.OldPath);
            seen.Add(doc.Key);
        }
    }

    [Fact]
    public void Build_is_deterministic()
    {
        var site = SampleSite();
        var reversed = site with { Documents = [.. site.Documents.Reverse()] };

        var first = JsonSerializer.Serialize(Build(site));

        Assert.Equal(first, JsonSerializer.Serialize(Build(site)));
        Assert.Equal(first, JsonSerializer.Serialize(Build(reversed)));
    }

    [Fact]
    public void Unknown_kentico_class_is_plan_error()
    {
        var profile = SampleProfile();
        var broken = profile with { Types = [.. profile.Types, profile.Types[0] with { KenticoClass = "NOPE.Missing", TargetAlias = "ghost", PathOverride = null }] };

        var plan = Build(profile: broken);

        var issue = Assert.Single(plan.Issues, i => i.Severity == PlanSeverities.Error);
        Assert.Contains("NOPE.Missing", issue.Message);
    }

    [Fact]
    public void Unresolved_new_parent_path_is_plan_error()
    {
        var profile = SampleProfile();
        var broken = profile with { Reparent = [profile.Reparent[0] with { NewParentPath = "/Nowhere" }] };

        var plan = Build(profile: broken);

        Assert.Contains(plan.Issues, i => i.Severity == PlanSeverities.Error && i.Message.Contains("/Nowhere", StringComparison.Ordinal));
    }

    [Fact]
    public void Unresolved_unlinked_parent_path_is_plan_error()
    {
        var profile = SampleProfile();
        var broken = profile with { UnlinkedRows = [profile.UnlinkedRows[0] with { ParentPath = "/Nowhere" }] };
        var site = SampleSite() with
        {
            UnlinkedRows = new Dictionary<string, IReadOnlyList<SourceRow>> { ["DEMO.Brand"] = [new SourceRow(1, Fields(("Name", "X")))] },
        };

        var plan = Build(site, broken);

        Assert.Contains(plan.Issues, i => i.Severity == PlanSeverities.Error && i.Message.Contains("/Nowhere", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Documents, d => d.OldPath.StartsWith("unlinked:", StringComparison.Ordinal));
    }

    [Fact]
    public void Clean_sample_has_no_issues()
    {
        Assert.Empty(Build().Issues);
    }
}
