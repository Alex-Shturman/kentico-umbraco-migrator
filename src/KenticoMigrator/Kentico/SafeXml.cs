using System.Xml;
using System.Xml.Linq;

namespace KenticoMigrator.Kentico;

/// <summary>Parses XML that comes from the database: no DTDs, no external entities.</summary>
internal static class SafeXml
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>Parses a document; throws <see cref="XmlException"/> on malformed input or a DTD.</summary>
    public static XDocument Parse(string xml)
    {
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, Settings);
        return XDocument.Load(reader);
    }
}
