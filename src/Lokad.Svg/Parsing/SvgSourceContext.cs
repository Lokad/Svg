using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Retains a bounded source location without retaining a reader or the input document.</summary>
internal readonly record struct SvgSourceContext(string ElementName, string ElementId, int? LineNumber, int? LinePosition)
{
    internal static SvgSourceContext Capture(XmlReader reader)
    {
        (int? line, int? position) = Position(reader);
        string name = reader.LocalName;
        string id = reader.GetAttribute("id") ?? string.Empty;
        return new(name.Length <= 256 ? name : name[..256], id.Length <= 256 ? id : id[..256], line, position);
    }

    internal SvgSourceDiagnostic Describe(string attribute, string value)
        => new(ElementName, ElementId, attribute, value, LineNumber, LinePosition);

    internal static void Attach(XmlReader reader, SvgException exception)
    {
        if (exception.SourceDiagnostic is not null) return;
        string attribute = reader.NodeType == XmlNodeType.Attribute ? reader.Name : string.Empty;
        string value = reader.NodeType == XmlNodeType.Attribute ? reader.Value : string.Empty;
        (int? line, int? position) = Position(reader);
        reader.MoveToElement();
        SvgSourceContext source = Capture(reader);
        exception.AttachSource(new(source.ElementName, source.ElementId, attribute, value, line, position));
    }

    internal static void Attach(XmlReader reader, SvgException exception, string attribute, string value)
    {
        if (exception.SourceDiagnostic is not null) return;
        SvgSourceContext source = Capture(reader);
        bool moved = reader.GetAttribute(attribute) == value && reader.MoveToAttribute(attribute);
        (int? line, int? position) = Position(reader);
        if (moved) reader.MoveToElement();
        exception.AttachSource(new(source.ElementName, source.ElementId, attribute, value, line, position));
    }

    private static (int? Line, int? Position) Position(XmlReader reader)
        => reader is IXmlLineInfo info && info.HasLineInfo() ? (info.LineNumber, info.LinePosition) : (null, null);
}
