using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgInertTypographyTests
{
    [Theory]
    [InlineData("font-size:0;font-size:22px !important;font-size:100%;font-family:'Missing Family';font-style:italic;font-weight:750;font-stretch:125%;font-variant:small-caps")]
    [InlineData("font-size:1em;line-height:100%;text-align:center;writing-mode:lr-tb;text-anchor:middle;white-space:pre-wrap;letter-spacing:-1px;-inkscape-font-specification:Arial")]
    [InlineData("font-size:initial;font-family:inherit;line-height:normal;letter-spacing:normal;font-stretch:condensed;font-style:oblique")]
    [InlineData("font-family:Arial,serif")]
    public async Task Geometry_typography_preserves_all_six_outputs_and_requires_no_font(string declarations)
    {
        const string plain = "<path fill='red' d='M2 2C2 0 30 0 30 2L30 30H2Z'/>";
        byte[] input = Wrap(plain.Replace("<path ", "<path style=\"" + declarations + "\" ", StringComparison.Ordinal));
        byte[] expected = SvgConverter.ConvertToPng(Wrap(plain), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        byte[] target = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, target, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, target);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, target, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, target);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData("font-size:-1px", SvgErrorCode.MalformedInput)]
    [InlineData("font-size:NaN", SvgErrorCode.MalformedInput)]
    [InlineData("font-size:1e999px", SvgErrorCode.MalformedInput)]
    [InlineData("font-size:var(--size)", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-size:22px !important !important", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-family:Arial,,serif", SvgErrorCode.MalformedInput)]
    [InlineData("font-style:oblique 10deg", SvgErrorCode.UnsupportedFeature)]
    [InlineData("font-weight:1001", SvgErrorCode.MalformedInput)]
    [InlineData("font-stretch:0%", SvgErrorCode.MalformedInput)]
    [InlineData("line-height:-1", SvgErrorCode.MalformedInput)]
    [InlineData("writing-mode:diagonal", SvgErrorCode.UnsupportedFeature)]
    [InlineData("text-anchor:center", SvgErrorCode.UnsupportedFeature)]
    public void Unadmitted_typography_rejects_without_retaining_partial_owners(string declaration, SvgErrorCode code)
    {
        byte[] input = Wrap("<path style='" + declaration + "' d='M2 2H30V30H2Z'/>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(code, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<g style='text-align:center'><path d='M2 2H30V30Z'/></g>")]
    [InlineData("<text style='font-stretch:normal'>x</text>")]
    [InlineData("<defs><symbol id='s' style='text-align:center'/></defs><use href='#s'/>")]
    [InlineData("<defs><path id='p' d='M2 2H30V30Z'/></defs><use href='#p' style='font-stretch:normal'/>")]
    public void Inert_geometry_handling_does_not_hide_text_or_container_semantics(string body)
    {
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(Wrap(body), SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("font-size", "-1px")]
    [InlineData("font-weight", "0")]
    [InlineData("font-stretch", "0%")]
    public void Presentation_attributes_share_scalar_validation(string property, string value)
    {
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(Wrap("<rect width='16' height='16' " + property + "='" + value + "'/>"), SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Typography_scans_charge_work_observe_cancellation_and_release_partial_owners()
    {
        byte[] input = Wrap("<path style='font-family:Missing Family;font-size:22px;text-align:center' d='M2 2H30V30H2Z'/>");
        RenderBudget accepted = new(1_000_000);
        long work;
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, accepted, CancellationToken.None)) work = root.ParserWorkUnits;
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        foreach (long allowed in new[] { 0, 1, work / 2, work - 1 })
        {
            RenderBudget budget = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new ParserWorkCounter(allowed), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SvgInertPresentationSyntax.TryValidateTypography("font-family", "Missing Family", new(1_000), cancellation.Token));
    }

    private static byte[] Wrap(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
}
