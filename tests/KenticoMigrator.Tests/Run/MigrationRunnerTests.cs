using System.Net;
using System.Text.Json;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Run;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Run;

public sealed class MigrationRunnerTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _runDir = Path.Combine(Path.GetTempPath(), "km-runner-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _output = new();

    public void Dispose()
    {
        if (Directory.Exists(_runDir))
        {
            Directory.Delete(_runDir, recursive: true);
        }
    }

    private static FakeUmbracoApi NewApi()
    {
        var plan = PlanBuilder.Build(SampleSite(), SampleProfile());
        var api = new FakeUmbracoApi();
        foreach (var builtIn in plan.DataTypes.Where(d => d.IsBuiltIn))
        {
            api.BuiltInDataTypes[builtIn.Name] = Guid.NewGuid();
        }

        return api;
    }

    private RunRequest Request(string command, SiteProfile? profile = null, bool overwrite = false) =>
        new(command, profile ?? SampleProfile(), _runDir, overwrite);

    private MigrationRunner Runner(IKenticoReader reader, IUmbracoApi? api, ITargetTreeClient? tree = null) => new(reader, api, _output, tree);

    private static FakeKenticoReader Reader(params SourceCounts[] counts) => new(SampleSite(), counts);

    private JsonElement ReadReport() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_runDir, "report.json"))).RootElement.Clone();

    /// <summary>A target whose document counts come from a script: snapshot n returns the n-th entry.</summary>
    private sealed class ScriptedTree(params int[] documentsPerSnapshot) : ITargetTreeClient
    {
        private int _snapshot = -1;

        public Task<IReadOnlyList<TargetTreeNode>> GetDocumentChildrenAsync(Guid? parentKey, CancellationToken ct)
        {
            if (parentKey is not null)
            {
                return Task.FromResult<IReadOnlyList<TargetTreeNode>>([]);
            }

            _snapshot++;
            var count = documentsPerSnapshot[Math.Min(_snapshot, documentsPerSnapshot.Length - 1)];
            return Task.FromResult<IReadOnlyList<TargetTreeNode>>([.. Enumerable.Range(0, count).Select(i => new TargetTreeNode(Guid.NewGuid(), "contentPage", false))]);
        }

        public Task<int> CountMediaAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<int> CountRedirectsAsync(CancellationToken ct) => Task.FromResult(0);
    }

    [Fact]
    public async Task Migrate_writes_everything_and_reports_success()
    {
        var api = NewApi();

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = ReadReport();
        Assert.Equal(0, report.GetProperty("exitCode").GetInt32());
        Assert.Equal("migrate", report.GetProperty("command").GetString());
        Assert.True(report.GetProperty("items").GetArrayLength() > 0 || File.Exists(Path.Combine(_runDir, "items.csv")));
        Assert.True(File.Exists(Path.Combine(_runDir, "report.md")));
        Assert.NotEqual(JsonValueKind.Null, report.GetProperty("targetCountsBefore").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, report.GetProperty("targetCountsAfter").ValueKind);
        Assert.Contains("documents:", _output.ToString());
        Assert.Contains(api.Writes, c => c.Path == "/document");
    }

    [Fact]
    public async Task Count_drop_stops_with_exit_code_3()
    {
        var api = NewApi();

        var exit = await Runner(Reader(), api, new ScriptedTree(5, 4)).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.CountDrop, exit);
        var drops = ReadReport().GetProperty("countDrops");
        var drop = Assert.Single(drops.EnumerateArray());
        Assert.Equal("contentPage", drop.GetProperty("type").GetString());
        Assert.Equal(5, drop.GetProperty("before").GetInt32());
        Assert.Equal(4, drop.GetProperty("after").GetInt32());
    }

    [Fact]
    public async Task Failed_item_gives_exit_code_1_and_run_finishes()
    {
        var api = NewApi();
        var failingKey = GuidFor(2);
        api.Failure = c => c.Method == HttpMethod.Post && c.Path == "/document" && c.Body?["id"]?.GetValue<string>() == failingKey.ToString("D")
            ? FakeUmbracoApi.Problem(HttpStatusCode.BadRequest, "Validation failed")
            : null;

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.ItemErrors, exit);
        Assert.True(File.Exists(Path.Combine(_runDir, "report.json")), "the report is written although an item failed");
        Assert.Contains("HTTP 400 Validation failed", File.ReadAllText(Path.Combine(_runDir, "items.csv")));
        Assert.DoesNotContain("detail that must never", File.ReadAllText(Path.Combine(_runDir, "report.json")));
        Assert.True(api.Writes.Count(c => c.Path == "/document") > 1, "the other pages were still written");
    }

    [Fact]
    public async Task Plan_errors_give_exit_code_2_and_no_writes()
    {
        var api = NewApi();
        var profile = SampleProfile();
        var broken = profile with { Types = [profile.Types[0] with { AllowedChildren = ["contentPage", "ghostType"] }, .. profile.Types.Skip(1)] };

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate, broken), Ct);

        Assert.Equal(ExitCodes.ConfigurationOrPlanError, exit);
        Assert.Empty(api.Writes);
        Assert.Contains("ghostType", File.ReadAllText(Path.Combine(_runDir, "report.json")));
    }

    [Fact]
    public async Task Preview_falls_back_to_plan_only_when_target_unreachable()
    {
        var api = NewApi();
        api.Failure = _ => new UmbracoAuthException("The Umbraco token endpoint answered HTTP 400.");

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Plan), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = ReadReport();
        Assert.Equal(JsonValueKind.Null, report.GetProperty("targetCountsBefore").ValueKind);
        Assert.Contains("not reachable", File.ReadAllText(Path.Combine(_runDir, "report.md")));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Preview_without_umbraco_configuration_is_plan_only()
    {
        var exit = await Runner(Reader(), api: null).RunAsync(Request(CliArguments.Plan), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("not reachable", File.ReadAllText(Path.Combine(_runDir, "report.md")));
    }

    [Fact]
    public async Task Preview_on_empty_target_reports_would_create_and_writes_nothing()
    {
        var api = NewApi();

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Plan), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(api.Writes);
        var items = File.ReadAllText(Path.Combine(_runDir, "items.csv"));
        Assert.Contains("WouldCreate", items);
        Assert.DoesNotContain(",Created,", items);
    }

    [Fact]
    public async Task Migrate_with_unreachable_target_gives_exit_code_2()
    {
        var api = NewApi();
        api.Failure = _ => FakeUmbracoApi.Problem(HttpStatusCode.BadGateway, "Bad gateway");

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.ConfigurationOrPlanError, exit);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Migrate_without_umbraco_configuration_gives_exit_code_2()
    {
        var exit = await Runner(Reader(), api: null).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.ConfigurationOrPlanError, exit);
    }

    [Fact]
    public async Task Changed_source_counts_are_a_warning_not_a_failure()
    {
        var api = NewApi();
        var before = FakeKenticoReader.Of(SampleSite());
        var after = before with { Aliases = before.Aliases + 1 };

        var exit = await Runner(Reader(before, after), api).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var issues = ReadReport().GetProperty("issues").EnumerateArray().ToList();
        Assert.Contains(issues, i => i.GetProperty("severity").GetString() == "warning" && i.GetProperty("message").GetString()!.Contains("source", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_in_source_items_are_reported()
    {
        var api = NewApi();
        var runner = Runner(Reader(), api);
        await runner.RunAsync(Request(CliArguments.Migrate), Ct);
        var home = PlanBuilder.Build(SampleSite(), SampleProfile()).Documents.Single(d => d.OldPath == "/Home");
        api.Seed("document", Guid.NewGuid(), new System.Text.Json.Nodes.JsonObject
        {
            ["documentType"] = new System.Text.Json.Nodes.JsonObject { ["id"] = Guid.NewGuid().ToString("D") },
            ["variants"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["name"] = "Removed page" }),
            ["values"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["alias"] = PlanAliases.SourceKey, ["value"] = "node-gone" }),
        }, parent: home.Key);

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var missing = Assert.Single(ReadReport().GetProperty("missingInSource").EnumerateArray());
        Assert.Equal("node-gone", missing.GetProperty("sourceKey").GetString());
        Assert.Contains("missing in source", _output.ToString());
    }

    [Fact]
    public async Task Counts_command_prints_both_sides_and_writes_nothing()
    {
        var api = NewApi();

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Counts), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(api.Writes);
        Assert.False(Directory.Exists(_runDir));
        var text = _output.ToString();
        Assert.Contains("source:", text);
        Assert.Contains("target:", text);
        Assert.Contains("CMS.MenuItem", text);
    }

    [Fact]
    public async Task Overwrite_option_reaches_the_document_sync()
    {
        var api = NewApi();
        await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);
        var docKey = PlanBuilder.Build(SampleSite(), SampleProfile()).Documents.First(d => d.OldPath == "/About").Key;
        api.Stored("document", docKey)!["values"]!.AsArray().Single(v => v!["alias"]!.GetValue<string>() == "title")!["value"] = "Edited";

        var skipped = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);
        var overwritten = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate, overwrite: true), Ct);

        Assert.Equal(ExitCodes.Success, skipped);
        Assert.Equal(ExitCodes.Success, overwritten);
        Assert.Contains("edited in Umbraco, overwritten: 1", _output.ToString());
        Assert.Contains("edited in Umbraco, not updated: 1", _output.ToString());
    }

    [Fact]
    public async Task Media_pickers_are_left_empty_until_media_is_synced_so_a_rerun_changes_nothing()
    {
        var site = SampleSite() with
        {
            Attachments = [Attachment(GuidFor(900), 11, "teaser.jpg", fieldColumn: "ArticleTeaserImage")],
        };
        var plan = PlanBuilder.Build(site, SampleProfile());
        var article = plan.Documents.Single(d => d.OldPath == "/Articles/First-trip");
        Assert.NotNull(article.Values["teaserImage"].Raw);
        var api = NewApi();
        var reader = new FakeKenticoReader(site);

        await Runner(reader, api).RunAsync(Request(CliArguments.Migrate), Ct);

        var posted = api.Stored("document", article.Key)!["values"]!.AsArray();
        Assert.Null(posted.Single(v => v!["alias"]!.GetValue<string>() == "teaserImage")!["value"]);

        var second = await Runner(reader, api).RunAsync(Request(CliArguments.Migrate), Ct);
        Assert.Equal(ExitCodes.Success, second);
        Assert.DoesNotContain(",Created,", File.ReadAllText(Path.Combine(_runDir, "items.csv")));
    }

    [Fact]
    public async Task Failure_after_the_writes_still_writes_the_report_and_gives_exit_code_1()
    {
        var api = NewApi();
        var redirectCalls = 0;
        // The first redirect read is the snapshot before the run; the second one is the snapshot after the writes.
        api.Failure = c => c.Path.StartsWith("/redirect-management", StringComparison.Ordinal) && ++redirectCalls == 2
            ? FakeUmbracoApi.Problem(HttpStatusCode.BadGateway, "Bad gateway")
            : null;

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.ItemErrors, exit);
        Assert.True(File.Exists(Path.Combine(_runDir, "report.json")));
        Assert.Contains("Created", File.ReadAllText(Path.Combine(_runDir, "items.csv")));
        Assert.Contains("stopped", File.ReadAllText(Path.Combine(_runDir, "report.md")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unexpected_exception_after_the_writes_still_writes_the_report_and_gives_exit_code_1()
    {
        var api = NewApi();
        var reader = new FakeKenticoReader(SampleSite()) { FailOnCountCall = 1 };

        var exit = await Runner(reader, api).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.ItemErrors, exit);
        Assert.Contains("Created", File.ReadAllText(Path.Combine(_runDir, "items.csv")));
        var report = File.ReadAllText(Path.Combine(_runDir, "report.md"));
        Assert.Contains("InvalidOperationException", report);
        Assert.DoesNotContain("went away", report);
    }

    [Fact]
    public async Task Cancel_after_the_writes_started_writes_the_report_and_gives_exit_code_1()
    {
        var api = NewApi();
        using var cancel = new CancellationTokenSource();
        var posts = 0;
        api.Failure = c =>
        {
            if (c.Method == HttpMethod.Post && c.Path == "/document" && ++posts == 3)
            {
                cancel.Cancel();
                return new OperationCanceledException(cancel.Token);
            }

            return null;
        };

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), cancel.Token);

        Assert.Equal(ExitCodes.ItemErrors, exit);
        Assert.Contains("cancelled", File.ReadAllText(Path.Combine(_runDir, "report.md")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, File.ReadAllLines(Path.Combine(_runDir, "items.csv")).Count(l => l.StartsWith("document,", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Pages_written_before_an_abort_are_in_the_report()
    {
        var api = NewApi();
        var posts = 0;
        api.Failure = c => c.Method == HttpMethod.Post && c.Path == "/document" && ++posts == 4
            ? new UmbracoAuthException("The Umbraco API rejected a freshly issued token (HTTP 401).")
            : null;

        var exit = await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);

        Assert.Equal(ExitCodes.ItemErrors, exit);
        var documents = File.ReadAllLines(Path.Combine(_runDir, "items.csv")).Where(l => l.StartsWith("document,", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, documents.Count);
        Assert.All(documents, l => Assert.Contains(",Created,", l));
    }

    [Fact]
    public async Task Edited_template_is_counted_and_the_overwrite_option_reaches_the_schema_sync()
    {
        var api = NewApi();
        await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);
        var templateKey = PlanBuilder.Build(SampleSite(), SampleProfile()).Templates.Single(t => t.Alias == "migratedPage").Key;
        api.Stored("template", templateKey)!["content"] = "<p>changed in the back office</p>";

        await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate), Ct);
        Assert.Contains("edited in Umbraco, not updated: 1", _output.ToString());
        Assert.Equal("<p>changed in the back office</p>", api.Stored("template", templateKey)!["content"]!.GetValue<string>());

        await Runner(Reader(), api).RunAsync(Request(CliArguments.Migrate, overwrite: true), Ct);
        Assert.Contains("edited in Umbraco, overwritten: 1", _output.ToString());
        Assert.Contains("migrator-fingerprint", api.Stored("template", templateKey)!["content"]!.GetValue<string>());
    }
}
