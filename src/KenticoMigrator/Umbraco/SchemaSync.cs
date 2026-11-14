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

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ensures data types, templates and document types (compositions first). With <paramref name="preview"/> nothing is
    /// written and the outcome is <c>WouldCreate</c>, <c>WouldUpdate</c> or <c>Unchanged</c>.
    /// </summary>
    public async Task<IReadOnlyList<ItemResult>> EnsureAsync(MigrationPlan plan, bool preview, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var results = new List<ItemResult>();
        var dataTypeIds = await EnsureDataTypesAsync(plan, preview, results, ct).ConfigureAwait(false);
        await EnsureTemplatesAsync(plan, preview, results, ct).ConfigureAwait(false);
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
    private async Task EnsureTemplatesAsync(MigrationPlan plan, bool preview, List<ItemResult> results, CancellationToken ct)
    {
        foreach (var template in plan.Templates)
        {
            try
            {
                results.Add(await EnsureTemplateAsync(template, preview, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                results.Add(Result(template.Key, ItemKinds.Template, template.Alias, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }
    }

    private async Task<ItemResult> EnsureTemplateAsync(TargetTemplate template, bool preview, CancellationToken ct)
    {
        var content = template.Content.Length > 0 ? template.Content : LoadEmbeddedTemplate(template.Alias);
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

        if (current["name"]?.GetValue<string>() == template.Name
            && current["alias"]?.GetValue<string>() == template.Alias
            && NormalizeContent(current["content"]?.GetValue<string>()) == NormalizeContent(content))
        {
            return Result(template.Key, ItemKinds.Template, template.Alias, ItemOutcome.Unchanged);
        }

        if (!preview)
        {
            await api.SendAsync(HttpMethod.Put, path, new TemplateRequest(null, template.Name, template.Alias, content), ct).ConfigureAwait(false);
        }

        return Result(template.Key, ItemKinds.Template, template.Alias, preview ? ItemOutcome.WouldUpdate : ItemOutcome.Updated);
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
        var pending = new List<(TargetDocumentType Type, DocumentTypeRequest Full, bool Created)>();

        foreach (var type in plan.DocumentTypes)
        {
            try
            {
                var full = BuildDocumentType(type, typeKeys, templateKeys, dataTypeIds, withAllowedChildren: true, withId: false);
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
                else if (Shape(current) == Shape(JsonSerializer.SerializeToNode(full, Json)!))
                {
                    results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, ItemOutcome.Unchanged));
                }
                else if (preview)
                {
                    results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, ItemOutcome.WouldUpdate));
                }
                else
                {
                    pending.Add((type, full, false));
                }
            }
            catch (Exception ex) when (ApiErrors.IsItemFailure(ex, ct))
            {
                results.Add(Result(type.Key, ItemKinds.DocumentType, type.Alias, ItemOutcome.Failed, ApiErrors.Describe(ex)));
            }
        }

        foreach (var (type, full, created) in pending)
        {
            try
            {
                await api.SendAsync(HttpMethod.Put, $"/document-type/{type.Key:D}", full, ct).ConfigureAwait(false);
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
                throw new InvalidDataException($"The data type of property '{p.Alias}' is not available in the target.");
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

    // The part of a document type the tool owns, in one canonical text. Used on the planned request and on the read model,
    // so ids the server may regenerate and fields the tool does not set do not matter.
    private static string Shape(JsonNode type)
    {
        var containers = (type["containers"]?.AsArray() ?? []).OfType<JsonObject>().OrderBy(c => c["sortOrder"]?.GetValue<int>() ?? 0).ToList();
        var containerNames = containers.ToDictionary(c => c["id"]!.GetValue<string>(), c => c["name"]?.GetValue<string>(), StringComparer.OrdinalIgnoreCase);

        var shape = new JsonObject
        {
            ["alias"] = type["alias"]?.GetValue<string>(),
            ["name"] = type["name"]?.GetValue<string>(),
            ["allowedAsRoot"] = type["allowedAsRoot"]?.GetValue<bool>(),
            ["allowedTemplates"] = Ids(type["allowedTemplates"], sorted: true),
            ["defaultTemplate"] = type["defaultTemplate"]?["id"]?.GetValue<string>()?.ToLowerInvariant(),
            ["compositions"] = Ids(type["compositions"]?.AsArray().Select(c => c?["documentType"]), sorted: true),
            ["allowedDocumentTypes"] = Ids(
                (type["allowedDocumentTypes"]?.AsArray() ?? []).OfType<JsonObject>().OrderBy(a => a["sortOrder"]?.GetValue<int>() ?? 0).Select(a => a["documentType"]),
                sorted: false),
            ["containers"] = new JsonArray([.. containers.Select(c => (JsonNode?)new JsonObject
            {
                ["name"] = c["name"]?.GetValue<string>(),
                ["type"] = c["type"]?.GetValue<string>(),
            })]),
            ["properties"] = new JsonArray([.. (type["properties"]?.AsArray() ?? []).OfType<JsonObject>()
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

    private static JsonArray Ids(JsonNode? references, bool sorted) => Ids(references?.AsArray().AsEnumerable(), sorted);

    private static JsonArray Ids(IEnumerable<JsonNode?>? references, bool sorted)
    {
        var ids = (references ?? []).Select(r => r?["id"]?.GetValue<string>()?.ToLowerInvariant()).Where(i => i is not null).Select(i => i!);
        return new JsonArray([.. (sorted ? ids.Order(StringComparer.Ordinal) : ids).Select(i => (JsonNode?)JsonValue.Create(i))]);
    }

    private static string Canonical(JsonNode? node) => node?.ToJsonString() ?? "null";

    private static ItemResult Result(Guid key, string kind, string typeAlias, ItemOutcome outcome, string? error = null) =>
        new(key, kind, typeAlias, string.Empty, outcome, error);
}
