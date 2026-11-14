using System.Xml.Linq;

namespace KenticoMigrator.Kentico;

/// <summary>Parser for the <c>CMS_Class.ClassFormDefinition</c> XML of a Kentico 11 page type.</summary>
public static class FormDefinition
{
    private static readonly string[] OptionControls = ["dropdownlistcontrol", "radiobuttonscontrol"];

    /// <summary>
    /// Returns the fields that have a column, in definition order. The primary key field is left out
    /// (see <see cref="PrimaryKeyColumn"/>); it is not content.
    /// </summary>
    public static IReadOnlyList<SourceField> Parse(string? xml)
    {
        var form = Load(xml);
        if (form is null)
        {
            return [];
        }

        var fields = new List<SourceField>();
        foreach (var field in form.Elements("field"))
        {
            var column = (string?)field.Attribute("column");
            if (string.IsNullOrWhiteSpace(column) || IsPrimaryKey(field))
            {
                continue;
            }

            var size = int.TryParse((string?)field.Attribute("columnsize"), out var parsedSize) ? parsedSize : (int?)null;
            var control = field.Element("settings")?.Element("controlname")?.Value;
            var guid = Guid.TryParse((string?)field.Attribute("guid"), out var parsedGuid) ? parsedGuid : Guid.Empty;

            fields.Add(new SourceField(
                column,
                ((string?)field.Attribute("columntype") ?? string.Empty).ToLowerInvariant(),
                size,
                control,
                ParseOptions(field, control),
                guid));
        }

        return fields;
    }

    /// <summary>The column of the field marked <c>isPK="true"</c>, or null when there is none.</summary>
    public static string? PrimaryKeyColumn(string? xml) =>
        Load(xml)?.Elements("field").Where(IsPrimaryKey).Select(f => (string?)f.Attribute("column")).FirstOrDefault();

    private static XElement? Load(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        return XDocument.Parse(xml).Root;
    }

    private static bool IsPrimaryKey(XElement field) =>
        string.Equals((string?)field.Attribute("isPK"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dropdown and radio controls store options either as lines of <c>value;text</c> or, in older
    /// definitions, as escaped <c>&lt;item value="..." text="..." /&gt;</c> markup. Empty values are dropped.
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

        var values = raw.TrimStart().StartsWith('<')
            ? ParseItemMarkup(raw)
            : raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(';', 2)[0].Trim());

        return values.Where(v => v.Length > 0).ToList();
    }

    private static IEnumerable<string> ParseItemMarkup(string markup) =>
        XElement.Parse($"<items>{markup}</items>").Elements("item").Select(i => ((string?)i.Attribute("value") ?? string.Empty).Trim());
}
