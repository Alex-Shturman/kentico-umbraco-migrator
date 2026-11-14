using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.Support;

/// <summary>One request the fake received.</summary>
internal sealed record Call(HttpMethod Method, string Path, JsonNode? Body)
{
    public bool IsWrite => Method != HttpMethod.Get;
}

/// <summary>
/// In-memory stand-in for the Umbraco Management API: stores what is POSTed and PUT per collection, answers GETs
/// from the store, lists document children, and records every call. A <see cref="Failure"/> hook makes chosen
/// calls fail like the real API does.
/// </summary>
internal sealed class FakeUmbracoApi : IUmbracoApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, Dictionary<Guid, JsonObject>> _store = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Guid?> _parents = [];
    private readonly Dictionary<Guid, int> _order = [];
    private int _nextOrder;

    public List<Call> Calls { get; } = [];

    public int RedirectTotal { get; set; }

    /// <summary>Built-in data types by name (served by the filter endpoint).</summary>
    public Dictionary<string, Guid> BuiltInDataTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>Returns the exception to throw for a call, or null to let it through.</summary>
    public Func<Call, Exception?>? Failure { get; set; }

    public IEnumerable<Call> Writes => Calls.Where(c => c.IsWrite);

    public IReadOnlyList<Guid> PublishedKeys => [.. Writes
        .Where(c => c.Path.EndsWith("/publish", StringComparison.Ordinal))
        .Select(c => Guid.Parse(c.Path.Split('/')[2]))];

    public JsonObject? Stored(string collection, Guid id) =>
        _store.TryGetValue(collection, out var items) && items.TryGetValue(id, out var item) ? item : null;

    /// <summary>Order of the document keys under <paramref name="parent"/> as the tree lists them.</summary>
    public IReadOnlyList<Guid> ChildrenOf(Guid? parent) =>
        [.. _parents.Where(p => p.Value == parent).Select(p => p.Key).OrderBy(k => _order[k])];

    /// <summary>Puts a document straight into the store (an item that already exists in the target).</summary>
    public void Seed(string collection, Guid id, JsonObject item, Guid? parent = null)
    {
        item["id"] = id.ToString("D");
        Items(collection)[id] = item;
        if (item["isTrashed"]?.GetValue<bool>() != true)
        {
            _parents[id] = parent;
            _order[id] = _nextOrder++;
        }
    }

    /// <summary>Takes a document out of the tree listing (it stays readable by key, like an item in the recycle bin).</summary>
    public void Remove(Guid id)
    {
        _parents.Remove(id);
        _order.Remove(id);
    }

    public Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        var call = new Call(HttpMethod.Get, path, null);
        Calls.Add(call);
        if (Failure?.Invoke(call) is { } failure)
        {
            throw failure;
        }

        var (route, query) = Split(path);
        var parts = route.Trim('/').Split('/');
        if (route == "/filter/data-type")
        {
            var name = query.GetValueOrDefault("name");
            var items = new JsonArray();
            if (name is not null && BuiltInDataTypes.TryGetValue(name, out var id))
            {
                items.Add(new JsonObject { ["id"] = id.ToString("D"), ["name"] = name });
            }

            return Task.FromResult<JsonNode?>(new JsonObject { ["total"] = items.Count, ["items"] = items });
        }

        if (route == "/redirect-management")
        {
            return Task.FromResult<JsonNode?>(new JsonObject { ["total"] = RedirectTotal, ["items"] = new JsonArray() });
        }

        if (route == "/tree/document/ancestors")
        {
            // Like the real endpoint: the path from the root down to the item itself; each entry names its parent.
            var descendant = Guid.Parse(query["descendantId"]);
            if (Stored("document", descendant) is null || !_parents.TryGetValue(descendant, out var parentOfKey))
            {
                return Task.FromResult<JsonNode?>(new JsonArray());
            }

            return Task.FromResult<JsonNode?>(new JsonArray(new JsonObject
            {
                ["id"] = descendant.ToString("D"),
                ["parent"] = parentOfKey is null ? null : new JsonObject { ["id"] = parentOfKey.Value.ToString("D") },
            }));
        }

        if (parts is ["tree", var kind, "root" or "children"])
        {
            Guid? parent = query.TryGetValue("parentId", out var p) ? Guid.Parse(p) : null;
            return Task.FromResult<JsonNode?>(TreePage(kind, parent, query));
        }

        if (parts.Length == 2 && Guid.TryParse(parts[1], out var key))
        {
            var item = Stored(parts[0], key)?.DeepClone().AsObject();
            item?.Remove("parent"); // the real read model of a document has no parent
            return Task.FromResult<JsonNode?>(item);
        }

        throw new InvalidOperationException($"Fake: unhandled GET {path}");
    }

    public Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var node = body is null ? null : JsonSerializer.SerializeToNode(body, Json);
        var call = new Call(method, path, node?.DeepClone());
        Calls.Add(call);
        if (Failure?.Invoke(call) is { } failure)
        {
            throw failure;
        }

        var parts = path.Trim('/').Split('/');
        if (method == HttpMethod.Post && parts.Length == 1)
        {
            var item = (JsonObject)node!;
            var id = Guid.Parse(item["id"]!.GetValue<string>());
            Items(parts[0])[id] = (JsonObject)item.DeepClone();
            if (parts[0] == "document")
            {
                _parents[id] = item["parent"]?["id"]?.GetValue<string>() is { } p ? Guid.Parse(p) : null;
                _order[id] = _nextOrder++;
            }

            return Ok(HttpStatusCode.Created);
        }

        if (method == HttpMethod.Put && parts.Length == 2 && parts[1] == "sort")
        {
            var sorting = ((JsonObject)node!)["sorting"]!.AsArray();
            foreach (var entry in sorting)
            {
                _order[Guid.Parse(entry!["id"]!.GetValue<string>())] = entry["sortOrder"]!.GetValue<int>();
            }

            return Ok(HttpStatusCode.OK);
        }

        if (method == HttpMethod.Put && parts.Length == 2 && Guid.TryParse(parts[1], out var putId))
        {
            var stored = Stored(parts[0], putId) ?? throw new InvalidOperationException($"Fake: PUT of missing item {path}");
            var publishDate = stored["variants"]?[0]?["publishDate"]?.DeepClone();
            foreach (var (name, value) in (JsonObject)node!)
            {
                stored[name] = value?.DeepClone();
            }

            // Like the real API: an update keeps the publish date; a published page then has pending changes.
            if (publishDate is not null && stored["variants"]?[0] is JsonObject variant)
            {
                variant["publishDate"] = publishDate;
                variant["state"] = "PublishedPendingChanges";
            }

            return Ok(HttpStatusCode.OK);
        }

        if (method == HttpMethod.Put && parts.Length == 3 && parts[2] == "move")
        {
            _parents[Guid.Parse(parts[1])] = node?["target"]?["id"]?.GetValue<string>() is { } t ? Guid.Parse(t) : null;
            return Ok(HttpStatusCode.OK);
        }

        if (method == HttpMethod.Put && parts is ["recycle-bin", "document", var trashedId, "restore"])
        {
            var key = Guid.Parse(trashedId);
            Stored("document", key)!["isTrashed"] = false;
            _parents[key] = node?["target"]?["id"]?.GetValue<string>() is { } t ? Guid.Parse(t) : null;
            _order[key] = _nextOrder++;
            return Ok(HttpStatusCode.OK);
        }

        if (method == HttpMethod.Put && parts.Length == 3 && parts[2] == "publish")
        {
            // Like the real read model: a published page carries a publish date.
            var variant = Stored("document", Guid.Parse(parts[1]))!["variants"]![0]!;
            variant["publishDate"] = "2026-01-01T00:00:00+00:00";
            variant["state"] = "Published";
            return Ok(HttpStatusCode.OK);
        }

        throw new InvalidOperationException($"Fake: unhandled {method} {path}");
    }

    private JsonObject TreePage(string kind, Guid? parent, Dictionary<string, string> query)
    {
        var skip = int.Parse(query.GetValueOrDefault("skip") ?? "0");
        var take = int.Parse(query.GetValueOrDefault("take") ?? "100");
        var all = ChildrenOf(parent).Where(k => Stored(kind, k) is not null).ToList();
        var items = new JsonArray();
        foreach (var key in all.Skip(skip).Take(take))
        {
            var doc = Stored(kind, key)!;
            items.Add(new JsonObject
            {
                ["id"] = key.ToString("D"),
                ["parent"] = parent is null ? null : new JsonObject { ["id"] = parent.Value.ToString("D") },
                ["hasChildren"] = _parents.Values.Any(p => p == key),
                ["documentType"] = doc["documentType"]?.DeepClone(),
                ["mediaType"] = doc["mediaType"]?.DeepClone(),
                ["variants"] = doc["variants"]?.DeepClone(),
            });
        }

        return new JsonObject { ["total"] = all.Count, ["items"] = items };
    }

    private Dictionary<Guid, JsonObject> Items(string collection)
    {
        if (!_store.TryGetValue(collection, out var items))
        {
            _store[collection] = items = [];
        }

        return items;
    }

    private static (string Route, Dictionary<string, string> Query) Split(string path)
    {
        var index = path.IndexOf('?', StringComparison.Ordinal);
        if (index < 0)
        {
            return (path, []);
        }

        var query = path[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : string.Empty));
        return (path[..index], query);
    }

    private static Task<ApiResponse> Ok(HttpStatusCode status) => Task.FromResult(new ApiResponse(status, string.Empty, null));

    public static UmbracoApiException Problem(HttpStatusCode status = HttpStatusCode.BadRequest, string title = "Validation failed") =>
        new(status, title, "detail that must never reach a report");
}
