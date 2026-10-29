using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNativeFillReplayTests
{
    [Theory]
    [InlineData("malta-flag.svg")]
    [InlineData("portugal-flag.svg")]
    [InlineData("south-africa-flag.svg")]
    public void Complete_document_releases_partial_source_owners_before_retry(string fileName)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(268_435_456L));
        using (SvgRoot root = SvgRootParser.Parse(input, limits, measured, null, CancellationToken.None))
            Assert.NotEmpty(root.Geometries);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget memoryDenied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, memoryDenied, null, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, memoryDenied.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, limits, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
            Assert.Contains(surface.Pixels, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<path d='M0 0 H512 V512 H0 Z M128 128 V384 H384 V128 Z' fill-rule='evenodd'/>")]
    [InlineData("<circle cx='255.5' cy='255.25' r='12.5'/>")]
    [InlineData("<path fill='none' stroke='red' stroke-width='.78125' stroke-linecap='round' d='M245.5 232.5 C210.2 266.7 265.3 223.4 260.2 259.8'/>")]
    [InlineData("<path transform='matrix(.6 -.8 .8 .6 225 275)' d='M20 20V26H16V20.000002L19.999998 20.000002'/>")]
    [InlineData("<path transform='matrix(.6 -.8 .8 .6 50 65)' d='M20 20V126H-16V20.000002C-3 20.000002 8.6 20.000002 19.999998 20.000002'/>")]
    [InlineData("<defs><clipPath id='c'><path d='M8 8L56 40L8 72Z'/></clipPath></defs><path fill='none' stroke='red' stroke-width='20' d='M8 8L56 40L8 72' clip-path='url(#c)'/>")]
    [InlineData("<defs><clipPath id='c'><path d='M8 8L56 40L8 72Z'/></clipPath></defs><path d='M0 40C0 0 80 0 80 40C80 80 0 80 0 40Z' clip-path='url(#c)'/>")]
    public void Native_fill_and_tiled_hairline_owners_release_on_denial_and_survive_retry(string content)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='600' height='600'>" + content + "</svg>");
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(268_435_456L));
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, measured, null, CancellationToken.None))
            Assert.Contains(root.Geometries, geometry => geometry.AuthoredPath is not null);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 256) DenyMemory(limit);
        DenyMemory(peak - 1);
        RenderBudget retry = new RenderBudget(new HostMemoryBudget(268_435_456L));
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, retry, null, CancellationToken.None))
        {
            SvgAuthoredPath[] sources = root.Geometries.Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            SvgAuthoredPathCommand[][] commands = sources.Select(source => source.Commands.ToArray()).ToArray();
            long owned = retry.Snapshot().CurrentBytes;
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            {
                Assert.Contains(surface.Pixels, value => value != 0);
                expected = surface.Pixels.ToArray();
            }
            Assert.Equal(owned, retry.Snapshot().CurrentBytes);
            using (retry.Reserve(268_435_456L - owned - 1024, SvgMemoryCategory.Surface))
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
            for (int index = 0; index < sources.Length; index++) Assert.Equal(commands[index], sources[index].Commands.ToArray());
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels.ToArray());
            Assert.Equal(owned, retry.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, retry.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, defaults, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("<circle cx='1e8' cy='1e8' r='1'/>")]
    [InlineData("<ellipse cx='1e8' cy='1e8' rx='1' ry='2'/>")]
    public void Oval_bounds_collapsing_in_native_float_enclose_no_area(string content)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.Single(root.Geometries).AuthoredPath ?? throw new InvalidOperationException();
            Assert.Empty(source.Commands.ToArray());
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.All(surface.Pixels, value => Assert.Equal(0, value));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
