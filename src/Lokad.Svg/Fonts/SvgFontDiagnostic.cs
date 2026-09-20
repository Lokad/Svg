namespace Lokad.Svg.Fonts;

/// <summary>Identifies the bounded stage at which an explicitly supplied font could not be used.</summary>
public enum SvgFontFailureStage
{
    /// <summary>The selected program was unavailable from the caller's provider.</summary>
    Source,
    /// <summary>The selected program failed declared integrity validation.</summary>
    Integrity,
    /// <summary>The selected program used an unsupported or malformed font format.</summary>
    Format,
    /// <summary>A selected program could not supply a requested glyph.</summary>
    Glyph
}

/// <summary>
/// Provides bounded font-selection context for an SVG failure. It never includes
/// font bytes or source text. Empty values mean that the corresponding context was
/// unavailable at the reporting boundary.
/// </summary>
public sealed class SvgFontDiagnostic
{
    private const int MaximumValueLength = 256;

    /// <summary>Initializes one bounded font diagnostic.</summary>
    public SvgFontDiagnostic(SvgFontFailureStage stage, string requestedFamily, string resolvedFamily, string faceIdentity, string sourceElementId)
    {
        RequestedFamily = Validate(requestedFamily, nameof(requestedFamily));
        ResolvedFamily = Validate(resolvedFamily, nameof(resolvedFamily));
        FaceIdentity = Validate(faceIdentity, nameof(faceIdentity));
        SourceElementId = Validate(sourceElementId, nameof(sourceElementId));
        Stage = stage;
    }

    /// <summary>Gets the bounded stage at which use failed.</summary>
    public SvgFontFailureStage Stage { get; }
    /// <summary>Gets the requested CSS-family context when known.</summary>
    public string RequestedFamily { get; }
    /// <summary>Gets the resolved manifest-family context when known.</summary>
    public string ResolvedFamily { get; }
    /// <summary>Gets a bounded face identity, never font bytes.</summary>
    public string FaceIdentity { get; }
    /// <summary>Gets the source element ID when retained by a future text parser.</summary>
    public string SourceElementId { get; }

    private static string Validate(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumValueLength) throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}
