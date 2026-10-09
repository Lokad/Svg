using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOvalPercentageLengthTests
{
    [Theory]
    [InlineData("<circle cx='50%' cy='50%' r='-1%'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<ellipse cx='50%' cy='50%' rx='30%' ry='-1%'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<circle cx='NaN%' cy='50%' r='20%'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<circle cx='50%' cy='50%' r='Infinity%'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<circle cx='50%' cy='50%' r='1e80%'/>", SvgErrorCode.LimitExceeded)]
    [InlineData("<circle cx='3355443200%' cy='50%' r='20%'/>", SvgErrorCode.LimitExceeded)]
    [InlineData("<circle cx='1e12%' cy='50%' r='20%'/>", SvgErrorCode.LimitExceeded)]
    [InlineData("<circle cx='50%' cy='50%' r='20cm'/>", SvgErrorCode.UnsupportedFeature)]
    public void Rejected_oval_lengths_release_all_partial_owners(string content, SvgErrorCode error)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='32'><rect width='3' height='4'/>" + content + "</svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(error, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        byte[] valid = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='32'><circle cx='50%' cy='50%' r='20%'/></svg>"u8.ToArray();
        using (SvgRoot root = SvgRootParser.Parse(valid, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
