using System.Net;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class MediaSyncTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SyncOptions Options = new();

    private static readonly Guid RootFolder = GuidFor(100);
    private static readonly Guid SubFolder = GuidFor(101);
    private static readonly Guid ImageKey = GuidFor(102);
    private static readonly Guid FileKey = GuidFor(103);

    // A file and an image, listed before their folders on purpose: the sync must still create the folders first.
    private static IReadOnlyList<TargetMedia> Media(string imageName = "photo.jpg") =>
    [
        new(ImageKey, SubFolder, MediaType.Image, imageName, new MediaSource(ImageKey, null, imageName, "image/jpeg"), "/Pages/photo.jpg"),
        new(FileKey, SubFolder, MediaType.File, "guide.pdf", new MediaSource(null, MediaFile(FileKey, "Gallery", "guide.pdf", ".pdf"), "guide.pdf", "application/pdf"), "Gallery/guide.pdf"),
        new(SubFolder, RootFolder, MediaType.Folder, "Pages", null, "Kentico import/Pages"),
        new(RootFolder, null, MediaType.Folder, "Kentico import", null, "Kentico import"),
    ];

    private static FakeKenticoReader Reader() => new(Site());

    [Fact]
    public async Task Creates_folders_before_files()
    {
        var api = new FakeUmbracoApi();

        var results = await new MediaSync(api).SyncAsync(Media(), Reader(), Options, Ct);

        var posted = api.Writes.Where(c => c.Method == HttpMethod.Post && c.Path == "/media")
            .Select(c => c.Body!["variants"]![0]!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(["Kentico import", "Pages", "photo.jpg", "guide.pdf"], posted);
        Assert.All(results, r => Assert.Equal(ItemOutcome.Created, r.Outcome));
        Assert.Equal(["Folder", "Folder", "Image", "File"], results.Select(r => r.TypeAlias).ToList());
        Assert.All(results, r => Assert.Equal(ItemKinds.Media, r.Kind));
    }

    [Fact]
    public async Task Uploads_temporary_file_then_creates_media()
    {
        var api = new FakeUmbracoApi();

        await new MediaSync(api).SyncAsync(Media(), Reader(), Options, Ct);

        var calls = api.Writes.Where(c => c.Path is "/temporary-file" or "/media").ToList();
        var index = calls.FindIndex(c => c.Path == "/temporary-file");
        Assert.Equal("/media", calls[index + 1].Path);
        var upload = calls[index].Body!;
        Assert.Equal("photo.jpg", upload["fileName"]!.GetValue<string>());
        Assert.Equal("image/jpeg", upload["mimeType"]!.GetValue<string>());
        Assert.Equal(3, upload["length"]!.GetValue<long>());

        var body = api.Stored("media", ImageKey)!;
        Assert.Equal(SubFolder.ToString("D"), body["parent"]!["id"]!.GetValue<string>());
        Assert.Equal(FakeUmbracoApi.MediaTypeKey("Image").ToString("D"), body["mediaType"]!["id"]!.GetValue<string>());
        var file = body["values"]!.AsArray().Single()!;
        Assert.Equal("umbracoFile", file["alias"]!.GetValue<string>());
        Assert.Equal(upload["id"]!.GetValue<string>(), file["value"]!["temporaryFileId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unchanged_media_is_not_uploaded_again()
    {
        var api = new FakeUmbracoApi();
        var sync = new MediaSync(api);
        await sync.SyncAsync(Media(), Reader(), Options, Ct);
        var reader = Reader();
        api.Calls.Clear();

        var results = await new MediaSync(api).SyncAsync(Media(), reader, Options, Ct);

        Assert.All(results, r => Assert.Equal(ItemOutcome.Unchanged, r.Outcome));
        Assert.Empty(api.Writes);
        Assert.Empty(reader.Opened);
    }

    [Fact]
    public async Task Urls_of_written_and_unchanged_files_are_kept_as_site_relative_paths()
    {
        var api = new FakeUmbracoApi();
        var first = new MediaSync(api);
        await first.SyncAsync(Media(), Reader(), Options, Ct);
        var second = new MediaSync(api);
        await second.SyncAsync(Media(), Reader(), Options, Ct);

        foreach (var urls in new[] { first.Urls, second.Urls })
        {
            Assert.Equal(2, urls.Count);
            Assert.EndsWith("/photo.jpg", urls[ImageKey], StringComparison.Ordinal);
            Assert.StartsWith("/media/", urls[ImageKey], StringComparison.Ordinal);
            Assert.False(urls.ContainsKey(SubFolder));
        }
    }

    [Fact]
    public async Task A_media_item_an_editor_renamed_is_left_alone_unless_overwrite_is_given()
    {
        var api = new FakeUmbracoApi();
        await new MediaSync(api).SyncAsync(Media(), Reader(), Options, Ct);
        api.Stored("media", ImageKey)!["variants"]![0]!["name"] = "Renamed by editor";
        api.Calls.Clear();

        var skipped = await new MediaSync(api).SyncAsync(Media(), Reader(), Options, Ct);

        Assert.Equal(ItemOutcome.EditedSkipped, skipped.Single(r => r.Key == ImageKey).Outcome);
        Assert.Empty(api.Writes);

        var overwritten = await new MediaSync(api).SyncAsync(Media(), Reader(), new SyncOptions(OverwriteEdited: true), Ct);

        Assert.Equal(ItemOutcome.EditedOverwritten, overwritten.Single(r => r.Key == ImageKey).Outcome);
        Assert.Equal("photo.jpg", api.Stored("media", ImageKey)!["variants"]![0]!["name"]!.GetValue<string>());
        Assert.Contains(api.Writes, c => c.Method == HttpMethod.Put && c.Path == $"/media/{ImageKey:D}");
        Assert.DoesNotContain(api.Writes, c => c.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task A_media_item_an_editor_moved_is_edited_and_overwrite_moves_it_back()
    {
        var api = new FakeUmbracoApi();
        await new MediaSync(api).SyncAsync(Media(), Reader(), Options, Ct);
        api.Calls.Clear();
        await api.SendAsync(HttpMethod.Put, $"/media/{FileKey:D}/move", new { target = new { id = RootFolder } }, Ct);

        var skipped = await new MediaSync(api).SyncAsync(Media(), Reader(), Options, Ct);
        Assert.Equal(ItemOutcome.EditedSkipped, skipped.Single(r => r.Key == FileKey).Outcome);

        var overwritten = await new MediaSync(api).SyncAsync(Media(), Reader(), new SyncOptions(OverwriteEdited: true), Ct);
        Assert.Equal(ItemOutcome.EditedOverwritten, overwritten.Single(r => r.Key == FileKey).Outcome);
        Assert.Contains(api.Writes, c => c.Path == $"/media/{FileKey:D}/move");
    }

    [Fact]
    public async Task Preview_writes_and_uploads_nothing()
    {
        var api = new FakeUmbracoApi();

        var results = await new MediaSync(api).SyncAsync(Media(), Reader(), new SyncOptions(Preview: true), Ct);

        Assert.All(results, r => Assert.Equal(ItemOutcome.WouldCreate, r.Outcome));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task A_failed_item_is_recorded_and_the_run_goes_on()
    {
        var api = new FakeUmbracoApi
        {
            Failure = c => c.Method == HttpMethod.Post && c.Path == "/media" && c.Body?["id"]?.GetValue<string>() == ImageKey.ToString("D")
                ? FakeUmbracoApi.Problem(HttpStatusCode.BadRequest, "Validation failed")
                : null,
        };

        var results = await new MediaSync(api).SyncAsync(Media(), Reader(), Options, Ct);

        var failed = results.Single(r => r.Outcome == ItemOutcome.Failed);
        Assert.Equal(ImageKey, failed.Key);
        Assert.Equal("HTTP 400 Validation failed", failed.Error);
        Assert.Equal(ItemOutcome.Created, results.Single(r => r.Key == FileKey).Outcome);
    }

    [Fact]
    public async Task A_file_missing_on_disk_fails_that_item_only()
    {
        var api = new FakeUmbracoApi();
        var reader = Reader();
        reader.MissingFiles.Add(ImageKey);

        var results = await new MediaSync(api).SyncAsync(Media(), reader, Options, Ct);

        Assert.Equal("FileNotFoundException", results.Single(r => r.Key == ImageKey).Error);
        Assert.Equal(ItemOutcome.Failed, results.Single(r => r.Key == ImageKey).Outcome);
        Assert.Equal(ItemOutcome.Created, results.Single(r => r.Key == FileKey).Outcome);
        Assert.Null(api.Stored("media", ImageKey));
    }
}
