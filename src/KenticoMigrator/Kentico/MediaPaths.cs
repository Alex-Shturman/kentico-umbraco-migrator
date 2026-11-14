namespace KenticoMigrator.Kentico;

/// <summary>Where a media library file lives under the media root; the one place that guards against paths leaving it.</summary>
public static class MediaPaths
{
    /// <summary>
    /// The full path of <paramref name="file"/> below <paramref name="mediaRoot"/>. The library folder and path come from the
    /// database, so a rooted value (it would make <see cref="Path.Combine(string[])"/> drop the root) or one that leaves the root
    /// throws <see cref="InvalidDataException"/>.
    /// </summary>
    public static string Resolve(string mediaRoot, SourceMediaFile file)
    {
        if (IsRooted(file.LibraryFolder) || IsRooted(file.FilePath))
        {
            throw new InvalidDataException($"Media file path '{file.LibraryFolder}/{file.FilePath}' must be relative.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        var path = Path.GetFullPath(Path.Combine(root, file.LibraryFolder, file.FilePath));
        // A filesystem root ("/") keeps its separator after trimming, so do not add a second one.
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Media file path '{file.FilePath}' points outside the media root.");
        }

        return path;
    }

    /// <summary>True when the file is on disk; a path that is rooted or leaves the media root counts as missing.</summary>
    public static bool Exists(string mediaRoot, SourceMediaFile file)
    {
        try
        {
            return File.Exists(Resolve(mediaRoot, file));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool IsRooted(string path) =>
        Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\');
}
