using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOrthogonalDashTests
{
    [Fact]
    public void Retained_dash_endpoints_match_the_compiled_native_float_vectors()
    {
        string[] rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData/dash-endpoints-native.csv"));
        Assert.Equal(97, rows.Length);
        foreach (string row in rows.Skip(1))
        {
            string[] values = row.Split(',');
            float start = float.Parse(values[0], CultureInfo.InvariantCulture);
            float end = float.Parse(values[1], CultureInfo.InvariantCulture);
            float length = Math.Abs(end - start);
            float expected = BitConverter.UInt32BitsToSingle(uint.Parse(values[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            string data = "<svg xmlns='http://www.w3.org/2000/svg' width='2048' height='64'><path fill='none' stroke='black' stroke-width='2' stroke-dasharray='" + values[2] + " " + length.ToString("R", CultureInfo.InvariantCulture) + "' d='M" + values[0] + " 20H" + values[1] + " M" + values[0] + " 40H" + values[1] + "'/></svg>";
            using SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(data), SvgRenderLimits.Default, CancellationToken.None);
            SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
            SvgAuthoredPath retained = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            SvgAuthoredPathCommand[] runs = retained.Commands.ToArray().Where(command => command.Verb == SvgAuthoredPathVerb.Line).ToArray();
            Assert.Equal(2, runs.Length);
            Assert.All(runs, command => Assert.Equal(expected, (float)command.First.X));
            Assert.Contains(polygon.Points, point => point.X == expected);
        }
    }

    [Fact]
    public void Coincident_orthogonal_edges_preserve_the_native_one_byte_fringe()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        {
            using SvgAnalyticCoverage walker = Prepare(budget, false, out bool eligible);
            Assert.True(eligible);
            walker.PrepareRow(4, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(35, pixel, SvgAnalyticPaint.Solid(new SvgColor(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal(new byte[] { 0, 0, 0, 1 }, pixel);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Orthogonal_classification_is_cached_across_raster_contexts()
    {
        SvgAnalyticContourPreparation preparation = new(Points(), [4, 8, 12], [], false, [1, 1, 1], SvgPolygonMapping.FiniteEdges, [], [], new(4, 2, 55, 10), 64, 16);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgAnalyticCoverage first = SvgAnalyticCoverage.Prepare(ref preparation, 4, 55, 2, 10, new(4, 55), null, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None))
            Assert.True(eligible);
        Assert.True(preparation.OrthogonalUnion);
        using (SvgAnalyticCoverage next = SvgAnalyticCoverage.Prepare(ref preparation, 4, 55, 2, 10, new(4, 55), null, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Coincident_evenodd_contours_keep_their_existing_fallback()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgAnalyticCoverage walker = Prepare(budget, true, out bool eligible);
        Assert.False(eligible);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("horizontal")]
    [InlineData("vertical")]
    [InlineData("single")]
    public async Task Six_outputs_agree_for_native_dash_endpoints(string direction)
    {
        byte[] input = Input(direction);
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
    public void Memory_denial_releases_newly_admitted_walker_storage()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1));
        SvgException exception = Assert.Throws<SvgException>(() =>
        {
            using SvgAnalyticCoverage walker = Prepare(budget, false, out bool _);
        });
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
    }

    [Fact]
    public void Cancellation_after_preparation_releases_newly_admitted_walker_storage()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource source = new();
        {
            using SvgAnalyticCoverage walker = Prepare(budget, false, out bool eligible);
            Assert.True(eligible);
            source.Cancel();
            try { walker.PrepareRow(4, source.Token); Assert.Fail("Cancelled row preparation must not complete."); }
            catch (OperationCanceledException exception) { Assert.Equal(source.Token, exception.CancellationToken); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgDevicePoint[] Points() => [new(4, 2), new(14, 2), new(14, 10), new(4, 10), new(8, 2), new(20, 2), new(20, 10), new(8, 10), new(39.999996185302734, 2), new(55, 2), new(55, 10), new(39.999996185302734, 10)];

    private static SvgAnalyticCoverage Prepare(RenderBudget budget, bool evenOdd, out bool eligible)
        => SvgAnalyticCoverage.Prepare(Points(), [4, 8, 12], evenOdd ? [1, 1, 1] : [], evenOdd, [1, 1, 1], SvgPolygonMapping.FiniteEdges, 4, 55, 2, 10, 64, 16, budget, out eligible, CancellationToken.None);

    private static byte[] Input(string direction)
    {
        string data = direction == "vertical" ? "M24 42V60 M24 120V12 M52 12V120" : direction == "single" ? "M120 24H12" : "M42 24H60 M120 24H12 M12 52H120";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><path fill='none' stroke='#2470c4' stroke-width='8' stroke-dasharray='23 7' d='" + data + "'/></svg>");
    }
}
