using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAnalyticPaintClipTests
{
    private static readonly SvgDevicePoint[] Triangle = [new(2.43, 2.761), new(27.8125, 16.013), new(6.43, 28.375)];

    [Fact]
    public void Mask_bounds_do_not_create_AA_boundaries_and_intersections_retain_geometric_origins()
    {
        SvgClipBox geometric = new(.375, .625, 80, 64);
        SvgClipBox allocation = new(4, 0, 64, 80) { Antialiasing = SvgClipAntialiasing.None };
        SvgClipBox combined = geometric.Intersect(allocation);
        Assert.Equal(combined, allocation.Intersect(geometric));
        Assert.Equal(SvgClipAntialiasing.Top | SvgClipAntialiasing.Bottom, combined.Antialiasing);
        Assert.Equal(SvgAnalyticPaintClip.Binary, SvgAnalyticRectangleClip.Classify(3.2, 3.2, 60.8, 60.8, new(0, 0, 64, 64), SvgClipAntialiasing.None));
        SvgClipBox enclosingAllocation = new(0, 0, 96, 80) { Antialiasing = SvgClipAntialiasing.None };
        Assert.Equal(geometric, geometric.Intersect(enclosingAllocation));
        SvgClipBox coincidentAllocation = geometric with { Antialiasing = SvgClipAntialiasing.None };
        Assert.Equal(geometric, geometric.Intersect(coincidentAllocation));
    }

    [Fact]
    public void Reflected_maps_preserve_the_selected_boundary_provenance()
    {
        SvgClipBox source = new(4, 6, 20, 24) { Antialiasing = SvgClipAntialiasing.Left | SvgClipAntialiasing.Top };
        SvgClipBox mapped = new SvgAffineTransform(-1, 0, 0, -1, 96, 80).Apply(source);
        Assert.Equal(new SvgClipBox(72, 50, 20, 24) { Antialiasing = SvgClipAntialiasing.Right | SvgClipAntialiasing.Bottom }, mapped);
    }

    [Fact]
    public void Compiled_native_predicate_and_edge_snapping_vectors_match()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "analytic-paint-clip-native.json")));
        Assert.Equal("f8b66b7597c4cc859d3ed190e9c6872241e6721c", data.RootElement.GetProperty("nativePin").GetString());
        JsonElement[] vectors = data.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        Assert.Equal(188, vectors.Length);
        foreach (JsonElement vector in vectors)
        {
            JsonElement rectangle = vector.GetProperty("rectangle");
            JsonElement bitmap = vector.GetProperty("bitmap");
            SvgAnalyticPaintClip actual = SvgAnalyticRectangleClip.Classify(rectangle[0].GetDouble(), rectangle[1].GetDouble(), rectangle[2].GetDouble(), rectangle[3].GetDouble(), new(bitmap[0].GetInt32(), bitmap[1].GetInt32(), bitmap[2].GetInt32(), bitmap[3].GetInt32()), SvgClipAntialiasing.All);
            Assert.True((actual == SvgAnalyticPaintClip.Antialiased) == vector.GetProperty("expectedAntialiased").GetBoolean(), vector.GetProperty("id").GetString());
        }
    }

    [Fact]
    public void Tiny_path_dispatch_is_context_local_and_does_not_contaminate_cached_geometry()
    {
        SvgAnalyticContourPreparation preparation = new(Triangle, [3], [], false, null, SvgPolygonMapping.QuarterEdges, [], [], new(0, 0, 32, 32), 96, 80);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        long binaryBytes;
        {
            using SvgAnalyticCoverage warm = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 96), null, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        {
            using SvgAnalyticCoverage binary = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 96), null, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(SvgAnalyticPaintKind.Pair, binary.PairPaintKind);
            binaryBytes = budget.Snapshot().CurrentBytes;
        }
        bool? cachedConvex = preparation.Convex;
        long? cachedCount = preparation.NativePointCount;
        {
            using SvgAnalyticCoverage aa = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 96), null, SvgAnalyticPaintClip.Antialiased, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(SvgAnalyticPaintKind.Direct, aa.PairPaintKind);
            Assert.Equal(binaryBytes + 64 + ParserCollectionMemory.GetArrayByteCount<SvgDirectPaintLink>(32), budget.Snapshot().CurrentBytes);
            Assert.False(preparation.UsesRunBlitter);
        }
        Assert.Equal(cachedConvex, preparation.Convex);
        Assert.Equal(cachedCount, preparation.NativePointCount);
        {
            using SvgAnalyticCoverage binary = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 96), null, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(SvgAnalyticPaintKind.Pair, binary.PairPaintKind);
            Assert.Equal(binaryBytes, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Forced_run_link_memory_refusal_releases_prepared_owners()
    {
        SvgAnalyticContourPreparation preparation = new(Triangle, [3], [], false, null, SvgPolygonMapping.QuarterEdges, [], [], new(0, 0, 32, 32), 96, 80);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        long bytes;
        {
            using SvgAnalyticCoverage warm = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 96), null, SvgAnalyticPaintClip.Antialiased, budget, out bool _, CancellationToken.None);
        }
        {
            using SvgAnalyticCoverage measured = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 96), null, SvgAnalyticPaintClip.Antialiased, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            bytes = budget.Snapshot().CurrentBytes;
        }
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(bytes - 1));
        try
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 96), null, SvgAnalyticPaintClip.Antialiased, denied, out bool _, CancellationToken.None);
            Assert.Fail("AA run links must fit together with existing row/edge ownership.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Equal(1, denied.Snapshot().DeniedReservationCount);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Tiny_forced_run_cancellation_during_replay_preserves_token_and_ownership()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource source = new();
        int replayCalls = 0;
        using IDisposable probe = SvgAnalyticPaintProbe.Arm((phase, _) =>
        {
            if (phase == SvgAnalyticPaintPhase.BeforeReplay) { replayCalls++; source.Cancel(); }
        });
        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => PaintTiny(budget, source.Token));
        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.Equal(1, replayCalls);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static void PaintTiny(RenderBudget budget, CancellationToken token)
        {
            SvgAnalyticContourPreparation preparation = new(Triangle, [3], [], false, null, SvgPolygonMapping.QuarterEdges, [], [], new(0, 0, 32, 32), 96, 80);
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 96), null, SvgAnalyticPaintClip.Antialiased, budget, out bool eligible, token);
            Assert.True(eligible);
            walker.PrepareRow(10, token);
            byte[] pixel = new byte[4];
            for (int x = 0; x < 32; x++) walker.PaintPixel(x, pixel, SvgAnalyticPaint.Solid(new(0, 52, 120, 255)), SvgAnalyticClipCoverage.Full);
        }
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public async Task Six_outputs_agree_for_fractionally_clipped_contained_curves_across_four_paints(string material)
    {
        string definitions = "<defs><linearGradient id='linear'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='radial'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='pattern' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='red'/></pattern></defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='80'>" + definitions + "<defs><clipPath id='clip'><rect x='.375' y='.625' width='80' height='64'/></clipPath></defs><g clip-path='url(#clip)'><path d='M4 4C4 2 8 1 12 1C20 .9998 27 3 28 12C29 24 16 30 6 27C2 24 1 12 4 4Z' fill='" + (material == "solid" ? "#46596d" : "url(#" + material + ")") + "'/></g></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometryKind kind = material switch { "solid" => SvgGeometryKind.PolygonFill, "linear" => SvgGeometryKind.GradientPolygon, "radial" => SvgGeometryKind.RadialPolygon, "pattern" => SvgGeometryKind.PatternPolygon, _ => throw new ArgumentOutOfRangeException(nameof(material)) };
        Assert.Contains(root.Geometries, geometry => geometry.Kind == kind);
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
}
