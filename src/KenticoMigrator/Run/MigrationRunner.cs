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
/// Runs the phases of one command in the order of FR-026 (read, plan, snapshot, schema, media, documents, missing in source,
/// snapshot, count guard, source counts, report) and returns the process exit code (contracts/cli.md). The console gets one
/// line per phase with counts and elapsed time; no field content is printed. <c>plan</c> runs the same path in preview mode.
/// </summary>
/// <param name="reader">The source.</param>
/// <param name="api">The target, or null when Umbraco is not configured.</param>
/// <param name="output">Console summary.</param>
/// <param name="tree">Count walk over the target; defaults to the Management API behind <paramref name="api"/>.</param>
/// <param name="time">Clock for the report start time.</param>
public sealed class MigrationRunner(IKenticoReader reader, IUmbracoApi? api, TextWriter output, ITargetTreeClient? tree = null, TimeProvider? time = null)
{
    private const string ProbePath = "/tree/document/root?skip=0&take=1";
    private const string ConfigurationHint = "Umbraco is not configured (Umbraco:BaseUrl and Umbraco:ClientSecret).";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<int> RunAsync(RunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Command == CliArguments.Counts
            ? await CountsAsync(ct).ConfigureAwait(false)
            : await RunPhasesAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<int> RunPhasesAsync(RunRequest request, CancellationToken ct)
    {
        var preview = request.Command == CliArguments.Plan;
        var startedUtc = _time.GetUtcNow().UtcDateTime;
        var total = Stopwatch.StartNew();
        var timings = new List<PhaseTiming>();
        var extraIssues = new List<PlanIssue>();
        var links = new LinkStats(0, 0, 0, []);
        IReadOnlyList<RedirectSummary> redirectSummaries = [];
        IReadOnlyList<PlannedRedirect> plannedRedirects = [];

        var (site, sourceBefore) = await Timed(timings, Phases.Read, async () =>
        {
            var read = await reader.ReadAsync([.. request.Profile.UnlinkedRows.Select(r => r.KenticoClass)], ct).ConfigureAwait(false);
            var counts = await reader.CountAsync(ct).ConfigureAwait(false);
            return ((read, counts), $"{read.Documents.Count} pages, {read.Attachments.Count} attachments, {read.MediaFiles.Count} media files");
        }).ConfigureAwait(false);

        var plan = await Timed(timings, Phases.Plan, () =>
        {
            var built = PlanBuilder.Build(site, request.Profile, request.MediaFileExists);
            var errors = built.Issues.Count(i => i.Severity == PlanSeverities.Error);
            return Task.FromResult((built, $"{built.Documents.Count} pages, {built.Skipped.Count} skipped, {errors} errors"));
        }).ConfigureAwait(false);

        void Finish(int exit, IReadOnlyList<ItemResult> items, IReadOnlyList<MissingInSource> missing,
            TargetCounts? targetBefore, TargetCounts? targetAfter, IReadOnlyList<CountDrop> drops, SourceCounts? sourceAfter)
        {
            var report = new RunReport(
                Path.GetFileName(request.RunDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                request.Command,
                startedUtc,
                total.Elapsed,
                site.KenticoVersion,
                sourceBefore,
                sourceAfter,
                targetBefore,
                targetAfter,
                drops,
                items,
                plan.Skipped,
                [.. plan.Issues, .. extraIssues],
                links,
                redirectSummaries,
                missing,
                [.. plan.Documents.Where(d => d.ScheduleNote is not null).Select(d => new ScheduleNote(d.Key, d.OldPath, d.ScheduleNote!))],
                timings,
                exit,
                plannedRedirects);
            Report.Write(report, request.RunDir);
            output.WriteLine($"report: {request.RunDir}");
        }

        var planErrors = plan.Issues.Where(i => i.Severity == PlanSeverities.Error).ToList();
        if (planErrors.Count > 0)
        {
            foreach (var issue in planErrors)
            {
                output.WriteLine($"plan error at {issue.OldPath}: {issue.Message}");
            }

            Finish(ExitCodes.ConfigurationOrPlanError, [], [], null, null, [], null);
            return ExitCodes.ConfigurationOrPlanError;
        }

        var unreachable = await ProbeAsync(ct).ConfigureAwait(false);
        if (unreachable is not null)
        {
            if (!preview)
            {
                output.WriteLine($"cannot reach Umbraco: {unreachable}");
                extraIssues.Add(new PlanIssue(PlanSeverities.Error, "target", $"Umbraco not reachable: {unreachable}"));
                Finish(ExitCodes.ConfigurationOrPlanError, [], [], null, null, [], null);
                return ExitCodes.ConfigurationOrPlanError;
            }

            output.WriteLine($"target not reachable, plan only: {unreachable}");
            extraIssues.Add(new PlanIssue(PlanSeverities.Warning, "target", $"Target not reachable, plan-only preview: {unreachable}"));
            Finish(ExitCodes.Success, [], [], null, null, [], null);
            return ExitCodes.Success;
        }

        var counter = tree ?? new UmbracoTargetClient(api!);
        var before = await Timed(timings, Phases.Snapshot, async () =>
        {
            var counts = await TargetCounter.CountAsync(counter, ct).ConfigureAwait(false);
            return (counts, Describe(counts));
        }).ConfigureAwait(false);

        // From here on something may have been written, so a failure ends the run with a report and exit code 1, not 2.
        var schemaResults = new List<ItemResult>();
        var mediaResults = new List<ItemResult>();
        var documents = new List<ItemResult>();
        var redirectFailures = new List<ItemResult>();
        IReadOnlyList<MissingInSource> missing = [];
        TargetCounts? after = null;
        IReadOnlyList<CountDrop> drops = [];
        SourceCounts? sourceAfter = null;
        var stopped = false;
        try
        {
            await Timed(timings, Phases.Schema, async () =>
            {
                await new SchemaSync(api!).EnsureAsync(plan, preview, ct, request.OverwriteEdited, schemaResults).ConfigureAwait(false);
                return (0, Summarize(schemaResults));
            }).ConfigureAwait(false);

            var mediaSync = new MediaSync(api!);
            await Timed(timings, Phases.Media, async () =>
            {
                await mediaSync.SyncAsync(plan.Media, reader, new SyncOptions(request.OverwriteEdited, preview), ct, mediaResults).ConfigureAwait(false);
                return (0, Summarize(mediaResults));
            }).ConfigureAwait(false);

            // A picker must not point at a media item that could not be written: Umbraco would drop it on save and the next
            // run would see an editor change.
            var failedMedia = mediaResults.Where(r => r.Outcome == ItemOutcome.Failed).Select(r => r.Key).ToHashSet();
            // One rewriter for the whole document phase (it is not thread-safe); image sources come from the media phase.
            var rewriter = new RichTextRewriter(
                UrlIndex.Create(plan, site.Aliases, request.Profile),
                key => mediaSync.Urls.GetValueOrDefault(key));
            var documentSync = new DocumentSync(api!, plan.DocumentTypes, plan.Templates, rewriter);
            await Timed(timings, Phases.Documents, async () =>
            {
                try
                {
                    await documentSync
                        .SyncAsync([.. plan.Documents.Select(d => WithoutMedia(d, failedMedia))], new SyncOptions(request.OverwriteEdited, preview), ct, documents)
                        .ConfigureAwait(false);
                }
                finally
                {
                    links = documentSync.Links; // what was counted is reported even when the phase stopped early
                    extraIssues.AddRange(documentSync.NotPublished);
                }

                var sorts = documents.Where(r => r.Kind == ItemKinds.Sort).ToList();
                return (0, Summarize(documents.Where(r => r.Kind == ItemKinds.Document).ToList()) + (sorts.Count > 0 ? $"; sorts: {Summarize(sorts)}" : string.Empty));
            }).ConfigureAwait(false);

            // Redirects come from the URLs the pages really have (FR-023), so they follow the page writes.
            await Timed(timings, Phases.Redirects, async () =>
            {
                IReadOnlyDictionary<Guid, string> urls;
                try
                {
                    urls = await documentSync.ReadUrlsAsync([.. plan.Documents.Where(d => d.Publish).Select(d => d.Key)], ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
                {
                    // Without the actual URLs no redirect can be planned; the run goes on to verify and report.
                    redirectFailures.Add(new ItemResult(Guid.Empty, ItemKinds.Redirect, string.Empty, "(page URLs)", ItemOutcome.Failed, ApiErrors.Describe(ex)));
                    return (0, "page URLs could not be read, no redirects planned");
                }

                var planned = RedirectPlanner.Plan(plan.Documents, urls);
                extraIssues.AddRange(planned.Warnings);
                plannedRedirects = planned.Redirects;
                var registered = await new RedirectSync(api!).RegisterAsync(planned.Redirects, preview, ct).ConfigureAwait(false);
                redirectSummaries = registered.Summaries;
                redirectFailures.AddRange(registered.Failures);
                return (0, preview
                    ? $"{planned.Redirects.Count} planned for pages that already exist, nothing sent"
                    : $"{planned.Redirects.Count} planned; {Total(registered.Summaries, s => s.Created)} created, {Total(registered.Summaries, s => s.Existing)} existing, {Total(registered.Summaries, s => s.Failed)} failed");
            }).ConfigureAwait(false);

            missing = await Timed(timings, Phases.Verify, async () =>
            {
                var found = await MissingInSourceFinder.FindAsync(plan, api!, ct).ConfigureAwait(false);
                if (!preview)
                {
                    after = await TargetCounter.CountAsync(counter, ct).ConfigureAwait(false);
                    drops = CountGuard.FindDrops(before, after);
                    sourceAfter = await reader.CountAsync(ct).ConfigureAwait(false);
                    if (!SameCounts(sourceBefore, sourceAfter))
                    {
                        extraIssues.Add(new PlanIssue(PlanSeverities.Warning, "source", "The source counts changed while the run was reading and writing."));
                    }
                }

                return (found, $"{found.Count} missing in source, {drops.Count} count drops");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Any failure, a cancel included: the pages written so far are in the lists, so the report still tells what happened.
            stopped = true;
            var reason = ex is OperationCanceledException && ct.IsCancellationRequested ? "cancelled" : ApiErrors.Describe(ex);
            output.WriteLine($"run stopped: {reason}");
            extraIssues.Add(new PlanIssue(PlanSeverities.Error, "target", $"The run stopped early: {reason}"));
        }

        var items = schemaResults.Concat(mediaResults).Concat(documents).Concat(redirectFailures).ToList();

        WriteSummaryLines(plan, items, missing, drops, links, extraIssues);

        var exit = drops.Count > 0 ? ExitCodes.CountDrop
            : stopped || items.Any(r => r.Outcome == ItemOutcome.Failed) ? ExitCodes.ItemErrors
            : ExitCodes.Success;
        WriteFailures(items);
        Finish(exit, items, missing, before, after, drops, sourceAfter);
        return exit;
    }

    private async Task<int> CountsAsync(CancellationToken ct)
    {
        var source = await reader.CountAsync(ct).ConfigureAwait(false);
        output.WriteLine($"source: {string.Join(", ", source.DocumentsByClass.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {p.Value}"))}; attachments {source.Attachments}; media files {source.MediaFiles}; aliases {source.Aliases}");

        var unreachable = await ProbeAsync(ct).ConfigureAwait(false);
        if (unreachable is not null)
        {
            output.WriteLine($"target: not reachable ({unreachable})");
            return ExitCodes.Success;
        }

        var counts = await TargetCounter.CountAsync(tree ?? new UmbracoTargetClient(api!), ct).ConfigureAwait(false);
        output.WriteLine($"target: {Describe(counts)}");
        return ExitCodes.Success;
    }

    // Takes the media items that failed out of the picker values of a page.
    private static TargetDocument WithoutMedia(TargetDocument doc, IReadOnlySet<Guid> failed)
    {
        if (failed.Count == 0 || !doc.Values.Values.Any(v => v.Editor is "MediaSingle" or "MediaMultiple"))
        {
            return doc;
        }

        PlannedValue Strip(PlannedValue value) => value switch
        {
            { Editor: "MediaSingle", Raw: Guid key } when failed.Contains(key) => value with { Raw = null },
            { Editor: "MediaMultiple", Raw: IEnumerable<Guid> keys } => value with { Raw = keys.Where(k => !failed.Contains(k)).ToArray() },
            _ => value,
        };

        return doc with { Values = doc.Values.ToDictionary(v => v.Key, v => Strip(v.Value)) };
    }

    // Null when the target answers; otherwise the reason (never a secret: only status and title).
    private async Task<string?> ProbeAsync(CancellationToken ct)
    {
        if (api is null)
        {
            return ConfigurationHint;
        }

        try
        {
            await api.GetAsync(ProbePath, ct).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is UmbracoAuthException or UmbracoApiException && !ct.IsCancellationRequested)
        {
            return ApiErrors.Describe(ex);
        }
    }

    private async Task<T> Timed<T>(List<PhaseTiming> timings, string phase, Func<Task<(T Value, string Summary)>> body)
    {
        var watch = Stopwatch.StartNew();
        var (value, summary) = await body().ConfigureAwait(false);
        timings.Add(new PhaseTiming(phase, watch.Elapsed));
        output.WriteLine($"{phase}: {summary} ({watch.Elapsed.TotalSeconds:0.0} s)");
        return value;
    }

    // Warnings in summary form: counts only, with old paths in the report files.
    private void WriteSummaryLines(MigrationPlan plan, IReadOnlyList<ItemResult> items, IReadOnlyList<MissingInSource> missing, IReadOnlyList<CountDrop> drops, LinkStats links, IReadOnlyList<PlanIssue> issues)
    {
        if (plan.Skipped.Count > 0)
        {
            output.WriteLine($"skipped: {plan.Skipped.Count} ({string.Join(", ", plan.Skipped.GroupBy(s => s.Category).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"))})");
        }

        var warnings = plan.Issues.Count(i => i.Severity == PlanSeverities.Warning);
        if (warnings > 0)
        {
            output.WriteLine($"plan warnings: {warnings}");
        }

        if (links.Rewritten + links.External + links.ToApplication + links.Unresolved.Count > 0)
        {
            output.WriteLine($"links: {links.Rewritten} rewritten, {links.External} external, {links.ToApplication} to the application, {links.Unresolved.Count} unresolved (listed in report.md)");
        }

        var notPublished = issues.Count(i => i.Message.Contains(DocumentSync.NotPublishedMarker, StringComparison.Ordinal));
        if (notPublished > 0)
        {
            output.WriteLine($"planned as published but not published in Umbraco, review: {notPublished} (listed in report.md)");
        }

        WriteEdited(items, ItemOutcome.EditedSkipped, "edited in Umbraco, not updated");
        WriteEdited(items, ItemOutcome.EditedOverwritten, "edited in Umbraco, overwritten");

        if (missing.Count > 0)
        {
            output.WriteLine($"missing in source, review manually: {missing.Count}");
        }

        foreach (var drop in drops)
        {
            output.WriteLine($"count dropped: {drop.Type} {drop.Before} -> {drop.After}");
        }
    }

    // The total, then the kinds it is made of: "2 (documents 1, templates 1)".
    private void WriteEdited(IReadOnlyList<ItemResult> items, ItemOutcome outcome, string label)
    {
        var edited = items.Where(r => r.Outcome == outcome).ToList();
        if (edited.Count == 0)
        {
            return;
        }

        var kinds = edited.GroupBy(r => KindLabel(r.Kind)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}");
        output.WriteLine($"{label}: {edited.Count} ({string.Join(", ", kinds)})");
    }

    private static string KindLabel(string kind) => kind switch
    {
        ItemKinds.Document => "documents",
        ItemKinds.Media => "media",
        ItemKinds.Redirect => "redirects",
        ItemKinds.Template => "templates",
        ItemKinds.DocumentType => "document types",
        ItemKinds.DataType => "data types",
        _ => kind,
    };

    // One line per distinct error with a count and one example (a type alias or old path, never a field value).
    private void WriteFailures(IReadOnlyList<ItemResult> items)
    {
        foreach (var group in items.Where(r => r.Outcome == ItemOutcome.Failed).GroupBy(f => (f.Kind, f.Error)))
        {
            var example = group.First();
            output.WriteLine($"failed {group.Key.Kind}: {group.Key.Error} ({group.Count()}x, e.g. {(example.OldPath.Length > 0 ? example.OldPath : example.TypeAlias)})");
        }
    }

    private static int Total(IReadOnlyList<RedirectSummary> summaries, Func<RedirectSummary, int> count) => summaries.Sum(count);

    private static string Summarize(IReadOnlyList<ItemResult> results) =>
        results.Count == 0
            ? "nothing to do"
            : string.Join(", ", results.GroupBy(r => r.Outcome).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));

    private static string Describe(TargetCounts counts) =>
        $"{(counts.DocumentsByType.Count == 0 ? "no documents" : string.Join(", ", counts.DocumentsByType.Select(p => $"{p.Key} {p.Value}")))}; media {counts.Media}; redirects {counts.Redirects}";

    private static bool SameCounts(SourceCounts a, SourceCounts b) =>
        a.Attachments == b.Attachments && a.MediaFiles == b.MediaFiles && a.Aliases == b.Aliases
        && a.DocumentsByClass.Count == b.DocumentsByClass.Count
        && a.DocumentsByClass.All(p => b.DocumentsByClass.TryGetValue(p.Key, out var n) && n == p.Value);
}
