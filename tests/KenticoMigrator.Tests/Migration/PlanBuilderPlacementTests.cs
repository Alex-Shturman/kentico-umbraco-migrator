using KenticoMigrator.Migration;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Migration;

public sealed class PlanBuilderPlacementTests
{
    private static MigrationPlan Build(SiteProfile profile) => PlanBuilder.Build(SampleSite(), profile);

    private static TargetDocument Doc(MigrationPlan plan, string oldPath) => plan.Documents.Single(d => d.OldPath == oldPath);

    [Fact]
    public void Container_is_placed_at_the_top_level_with_the_given_url_name_and_its_children_follow()
    {
        var profile = SampleProfile() with { Placements = [new PlacementRule("/Catalog/Brands", "/", "brand-list")] };

        var plan = Build(profile);

        Assert.DoesNotContain(plan.Issues, i => i.Severity == PlanSeverities.Error);
        var brands = Doc(plan, "/Catalog/Brands");
        Assert.Equal(Doc(plan, "/Home").Key, brands.ParentKey);
        Assert.Equal("brand-list", brands.UrlName);
        Assert.Equal(brands.Key, Doc(plan, "/Catalog/Brands/Sea-Spirit").ParentKey);
    }

    [Fact]
    public void Placement_without_a_url_name_keeps_the_node_alias()
    {
        var plan = Build(SampleProfile() with { Placements = [new PlacementRule("/Catalog/Brands", "/", null)] });

        Assert.Equal("Brands", Doc(plan, "/Catalog/Brands").UrlName);
    }

    [Theory]
    [InlineData("/Nowhere", "/")]
    [InlineData("/Catalog/Brands", "/Nowhere")]
    public void Placement_with_an_unresolved_path_is_a_plan_error(string path, string newParent)
    {
        var plan = Build(SampleProfile() with { Placements = [new PlacementRule(path, newParent, null)] });

        Assert.Contains(plan.Issues, i => i.Severity == PlanSeverities.Error && i.Message.Contains("Placement", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_folder_is_a_hidden_published_folder_at_the_top_level_and_receives_placed_containers()
    {
        var profile = SampleProfile() with
        {
            SettingsFolder = new SettingsFolderRule("/Settings", "Settings"),
            Placements = [new PlacementRule("/Catalog/Products", "/Settings", null)],
        };

        var plan = Build(profile);

        Assert.DoesNotContain(plan.Issues, i => i.Severity == PlanSeverities.Error);
        var settings = Doc(plan, "/Settings");
        Assert.Equal("contentFolder", settings.TypeAlias);
        Assert.Equal("Settings", settings.Name);
        Assert.Equal("Settings", settings.UrlName);
        Assert.Equal(Doc(plan, "/Home").Key, settings.ParentKey);
        Assert.True(settings.Publish);
        Assert.Equal(true, settings.Values[PlanAliases.NaviHide].Raw);
        Assert.Equal(true, settings.Values[PlanAliases.HideFromSitemap].Raw);
        Assert.Empty(settings.OldUrls);
        Assert.Equal(settings.Key, Doc(plan, "/Catalog/Products").ParentKey);
        Assert.Equal(Build(profile).Documents.Single(d => d.OldPath == "/Settings").Key, settings.Key); // deterministic
    }

    [Fact]
    public void Settings_folder_path_that_is_a_source_page_is_a_plan_error()
    {
        var plan = Build(SampleProfile() with { SettingsFolder = new SettingsFolderRule("/About", "Settings") });

        Assert.Contains(plan.Issues, i => i.Severity == PlanSeverities.Error && i.OldPath == "/About");
    }

    [Fact]
    public void Folder_type_carries_the_navigation_composition_so_the_settings_folder_can_be_hidden()
    {
        var plan = Build(SampleProfile());

        Assert.Contains("navigationComposition", plan.DocumentTypes.Single(t => t.Alias == "contentFolder").Compositions);
    }

    [Fact]
    public void Other_folders_get_no_navigation_values_so_their_fingerprint_is_unchanged()
    {
        var plan = Build(SampleProfile());

        Assert.DoesNotContain(PlanAliases.NaviHide, Doc(plan, "/Catalog").Values.Keys);
    }

    [Fact]
    public void Two_placements_of_the_same_path_are_a_plan_error()
    {
        var profile = SampleProfile() with
        {
            Placements = [new PlacementRule("/Catalog/Brands", "/", "one"), new PlacementRule("/Catalog/Brands", "/", "two")],
        };

        var plan = Build(profile);

        Assert.Contains(plan.Issues, i => i.Severity == PlanSeverities.Error && i.OldPath == "/Catalog/Brands" && i.Message == "Duplicate placement for this path.");
    }

    [Fact]
    public void Placement_that_creates_a_cycle_says_so_without_claiming_the_page_is_moved()
    {
        var profile = SampleProfile() with { Placements = [new PlacementRule("/Catalog", "/Catalog/Products", null)] };

        var plan = Build(profile);

        Assert.Contains(plan.Issues, i => i.Severity == PlanSeverities.Error && i.Message == "Re-parenting or a placement creates a cycle.");
    }
}
