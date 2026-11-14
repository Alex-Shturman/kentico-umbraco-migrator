using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KenticoMigrator.Kentico;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Run;

/// <summary>
/// Writes the run folder (contracts/cli.md): <c>report.json</c>, <c>report.md</c>, <c>redirects.csv</c>,
/// <c>skipped.csv</c> and <c>items.csv</c>. The writers only read keys, paths, counts, outcomes and timings from
/// <see cref="RunReport"/>; field values never reach a file (FR-028).
/// </summary>
public static class Report
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Longest item error kept in a report.</summary>
    internal const int MaxErrorLength = 200;

    public static void Write(RunReport report, string runDir)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDir);

        report = report with { Items = [.. report.Items.Select(i => i with { Error = SanitizeError(i.Error) })] };

        Directory.CreateDirectory(runDir);
        WriteFile(runDir, "report.json", JsonSerializer.Serialize(report, JsonOptions) + "\n");
        WriteFile(runDir, "report.md", Markdown(report));
        WriteFile(runDir, "redirects.csv", Csv(
            ["old_url", "new_url", "target_key", "reason"],
            (report.PlannedRedirects ?? []).Select(r => new[] { r.OldUrl, r.NewUrl, r.TargetKey.ToString(), r.Reason })));
        WriteFile(runDir, "skipped.csv", Csv(
            ["category", "identifier", "old_path", "reason"],
            report.Skipped.Select(s => new[] { s.Category, s.Identifier, s.OldPath, s.Reason })));
        WriteFile(runDir, "items.csv", Csv(
            ["kind", "type", "key", "old_path", "outcome", "error"],
            report.Items.Select(i => new[] { i.Kind, i.TypeAlias, i.Key.ToString(), i.OldPath, i.Outcome.ToString(), i.Error ?? string.Empty })));
    }

    /// <summary>
    /// Keeps only the first line up to the start of any payload (<c>{</c>, <c>[</c> or a double or single quote), so an error such as
    /// <c>HTTP 400 Validation failed {...}</c> keeps its status and title but not the response body, which can echo
    /// content values (FR-028). Capped at <see cref="MaxErrorLength"/>.
    /// </summary>
    internal static string? SanitizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var firstLine = error.AsSpan();
        var end = firstLine.IndexOfAny("\r\n{[\"'");
        var kept = (end >= 0 ? firstLine[..end] : firstLine).Trim().ToString();
        return kept.Length > MaxErrorLength ? kept[..MaxErrorLength] : kept;
    }

    private static void WriteFile(string dir, string name, string content) =>
        File.WriteAllText(Path.Combine(dir, name), content, Utf8NoBom);

    /// <summary>RFC 4180 quoting: fields with a comma, quote or line break are quoted and quotes doubled.</summary>
    private static string Csv(string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', header)).Append('\n');
        foreach (var row in rows)
        {
            sb.Append(string.Join(',', row.Select(Escape))).Append('\n');
        }

        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    private static string Markdown(RunReport r)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        sb.Append("# Migration report ").Append(r.RunId).Append("\n\n");
        sb.Append("- Command: ").Append(r.Command).Append('\n');
        sb.Append("- Started (UTC): ").Append(r.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append('\n');
        sb.Append("- Duration: ").Append(r.Duration.ToString("c", inv)).Append('\n');
        sb.Append("- Kentico version: ").Append(r.KenticoVersion).Append('\n');
        sb.Append("- Exit code: ").Append(r.ExitCode.ToString(inv)).Append("\n\n");

        AppendSourceCounts(sb, r);
        AppendTargetCounts(sb, r);

        sb.Append("## Count drops\n\n");
        if (r.CountDrops.Count == 0)
        {
            sb.Append("None.\n\n");
        }
        else
        {
            Table(sb, ["Type", "Before", "After"], r.CountDrops.Select(d => new[] { d.Type, N(d.Before), N(d.After) }));
        }

        sb.Append("## Items\n\n");
        Table(sb, ["Kind", "Type", "Outcome", "Count"], r.Items
            .GroupBy(i => (i.Kind, i.TypeAlias, i.Outcome))
            .OrderBy(g => g.Key.Kind, StringComparer.Ordinal)
            .ThenBy(g => g.Key.TypeAlias, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Outcome.ToString(), StringComparer.Ordinal)
            .Select(g => new[] { g.Key.Kind, g.Key.TypeAlias, g.Key.Outcome.ToString(), N(g.Count()) }));

        sb.Append("## Skipped by category\n\n");
        Table(sb, ["Category", "Count"], r.Skipped
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new[] { g.Key, N(g.Count()) }));
        sb.Append("Every skipped item is listed in skipped.csv.\n\n");

        sb.Append("## Issues\n\n");
        List(sb, r.Issues.Select(i => $"{i.Severity}: {i.OldPath}: {i.Message}"));

        sb.Append("## Links\n\n");
        sb.Append("- Rewritten: ").Append(N(r.Links.Rewritten)).Append('\n');
        sb.Append("- External: ").Append(N(r.Links.External)).Append('\n');
        sb.Append("- Pointing to the application: ").Append(N(r.Links.ToApplication)).Append('\n');
        sb.Append("- Unresolved: ").Append(N(r.Links.Unresolved.Count)).Append("\n\n");
        List(sb, r.Links.Unresolved.Select(u => $"{u.OldPath} links to {u.Href}"));

        sb.Append("## Redirects\n\n");
        Table(sb, ["Reason", "Created", "Existing", "Failed"], r.Redirects.Select(x => new[] { x.Reason, N(x.Created), N(x.Existing), N(x.Failed) }));

        sb.Append("## Missing in source, review manually\n\n");
        List(sb, r.MissingInSource.Select(m => $"{m.Kind} {m.Name} (key {m.Key}, source key {m.SourceKey})"));

        sb.Append("## Edited in Umbraco\n\n");
        List(sb, r.Items
            .Where(i => i.Outcome is ItemOutcome.EditedSkipped or ItemOutcome.EditedOverwritten)
            .Select(i => $"{i.Outcome}: {i.Kind} {i.TypeAlias} {i.OldPath} (key {i.Key})"));

        sb.Append("## Publish schedules not applied\n\n");
        List(sb, r.Schedules.Select(s => $"{s.OldPath} (key {s.Key}): {s.Note}"));

        sb.Append("## Timings\n\n");
        Table(sb, ["Phase", "Elapsed"], r.Timings.Select(t => new[] { t.Phase, t.Elapsed.ToString("c", inv) }));

        return sb.ToString();
    }

    private static void AppendSourceCounts(StringBuilder sb, RunReport r)
    {
        sb.Append("## Source counts\n\n");
        if (r.SourceCountsBefore is null)
        {
            sb.Append("Source counts: not taken.\n\n");
            return;
        }

        var before = r.SourceCountsBefore;
        var after = r.SourceCountsAfter;
        var rows = before.DocumentsByClass.Keys.Union(after?.DocumentsByClass.Keys ?? [])
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new[] { k, N(before.DocumentsByClass.GetValueOrDefault(k)), After(after?.DocumentsByClass.GetValueOrDefault(k)) })
            .Append(["attachments", N(before.Attachments), After(after?.Attachments)])
            .Append(["media files", N(before.MediaFiles), After(after?.MediaFiles)])
            .Append(["aliases", N(before.Aliases), After(after?.Aliases)]);
        Table(sb, ["Item", "Before", "After"], rows);
    }

    private static void AppendTargetCounts(StringBuilder sb, RunReport r)
    {
        sb.Append("## Target counts\n\n");
        if (r.TargetCountsBefore is null)
        {
            sb.Append("Target counts: not taken.\n\n");
            return;
        }

        var before = r.TargetCountsBefore;
        var after = r.TargetCountsAfter;
        var rows = before.DocumentsByType.Keys.Union(after?.DocumentsByType.Keys ?? [])
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new[] { k, N(before.DocumentsByType.GetValueOrDefault(k)), After(after?.DocumentsByType.GetValueOrDefault(k)) })
            .Append(["media", N(before.Media), After(after?.Media)])
            .Append(["redirects", N(before.Redirects), After(after?.Redirects)]);
        Table(sb, ["Type", "Before", "After"], rows);
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string After(int? value) => value is null ? "-" : N(value.Value);

    private static void Table(StringBuilder sb, string[] header, IEnumerable<string[]> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            sb.Append("None.\n\n");
            return;
        }

        sb.Append("| ").Append(string.Join(" | ", header)).Append(" |\n");
        sb.Append("|").Append(string.Concat(header.Select(_ => "---|"))).Append('\n');
        foreach (var row in list)
        {
            sb.Append("| ").Append(string.Join(" | ", row.Select(c => c.Replace("|", "\\|", StringComparison.Ordinal)))).Append(" |\n");
        }

        sb.Append('\n');
    }

    private static void List(StringBuilder sb, IEnumerable<string> lines)
    {
        var any = false;
        foreach (var line in lines)
        {
            sb.Append("- ").Append(line).Append('\n');
            any = true;
        }

        sb.Append(any ? "\n" : "None.\n\n");
    }
}
