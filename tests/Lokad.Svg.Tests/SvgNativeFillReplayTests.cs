using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNativeFillReplayTests
{
    [Fact]
    public void Complete_Malta_document_releases_partial_source_owners_before_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/malta-flag.svg"));
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget measured = new(limits.MaximumAccountedBytes);
        ParserWorkCounter work = new(limits.MaximumWorkUnits);
        using (SvgRoot root = SvgRootParser.Parse(input, limits, measured, null, work, CancellationToken.None))
            Assert.NotEmpty(root.Geometries);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long allowance in new[] { 0L, work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(limits.MaximumAccountedBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, denied, null, new(allowance), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget memoryDenied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, memoryDenied, null, new(limits.MaximumWorkUnits), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, memoryDenied.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, limits, measured, null, new(work.Used), CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
            Assert.Contains(surface.Pixels, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<path d='M0 0 H512 V512 H0 Z M128 128 V384 H384 V128 Z' fill-rule='evenodd'/>")]
    [InlineData("<circle cx='255.5' cy='255.25' r='12.5'/>")]
    [InlineData("<path fill='none' stroke='red' stroke-width='.78125' stroke-linecap='round' d='M245.5 232.5 C210.2 266.7 265.3 223.4 260.2 259.8'/>")]
    public void Native_fill_and_tiled_hairline_owners_release_on_denial_and_survive_retry(string content)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='600' height='600'>" + content + "</svg>");
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        RenderBudget measured = new(defaults.MaximumAccountedBytes);
        ParserWorkCounter work = new(defaults.MaximumWorkUnits);
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, measured, null, work, CancellationToken.None))
            Assert.NotNull(Assert.Single(root.Geometries).AuthoredPath);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 0; limit < work.Used; limit++)
        {
            RenderBudget denied = new(defaults.MaximumAccountedBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, defaults, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 256) DenyMemory(limit);
        DenyMemory(peak - 1);
        RenderBudget retry = new(defaults.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, retry, null, new(work.Used), CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.Single(root.Geometries).AuthoredPath ?? throw new InvalidOperationException();
            SvgAuthoredPathCommand[] commands = source.Commands.ToArray();
            long owned = retry.Snapshot().CurrentBytes;
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            {
                Assert.Contains(surface.Pixels, value => value != 0);
                expected = surface.Pixels.ToArray();
            }
            Assert.Equal(owned, retry.Snapshot().CurrentBytes);
            foreach (long allowance in new[] { 1L, 64L, 512L })
            {
                SvgRenderLimits tight = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                    defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                    checked((int)(root.ParserWorkUnits + allowance)), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, tight), retry, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(owned, retry.Snapshot().CurrentBytes);
            }
            using (retry.Reserve(defaults.MaximumAccountedBytes - owned - 1024, SvgMemoryCategory.Surface))
            {
                long padded = retry.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(padded, retry.Snapshot().CurrentBytes);
            }
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() =>
            {
                using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, cancellation.Token);
            });
            Assert.Equal(commands, source.Commands.ToArray());
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels.ToArray());
            Assert.Equal(owned, retry.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, retry.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, defaults, denied, null, new(defaults.MaximumWorkUnits), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("<circle cx='1e8' cy='1e8' r='1'/>")]
    [InlineData("<ellipse cx='1e8' cy='1e8' rx='1' ry='2'/>")]
    public void Oval_bounds_collapsing_in_native_float_enclose_no_area(string content)
    {
        RenderBudget budget = new(1_000_000);
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.Single(root.Geometries).AuthoredPath ?? throw new InvalidOperationException();
            Assert.Empty(source.Commands.ToArray());
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.All(surface.Pixels, value => Assert.Equal(0, value));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
