namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgMaskSourceClipReferenceTests
{
    [Fact]
    public void Hairline_uses_binary_dispatch_inside_a_fully_opaque_mask_bitmap()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("mask-source-clip", "opaque-mask-bitmap", 1);
}
