using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgLocalUrlTests
{
    [Theory]
    [InlineData("url(#a)", true, "a")]
    [InlineData("url(\"#a\")", true, "a")]
    [InlineData("url('#a')", true, "a")]
    [InlineData("url( #a )", true, "a")]
    [InlineData("url( \"#a\" )", true, "a")]
    [InlineData("url(#a-b_c.1)", true, "a-b_c.1")]
    [InlineData("url()", false, "")]
    [InlineData("url(#)", false, "")]
    [InlineData("url(#a", false, "")]
    [InlineData("#a", false, "")]
    [InlineData("url(http://example.com/#a)", false, "")]
    [InlineData("url(#a\"b)", false, "")]
    [InlineData("url(\"#a)", false, "")]
    [InlineData("url(#a'b)", false, "")]
    [InlineData("none", false, "")]
    public void Parses_quoted_and_spaced_local_references(string value, bool expected, string expectedId)
    {
        Assert.Equal(expected, SvgLocalUrl.TryParseId(value, out string id));
        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void Rejects_overlong_reference_identifiers()
    {
        string value = "url(#" + new string('a', SvgSyntaxLimits.MaximumIdentifierCharacters + 1) + ")";

        Assert.False(SvgLocalUrl.TryParseId(value, out _));
    }
}
