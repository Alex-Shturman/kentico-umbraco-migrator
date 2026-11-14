using System.Net;
using System.Text.Json.Nodes;
using KenticoMigrator.Migration;

namespace KenticoMigrator.Umbraco;

/// <summary>Options of one document sync.</summary>
public sealed record SyncOptions(bool OverwriteEdited = false, bool Preview = false);

/// <summary>
/// Upserts planned documents (FR-014, FR-015): parents before children, one GET per document by its key, POST when it is
/// missing, PUT when the planned values differ from what the tool wrote last, publish after a write when the plan says so,
/// and the children of each parent put in the planned order when the order differs. A failed item is a
/// <see cref="ItemOutcome.Failed"/> result; the run goes on. Nothing here deletes or unpublishes anything (FR-015b).
/// </summary>
public sealed class DocumentSync
{
    private static readonly Guid RootKey = Guid.Empty;

    // Stands for a parent that is not the planned one; it never equals a planned parent key, so the fingerprint differs.
    private static readonly Guid UnknownParent = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private readonly IUmbracoApi _api;
    private readonly Dictionary<string, TargetDocumentType> _types;
    private readonly Dictionary<string, Guid> _templateKeys;
    private readonly Dictionary<Guid, List<Guid>> _children = [];

    public DocumentSync(IUmbracoApi api, IReadOnlyList<TargetDocumentType> types, IReadOnlyList<TargetTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _types = types.ToDictionary(t => t.Alias, StringComparer.Ordinal);
        _templateKeys = templates.ToDictionary(t => t.Alias, t => t.Key, StringComparer.Ordinal);
    }

