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
        : base(message)
    {
        ErrorCode = errorCode;
        FontDiagnostic = fontDiagnostic;
    }

    /// <summary>Gets the stable failure category.</summary>
    public SvgErrorCode ErrorCode { get; }

    /// <summary>Gets bounded font context for a font-related failure, or <see langword="null"/> when unavailable.</summary>
    public Fonts.SvgFontDiagnostic? FontDiagnostic { get; }
}
