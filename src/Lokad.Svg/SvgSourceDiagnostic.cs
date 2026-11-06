namespace Lokad.Svg;

/// <summary>Identifies the source construct associated with one conversion failure.</summary>
/// <remarks>Strings are limited to 256 UTF-16 code units without splitting surrogate pairs.
/// Empty strings and null positions mean unavailable context. Positions are one-based XML
/// positions, not normalized text offsets.</remarks>
public sealed class SvgSourceDiagnostic
{
    /// <summary>Initializes bounded source context without a local-reference instance.</summary>
    public SvgSourceDiagnostic(string elementName, string elementId, string attributeName, string attributeValue,
        int? lineNumber, int? linePosition)
        : this(elementName, elementId, attributeName, attributeValue, lineNumber, linePosition, string.Empty)
    {
    }

    /// <summary>Initializes bounded source context including the referencing instance ID when available.</summary>
    public SvgSourceDiagnostic(string elementName, string elementId, string attributeName, string attributeValue,
        int? lineNumber, int? linePosition, string instanceElementId)
    {
        if (lineNumber <= 0) throw new ArgumentOutOfRangeException(nameof(lineNumber));
        if (linePosition <= 0) throw new ArgumentOutOfRangeException(nameof(linePosition));
        ElementName = SvgDiagnosticText.Bound(elementName); ElementId = SvgDiagnosticText.Bound(elementId);
        AttributeName = SvgDiagnosticText.Bound(attributeName); AttributeValue = SvgDiagnosticText.Bound(attributeValue);
        LineNumber = lineNumber; LinePosition = linePosition;
        InstanceElementId = SvgDiagnosticText.Bound(instanceElementId);
    }

    public string ElementName { get; }
    public string ElementId { get; }
    /// <summary>Gets the attribute or CSS property name, or an empty string when unavailable.</summary>
    public string AttributeName { get; }
    public string AttributeValue { get; }
    public int? LineNumber { get; }
    public int? LinePosition { get; }
    public string InstanceElementId { get; }
}
