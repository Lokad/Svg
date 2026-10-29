using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgShaderHairlineOwnershipTests
{
    [Theory]
    [InlineData("clip", "linear")]
    [InlineData("clip", "radial")]
    [InlineData("clip", "pattern")]
    [InlineData("mask", "linear")]
    [InlineData("mask", "radial")]
    [InlineData("mask", "pattern")]
    [InlineData("filter", "linear")]
    [InlineData("filter", "radial")]
    [InlineData("filter", "pattern")]
    [InlineData("viewport", "linear")]
    [InlineData("viewport", "radial")]
    [InlineData("viewport", "pattern")]
    public void Shader_hairline_materials_release_storage_and_retry_after_denial_or_cancellation(string effect, string paint)
    {
        string resources = "<defs><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect x='2' y='2' width='26' height='26'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.5'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter><symbol id='s' viewBox='0 0 32 32'><path d='M4 24C4 4 24 4 24 24' fill='green' stroke='url(#p)' stroke-width='.5' stroke-opacity='.75' stroke-linecap='round'/></symbol></defs>";
        if (paint == "radial") resources = resources.Replace("<linearGradient id='p'>", "<radialGradient id='p'>", StringComparison.Ordinal).Replace("</linearGradient>", "</radialGradient>", StringComparison.Ordinal);
        else if (paint == "pattern") resources = resources.Replace("<linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>", "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>", StringComparison.Ordinal);
        string body = "<g transform='scale(.75 1.1)' opacity='.5'><use href='#s' width='32' height='32'/></g>";
        body = effect switch
        {
            "clip" => "<g clip-path='url(#c)'>" + body + "</g>",
            "mask" => "<g mask='url(#m)'>" + body + "</g>",
            "filter" => "<g filter='url(#f)'>" + body + "</g>",
            _ => "<svg x='1' y='1' width='30' height='30' viewBox='0 0 32 32'>" + body + "</svg>"
        };
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + resources + body + "</svg>");
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (RenderBudget.BudgetReservation borrower = parser.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, null, CancellationToken.None)) { }
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
            Assert.Equal(73, parser.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(parser.Snapshot().PeakBytes - 74));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None);
            RenderBudget raster = new RenderBudget(new HostMemoryBudget(8_000_000));
            SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
            byte[] expected;
            using (SvgConverter.RasterizedSurface first = SvgConverter.Rasterize(root, options, raster, CancellationToken.None)) expected = first.Pixels.ToArray();
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            RenderBudget rasterDenied = new RenderBudget(new HostMemoryBudget(raster.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, options, rasterDenied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, rasterDenied.Snapshot().CurrentBytes);
            using CancellationTokenSource canceled = new(); canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, options, raster, canceled.Token)).CancellationToken);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, options, raster, CancellationToken.None)) Assert.Equal(expected, retry.Pixels);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
