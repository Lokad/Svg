using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgInertPresentationTests
{
    private const string Shape = "<path fill='red' d='M4 4 C4 1 28 1 28 4 L28 24 C28 29 4 29 4 24 Z'/>";
    private const string Declarations = "marker:none;marker-start:none;marker-mid:none;marker-end:none;enable-background:accumulate";

    [Theory]
    [InlineData("inline")]
    [InlineData("root")]
    [InlineData("group")]
    [InlineData("stylesheet")]
    [InlineData("use")]
    [InlineData("attributes")]
    [InlineData("important")]
    [InlineData("duplicate")]
    [InlineData("visible-inline")]
    [InlineData("visible-group")]
    [InlineData("visible-stylesheet")]
    [InlineData("visible-attributes")]
    public async Task Inactive_presentation_preserves_pixels_through_six_public_outputs(string context)
    {
        byte[] input = Input(context);
        SvgRenderOptions options = SvgRenderOptions.Default;
        byte[] expected = SvgConverter.ConvertToPng(Wrap(Shape), options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData("marker", "url(#arrow)")]
    [InlineData("marker-start", "url(#arrow)")]
    [InlineData("marker-mid", "url(#arrow)")]
    [InlineData("marker-end", "url(#arrow)")]
    [InlineData("marker", "inherit")]
    [InlineData("marker-start", "auto")]
    [InlineData("marker-mid", "none extra")]
    [InlineData("marker-end", "none !important !important")]
    [InlineData("marker-later", "none")]
    [InlineData("enable-background", "new")]
    [InlineData("enable-background", "new 0 0 32 32")]
    [InlineData("enable-background", "accumulate 0 0 32 32")]
    [InlineData("enable-background", "inherit")]
    public void Active_or_unimplemented_values_reject_in_attributes_inline_and_stylesheets(string property, string value)
    {
        string declaration = property + ":" + value;
        foreach (string body in new[]
        {
            Shape.Replace("<path ", "<path style='" + declaration + "' "),
            "<style>.paint{" + declaration + "}</style>" + Shape.Replace("<path ", "<path class='paint' "),
            Shape.Replace("<path ", "<path " + property + "='" + value + "' "),
        })
        {
            RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
            SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Wrap(body), SvgRenderLimits.Default,
                budget, null, new(SvgRenderLimits.Default.MaximumWorkUnits), CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("marker:none;marker-start:url(#arrow)")]
    [InlineData("marker-start:url(#arrow);marker-start:none")]
    [InlineData("marker-start:none !important;marker-start:url(#arrow)")]
    public void Unsupported_marker_values_cannot_hide_behind_shorthand_or_priority(string declarations)
    {
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(
            Wrap(Shape.Replace("<path ", "<path style='" + declarations + "' ")),
            SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    [Theory]
    [InlineData("inline")]
    [InlineData("stylesheet")]
    [InlineData("attributes")]
    public void Inactive_presentation_observes_work_memory_cancellation_and_retry(string context)
    {
        byte[] input = Input(context);
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget normal = new(limits.MaximumAccountedBytes);
        long work;
        using (SvgRoot root = SvgRootParser.Parse(input, limits, normal, null, new(limits.MaximumWorkUnits), CancellationToken.None)) work = root.ParserWorkUnits;
        Assert.Equal(0, normal.Snapshot().CurrentBytes);
        RenderBudget workBudget = new(limits.MaximumAccountedBytes);
        SvgException workDenied = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, workBudget, null, new(work - 1), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, workDenied.ErrorCode);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
        RenderBudget memoryBudget = new(normal.Snapshot().PeakBytes - 1);
        SvgException memoryDenied = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, memoryBudget, null, new(limits.MaximumWorkUnits), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, memoryDenied.ErrorCode);
        Assert.Equal(0, memoryBudget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException cancelled = Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, limits, workBudget, null, new(limits.MaximumWorkUnits), cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(input, limits, workBudget, null, new(limits.MaximumWorkUnits), CancellationToken.None)) Assert.Single(retry.Geometries);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Font_discovery_accepts_inactive_style_and_attribute_values()
    {
        Assert.Empty(SvgFontRequestDiscovery.Discover(Input("inline"), CancellationToken.None));
        Assert.Empty(SvgFontRequestDiscovery.Discover(Input("attributes"), CancellationToken.None));
        Assert.Empty(SvgFontRequestDiscovery.Discover(Input("visible-group"), CancellationToken.None));
    }

    [Theory]
    [InlineData("<svg width='16' height='16' style='overflow:visible'><rect width='4' height='4'/></svg>")]
    [InlineData("<svg width='16' height='16' overflow='visible'><rect width='4' height='4'/></svg>")]
    [InlineData("<style>.viewport{overflow:visible}</style><svg class='viewport' width='16' height='16'><rect width='4' height='4'/></svg>")]
    public void Visible_overflow_stays_unsupported_on_nested_viewports(string body)
    {
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(Wrap(body), SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    [Theory]
    [InlineData("style='overflow:visible'")]
    [InlineData("overflow='visible'")]
    public void Visible_root_overflow_stays_unsupported_in_conversion_and_font_discovery(string attributes)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' " + attributes + ">" + Shape + "</svg>");
        SvgException conversion = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));
        SvgException discovery = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, conversion.ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, discovery.ErrorCode);
    }

    private static byte[] Input(string context) => context switch
    {
        "inline" => Wrap(Shape.Replace("<path ", "<path style='" + Declarations + "' ")),
        "root" => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' style='" + Declarations + "'>" + Shape + "</svg>"),
        "group" => Wrap("<g style='" + Declarations + "'>" + Shape + "</g>"),
        "stylesheet" => Wrap("<style>.paint{" + Declarations + "}</style>" + Shape.Replace("<path ", "<path class='paint' ")),
        "use" => Wrap("<defs>" + Shape.Replace("<path ", "<path id='p' ") + "</defs><use href='#p' style='" + Declarations + "'/>"),
        "attributes" => Wrap("<g marker='none' enable-background='accumulate'>" + Shape.Replace("<path ", "<path marker-start='none' marker-mid='none' marker-end='none' ") + "</g>"),
        "important" => Wrap(Shape.Replace("<path ", "<path style='MARKER:NONE !important;marker-start:none ! important;enable-background:ACCUMULATE !IMPORTANT' ")),
        "duplicate" => Wrap(Shape.Replace("<path ", "<path style='marker:none;marker-start:none;marker-start:none !important;marker:none;enable-background:accumulate' ")),
        "visible-inline" => Wrap(Shape.Replace("<path ", "<path style='overflow:visible' ")),
        "visible-group" => Wrap("<g style='overflow:visible !important'>" + Shape + "</g>"),
        "visible-stylesheet" => Wrap("<style>.paint{overflow:visible !important}</style>" + Shape.Replace("<path ", "<path class='paint' ")),
        "visible-attributes" => Wrap("<g overflow='visible'>" + Shape.Replace("<path ", "<path overflow='visible' ") + "</g>"),
        _ => throw new ArgumentException("Unknown inactive presentation context.", nameof(context)),
    };

    private static byte[] Wrap(string body) => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
}
