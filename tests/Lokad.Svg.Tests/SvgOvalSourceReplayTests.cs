using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOvalSourceReplayTests
{
    [Theory]
    [InlineData("<ellipse cx='14.5' cy='16.25' rx='11' ry='6' fill='none' stroke='red' stroke-width='3'/>")]
    [InlineData("<defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><circle cx='16' cy='16' r='11' fill='none' stroke='white' stroke-width='3'/></mask></defs><rect width='32' height='32' mask='url(#m)'/>")]
    [InlineData("<circle cx='47.125%' cy='51.25%' r='34.375%' fill='none' stroke='red' stroke-width='3'/>")]
    [InlineData("<defs><radialGradient id='p' fx='31%' fy='42%'><stop stop-color='white'/><stop offset='1' stop-color='black'/></radialGradient></defs><ellipse cx='50%' cy='50%' rx='31.125%' ry='19.375%' fill='url(#p)'/>")]
    public void Source_oval_owners_release_at_every_parser_work_denial_and_sampled_memory_denials(string content)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 256) DenyMemory(limit);
        DenyMemory(peak - 1);
        RenderBudget retry = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, retry, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            Assert.Contains(surface.Pixels, value => value != 0);
        Assert.Equal(0, retry.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }
}
