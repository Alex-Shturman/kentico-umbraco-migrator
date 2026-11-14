using System.Xml.Linq;

namespace KenticoMigrator.Kentico;

/// <summary>Reads editable regions from <c>CMS_Document.DocumentContent</c> and the page template.</summary>
public static class EditableRegions
{
    private const string EditableTextType = "editabletext";

    /// <summary>
    /// Returns the control ids of the <c>editabletext</c> web parts of a page template
    /// (<c>CMS_PageTemplate.PageTemplateWebParts</c>), in template order.
    /// </summary>
    public static IReadOnlyList<string> TemplateOrder(string? pageTemplateWebPartsXml)
    {
        if (string.IsNullOrWhiteSpace(pageTemplateWebPartsXml))
        {
            return [];
        }

        return XDocument.Parse(pageTemplateWebPartsXml)
            .Descendants("webpart")
            .Where(w => string.Equals((string?)w.Attribute("type"), EditableTextType, StringComparison.OrdinalIgnoreCase))
            .Select(w => (string?)w.Attribute("controlid"))
            .OfType<string>()
            .ToList();
    }

    /// <summary>
    /// Returns the regions of a document: first those named in <paramref name="templateOrder"/> (template order),
    /// then the others in document order. A document id is either the control id or <c>controlid;webpartguid</c>.
    /// The XML layer decodes entities once; CDATA content is taken literally.
    /// </summary>
    public static IReadOnlyList<EditableRegion> Parse(string? documentContent, IReadOnlyList<string> templateOrder)
    {
        if (string.IsNullOrWhiteSpace(documentContent))
        {
            return [];
        }

        var inDocument = XDocument.Parse(documentContent)
            .Descendants("webpart")
            .Select(w => new EditableRegion((string?)w.Attribute("id") ?? string.Empty, w.Value))
            .Where(r => r.Id.Length > 0)
            .ToList();

        var ordered = new List<EditableRegion>(inDocument.Count);
        foreach (var controlId in templateOrder)
        {
            var match = inDocument.FirstOrDefault(r => !ordered.Contains(r) && ControlIdOf(r.Id).Equals(controlId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                ordered.Add(match);
            }
        }

        ordered.AddRange(inDocument.Where(r => !ordered.Contains(r)));
        return ordered;
    }

    private static string ControlIdOf(string regionId)
    {
        var separator = regionId.IndexOf(';');
        return separator < 0 ? regionId : regionId[..separator];
    }
}
