using System.Text;
using System.Text.Json.Nodes;
using KenticoMigrator.Migration;
using KenticoMigrator.Run;

namespace KenticoMigrator.Umbraco;

/// <summary>What <see cref="RedirectSync.RegisterAsync"/> registered, by reason, and the redirects that were refused.</summary>
public sealed record RedirectSyncResult(IReadOnlyList<RedirectSummary> Summaries, IReadOnlyList<ItemResult> Failures);

/// <summary>
/// Registers planned redirects through the host endpoint (research R4, contracts/redirect-endpoint.md) in batches of 200,
/// one reason at a time so the counts can be attributed to a reason. The endpoint is idempotent, so a re-run reports the
/// redirects as existing. In preview nothing is sent. Only redirects that were refused are reported as items.
/// </summary>
public sealed class RedirectSync(IUmbracoApi api)
{
    /// <summary>Path relative to the Management API base; the host controller lives beside the Management API controllers.</summary>
    internal const string EndpointPath = "/kentico-migration/redirects";

    internal const int BatchSize = 200;

    // The refusal texts of contracts/redirect-endpoint.md. Any other text (an exception type name) is not copied into a report.
    private static readonly HashSet<string> KnownReasons = new(StringComparer.Ordinal)
    {
        "entry is null",
        "url must start with /",
        "url must not start with //",
        "url is longer than 2000 characters",
        "url must not contain a query string or fragment",
        "url must not contain whitespace or control characters (percent-encode them)",
        "url must not contain control characters",
        "url must not contain //",
        "url must not be the site root",
        "content not found",
        "url already redirects to another document",
    };

    public async Task<RedirectSyncResult> RegisterAsync(IReadOnlyList<PlannedRedirect> redirects, bool preview, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(redirects);

        var summaries = new List<RedirectSummary>();
        var failures = new List<ItemResult>();
        if (preview)
        {
            return new RedirectSyncResult(summaries, failures);
        }

        foreach (var group in redirects.GroupBy(r => r.Reason))
        {
            int created = 0, existing = 0, failed = 0;
            foreach (var batch in group.Chunk(BatchSize))
            {
                var (batchCreated, batchExisting, batchFailures) = await SendBatchAsync(batch, ct).ConfigureAwait(false);
                created += batchCreated;
                existing += batchExisting;
                failed += batchFailures.Count;
                failures.AddRange(batchFailures);
            }

            summaries.Add(new RedirectSummary(group.Key, created, existing, failed));
        }

        return new RedirectSyncResult(summaries, failures);
    }

    private async Task<(int Created, int Existing, List<ItemResult> Failures)> SendBatchAsync(PlannedRedirect[] batch, CancellationToken ct)
    {
        var body = batch.Select(r => new RedirectRequest(EncodePath(r.OldUrl), r.TargetKey)).ToList();
        JsonNode answer;
        try
        {
            var response = await api.SendAsync(HttpMethod.Post, EndpointPath, body, ct).ConfigureAwait(false);
            answer = JsonNode.Parse(response.Body) ?? throw new InvalidDataException("The redirect endpoint answered without a body.");
        }
        catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
        {
            // The call failed as a whole (a POST is not retried once it may have arrived) or the answer cannot be read:
            // every entry of the batch failed, and nothing of the answer is copied into the report.
            var error = ApiErrors.Describe(ex);
            return (0, 0, [.. batch.Select(r => Failure(r, error))]);
        }

        var byUrl = batch.GroupBy(r => EncodePath(r.OldUrl)).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var failures = new List<ItemResult>();
        foreach (var entry in answer["failed"]?.AsArray() ?? [])
        {
            if (entry?["url"]?.GetValue<string>() is { } url && byUrl.TryGetValue(url, out var redirect))
            {
                var reason = entry["reason"]?.GetValue<string>();
                failures.Add(Failure(redirect, reason is not null && KnownReasons.Contains(reason) ? $"Redirect refused: {reason}" : "Redirect refused"));
            }
        }

        var failedCount = answer["failed"]?.AsArray().Count ?? 0;
        return (answer["created"]?.GetValue<int>() ?? 0, answer["existing"]?.GetValue<int>() ?? 0, PadFailures(failures, failedCount, batch));
    }

    // An entry the endpoint refused under a url the tool cannot match still counts as failed.
    private static List<ItemResult> PadFailures(List<ItemResult> failures, int failedCount, PlannedRedirect[] batch)
    {
        while (failures.Count < failedCount)
        {
            failures.Add(Failure(batch[0], "Redirect refused"));
        }

        return failures;
    }

    private static ItemResult Failure(PlannedRedirect redirect, string error) =>
        new(redirect.TargetKey, ItemKinds.Redirect, redirect.Reason, redirect.OldUrl, ItemOutcome.Failed, error);

    /// <summary>
    /// The old URL as a browser sends it: the host refuses literal spaces and keeps non-ASCII text only when it is encoded.
    /// Characters that are not allowed in a path (a literal <c>?</c> or <c>#</c> in a name is data, not a query or fragment) and a <c>%</c> that does not start an escape are percent-encoded; an
    /// existing escape is kept, so an encoded URL is not encoded twice.
    /// </summary>
    internal static string EncodePath(string url)
    {
        var encoded = new StringBuilder(url.Length);
        for (var i = 0; i < url.Length; i++)
        {
            var c = url[i];
            var keep = c is > ' ' and < '\u007f' && "\"<>\\^`{|}?#".IndexOf(c, StringComparison.Ordinal) < 0
                && (c != '%' || (i + 2 < url.Length && Uri.IsHexDigit(url[i + 1]) && Uri.IsHexDigit(url[i + 2])));
            if (keep)
            {
                encoded.Append(c);
                continue;
            }

            var text = char.IsHighSurrogate(c) && i + 1 < url.Length ? url.Substring(i++, 2) : c.ToString();
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                encoded.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return encoded.ToString();
    }
}
