using System.Text.Json.Nodes;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;

namespace KenticoMigrator.Umbraco;

/// <summary>
/// Upserts planned media (FR-013): folders before files, parents before children, one GET per item by its key, a temporary
/// file upload and POST when it is missing, and the same outcome rules as documents (research R5). The built-in
/// <c>Folder</c>, <c>Image</c> and <c>File</c> media types are never changed, so an item carries no stored fingerprint: the
/// tool wrote exactly its planned name, folder and file, so an item whose current name and folder hash to the planned
/// fingerprint is <see cref="ItemOutcome.Unchanged"/> and any other state is an edit (<see cref="ItemOutcome.EditedSkipped"/>, or
/// <see cref="ItemOutcome.EditedOverwritten"/> with <see cref="SyncOptions.OverwriteEdited"/>). The file bytes are not compared,
/// and a media item is never deleted (FR-015b).
/// </summary>
public sealed class MediaSync(IUmbracoApi api)
{
    private const string FileAlias = "umbracoFile";
    private const int UrlBatchSize = 50;

    private static readonly Guid RootKey = Guid.Empty;

    // Stands for a folder that is not the planned one; it never equals a planned parent key, so the fingerprint differs.
    private static readonly Guid UnknownParent = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private readonly Dictionary<MediaType, Guid> _typeKeys = [];
    private readonly Dictionary<Guid, List<Guid>> _children = [];
    private readonly Dictionary<Guid, string> _urls = [];

    /// <summary>
    /// The site-relative URL (<c>/media/&lt;token&gt;/&lt;file&gt;</c>) of every file that exists in Umbraco after
    /// <see cref="SyncAsync"/>; folders and failed items are not in it. The rich text rewriter uses it for image <c>src</c>.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> Urls => _urls;

