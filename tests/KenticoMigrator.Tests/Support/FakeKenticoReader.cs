using KenticoMigrator.Kentico;

namespace KenticoMigrator.Tests.Support;

/// <summary>Source reader over an in-memory site; <see cref="CountAsync"/> answers from a script, the last entry repeating.</summary>
internal sealed class FakeKenticoReader(SourceSite site, params SourceCounts[] countScript) : IKenticoReader
{
    public int CountCalls { get; private set; }

    /// <summary>Throws from the n-th (0-based) count call when set.</summary>
    public int? FailOnCountCall { get; set; }

    public Task<SourceSite> ReadAsync(IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct) => Task.FromResult(site);

    /// <summary>File guids whose file is gone from disk (the open throws like the real reader).</summary>
    public HashSet<Guid> MissingFiles { get; } = [];

    /// <summary>Guids of the files opened, in order.</summary>
    public List<Guid> Opened { get; } = [];

    public Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct) => Open(attachmentGuid);

    public Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct) => Open(file.FileGuid);

    private Task<Stream> Open(Guid guid)
    {
        Opened.Add(guid);
        return MissingFiles.Contains(guid)
            ? throw new FileNotFoundException("gone")
            : Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    }

    public Task<SourceCounts> CountAsync(CancellationToken ct)
    {
        if (FailOnCountCall == CountCalls)
        {
            throw new InvalidOperationException("The source went away.");
        }

        var counts = countScript.Length == 0 ? Of(site) : countScript[Math.Min(CountCalls, countScript.Length - 1)];
        CountCalls++;
        return Task.FromResult(counts);
    }

    public static SourceCounts Of(SourceSite site, int attachments = 0) =>
        new(site.Documents.GroupBy(d => d.ClassName).ToDictionary(g => g.Key, g => g.Count()), attachments, site.MediaFiles.Count, site.Aliases.Count);
}
