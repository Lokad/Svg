using Lokad.Svg.Fonts;

namespace Lokad.Svg.Parsing;

/// <summary>Owns normalized continuation text until a local path supplies its initial current position.</summary>
/// <remarks>The queued record owns the aggregate metadata allowance; this owner adopts the extracted
/// layout/paint/font arrays and borrows the caller snapshot. Only the parser thread reads or disposes it.
/// Disposal releases material arrays and references unconditionally, including after failed construction.</remarks>
internal sealed class SvgPendingTextLayout : IDisposable
{
    private SvgExtractedTextRun _source;
    private SvgFontSnapshot? _snapshot;
    internal SvgPendingTextLayout(SvgExtractedTextRun source, SvgTextStyle style, SvgTextGlyphPaint paint,
        SvgGlyphDeviceMetrics? metrics, SvgFontSnapshot snapshot)
    { _source = source; Style = style; Paint = paint; Metrics = metrics; _snapshot = snapshot; }
    internal SvgExtractedTextRun Source => _snapshot is null ? throw new ObjectDisposedException(nameof(SvgPendingTextLayout)) : _source;
    internal SvgFontSnapshot Snapshot => _snapshot ?? throw new ObjectDisposedException(nameof(SvgPendingTextLayout));
    internal SvgTextStyle Style { get; }
    internal SvgTextGlyphPaint Paint { get; }
    internal SvgGlyphDeviceMetrics? Metrics { get; }
    public void Dispose() { _source.Dispose(); _source = default; _snapshot = null; }
}
