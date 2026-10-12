using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClipIntersectionTests
{
    [Theory]
    [InlineData("group-object-inherited")]
    [InlineData("nested-oval-intersection")]
    [InlineData("clip-child-oval-use")]
    public void Bounds_stencils_and_mapped_source_clips_release_owners_on_sampled_denial(string identity)
    {
        byte[] input = Read(identity);
        RenderBudget measured = new(8_000_000);
        ParserWorkCounter work = new(1_000_000);
        byte[] expected;
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 0, work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(8_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget memory = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, memory, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, memory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, memory.Snapshot().CurrentBytes);
        RenderBudget retry = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, retry, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            Assert.Equal(expected, surface.Pixels);
        Assert.Equal(0, retry.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Canceling_stencil_replay_preserves_owned_source_and_identical_retry()
    {
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(Read("nested-oval-intersection"), SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                expected = surface.Pixels.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            bool observed = false;
            using (SvgAnalyticPaintProbe.Arm((phase, _) =>
            {
                if (phase == SvgAnalyticPaintPhase.BeforeReplay)
                {
                    observed = true;
                    cancellation.Cancel();
                }
            }))
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                {
                    using SvgConverter.RasterizedSurface canceled = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token);
                }).CancellationToken);
            Assert.True(observed);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<rect width='8' height='8' fill='red' clip-path='inherit'/>")]
    [InlineData("<defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><g clip-path='url(#c)'><g><rect width='8' height='8' fill='red' transform='translate(2)' clip-path='inherit'/></g></g>")]
    public void Explicit_clip_inheritance_uses_the_immediate_container_or_initial_value(string body)
    {
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>";
        byte[] expected = SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(prefix + body.Replace(" clip-path='inherit'", "", StringComparison.Ordinal) + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] actual = SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(prefix + body + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Invalid_instance_rule_rejects_even_when_the_source_authors_its_own_rule()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><path id='p' d='M0 0H8V8H0Z' fill-rule='nonzero'/></defs><use href='#p' fill-rule='bogus'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("", "<g clip-path='url(#c)'><rect width='8' height='8'/></g>", "a")]
    [InlineData(" clipPathUnits='objectBoundingBox'", "<rect width='8' height='8' clip-path='url(#c)'/>", "a")]
    [InlineData("", "<rect width='8' height='8' clip-path='url(#c)'/>", "c")]
    public void Unqualified_nested_resource_targets_and_cycles_reject_with_cleanup(string units, string target, string nested)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='a'><path d='M0 0H8V8H0Z'/></clipPath><clipPath id='c'" + units + "><circle cx='4' cy='4' r='3' clip-path='url(#" + nested + ")'/></clipPath></defs>" + target + "</svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Read(string identity)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "clip-intersections", identity + ".svg"));
}
