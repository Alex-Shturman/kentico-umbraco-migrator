using System.Text.Json.Nodes;
using KenticoMigrator.Kentico;
using KenticoMigrator.Run;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.EndToEnd;

/// <summary>Same environment as <see cref="PagesEndToEndTests"/>; every test leaves the target as the migration wrote it.</summary>
[Trait("Category", "Integration")]
[Collection("EndToEnd")]
public sealed class RerunEndToEndTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TargetCounts> CountsAsync(EndToEndHost host) =>
        await TargetCounter.CountAsync(new UmbracoTargetClient(host.Api), Ct);

    [Fact]
    public async Task Second_run_creates_and_updates_nothing()
    {
        using var host = EndToEndHost.Connect();
        var first = await host.RunAsync(CliArguments.Migrate, ct: Ct);
        Assert.True(first.ExitCode == ExitCodes.Success, first.Output);
        var countsAfterFirst = await CountsAsync(host);

        var second = await host.RunAsync(CliArguments.Migrate, ct: Ct);

        Assert.True(second.ExitCode == ExitCodes.Success, second.Output);
        var items = EndToEndHost.Items(second);
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Equal("Unchanged", i.Outcome));
        var countsAfterSecond = await CountsAsync(host);
        Assert.Equal(countsAfterFirst.DocumentsByType, countsAfterSecond.DocumentsByType);
        Assert.Equal(countsAfterFirst.Media, countsAfterSecond.Media);
        Assert.Equal(countsAfterFirst.Redirects, countsAfterSecond.Redirects);
    }

    [Fact]
    public async Task Edited_page_is_reported_and_kept()
    {
        using var host = EndToEndHost.Connect();
        Assert.Equal(ExitCodes.Success, (await host.RunAsync(CliArguments.Migrate, ct: Ct)).ExitCode);
        var tree = await TargetSnapshot.ReadAsync(host.Api, Ct);
        var about = tree.Find("Home", "About");
        var originalTitle = about.Text("title");
        await SetTitleAsync(host, about.Key, "Edited in the back office");

        try
        {
            var skipped = await host.RunAsync(CliArguments.Migrate, ct: Ct);

            Assert.Equal(ExitCodes.Success, skipped.ExitCode);
            Assert.Equal(["EditedSkipped"], EndToEndHost.Items(skipped).Where(i => i.Outcome != "Unchanged").Select(i => i.Outcome));
            Assert.Equal("Edited in the back office", (await TargetSnapshot.ReadAsync(host.Api, Ct)).Find("Home", "About").Text("title"));

            var overwritten = await host.RunAsync(CliArguments.Migrate, overwriteEdited: true, ct: Ct);

            Assert.Equal(["EditedOverwritten"], EndToEndHost.Items(overwritten).Where(i => i.Outcome != "Unchanged").Select(i => i.Outcome));
            Assert.Equal(originalTitle, (await TargetSnapshot.ReadAsync(host.Api, Ct)).Find("Home", "About").Text("title"));
        }
        finally
        {
            // Leave the target as the migration wrote it, whatever happened above.
            await host.RunAsync(CliArguments.Migrate, overwriteEdited: true, ct: Ct);
        }
    }

    [Fact]
    public async Task Preview_reports_would_create_on_empty_target_and_unchanged_after_migrate()
    {
        using var host = EndToEndHost.Connect();
        Assert.Equal(ExitCodes.Success, (await host.RunAsync(CliArguments.Migrate, ct: Ct)).ExitCode);
        var before = await CountsAsync(host);

        // The same site under keys the target has never seen: every page is new, so a preview says would create.
        var asNew = await host.RunAsync(CliArguments.Plan, ct: Ct, wrapReader: inner => new RekeyedReader(inner));
        Assert.Equal(ExitCodes.Success, asNew.ExitCode);
        var documents = EndToEndHost.Items(asNew).Where(i => i.Kind == ItemKinds.Document).ToList();
        Assert.NotEmpty(documents);
        Assert.All(documents, i => Assert.Equal("WouldCreate", i.Outcome));

        var afterMigrate = await host.RunAsync(CliArguments.Plan, ct: Ct);
        Assert.Equal(ExitCodes.Success, afterMigrate.ExitCode);
        Assert.All(EndToEndHost.Items(afterMigrate), i => Assert.Equal("Unchanged", i.Outcome));

        var after = await CountsAsync(host);
        Assert.Equal(before.DocumentsByType, after.DocumentsByType);
    }

    [Fact]
    public async Task Page_an_editor_unpublished_stays_unpublished_and_is_listed_for_review()
    {
        using var host = EndToEndHost.Connect();
        Assert.Equal(ExitCodes.Success, (await host.RunAsync(CliArguments.Migrate, ct: Ct)).ExitCode);
        var page = (await TargetSnapshot.ReadAsync(host.Api, Ct)).Find("Home", "Articles", "Paddling basics");
        var publish = new { publishSchedules = new[] { new { culture = (string?)null } } };

        try
        {
            await host.Api.SendAsync(HttpMethod.Put, $"/document/{page.Key:D}/unpublish", new { cultures = (string[]?)null }, Ct);

            var run = await host.RunAsync(CliArguments.Migrate, ct: Ct);

            Assert.True(run.ExitCode == ExitCodes.Success, run.Output);
            Assert.Equal("Unchanged", Assert.Single(EndToEndHost.Items(run), i => i.Kind == ItemKinds.Document && i.OldPath == "/Articles/Paddling-Basics").Outcome);
            Assert.Equal("Draft", (await ReadPageAsync(host, page.Key)).State);
            Assert.Contains(
                $"/Articles/Paddling-Basics: Page {page.Key:D} is planned as published but not published in Umbraco",
                File.ReadAllText(Path.Combine(run.RunDir, "report.md")));
        }
        finally
        {
            await host.Api.SendAsync(HttpMethod.Put, $"/document/{page.Key:D}/publish", publish, Ct);
        }
    }

    [Fact]
    public async Task Source_change_updates_a_page_an_editor_unpublished_without_publishing_it()
    {
        using var host = EndToEndHost.Connect();
        Assert.Equal(ExitCodes.Success, (await host.RunAsync(CliArguments.Migrate, ct: Ct)).ExitCode);
        var page = (await TargetSnapshot.ReadAsync(host.Api, Ct)).Find("Home", "Articles", "Paddling basics");
        const string PageOldPath = "/Articles/Paddling-Basics";
        var publish = new { publishSchedules = new[] { new { culture = (string?)null } } };

        try
        {
            await host.Api.SendAsync(HttpMethod.Put, $"/document/{page.Key:D}/unpublish", new { cultures = (string[]?)null }, Ct);

            var run = await host.RunAsync(CliArguments.Migrate, ct: Ct, wrapReader: inner => new SourceEditingReader(inner, site => site with
            {
                Documents = [.. site.Documents.Select(d => d.AliasPath == PageOldPath ? d with { PageTitle = "Changed in the source" } : d)],
            }));

            Assert.True(run.ExitCode == ExitCodes.Success, run.Output);
            Assert.Equal("Updated", Assert.Single(EndToEndHost.Items(run), i => i.Kind == ItemKinds.Document && i.OldPath == PageOldPath).Outcome);
            Assert.Equal("Draft", (await ReadPageAsync(host, page.Key)).State);
            Assert.Contains($"{PageOldPath}: Page {page.Key:D} is planned as published but not published in Umbraco", File.ReadAllText(Path.Combine(run.RunDir, "report.md")));
        }
        finally
        {
            await host.Api.SendAsync(HttpMethod.Put, $"/document/{page.Key:D}/publish", publish, Ct);
            await host.RunAsync(CliArguments.Migrate, overwriteEdited: true, ct: Ct);
        }
    }

    [Fact]
    public async Task Refused_publish_is_failed_once_then_unchanged_and_overwrite_republishes_after_the_cause_is_fixed()
    {
        using var host = EndToEndHost.Connect();
        Assert.Equal(ExitCodes.Success, (await host.RunAsync(CliArguments.Migrate, ct: Ct)).ExitCode);
        var tree = await TargetSnapshot.ReadAsync(host.Api, Ct);
        var parent = tree.Find("Home", "Articles");
        var page = tree.Find("Home", "Articles", "Paddling basics");
        var originalTitle = page.Text("title");
        const string PageOldPath = "/Articles/Paddling-Basics";
        var publish = new { publishSchedules = new[] { new { culture = (string?)null } } };

        // The parent is a page the plan does not publish (the wrapper marks it unpublished in the source), so the tool leaves it
        // alone. An editor unpublishes it, and Umbraco then refuses to publish any page below it.
        static IKenticoReader ParentNotPublished(IKenticoReader inner) => new SourceEditingReader(inner, site => site with
        {
            Documents = [.. site.Documents.Select(d => d.AliasPath == "/Articles" ? d with { IsPublished = false } : d)],
        });

        try
        {
            await host.Api.SendAsync(HttpMethod.Put, $"/document/{parent.Key:D}/unpublish", new { cultures = (string[]?)null }, Ct);
            await SetTitleAsync(host, page.Key, "Edited in the back office");

            var refused = await host.RunAsync(CliArguments.Migrate, overwriteEdited: true, ct: Ct, wrapReader: ParentNotPublished);

            Assert.True(refused.ExitCode == ExitCodes.ItemErrors, refused.Output);
            Assert.Contains(EndToEndHost.Items(refused), i => i.Kind == ItemKinds.Document && i.OldPath == PageOldPath && i.Outcome == "Failed");
            var pending = await ReadPageAsync(host, page.Key);
            Assert.Equal(originalTitle, pending.Text("title")); // written, as pending changes
            Assert.NotEqual("Published", pending.State);

            // Still refused, but the page now holds what the tool wrote: it is not failed again and not written again.
            var again = await host.RunAsync(CliArguments.Migrate, ct: Ct, wrapReader: ParentNotPublished);
            Assert.Equal("Unchanged", Assert.Single(EndToEndHost.Items(again), i => i.Kind == ItemKinds.Document && i.OldPath == PageOldPath).Outcome);

            // Fix the cause. The pending changes are not published by a plain run: they could be an editor's held-back draft.
            await host.Api.SendAsync(HttpMethod.Put, $"/document/{parent.Key:D}/publish", publish, Ct);
            var plain = await host.RunAsync(CliArguments.Migrate, ct: Ct, wrapReader: ParentNotPublished);
            Assert.Equal("Unchanged", Assert.Single(EndToEndHost.Items(plain), i => i.Kind == ItemKinds.Document && i.OldPath == PageOldPath).Outcome);
            Assert.NotEqual("Published", (await ReadPageAsync(host, page.Key)).State);

            // Change the page in Umbraco and re-run with the overwrite option: it is written and published.
            await SetTitleAsync(host, page.Key, "Edited again");
            var republished = await host.RunAsync(CliArguments.Migrate, overwriteEdited: true, ct: Ct, wrapReader: ParentNotPublished);

            Assert.True(republished.ExitCode == ExitCodes.Success, republished.Output);
            Assert.Equal("EditedOverwritten", Assert.Single(EndToEndHost.Items(republished), i => i.Kind == ItemKinds.Document && i.OldPath == PageOldPath).Outcome);
            var final = await ReadPageAsync(host, page.Key);
            Assert.Equal("Published", final.State);
            Assert.Equal(originalTitle, final.Text("title"));
        }
        finally
        {
            // Leave the target as the migration wrote it, whatever happened above.
            await host.Api.SendAsync(HttpMethod.Put, $"/document/{parent.Key:D}/publish", publish, Ct);
            await host.RunAsync(CliArguments.Migrate, overwriteEdited: true, ct: Ct);
        }
    }

    private static async Task<TargetPage> ReadPageAsync(EndToEndHost host, Guid key)
    {
        var doc = (await host.Api.GetAsync($"/document/{key:D}", Ct))!;
        var values = doc["values"]!.AsArray().ToDictionary(v => v!["alias"]!.GetValue<string>(), v => v!["value"]);
        return new TargetPage(key, null, doc["variants"]![0]!["name"]!.GetValue<string>(), string.Empty, doc["variants"]![0]!["state"]!.GetValue<string>(), values);
    }

    private static async Task SetTitleAsync(EndToEndHost host, Guid key, string title)
    {
        var current = (await host.Api.GetAsync($"/document/{key:D}", Ct))!;
        var values = new JsonArray();
        foreach (var value in current["values"]!.AsArray())
        {
            var alias = value!["alias"]!.GetValue<string>();
            values.Add(new JsonObject
            {
                ["alias"] = alias,
                ["culture"] = null,
                ["segment"] = null,
                ["value"] = alias == "title" ? title : value["value"]?.DeepClone(),
            });
        }

        var body = new JsonObject
        {
            ["template"] = current["template"]?.DeepClone(),
            ["values"] = values,
            ["variants"] = new JsonArray(new JsonObject
            {
                ["culture"] = null,
                ["segment"] = null,
                ["name"] = current["variants"]![0]!["name"]!.GetValue<string>(),
            }),
        };
        await host.Api.SendAsync(HttpMethod.Put, $"/document/{key:D}", body, Ct);
    }

    /// <summary>Reads the real source, then gives every page and every unlinked row a new key.</summary>
    private sealed class RekeyedReader(IKenticoReader inner) : IKenticoReader
    {
        public async Task<SourceSite> ReadAsync(IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct)
        {
            var site = await inner.ReadAsync(unlinkedRowClasses, ct);
            return site with
            {
                Documents = [.. site.Documents.Select(d => d with { NodeGuid = Guid.NewGuid() })],
                // Rows without a page get their key from the row id.
                UnlinkedRows = site.UnlinkedRows.ToDictionary(
                    p => p.Key,
                    p => (IReadOnlyList<SourceRow>)[.. p.Value.Select(r => r with { Id = r.Id + 100_000 })]),
            };
        }

        public Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct) => inner.OpenAttachmentAsync(attachmentGuid, ct);

        public Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct) => inner.OpenMediaFileAsync(file, ct);

        public Task<SourceCounts> CountAsync(CancellationToken ct) => inner.CountAsync(ct);
    }

    /// <summary>Reads the real source and lets a test change the site before the migrator sees it.</summary>
    private sealed class SourceEditingReader(IKenticoReader inner, Func<SourceSite, SourceSite> edit) : IKenticoReader
    {
        public async Task<SourceSite> ReadAsync(IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct) =>
            edit(await inner.ReadAsync(unlinkedRowClasses, ct));

        public Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct) => inner.OpenAttachmentAsync(attachmentGuid, ct);

        public Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct) => inner.OpenMediaFileAsync(file, ct);

        public Task<SourceCounts> CountAsync(CancellationToken ct) => inner.CountAsync(ct);
    }
}
