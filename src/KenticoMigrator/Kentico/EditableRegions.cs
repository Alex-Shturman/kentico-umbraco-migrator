using System.Xml;
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
    /// <param name="context">Names the template in the error message when the XML is invalid.</param>
    /// <exception cref="InvalidDataException">The XML is malformed or contains a DTD.</exception>
    public static IReadOnlyList<string> TemplateOrder(string? pageTemplateWebPartsXml, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(pageTemplateWebPartsXml))
        {
            return [];
        }

        return Load(pageTemplateWebPartsXml, $"page template {context ?? "(unnamed)"}")
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
    /// <param name="context">Names the page (for example its alias path) in the error message when the XML is invalid.</param>
    /// <exception cref="InvalidDataException">The XML is malformed or contains a DTD.</exception>
    public static IReadOnlyList<EditableRegion> Parse(string? documentContent, IReadOnlyList<string> templateOrder, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(documentContent))
        {
            return [];
        }

        var inDocument = Load(documentContent, $"document content of {context ?? "(unnamed)"}")
            .Descendants("webpart")
            .Select(w => new EditableRegion((string?)w.Attribute("id") ?? string.Empty, w.Value))
            .Where(r => r.Id.Length > 0)
            .ToList();

        // Index-based, so identical regions are all kept.
        var taken = new bool[inDocument.Count];
        var ordered = new List<EditableRegion>(inDocument.Count);
        foreach (var controlId in templateOrder)
        {
            for (var i = 0; i < inDocument.Count; i++)
            {
                if (!taken[i] && ControlIdOf(inDocument[i].Id).Equals(controlId, StringComparison.OrdinalIgnoreCase))
                {
                    taken[i] = true;
                    ordered.Add(inDocument[i]);
                    break;
                }
            }
        }

        for (var i = 0; i < inDocument.Count; i++)
        {
            if (!taken[i])
            {
                ordered.Add(inDocument[i]);
            }
        }

        return ordered;
    }

    private static XElement Load(string xml, string what)
    {
        try
        {
            return SafeXml.Parse(xml).Root!;
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"Cannot parse {what}: {ex.Message}", ex);
        }
    }

    private static string ControlIdOf(string regionId)
    {
        var separator = regionId.IndexOf(';');
        return separator < 0 ? regionId : regionId[..separator];
    }
}
