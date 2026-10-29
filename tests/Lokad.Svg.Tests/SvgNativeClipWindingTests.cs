using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNativeClipWindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partial_clip_replay_cancellation_preserves_borrowed_commands_and_identical_retry(bool isEvenOdd)
    {
        byte[] source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "native-clip-winding", "bowtie-ordered-group.svg"));
        byte[] input = isEvenOdd ? System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(source).Replace("<clipPath id=\"c\">", "<clipPath id=\"c\" clip-rule=\"evenodd\">", StringComparison.Ordinal)) : source;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
            SvgAuthoredPath[] paths = storage.MaskContents.SelectMany(mask => mask.Geometries).Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            SvgAuthoredPathCommand[][] original = paths.Select(path => path.Commands.ToArray()).ToArray();
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                expected = surface.Pixels.ToArray();
            foreach (int threshold in new[] { 1, 10 })
            {
                using CancellationTokenSource cancellation = new();
                int observed = 0;
                using (SvgAnalyticPaintProbe.Arm((phase, _) =>
                {
                    if (phase == SvgAnalyticPaintPhase.BeforeReplay && ++observed == threshold) cancellation.Cancel();
                }))
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    {
                        using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token);
                    }).CancellationToken);
                Assert.Equal(threshold, observed);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            }
            for (int index = 0; index < paths.Length; index++) Assert.Equal(original[index], paths[index].Commands.ToArray());
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Full_warning_document_releases_partial_owners_on_work_and_memory_denial()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "non-ionizing-warning.svg"));
        SvgRenderOptions options = new(new SvgViewport(616, 540), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(20_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, measured, CancellationToken.None))
        {
            Assert.Equal(616, surface.Width);
            Assert.Equal(540, surface.Height);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget deniedMemory = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedMemory, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, deniedMemory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, deniedMemory.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Multi_contour_parity_preserves_holes_and_releases_definition_staging()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><clipPath id='c' clip-rule='evenodd'><path d='M0 0H32V32H0Z M4 4H28V28H4Z'/></clipPath><rect width='32' height='32' clip-path='url(#c)'/></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal((byte)0, surface.Pixels[(16 * 32 + 16) * 4 + 3]);
            Assert.Equal((byte)255, surface.Pixels[(2 * 32 + 2) * 4 + 3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, denied, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }
}
