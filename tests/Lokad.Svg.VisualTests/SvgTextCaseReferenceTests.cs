namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgTextCaseReferenceTests
{
    [Theory]
    [InlineData("basic")]
    [InlineData("path")]
    [InlineData("indexed")]
    public Task Uppercase_uses_emitted_text_and_original_svg_position_addresses(string identity)
        => NativeFontFixture.AssertMatches("text-case-mapping/" + identity, "text-case-" + identity, "Example");

    [Theory]
    [InlineData("lowercase")]
    [InlineData("lowercase-path")]
    public Task Lowercase_keeps_original_context_across_spans_paths_and_text_boundaries(string identity)
        => NativeFontFixture.AssertMatches("text-case-mapping/" + identity, "text-case-" + identity, "Example");

    [Theory]
    [InlineData("capitalize")]
    [InlineData("capitalize-path")]
    [InlineData("capitalize-indexed")]
    [InlineData("capitalize-words")]
    public Task Capitalization_uses_full_titlecase_and_original_word_context(string identity)
        => NativeFontFixture.AssertMatches("text-case-mapping/" + identity, "text-case-" + identity, "Example");

    [Theory]
    [InlineData("language-simple")]
    [InlineData("language-nodes")]
    [InlineData("language-path")]
    [InlineData("language-indexed")]
    public Task Language_casing_preserves_original_context_positions_and_empty_owners(string identity)
        => NativeFontFixture.AssertMatches("text-case-mapping/" + identity, "text-case-" + identity, "Example");
}
