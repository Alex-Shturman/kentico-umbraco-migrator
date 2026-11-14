using KenticoMigrator.Kentico;

namespace KenticoMigrator.Tests.Kentico;

public sealed class PublishWindowTests
{
    private sealed class FixedTimeProvider(DateTime localNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(localNow, DateTimeKind.Utc));
    }

    private static readonly TimeProvider Now = new FixedTimeProvider(new DateTime(2024, 6, 15, 12, 0, 0));

    [Fact]
    public void Publishable_without_window_is_published() =>
        Assert.True(PublishWindow.IsPublished(true, null, null, Now));

    [Fact]
    public void Not_publishable_is_never_published() =>
        Assert.False(PublishWindow.IsPublished(false, null, null, Now));

    [Fact]
    public void Window_that_has_not_opened_is_not_published() =>
        Assert.False(PublishWindow.IsPublished(true, new DateTime(2099, 1, 1), null, Now));

    [Fact]
    public void Window_that_has_opened_is_published() =>
        Assert.True(PublishWindow.IsPublished(true, new DateTime(2024, 6, 15, 12, 0, 0), null, Now));

    [Fact]
    public void Window_that_has_closed_is_not_published() =>
        Assert.False(PublishWindow.IsPublished(true, null, new DateTime(2024, 6, 15, 12, 0, 0), Now));

    [Fact]
    public void Inside_open_and_closed_window_is_published() =>
        Assert.True(PublishWindow.IsPublished(true, new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), Now));
}

public sealed class Kentico11SqlReaderMediaPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kentico-media-" + Guid.NewGuid().ToString("N"), "media");

    public Kentico11SqlReaderMediaPathTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Gallery"));
        File.WriteAllText(Path.Combine(_root, "Gallery", "a.txt"), "hello");
        Directory.CreateDirectory(_root + "-evil");
        File.WriteAllText(Path.Combine(_root + "-evil", "secret.txt"), "secret");
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);

    private static SourceMediaFile File_(string folder, string path) =>
        new(Guid.NewGuid(), "Gallery", folder, path, "a.txt", "txt", "text/plain", 5, null);

    private static Kentico11SqlReader Reader(string root) =>
        new("Server=db.example;Database=x;User Id=u;Password=p", root);

    [Fact]
    public async Task Opens_a_file_under_the_root()
    {
        await using var stream = await Reader(_root).OpenMediaFileAsync(File_("Gallery", "a.txt"), CancellationToken.None);

        Assert.Equal(5, stream.Length);
    }

    [Fact]
    public async Task Root_with_trailing_separator_still_works()
    {
        var reader = Reader(_root + Path.DirectorySeparatorChar);

        await using var stream = await reader.OpenMediaFileAsync(File_("Gallery", "a.txt"), CancellationToken.None);

        Assert.Equal(5, stream.Length);
    }

    [Fact]
    public async Task Rooted_file_path_is_rejected()
    {
        var rooted = Path.Combine(_root, "Gallery", "a.txt");

        await Assert.ThrowsAsync<InvalidDataException>(() => Reader(_root).OpenMediaFileAsync(File_("Gallery", rooted), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => Reader(_root).OpenMediaFileAsync(File_("Gallery", "/a.txt"), CancellationToken.None));
    }

    [Fact]
    public async Task Rooted_library_folder_is_rejected()
    {
        var rooted = Path.Combine(_root, "Gallery");

        await Assert.ThrowsAsync<InvalidDataException>(() => Reader(_root).OpenMediaFileAsync(File_(rooted, "a.txt"), CancellationToken.None));
    }

    [Fact]
    public async Task Parent_traversal_is_rejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Reader(_root).OpenMediaFileAsync(File_("Gallery", "../../x.txt"), CancellationToken.None));
    }

    [Fact]
    public async Task Sibling_folder_with_the_same_prefix_is_rejected()
    {
        // <root>-evil starts with the root path as a string but is not inside it.
        await Assert.ThrowsAsync<InvalidDataException>(() => Reader(_root).OpenMediaFileAsync(File_("../media-evil", "secret.txt"), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_file_is_file_not_found()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => Reader(_root).OpenMediaFileAsync(File_("Gallery", "none.txt"), CancellationToken.None));
    }
}
