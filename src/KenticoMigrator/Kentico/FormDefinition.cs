using System.Xml;
using System.Xml.Linq;

namespace KenticoMigrator.Kentico;

/// <summary>The fields of a page type and its primary key column, from one parse.</summary>
public sealed record ParsedForm(IReadOnlyList<SourceField> Fields, string? PrimaryKey);

/// <summary>Parser for the <c>CMS_Class.ClassFormDefinition</c> XML of a Kentico 11 page type.</summary>
public static class FormDefinition
{
    private static readonly string[] OptionControls = ["dropdownlistcontrol", "radiobuttonscontrol"];

    /// <summary>
    /// Returns the fields that have a column, in definition order. The primary key field is left out
    /// (see <see cref="PrimaryKeyColumn"/>); it is not content.
    /// </summary>
    /// <exception cref="InvalidDataException">The XML is malformed or contains a DTD.</exception>
    public static IReadOnlyList<SourceField> Parse(string? xml) => ParseForm(xml).Fields;

    /// <summary>The column of the field marked <c>isPK="true"</c>, or null when there is none.</summary>
    public static string? PrimaryKeyColumn(string? xml) => ParseForm(xml).PrimaryKey;

    /// <summary>Parses the definition once and returns both the fields and the primary key.</summary>
    /// <exception cref="InvalidDataException">The XML is malformed or contains a DTD.</exception>
    public static ParsedForm ParseForm(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return new ParsedForm([], null);
        }

        XElement form;
        try
        {
            form = SafeXml.Parse(xml).Root!;
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"Cannot parse the form definition: {ex.Message}", ex);
        }

        var fields = new List<SourceField>();
        string? primaryKey = null;
        foreach (var field in form.Elements("field"))
        {
            var column = (string?)field.Attribute("column");
            if (string.IsNullOrWhiteSpace(column))
            {
                continue;
            }

            if (IsPrimaryKey(field))
            {
                primaryKey ??= column;
                continue;
            }

            var size = int.TryParse((string?)field.Attribute("columnsize"), out var parsedSize) ? parsedSize : (int?)null;
            var control = field.Element("settings")?.Element("controlname")?.Value;
            var guid = Guid.TryParse((string?)field.Attribute("guid"), out var parsedGuid) ? parsedGuid : Guid.Empty;

            fields.Add(new SourceField(
                column,
                ((string?)field.Attribute("columntype") ?? string.Empty).ToLowerInvariant(),
                size,
                ParseOptions(field, control),
                guid));
        }

        return new ParsedForm(fields, primaryKey);
    }

    private static bool IsPrimaryKey(XElement field) =>
        string.Equals((string?)field.Attribute("isPK"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dropdown and radio controls store options either as lines of <c>value;text</c> or, in older
    /// definitions, as escaped <c>&lt;item value="..." text="..." /&gt;</c> markup. Empty values are dropped.
    /// Markup that cannot be parsed (or has a DTD) gives no options.
    /// </summary>
    private static IReadOnlyList<string> ParseOptions(XElement field, string? control)
    {
        if (control is null || !OptionControls.Contains(control.ToLowerInvariant()))
        {
            return [];
        }

        var raw = field.Element("settings")?.Element("Options")?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        IEnumerable<string> values;
        if (raw.TrimStart().StartsWith('<'))
        {
            try
            {
                values = SafeXml.Parse($"<items>{raw}</items>").Root!.Elements("item")
                    .Select(i => ((string?)i.Attribute("value") ?? string.Empty).Trim())
                    .ToList();
            }
            catch (XmlException)
            {
                return [];
            }
        }
        else
        {
            values = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(';', 2)[0].Trim());
        }

        return values.Where(v => v.Length > 0).ToList();
    }
}
