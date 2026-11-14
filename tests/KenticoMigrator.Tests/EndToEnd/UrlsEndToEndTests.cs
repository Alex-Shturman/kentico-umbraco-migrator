using KenticoMigrator.Run;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.EndToEnd;

/// <summary>Same environment as <see cref="PagesEndToEndTests"/>. Runs the checks of <c>scripts/check-urls.sh</c> in C#.</summary>
[Trait("Category", "Integration")]
[Collection("EndToEnd")]
public sealed class UrlsEndToEndTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Expected_urls_all_ok()
    {
        using var host = EndToEndHost.Connect();
        var run = await host.RunAsync(CliArguments.Migrate, ct: Ct);
        Assert.True(run.ExitCode == ExitCodes.Success, run.Output);

        var lines = File.ReadAllLines(RepoPaths.Combine("sample", "expected-urls.txt"))
            .Where(l => l.Trim().Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        Assert.All(lines, l => Assert.Equal(3, l.Length));

        var failures = new List<string>();
        foreach (var line in lines)
        {
            var (url, expectedStatus, expectedPath) = (line[0], int.Parse(line[1]), line[2]);
            using var response = await host.Site.GetAsync(url, Ct);

            // Redirects are not followed: the final path of a redirect is the Location path, any other answer ends at the request.
            var finalPath = response.Headers.Location is { } location
                ? location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString
                : url;
            if ((int)response.StatusCode != expectedStatus || finalPath != expectedPath)
            {
                failures.Add($"{url}: expected {expectedStatus} {expectedPath}, got {(int)response.StatusCode} {finalPath}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Redirects_are_listed_in_the_back_office_and_not_duplicated_by_a_second_run()
    {
        using var host = EndToEndHost.Connect();
        Assert.Equal(ExitCodes.Success, (await host.RunAsync(CliArguments.Migrate, ct: Ct)).ExitCode);
        var total = await RedirectTotalAsync(host);
        Assert.True(total > 0);

        var second = await host.RunAsync(CliArguments.Migrate, ct: Ct);

        Assert.Equal(ExitCodes.Success, second.ExitCode);
        Assert.Equal(total, await RedirectTotalAsync(host));
        // The list shows the stored route (lower case, decoded) with a trailing slash.
        var list = await host.Api.GetAsync("/redirect-management?skip=0&take=100", Ct);
        var listed = list!["items"]!.AsArray().Select(i => i!["originalUrl"]!.GetValue<string>().TrimEnd('/')).ToList();
        Assert.Contains("/about.aspx", listed);
        Assert.Contains("/brand-search/sea spirit", listed);
    }

    private static async Task<int> RedirectTotalAsync(EndToEndHost host) =>
        new UmbracoTargetClient(host.Api) is { } client ? await client.CountRedirectsAsync(Ct) : 0;
}
