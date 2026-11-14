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
        Assert.Equal("Mapping error", results[0].Error);
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

    [Fact]
    public async Task Page_that_was_never_published_is_published_on_the_next_run()
    {
        var api = new FakeUmbracoApi
        {
            Failure = c => c.Path.EndsWith("/publish", StringComparison.Ordinal) ? FakeUmbracoApi.Problem(HttpStatusCode.BadRequest, "Cannot publish") : null,
        };
        var docs = new[] { Doc(1, null, "Live", publish: true) };
        var first = await NewSync(api).SyncAsync(docs, Options, Ct);
        Assert.Equal(ItemOutcome.Failed, first.Single().Outcome);
        api.Failure = null;
        api.Calls.Clear();

        var second = await NewSync(api).SyncAsync(docs, Options, Ct);

        Assert.Equal(ItemOutcome.Updated, second.Single().Outcome);
        Assert.Equal([GuidFor(1)], api.PublishedKeys);
        Assert.DoesNotContain(api.Writes, c => c.Method == HttpMethod.Put && c.Path == $"/document/{GuidFor(1):D}");

        api.Calls.Clear();
        var third = await NewSync(api).SyncAsync(docs, Options, Ct);
        Assert.Equal(ItemOutcome.Unchanged, third.Single().Outcome);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Unexpected_exception_in_one_item_does_not_abort_the_run()
    {
        var api = new FakeUmbracoApi
        {
            Failure = c => c.Method == HttpMethod.Post && c.Body?["variants"]?[0]?["name"]?.GetValue<string>() == "Odd"
                ? new InvalidOperationException("secret value that must not be reported")
                : null,
        };

        var results = await NewSync(api).SyncAsync([Doc(1, null, "Odd"), Doc(2, null, "Fine")], Options, Ct);

        Assert.Equal([ItemOutcome.Failed, ItemOutcome.Created], results.Select(r => r.Outcome));
        Assert.DoesNotContain("secret", results[0].Error);
    }

    [Fact]
    public async Task Results_sink_keeps_the_pages_written_before_an_abort()
    {
        var posts = 0;
        var api = new FakeUmbracoApi
        {
            Failure = c => c.Method == HttpMethod.Post && ++posts == 3 ? new UmbracoAuthException("token rejected") : null,
        };
        var sink = new List<ItemResult>();

        await Assert.ThrowsAsync<UmbracoAuthException>(() =>
            NewSync(api).SyncAsync([Doc(1, null, "A"), Doc(2, null, "B"), Doc(3, null, "C")], Options, Ct, sink));

        Assert.Equal([ItemOutcome.Created, ItemOutcome.Created], sink.Select(r => r.Outcome));
    }

    private static TargetDocument Reparent(TargetDocument doc, int newParentId) => doc with { ParentKey = GuidFor(newParentId) };

    [Fact]
    public async Task Page_moved_in_the_source_is_moved_and_reported_updated()
    {
        var api = new FakeUmbracoApi();
        await NewSync(api).SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "A"), Doc(3, 1, "B")], Options, Ct);
        var movedPlan = new[] { Doc(1, null, "Root"), Doc(2, 1, "A"), Reparent(Doc(3, 1, "B"), 2) };
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync(movedPlan, Options, Ct);

        Assert.Equal(ItemOutcome.Updated, results.Single(r => r.Key == GuidFor(3)).Outcome);
        Assert.Contains(api.Writes, c => c.Path == $"/document/{GuidFor(3):D}/move" && c.Body!["target"]!["id"]!.GetValue<string>() == GuidFor(2).ToString("D"));
        Assert.Equal([GuidFor(3)], api.ChildrenOf(GuidFor(2)));

        var again = await NewSync(api).SyncAsync(movedPlan, Options, Ct);
        Assert.All(again, r => Assert.Equal(ItemOutcome.Unchanged, r.Outcome));
    }

    [Fact]
    public async Task Source_move_in_preview_is_would_update_and_writes_nothing()
    {
        var api = new FakeUmbracoApi();
        await NewSync(api).SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "A"), Doc(3, 1, "B")], Options, Ct);
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "A"), Reparent(Doc(3, 1, "B"), 2)], new SyncOptions(Preview: true), Ct);

        Assert.Equal(ItemOutcome.WouldUpdate, results.Single(r => r.Key == GuidFor(3)).Outcome);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Editor_move_is_still_an_edit_when_the_source_moved_too()
    {
        var api = new FakeUmbracoApi();
        await NewSync(api).SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "A"), Doc(3, 1, "B"), Doc(4, 1, "C")], Options, Ct);
        // An editor moved B under C; the source now says B belongs under A.
        await api.SendAsync(HttpMethod.Put, $"/document/{GuidFor(3):D}/move", new { target = new { id = GuidFor(4) } }, Ct);
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "A"), Reparent(Doc(3, 1, "B"), 2), Doc(4, 1, "C")], Options, Ct);

        Assert.Equal(ItemOutcome.EditedSkipped, results.Single(r => r.Key == GuidFor(3)).Outcome);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Page_with_pending_changes_that_should_be_published_is_published_again()
    {
        var api = new FakeUmbracoApi();
        var docs = new[] { Doc(1, null, "Live", publish: true) };
        await NewSync(api).SyncAsync(docs, Options, Ct);
        // A PUT succeeded earlier and its publish failed: the page now has changes that are not published.
        api.Stored("document", GuidFor(1))!["variants"]![0]!["state"] = "PublishedPendingChanges";
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync(docs, Options, Ct);

        Assert.Equal(ItemOutcome.Updated, results.Single().Outcome);
        Assert.Equal([GuidFor(1)], api.PublishedKeys);
        Assert.DoesNotContain(api.Writes, c => c.Method == HttpMethod.Put && c.Path == $"/document/{GuidFor(1):D}");
    }

    [Fact]
    public async Task Page_an_editor_unpublished_is_left_unpublished()
    {
        var api = new FakeUmbracoApi();
        var docs = new[] { Doc(1, null, "Live", publish: true) };
        await NewSync(api).SyncAsync(docs, Options, Ct);
        api.Stored("document", GuidFor(1))!["variants"]![0]!["state"] = "Draft"; // publishDate stays: it was published once
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync(docs, Options, Ct);

        Assert.Equal(ItemOutcome.Unchanged, results.Single().Outcome);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Sort_is_reported_as_its_own_item_keyed_by_the_parent()
    {
        var api = new FakeUmbracoApi();
        var docs = new[] { Doc(1, null, "Root"), Doc(2, 1, "A", sortOrder: 2), Doc(3, 1, "B", sortOrder: 1) };

        var results = await NewSync(api).SyncAsync(docs, Options, Ct);

        var sort = Assert.Single(results, r => r.Kind == ItemKinds.Sort);
        Assert.Equal((GuidFor(1), ItemOutcome.Updated, "/Root"), (sort.Key, sort.Outcome, sort.OldPath));
        Assert.Equal(docs.Length, results.Count(r => r.Kind == ItemKinds.Document));
    }

    [Fact]
    public async Task Sort_failure_is_a_separate_failed_item_and_pages_keep_their_outcome()
    {
        var api = new FakeUmbracoApi
        {
            Failure = c => c.Path == "/document/sort" ? FakeUmbracoApi.Problem(HttpStatusCode.BadRequest, "Cannot sort") : null,
        };
        var docs = new[] { Doc(1, null, "Root"), Doc(2, 1, "A", sortOrder: 2), Doc(3, 1, "B", sortOrder: 1) };

        var results = await NewSync(api).SyncAsync(docs, Options, Ct);

        Assert.All(results.Where(r => r.Kind == ItemKinds.Document), r => Assert.Equal(ItemOutcome.Created, r.Outcome));
        var sort = Assert.Single(results, r => r.Kind == ItemKinds.Sort);
        Assert.Equal((ItemOutcome.Failed, "HTTP 400 Cannot sort"), (sort.Outcome, sort.Error));
    }

    [Fact]
    public async Task Order_an_editor_set_is_kept_when_nothing_under_the_parent_changed()
    {
        var api = new FakeUmbracoApi();
        var docs = new[] { Doc(1, null, "Root"), Doc(2, 1, "A", sortOrder: 1), Doc(3, 1, "B", sortOrder: 2) };
        await NewSync(api).SyncAsync(docs, Options, Ct);
        await api.SendAsync(HttpMethod.Put, "/document/sort", new { parent = new { id = GuidFor(1) }, sorting = new[] { new { id = GuidFor(3), sortOrder = 0 }, new { id = GuidFor(2), sortOrder = 1 } } }, Ct);
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync(docs, Options, Ct);

        Assert.All(results, r => Assert.Equal(ItemOutcome.Unchanged, r.Outcome));
        Assert.Empty(api.Writes);
        Assert.Equal([GuidFor(3), GuidFor(2)], api.ChildrenOf(GuidFor(1)));
    }

    [Fact]
    public async Task No_sort_under_a_parent_that_has_an_edited_child()
    {
        var api = new FakeUmbracoApi();
        await NewSync(api).SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "A", sortOrder: 5)], Options, Ct);
        EditTitle(api, GuidFor(2), "Edited");
        api.Calls.Clear();

        var results = await NewSync(api).SyncAsync([Doc(1, null, "Root"), Doc(2, 1, "A", sortOrder: 5), Doc(3, 1, "C", sortOrder: 0)], Options, Ct);

        Assert.Equal([ItemOutcome.Unchanged, ItemOutcome.EditedSkipped, ItemOutcome.Created], results.Select(r => r.Outcome));
        Assert.DoesNotContain(api.Writes, c => c.Path == "/document/sort");
    }
}
