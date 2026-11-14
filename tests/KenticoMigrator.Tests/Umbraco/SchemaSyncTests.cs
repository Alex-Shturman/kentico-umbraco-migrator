using System.Net;
using System.Text.Json.Nodes;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class SchemaSyncTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly MigrationPlan Plan = PlanBuilder.Build(SampleSite(), SampleProfile());

    private static FakeUmbracoApi NewApi()
    {
        var api = new FakeUmbracoApi();
        foreach (var builtIn in Plan.DataTypes.Where(d => d.IsBuiltIn))
        {
            api.BuiltInDataTypes[builtIn.Name] = Guid.NewGuid();
        }

        return api;
    }

    [Fact]
    public async Task Creates_missing_and_skips_equal()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);

        var first = await sync.EnsureAsync(Plan, preview: false, Ct);

        var created = first.Where(r => r.Outcome == ItemOutcome.Created).ToList();
        Assert.Equal(Plan.DataTypes.Count(d => !d.IsBuiltIn), created.Count(r => r.Kind == ItemKinds.DataType));
        Assert.Equal(Plan.Templates.Count, created.Count(r => r.Kind == ItemKinds.Template));
        Assert.Equal(Plan.DocumentTypes.Count, created.Count(r => r.Kind == ItemKinds.DocumentType));
        Assert.DoesNotContain(first, r => r.Outcome == ItemOutcome.Failed);
        Assert.NotNull(api.Stored("document-type", Plan.DocumentTypes[0].Key));

        api.Calls.Clear();
        var second = await sync.EnsureAsync(Plan, preview: false, Ct);

        Assert.All(second, r => Assert.Equal(ItemOutcome.Unchanged, r.Outcome));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Compositions_before_page_types()
    {
        var api = NewApi();

        await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        var order = api.Writes
            .Where(c => c.Method == HttpMethod.Post && c.Path == "/document-type")
            .Select(c => c.Body!["alias"]!.GetValue<string>())
            .ToList();
        var compositionAliases = Plan.DocumentTypes.Where(t => t.IsElementOrComposition).Select(t => t.Alias).ToList();
        var lastComposition = compositionAliases.Max(order.IndexOf);
        var firstPageType = Plan.DocumentTypes.Where(t => !t.IsElementOrComposition).Min(t => order.IndexOf(t.Alias));
        Assert.True(lastComposition < firstPageType);

        var firstDocumentType = api.Writes.Select((c, i) => (c, i)).First(x => x.c.Path == "/document-type").i;
        var lastTemplate = api.Writes.Select((c, i) => (c, i)).Last(x => x.c.Path == "/template").i;
        Assert.True(lastTemplate < firstDocumentType, "templates must exist before the types that allow them");
    }

    [Fact]
    public async Task Allowed_children_and_compositions_are_set_by_key()
    {
        var api = NewApi();

        await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        var contentPage = Plan.DocumentTypes.Single(t => t.Alias == "contentPage");
        var stored = api.Stored("document-type", contentPage.Key)!;
        var allowed = stored["allowedDocumentTypes"]!.AsArray().Select(a => a!["documentType"]!["id"]!.GetValue<string>()).ToList();
        foreach (var alias in contentPage.AllowedChildAliases)
        {
            Assert.Contains(Plan.DocumentTypes.Single(t => t.Alias == alias).Key.ToString("D"), allowed);
        }

        var compositions = stored["compositions"]!.AsArray().Select(c => c!["documentType"]!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(contentPage.Compositions.Count, compositions.Count);
        Assert.Equal(Plan.Templates.Single(t => t.Alias == "migratedPage").Key.ToString("D"), stored["defaultTemplate"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Property_uses_resolved_built_in_data_type_id()
    {
        var api = NewApi();

        await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        var stored = api.Stored("document-type", Plan.DocumentTypes.Single(t => t.Alias == "article").Key)!;
        var bodyText = stored["properties"]!.AsArray().Single(p => p!["alias"]!.GetValue<string>() == "bodyText")!;
        Assert.Equal(api.BuiltInDataTypes["Richtext editor"].ToString("D"), bodyText["dataType"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Changed_item_is_updated()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);
        await sync.EnsureAsync(Plan, preview: false, Ct);
        var brand = Plan.DocumentTypes.Single(t => t.Alias == "brand");
        api.Stored("document-type", brand.Key)!["name"] = "Tampered";

        var results = await sync.EnsureAsync(Plan, preview: false, Ct);

        Assert.Equal(ItemOutcome.Updated, results.Single(r => r.Key == brand.Key).Outcome);
        Assert.Equal(brand.Name, api.Stored("document-type", brand.Key)!["name"]!.GetValue<string>());
        Assert.Equal(1, results.Count(r => r.Outcome != ItemOutcome.Unchanged));
    }

    [Fact]
    public async Task Missing_built_in_data_type_is_a_failed_item_and_the_rest_continues()
    {
        var api = NewApi();
        api.BuiltInDataTypes.Remove("Tags");

        var results = await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        Assert.Contains(results, r => r.Kind == ItemKinds.DataType && r.Outcome == ItemOutcome.Failed);
        Assert.Contains(results, r => r.Kind == ItemKinds.Template && r.Outcome == ItemOutcome.Created);
        // The types that use the missing data type cannot be written, the others can.
        Assert.Contains(results, r => r.Kind == ItemKinds.DocumentType && r.TypeAlias == "brand" && r.Outcome == ItemOutcome.Failed);
        Assert.Contains(results, r => r.Kind == ItemKinds.DocumentType && r.TypeAlias == "article" && r.Outcome == ItemOutcome.Created);
    }

    [Fact]
    public async Task Api_error_gives_status_and_title_only()
    {
        var api = NewApi();
        api.Failure = c => c.Method == HttpMethod.Post && c.Path == "/template"
            ? FakeUmbracoApi.Problem(HttpStatusCode.BadRequest, "Invalid alias")
            : null;

        var results = await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        var failed = results.First(r => r.Kind == ItemKinds.Template);
        Assert.Equal(ItemOutcome.Failed, failed.Outcome);
        Assert.Equal("HTTP 400 Invalid alias", failed.Error);
    }

    [Fact]
    public async Task Preview_reports_would_create_and_writes_nothing()
    {
        var api = NewApi();

        var results = await new SchemaSync(api).EnsureAsync(Plan, preview: true, Ct);

        Assert.Empty(api.Writes);
        Assert.All(results, r => Assert.Equal(ItemOutcome.WouldCreate, r.Outcome));
        Assert.Contains(results, r => r.Kind == ItemKinds.DocumentType);
    }

    [Fact]
    public async Task Dropdown_data_type_carries_its_items()
    {
        var api = NewApi();

        await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        var dropdown = Plan.DataTypes.Single(d => d.Name.StartsWith("Dropdown product", StringComparison.Ordinal));
        var values = api.Stored("data-type", dropdown.Key)!["values"]!.AsArray();
        Assert.Equal("Umbraco.DropDown.Flexible", api.Stored("data-type", dropdown.Key)!["editorAlias"]!.GetValue<string>());
        Assert.Contains(values, v => v!["alias"]!.GetValue<string>() == "multiple" && !v["value"]!.GetValue<bool>());
        var items = values.Single(v => v!["alias"]!.GetValue<string>() == "items")!["value"]!.AsArray();
        Assert.Equal(["standard", "express"], items.Select(i => i!.GetValue<string>()));
    }

    private static void AddEditorAdditions(FakeUmbracoApi api, TargetDocumentType type, Guid containerId, Guid propertyId)
    {
        var stored = api.Stored("document-type", type.Key)!;
        stored["containers"]!.AsArray().Add(new JsonObject { ["id"] = containerId.ToString("D"), ["parent"] = null, ["name"] = "Editor tab", ["type"] = "Tab", ["sortOrder"] = 9 });
        stored["properties"]!.AsArray().Add(new JsonObject
        {
            ["id"] = propertyId.ToString("D"),
            ["container"] = new JsonObject { ["id"] = containerId.ToString("D") },
            ["sortOrder"] = 99,
            ["alias"] = "editorNote",
            ["name"] = "Editor note",
            ["dataType"] = new JsonObject { ["id"] = api.BuiltInDataTypes["Textstring"].ToString("D") },
            ["variesByCulture"] = false,
            ["variesBySegment"] = false,
            ["validation"] = new JsonObject { ["mandatory"] = false },
            ["appearance"] = new JsonObject { ["labelOnTop"] = false },
        });
        stored["allowedDocumentTypes"]!.AsArray().Add(new JsonObject
        {
            ["documentType"] = new JsonObject { ["id"] = Plan.DocumentTypes.Single(t => t.Alias == "brand").Key.ToString("D") },
            ["sortOrder"] = 9,
        });
        stored["compositions"]!.AsArray().Add(new JsonObject
        {
            ["documentType"] = new JsonObject { ["id"] = Plan.DocumentTypes.Single(t => t.Alias == "contentFolder").Key.ToString("D") },
            ["compositionType"] = "Composition",
        });
    }

    [Fact]
    public async Task Editor_additions_on_a_document_type_do_not_make_it_changed_and_survive_an_update()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);
        await sync.EnsureAsync(Plan, preview: false, Ct);
        var article = Plan.DocumentTypes.Single(t => t.Alias == "article");
        var containerId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        AddEditorAdditions(api, article, containerId, propertyId);
        api.Calls.Clear();

        var quiet = await sync.EnsureAsync(Plan, preview: false, Ct);

        Assert.All(quiet, r => Assert.Equal(ItemOutcome.Unchanged, r.Outcome));
        Assert.Empty(api.Writes);

        api.Stored("document-type", article.Key)!["name"] = "Tampered";
        var results = await sync.EnsureAsync(Plan, preview: false, Ct);

        Assert.Equal(ItemOutcome.Updated, results.Single(r => r.Key == article.Key).Outcome);
        var stored = api.Stored("document-type", article.Key)!;
        Assert.Equal(article.Name, stored["name"]!.GetValue<string>());
        Assert.Contains(stored["properties"]!.AsArray(), p => p!["alias"]!.GetValue<string>() == "editorNote" && p["id"]!.GetValue<string>() == propertyId.ToString("D"));
        Assert.Contains(stored["containers"]!.AsArray(), c => c!["id"]!.GetValue<string>() == containerId.ToString("D"));
        Assert.Contains(stored["allowedDocumentTypes"]!.AsArray(), a => a!["documentType"]!["id"]!.GetValue<string>() == Plan.DocumentTypes.Single(t => t.Alias == "brand").Key.ToString("D"));
        Assert.Contains(stored["compositions"]!.AsArray(), c => c!["documentType"]!["id"]!.GetValue<string>() == Plan.DocumentTypes.Single(t => t.Alias == "contentFolder").Key.ToString("D"));
        Assert.Contains(stored["properties"]!.AsArray(), p => p!["alias"]!.GetValue<string>() == "bodyText");
    }

    [Fact]
    public async Task Planned_property_keeps_the_id_it_already_has()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);
        await sync.EnsureAsync(Plan, preview: false, Ct);
        var article = Plan.DocumentTypes.Single(t => t.Alias == "article");
        var stored = api.Stored("document-type", article.Key)!;
        var existingId = Guid.NewGuid().ToString("D");
        stored["properties"]!.AsArray().Single(p => p!["alias"]!.GetValue<string>() == "bodyText")!["id"] = existingId;
        stored["name"] = "Tampered";

        await sync.EnsureAsync(Plan, preview: false, Ct);

        Assert.Equal(existingId, stored["properties"]!.AsArray().Single(p => p!["alias"]!.GetValue<string>() == "bodyText")!["id"]!.GetValue<string>());
    }

    private static Guid MigratedPageKey => Plan.Templates.Single(t => t.Alias == "migratedPage").Key;

    [Fact]
    public async Task Written_template_carries_a_fingerprint_marker()
    {
        var api = NewApi();

        await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        Assert.Matches(@"@\* migrator-fingerprint:[0-9a-f]{64} \*@", api.Stored("template", MigratedPageKey)!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Edited_template_is_skipped_without_the_flag()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);
        await sync.EnsureAsync(Plan, preview: false, Ct);
        var stored = api.Stored("template", MigratedPageKey)!;
        stored["content"] = stored["content"]!.GetValue<string>().Replace("<h1>", "<h1 class=\"mine\">", StringComparison.Ordinal);
        var edited = stored["content"]!.GetValue<string>();
        api.Calls.Clear();

        var results = await sync.EnsureAsync(Plan, preview: false, Ct);

        Assert.Equal(ItemOutcome.EditedSkipped, results.Single(r => r.Key == MigratedPageKey).Outcome);
        Assert.Empty(api.Writes);
        Assert.Equal(edited, stored["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Edited_template_is_overwritten_with_the_flag_and_then_unchanged()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);
        await sync.EnsureAsync(Plan, preview: false, Ct);
        var original = api.Stored("template", MigratedPageKey)!["content"]!.GetValue<string>();
        api.Stored("template", MigratedPageKey)!["content"] = original + "<p>edited</p>";

        var results = await sync.EnsureAsync(Plan, preview: false, Ct, overwriteEdited: true);

        Assert.Equal(ItemOutcome.EditedOverwritten, results.Single(r => r.Key == MigratedPageKey).Outcome);
        Assert.Equal(original, api.Stored("template", MigratedPageKey)!["content"]!.GetValue<string>());
        var again = await sync.EnsureAsync(Plan, preview: false, Ct);
        Assert.All(again, r => Assert.Equal(ItemOutcome.Unchanged, r.Outcome));
    }

    [Fact]
    public async Task Template_without_a_marker_counts_as_edited()
    {
        var api = NewApi();
        api.Seed("template", MigratedPageKey, new JsonObject { ["name"] = "Migrated page", ["alias"] = "migratedPage", ["content"] = "<p>made by hand</p>" });

        var results = await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        Assert.Equal(ItemOutcome.EditedSkipped, results.Single(r => r.Key == MigratedPageKey).Outcome);
        Assert.DoesNotContain(api.Writes, c => c.Path == $"/template/{MigratedPageKey:D}");
    }

    [Fact]
    public async Task Edited_template_in_preview_is_skipped_or_would_update_with_the_flag()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);
        await sync.EnsureAsync(Plan, preview: false, Ct);
        api.Stored("template", MigratedPageKey)!["content"] = "@* migrator-fingerprint:" + new string('a', 64) + " *@ edited";
        api.Calls.Clear();

        var plain = await sync.EnsureAsync(Plan, preview: true, Ct);
        var overwrite = await sync.EnsureAsync(Plan, preview: true, Ct, overwriteEdited: true);

        Assert.Equal(ItemOutcome.EditedSkipped, plain.Single(r => r.Key == MigratedPageKey).Outcome);
        Assert.Equal(ItemOutcome.WouldUpdate, overwrite.Single(r => r.Key == MigratedPageKey).Outcome);
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task Template_from_an_earlier_version_of_the_tool_is_not_an_edit_but_other_marker_less_content_is()
    {
        var api = NewApi();
        var earlier = File.ReadAllText(RepoPaths.Combine("tests", "KenticoMigrator.Tests", "Fixtures", "templates", "MigratedLayout.earlier.cshtml"));
        var layout = Plan.Templates.Single(t => t.Alias == "migratedLayout");
        api.Seed("template", layout.Key, new JsonObject { ["name"] = layout.Name, ["alias"] = layout.Alias, ["content"] = earlier });
        var page = Plan.Templates.Single(t => t.Alias == "migratedPage");
        api.Seed("template", page.Key, new JsonObject { ["name"] = page.Name, ["alias"] = page.Alias, ["content"] = "<p>by hand</p>" });

        var results = await new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct);

        Assert.Equal(ItemOutcome.Updated, results.Single(r => r.Key == layout.Key).Outcome);
        Assert.Contains("migrator-fingerprint", api.Stored("template", layout.Key)!["content"]!.GetValue<string>());
        Assert.Equal(ItemOutcome.EditedSkipped, results.Single(r => r.Key == page.Key).Outcome);
    }

    [Fact]
    public async Task Type_level_fields_and_property_settings_an_editor_chose_are_kept()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);
        await sync.EnsureAsync(Plan, preview: false, Ct);
        var article = Plan.DocumentTypes.Single(t => t.Alias == "article");
        var stored = api.Stored("document-type", article.Key)!;
        stored["icon"] = "icon-custom";
        stored["description"] = "Edited description";
        stored["cleanup"] = new JsonObject { ["preventCleanup"] = true };
        var body = stored["properties"]!.AsArray().Single(p => p!["alias"]!.GetValue<string>() == "bodyText")!;
        body["description"] = "Help text";
        body["validation"] = new JsonObject { ["mandatory"] = true, ["mandatoryMessage"] = "Needed" };
        body["appearance"] = new JsonObject { ["labelOnTop"] = true };
        api.Calls.Clear();

        var quiet = await sync.EnsureAsync(Plan, preview: false, Ct);
        Assert.All(quiet, r => Assert.Equal(ItemOutcome.Unchanged, r.Outcome));
        Assert.Empty(api.Writes);

        stored["name"] = "Tampered";
        await sync.EnsureAsync(Plan, preview: false, Ct);

        Assert.Equal("icon-custom", stored["icon"]!.GetValue<string>());
        Assert.Equal("Edited description", stored["description"]!.GetValue<string>());
        Assert.True(stored["cleanup"]!["preventCleanup"]!.GetValue<bool>());
        var after = stored["properties"]!.AsArray().Single(p => p!["alias"]!.GetValue<string>() == "bodyText")!;
        Assert.Equal("Help text", after["description"]!.GetValue<string>());
        Assert.True(after["validation"]!["mandatory"]!.GetValue<bool>());
        Assert.Equal("Needed", after["validation"]!["mandatoryMessage"]!.GetValue<string>());
        Assert.True(after["appearance"]!["labelOnTop"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Editor_extras_keep_their_sort_order()
    {
        var api = NewApi();
        var sync = new SchemaSync(api);
        await sync.EnsureAsync(Plan, preview: false, Ct);
        var article = Plan.DocumentTypes.Single(t => t.Alias == "article");
        AddEditorAdditions(api, article, Guid.NewGuid(), Guid.NewGuid());
        api.Stored("document-type", article.Key)!["name"] = "Tampered";

        await sync.EnsureAsync(Plan, preview: false, Ct);

        var note = api.Stored("document-type", article.Key)!["properties"]!.AsArray().Single(p => p!["alias"]!.GetValue<string>() == "editorNote")!;
        Assert.Equal(99, note["sortOrder"]!.GetValue<int>());
    }

    [Fact]
    public async Task Results_sink_keeps_what_was_done_before_an_abort()
    {
        var api = NewApi();
        api.Failure = c => c.Method == HttpMethod.Post && c.Path == "/document-type" ? new UmbracoAuthException("token rejected") : null;
        var sink = new List<ItemResult>();

        await Assert.ThrowsAsync<UmbracoAuthException>(() => new SchemaSync(api).EnsureAsync(Plan, preview: false, Ct, results: sink));

        Assert.Contains(sink, r => r.Kind == ItemKinds.DataType && r.Outcome == ItemOutcome.Created);
        Assert.Contains(sink, r => r.Kind == ItemKinds.Template && r.Outcome == ItemOutcome.Created);
    }
}
