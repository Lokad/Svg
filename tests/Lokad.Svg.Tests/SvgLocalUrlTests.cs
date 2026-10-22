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
    [InlineData("url('#a ')", true, "a")]
    [InlineData("url(#a )", true, "a")]
    [InlineData("url(#\u2003a)", true, "\u2003a")]
    [InlineData("url('# a')", false, "")]
    [InlineData("url(' #a')", false, "")]
    [InlineData("url(# a)", false, "")]
    [InlineData("url(#a b)", false, "")]
    [InlineData("url('#a\tb')", false, "")]
    [InlineData("url(#a-b_c.1)", true, "a-b_c.1")]
    [InlineData("url(#%73)", true, "s")]
    [InlineData("url(#%2573)", true, "%73")]
    [InlineData("url('#%C3%A9')", true, "\u00e9")]
    [InlineData("url(#%f0%9f%9a%80)", true, "\ud83d\ude80")]
    [InlineData("url(#%65%CC%81)", true, "e\u0301")]
    [InlineData("url(#a%27b)", true, "a'b")]
    [InlineData("url(#%23s)", true, "#s")]
    [InlineData("url(#%25GG)", true, "%GG")]
    [InlineData("url(#%)", false, "")]
    [InlineData("url(#%7)", false, "")]
    [InlineData("url(#%GG)", false, "")]
    [InlineData("url(#%C3%28)", false, "")]
    [InlineData("url(#%ED%A0%80)", false, "")]
    [InlineData("url(#%C0%AF)", false, "")]
    [InlineData("url(#%F4%90%80%80)", false, "")]
    [InlineData("url(#%00)", false, "")]
    [InlineData("url(#%20s)", false, "")]
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

    [Fact]
    public void Fragment_decoding_keeps_authored_bounds_and_budgeted_string_ownership()
    {
        string fragment = "%61" + new string('a', SvgSyntaxLimits.MaximumIdentifierCharacters - 3);
        Assert.True(SvgLocalUrl.TryParseId("url(#" + fragment + ")", out string decoded));
        Assert.Equal(new string('a', SvgSyntaxLimits.MaximumIdentifierCharacters - 2), decoded);
        string oversized = fragment + "a";
        Assert.False(SvgLocalUrl.TryParseId("url(#" + oversized + ")", out _));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgLocalUrl.ParseFragment(oversized)).ErrorCode);
        Lokad.Svg.Rendering.RenderBudget budget = new(1_000_000);
        using (BudgetedParserStringStore strings = new(budget)) Assert.Equal(decoded, SvgLocalUrl.CopyFragment(fragment, strings));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Lokad.Svg.Rendering.RenderBudget denied = new(1);
        using (BudgetedParserStringStore strings = new(denied)) Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgLocalUrl.CopyFragment(fragment, strings)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }
}
