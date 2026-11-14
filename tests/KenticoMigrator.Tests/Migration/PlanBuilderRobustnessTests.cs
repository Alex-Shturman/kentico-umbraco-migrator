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
}
