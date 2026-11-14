namespace KenticoMigrator.Kentico;

/// <summary>The rule for "is this page published" from research R5a.</summary>
public static class PublishWindow
{
    /// <summary>
    /// A page is published only when it may be published and the current local time is inside its window:
    /// <paramref name="from"/> is null or not later than now, <paramref name="to"/> is null or later than now.
    /// Kentico stores publish times in local time.
    /// </summary>
    public static bool IsPublished(bool canBePublished, DateTime? from, DateTime? to, TimeProvider time)
    {
        var now = time.GetLocalNow().DateTime;
        return canBePublished && (from is null || from <= now) && (to is null || to > now);
    }
}
