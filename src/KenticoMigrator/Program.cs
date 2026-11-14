using System.Reflection;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Run;
using KenticoMigrator.Umbraco;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

if (!CliArguments.TryParse(args, out var cli, out var parseError))
{
    Console.Error.WriteLine(parseError);
    Console.Error.WriteLine("Usage: KenticoMigrator <counts|plan|migrate> --profile <path> [--overwrite-edited] [--run-dir <path>] [--Section:Key=Value ...]");
    return ExitCodes.ConfigurationOrPlanError;
}

try
{
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables("KM_")
        .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
        .AddCommandLine([.. cli!.ConfigArguments])
        .Build();

    var profile = SiteProfile.Load(cli.Profile);

    var connectionString = config["Kentico:ConnectionString"];
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("Kentico:ConnectionString is not configured (user secret, KM_Kentico__ConnectionString or --Kentico:ConnectionString=...).");
        return ExitCodes.ConfigurationOrPlanError;
    }

    var mediaRoot = config["Kentico:MediaRoot"] is { Length: > 0 } configured ? configured : profile.MediaRoot;
    var reader = new Kentico11SqlReader(connectionString, mediaRoot);

    ManagementApiClient? client = null;
    try
    {
        if (!string.IsNullOrWhiteSpace(config["Umbraco:BaseUrl"]) && !string.IsNullOrWhiteSpace(config["Umbraco:ClientSecret"]))
        {
            client = ManagementApiClient.Create(new UmbracoOptions(
                config["Umbraco:BaseUrl"]!,
                config["Umbraco:ClientId"] is { Length: > 0 } id ? id : "umbraco-back-office-migrator",
                config["Umbraco:ClientSecret"]!,
                bool.TryParse(config["Umbraco:AllowUntrustedCertificate"], out var untrusted) && untrusted));
        }

        var runDir = cli.RunDir ?? Path.Combine("runs", DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Func<SourceMediaFile, bool>? mediaExists = string.IsNullOrWhiteSpace(mediaRoot)
            ? null
            : file => File.Exists(Path.Combine(mediaRoot, file.LibraryFolder, file.FilePath));

        var runner = new MigrationRunner(reader, client is null ? null : new UmbracoApi(client), Console.Out);
        var exit = await runner.RunAsync(new RunRequest(cli.Command, profile, runDir, cli.OverwriteEdited, mediaExists), cancellation.Token);
        Console.WriteLine($"Exit code {exit}");
        return exit;
    }
    finally
    {
        client?.Dispose();
    }
}
catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or SqlException or UmbracoAuthException or UmbracoApiException)
{
    // Configuration, connection or version problem: nothing was written before this point (contracts/cli.md).
    Console.Error.WriteLine($"Error: {(ex is UmbracoApiException or UmbracoAuthException ? ApiErrors.Describe(ex) : ex.Message)}");
    return ExitCodes.ConfigurationOrPlanError;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return ExitCodes.ConfigurationOrPlanError;
}
