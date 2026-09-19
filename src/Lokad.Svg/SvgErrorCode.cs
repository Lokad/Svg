namespace Lokad.Svg;

/// <summary>Identifies a stable category of SVG conversion failure.</summary>
public enum SvgErrorCode
{
    /// <summary>The supplied SVG or an embedded resource is malformed.</summary>
    MalformedInput,
    /// <summary>A visible SVG behavior is not supported by this release.</summary>
    UnsupportedFeature,
    /// <summary>A required resource or font could not be resolved.</summary>
    UnresolvedResource,
    /// <summary>A configured structural, work, dimension, or memory limit was exceeded.</summary>
    LimitExceeded,
    /// <summary>The caller-provided PNG destination is too small.</summary>
    InsufficientDestination
}
