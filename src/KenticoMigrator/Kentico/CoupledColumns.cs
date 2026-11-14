namespace KenticoMigrator.Kentico;

/// <summary>Matches the columns named in a form definition to the columns of the coupled table.</summary>
public static class CoupledColumns
{
    /// <summary>
    /// Returns, for each wanted column, the spelling used by the table (SQL Server names are case-insensitive,
    /// form definitions may differ in case). Throws <see cref="InvalidDataException"/> when the table has no
    /// columns (it does not exist) or a wanted column is missing.
    /// </summary>
    public static IReadOnlyList<string> Resolve(
        string pageType, string table, IEnumerable<string> wanted, IEnumerable<string> actual)
    {
        var byName = actual.ToDictionary(c => c, StringComparer.OrdinalIgnoreCase);
        if (byName.Count == 0)
        {
            throw new InvalidDataException($"Page type {pageType}: table {table} was not found or has no columns.");
        }

        return wanted
            .Select(column => byName.TryGetValue(column, out var name)
                ? name
                : throw new InvalidDataException(
                    $"Page type {pageType}: column '{column}' from the form definition is missing in table {table}."))
            .ToList();
    }
}
