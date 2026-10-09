using System.Runtime.CompilerServices;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientStopPrecisionTests
{
    [Theory]
    [InlineData(".27", .27f, 69)]
    [InlineData("27%", .27f, 69)]
    [InlineData(".999", .999f, 255)]
    [InlineData("0", 0f, 0)]
    [InlineData("1", 1f, 255)]
    [InlineData(".414", .414f, 106)]
    public void Parsed_stops_retain_source_alpha_alongside_byte_color(string opacity, float expected, byte quantized)
    {
        using SvgRoot root = Parse("<linearGradient id='p'><stop offset='0' stop-color='#46596d' stop-opacity='" + opacity + "'/><stop offset='1' stop-color='#46596d'/></linearGradient>");
        SvgGradientStop stop = Assert.Single(root.Geometries).Gradient.Gradient.Stops[0];
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(stop.Alpha));
        Assert.Equal(quantized, stop.Color.Alpha);
    }

    [Fact]
    public void Stop_alpha_uses_existing_padding_on_x64()
    {
        if (IntPtr.Size == 8) Assert.Equal(16, Unsafe.SizeOf<SvgGradientStop>());
    }

    [Fact]
    public void Offset_clamping_and_monotonicity_preserve_source_alpha()
    {
        using SvgRoot root = Parse("<linearGradient id='p'><stop offset='-.125' stop-opacity='.27'/><stop offset='.6' stop-opacity='.81'/><stop offset='.4' stop-opacity='.46'/><stop offset='1.25' stop-opacity='.999'/></linearGradient>");
        SvgGradientStop[] stops = Assert.Single(root.Geometries).Gradient.Gradient.Stops;
        Assert.Equal(new[] { 0d, (double).6f, .6f, 1 }, stops.Select(stop => stop.Offset));
        Assert.Equal(new[] { .27f, .81f, .46f, .999f }, stops.Select(stop => stop.Alpha));
    }

    [Fact]
    public void Finite_offsets_keep_clamping_when_native_place_values_overflow()
    {
        foreach ((string token, double expected) in new (string, double)[]
        {
            ("1e40", 1),
            ("1e40%", 1),
            ("0000000000000000000000000000000000000000000000.5", .5),
            ("0000000000000000000000000000000000000000000050%", .5),
        })
        {
            using SvgRoot root = Parse("<linearGradient id='p'><stop offset='0'/><stop offset='" + token + "'/><stop offset='1'/></linearGradient>");
            SvgGradientStop[] stops = Assert.Single(root.Geometries).Gradient.Gradient.Stops;
            Assert.All(stops, stop => Assert.True(double.IsFinite(stop.Offset)));
            Assert.Equal(new[] { 0d, expected, 1 }, stops.Select(stop => stop.Offset));
        }
    }

    [Fact]
    public void Forward_href_and_stop_style_preserve_source_alpha()
    {
        using SvgRoot root = Parse("<linearGradient id='p' href='#base'/><linearGradient id='base'><stop offset='0' stop-opacity='.9' style='stop-opacity:27% !important'/><stop offset='1' stop-opacity='.999'/></linearGradient>");
        SvgGradientStop[] stops = Assert.Single(root.Geometries).Gradient.Gradient.Stops;
        Assert.Equal(new[] { .27f, .999f }, stops.Select(stop => stop.Alpha));
        Assert.Equal(new byte[] { 69, 255 }, stops.Select(stop => stop.Color.Alpha));
    }

    [Fact]
    public void Encoded_color_alpha_multiplies_authored_stop_opacity()
    {
        using SvgRoot root = Parse("<linearGradient id='p'><stop offset='0' stop-color='#46596d80' stop-opacity='.27'/><stop offset='1'/></linearGradient>");
        SvgGradientStop stop = Assert.Single(root.Geometries).Gradient.Gradient.Stops[0];
        Assert.Equal(128 * (1f / 255f) * .27f, stop.Alpha);
        Assert.Equal(35, stop.Color.Alpha);
    }

    [Theory]
    [InlineData("rgba(0,0,255,.27)")]
    [InlineData("rgba(0,0,255,27%)")]
    [InlineData("rgb(0 0 255 / .27)")]
    [InlineData("rgba(0 0 255 / 27%)")]
    [InlineData("hsla(240,100%,50%,.27)")]
    [InlineData("hsla(240,100%,50%,27%)")]
    [InlineData("hsl(240 100% 50% / .27)")]
    [InlineData("hsla(240 100% 50% / 27%)")]
    public void Functional_color_alpha_is_encoded_before_authored_stop_opacity(string color)
    {
        using SvgRoot root = Parse("<linearGradient id='p'><stop offset='0' stop-color='" + color + "' stop-opacity='.414'/><stop offset='1'/></linearGradient>");
        SvgGradientStop stop = Assert.Single(root.Geometries).Gradient.Gradient.Stops[0];
        Assert.Equal(69 * (1f / 255f) * .414f, stop.Alpha);
        Assert.Equal(new SvgColor(0, 0, 255, 29), stop.Color);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public async Task Six_outputs_agree_for_polygon_shader_replay(string paint)
    {
        byte[] input = Polygon(paint);
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public void Polygon_work_denial_releases_replay_storage(string paint)
    {
        using SvgRoot root = SvgRootParser.Parse(Polygon(paint), SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        long work;
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None)) work = surface.WorkUnits;
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        SvgRenderLimits limits = new(SvgRenderLimits.Default.MaximumAccountedBytes, 1_000_000, 1000, 1_000_000, 1_000_000, 1000, 1000, checked((int)work - 1));
        RenderBudget denied = new(limits.MaximumAccountedBytes);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, limits), denied, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public void Cancellation_between_shader_events_releases_replay_storage(string paint)
    {
        using SvgRoot root = SvgRootParser.Parse(Polygon(paint), SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using CancellationTokenSource cancellation = new();
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeReplay) cancellation.Cancel();
        });
        OperationCanceledException exception = Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgRoot Parse(string definitions) => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='32'><defs>" + definitions + "</defs><rect width='40' height='32' fill='url(#p)'/></svg>"), SvgRenderLimits.Default, CancellationToken.None);

    private static byte[] Polygon(string paint)
    {
        string definition = paint switch
        {
            "linear" => "<linearGradient id='p'><stop offset='0' stop-color='#17a9cd' stop-opacity='.27'/><stop offset='1' stop-color='#c3d537' stop-opacity='.81'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop offset='0' stop-color='#17a9cd' stop-opacity='.27'/><stop offset='1' stop-color='#c3d537' stop-opacity='.81'/></radialGradient>",
            "pattern" => "<pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='#b34117' fill-opacity='.47'/></pattern>",
            _ => throw new ArgumentException("Unknown shader paint.", nameof(paint)),
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><defs>" + definition + "</defs><path d='M2.25 2.5L97.5 9.25L54.75 95.5Z' fill='url(#p)' fill-opacity='.79'/></svg>");
    }
}
