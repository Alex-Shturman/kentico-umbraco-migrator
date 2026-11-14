using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Migration;

public sealed class PlanBuilderRobustnessTests
{
    [Fact]
    public void Duplicate_node_id_is_a_plan_error_not_a_crash()
    {
        var site = Site(
        [
            Doc(1, null, "CMS.MenuItem", "/Home"),
            Doc(2, null, "CMS.MenuItem", "/About"),
            Doc(2, null, "CMS.MenuItem", "/AboutCopy"),
        ]);

        var plan = PlanBuilder.Build(site, SampleProfile());

        Assert.Equal(["/About", "/Home"], plan.Documents.Select(d => d.OldPath).Order().ToList());
        Assert.Single(plan.Issues, i => i.Severity == PlanSeverities.Error && i.OldPath == "/AboutCopy");
    }

    [Fact]
    public void Unplanned_kentico_root_parent_gives_no_warning()
    {
        var site = Site(
        [
            Doc(100, null, "CMS.Root", "/"),
            Doc(1, 100, "CMS.MenuItem", "/Home"),
            Doc(2, 100, "CMS.MenuItem", "/About"),
        ], types: [.. SampleTypes(), Type("CMS.Root")]);

        var plan = PlanBuilder.Build(site, SampleProfile());

        Assert.DoesNotContain(plan.Issues, i => i.Severity == PlanSeverities.Warning);
        Assert.Equal(plan.Documents.Single(d => d.OldPath == "/Home").Key, plan.Documents.Single(d => d.OldPath == "/About").ParentKey);
    }

    [Fact]
    public void Unlinked_row_with_bad_order_or_unusable_name_does_not_crash()
    {
        var site = SampleSite() with
        {
            UnlinkedRows = new Dictionary<string, IReadOnlyList<SourceRow>>
            {
                ["DEMO.Brand"] =
                [
                    new SourceRow(1, Fields(("Name", "Okay"), ("Order", DBNull.Value))),
                    new SourceRow(2, Fields(("Name", "!!!"))),
                ],
            },
        };

        var plan = PlanBuilder.Build(site, SampleProfile());

        Assert.Equal(0, plan.Documents.Single(d => d.OldPath == "unlinked:DEMO.Brand:1").SortOrder);
        Assert.DoesNotContain(plan.Documents, d => d.OldPath == "unlinked:DEMO.Brand:2");
        Assert.Single(plan.Issues, i => i.Severity == PlanSeverities.Error && i.OldPath == "unlinked:DEMO.Brand:2");
    }

    [Fact]
    public void Allowed_child_alias_that_is_not_mapped_is_plan_error()
    {
        var profile = SampleProfile();
        var broken = profile with { Types = [profile.Types[0] with { AllowedChildren = ["contentPage", "ghostType", "folder"] }, .. profile.Types.Skip(1)] };

        var plan = PlanBuilder.Build(SampleSite(), broken);

        var issue = Assert.Single(plan.Issues, i => i.Severity == PlanSeverities.Error);
        Assert.Contains("ghostType", issue.Message);
    }

    [Fact]
    public void Reparent_under_a_page_of_the_same_class_is_plan_error()
    {
        var profile = SampleProfile();
        var broken = profile with { Reparent = [profile.Reparent[0] with { NewParentPath = "/Catalog/Brands/Other" }] };
        var site = SampleSite();
        site = site with { Documents = [.. site.Documents, Doc(12, 6, "DEMO.Brand", "/Catalog/Brands/Other")] };

        var plan = PlanBuilder.Build(site, broken);

        Assert.Contains(plan.Issues, i => i.Severity == PlanSeverities.Error && i.OldPath == "/Catalog/Brands/Sea-Spirit"
            && i.Message.Contains("same Kentico class", StringComparison.Ordinal));
    }

    [Fact]
    public void Reparent_cycle_is_plan_error_and_does_not_hang()
    {
        var profile = SampleProfile();
        var cyclic = profile with
        {
            Reparent =
            [
                profile.Reparent[0] with { NewParentPath = "/Catalog/Products/Tandem" },
                new ReparentRule("DEMO.Product", "/Catalog/Brands/Sea-Spirit", "NodeAlias", null),
            ],
        };

        var plan = PlanBuilder.Build(SampleSite(), cyclic);

        Assert.Contains(plan.Issues, i => i.Severity == PlanSeverities.Error && i.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Documents, d => d.OldPath == "/Catalog/Brands/Sea-Spirit");
        Assert.Contains(plan.Documents, d => d.OldPath == "/Catalog/Products/Tandem");
    }

    [Fact]
    public void Missing_parent_node_warns_for_nested_pages_but_not_for_top_level_pages()
    {
        var site = Site(
        [
            Doc(1, null, "CMS.MenuItem", "/Home"),
            Doc(2, 999, "CMS.MenuItem", "/About"),
            Doc(3, 998, "CMS.MenuItem", "/Lost/Page"),
        ]);

        var plan = PlanBuilder.Build(site, SampleProfile());

        var warning = Assert.Single(plan.Issues);
        Assert.Equal(PlanSeverities.Warning, warning.Severity);
        Assert.Equal("/Lost/Page", warning.OldPath);
    }

    [Fact]
    public void Case_insensitive_field_lookup_is_deterministic()
    {
        var site = SampleSite();
        var documents = site.Documents.Select(d => d.NodeId == 7 ? d with { Fields = Fields(("name", "lower"), ("NAME", "upper")) } : d).ToList();

        var plan = PlanBuilder.Build(site with { Documents = documents }, SampleProfile());

        Assert.Equal("upper", plan.Documents.Single(d => d.OldPath == "/Catalog/Brands/Sea-Spirit").Values["title"].Raw);
    }
}
