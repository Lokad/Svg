using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRectanglePaintTests
{
    [Fact]
    public void Rectangle_coverage_matches_compiled_native_pixels()
    {
        string[] rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData/rectangle-coverage-native.csv"));
        Assert.Equal(961, rows.Length);
        foreach (string row in rows.Skip(1))
        {
            string[] values = row.Split(',');
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(Parse(values[0]), Parse(values[1]), Parse(values[2]), Parse(values[3]), 40, 32);
            Assert.Equal(byte.Parse(values[6], CultureInfo.InvariantCulture), coverage.GetAlpha(int.Parse(values[4], CultureInfo.InvariantCulture), int.Parse(values[5], CultureInfo.InvariantCulture)));
        }

        static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Solid_rectangle_retains_original_edges_and_separate_clip()
    {
        using SvgRoot root = SvgRootParser.Parse(Input("solid"), SvgRenderLimits.Default, CancellationToken.None);
        SvgRectangle rectangle = Assert.Single(root.Geometries).Rectangle;
        Assert.Equal(-1.85, rectangle.X);
        Assert.Equal(-1.625, rectangle.Y);
        Assert.Equal(44, rectangle.Width);
        Assert.Equal(36, rectangle.Height);
        Assert.Equal(new SvgClipBox(.15, .375, 35.7, 27.25), rectangle.Clip);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("pattern")]
    public void Paint_server_fill_retains_its_original_box_and_clip(string paint)
    {
        using SvgRoot root = SvgRootParser.Parse(Input(paint), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgRectangle fill = paint == "linear" ? geometry.Gradient.Fill : geometry.Pattern.Fill;
        Assert.Equal(-1.85, fill.X);
        Assert.Equal(-1.625, fill.Y);
        Assert.Equal(44, fill.Width);
        Assert.Equal(36, fill.Height);
        Assert.Equal(new SvgClipBox(.15, .375, 35.7, 27.25), fill.Clip);
        if (paint == "linear")
        {
            Assert.Equal(44, geometry.Gradient.Box.Width);
            Assert.Equal(36, geometry.Gradient.Box.Height);
            Assert.Null(geometry.Gradient.Box.Clip);
        }
    }

    [Fact]
    public void Rectangle_transform_maps_clip_edges_and_reflected_boundary_roles()
    {
        SvgRectangle source = new(4, 6, 7, 8, SvgColor.Transparent, false, null)
        {
            Clip = new SvgClipBox(.125, .375, 10, 12) { Antialiasing = SvgClipAntialiasing.Left | SvgClipAntialiasing.Top }
        };
        SvgRectangle mapped = new SvgAffineTransform(-2, 0, 0, 3, 40, -4).Apply(source);
        Assert.Equal(18, mapped.X);
        Assert.Equal(14, mapped.Y);
        Assert.Equal(14, mapped.Width);
        Assert.Equal(24, mapped.Height);
        Assert.Equal(new SvgClipBox(19.75, -2.875, 20, 36) { Antialiasing = SvgClipAntialiasing.Right | SvgClipAntialiasing.Top }, mapped.Clip);
    }

    [Fact]
    public void Viewport_shift_maps_the_retained_clip()
    {
        SvgRectangle source = new(4, 6, 7, 8, SvgColor.Transparent, false, null) { Clip = new(.125, .375, 10, 12) };
        SvgViewportFrame frame = new(.3125, .4375, .5, .25, 100, 100, new(0, 0, 100, 100), 1);
        SvgRectangle mapped = frame.Shift(source);
        Assert.Equal(2.3125, mapped.X);
        Assert.Equal(1.9375, mapped.Y);
        Assert.Equal(new SvgClipBox(.375, .53125, 5, 3), mapped.Clip);
    }

    [Fact]
    public void Symbol_viewbox_maps_rectangle_clip_with_original_edges()
    {
        SvgRectangle source = new(0, 0, 8, 8, SvgColor.Transparent, false, null)
        {
            Clip = new(1, 2, 3, 4)
        };
        SvgRectangle mapped = SvgReferenceResolver.MapSymbolBox(source, new(0, 0, 8, 8, SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet), 16, 16);
        Assert.Equal(16, mapped.Width);
        Assert.Equal(16, mapped.Height);
        Assert.Equal(new SvgClipBox(2, 4, 6, 8), mapped.Clip);
    }

    [Fact]
    public void Use_translation_moves_the_definition_clip()
    {
        SvgRectangle source = new(0, 0, 8, 8, new(255, 0, 0, 255), true, null) { Clip = new(1, 2, 3, 4) };
        SvgDefinition definition = new([SvgGeometry.FromRectangle(source)], false, null, null);
        using SvgDocumentState state = new(new RenderBudget(10_000), CancellationToken.None);
        SvgUseReference reference = new("r", 10, 12, null, null, SvgAffineTransform.Identity, SvgAffineTransform.Identity, 255, true, null, null, null);
        SvgRectangle rectangle = Assert.Single(SvgReferenceResolver.Apply(definition, reference, null, state, CancellationToken.None)).Rectangle;
        Assert.Equal((10, 12, 8, 8), (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
        Assert.Equal(new SvgClipBox(11, 14, 3, 4), rectangle.Clip);
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("linear")]
    [InlineData("pattern")]
    public async Task Six_outputs_agree_for_rectangle_paint_clips(string paint)
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

    [Fact]
    public void Parser_denial_releases_storage_for_the_retained_rectangle_clip()
    {
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(Input("solid"), SvgRenderLimits.Default, measured, CancellationToken.None))
            Assert.Single(root.Geometries);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input("solid"), SvgRenderLimits.Default, denied, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Raster_memory_denial_retains_no_surface_storage()
    {
        using SvgRoot root = SvgRootParser.Parse(Input("solid"), SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget denied = new(1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, denied, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancellation_during_surface_initialization_releases_rectangle_storage()
    {
        using SvgRoot root = SvgRootParser.Parse(Input("solid"), SvgRenderLimits.Default, CancellationToken.None);
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
        string definitions = paint switch
        {
            "linear" => "<linearGradient id='p'><stop offset='0' stop-color='#46596d'/><stop offset='1' stop-color='#46596d'/></linearGradient>",
            "pattern" => "<pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='#46596d'/></pattern>",
            _ => ""
        };
        string fill = paint == "solid" ? "#46596d" : "url(#p)";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='32'><defs>" + definitions + "</defs><svg x='.15' y='.375' width='35.7' height='27.25' viewBox='0 0 35.7 27.25'><rect x='-2' y='-2' width='44' height='36' fill='" + fill + "'/></svg></svg>");
    }
}
