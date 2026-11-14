using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace KenticoMigrator.Tests.Kentico;

/// <summary>Access to the restored sample database (scripts/restore-sample.sh) for integration tests.</summary>
internal static class SampleDatabase
{
    public const string ConnectionStringVariable = "KENTICO_SAMPLE_CS";

    public static string? ConnectionString { get; } =
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } cs ? cs : null;

    /// <summary>Connection string pointing at another database on the same server.</summary>
    public static string ForDatabase(string database) =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = database }.ConnectionString;

    /// <summary>Folder that holds the sample media files (sample/media).</summary>
    public static string MediaRoot { get; } = FindRepoPath("sample", "media");

    private static string FindRepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KenticoMigrator.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine([dir?.FullName ?? AppContext.BaseDirectory, .. parts]);
    }
}

/// <summary>A fact that is skipped unless <c>KENTICO_SAMPLE_CS</c> is set. Use together with the Integration trait.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (SampleDatabase.ConnectionString is null)
        {
            Skip = $"{SampleDatabase.ConnectionStringVariable} is not set";
        }
    }
}
