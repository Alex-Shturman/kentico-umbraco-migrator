using AngleSharp.Html.Parser;
using KenticoMigrator.Run;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.EndToEnd;

/// <summary>Same environment as <see cref="PagesEndToEndTests"/>.</summary>
[Trait("Category", "Integration")]
[Collection("EndToEnd")]
public sealed class LinksEndToEndTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rendered_story_page_links_point_to_new_urls()
    {
        using var host = EndToEndHost.Connect();
        var run = await host.RunAsync(CliArguments.Migrate, ct: Ct);
        Assert.True(run.ExitCode == ExitCodes.Success, run.Output);
        var tree = await TargetSnapshot.ReadAsync(host.Api, Ct);

        using var response = await host.Site.GetAsync("/story", Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var page = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync(Ct));
        var hrefs = page.QuerySelectorAll("a[href]").Select(a => a.GetAttribute("href")!).ToList();

        // About is linked twice (by alias path and by its .aspx form); the own-host link to the product and the short alias
        // /visit (the contact page) and the brand route with a space in the name all end at the pages' actual URLs.
        var about = await UrlOfAsync(host, tree.Find("Home", "About").Key);
        var contact = await UrlOfAsync(host, tree.Find("Home", "Contact").Key);
        var product = await UrlOfAsync(host, tree.Find("Home", "Catalog", "Products", "Pacific Touring Kayak").Key);
        var brand = await UrlOfAsync(host, tree.Find("Home", "Catalog", "Brands", "Sea Spirit").Key);
        Assert.Equal(2, hrefs.Count(h => h == about));
        Assert.Contains(contact, hrefs);
        Assert.Contains(product, hrefs);
        Assert.Contains(brand, hrefs);

        // Links that are not pages stay as they were: another site, mail and an anchor in the same page.
        Assert.Contains("https://weather.example.org/lakes", hrefs);
        Assert.Contains("mailto:info@kayaks.example", hrefs);
        Assert.Contains("#rental", hrefs);

        // The media link and the attachment image point at the uploaded files, which the host serves from blob storage.
        var mediaLink = Assert.Single(hrefs, h => h.StartsWith("/media/", StringComparison.Ordinal));
        Assert.EndsWith("/kayak-blue.jpg", mediaLink, StringComparison.Ordinal);
        var image = Assert.Single(page.QuerySelectorAll("img[src]"));
        var src = image.GetAttribute("src")!;
        Assert.StartsWith("/media/", src, StringComparison.Ordinal);
        Assert.EndsWith("/pacific-front.jpg", src, StringComparison.Ordinal);
        foreach (var path in new[] { mediaLink, src })
        {
            using var file = await host.Site.GetAsync(path, Ct);
            Assert.Equal(System.Net.HttpStatusCode.OK, file.StatusCode);
            Assert.StartsWith("image/", file.Content.Headers.ContentType!.MediaType, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Report_lists_one_unresolved_link()
    {
        using var host = EndToEndHost.Connect();
        var run = await host.RunAsync(CliArguments.Migrate, ct: Ct);
        Assert.True(run.ExitCode == ExitCodes.Success, run.Output);

        using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(run.RunDir, "report.json")));
        var links = report.RootElement.GetProperty("links");
        var unresolved = Assert.Single(links.GetProperty("unresolved").EnumerateArray());
        Assert.Equal("/Articles/Winter-Storage", unresolved.GetProperty("oldPath").GetString());
        Assert.Equal("/Seasonal/Deals.aspx", unresolved.GetProperty("href").GetString());
        Assert.Equal(1, links.GetProperty("external").GetInt32()); // the weather link; mail and anchors are not counted
        Assert.Equal(1, links.GetProperty("toApplication").GetInt32());
        Assert.Contains("/Articles/Winter-Storage links to /Seasonal/Deals.aspx", File.ReadAllText(Path.Combine(run.RunDir, "report.md")));
    }

    private static async Task<string> UrlOfAsync(EndToEndHost host, Guid key)
    {
        var urls = await host.Api.GetAsync($"/document/urls?id={key:D}", Ct);
        return urls!.AsArray().Single()!["urlInfos"]!.AsArray().First()!["url"]!.GetValue<string>();
    }
}