    /// <summary>Results come in processing order (parents first), one per planned document.</summary>
    public async Task<IReadOnlyList<ItemResult>> SyncAsync(IReadOnlyList<TargetDocument> documents, SyncOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(options);

        var results = new List<ItemResult>(documents.Count);
        var present = new HashSet<Guid>();
        foreach (var doc in ParentsFirst(documents))
        {
            try
            {
                var outcome = await SyncOneAsync(doc, options, ct).ConfigureAwait(false);
                results.Add(Result(doc, outcome));
                if (outcome is not ItemOutcome.WouldCreate)
                {
                    present.Add(doc.Key);
                }
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                results.Add(Result(doc, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }

        if (!options.Preview)
        {
            await SortAsync(documents, present, results, ct).ConfigureAwait(false);
        }

        return results;
    }

    private async Task<ItemOutcome> SyncOneAsync(TargetDocument doc, SyncOptions options, CancellationToken ct)
    {
        var owned = OwnedValues.FromPlanned(doc);
        var plannedFingerprint = Fingerprint.Compute(owned);
        var current = await _api.GetAsync(DocumentPath(doc.Key), ct).ConfigureAwait(false);

        if (current is null)
        {
            if (options.Preview)
            {
                return ItemOutcome.WouldCreate;
            }

            await CreateAsync(doc, owned, plannedFingerprint, ct).ConfigureAwait(false);
            return await PublishAsync(doc, ItemOutcome.Created, ct).ConfigureAwait(false);
        }

        // An editor deleted the page, which only moved it to the recycle bin and left its key taken. The key cannot be
        // created again, so the page is restored under its planned parent and written like a new one (quickstart step 7).
        if (current["isTrashed"]?.GetValue<bool>() == true)
        {
            if (options.Preview)
            {
                return ItemOutcome.WouldCreate;
            }

            await RestoreAsync(doc, ct).ConfigureAwait(false);
            await UpdateAsync(doc, owned, plannedFingerprint, ct).ConfigureAwait(false);
            return await PublishAsync(doc, ItemOutcome.Created, ct).ConfigureAwait(false);
        }

        // R5: an item whose current owned values no longer hash to what the tool stored was changed by an editor.
        var siblings = await ChildrenOfAsync(doc.ParentKey, ct).ConfigureAwait(false);
        var moved = !siblings.Contains(doc.Key);
        var storedFingerprint = StoredFingerprint(current);
        var currentFingerprint = Fingerprint.Compute(Fingerprint.FromCurrent(
            CurrentName(current),
            moved ? UnknownParent : doc.ParentKey,
            current["values"],
            [.. owned.Values.Keys]));

        if (storedFingerprint != currentFingerprint)
        {
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
                await MoveAsync(doc, siblings, ct).ConfigureAwait(false);
            }

            await UpdateAsync(doc, owned, plannedFingerprint, ct).ConfigureAwait(false);
            return await PublishAsync(doc, ItemOutcome.EditedOverwritten, ct).ConfigureAwait(false);
        }

        if (storedFingerprint == plannedFingerprint)
        {
            // A page that should be published but never was (an earlier publish failed) is published now. A page an editor
            // unpublished has a publish date and is left alone.
            if (doc.Publish && NeverPublished(current))
            {
                return options.Preview ? ItemOutcome.WouldUpdate : await PublishAsync(doc, ItemOutcome.Updated, ct).ConfigureAwait(false);
            }

            return ItemOutcome.Unchanged;
        }

        if (options.Preview)
        {
            return ItemOutcome.WouldUpdate;
        }

        await UpdateAsync(doc, owned, plannedFingerprint, ct).ConfigureAwait(false);
        return await PublishAsync(doc, ItemOutcome.Updated, ct).ConfigureAwait(false);
    }

    private async Task RestoreAsync(TargetDocument doc, CancellationToken ct)
    {
        var siblings = await ChildrenOfAsync(doc.ParentKey, ct).ConfigureAwait(false);
        var body = new RestoreRequest(doc.ParentKey is { } parent ? new IdRef(parent) : null);
        await _api.SendAsync(HttpMethod.Put, $"/recycle-bin/document/{doc.Key:D}/restore", body, ct).ConfigureAwait(false);
        siblings.Add(doc.Key);
    }

    // Puts a document that an editor moved back under its planned parent, at the end of that parent's children.
    private async Task MoveAsync(TargetDocument doc, List<Guid> plannedSiblings, CancellationToken ct)
    {
        var body = new MoveRequest(doc.ParentKey is { } parent ? new IdRef(parent) : null);
        await _api.SendAsync(HttpMethod.Put, $"{DocumentPath(doc.Key)}/move", body, ct).ConfigureAwait(false);
        foreach (var children in _children.Values)
        {
            children.Remove(doc.Key);
        }

        plannedSiblings.Add(doc.Key);
    }

    private async Task CreateAsync(TargetDocument doc, OwnedValues owned, string fingerprint, CancellationToken ct)
    {
        if (!_types.TryGetValue(doc.TypeAlias, out var type))
        {
            throw new MappingException(doc.TypeAlias, "Document type is not part of the plan.");
        }

        var body = new DocumentCreateRequest(
            doc.Key,
            doc.ParentKey is { } parent ? new IdRef(parent) : null,
            new IdRef(type.Key),
            TemplateOf(type),
            owned.ToApiValues(fingerprint),
            Variants(doc));
        // Read the siblings before the new document exists, then add it: the list is the tree order after the create.
        var siblings = await ChildrenOfAsync(doc.ParentKey, ct).ConfigureAwait(false);
        await _api.SendAsync(HttpMethod.Post, "/document", body, ct).ConfigureAwait(false);
        siblings.Add(doc.Key);
    }

    private async Task UpdateAsync(TargetDocument doc, OwnedValues owned, string fingerprint, CancellationToken ct)
    {
        var template = _types.TryGetValue(doc.TypeAlias, out var type) ? TemplateOf(type) : null;
        var body = new DocumentUpdateRequest(template, owned.ToApiValues(fingerprint), Variants(doc));
        await _api.SendAsync(HttpMethod.Put, DocumentPath(doc.Key), body, ct).ConfigureAwait(false);
    }

    private async Task<ItemOutcome> PublishAsync(TargetDocument doc, ItemOutcome written, CancellationToken ct)
    {
        if (doc.Publish)
        {
            await _api.SendAsync(HttpMethod.Put, $"{DocumentPath(doc.Key)}/publish", new PublishRequest([new PublishScheduleRequest(null)]), ct).ConfigureAwait(false);
        }

        return written;
    }

    // Puts the planned documents of each parent in the planned order. Documents the plan does not know keep their relative
    // order and go after the planned ones, so an editor's page is never lost between the planned pages.
    private async Task SortAsync(IReadOnlyList<TargetDocument> documents, HashSet<Guid> present, List<ItemResult> results, CancellationToken ct)
    {
        foreach (var group in documents.GroupBy(d => d.ParentKey ?? RootKey))
        {
            try
            {
                var parent = group.Key == RootKey ? (Guid?)null : group.Key;
                var children = await ChildrenOfAsync(parent, ct).ConfigureAwait(false);
                // Only documents that really sit under this parent: an item an editor moved elsewhere is left alone.
                var wanted = group.Select((d, index) => (d, index)).OrderBy(x => x.d.SortOrder).ThenBy(x => x.index)
                    .Select(x => x.d.Key).Where(key => present.Contains(key) && children.Contains(key)).ToList();
                if (children.Where(wanted.Contains).SequenceEqual(wanted))
                {
                    continue;
                }

                var order = wanted.Concat(children.Where(c => !wanted.Contains(c))).ToList();
                var body = new SortRequest(parent is { } p ? new IdRef(p) : null, [.. order.Select((key, index) => new SortItemRequest(key, index))]);
                await _api.SendAsync(HttpMethod.Put, "/document/sort", body, ct).ConfigureAwait(false);
                children.Clear();
                children.AddRange(order);
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                // The pages exist; only their order could not be set. Report it on the first document of the group.
                var first = group.First();
                results.Add(Result(first, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }
    }

    private async Task<List<Guid>> ChildrenOfAsync(Guid? parent, CancellationToken ct)
    {
        var key = parent ?? RootKey;
        if (!_children.TryGetValue(key, out var children))
        {
            var nodes = await UmbracoTree.ChildrenAsync(_api, UmbracoTree.DocumentKind, parent, ct).ConfigureAwait(false);
            _children[key] = children = [.. nodes.Select(n => n.Key)];
        }

        return children;
    }

    // Parents are written before their children whatever the order of the plan; the original order is kept inside a level.
    private static IEnumerable<TargetDocument> ParentsFirst(IReadOnlyList<TargetDocument> documents)
    {
        var byKey = documents.GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.First());
        int Depth(TargetDocument doc)
        {
            var depth = 0;
            for (var parent = doc.ParentKey; parent is { } key && byKey.TryGetValue(key, out var up) && depth <= documents.Count; parent = up.ParentKey)
            {
                depth++;
            }

            return depth;
        }

        return documents.OrderBy(Depth);
    }

    private static string? StoredFingerprint(JsonNode document) =>
        document["values"]?.AsArray()
            .FirstOrDefault(v => v?["alias"]?.GetValue<string>() == PlanAliases.Fingerprint)?["value"]?.GetValue<string>();

    private static bool NeverPublished(JsonNode document) =>
        document["variants"]?.AsArray().FirstOrDefault()?["publishDate"] is null;

    private static string CurrentName(JsonNode document) =>
        document["variants"]?.AsArray().FirstOrDefault()?["name"]?.GetValue<string>() ?? string.Empty;

    private IdRef? TemplateOf(TargetDocumentType type) =>
        type.TemplateAlias is { } alias && _templateKeys.TryGetValue(alias, out var key) ? new IdRef(key) : null;

    private static List<VariantRequest> Variants(TargetDocument doc) => [new VariantRequest(null, null, doc.Name)];

    private static string DocumentPath(Guid key) => $"/document/{key:D}";

    private static ItemResult Result(TargetDocument doc, ItemOutcome outcome, string? error = null) =>
        new(doc.Key, ItemKinds.Document, doc.TypeAlias, doc.OldPath, outcome, error);
}
