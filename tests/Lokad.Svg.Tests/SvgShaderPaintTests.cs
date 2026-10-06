using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgShaderPaintTests
{
    [Fact]
    public void Float_material_stages_match_compiled_native_pixels()
    {
        string[] rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData/shader-paint-native.csv"));
        Assert.Equal(5761, rows.Length);
        foreach (string row in rows.Skip(1))
        {
            string[] columns = row.Split(',');
            int[] values = columns.Skip(1).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            SvgColor color = new((byte)values[0], (byte)values[1], (byte)values[2], (byte)values[3]);
            int x = values[6], y = values[7] ^ x;
            int matrix = (y & 1) << 5 | (x & 1) << 4 | (y & 2) << 2 | (x & 2) << 1 | (y & 4) >> 1 | (x & 4) >> 2;
            SvgShaderPaint paint = columns[0] == "gradient"
                ? SvgShaderPaint.Gradient(color, color, 0, (byte)values[4], matrix)
                : SvgShaderPaint.Pattern(color.Red, color.Green, color.Blue, color.Alpha, (byte)values[4]);
            byte[] destination = values.Skip(8).Take(4).Select(value => (byte)value).ToArray();
            paint.Blend(destination, (byte)values[5]);
            Assert.Equal(values.Skip(12).Select(value => (byte)value), destination);
        }
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public async Task Six_outputs_agree_for_translucent_shader_clips(string paint)
    {
        byte[] input = Input(paint);
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
    public void Raster_work_denial_releases_shader_storage(string paint)
    {
        using SvgRoot root = SvgRootParser.Parse(Input(paint), SvgRenderLimits.Default, CancellationToken.None);
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
    public void Raster_memory_denial_releases_shader_storage(string paint)
    {
        using SvgRoot root = SvgRootParser.Parse(Input(paint), SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget denied = new(1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, denied, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public void Cancellation_during_initialization_releases_shader_storage(string paint)
    {
        using SvgRoot root = SvgRootParser.Parse(Input(paint), SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using CancellationTokenSource cancellation = new();
        using IDisposable probe = BackgroundInitializationProbe.Arm(cancellation.Cancel, static () => { });
        SvgRenderOptions options = new(null, 1, new SvgColor(1, 2, 3, 255), SvgRenderLimits.Default);
        OperationCanceledException exception = Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, options, budget, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string paint)
    {
        string definition = paint switch
        {
            "linear" => "<linearGradient id='p'><stop offset='0' stop-color='#17a9cd'/><stop offset='1' stop-color='#c3d537'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop offset='0' stop-color='#17a9cd'/><stop offset='1' stop-color='#c3d537'/></radialGradient>",
            "pattern" => "<pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='#b34117' fill-opacity='.47'/></pattern>",
            _ => throw new ArgumentException("Unknown shader paint.", nameof(paint)),
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='32'><defs>" + definition + "</defs><svg x='.15' y='.375' width='35.7' height='27.25' viewBox='0 0 35.7 27.25'><rect x='-2' y='-2' width='44' height='36' fill='url(#p)' fill-opacity='.79'/></svg></svg>");
    }
}
