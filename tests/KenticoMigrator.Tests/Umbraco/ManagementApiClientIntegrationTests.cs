using System.Text.Json;
using KenticoMigrator.Umbraco;
using Microsoft.Extensions.Configuration;

namespace KenticoMigrator.Tests.Umbraco;

/// <summary>
/// Needs the Umbraco host running (<c>dotnet run --project host/UmbracoSite</c>) and
/// <c>Umbraco:ClientSecret</c> in the migrator's user secrets or the environment
/// (<c>Umbraco__ClientSecret</c>). Skipped when the secret is not configured.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ManagementApiClientIntegrationTests
{
    // Same UserSecretsId as src/KenticoMigrator/KenticoMigrator.csproj.
    private const string MigratorUserSecretsId = "kentico-migrator-6f1b6d0e";

    [Fact]
    public async Task Can_get_token_and_read_root()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(MigratorUserSecretsId)
            .AddEnvironmentVariables()
            .Build();
        var secret = config["Umbraco:ClientSecret"];
        Assert.SkipWhen(string.IsNullOrEmpty(secret), "Umbraco:ClientSecret is not configured.");

        var client = ManagementApiClient.Create(new UmbracoOptions(
            config["Umbraco:BaseUrl"] ?? UmbracoOptions.DefaultBaseUrl,
            config["Umbraco:ClientId"] ?? UmbracoOptions.DefaultClientId,
            secret!,
            AllowUntrustedCertificate: true));

        var root = await client.GetAsync<JsonElement>("/tree/document/root?skip=0&take=1", TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("total", out _));
    }
}
