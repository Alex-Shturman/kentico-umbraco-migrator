using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KenticoMigrator.Migration;

namespace KenticoMigrator.Umbraco;

/// <summary>
/// The values the tool owns on one item, in the shape the Management API uses. For a document the values
/// include <c>umbracoUrlName</c> and <c>migrationSourceKey</c>; the fingerprint property itself is never part of it.
/// </summary>
public sealed record OwnedValues(string Name, Guid? ParentKey, IReadOnlyDictionary<string, JsonNode?> Values)
{
    /// <summary>Owned values of a media item: its name, folder and media type.</summary>
    public static OwnedValues ForMedia(string name, Guid? parentKey, string mediaType) =>
        new(name, parentKey, new Dictionary<string, JsonNode?> { ["mediaType"] = JsonValue.Create(mediaType) });
}

/// <summary>
/// Change detection (research R5). The same canonical form is computed from the values the tool plans to write and
/// from the values read back from Umbraco, so an item that nobody touched hashes equal on both sides. The form hides
/// what the server adds or reshapes (verified, R6/R7): rich text compares by <c>markup</c> only, media picker items by
/// <c>key</c> and <c>mediaKey</c> only, dates in the <c>yyyy-MM-dd HH:mm:ss</c> form, numbers by value.
/// Empty values (null, empty string, empty array, empty markup) equal a missing value.
/// </summary>
public static partial class Fingerprint
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>SHA-256 (lowercase hex) over the canonical JSON of <paramref name="owned"/>.</summary>
    public static string Compute(OwnedValues owned)
    {
        ArgumentNullException.ThrowIfNull(owned);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("name", owned.Name);
            if (owned.ParentKey is { } parent)
            {
                writer.WriteString("parent", parent.ToString("D"));
            }
            else
            {
                writer.WriteNull("parent");
            }

            writer.WriteStartObject("values");
            foreach (var (alias, value) in owned.Values
                .Where(v => v.Key != PlanAliases.Fingerprint)
                .OrderBy(v => v.Key, StringComparer.Ordinal))
            {
                if (Normalize(value) is { } normalized)
                {
                    writer.WritePropertyName(alias);
                    Write(writer, normalized);
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    /// <summary>Fingerprint of a planned media item (name, folder, media type).</summary>
    public static string Compute(TargetMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);
        return Compute(OwnedValues.ForMedia(media.Name, media.ParentKey, media.MediaType.ToString()));
    }

    /// <summary>
    /// Owned values of an item read from Umbraco. <paramref name="apiValues"/> is the <c>values</c> array of the read
    /// model; only the properties in <paramref name="ownedAliases"/> count, so a property the tool does not write cannot
    /// make an item look edited.
    /// </summary>
    public static OwnedValues FromCurrent(string name, Guid? parentKey, JsonNode? apiValues, IReadOnlyCollection<string> ownedAliases)
    {
        ArgumentNullException.ThrowIfNull(ownedAliases);

        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (apiValues is JsonArray array)
        {
            foreach (var entry in array.OfType<JsonObject>())
            {
                if (entry["alias"]?.GetValue<string>() is { } alias && ownedAliases.Contains(alias))
                {
                    values[alias] = entry["value"];
                }
            }
        }

        return new OwnedValues(name, parentKey, values);
    }

    /// <summary>
    /// A stable key for one media picker item. The picker item <c>key</c> is part of the fingerprint, so the tool
    /// must write the same key on every run.
    /// </summary>
    public static Guid PickerItemKey(Guid ownerKey, string alias, Guid mediaKey) =>
        Keys.Derived("pickeritem", $"{ownerKey:D}:{alias}:{mediaKey:D}");

    // Returns the canonical node, or null when the value is empty.
    private static JsonNode? Normalize(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonValue value:
                return NormalizeValue(value);
            case JsonArray array:
                return NormalizeArray(array);
            case JsonObject obj:
                return NormalizeObject(obj);
            default:
                return null;
        }
    }

    private static JsonNode? NormalizeValue(JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                var text = value.GetValue<string>();
                return text.Length == 0 ? null : JsonValue.Create(NormalizeText(text));
            case JsonValueKind.True:
                return JsonValue.Create(true);
            case JsonValueKind.False:
                return JsonValue.Create(false);
            case JsonValueKind.Number:
                return JsonNode.Parse(NumberText(value.ToJsonString()));
            default:
                return null;
        }
    }

    private static JsonNode? NormalizeArray(JsonArray array)
    {
        if (array.Count == 0)
        {
            return null;
        }

        if (array.All(item => item is JsonObject o && o.ContainsKey("mediaKey")))
        {
            // Media picker: keep only what the tool writes; the server adds mediaTypeAlias, crops and focalPoint.
            var items = new JsonArray();
            foreach (var item in array.Cast<JsonObject>())
            {
                items.Add(new JsonObject
                {
                    ["key"] = JsonValue.Create(GuidText(item["key"])),
                    ["mediaKey"] = JsonValue.Create(GuidText(item["mediaKey"])),
                });
            }

            return items;
        }

        var normalized = new JsonArray();
        foreach (var item in array)
        {
            normalized.Add(Normalize(item));
        }

        return normalized;
    }

    private static JsonNode? NormalizeObject(JsonObject obj)
    {
        if (obj.ContainsKey("markup"))
        {
            // Rich text: the server returns an empty blocks object where null was sent; only the markup counts.
            var markup = obj["markup"]?.GetValue<string>();
            return string.IsNullOrEmpty(markup) ? null : new JsonObject { ["markup"] = JsonValue.Create(markup) };
        }

        var result = new JsonObject();
        foreach (var (key, child) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (Normalize(child) is { } normalized)
            {
                result[key] = normalized;
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static string NormalizeText(string text)
    {
        var match = DateTimeText().Match(text);
        if (!match.Success)
        {
            return text;
        }

        var seconds = match.Groups[6].Success ? match.Groups[6].Value : "00";
        return $"{match.Groups[1].Value}-{match.Groups[2].Value}-{match.Groups[3].Value} {match.Groups[4].Value}:{match.Groups[5].Value}:{seconds}";
    }

    private static string? GuidText(JsonNode? node)
    {
        var text = node?.GetValue<string>();
        return text is not null && Guid.TryParse(text, out var guid) ? guid.ToString("D") : text;
    }

    // Equal numbers get equal text whatever their spelling (7, 7.0, 7.00) and never depend on the current culture.
    private static string NumberText(string json) =>
        decimal.TryParse(json, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.############################", CultureInfo.InvariantCulture)
            : json;

    // Properties are written in ordinal key order so the text never depends on insertion order.
    private static void Write(Utf8JsonWriter writer, JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, child) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    Write(writer, child!);
                }

                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    if (item is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        Write(writer, item);
                    }
                }

                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    [GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.\d+)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DateTimeText();
}
