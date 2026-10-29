using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPaintFillStrokeOwnershipTests
{
    [Theory]
    [InlineData("url(#linear)", "#444444")]
    [InlineData("url(#linear)", "url(#linear)")]
    [InlineData("url(#linear)", "url(#radial)")]
    [InlineData("url(#linear)", "url(#pattern)")]
    [InlineData("url(#radial)", "#444444")]
    [InlineData("url(#pattern)", "#444444")]
    [InlineData("url(#empty-linear)", "#444444")]
    [InlineData("#444444", "url(#empty-linear)")]
    [InlineData("url(#empty-pattern)", "url(#radial)")]
    [InlineData("url(#linear)", "url(#empty-pattern)")]
    public void Every_parser_work_denial_releases_fill_and_partial_stroke_owners(string fill, string stroke)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("""
            <svg xmlns='http://www.w3.org/2000/svg' width='44' height='32'>
            <defs>
              <linearGradient id='linear'><stop stop-color='#dc442b'/><stop offset='1' stop-color='#3565cc'/></linearGradient>
              <radialGradient id='radial'><stop stop-color='#294dc9'/><stop offset='1' stop-color='#6cbb3c'/></radialGradient>
              <pattern id='pattern' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='#427ac9'/></pattern>
              <linearGradient id='empty-linear'/>
              <pattern id='empty-pattern' patternUnits='userSpaceOnUse' width='4' height='4'/>
            </defs>
            <path d='M6,25 Q21,3 36,25 Z'
            """ + " fill='" + fill + "' stroke='" + stroke + "' stroke-width='2'/></svg>");
        RenderBudget success = new(1_000_000);
        long parserWork;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, success, null, CancellationToken.None))
        {
            Assert.False(scene.HasUnsupportedChild);
            parserWork = 0L;
        }
        Assert.Equal(0, success.Snapshot().CurrentBytes);
        for (long limit = 0; limit < parserWork; limit++)
        {
            RenderBudget denied = new(1_000_000);
            SvgException error = Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }
}
