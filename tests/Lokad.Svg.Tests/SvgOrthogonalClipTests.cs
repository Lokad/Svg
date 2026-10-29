using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOrthogonalClipTests
{
    [Theory]
    [InlineData("path")]
    [InlineData("polygon")]
    [InlineData("polyline")]
    public void Clipping_preserves_the_scaled_contour_and_separate_paint_boundaries(string element)
    {
        using SvgRoot root = SvgRootParser.Parse(Input(element), SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        SvgClipBox clip = Assert.IsType<SvgClipBox>(polygon.Clip);
        Assert.Equal(SvgClipAntialiasing.All, clip.Antialiasing);
        Assert.Equal(new SvgDevicePoint(-1.6875, -1.5625), polygon.Points[0]);
        Assert.Contains(polygon.Points, point => point.X > clip.Right);
        Assert.Contains(polygon.Points, point => point.Y > clip.Bottom);
        Assert.Equal(.3125, clip.X);
        Assert.Equal(.4375, clip.Y);
        Assert.Equal(35.375, clip.Width);
        Assert.Equal(27.125, clip.Height);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("polygon")]
    [InlineData("polyline")]
    public async Task Six_outputs_agree_for_a_scaled_orthogonal_contour_with_an_AA_clip(string element)
    {
        byte[] input = Input(element);
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        byte[] asyncDestination = new byte[expected.Length];
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncDestination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncDestination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Fact]
    public void Parser_memory_denial_releases_contour_and_scene_storage()
    {
        byte[] input = Input("path");
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
            Assert.Single(root.Geometries);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(1, denied.Snapshot().DeniedReservationCount);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancellation_after_clipped_preparation_releases_raster_storage()
    {
        using SvgRoot root = SvgRootParser.Parse(Input("path"), SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        RenderBudget budget = new(1_000_000);
        using CancellationTokenSource source = new();
        {
            SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, new(-2, -2, 43, 35), 40, 32);
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 40, 0, 32, new(0, 40), new(new(0, 0, 36, 28)), SvgAnalyticPaintClip.Antialiased, budget, out bool eligible, source.Token);
            Assert.True(eligible);
            Assert.True(budget.Snapshot().CurrentBytes > 0);
            source.Cancel();
            try { walker.PrepareRow(0, source.Token); Assert.Fail("Cancelled row preparation must not complete."); }
            catch (OperationCanceledException exception) { Assert.Equal(source.Token, exception.CancellationToken); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string element)
    {
        string contour = element == "path" ? "<path d='M-4 -4H84V28H36V68H-4Z'" : "<" + element + " points='-4,-4 84,-4 84,28 36,28 36,68 -4,68'";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='32'><svg x='.3125' y='.4375' width='35.375' height='27.125' viewBox='0 0 70.75 54.25'>" + contour + " fill='#2781cb' fill-opacity='.63'/></svg></svg>");
    }
}
