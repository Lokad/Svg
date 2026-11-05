using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCssSelectorIdentifierTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("1")]
    [InlineData(".1")]
    [InlineData("#1")]
    [InlineData("-1")]
    [InlineData(".-1")]
    [InlineData("#-1")]
    [InlineData("-")]
    [InlineData(".-")]
    [InlineData("#-")]
    [InlineData("[id=1]")]
    [InlineData("[class=-1]")]
    [InlineData("rect[class=-]")]
    [InlineData("rect.1")]
    [InlineData("rect:hover.1")]
    [InlineData("rect[id=-1]")]
    public void Invalid_identifier_starts_do_not_become_selector_tokens(string selector)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgCssSelectorSyntax.Specificity(selector, CancellationToken.None)).ErrorCode);

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(".a1", "", "a1")]
    [InlineData("._1", "", "_1")]
    [InlineData(".-a", "", "-a")]
    [InlineData(".--1", "", "--1")]
    [InlineData("[id='1']", "1", "")]
    [InlineData("[class=\"-1\"]", "", "-1")]
    [InlineData("[id='-']", "-", "")]
    [InlineData("[class=a1]", "", "a1")]
    public void Identifier_tokens_and_quoted_attribute_strings_keep_their_distinct_starts(string selector, string id, string classes)
    {
        Assert.Equal(new CssSpecificity(0, 1, 0), SvgCssSelectorSyntax.Specificity(selector, CancellationToken.None));
        Assert.True(SvgCssSelectorSyntax.Matches(selector, new("rect", id, classes, true), CancellationToken.None));
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData(".1")]
    [InlineData(".1:hover")]
    [InlineData("rect,[id=1]")]
    public void Admission_validates_active_inactive_and_list_names_before_publishing_output(string selector)
    {
        byte[] input = Document(selector), unchanged = input.ToArray();
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using MemoryStream destination = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, destination.Length);
        using (SvgRoot retry = SvgRootParser.Parse(Document("[id='1']"), SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.Single(retry.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);

        static byte[] Document(string selector) => TextEncoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><style>" + selector
            + "{fill:red}</style><rect id='1' class='1' width='8' height='8'/></svg>");
    }
}
