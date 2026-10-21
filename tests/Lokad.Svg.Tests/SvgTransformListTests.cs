using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgTransformListTests
{
    [Theory]
    [InlineData(",")]
    [InlineData(", ")]
    [InlineData(" ,")]
    [InlineData(" , \t")]
    [InlineData("\r\n,\t")]
    public void Comma_separators_match_whitespace_in_elements_instances_and_paints(string separator)
    {
        foreach (string target in new[] { "shape", "group", "use", "linear", "radial", "pattern" })
        {
            Assert.Equal(Render(Input(target, "translate(2 2) scale(.8 .9)")), Render(Input(target, "translate(2 2)" + separator + "scale(.8 .9)")));
        }
    }

    [Theory]
    [InlineData("translate(.123 .456)$scale(1.234 .987)$translate(.789 .123)")]
    [InlineData("scale(1.234 .987)$matrix(1 0 0 1 .123 .456)$translate(.789 .123)")]
    [InlineData("translate(.123 .456)$rotate(13 8 8)$scale(1.234 .987)")]
    [InlineData("skewX(13)$translate(.123 .456)$skewY(7)")]
    [InlineData("matrix(1 .2 .3 1 .123 .456)$scale(1.234 .987)")]
    public void Separator_changes_preserve_existing_numeric_composition(string template)
    {
        string control = template.Replace("$", " ", StringComparison.Ordinal);
        string commas = template.Replace("$", ", ", StringComparison.Ordinal);
        Assert.Equal(SvgGeometrySyntax.ParseTransform(control, new(10_000), CancellationToken.None), SvgGeometrySyntax.ParseTransform(commas, new(10_000), CancellationToken.None));
        foreach (string target in new[] { "shape", "linear", "radial", "pattern" })
            Assert.Equal(Render(Input(target, control)), Render(Input(target, commas)));
    }

    [Theory]
    [InlineData(",translate(1)")]
    [InlineData(" ,translate(1)")]
    [InlineData("translate(1),")]
    [InlineData("translate(1), \t")]
    [InlineData("translate(1),,scale(2)")]
    [InlineData("translate(1) , ,scale(2)")]
    [InlineData("translate(1),garbage")]
    [InlineData("translate(1),scale(")]
    public void Malformed_lists_keep_their_error_category(string transform)
    {
        foreach (string target in new[] { "shape", "linear", "radial", "pattern" })
        {
            RenderBudget budget = new(1_000_000);
            SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(target, transform), SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    [InlineData("translate(2)scale(2)")]
    public void Existing_empty_and_adjacent_lists_keep_their_behavior(string transform)
    {
        string control = transform.Replace(")scale", ") scale", StringComparison.Ordinal);
        foreach (string target in new[] { "shape", "linear", "radial", "pattern" })
            Assert.Equal(Render(Input(target, control)), Render(Input(target, transform)));
    }

    [Fact]
    public void Comma_lists_observe_work_memory_cancellation_and_token_limits()
    {
        foreach (string target in new[] { "shape", "linear", "radial", "pattern" })
        {
            byte[] input = Input(target, "translate(2 2),scale(.8 .9)");
            RenderBudget budget = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            RenderBudget denied = new(1);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, cancellation.Token)).CancellationToken);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            byte[] overlong = Input(target, new string(' ', SvgSyntaxLimits.MaximumTransformCharacters) + "translate(2)");
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(overlong, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();

    private static byte[] Input(string target, string transform)
    {
        const string shape = "<rect x='2' y='2' width='8' height='8' fill='red'/>";
        string body = target switch
        {
            "shape" => "<rect x='2' y='2' width='8' height='8' fill='red' transform='" + transform + "'/>",
            "group" => "<g transform='" + transform + "'>" + shape + "</g>",
            "use" => "<defs><rect id='s' x='2' y='2' width='8' height='8' fill='red'/></defs><use href='#s' transform='" + transform + "'/>",
            "linear" or "radial" => "<defs><" + (target == "linear" ? "linearGradient" : "radialGradient") + " id='p' gradientUnits='userSpaceOnUse' " + (target == "linear" ? "x1='0' y1='0' x2='16' y2='0'" : "cx='8' cy='8' r='8'") + " gradientTransform='" + transform + "'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + (target == "linear" ? "linearGradient" : "radialGradient") + "></defs><rect width='16' height='16' fill='url(#p)'/>",
            "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4' patternTransform='" + transform + "'><rect width='2' height='2' fill='red'/></pattern></defs><rect width='16' height='16' fill='url(#p)'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + body + "</svg>");
    }
}
