using System.Text.Json.Nodes;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Run;
using KenticoMigrator.Tests.Kentico;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;
using Microsoft.Extensions.Configuration;

namespace KenticoMigrator.Tests.EndToEnd;

/// <summary>The outcome of one migrator run against the live host.</summary>
internal sealed record EndToEndRun(int ExitCode, string Output, string RunDir);

/// <summary>
/// Everything an end-to-end test needs: the sample database (<c>KENTICO_SAMPLE_CS</c>), the running Umbraco host and the
/// migrator's <c>Umbraco:ClientSecret</c> (user secrets or <c>Umbraco__ClientSecret</c>). A test is skipped when one is missing.
/// </summary>
internal sealed class EndToEndHost : IDisposable
{
    // Same UserSecretsId as src/KenticoMigrator/KenticoMigrator.csproj.
    private const string MigratorUserSecretsId = "kentico-migrator-6f1b6d0e";

    private readonly ManagementApiClient _client;

    private EndToEndHost(ManagementApiClient client)
    {
        _client = client;
        Api = new UmbracoApi(client);
    }

    public IUmbracoApi Api { get; }

    public static EndToEndHost Connect()
    {
        Assert.SkipWhen(SampleDatabase.ConnectionString is null, $"{SampleDatabase.ConnectionStringVariable} is not set.");
        var config = new ConfigurationBuilder().AddUserSecrets(MigratorUserSecretsId).AddEnvironmentVariables().Build();
        var secret = config["Umbraco:ClientSecret"];
        Assert.SkipWhen(string.IsNullOrEmpty(secret), "Umbraco:ClientSecret is not configured.");

        return new EndToEndHost(ManagementApiClient.Create(new UmbracoOptions(
            config["Umbraco:BaseUrl"] is { Length: > 0 } url ? url : "https://localhost:44317",
            config["Umbraco:ClientId"] is { Length: > 0 } id ? id : "umbraco-back-office-migrator",
            secret!,
            AllowUntrustedCertificate: true)));
    }

    /// <summary>Runs a command with the sample profile and a fresh run folder.</summary>
    public async Task<EndToEndRun> RunAsync(string command, bool overwriteEdited = false, CancellationToken ct = default, Func<IKenticoReader, IKenticoReader>? wrapReader = null)
    {
        var profile = SourceFactory.SampleProfile();
        var mediaRoot = SampleDatabase.MediaRoot;
        IKenticoReader reader = new Kentico11SqlReader(SampleDatabase.ConnectionString!, mediaRoot);
        if (wrapReader is not null)
        {
            reader = wrapReader(reader);
        }

        var output = new StringWriter();
        var runDir = Path.Combine(Path.GetTempPath(), "km-e2e-" + Guid.NewGuid().ToString("N"));

        var runner = new MigrationRunner(reader, Api, output);
        var exit = await runner.RunAsync(
            new RunRequest(command, profile, runDir, overwriteEdited, file => File.Exists(Path.Combine(mediaRoot, file.LibraryFolder, file.FilePath))),
            ct);
        return new EndToEndRun(exit, output.ToString(), runDir);
    }

    /// <summary>The per-item results of a run, read from its <c>report.json</c>.</summary>
    public static IReadOnlyList<(string Kind, string Type, string OldPath, string Outcome)> Items(EndToEndRun run)
    {
        using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(run.RunDir, "report.json")));
        return [.. report.RootElement.GetProperty("items").EnumerateArray().Select(i => (
            i.GetProperty("kind").GetString()!,
            i.GetProperty("typeAlias").GetString()!,
            i.GetProperty("oldPath").GetString()!,
            i.GetProperty("outcome").GetString()!))];
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>A migrated document as read back through the API.</summary>
internal sealed record TargetPage(Guid Key, Guid? ParentKey, string Name, string TypeAlias, string State, IReadOnlyDictionary<string, JsonNode?> Values)
{
    public string Text(string alias) => Values.TryGetValue(alias, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
}

/// <summary>The whole document tree of the target, read through the API.</summary>
internal sealed class TargetSnapshot
{
    private readonly List<TargetPage> _pages;

    private TargetSnapshot(List<TargetPage> pages) => _pages = pages;

    public IReadOnlyList<TargetPage> Pages => _pages;

    public static async Task<TargetSnapshot> ReadAsync(IUmbracoApi api, CancellationToken ct)
    {
        var typeAliases = new Dictionary<Guid, string>();
        var pages = new List<TargetPage>();
        var pending = new Stack<Guid?>([null]);
        while (pending.Count > 0)
        {
            var parent = pending.Pop();
            foreach (var node in await UmbracoTree.ChildrenAsync(api, UmbracoTree.DocumentKind, parent, ct))
            {
                if (!typeAliases.TryGetValue(node.TypeKey, out var alias))
                {
                    var type = await api.GetAsync($"/document-type/{node.TypeKey:D}", ct);
                    typeAliases[node.TypeKey] = alias = type!["alias"]!.GetValue<string>();
                }

                var doc = await api.GetAsync($"/document/{node.Key:D}", ct);
                var values = doc!["values"]!.AsArray().ToDictionary(v => v!["alias"]!.GetValue<string>(), v => v!["value"]);
                pages.Add(new TargetPage(node.Key, parent, node.Name, alias, doc["variants"]![0]!["state"]!.GetValue<string>(), values));
                if (node.HasChildren)
                {
                    pending.Push(node.Key);
                }
            }
        }

        return new TargetSnapshot(pages);
    }

    /// <summary>Finds a page by the names on its path, for example <c>"Home", "Catalog", "Brands"</c>.</summary>
    public TargetPage Find(params string[] path)
    {
        TargetPage? current = null;
        foreach (var name in path)
        {
            current = _pages.Single(p => p.ParentKey == current?.Key && p.Name == name);
        }

        return current!;
    }
}
