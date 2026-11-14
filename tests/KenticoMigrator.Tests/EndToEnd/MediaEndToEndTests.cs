using System.Text.Json.Nodes;
using KenticoMigrator.Kentico;
using KenticoMigrator.Run;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.EndToEnd;

/// <summary>Same environment as <see cref="PagesEndToEndTests"/>, plus Azurite for the media container.</summary>
[Trait("Category", "Integration")]
[Collection("EndToEnd")]
public sealed class MediaEndToEndTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The names the sample gives the files of each gallery, in the order the source defines (research R11, sample/README.md).
    private static readonly string[] PacificGallery = ["pacific-front.jpg", "pacific-side.png"];
    private static readonly string[] RiverGallery = ["river-side.jpg", "river-front.jpg"];

    [Fact]
    public async Task Sample_media_exist_and_are_served_from_blob()
    {
        using var host = EndToEndHost.Connect();
        var run = await host.RunAsync(CliArguments.Migrate, ct: Ct);
        Assert.True(run.ExitCode == ExitCodes.Success, run.Output);

        var files = await FilesAsync(host);
        var blobs = await AzuriteBlobNamesAsync();

        var expected = EndToEndHost.Items(run).Count(i => i.Kind == ItemKinds.Media && i.Type != "Folder");
        Assert.True(expected > 0);
        Assert.Equal(expected, files.Count);
        foreach (var file in files)
        {
            var urls = await host.Api.GetAsync($"/media/urls?id={file.Key:D}", Ct);
            var url = urls!.AsArray().Single()!["urlInfos"]!.AsArray().Single()!["url"]!.GetValue<string>();
            var path = new Uri(url).AbsolutePath;

            // The media URL is the host's; the file itself must be the blob "media/<token>/<file name>" in the Azurite container.
            Assert.Contains(path.TrimStart('/'), blobs);
            using var response = await host.Site.GetAsync(path, Ct);
            Assert.True(response.IsSuccessStatusCode, $"{file.Name}: {(int)response.StatusCode}");
            Assert.True((await response.Content.ReadAsByteArrayAsync(Ct)).Length > 0, file.Name);
        }

        Assert.Contains(files, f => f.Name == "kayak-blue.jpg");
        Assert.Contains(files, f => f.Name == "paddle-size-guide.pdf");
    }

    [Fact]
    public async Task Product_gallery_has_images_in_order()
    {
        using var host = EndToEndHost.Connect();
        Assert.Equal(ExitCodes.Success, (await host.RunAsync(CliArguments.Migrate, ct: Ct)).ExitCode);
        var tree = await TargetSnapshot.ReadAsync(host.Api, Ct);

        Assert.Equal(PacificGallery, await GalleryAsync(host, tree.Find("Home", "Catalog", "Products", "Pacific Touring Kayak")));
        Assert.Equal(RiverGallery, await GalleryAsync(host, tree.Find("Home", "Catalog", "Products", "River Runner Kayak")));
    }

    [Fact]
    public async Task Second_run_uploads_nothing()
    {
        using var host = EndToEndHost.Connect();
        Assert.Equal(ExitCodes.Success, (await host.RunAsync(CliArguments.Migrate, ct: Ct)).ExitCode);
        var before = await FilesAsync(host);
        CountingReader? counting = null;

        var second = await host.RunAsync(CliArguments.Migrate, ct: Ct, wrapReader: reader => counting = new CountingReader(reader));

        Assert.True(second.ExitCode == ExitCodes.Success, second.Output);
        Assert.Equal(0, counting!.Opened);
        var media = EndToEndHost.Items(second).Where(i => i.Kind == ItemKinds.Media).ToList();
        Assert.NotEmpty(media);
        Assert.All(media, i => Assert.Equal("Unchanged", i.Outcome));
        Assert.Equal(before.Select(f => f.Key).Order(), (await FilesAsync(host)).Select(f => f.Key).Order());
    }

    // The well-known Azurite development account (documented by Microsoft, not a secret). Anonymous container listing is
    // refused, so the list request is signed with the shared key.
    private const string AzuriteAccount = "devstoreaccount1";
    private const string AzuriteKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private const string AzuriteVersion = "2021-08-06";

    private static async Task<HashSet<string>> AzuriteBlobNamesAsync()
    {
        var date = DateTime.UtcNow.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        // GET, then the eleven standard headers (all empty), the x-ms headers, and the resource (the emulator repeats the account).
        var toSign = $"GET\n\n\n\n\n\n\n\n\n\n\n\nx-ms-date:{date}\nx-ms-version:{AzuriteVersion}\n/{AzuriteAccount}/{AzuriteAccount}/media\ncomp:list\nrestype:container";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Convert.FromBase64String(AzuriteKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(toSign)));

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:10000/{AzuriteAccount}/media?restype=container&comp=list");
        request.Headers.Add("x-ms-date", date);
        request.Headers.Add("x-ms-version", AzuriteVersion);
        request.Headers.TryAddWithoutValidation("Authorization", $"SharedKey {AzuriteAccount}:{signature}");
        using var response = await http.SendAsync(request, Ct);
        var xml = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"Azurite container listing: {(int)response.StatusCode}");
        return [.. System.Xml.Linq.XDocument.Parse(xml).Descendants("Name").Select(n => n.Value)];
    }

    private static async Task<List<TreeNode>> FilesAsync(EndToEndHost host)
    {
        var leaves = new List<TreeNode>();
        var pending = new Stack<Guid?>([null]);
        while (pending.Count > 0)
        {
            foreach (var node in await UmbracoTree.ChildrenAsync(host.Api, UmbracoTree.MediaKind, pending.Pop(), Ct))
            {
                if (node.HasChildren)
                {
                    pending.Push(node.Key);
                }
                else
                {
                    leaves.Add(node);
                }
            }
        }

        // A folder without children is a leaf too: keep only the nodes that hold a file.
        var files = new List<TreeNode>();
        foreach (var node in leaves)
        {
            var item = await host.Api.GetAsync($"/media/{node.Key:D}", Ct);
            if (item!["values"]!.AsArray().Any(v => v!["alias"]!.GetValue<string>() == "umbracoFile"))
            {
                files.Add(node);
            }
        }

        return files;
    }

    // The media file names a page's picker points at, in picker order (the picker with the most items is the gallery).
    private static async Task<string[]> GalleryAsync(EndToEndHost host, TargetPage page)
    {
        var picker = page.Values.Values.OfType<JsonArray>()
            .Where(a => a.Count > 0 && a[0] is JsonObject o && o.ContainsKey("mediaKey"))
            .MaxBy(a => a.Count)!;
        var names = new List<string>();
        foreach (var entry in picker)
        {
            var media = await host.Api.GetAsync($"/media/{entry!["mediaKey"]!.GetValue<string>()}", Ct);
            names.Add(media!["variants"]![0]!["name"]!.GetValue<string>());
        }

        return [.. names];
    }

    private sealed class CountingReader(IKenticoReader inner) : IKenticoReader
    {
        public int Opened { get; private set; }

        public Task<SourceSite> ReadAsync(IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct) => inner.ReadAsync(unlinkedRowClasses, ct);

        public Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct)
        {
            Opened++;
            return inner.OpenAttachmentAsync(attachmentGuid, ct);
        }

        public Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct)
        {
            Opened++;
            return inner.OpenMediaFileAsync(file, ct);
        }

        public Task<SourceCounts> CountAsync(CancellationToken ct) => inner.CountAsync(ct);
    }
}
