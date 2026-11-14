using System.Net;
using System.Text.Json.Nodes;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class DocumentSyncTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly MigrationPlan Plan = PlanBuilder.Build(SampleSite(), SampleProfile());

    private static readonly SyncOptions Options = new();

    private static DocumentSync NewSync(FakeUmbracoApi api) => new(api, Plan.DocumentTypes, Plan.Templates);

    private static TargetDocument Doc(
        int id,
        int? parentId,
        string name,
        bool publish = false,
        int sortOrder = 1,
        string title = "Title",
        string typeAlias = "contentPage") =>
        new(GuidFor(id), parentId is null ? null : GuidFor(parentId.Value), typeAlias, name, name.ToLowerInvariant(), sortOrder, publish,
            new Dictionary<string, PlannedValue>
            {
                ["title"] = new("TextBox", title),
                [PlanAliases.SourceKey] = new("TextBox", GuidFor(id).ToString()),
            },
            "/" + name, [], null);

    [Fact]
    public async Task Creates_parents_before_children()
    {
        var api = new FakeUmbracoApi();
        var docs = new[] { Doc(3, 2, "Grandchild"), Doc(2, 1, "Child"), Doc(1, null, "Root") };

        var results = await NewSync(api).SyncAsync(docs, Options, Ct);

        var posted = api.Writes.Where(c => c.Method == HttpMethod.Post).Select(c => c.Body!["variants"]![0]!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(["Root", "Child", "Grandchild"], posted);
        Assert.All(results, r => Assert.Equal(ItemOutcome.Created, r.Outcome));
    }

    [Fact]
    public async Task Create_body_uses_planned_key_type_template_parent_and_fingerprint()
    {
        var api = new FakeUmbracoApi();

        await NewSync(api).SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "Child")], Options, Ct);

        var body = api.Stored("document", GuidFor(2))!;
        Assert.Equal(GuidFor(1).ToString("D"), body["parent"]!["id"]!.GetValue<string>());
        Assert.Equal(Plan.DocumentTypes.Single(t => t.Alias == "contentPage").Key.ToString("D"), body["documentType"]!["id"]!.GetValue<string>());
        Assert.Equal(Plan.Templates.Single(t => t.Alias == "migratedPage").Key.ToString("D"), body["template"]!["id"]!.GetValue<string>());
        var values = body["values"]!.AsArray();
        Assert.Contains(values, v => v!["alias"]!.GetValue<string>() == PlanAliases.UrlName && v["value"]!.GetValue<string>() == "child");
        Assert.Matches("^[0-9a-f]{64}$", values.Single(v => v!["alias"]!.GetValue<string>() == PlanAliases.Fingerprint)!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Posts_when_missing_puts_when_changed_skips_when_equal()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        await sync.SyncAsync([Doc(1, null, "Root", title: "One")], Options, Ct);

        api.Calls.Clear();
        var same = await sync.SyncAsync([Doc(1, null, "Root", title: "One")], Options, Ct);
        Assert.Equal(ItemOutcome.Unchanged, same.Single().Outcome);
        Assert.Empty(api.Writes);

        var changed = await sync.SyncAsync([Doc(1, null, "Root", title: "Two")], Options, Ct);
        Assert.Equal(ItemOutcome.Updated, changed.Single().Outcome);
        var put = api.Writes.Single();
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal($"/document/{GuidFor(1):D}", put.Path);
        Assert.Contains(put.Body!["values"]!.AsArray(), v => v!["alias"]!.GetValue<string>() == "title" && v["value"]!.GetValue<string>() == "Two");

        var added = await sync.SyncAsync([Doc(1, null, "Root", title: "Two"), Doc(2, 1, "Child")], Options, Ct);
        Assert.Equal([ItemOutcome.Unchanged, ItemOutcome.Created], added.Select(r => r.Outcome));
    }

    [Fact]
    public async Task Publishes_only_publish_items()
    {
        var api = new FakeUmbracoApi();
        var docs = new[] { Doc(1, null, "Live", publish: true), Doc(2, null, "Draft", publish: false) };

        await NewSync(api).SyncAsync(docs, Options, Ct);

        Assert.Equal([GuidFor(1)], api.PublishedKeys);
        var publish = api.Writes.Single(c => c.Path.EndsWith("/publish", StringComparison.Ordinal));
        Assert.Null(publish.Body!["publishSchedules"]![0]!["culture"]);
    }

    [Fact]
    public async Task Publishes_again_after_an_update()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        await sync.SyncAsync([Doc(1, null, "Live", publish: true, title: "A")], Options, Ct);
        api.Calls.Clear();

        await sync.SyncAsync([Doc(1, null, "Live", publish: true, title: "B")], Options, Ct);

        Assert.Equal([GuidFor(1)], api.PublishedKeys);
    }

    [Fact]
    public async Task Failed_item_does_not_stop_others_and_error_has_no_detail()
    {
        var api = new FakeUmbracoApi
        {
            Failure = c => c.Method == HttpMethod.Post && c.Body?["variants"]?[0]?["name"]?.GetValue<string>() == "Bad"
                ? FakeUmbracoApi.Problem(HttpStatusCode.BadRequest, "Validation failed")
                : null,
        };
        var docs = new[] { Doc(1, null, "Good"), Doc(2, null, "Bad"), Doc(3, null, "AlsoGood") };

        var results = await NewSync(api).SyncAsync(docs, Options, Ct);

        Assert.Equal([ItemOutcome.Created, ItemOutcome.Failed, ItemOutcome.Created], results.Select(r => r.Outcome));
        var failed = results[1];
        Assert.Equal("HTTP 400 Validation failed", failed.Error);
        Assert.Equal("/Bad", failed.OldPath);
        Assert.Equal(GuidFor(2), failed.Key);
        Assert.Equal(ItemKinds.Document, failed.Kind);
    }

    [Fact]
    public async Task Value_that_cannot_be_written_fails_only_that_item()
    {
        var api = new FakeUmbracoApi();
        var bad = Doc(1, null, "Bad") with
        {
            Values = new Dictionary<string, PlannedValue> { ["count"] = new("Integer", 1.5m) },
        };

        var results = await NewSync(api).SyncAsync([bad, Doc(2, null, "Good")], Options, Ct);

        Assert.Equal([ItemOutcome.Failed, ItemOutcome.Created], results.Select(r => r.Outcome));
        Assert.Contains("whole number", results[0].Error);
    }

    [Fact]
    public async Task Sorts_children_only_when_the_order_differs()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        var root = Doc(1, null, "Root");
        // Planned order B, A; creation order follows the plan list, so make the list order differ from the sort order.
        var docs = new[] { root, Doc(2, 1, "A", sortOrder: 2), Doc(3, 1, "B", sortOrder: 1) };

        await sync.SyncAsync(docs, Options, Ct);

        Assert.Equal([GuidFor(3), GuidFor(2)], api.ChildrenOf(GuidFor(1)));
        Assert.Single(api.Writes, c => c.Path == "/document/sort");

        api.Calls.Clear();
        await sync.SyncAsync(docs, Options, Ct);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Sort_keeps_documents_the_plan_does_not_know_after_the_planned_ones()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        await sync.SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "A", sortOrder: 1)], Options, Ct);
        var editorPage = Guid.NewGuid();
        api.Seed("document", editorPage, new JsonObject { ["variants"] = new JsonArray(new JsonObject { ["name"] = "Editor page" }) }, parent: GuidFor(1));

        await sync.SyncAsync([Doc(1, null, "Root"), Doc(3, 1, "C", sortOrder: 0), Doc(2, 1, "A", sortOrder: 1)], Options, Ct);

        Assert.Equal([GuidFor(3), GuidFor(2), editorPage], api.ChildrenOf(GuidFor(1)));
    }

    private static void EditTitle(FakeUmbracoApi api, Guid key, string title)
    {
        var values = api.Stored("document", key)!["values"]!.AsArray();
        values.Single(v => v!["alias"]!.GetValue<string>() == "title")!["value"] = title;
    }

    [Fact]
    public async Task Edited_item_is_skipped()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        var docs = new[] { Doc(1, null, "Root", publish: true, title: "One") };
        await sync.SyncAsync(docs, Options, Ct);
        EditTitle(api, GuidFor(1), "Edited by an editor");
        api.Calls.Clear();

        // The plan also changed; the editor's change still wins.
        var results = await sync.SyncAsync([Doc(1, null, "Root", publish: true, title: "Two")], Options, Ct);

        Assert.Equal(ItemOutcome.EditedSkipped, results.Single().Outcome);
        Assert.Empty(api.Writes);
        Assert.Equal("Edited by an editor", api.Stored("document", GuidFor(1))!["values"]!.AsArray()
            .Single(v => v!["alias"]!.GetValue<string>() == "title")!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Renamed_item_counts_as_edited()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        await sync.SyncAsync([Doc(1, null, "Root")], Options, Ct);
        api.Stored("document", GuidFor(1))!["variants"]![0]!["name"] = "Renamed in the back office";

        var results = await sync.SyncAsync([Doc(1, null, "Root")], Options, Ct);

        Assert.Equal(ItemOutcome.EditedSkipped, results.Single().Outcome);
    }

    [Fact]
    public async Task Item_without_a_stored_fingerprint_counts_as_edited()
    {
        var api = new FakeUmbracoApi();
        api.Seed("document", GuidFor(1), new JsonObject
        {
            ["values"] = new JsonArray(),
            ["variants"] = new JsonArray(new JsonObject { ["name"] = "Root" }),
        });

        var results = await NewSync(api).SyncAsync([Doc(1, null, "Root")], Options, Ct);

        Assert.Equal(ItemOutcome.EditedSkipped, results.Single().Outcome);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Moved_item_counts_as_edited_and_is_moved_back_when_overwriting()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        var docs = new[] { Doc(1, null, "Root"), Doc(2, 1, "A"), Doc(3, 1, "B") };
        await sync.SyncAsync(docs, Options, Ct);
        await api.SendAsync(HttpMethod.Put, $"/document/{GuidFor(3):D}/move", new { target = new { id = GuidFor(2) } }, Ct);
        api.Calls.Clear();

        var skipped = await NewSync(api).SyncAsync(docs, Options, Ct);
        Assert.Equal(ItemOutcome.EditedSkipped, skipped.Single(r => r.Key == GuidFor(3)).Outcome);
        Assert.Empty(api.Writes);

        var overwritten = await NewSync(api).SyncAsync(docs, new SyncOptions(OverwriteEdited: true), Ct);
        Assert.Equal(ItemOutcome.EditedOverwritten, overwritten.Single(r => r.Key == GuidFor(3)).Outcome);
        Assert.Equal([GuidFor(2), GuidFor(3)], api.ChildrenOf(GuidFor(1)));
        Assert.Contains(api.Writes, c => c.Path == $"/document/{GuidFor(3):D}/move");
    }

    [Fact]
    public async Task Edited_item_is_overwritten_with_option()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        var docs = new[] { Doc(1, null, "Root", publish: true, title: "One") };
        await sync.SyncAsync(docs, Options, Ct);
        EditTitle(api, GuidFor(1), "Edited by an editor");
        api.Calls.Clear();

        var results = await sync.SyncAsync(docs, new SyncOptions(OverwriteEdited: true), Ct);

        Assert.Equal(ItemOutcome.EditedOverwritten, results.Single().Outcome);
        Assert.Equal("One", api.Stored("document", GuidFor(1))!["values"]!.AsArray()
            .Single(v => v!["alias"]!.GetValue<string>() == "title")!["value"]!.GetValue<string>());
        Assert.Equal([GuidFor(1)], api.PublishedKeys);

        api.Calls.Clear();
        var again = await sync.SyncAsync(docs, Options, Ct);
        Assert.Equal(ItemOutcome.Unchanged, again.Single().Outcome);
    }

    [Fact]
    public async Task Preview_makes_no_writes()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        var docs = new[] { Doc(1, null, "Root", title: "One"), Doc(2, 1, "Child"), Doc(3, 1, "Other") };

        var onEmpty = await sync.SyncAsync(docs, new SyncOptions(Preview: true), Ct);
        Assert.All(onEmpty, r => Assert.Equal(ItemOutcome.WouldCreate, r.Outcome));
        Assert.Empty(api.Writes);

        await sync.SyncAsync(docs, Options, Ct);
        EditTitle(api, GuidFor(3), "Edited");
        api.Calls.Clear();
        var changed = new[] { Doc(1, null, "Root", title: "Changed"), Doc(2, 1, "Child"), Doc(3, 1, "Other") };

        var preview = await sync.SyncAsync(changed, new SyncOptions(Preview: true), Ct);

        Assert.Equal([ItemOutcome.WouldUpdate, ItemOutcome.Unchanged, ItemOutcome.EditedSkipped], preview.Select(r => r.Outcome));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Preview_with_overwrite_reports_would_update_for_an_edited_item()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        var docs = new[] { Doc(1, null, "Root") };
        await sync.SyncAsync(docs, Options, Ct);
        EditTitle(api, GuidFor(1), "Edited");
        api.Calls.Clear();

        var preview = await sync.SyncAsync(docs, new SyncOptions(OverwriteEdited: true, Preview: true), Ct);

        Assert.Equal(ItemOutcome.WouldUpdate, preview.Single().Outcome);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Page_in_the_recycle_bin_is_restored_and_counts_as_created()
    {
        var api = new FakeUmbracoApi();
        var sync = NewSync(api);
        var docs = new[] { Doc(1, null, "Root", publish: true), Doc(2, 1, "Child", publish: true, title: "Kept") };
        await sync.SyncAsync(docs, Options, Ct);
        api.Stored("document", GuidFor(2))!["isTrashed"] = true;
        api.Remove(GuidFor(2));
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync(docs, Options, Ct);

        Assert.Equal([ItemOutcome.Unchanged, ItemOutcome.Created], results.Select(r => r.Outcome));
        Assert.Contains(api.Writes, c => c.Path == $"/recycle-bin/document/{GuidFor(2):D}/restore" && c.Body!["target"]!["id"]!.GetValue<string>() == GuidFor(1).ToString("D"));
        Assert.Equal([GuidFor(2)], api.PublishedKeys);
        Assert.Equal([GuidFor(2)], api.ChildrenOf(GuidFor(1)));

        var again = await NewSync(api).SyncAsync(docs, Options, Ct);
        Assert.All(again, r => Assert.Equal(ItemOutcome.Unchanged, r.Outcome));
    }

    [Fact]
    public async Task Preview_reports_would_create_for_a_page_in_the_recycle_bin()
    {
        var api = new FakeUmbracoApi();
        var docs = new[] { Doc(1, null, "Root") };
        await NewSync(api).SyncAsync(docs, Options, Ct);
        api.Stored("document", GuidFor(1))!["isTrashed"] = true;
        api.Remove(GuidFor(1));
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync(docs, new SyncOptions(Preview: true), Ct);

        Assert.Equal(ItemOutcome.WouldCreate, results.Single().Outcome);
        Assert.Empty(api.Writes);
    }
}
