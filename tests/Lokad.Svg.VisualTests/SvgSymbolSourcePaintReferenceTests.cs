namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgSymbolSourcePaintReferenceTests
{
    [Fact]
    public void Nonuniform_symbol_maps_preserve_retained_fill_stroke_and_paint_frames()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("symbol-source-paints", "nonuniform-paints", 1);
}
