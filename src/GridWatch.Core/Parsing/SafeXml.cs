using System.Xml;
using System.Xml.Linq;

namespace GridWatch.Core.Parsing;

internal static class SafeXml
{
    public static readonly XNamespace Ieso = "http://www.ieso.ca/schema";

    /// <summary>
    /// Loads XML with DTD processing prohibited and no external resolver, which blocks
    /// XML external entity (XXE) and entity-expansion attacks from a tampered file.
    /// </summary>
    public static XDocument Load(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        try
        {
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new IesoFormatException($"Report is not valid XML: {ex.Message}");
        }
    }

    public static XElement Body(XDocument doc, string expectedDocId)
    {
        var root = doc.Root ?? throw new IesoFormatException("Report has no root element.");
        var docId = (string?)root.Attribute("docID");
        if (!string.Equals(docId, expectedDocId, StringComparison.Ordinal))
        {
            throw new IesoFormatException($"Expected a {expectedDocId} report but got '{docId ?? "unknown"}'.");
        }

        return root.Element(Ieso + "DocBody") ?? throw new IesoFormatException("Report has no DocBody.");
    }
}
