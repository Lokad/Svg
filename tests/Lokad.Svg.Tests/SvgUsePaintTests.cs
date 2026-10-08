using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUsePaintTests
{
    [Fact]
    public void Use_paint_context_and_source_stroke_roles_preserve_distinct_alphas_and_release_owners()
    {
        byte[] input = Input("<defs><rect id='p' x='4' y='4' width='12' height='10' fill='currentColor' stroke='currentColor' stroke-width='2' stroke-opacity='.625'/></defs><use href='#p' color='#297fb3' fill-opacity='.375'/>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgGeometry geometry = Assert.Single(scene.Geometries);
            SvgRecordedPaintValue fill = scene.SceneStorage?.RecordedPaintValues[geometry.RecordedPaint.Identity - 1] ?? throw new InvalidOperationException();
            SvgRecordedPaintValue stroke = scene.SceneStorage?.RecordedPaintValues[geometry.RecordedStrokePaint.Identity - 1] ?? throw new InvalidOperationException();
            Assert.False(fill.IsStroke);
            Assert.True(stroke.IsStroke);
            Assert.Equal(.375f, fill.Alpha);
            Assert.Equal(.625f, stroke.Alpha);
            Assert.Equal(new SvgColor(41, 127, 179, 96), geometry.Rectangle.Color);
            Assert.Equal(new SvgColor(41, 127, 179, 159), Assert.IsType<SvgStroke>(geometry.Rectangle.Stroke).Color);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
    }

    [Theory]
    [InlineData("color='rgb(NaN,0,0)'")]
    [InlineData("fill-opacity='NaN'")]
    [InlineData("stroke-opacity='NaN'")]
    public void Invalid_use_paint_values_are_malformed_and_release_parser_owners(string attributes)
    {
        byte[] input = Input("<defs><rect id='p' width='12' height='10'/></defs><use href='#p' " + attributes + "/>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='24'>" + content + "</svg>");
}
