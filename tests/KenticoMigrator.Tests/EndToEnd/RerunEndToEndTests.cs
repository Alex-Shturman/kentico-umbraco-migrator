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
}
