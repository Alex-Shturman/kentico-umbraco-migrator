using KenticoMigrator.Kentico;

namespace KenticoMigrator.Migration;

/// <summary>
/// Turns a <see cref="SourceSite"/> and a <see cref="SiteProfile"/> into a deterministic
/// <see cref="MigrationPlan"/>: the same input always gives the same plan with the same keys.
/// Pure: no database, no HTTP, no clock.
/// </summary>
public sealed partial class PlanBuilder
{
    private readonly SourceSite _site;
    private readonly SiteProfile _profile;
    private readonly Func<SourceMediaFile, bool>? _mediaFileExists;
    private readonly List<SkippedItem> _skipped = [];
    private readonly List<PlanIssue> _issues = [];

    private PlanBuilder(SourceSite site, SiteProfile profile, Func<SourceMediaFile, bool>? mediaFileExists)
    {
        _site = site;
        _profile = profile;
        _mediaFileExists = mediaFileExists;
    }

    /// <summary>
    /// Builds the plan. <paramref name="mediaFileExists"/> lets the caller report media library files that are
    /// missing on disk (category <see cref="SkipCategories.MissingFile"/>); when null every library file is planned.
    /// </summary>
    public static MigrationPlan Build(SourceSite site, SiteProfile profile, Func<SourceMediaFile, bool>? mediaFileExists = null)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(profile);
        return new PlanBuilder(site, profile, mediaFileExists).Run();
    }

    private MigrationPlan Run()
    {
        var schema = BuildSchema();
        return new MigrationPlan(
            schema.DataTypes,
            schema.DocumentTypes,
            schema.Templates,
            [],
            [],
            _skipped,
            _issues);
    }

    private void Error(string oldPath, string message) => _issues.Add(new PlanIssue(PlanSeverities.Error, oldPath, message));

    private void Warning(string oldPath, string message) => _issues.Add(new PlanIssue(PlanSeverities.Warning, oldPath, message));
}
