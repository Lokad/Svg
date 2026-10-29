using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterSourceCoverageTests
{
    public static IEnumerable<object[]> DeviationCases()
    {
        using JsonDocument data = ReadVectors();
        foreach (JsonElement row in data.RootElement.GetProperty("parsing").EnumerateArray())
            yield return [row.GetProperty("token").GetString() ?? throw new InvalidDataException(),
                row.GetProperty("horizontalBits").GetInt32(), row.GetProperty("verticalBits").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(DeviationCases))]
    [Trait("Layer", "Mechanism")]
    public void Gaussian_deviations_retain_native_float_place_values(string token, int horizontalBits, int verticalBits)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot scene = Parse("<feGaussianBlur stdDeviation='" + token + "'/>", budget);
        SvgFilterPrimitive primitive = Resource(scene).Primitives.Items[0];
        Assert.Equal(horizontalBits, BitConverter.SingleToInt32Bits((float)primitive.SigmaX));
        Assert.Equal(verticalBits, BitConverter.SingleToInt32Bits((float)primitive.SigmaY));
    }

    [Theory]
    [InlineData("0", 0, 0)]
    [InlineData(".00001", 0, 0)]
    [InlineData(".019999", 0, 0)]
    [InlineData("2e-2", 0, 0)]
    [InlineData(".02", 1, 1)]
    [InlineData(".020001", 1, 1)]
    [InlineData(".4", 2, 2)]
    [InlineData(".4 .00001", 2, 0)]
    [InlineData(".00001 .4", 0, 2)]
    [Trait("Layer", "Mechanism")]
    public void Materialized_bounds_distinguish_effective_zero_axes_from_identity_pixel_kernels(string sigma, int radiusX, int radiusY)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot scene = Parse("<feGaussianBlur stdDeviation='" + sigma + "'/>", budget);
        SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(100, 100), new(-100, -100, 100, 100));
        SvgPaintBounds stored = SvgFilterPlayback.SourceLayerBounds(Resource(scene), new(1.5, 1.5), context,
            new(10, 10, 20, 20), SvgFilterBoundsKind.StoredImage, budget, CancellationToken.None);
        Assert.Equal(new SvgPaintBounds(10 - radiusX, 10 - radiusY, 20 + radiusX, 20 + radiusY), stored);
    }

    public static IEnumerable<object[]> CoverageCases()
    {
        using JsonDocument data = ReadVectors();
        foreach (JsonElement row in data.RootElement.GetProperty("halfCoverage").EnumerateArray())
            yield return [row.GetProperty("x").GetInt32(),
                row.GetProperty("source").EnumerateArray().Select(value => value.GetByte()).ToArray(),
                row.GetProperty("expected").EnumerateArray().Select(value => value.GetByte()).ToArray()];
    }

    [Theory]
    [InlineData("-1e-100")]
    [InlineData("0 -1e-100")]
    [Trait("Layer", "Mechanism")]
    public void Negative_deviations_retain_rejection_before_float_underflow(string sigma)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = Parse("<feGaussianBlur stdDeviation='" + sigma + "'/>", budget);
        }).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [MemberData(nameof(CoverageCases))]
    [Trait("Layer", "Mechanism")]
    public void Floating_paint_half_coverage_matches_independent_native_texels(int sourceX, byte[] source, byte[] expected)
    {
        Assert.InRange(sourceX, 39, 128);
        byte[] actual = new byte[4];
        new SvgShaderPaint(source[0] / 255f, source[1] / 255f, source[2] / 255f, source[3] / 255f).Blend(actual, 128);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Contract")]
    public void Aliased_source_memory_refusal_cancellation_and_retry_release_temporary_owners(bool materialized)
    {
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000));
        string graph = materialized ? "<feGaussianBlur stdDeviation='.4 0'/><feOffset dy='.5'/>"
            : "<feGaussianBlur stdDeviation='0'/><feMerge result='i'><feMergeNode/></feMerge><feOffset in='i' dy='.5'/>";
        using (SvgRoot scene = Parse(graph, parser))
        {
            SvgFilterResource resource = Resource(scene);
            byte[] source = [253, 0, 1, 255, 253, 0, 1, 255];
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
            byte[] expected;
            using (SvgFilterResult result = SvgFilterPlayback.ApplyFullFrame(source, 1, 2, resource, new(1, 1), budget, CancellationToken.None))
            {
                expected = result.Pixels.ToArray();                 Assert.Equal(materialized, result.Samples is not null);
            }
            if (!materialized) Assert.Equal(new byte[] { 127, 0, 1, 128, 253, 0, 1, 255 }, expected);
            Assert.Equal(128, expected[3]);
            Assert.Equal(255, expected[7]);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            RenderBudget tight = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgFilterPlayback.ApplyFullFrame(source, 1, 2, resource, new(1, 1), tight, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() =>
                SvgFilterPlayback.ApplyFullFrame(source, 1, 2, resource, new(1, 1), budget, cancellation.Token)).CancellationToken);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            using (SvgFilterResult retry = SvgFilterPlayback.ApplyFullFrame(source, 1, 2, resource, new(1, 1), budget, CancellationToken.None))
                Assert.Equal(expected, retry.Pixels);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(new byte[] { 253, 0, 1, 255, 253, 0, 1, 255 }, source);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    private static JsonDocument ReadVectors() => JsonDocument.Parse(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "TestData", "filter-source-coverage", "mechanisms.json")));

    private static SvgRoot Parse(string graph, RenderBudget budget) => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs><filter id='f'>" + graph
        + "</filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>"), SvgRenderLimits.Default, budget, CancellationToken.None);

    private static SvgFilterResource Resource(SvgRoot scene)
    {
        SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
        return arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
    }
}
