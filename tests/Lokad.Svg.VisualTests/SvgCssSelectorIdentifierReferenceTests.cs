namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgCssSelectorIdentifierReferenceTests
{
    [Theory]
    [InlineData("quoted-digit-attribute")]
    [InlineData("double-hyphen-class")]
    [InlineData("quoted-id-reference")]
    public void Numeric_attribute_strings_and_valid_hyphen_names_keep_native_paint(string identity)
        => SvgNativeControlAssertions.MatchesCanonicalConversion("css-selector-identifiers", identity, 12);

    [Fact]
    public void A_complete_commented_selector_list_keeps_native_paint()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("css-selector-identifiers", "list-valid-comment", 12);
}
