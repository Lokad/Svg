using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

// Retained forward-trajectory diagnostic for the Ford slice (PLAN row 4/5):
// it separates edge advancement (live versus flattened coverage rows per fill,
// first divergence row) from coverage (alpha mismatches) and compositing
// (RGB-only mismatches, cross-background census) instead of reporting one
// aggregate pixel count.
public sealed class FordForwardTrajectoryTests
{
    [Fact]
    public void Cubic_twin_first_divergence_is_coverage_before_compositing()
    {
        string research = Path.Combine(AppContext.BaseDirectory, "Research");
        byte[] svg = File.ReadAllBytes(Path.Combine(research, "cubic-twin-100x100.svg"));
        byte[] oracleBytes = File.ReadAllBytes(Path.Combine(research, "references", "cubic-twin-100x100.png"));
        RgbaImage reference = PngRgbaDecoder.Decode(oracleBytes, 100 * 100);
        RgbaImage candidate = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray(), 100 * 100);
        PixelComparison comparison = PixelComparer.Compare(reference, candidate);
        Assert.True(comparison.DimensionsMatch);
        Assert.Equal(56, comparison.ChangedPixelCount);
        Assert.Equal((byte)128, comparison.MaximumChannelError);
        Assert.Equal((byte)7, comparison.MaximumAlphaError);
        Assert.Equal(56, comparison.AlphaChangedPixelCount);
        (int alphaCount, int rgbOnlyCount, int over, int under, int firstAlphaRow, int firstRgbOnlyRow) = SplitStages(reference, candidate);
        Assert.Equal(56, alphaCount);
        Assert.Equal(0, rgbOnlyCount);
        Assert.Equal(50, over);
        Assert.Equal(6, under);
        Assert.Equal(36, firstAlphaRow);
        Assert.Equal(-1, firstRgbOnlyRow);
    }

    [Fact]
    public void Ford_fills_diverge_live_first_at_row_three()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        SvgRenderOptions options = new(new SvgViewport(500, 200), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        (double horizontalScale, double verticalScale) = SvgConverter.TryResolveOutputScales(svg, options, budget, CancellationToken.None);
        SvgRoot root = SvgRootParser.Parse(svg, options.Limits, budget, null, new ParserWorkCounter(options.Limits.MaximumWorkUnits), CancellationToken.None, horizontalScale, verticalScale, options.Viewport);
        List<string> fills = new();
        foreach (SvgGeometry geometry in root.Geometries)
        {
            if (geometry.Kind != SvgGeometryKind.PolygonFill) continue;
            var polygon = geometry.Polygon;
            (int firstRow, long differingBytes, long liveMass, long flatMass, bool liveEligible, bool flatEligible) = WalkLiveVersusFlat(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.IsEvenOdd, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans);
            fills.Add("points=" + polygon.Points.Length + " quads=" + polygon.QuadSpans.Length + " cubics=" + polygon.CubicSpans.Length + " liveEligible=" + liveEligible + " flatEligible=" + flatEligible + " firstRow=" + firstRow + " differingBytes=" + differingBytes + " liveMass=" + liveMass + " flatMass=" + flatMass);
        }
        Assert.Equal(2, fills.Count);
        Assert.Equal("points=338 quads=0 cubics=84 liveEligible=True flatEligible=True firstRow=60 differingBytes=1970 liveMass=4335894 flatMass=4338774", fills[0]);
        Assert.Equal("points=298 quads=0 cubics=60 liveEligible=True flatEligible=True firstRow=3 differingBytes=2114 liveMass=4177997 flatMass=4174854", fills[1]);
    }

    [Fact]
    public void Ford_500x200_coverage_census_pins_opaque_and_fringe()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        RgbaImage transparent = RenderAt(svg, SvgColor.Transparent);
        RgbaImage white = RenderAt(svg, new SvgColor(255, 255, 255, 255));
        RgbaImage black = RenderAt(svg, new SvgColor(0, 0, 0, 255));
        Assert.Equal(500, transparent.Width);
        Assert.Equal(200, transparent.Height);
        (long opaque, long fringe, long clear) = CensusAlpha(transparent);
        Assert.Equal(31329, opaque);
        Assert.Equal(4137, fringe);
        Assert.Equal(64534, clear);
        Assert.Equal(35466, CountNonBackground(white, 255, 255, 255));
        Assert.Equal(35393, CountNonBackground(black, 0, 0, 0));
    }

    private static RgbaImage RenderAt(byte[] svg, SvgColor background)
    {
        SvgRenderOptions options = new(new SvgViewport(500, 200), 1, background, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        return PngRgbaDecoder.Decode(png.ToArray(), 500 * 200);
    }

    private static (int AlphaCount, int RgbOnlyCount, int Over, int Under, int FirstAlphaRow, int FirstRgbOnlyRow) SplitStages(RgbaImage reference, RgbaImage candidate)
    {
        int alphaCount = 0;
        int rgbOnlyCount = 0;
        int over = 0;
        int under = 0;
        int firstAlphaRow = -1;
        int firstRgbOnlyRow = -1;
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                int offset = (y * reference.Width + x) * 4;
                bool alphaDiffers = reference.Pixels[offset + 3] != candidate.Pixels[offset + 3];
                bool rgbDiffers = reference.Pixels[offset] != candidate.Pixels[offset] || reference.Pixels[offset + 1] != candidate.Pixels[offset + 1] || reference.Pixels[offset + 2] != candidate.Pixels[offset + 2];
                if (!alphaDiffers && !rgbDiffers) continue;
                if (alphaDiffers)
                {
                    alphaCount++;
                    if (candidate.Pixels[offset + 3] > reference.Pixels[offset + 3]) over++; else under++;
                    if (firstAlphaRow < 0) firstAlphaRow = y;
                }
                else
                {
                    rgbOnlyCount++;
                    if (firstRgbOnlyRow < 0) firstRgbOnlyRow = y;
                }
            }
        }
        return (alphaCount, rgbOnlyCount, over, under, firstAlphaRow, firstRgbOnlyRow);
    }

    private static (int FirstRow, long DifferingBytes, long LiveMass, long FlatMass, bool LiveEligible, bool FlatEligible) WalkLiveVersusFlat(SvgDevicePoint[] points, int[] ends, int[]? signs, bool isEvenOdd, SvgPolygonMapping mapping, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans)
    {
        RenderBudget budget = new(10_000_000);
        ReadOnlySpan<int> votes = signs is int[] roles ? roles : Array.Empty<int>();
        using SvgAnalyticCoverage live = SvgAnalyticCoverage.Prepare(points, ends, votes, isEvenOdd, signs, mapping, quadSpans, cubicSpans, 0, 500, 0, 200, 500, 200, budget, out long _, out bool liveEligible, CancellationToken.None);
        using SvgAnalyticCoverage flat = SvgAnalyticCoverage.Prepare(points, ends, votes, isEvenOdd, signs, mapping, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), 0, 500, 0, 200, 500, 200, budget, out long _, out bool flatEligible, CancellationToken.None);
        int firstRow = -1;
        long differingBytes = 0;
        long liveMass = 0;
        long flatMass = 0;
        if (liveEligible && flatEligible)
        {
            for (int y = 0; y < 200; y++)
            {
                live.PrepareRow(y, out long _, CancellationToken.None);
                flat.PrepareRow(y, out long _, CancellationToken.None);
                byte[] liveRow = live.Row.ToArray();
                byte[] flatRow = flat.Row.ToArray();
                foreach (byte value in liveRow) liveMass += value;
                foreach (byte value in flatRow) flatMass += value;
                if (!liveRow.SequenceEqual(flatRow))
                {
                    if (firstRow < 0) firstRow = y;
                    for (int i = 0; i < liveRow.Length; i++) if (liveRow[i] != flatRow[i]) differingBytes++;
                }
            }
        }
        return (firstRow, differingBytes, liveMass, flatMass, liveEligible, flatEligible);
    }

    private static (long Opaque, long Fringe, long Clear) CensusAlpha(RgbaImage image)
    {
        long opaque = 0;
        long fringe = 0;
        long clear = 0;
        for (int i = 3; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] == 255) opaque++;
            else if (image.Pixels[i] == 0) clear++;
            else fringe++;
        }
        return (opaque, fringe, clear);
    }

    private static long CountNonBackground(RgbaImage image, byte r, byte g, byte b)
    {
        long count = 0;
        for (int i = 0; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] != r || image.Pixels[i + 1] != g || image.Pixels[i + 2] != b) count++;
        }
        return count;
    }
}
