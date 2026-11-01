using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgClipBooleanTests
{
    [Theory]
    [InlineData("<clipPath id='a' clip-path='url(#a)'><path d='M1 1H7V7H1Z'/></clipPath>")]
    [InlineData("<clipPath id='a' clip-path='url(#b)'><path d='M1 1H7V7H1Z'/></clipPath><clipPath id='b' clip-path='url(#a)'><path d='M1 1H7V7H1Z'/></clipPath>")]
    [InlineData("<clipPath id='a'><path d='M1 1H7V7H1Z' clip-path='url(#b)'/><rect width='2' height='2'/></clipPath><clipPath id='b'><path d='M1 1H7V7H1Z' clip-path='url(#a)'/><rect width='2' height='2'/></clipPath>")]
    public void Cycles_through_definitions_or_painted_union_children_release_the_registered_owners(string definitions)
    {
        byte[] source = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>"
            + definitions + "</defs><rect width='8' height='8' clip-path='url(#a)'/></svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(source, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("polygon", "")]
    [InlineData("polyline", "1,1 6,1")]
    [InlineData("polygon", "2,2 2,2 2,2")]
    public void Short_or_collapsed_point_lists_have_empty_clip_coverage(string kind, string points)
    {
        byte[] source = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><"
            + kind + " points='" + points + "'/></clipPath></defs><rect width='8' height='8' clip-path='url(#c)'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(source, SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.All(surface.Pixels, value => Assert.Equal(0, value));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Shared_dependencies_are_not_cycles_and_a_disjoint_intersection_is_empty(bool sharedDependency)
    {
        const string shared = "<clipPath id='d'><rect x='1' y='1' width='6' height='6'/></clipPath>"
            + "<clipPath id='b' clip-path='url(#d)'><path d='M0 0H8V8H0Z'/></clipPath>"
            + "<clipPath id='a' clip-path='url(#d)'><path d='M0 0H8V8H0Z' clip-path='url(#b)'/></clipPath>";
        const string disjoint = "<clipPath id='a' clip-path='url(#b)'><path d='M1 1H3V3H1Z'/></clipPath>"
            + "<clipPath id='b'><rect x='5' y='5' width='2' height='2'/></clipPath>";
        string definitions = sharedDependency ? shared : disjoint;
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>"
            + definitions + "</defs><rect width='8' height='8' clip-path='url(#a)'/></svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                Assert.Equal(sharedDependency && x is >= 1 and < 7 && y is >= 1 and < 7 ? 255 : 0,
                    surface.Pixels[(y * 8 + x) * 4 + 3]);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
