namespace Lokad.Svg;

/// <summary>Represents a categorized SVG conversion failure.</summary>
public sealed class SvgException : Exception
{
    /// <summary>Initializes a categorized conversion failure.</summary>
    public SvgException(SvgErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets the stable failure category.</summary>
    public SvgErrorCode ErrorCode { get; }
}
