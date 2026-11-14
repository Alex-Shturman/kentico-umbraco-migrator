using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using KenticoMigrator.Migration;
using KenticoMigrator.Run;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class RedirectSyncTests
{
    private const string Endpoint = "/kentico-migration/redirects";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlannedRedirect Redirect(int n, string reason = RedirectReasons.Alias, string? oldUrl = null) =>
        new(oldUrl ?? $"/old-{n}", GuidFor(n % 7 + 1), "/new/", reason);

    /// <summary>Answers like the host: every entry is created unless the hook says otherwise.</summary>
    private sealed class Host : IUmbracoApi
    {
        public List<JsonArray> Batches { get; } = [];

        public Func<JsonNode, string?>? Refuse { get; set; }

        public Func<int, Exception?>? FailBatch { get; set; }

        /// <summary>When set, the body of every 200 answer (a host that answers with something that is not the contract).</summary>
        public string? RawAnswer { get; set; }

        public HashSet<string> Known { get; } = [];

        public Task<JsonNode?> GetAsync(string path, CancellationToken ct) => throw new InvalidOperationException(path);

        public Task<ApiResponse> UploadTemporaryFileAsync(Guid id, string fileName, string mimeType, Stream content, CancellationToken ct) =>
            throw new InvalidOperationException();

        public Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, method);
            Assert.Equal(Endpoint, path);
            var batch = JsonSerializer.SerializeToNode(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsArray();
            Batches.Add((JsonArray)batch.DeepClone());
            if (FailBatch?.Invoke(Batches.Count - 1) is { } failure)
            {
                throw failure;
            }

            int created = 0, existing = 0;
            var failed = new JsonArray();
            foreach (var entry in batch)
            {
                var url = entry!["url"]!.GetValue<string>();
                if (Refuse?.Invoke(entry) is { } reason)
                {
                    failed.Add(new JsonObject { ["url"] = url, ["reason"] = reason });
                }
                else if (!Known.Add(url + entry["contentKey"]))
                {
                    existing++;
                }
                else
                {
                    created++;
                }
            }

            var answer = new JsonObject { ["created"] = created, ["existing"] = existing, ["failed"] = failed };
            return Task.FromResult(new ApiResponse(HttpStatusCode.OK, RawAnswer ?? answer.ToJsonString(), null));
        }
    }

    [Fact]
    public async Task Sends_batches_of_200()
    {
        var host = new Host();
        var redirects = Enumerable.Range(1, 450).Select(n => Redirect(n)).ToList();

        var result = await new RedirectSync(host).RegisterAsync(redirects, preview: false, Ct);

        Assert.Equal([200, 200, 50], host.Batches.Select(b => b.Count));
        var summary = Assert.Single(result.Summaries);
        Assert.Equal(new RedirectSummary(RedirectReasons.Alias, 450, 0, 0), summary);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task Preview_sends_nothing()
    {
        var host = new Host();

        var result = await new RedirectSync(host).RegisterAsync([Redirect(1)], preview: true, Ct);

        Assert.Empty(host.Batches);
        Assert.Empty(result.Summaries);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task Body_has_the_encoded_path_and_the_target_key()
    {
        var host = new Host();

        await new RedirectSync(host).RegisterAsync([Redirect(1, oldUrl: "/brand-search/Sea Spirit"), Redirect(2, oldUrl: "/café"), Redirect(3, oldUrl: "/a%20b"), Redirect(4, oldUrl: "/50%"), Redirect(5, oldUrl: "/brand-search/Who? Me #1")], false, Ct);

        var urls = host.Batches.Single().Select(e => e!["url"]!.GetValue<string>()).ToList();
        Assert.Equal(["/brand-search/Sea%20Spirit", "/caf%C3%A9", "/a%20b", "/50%25", "/brand-search/Who%3F%20Me%20%231"], urls);
        Assert.Equal(GuidFor(2).ToString("D"), host.Batches.Single()[0]!["contentKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task Counts_are_kept_per_reason_and_a_second_registration_reports_existing()
    {
        var host = new Host();
        var redirects = new[] { Redirect(1, RedirectReasons.Aspx), Redirect(2, RedirectReasons.Alias), Redirect(3, RedirectReasons.Aspx) };
        var sync = new RedirectSync(host);

        var first = await sync.RegisterAsync(redirects, false, Ct);
        var second = await sync.RegisterAsync(redirects, false, Ct);

        Assert.Equal(
            [new RedirectSummary(RedirectReasons.Aspx, 2, 0, 0), new RedirectSummary(RedirectReasons.Alias, 1, 0, 0)],
            first.Summaries);
        Assert.Equal(
            [new RedirectSummary(RedirectReasons.Aspx, 0, 2, 0), new RedirectSummary(RedirectReasons.Alias, 0, 1, 0)],
            second.Summaries);
    }

    [Fact]
    public async Task A_refused_entry_is_a_failed_item_and_the_others_are_still_registered()
    {
        var host = new Host { Refuse = e => e["url"]!.GetValue<string>() == "/old-2" ? "content not found" : null };

        var result = await new RedirectSync(host).RegisterAsync([Redirect(1), Redirect(2), Redirect(3)], false, Ct);

        Assert.Equal(new RedirectSummary(RedirectReasons.Alias, 2, 0, 1), Assert.Single(result.Summaries));
        var failure = Assert.Single(result.Failures);
        Assert.Equal(ItemKinds.Redirect, failure.Kind);
        Assert.Equal(ItemOutcome.Failed, failure.Outcome);
        Assert.Equal("/old-2", failure.OldPath);
        Assert.Equal(GuidFor(3), failure.Key);
        Assert.Equal("Redirect refused: content not found", failure.Error);
    }

    [Fact]
    public async Task An_unknown_refusal_text_is_not_copied_into_the_report()
    {
        var host = new Host { Refuse = _ => "SomethingWithAValue(secret)" };

        var result = await new RedirectSync(host).RegisterAsync([Redirect(1)], false, Ct);

        Assert.Equal("Redirect refused", Assert.Single(result.Failures).Error);
    }

    [Fact]
    public async Task A_failed_call_fails_its_batch_only()
    {
        var host = new Host { FailBatch = i => i == 0 ? FakeUmbracoApi.Problem(HttpStatusCode.BadGateway, "Bad gateway") : null };
        var redirects = Enumerable.Range(1, 250).Select(n => Redirect(n)).ToList();

        var result = await new RedirectSync(host).RegisterAsync(redirects, false, Ct);

        Assert.Equal(new RedirectSummary(RedirectReasons.Alias, 50, 0, 200), Assert.Single(result.Summaries));
        Assert.Equal(200, result.Failures.Count);
        Assert.All(result.Failures, f => Assert.Equal("HTTP 502 Bad gateway", f.Error));
    }

    [Fact]
    public async Task A_rejected_token_stops_the_registration()
    {
        var host = new Host { FailBatch = _ => FakeUmbracoApi.Problem(HttpStatusCode.Unauthorized, "Unauthorized") };

        await Assert.ThrowsAsync<UmbracoApiException>(() => new RedirectSync(host).RegisterAsync([Redirect(1)], false, Ct));
    }

    [Fact]
    public async Task A_malformed_answer_fails_its_batch_not_the_registration()
    {
        var host = new Host { RawAnswer = "<html>gateway</html>" };

        var result = await new RedirectSync(host).RegisterAsync([Redirect(1), Redirect(2)], false, Ct);

        Assert.Equal(new RedirectSummary(RedirectReasons.Alias, 0, 0, 2), Assert.Single(result.Summaries));
        Assert.All(result.Failures, f => Assert.Equal(ItemOutcome.Failed, f.Outcome));
        Assert.DoesNotContain(result.Failures, f => f.Error!.Contains("gateway", StringComparison.Ordinal));
    }
}