    /// <summary>
    /// Results come in processing order, one per planned item. Pass <paramref name="results"/> to keep what was done when the
    /// run aborts, as <see cref="DocumentSync.SyncAsync"/> does.
    /// </summary>
    public async Task<IReadOnlyList<ItemResult>> SyncAsync(IReadOnlyList<TargetMedia> media, IKenticoReader reader, SyncOptions options, CancellationToken ct, List<ItemResult>? results = null)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);

        results ??= new List<ItemResult>(media.Count);
        var firstOfThisRun = results.Count;
        foreach (var item in FoldersFirst(media))
        {
            try
            {
                results.Add(Result(item, await SyncOneAsync(item, reader, options, ct).ConfigureAwait(false)));
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                results.Add(Result(item, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }

        var outcomes = results.Skip(firstOfThisRun).ToDictionary(r => r.Key, r => r.Outcome);
        await ReadUrlsAsync(
            [.. media.Where(m => m.MediaType != MediaType.Folder && outcomes.TryGetValue(m.Key, out var o) && o is not (ItemOutcome.Failed or ItemOutcome.WouldCreate)).Select(m => m.Key)],
            ct).ConfigureAwait(false);
        return results;
    }

    private async Task<ItemOutcome> SyncOneAsync(TargetMedia item, IKenticoReader reader, SyncOptions options, CancellationToken ct)
    {
        var current = await api.GetAsync(MediaPath(item.Key), ct).ConfigureAwait(false);
        if (current is null)
        {
            if (options.Preview)
            {
                return ItemOutcome.WouldCreate;
            }

            await CreateAsync(item, reader, ct).ConfigureAwait(false);
            return ItemOutcome.Created;
        }

        // An editor deleted the item, which only moved it to the recycle bin and left its key taken: restore it, like a page.
        if (current["isTrashed"]?.GetValue<bool>() == true)
        {
            if (options.Preview)
            {
                return ItemOutcome.WouldCreate;
            }

            await RestoreAsync(item, ct).ConfigureAwait(false);
            return ItemOutcome.Created;
        }

        var siblings = await ChildrenOfAsync(item.ParentKey, ct).ConfigureAwait(false);
        var moved = !siblings.Contains(item.Key);
        var actualParent = moved ? await ActualParentAsync(item.Key, ct).ConfigureAwait(false) : item.ParentKey;
        var currentFingerprint = Fingerprint.Compute(OwnedValues.ForMedia(CurrentName(current), actualParent, item.MediaType.ToString()));
        if (currentFingerprint == Fingerprint.Compute(item))
        {
            return ItemOutcome.Unchanged;
        }

        if (!options.OverwriteEdited)
        {
            return ItemOutcome.EditedSkipped;
        }

        if (options.Preview)
        {
            return ItemOutcome.WouldUpdate;
        }

        if (moved)
        {
            await MoveAsync(item, siblings, ct).ConfigureAwait(false);
        }

        await UpdateAsync(item, reader, ct).ConfigureAwait(false);
        return ItemOutcome.EditedOverwritten;
    }

    private async Task CreateAsync(TargetMedia item, IKenticoReader reader, CancellationToken ct)
    {
        var typeKey = await TypeKeyAsync(item.MediaType, ct).ConfigureAwait(false);
        var values = await FileValuesAsync(item, reader, ct).ConfigureAwait(false);
        var body = new MediaCreateRequest(
            item.Key,
            item.ParentKey is { } parent ? new IdRef(parent) : null,
            new IdRef(typeKey),
            values,
            [new VariantRequest(null, null, item.Name)]);
        // Read the siblings before the new item exists, then add it: the list is the tree content after the create.
        var siblings = await ChildrenOfAsync(item.ParentKey, ct).ConfigureAwait(false);
        await api.SendAsync(HttpMethod.Post, "/media", body, ct).ConfigureAwait(false);
        siblings.Add(item.Key);
    }

    private async Task UpdateAsync(TargetMedia item, IKenticoReader reader, CancellationToken ct)
    {
        var values = await FileValuesAsync(item, reader, ct).ConfigureAwait(false);
        await api.SendAsync(HttpMethod.Put, MediaPath(item.Key), new MediaUpdateRequest(values, [new VariantRequest(null, null, item.Name)]), ct).ConfigureAwait(false);
    }

    // The file goes up as a temporary file; the media item then takes it (research R9). A folder has no file.
    private async Task<JsonArray> FileValuesAsync(TargetMedia item, IKenticoReader reader, CancellationToken ct)
    {
        if (item.Source is not { } source)
        {
            return [];
        }

        var temporaryId = Guid.NewGuid();
        var stream = source.AttachmentGuid is { } attachment
            ? await reader.OpenAttachmentAsync(attachment, ct).ConfigureAwait(false)
            : await reader.OpenMediaFileAsync(source.MediaFile!, ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await api.UploadTemporaryFileAsync(temporaryId, source.FileName, source.MimeType, stream, ct).ConfigureAwait(false);
        }

        return
        [
            new JsonObject
            {
                ["alias"] = FileAlias,
                ["culture"] = null,
                ["segment"] = null,
                ["value"] = new JsonObject { ["temporaryFileId"] = temporaryId.ToString("D") },
            },
        ];
    }

    private async Task RestoreAsync(TargetMedia item, CancellationToken ct)
    {
        var siblings = await ChildrenOfAsync(item.ParentKey, ct).ConfigureAwait(false);
        var body = new RestoreRequest(item.ParentKey is { } parent ? new IdRef(parent) : null);
        await api.SendAsync(HttpMethod.Put, $"/recycle-bin/media/{item.Key:D}/restore", body, ct).ConfigureAwait(false);
        siblings.Add(item.Key);
    }

    private async Task MoveAsync(TargetMedia item, List<Guid> plannedSiblings, CancellationToken ct)
    {
        var body = new MoveRequest(item.ParentKey is { } parent ? new IdRef(parent) : null);
        await api.SendAsync(HttpMethod.Put, $"{MediaPath(item.Key)}/move", body, ct).ConfigureAwait(false);
        foreach (var children in _children.Values)
        {
            children.Remove(item.Key);
        }

        plannedSiblings.Add(item.Key);
    }

    // The folder an item really has (null: the media root), from the ancestors endpoint, whose last entry is the item itself.
    private async Task<Guid?> ActualParentAsync(Guid key, CancellationToken ct)
    {
        var path = await api.GetAsync($"/tree/media/ancestors?descendantId={key:D}", ct).ConfigureAwait(false);
        var self = path?.AsArray().FirstOrDefault(n => n?["id"]?.GetValue<string>() is { } id && Guid.Parse(id) == key);
        if (self is null)
        {
            return UnknownParent;
        }

        return self["parent"]?["id"]?.GetValue<string>() is { } parent ? Guid.Parse(parent) : null;
    }

    private async Task<Guid> TypeKeyAsync(MediaType type, CancellationToken ct)
    {
        if (!_typeKeys.TryGetValue(type, out var key))
        {
            var name = type.ToString();
            var found = await api.GetAsync($"/item/media-type/search?query={Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
            var items = found?["items"]?.AsArray() ?? found?.AsArray() ?? [];
            var match = items.FirstOrDefault(i => string.Equals(i?["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
            _typeKeys[type] = key = match?["id"]?.GetValue<string>() is { } id
                ? Guid.Parse(id)
                : throw new MappingException(null, "Media type is not part of the target.");
        }

        return key;
    }

    private async Task ReadUrlsAsync(IReadOnlyList<Guid> keys, CancellationToken ct)
    {
        for (var skip = 0; skip < keys.Count; skip += UrlBatchSize)
        {
            var ids = string.Join('&', keys.Skip(skip).Take(UrlBatchSize).Select(k => $"id={k:D}"));
            var answer = await api.GetAsync($"/media/urls?{ids}", ct).ConfigureAwait(false);
            foreach (var entry in answer?.AsArray() ?? [])
            {
                var url = entry?["urlInfos"]?.AsArray().Select(i => i?["url"]?.GetValue<string>()).FirstOrDefault(u => !string.IsNullOrEmpty(u));
                if (url is not null && entry?["id"]?.GetValue<string>() is { } id)
                {
                    _urls[Guid.Parse(id)] = SiteRelative(url);
                }
            }
        }
    }

    // The URL as the page should carry it: no host, so the content does not depend on where the site is served from.
    private static string SiteRelative(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute.PathAndQuery : url;

    private async Task<List<Guid>> ChildrenOfAsync(Guid? parent, CancellationToken ct)
    {
        var key = parent ?? RootKey;
        if (!_children.TryGetValue(key, out var children))
        {
            var nodes = await UmbracoTree.ChildrenAsync(api, UmbracoTree.MediaKind, parent, ct).ConfigureAwait(false);
            _children[key] = children = [.. nodes.Select(n => n.Key)];
        }

        return children;
    }

    // Folders before files, and a folder after the folder that holds it; the plan order is kept otherwise.
    private static IEnumerable<TargetMedia> FoldersFirst(IReadOnlyList<TargetMedia> media)
    {
        var byKey = media.GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.First());
        int Depth(TargetMedia item)
        {
            var depth = 0;
            for (var parent = item.ParentKey; parent is { } key && byKey.TryGetValue(key, out var up) && depth <= media.Count; parent = up.ParentKey)
            {
                depth++;
            }

            return depth;
        }

        return media.Where(m => m.MediaType == MediaType.Folder).OrderBy(Depth)
            .Concat(media.Where(m => m.MediaType != MediaType.Folder));
    }

    private static string CurrentName(JsonNode item) =>
        item["variants"]?.AsArray().FirstOrDefault()?["name"]?.GetValue<string>() ?? string.Empty;

    private static string MediaPath(Guid key) => $"/media/{key:D}";

    private static ItemResult Result(TargetMedia item, ItemOutcome outcome, string? error = null) =>
        new(item.Key, ItemKinds.Media, item.MediaType.ToString(), item.SourcePath, outcome, error);
}
