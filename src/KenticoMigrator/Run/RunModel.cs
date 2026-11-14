using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Run;

/// <summary>Process exit codes (contracts/cli.md).</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int ItemErrors = 1;
    public const int ConfigurationOrPlanError = 2;
    public const int CountDrop = 3;
}

/// <summary>Counts of the target system, taken before and after a run.</summary>
public sealed record TargetCounts(
    IReadOnlyDictionary<string, int> DocumentsByType,
    int Media,
    int Redirects);

public sealed record CountDrop(string Type, int Before, int After);

/// <summary>Values for <see cref="PhaseTiming.Phase"/>.</summary>
public static class Phases
{
    public const string Read = "read";
    public const string Plan = "plan";
    public const string Snapshot = "snapshot";
    public const string Schema = "schema";
    public const string Media = "media";
    public const string Documents = "documents";
    public const string Redirects = "redirects";
    public const string Verify = "verify";
    public const string Report = "report";
}

public sealed record PhaseTiming(string Phase, TimeSpan Elapsed);

/// <summary>
/// The verification report (data-model section 6). Holds paths, keys, counts and timings only.
/// <paramref name="Items"/> are the per-item results; writers summarise them per kind and type.
/// <paramref name="PlannedRedirects"/> are the individual redirects written to <c>redirects.csv</c> only (not repeated in report.json).
/// </summary>
public sealed record RunReport(
    string RunId,
    string Command,
    DateTime StartedUtc,
    TimeSpan Duration,
    string KenticoVersion,
    SourceCounts? SourceCountsBefore,
    SourceCounts? SourceCountsAfter,
    TargetCounts? TargetCountsBefore,
    TargetCounts? TargetCountsAfter,
    IReadOnlyList<CountDrop> CountDrops,
    IReadOnlyList<ItemResult> Items,
    IReadOnlyList<SkippedItem> Skipped,
    IReadOnlyList<PlanIssue> Issues,
    LinkStats Links,
    IReadOnlyList<RedirectSummary> Redirects,
    IReadOnlyList<MissingInSource> MissingInSource,
    IReadOnlyList<ScheduleNote> Schedules,
    IReadOnlyList<PhaseTiming> Timings,
    int ExitCode,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<PlannedRedirect>? PlannedRedirects = null);

/// <summary>Rich text link totals plus the unresolved links with the page's old path.</summary>
public sealed record LinkStats(
    int Rewritten,
    int External,
    int ToApplication,
    IReadOnlyList<UnresolvedLink> Unresolved);

public sealed record UnresolvedLink(string OldPath, string Href);

/// <summary>Redirects registered, grouped by reason (<see cref="RedirectReasons"/>) and outcome.</summary>
public sealed record RedirectSummary(string Reason, int Created, int Existing, int Failed);

/// <summary>A publish window of a source page that the migration does not apply (FR-014a).</summary>
public sealed record ScheduleNote(Guid Key, string OldPath, string Note);
