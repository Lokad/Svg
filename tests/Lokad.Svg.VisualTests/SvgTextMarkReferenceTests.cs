namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgTextMarkReferenceTests
{
    [Fact]
    public Task Canonical_marks_preserve_ligatures_contextual_advances_default_ignorables_and_fitting()
        => NativeFontFixture.AssertMatches("text-mark-shaping", "text-mark-shaping", "Example");

    [Fact]
    public Task Canonical_glyph_paths_preserve_translucency_shaders_and_clipping()
        => NativeFontFixture.AssertMatches("text-canonical-path", "text-canonical-path", "Example");
}
