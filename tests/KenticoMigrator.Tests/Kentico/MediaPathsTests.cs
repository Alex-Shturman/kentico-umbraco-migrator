using KenticoMigrator.Kentico;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Kentico;

public sealed class MediaPathsTests : IDisposable
{
    private readonly string _base = Directory.CreateTempSubdirectory("media-paths").FullName;
    private string Root => Path.Combine(_base, "media");

    public MediaPathsTests()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Gallery"));
        File.WriteAllText(Path.Combine(Root, "Gallery", "a.txt"), "x");
        File.WriteAllText(Path.Combine(_base, "secret.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    [Fact]
    public void Resolve_returns_the_file_below_the_root()
    {
        var path = MediaPaths.Resolve(Root, MediaFile(Guid.NewGuid(), "Gallery", "a.txt"));

        Assert.Equal(Path.Combine(Path.GetFullPath(Root), "Gallery", "a.txt"), path);
    }

    [Theory]
    [InlineData("../../secret.txt")]
    [InlineData("/etc/hosts")]
    public void Resolve_rejects_a_path_outside_the_root_or_rooted(string filePath)
    {
        Assert.Throws<InvalidDataException>(() => MediaPaths.Resolve(Root, MediaFile(Guid.NewGuid(), "Gallery", filePath)));
    }

    [Theory]
    [InlineData("a.txt", true)]
    [InlineData("missing.txt", false)]
    [InlineData("../../secret.txt", false)]
    [InlineData("/etc/hosts", false)]
    public void Exists_is_false_for_a_missing_or_unsafe_path(string filePath, bool expected)
    {
        Assert.Equal(expected, MediaPaths.Exists(Root, MediaFile(Guid.NewGuid(), "Gallery", filePath)));
    }
}
