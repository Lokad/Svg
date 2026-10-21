using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientRegistrationTests
{
    [Theory]
    [InlineData("linearGradient", "linearGradient")]
    [InlineData("linearGradient", "radialGradient")]
    [InlineData("radialGradient", "linearGradient")]
    [InlineData("radialGradient", "radialGradient")]
    public void Gradient_ids_are_unique_across_kinds_pending_aliases_and_defs_blocks(string firstKind, string secondKind)
    {
        foreach (bool firstPending in new[] { false, true })
        foreach (bool secondPending in new[] { false, true })
        foreach (bool split in new[] { false, true })
        {
            string first = Definition(firstKind, firstPending), second = Definition(secondKind, secondPending);
            string separator = split ? "</defs><defs>" : string.Empty;
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs>" + first + separator + second + "</defs><rect width='4' height='4' fill='url(#g)'/></svg>");
            RenderBudget budget = new(1_000_000);
            SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
            Assert.Equal("SVG gradient ids must be unique.", error.Message);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        }

        static string Definition(string kind, bool pending) => pending
            ? "<" + kind + " id='g' href='#later'/>"
            : "<" + kind + " id='g'><stop stop-color='red'/><stop offset='1' stop-color='red'/></" + kind + ">";
    }

    [Theory]
    [InlineData("linearGradient", "radialGradient")]
    [InlineData("radialGradient", "linearGradient")]
    public void Distinct_ids_keep_cross_kind_inheritance_with_forward_targets(string aliasKind, string parentKind)
    {
        byte[] control = "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><rect width='4' height='4' fill='red'/></svg>"u8.ToArray();
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs><" + aliasKind + " id='g' href='#later'/></defs><defs><" + parentKind + " id='later'><stop stop-color='red'/><stop offset='1' stop-color='red'/></" + parentKind + "></defs><rect width='4' height='4' fill='url(#g)'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }
}
