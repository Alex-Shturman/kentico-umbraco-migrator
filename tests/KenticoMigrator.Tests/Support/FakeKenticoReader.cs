using KenticoMigrator.Kentico;

namespace KenticoMigrator.Tests.Support;

/// <summary>Source reader over an in-memory site; <see cref="CountAsync"/> answers from a script, the last entry repeating.</summary>
internal sealed class FakeKenticoReader(SourceSite site, params SourceCounts[] countScript) : IKenticoReader
{
    public int CountCalls { get; private set; }

    public Task<SourceSite> ReadAsync(IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct) => Task.FromResult(site);

    public Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct) => throw new NotSupportedException();

    public Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct) => throw new NotSupportedException();

    public Task<SourceCounts> CountAsync(CancellationToken ct)
    {
        var counts = countScript.Length == 0 ? Of(site) : countScript[Math.Min(CountCalls, countScript.Length - 1)];
        CountCalls++;
        return Task.FromResult(counts);
    }

    public static SourceCounts Of(SourceSite site, int attachments = 0) =>
        new(site.Documents.GroupBy(d => d.ClassName).ToDictionary(g => g.Key, g => g.Count()), attachments, site.MediaFiles.Count, site.Aliases.Count);
}
