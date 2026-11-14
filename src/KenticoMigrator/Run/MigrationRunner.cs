using System.Diagnostics;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Run;

/// <summary>What one run is asked to do.</summary>
public sealed record RunRequest(
    string Command,
    SiteProfile Profile,
    string RunDir,
    bool OverwriteEdited = false,
    Func<SourceMediaFile, bool>? MediaFileExists = null);

/// <summary>
/// Runs the phases of one command in the order of FR-026 and returns the process exit code (contracts/cli.md). The console
/// gets one line per phase with counts and elapsed time; no field content is printed.
/// </summary>
public sealed class MigrationRunner(IKenticoReader reader, IUmbracoApi? api, TextWriter output)
{
    public async Task<int> RunAsync(RunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Command != CliArguments.Migrate)
        {
            output.WriteLine($"The '{request.Command}' command is not available yet.");
            return ExitCodes.ConfigurationOrPlanError;
        }

        if (api is null)
        {
            output.WriteLine("Umbraco is not configured (Umbraco:BaseUrl, Umbraco:ClientId, Umbraco:ClientSecret).");
            return ExitCodes.ConfigurationOrPlanError;
        }

        var site = await Phase(Phases.Read, async () =>
        {
            var read = await reader.ReadAsync([.. request.Profile.UnlinkedRows.Select(r => r.KenticoClass)], ct).ConfigureAwait(false);
            return (read, $"{read.Documents.Count} pages, {read.Attachments.Count} attachments, {read.MediaFiles.Count} media files");
        }).ConfigureAwait(false);

        var plan = Phase(Phases.Plan, () =>
        {
            var built = PlanBuilder.Build(site, request.Profile, request.MediaFileExists);
            var errors = built.Issues.Count(i => i.Severity == PlanSeverities.Error);
            return (built, $"{built.Documents.Count} pages, {built.Skipped.Count} skipped, {errors} errors");
        });

        var errorIssues = plan.Issues.Where(i => i.Severity == PlanSeverities.Error).ToList();
        if (errorIssues.Count > 0)
        {
            foreach (var issue in errorIssues)
            {
                output.WriteLine($"Plan error at {issue.OldPath}: {issue.Message}");
            }

            return ExitCodes.ConfigurationOrPlanError;
        }

        var schema = await Phase(Phases.Schema, async () =>
        {
            var results = await new SchemaSync(api).EnsureAsync(plan, preview: false, ct).ConfigureAwait(false);
            return (results, Summarize(results));
        }).ConfigureAwait(false);

        var documents = await Phase(Phases.Documents, async () =>
        {
            var results = await new DocumentSync(api, plan.DocumentTypes, plan.Templates)
                .SyncAsync(plan.Documents, new SyncOptions(request.OverwriteEdited), ct).ConfigureAwait(false);
            return (results, Summarize(results));
        }).ConfigureAwait(false);

        var failures = schema.Concat(documents).Where(r => r.Outcome == ItemOutcome.Failed).ToList();
        WriteFailures(failures);
        return failures.Count > 0 ? ExitCodes.ItemErrors : ExitCodes.Success;
    }

    private async Task<T> Phase<T>(string name, Func<Task<(T Value, string Summary)>> body)
    {
        var watch = Stopwatch.StartNew();
        var (value, summary) = await body().ConfigureAwait(false);
        output.WriteLine($"{name}: {summary} ({watch.Elapsed.TotalSeconds:0.0} s)");
        return value;
    }

    private T Phase<T>(string name, Func<(T Value, string Summary)> body) =>
        Phase(name, () => Task.FromResult(body())).GetAwaiter().GetResult();

    // One line per distinct error with a count and one example (a type alias or old path, never a field value).
    private void WriteFailures(IReadOnlyList<ItemResult> failures)
    {
        foreach (var group in failures.GroupBy(f => (f.Kind, f.Error)))
        {
            var example = group.First();
            output.WriteLine($"  failed {group.Key.Kind}: {group.Key.Error} ({group.Count()}x, e.g. {(example.OldPath.Length > 0 ? example.OldPath : example.TypeAlias)})");
        }
    }

    private static string Summarize(IReadOnlyList<ItemResult> results) =>
        string.Join(", ", results.GroupBy(r => r.Outcome).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
}
