using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUsePaintTests
{
    [Theory]
    [InlineData("direct")]
    [InlineData("forward")]
    [InlineData("symbol")]
    [InlineData("group")]
    [InlineData("pattern")]
    [InlineData("mask")]
    public void Inline_use_style_preserves_presentation_across_replay_contexts(string placement)
    {
        const string attributes = "color='#0755a0' fill-opacity='.625' stroke='#ed6817' stroke-width='1px' opacity='.75' paint-order='stroke fill'";
        const string style = "style='color:#0755a0;fill-opacity:.625;stroke:#ed6817;stroke-width:1px;opacity:.75;paint-order:stroke fill'";
        Assert.Equal(Render(Content(attributes)), Render(Content(style)));

        string Content(string declarations)
        {
            const string source = "<defs><rect id='p' x='2' y='2' width='8' height='8' fill='currentColor'/></defs>";
            const string sheet = "<style>use{color:blue!important}</style>";
            string use = "<use href='#p' " + declarations + "/>";
            return sheet + (placement switch
            {
                "direct" => source + use,
                "forward" => use + source,
                "symbol" => source + "<symbol id='s'>" + use + "</symbol><use href='#s'/>",
                "group" => source + "<g id='g'>" + use + "</g><use href='#g' x='12'/>",
                "pattern" => source + "<defs><pattern id='t' width='12' height='12' patternUnits='userSpaceOnUse'>" + use + "</pattern></defs><rect width='32' height='24' fill='url(#t)'/>",
                "mask" => source + "<defs><mask id='m' mask-type='alpha'>" + use + "</mask></defs><rect width='32' height='24' fill='red' mask='url(#m)'/>",
                _ => throw new ArgumentOutOfRangeException(nameof(placement))
            });
        }
    }

    [Theory]
    [InlineData("font-size:8px", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-family:Example", SvgErrorCode.UnsupportedFeature)]
    [InlineData("white-space:normal", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stop-color:red", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stop-opacity:.5", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stop-opacity:NaN", SvgErrorCode.MalformedInput)]
    [InlineData("fill:red;fill:blue!important!important", SvgErrorCode.UnsupportedFeature)]
    public void Inline_use_style_keeps_existing_admission_errors(string declarations, SvgErrorCode expected)
    {
        foreach (string body in new[]
        {
            "<use href='#p' style='" + declarations + "'/>",
            "<symbol id='s'><use href='#p' style='" + declarations + "'/></symbol><use href='#s'/>",
            "<defs><pattern id='t' width='8' height='8' patternUnits='userSpaceOnUse'><use href='#p' style='" + declarations + "'/></pattern></defs>"
        })
        {
            RenderBudget budget = new(1_000_000);
            SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input("<defs><rect id='p' width='8' height='8'/></defs>" + body), SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(expected, error.ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

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

    private static byte[] Render(string content) => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
