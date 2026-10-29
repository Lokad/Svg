using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgAffineFloodOwnershipTests
{
    [Theory]
    [InlineData("group")]
    [InlineData("zero-symbol")]
    public void Affine_flood_restoration_releases_on_memory_refusal_cancellation_and_retry(string source)
    {
        byte[] input = SvgAffineFloodInput.Create("rotate(15 16 16)", "x='2.25' y='3.5' width='24.5' height='23.25'", "#4f83bc", ".43", source, true);
        AssertRestorationLimits(input);
    }

    [Fact]
    public void Deferred_offset_restoration_preserves_pixels_and_owner_lifetimes()
    {
        string svg = System.Text.Encoding.UTF8.GetString(SvgAffineFloodInput.Create("rotate(15 16 16)", "x='0' y='0' width='32' height='32'", "blue", "1", "group", false));
        svg = svg.Replace("<feFlood flood-color='blue' flood-opacity='1'/>", "<feOffset in='SourceGraphic' dx='.3' dy='.7'/>", StringComparison.Ordinal);
        AssertRestorationLimits(System.Text.Encoding.UTF8.GetBytes(svg));
    }

    private static void AssertRestorationLimits(byte[] input)
    {
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            byte[] expected;
            RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            { expected = surface.Pixels.ToArray(); }
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, cancellation.Token)).CancellationToken);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

}
