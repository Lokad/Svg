namespace Lokad.Svg.VisualTests;

public sealed class SvgLegacyFontDeclarationReferenceTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Dormant_legacy_font_definitions_keep_the_explicit_host_program()
        => NativeFontFixture.AssertMatches("legacy-font-declarations", "original-legacy-declarations", "ProbeTiny");
}
