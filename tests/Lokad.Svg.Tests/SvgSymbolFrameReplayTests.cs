using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSymbolFrameReplayTests
{
    [Theory]
    [InlineData("symbol-card-diagram")]
    [InlineData("fractional-authored")]
    [InlineData("nonuniform-symbol-frame")]
    [InlineData("collapsed-clipped-frame")]
    public void Native_symbol_frames_preserve_source_and_release_material_storage_after_denial_and_retry(string identity)
    {
        byte[] input = Input(identity);
        byte[] unchanged = input.ToArray();
        SvgRenderOptions options = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget measured = new(8_000_000);
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new(Math.Max(1, measured.Snapshot().PeakBytes - 4_096));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, tight, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            Assert.Equal(expected, surface.Pixels);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Fact]
    public void Forward_fractional_symbol_frame_preserves_declared_order_and_pixels()
    {
        byte[] backward = Input("fractional-authored");
        string text = System.Text.Encoding.UTF8.GetString(backward);
        int start = text.IndexOf("<defs>", StringComparison.Ordinal);
        int end = text.IndexOf("</defs>", StringComparison.Ordinal) + "</defs>".Length;
        string definitions = text[start..end];
        string forward = text.Remove(start, end - start);
        forward = forward.Insert(forward.LastIndexOf("</svg>", StringComparison.Ordinal), definitions);
        Assert.Equal(SvgConverter.ConvertToPng(backward, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(forward), SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Input(string identity) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "symbol-fractional-frames", identity + ".svg"));
}
