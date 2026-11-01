using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgDeferredClipTests
{
    [Theory]
    [InlineData(42, false)]
    [InlineData(43, true)]
    public void Deferred_sources_keep_the_existing_boundary_and_painted_replay_threshold(int count, bool paintedReplay)
    {
        // The two direct groups are staged on opposite sides of the reference.
        // Retaining the second group makes the first group's child range nonterminal.
        string children = string.Concat(Enumerable.Range(1, 20).Select(Rectangle))
            + "<use href='#s' x='21'/>"
            + string.Concat(Enumerable.Range(22, count - 21).Select(Rectangle));
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='8'><defs>"
            + "<clipPath id='c'>" + children + "</clipPath><path id='s' d='M0 2H1V6H0Z'/></defs>"
            + "<rect width='48' height='8' fill='red' clip-path='url(#c)'/></svg>");
        RenderBudget budget = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
            SvgVectorContent[] masks = storage.MaskContents.Select(mask => mask.VectorContent).OfType<SvgVectorContent>().ToArray();
            if (paintedReplay) Assert.Equal(count, Assert.Single(masks).Scene.Geometries.Length);
            else Assert.Empty(masks);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 48; x++)
            {
                byte alpha = x >= 1 && x <= count && y is >= 2 and < 6 ? (byte)255 : (byte)0;
                int offset = (y * 48 + x) * 4;
                Assert.Equal(alpha, surface.Pixels[offset]);
                Assert.Equal(0, surface.Pixels[offset + 1]);
                Assert.Equal(0, surface.Pixels[offset + 2]);
                Assert.Equal(alpha, surface.Pixels[offset + 3]);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static string Rectangle(int x) => $"<rect x='{x}' y='2' width='1' height='4'/>";
    }

    [Theory]
    [InlineData("<use href='#s' visibility='hidden'/><rect x='2' y='2' width='4' height='4'/>")]
    [InlineData("<rect x='2' y='2' width='4' height='4'/><use href='#s' display='none'/>")]
    [InlineData("<use href='#container'/><rect x='2' y='2' width='4' height='4'/><use href='#container'/>")]
    public void Nonpainting_references_do_not_erase_a_visible_sibling(string children)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>"
            + "<clipPath id='c'>" + children + "</clipPath><path id='s' d='M0 0H8V8H0Z'/>"
            + "<g id='container'><rect width='8' height='8'/></g></defs><rect width='8' height='8' clip-path='url(#c)'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                Assert.Equal(x is >= 2 and < 6 && y is >= 2 and < 6 ? 255 : 0, surface.Pixels[(y * 8 + x) * 4 + 3]);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
