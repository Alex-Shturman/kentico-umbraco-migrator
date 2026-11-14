using System.Text.Json;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Run;
using KenticoMigrator.Umbraco;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Run;

public sealed class ReportTests : IDisposable
{
    private const string Marker = "ZZ-SECRET-FIELD-VALUE-ZZ";

    private readonly string _root = Directory.CreateTempSubdirectory("report-tests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string RunDir => Path.Combine(_root, "runs", "20260101-000000");

    private static RunReport SampleReport(
        IReadOnlyList<SkippedItem>? skipped = null,
        IReadOnlyList<PlanIssue>? issues = null,
        IReadOnlyList<ItemResult>? items = null,
        bool withTargetCounts = true) =>
        new(
            RunId: "20260101-000000",
            Command: "migrate",
            StartedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Duration: TimeSpan.FromSeconds(12),
            KenticoVersion: "11.0.0",
            SourceCountsBefore: new SourceCounts(new Dictionary<string, int> { ["DEMO.Brand"] = 6 }, 4, 3, 2),
            SourceCountsAfter: new SourceCounts(new Dictionary<string, int> { ["DEMO.Brand"] = 6 }, 4, 3, 2),
            TargetCountsBefore: withTargetCounts ? new TargetCounts(new Dictionary<string, int> { ["brand"] = 0 }, 0, 0) : null,
            TargetCountsAfter: withTargetCounts ? new TargetCounts(new Dictionary<string, int> { ["brand"] = 6 }, 7, 3) : null,
            CountDrops: [new CountDrop("article", 3, 1)],
            Items: items ??
            [
                new ItemResult(GuidFor(1), ItemKinds.Document, "brand", "/Brands/A", ItemOutcome.Created, null),
                new ItemResult(GuidFor(2), ItemKinds.Document, "brand", "/Brands/B", ItemOutcome.Created, null),
                new ItemResult(GuidFor(3), ItemKinds.Document, "brand", "/Brands/C", ItemOutcome.EditedSkipped, null),
                new ItemResult(GuidFor(4), ItemKinds.Document, "article", "/Articles/X", ItemOutcome.Failed, "HTTP 400"),
            ],
            Skipped: skipped ??
            [
                new SkippedItem(SkipCategories.ApplicationPage, "n1", "/Account", "Under excluded path."),
                new SkippedItem(SkipCategories.HostFile, "n2", "/Files/feed", "Not uploaded."),
                new SkippedItem(SkipCategories.HostFile, "n3", "/Files/ads", "Not uploaded."),
            ],
            Issues: issues ?? [new PlanIssue(PlanSeverities.Warning, "/Misc/Deep", "Parent is not migrated.")],
            Links: new LinkStats(5, 2, 1, [new UnresolvedLink("/Story", "/missing-page")]),
            Redirects: [new RedirectSummary(RedirectReasons.Aspx, 4, 1, 0)],
            MissingInSource: [new MissingInSource(GuidFor(9), ItemKinds.Document, "node-9", "Old page")],
            Schedules: [new ScheduleNote(GuidFor(8), "/Soon", "Publish window not applied: from 2099-01-01 00:00 to open.")],
            Timings: [new PhaseTiming(Phases.Read, TimeSpan.FromSeconds(1)), new PhaseTiming(Phases.Documents, TimeSpan.FromSeconds(8))],
            ExitCode: ExitCodes.ItemErrors,
            PlannedRedirects: [new PlannedRedirect("/Story.aspx", GuidFor(5), "/story", RedirectReasons.Aspx)]);

    private string Markdown() => File.ReadAllText(Path.Combine(RunDir, "report.md"));

    [Fact]
    public void Writes_all_five_files_into_a_new_run_folder()
    {
        Report.Write(SampleReport(), RunDir);

        foreach (var name in new[] { "report.json", "report.md", "redirects.csv", "skipped.csv", "items.csv" })
        {
            Assert.True(File.Exists(Path.Combine(RunDir, name)), name);
        }

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RunDir, "report.json")));
        Assert.Equal("migrate", json.RootElement.GetProperty("command").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void Markdown_lists_every_skipped_category()
    {
        Report.Write(SampleReport(), RunDir);

        var md = Markdown();
        Assert.Contains("| application page | 1 |", md);
        Assert.Contains("| host file | 2 |", md);
    }

    [Fact]
    public void Markdown_lists_missing_edited_and_schedules()
    {
        Report.Write(SampleReport(), RunDir);

        var md = Markdown();
        Assert.Contains("## Missing in source, review manually", md);
        Assert.Contains("node-9", md);
        Assert.Contains("## Edited in Umbraco", md);
        Assert.Contains("/Brands/C", md);
        Assert.Contains("## Publish schedules not applied", md);
        Assert.Contains("/Soon", md);
    }

    [Fact]
    public void Markdown_shows_counts_drops_items_links_and_timings()
    {
        Report.Write(SampleReport(), RunDir);

        var md = Markdown();
        Assert.Contains("article", md);
        Assert.Contains("| article | 3 | 1 |", md);
        Assert.Contains("| document | brand | Created | 2 |", md);
        Assert.Contains("/missing-page", md);
        Assert.Contains("| documents | 00:00:08 |", md);
        Assert.Contains("Exit code: 1", md);
    }

    [Fact]
    public void Markdown_says_when_target_counts_were_not_taken()
    {
        Report.Write(SampleReport(withTargetCounts: false), RunDir);

        Assert.Contains("Target counts: not taken", Markdown());
    }

    [Fact]
    public void Markdown_has_no_em_dash()
    {
        Report.Write(SampleReport(), RunDir);

        foreach (var file in Directory.GetFiles(RunDir))
        {
            Assert.DoesNotContain((char)0x2014, File.ReadAllText(file));
        }
    }

    [Fact]
    public void Csv_escapes_commas_and_quotes()
    {
        var skipped = new[] { new SkippedItem("host file", "id", "/a,b", "said \"no\"\nthen yes") };

        Report.Write(SampleReport(skipped: skipped), RunDir);

        var csv = File.ReadAllText(Path.Combine(RunDir, "skipped.csv"));
        Assert.StartsWith("category,identifier,old_path,reason\n", csv);
        Assert.Contains("host file,id,\"/a,b\",\"said \"\"no\"\"\nthen yes\"\n", csv);
    }

    [Fact]
    public void Items_and_redirects_csv_follow_the_contract_columns()
    {
        Report.Write(SampleReport(), RunDir);

        var items = File.ReadAllLines(Path.Combine(RunDir, "items.csv"));
        Assert.Equal("kind,type,key,old_path,outcome,error", items[0]);
        Assert.Contains($"document,article,{GuidFor(4)},/Articles/X,Failed,HTTP 400", items);

        var redirects = File.ReadAllLines(Path.Combine(RunDir, "redirects.csv"));
        Assert.Equal("old_url,new_url,target_key,reason", redirects[0]);
        Assert.Equal($"/Story.aspx,/story,{GuidFor(5)},aspx", redirects[1]);
    }

    [Fact]
    public void Report_contains_no_field_values()
    {
        // A field value, a region and an unlinked row name carry the marker; none of it may reach a report.
        var site = SampleSite() with
        {
            UnlinkedRows = new Dictionary<string, IReadOnlyList<SourceRow>>
            {
                ["DEMO.Brand"] = [new SourceRow(99, Fields(("Name", Marker), ("HeaderText", Marker)))],
            },
        };
        site = site with
        {
            Documents =
            [
                .. site.Documents,
                Doc(30, null, "CMS.MenuItem", "/Dup", alias: "dup", regions: [Region("r", $"<p>{Marker}</p>")], fields: Fields(("x", Marker))),
                Doc(31, null, "CMS.MenuItem", "/Dup2", alias: "DUP", regions: [Region("r", Marker)], pageTitle: Marker),
            ],
        };
        var plan = PlanBuilder.Build(site, SampleProfile());
        Assert.NotEmpty(plan.Issues);

        var items = plan.Documents.Select(d => new ItemResult(d.Key, ItemKinds.Document, d.TypeAlias, d.OldPath, ItemOutcome.Created, null)).ToList();
        Report.Write(SampleReport(plan.Skipped, plan.Issues, items), RunDir);

        foreach (var file in Directory.GetFiles(RunDir))
        {
            Assert.DoesNotContain(Marker, File.ReadAllText(file));
        }
    }
}
