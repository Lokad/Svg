namespace Lokad.Svg;

/// <summary>Represents a categorized SVG conversion failure.</summary>
public sealed class SvgException : Exception
{
    /// <summary>Initializes a categorized conversion failure.</summary>
    public SvgException(SvgErrorCode errorCode, string message)
        : this(errorCode, message, null)
    {
    }

    /// <summary>Initializes a categorized conversion failure with bounded font context when available.</summary>
    public SvgException(SvgErrorCode errorCode, string message, Fonts.SvgFontDiagnostic? fontDiagnostic)
        : this(errorCode, message, fontDiagnostic, null)
    {
    }

    /// <summary>Initializes a categorized failure with bounded font and SVG source context when available.</summary>
    public SvgException(SvgErrorCode errorCode, string message, Fonts.SvgFontDiagnostic? fontDiagnostic, SvgSourceDiagnostic? sourceDiagnostic)
        : base(message)
    {
        ErrorCode = errorCode;
        FontDiagnostic = fontDiagnostic;
        SourceDiagnostic = sourceDiagnostic;
    }

    /// <summary>Gets the stable failure category.</summary>
    public SvgErrorCode ErrorCode { get; }

    /// <summary>Gets bounded font context for a font-related failure, or <see langword="null"/> when unavailable.</summary>
    public Fonts.SvgFontDiagnostic? FontDiagnostic { get; }

    /// <summary>Gets bounded source context captured at the failure or retained through deferred work.</summary>
    public SvgSourceDiagnostic? SourceDiagnostic { get; private set; }

    internal void AttachSource(SvgSourceDiagnostic source) => SourceDiagnostic ??= source;

    internal void AttachInstance(string elementId)
    {
        if (SourceDiagnostic is {} source && source.InstanceElementId.Length == 0 && elementId.Length != 0)
            SourceDiagnostic = new(source.ElementName, source.ElementId, source.AttributeName, source.AttributeValue,
                source.LineNumber, source.LinePosition, elementId);
    }
}
