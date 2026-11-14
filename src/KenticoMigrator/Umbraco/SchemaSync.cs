using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using KenticoMigrator.Migration;

namespace KenticoMigrator.Umbraco;

/// <summary>
/// Makes the target schema match the plan (FR-012): data types, templates, then document types. Items are found by their
/// key (the planned keys are sent as the id on create), an item that already equals the plan is left alone, and a failed
/// item is a <see cref="ItemOutcome.Failed"/> result while the rest continues. Built-in data types are only looked up by
/// name and are reported only when they cannot be found.
/// </summary>
public sealed class SchemaSync(IUmbracoApi api)
{
    private const string GroupType = "Group";
    private const string CompositionType = "Composition";
    private const string DefaultIcon = "icon-document";
    private const string FolderIcon = "icon-folder";
    private const int FilterPageSize = 100;

    private static readonly string[] TypeLevelEditorFields = ["icon", "description", "cleanup", "collection", "variesByCulture", "variesBySegment"];
    private static readonly string[] PropertyLevelEditorFields = ["description", "validation", "appearance", "variesByCulture", "variesBySegment"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ensures data types, templates and document types (compositions first). With <paramref name="preview"/> nothing is
    /// written and the outcome is <c>WouldCreate</c>, <c>WouldUpdate</c> or <c>Unchanged</c>. Editor additions to a document
    /// type (properties, groups, allowed children, compositions) are kept; a template an editor changed is left alone
    /// (<c>EditedSkipped</c>) unless <paramref name="overwriteEdited"/> is set. Pass <paramref name="results"/> to keep what was
    /// done when the run aborts: the list is filled as items are handled.
    /// </summary>
    public async Task<IReadOnlyList<ItemResult>> EnsureAsync(MigrationPlan plan, bool preview, CancellationToken ct, bool overwriteEdited = false, List<ItemResult>? results = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        results ??= [];
        var dataTypeIds = await EnsureDataTypesAsync(plan, preview, results, ct).ConfigureAwait(false);
        await EnsureTemplatesAsync(plan, preview, overwriteEdited, results, ct).ConfigureAwait(false);
        await EnsureDocumentTypesAsync(plan, dataTypeIds, preview, results, ct).ConfigureAwait(false);
        return results;
    }

    // Data types. Returns plan key -> id in the target (a placeholder key of a built-in type maps to the id found by name).
    private async Task<Dictionary<Guid, Guid>> EnsureDataTypesAsync(MigrationPlan plan, bool preview, List<ItemResult> results, CancellationToken ct)
    {
        var ids = new Dictionary<Guid, Guid>();
        foreach (var type in plan.DataTypes)
        {
            try
            {
                if (type.IsBuiltIn)
                {
                    if (await FindBuiltInAsync(type.Name, ct).ConfigureAwait(false) is { } found)
                    {
                        ids[type.Key] = found;
                    }
                    else
                    {
                        results.Add(Result(type.Key, ItemKinds.DataType, type.EditorAlias, ItemOutcome.Failed, $"Built-in data type '{type.Name}' not found"));
                    }

                    continue;
                }

                ids[type.Key] = type.Key;
                results.Add(await EnsureDataTypeAsync(type, preview, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                results.Add(Result(type.Key, ItemKinds.DataType, type.EditorAlias, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }

        return ids;
    }

    private async Task<Guid?> FindBuiltInAsync(string name, CancellationToken ct)
    {
        var page = await api.GetAsync($"/filter/data-type?skip=0&take={FilterPageSize}&name={Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        var match = page?["items"]?.AsArray().FirstOrDefault(i => string.Equals(i?["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
        return match?["id"]?.GetValue<string>() is { } id ? Guid.Parse(id) : null;
    }

    private async Task<ItemResult> EnsureDataTypeAsync(TargetDataType type, bool preview, CancellationToken ct)
    {
        var values = type.Configuration.Select(c => new DataTypeValue(c.Key, c.Value)).ToList();
        var path = $"/data-type/{type.Key:D}";
        var current = await api.GetAsync(path, ct).ConfigureAwait(false);

        if (current is null)
        {
            if (!preview)
            {
                await api.SendAsync(HttpMethod.Post, "/data-type", new DataTypeRequest(type.Key, type.Name, type.EditorAlias, type.EditorUiAlias, values), ct).ConfigureAwait(false);
            }

            return Result(type.Key, ItemKinds.DataType, type.EditorAlias, preview ? ItemOutcome.WouldCreate : ItemOutcome.Created);
        }

        if (DataTypeMatches(current, type, values))
        {
            return Result(type.Key, ItemKinds.DataType, type.EditorAlias, ItemOutcome.Unchanged);
        }

        if (!preview)
        {
            await api.SendAsync(HttpMethod.Put, path, new DataTypeRequest(null, type.Name, type.EditorAlias, type.EditorUiAlias, values), ct).ConfigureAwait(false);
        }

        return Result(type.Key, ItemKinds.DataType, type.EditorAlias, preview ? ItemOutcome.WouldUpdate : ItemOutcome.Updated);
    }

    // Only the configuration keys the tool sets are compared: the server adds defaults of its own.
    private static bool DataTypeMatches(JsonNode current, TargetDataType planned, List<DataTypeValue> values)
    {
        if (current["name"]?.GetValue<string>() != planned.Name
            || current["editorAlias"]?.GetValue<string>() != planned.EditorAlias
            || current["editorUiAlias"]?.GetValue<string>() != planned.EditorUiAlias)
        {
            return false;
        }

        var currentValues = current["values"]?.AsArray() ?? [];
        foreach (var value in values)
        {
            var existing = currentValues.FirstOrDefault(v => v?["alias"]?.GetValue<string>() == value.Alias)?["value"];
            if (Canonical(existing) != Canonical(JsonSerializer.SerializeToNode(value.Value, Json)))
            {
                return false;
            }
        }

        return true;
    }

    // Templates. The Razor content comes from the embedded views; the page template names its layout in its own markup.
    private async Task EnsureTemplatesAsync(MigrationPlan plan, bool preview, bool overwriteEdited, List<ItemResult> results, CancellationToken ct)
    {
        foreach (var template in plan.Templates)
        {
            try
            {
                results.Add(await EnsureTemplateAsync(template, preview, overwriteEdited, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                results.Add(Result(template.Key, ItemKinds.Template, template.Alias, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }
    }

    private async Task<ItemResult> EnsureTemplateAsync(TargetTemplate template, bool preview, bool overwriteEdited, CancellationToken ct)
    {
        var body = template.Content.Length > 0 ? template.Content : LoadEmbeddedTemplate(template.Alias);
        var plannedHash = ContentHash(body);
        var content = WithMarker(body, plannedHash);
        var path = $"/template/{template.Key:D}";
        var current = await api.GetAsync(path, ct).ConfigureAwait(false);

        if (current is null)
        {
            if (!preview)
            {
                await api.SendAsync(HttpMethod.Post, "/template", new TemplateRequest(template.Key, template.Name, template.Alias, content), ct).ConfigureAwait(false);
            }

            return Result(template.Key, ItemKinds.Template, template.Alias, preview ? ItemOutcome.WouldCreate : ItemOutcome.Created);
        }

        // The marker holds the hash of the body the tool wrote. A body that no longer hashes to it (or has no marker) was
        // changed by an editor; the tool leaves it alone unless it is told to overwrite.
        var (storedHash, currentBody) = SplitMarker(current["content"]?.GetValue<string>());
        var legacy = false;
        if (storedHash is null && KnownEarlierBodyHashes.Contains(ContentHash(currentBody)))
        {
            // The tool's own body from before markers existed: not an edit, and it gets its marker now.
            storedHash = ContentHash(currentBody);
            legacy = true;
        }

        if (storedHash is null || storedHash != ContentHash(currentBody))
        {
            if (!overwriteEdited)
            {
                return Result(template.Key, ItemKinds.Template, template.Alias, ItemOutcome.EditedSkipped);
            }

            if (!preview)
            {
                await api.SendAsync(HttpMethod.Put, path, new TemplateRequest(null, template.Name, template.Alias, content), ct).ConfigureAwait(false);
            }

            return Result(template.Key, ItemKinds.Template, template.Alias, preview ? ItemOutcome.WouldUpdate : ItemOutcome.EditedOverwritten);
        }

        if (!legacy
            && storedHash == plannedHash
            && current["name"]?.GetValue<string>() == template.Name
            && current["alias"]?.GetValue<string>() == template.Alias)
        {
            return Result(template.Key, ItemKinds.Template, template.Alias, ItemOutcome.Unchanged);
        }

        if (!preview)
        {
            await api.SendAsync(HttpMethod.Put, path, new TemplateRequest(null, template.Name, template.Alias, content), ct).ConfigureAwait(false);
        }

        return Result(template.Key, ItemKinds.Template, template.Alias, preview ? ItemOutcome.WouldUpdate : ItemOutcome.Updated);
    }

    // Bodies earlier versions of the tool wrote without a marker (sha256 of the normalised body of MigratedPage.cshtml before the
    // marker was introduced, and of the two earlier MigratedLayout.cshtml versions). Such a template is the tool's own, not an edit.
    private static readonly HashSet<string> KnownEarlierBodyHashes =
    [
        "50e5838dca66f12c97fbf28f6c71b62f9d1aca7d99b3bba80648acae33f1bc89",
        "2ff65ec378f9445deb7e552b5c0689e7a9312d3f759c24cb9906b0e6518db10d",
        "e3e3c7c0ce12eab0ac6bfa6a93dd39b013be62155088e4a10588bee5763cd3a1",
    ];

    private const string MarkerPrefix = "@* migrator-fingerprint:";
    private const string MarkerSuffix = " *@";

    private static string ContentHash(string body) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(NormalizeContent(body))));

    private static string WithMarker(string body, string hash) => $"{body.TrimEnd()}\n{MarkerPrefix}{hash}{MarkerSuffix}\n";

    // The hash in the last marker comment (null when there is none) and the content without that comment.
    private static (string? Hash, string Body) SplitMarker(string? content)
    {
        content ??= string.Empty;
        var start = content.LastIndexOf(MarkerPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return (null, content);
        }

        var hashStart = start + MarkerPrefix.Length;
        var end = content.IndexOf(MarkerSuffix, hashStart, StringComparison.Ordinal);
        if (end < 0)
        {
            return (null, content);
        }

        return (content[hashStart..end], content.Remove(start, end + MarkerSuffix.Length - start));
    }

    internal static string LoadEmbeddedTemplate(string alias)
    {
        var name = $"Templates/{char.ToUpperInvariant(alias[0])}{alias[1..]}.cshtml";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"No embedded view for template '{alias}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string NormalizeContent(string? content) => (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    // Document types. Pass 1 creates a missing type without its allowed children (they may point at types that do not exist yet,
    // or at the type itself); pass 2 writes the complete type for every new or changed type.
    private async Task EnsureDocumentTypesAsync(MigrationPlan plan, Dictionary<Guid, Guid> dataTypeIds, bool preview, List<ItemResult> results, CancellationToken ct)
    {
        var typeKeys = plan.DocumentTypes.ToDictionary(t => t.Alias, t => t.Key, StringComparer.Ordinal);
        var templateKeys = plan.Templates.ToDictionary(t => t.Alias, t => t.Key, StringComparer.Ordinal);
        var pending = new List<(TargetDocumentType Type, object Body, bool Created)>();

        foreach (var type in plan.DocumentTypes)
        {
            try
            {
                var full = BuildDocumentType(type, typeKeys, templateKeys, dataTypeIds, withAllowedChildren: true, withId: false);
                var planned = JsonSerializer.SerializeToNode(full, Json)!.AsObject();
                var current = await api.GetAsync($"/document-type/{type.Key:D}", ct).ConfigureAwait(false);

                if (current is null)
                {
                    if (preview)
                    {
                        results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, ItemOutcome.WouldCreate));
                        continue;
                    }

                    var initial = BuildDocumentType(type, typeKeys, templateKeys, dataTypeIds, withAllowedChildren: false, withId: true);
                    await api.SendAsync(HttpMethod.Post, "/document-type", initial, ct).ConfigureAwait(false);
                    pending.Add((type, full, true));
                }
                else if (Shape(current, planned) == Shape(planned, planned))
                {
                    results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, ItemOutcome.Unchanged));
                }
                else if (preview)
                {
                    results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, ItemOutcome.WouldUpdate));
                }
                else
                {
                    pending.Add((type, MergeEditorAdditions(planned, current), false));
                }
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }

        foreach (var (type, body, created) in pending)
        {
            try
            {
                await api.SendAsync(HttpMethod.Put, $"/document-type/{type.Key:D}", body, ct).ConfigureAwait(false);
                results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, created ? ItemOutcome.Created : ItemOutcome.Updated));
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }
    }

    private static DocumentTypeRequest BuildDocumentType(
        TargetDocumentType type,
        Dictionary<string, Guid> typeKeys,
        Dictionary<string, Guid> templateKeys,
        Dictionary<Guid, Guid> dataTypeIds,
        bool withAllowedChildren,
        bool withId)
    {
        var groups = type.Properties.Select(p => p.Group).Distinct(StringComparer.Ordinal).ToList();
        var containers = groups
            .Select((name, index) => new ContainerRequest(Keys.Derived("container", $"{type.Alias}:{name}"), null, name, GroupType, index))
            .ToList();

        var properties = type.Properties.Select((p, index) =>
        {
            if (!dataTypeIds.TryGetValue(p.DataTypeKey, out var dataTypeId))
            {
                throw new MappingException(p.Alias, "The data type of the property is not available in the target.");
            }

            return new PropertyTypeRequest(
                Keys.Derived("property", $"{type.Alias}:{p.Alias}"),
                new IdRef(Keys.Derived("container", $"{type.Alias}:{p.Group}")),
                index,
                p.Alias,
                p.Name,
                new IdRef(dataTypeId),
                VariesByCulture: false,
                VariesBySegment: false,
                new PropertyValidation(p.Mandatory),
                new PropertyAppearance(LabelOnTop: false));
        }).ToList();

        IdRef? template = type.TemplateAlias is null ? null : new IdRef(templateKeys[type.TemplateAlias]);
        var allowed = withAllowedChildren
            ? type.AllowedChildAliases.Where(typeKeys.ContainsKey)
                .Select((alias, index) => new AllowedDocumentType(new IdRef(typeKeys[alias]), index)).ToList()
            : [];

        return new DocumentTypeRequest(
            withId ? type.Key : null,
            type.Alias,
            type.Name,
            type.Alias == PlanAliases.Folder ? FolderIcon : DefaultIcon,
            type.AllowedAtRoot,
            VariesByCulture: false,
            VariesBySegment: false,
            IsElement: false,
            properties,
            containers,
            template is null ? [] : [template],
            template,
            new CleanupRequest(PreventCleanup: false),
            allowed,
            [.. type.Compositions.Select(alias => new CompositionRequest(new IdRef(typeKeys[alias]), CompositionType))]);
    }

    // The part of a document type the tool owns, in one canonical text. Used on the planned request and on the read model, so
    // ids the server may regenerate and fields the tool does not set do not matter, and neither do additions an editor made:
    // only the properties, groups, allowed children, compositions and templates that the plan names are compared.
    private static string Shape(JsonNode type, JsonObject planned)
    {
        var plannedAliases = Aliases(planned["properties"]).ToHashSet(StringComparer.Ordinal);
        var plannedGroups = (planned["containers"]?.AsArray() ?? []).Select(c => c?["name"]?.GetValue<string>()).ToHashSet();
        var plannedAllowed = ReferenceIds(planned["allowedDocumentTypes"]?.AsArray().Select(a => a?["documentType"])).ToHashSet();
        var plannedCompositions = ReferenceIds(planned["compositions"]?.AsArray().Select(c => c?["documentType"])).ToHashSet();
        var plannedTemplates = ReferenceIds(planned["allowedTemplates"]?.AsArray()).ToHashSet();

        var containers = (type["containers"]?.AsArray() ?? []).OfType<JsonObject>().OrderBy(c => c["sortOrder"]?.GetValue<int>() ?? 0).ToList();
        var containerNames = containers.ToDictionary(c => c["id"]!.GetValue<string>(), c => c["name"]?.GetValue<string>(), StringComparer.OrdinalIgnoreCase);

        var shape = new JsonObject
        {
            ["alias"] = type["alias"]?.GetValue<string>(),
            ["name"] = type["name"]?.GetValue<string>(),
            // Only a planned "allowed at root" is owned: an editor may allow more.
            ["allowedAsRoot"] = planned["allowedAsRoot"]?.GetValue<bool>() == true ? type["allowedAsRoot"]?.GetValue<bool>() : null,
            ["allowedTemplates"] = Ids(ReferenceIds(type["allowedTemplates"]?.AsArray()).Where(plannedTemplates.Contains).Order(StringComparer.Ordinal)),
            ["defaultTemplate"] = type["defaultTemplate"]?["id"]?.GetValue<string>()?.ToLowerInvariant(),
            ["compositions"] = Ids(ReferenceIds(type["compositions"]?.AsArray().Select(c => c?["documentType"])).Where(plannedCompositions.Contains).Order(StringComparer.Ordinal)),
            ["allowedDocumentTypes"] = Ids(
                ReferenceIds((type["allowedDocumentTypes"]?.AsArray() ?? []).OfType<JsonObject>().OrderBy(a => a["sortOrder"]?.GetValue<int>() ?? 0).Select(a => a["documentType"]))
                    .Where(plannedAllowed.Contains)),
            ["containers"] = new JsonArray([.. containers.Where(c => plannedGroups.Contains(c["name"]?.GetValue<string>())).Select(c => (JsonNode?)new JsonObject
            {
                ["name"] = c["name"]?.GetValue<string>(),
                ["type"] = c["type"]?.GetValue<string>(),
            })]),
            ["properties"] = new JsonArray([.. (type["properties"]?.AsArray() ?? []).OfType<JsonObject>()
                .Where(p => plannedAliases.Contains(p["alias"]?.GetValue<string>() ?? string.Empty))
                .OrderBy(p => p["sortOrder"]?.GetValue<int>() ?? 0)
                .Select(p => (JsonNode?)new JsonObject
                {
                    ["alias"] = p["alias"]?.GetValue<string>(),
                    ["name"] = p["name"]?.GetValue<string>(),
                    ["dataType"] = p["dataType"]?["id"]?.GetValue<string>()?.ToLowerInvariant(),
                    ["group"] = p["container"]?["id"]?.GetValue<string>() is { } cid && containerNames.TryGetValue(cid, out var group) ? group : null,
                    ["mandatory"] = p["validation"]?["mandatory"]?.GetValue<bool>() ?? false,
                })]),
        };
        return shape.ToJsonString();
    }

    // The body of an update: the planned type, plus everything an editor added that the plan does not name (properties, groups,
    // allowed children, compositions, templates). Planned properties and groups keep the ids they already have in the target,
    // so an update never makes Umbraco see a property as new and drop its data.
    private static JsonObject MergeEditorAdditions(JsonObject planned, JsonNode current)
    {
        var merged = (JsonObject)planned.DeepClone();
        merged["allowedAsRoot"] = (planned["allowedAsRoot"]?.GetValue<bool>() ?? false) || (current["allowedAsRoot"]?.GetValue<bool>() ?? false);

        // Type-level settings the tool does not own stay as the editor left them.
        foreach (var field in TypeLevelEditorFields)
        {
            if (current is JsonObject currentObject && currentObject.ContainsKey(field))
            {
                merged[field] = currentObject[field]?.DeepClone();
            }
        }

        var currentContainers = (current["containers"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        var plannedContainers = merged["containers"]!.AsArray();
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in plannedContainers.OfType<JsonObject>())
        {
            var existing = currentContainers.FirstOrDefault(c => c["name"]?.GetValue<string>() == container["name"]?.GetValue<string>());
            if (existing?["id"]?.GetValue<string>() is { } existingId)
            {
                idMap[container["id"]!.GetValue<string>()] = existingId;
                container["id"] = existingId;
            }
        }

        var plannedGroupNames = plannedContainers.Select(c => c?["name"]?.GetValue<string>()).ToHashSet();
        foreach (var extra in currentContainers.Where(c => !plannedGroupNames.Contains(c["name"]?.GetValue<string>())))
        {
            plannedContainers.Add(extra.DeepClone());
        }

        var currentProperties = (current["properties"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        var properties = merged["properties"]!.AsArray();
        foreach (var property in properties.OfType<JsonObject>())
        {
            if (property["container"]?["id"]?.GetValue<string>() is { } containerId && idMap.TryGetValue(containerId, out var mapped))
            {
                property["container"] = new JsonObject { ["id"] = mapped };
            }

            var existing = currentProperties.FirstOrDefault(p => p["alias"]?.GetValue<string>() == property["alias"]?.GetValue<string>());
            if (existing?["id"]?.GetValue<string>() is { } propertyId)
            {
                property["id"] = propertyId;
                // The tool owns alias, name, data type, group and the mandatory flag; help text, the other validation settings
                // (patterns, messages) and appearance stay as they are.
                var plannedMandatory = property["validation"]?["mandatory"]?.GetValue<bool>() ?? false;
                foreach (var field in PropertyLevelEditorFields.Where(existing.ContainsKey))
                {
                    property[field] = existing[field]?.DeepClone();
                }

                if (property["validation"] is JsonObject validation)
                {
                    validation["mandatory"] = plannedMandatory;
                }
            }
        }

        var plannedAliases = properties.Select(p => p?["alias"]?.GetValue<string>()).ToHashSet();
        // Editor-added properties go after the planned ones, in the order they had, with new numbers so no two tie.
        var nextOrder = properties.Count;
        foreach (var extra in currentProperties.Where(p => !plannedAliases.Contains(p["alias"]?.GetValue<string>())).OrderBy(p => p["sortOrder"]?.GetValue<int>() ?? 0))
        {
            var clone = (JsonObject)extra.DeepClone();
            clone["sortOrder"] = nextOrder++;
            properties.Add(clone);
        }

        AppendMissing(merged["allowedDocumentTypes"]!.AsArray(), current["allowedDocumentTypes"]?.AsArray(), a => a?["documentType"]?["id"]?.GetValue<string>(), renumber: true);
        AppendMissing(merged["compositions"]!.AsArray(), current["compositions"]?.AsArray(), c => c?["documentType"]?["id"]?.GetValue<string>(), renumber: false);
        AppendMissing(merged["allowedTemplates"]!.AsArray(), current["allowedTemplates"]?.AsArray(), t => t?["id"]?.GetValue<string>(), renumber: false);
        return merged;
    }

    // Items of the target that the plan does not name are added unchanged. Allowed child types are numbered after the planned ones,
    // in the order they had, so no two share a sort order.
    private static void AppendMissing(JsonArray target, JsonArray? source, Func<JsonNode?, string?> idOf, bool renumber)
    {
        var known = target.Select(idOf).Where(i => i is not null).Select(i => i!.ToLowerInvariant()).ToHashSet();
        var ordered = renumber ? (source ?? []).OrderBy(i => i?["sortOrder"]?.GetValue<int>() ?? 0).ToList() : [.. source ?? []];
        var next = target.Count;
        foreach (var item in ordered)
        {
            if (idOf(item) is { } id && known.Add(id.ToLowerInvariant()))
            {
                var clone = item!.DeepClone();
                if (renumber)
                {
                    clone["sortOrder"] = next++;
                }

                target.Add(clone);
            }
        }
    }

    private static IEnumerable<string> Aliases(JsonNode? properties) =>
        (properties?.AsArray() ?? []).Select(p => p?["alias"]?.GetValue<string>()).Where(a => a is not null).Select(a => a!);

    private static IEnumerable<string> ReferenceIds(IEnumerable<JsonNode?>? references) =>
        (references ?? []).Select(r => r?["id"]?.GetValue<string>()?.ToLowerInvariant()).Where(i => i is not null).Select(i => i!);

    private static JsonArray Ids(IEnumerable<string> ids) => new([.. ids.Select(i => (JsonNode?)JsonValue.Create(i))]);

    private static string Canonical(JsonNode? node) => node?.ToJsonString() ?? "null";

    private static ItemResult Result(Guid key, string kind, string typeAlias, ItemOutcome outcome, string? error = null) =>
        new(key, kind, typeAlias, string.Empty, outcome, error);
}
