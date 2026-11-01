using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgDeferredClipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_resource_units_bind_use_and_source_percentages_without_leaking_to_the_next_definition(bool reverse)
    {
        const string box = "<clipPath id='box' clipPathUnits='objectBoundingBox'><use href='#s' x='25%'/></clipPath>";
        const string user = "<clipPath id='user'><use href='#p'/></clipPath>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='16'><defs>"
            + (reverse ? user + box : box + user)
            + "<circle id='s' cx='25%' cy='50%' r='25%'/><path id='p' d='M22 2H26V6H22Z'/></defs>"
            + "<rect x='4' y='2' width='8' height='8' fill='red' clip-path='url(#box)'/>"
            + "<rect x='20' width='16' height='16' fill='blue' clip-path='url(#user)'/></svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((6 * 40 + 8) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, surface.Pixels.AsSpan((4 * 40 + 24) * 4, 4).ToArray());
            Assert.Equal(0, surface.Pixels[(3 * 40 + 5) * 4 + 3]);
            Assert.Equal(0, surface.Pixels[(4 * 40 + 21) * 4 + 3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Object_box_path_construction_encloses_normalized_geometry_in_the_referencing_canvas()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='96'><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><path d='M.1 .1L.9 .15L.4 .9Z'/></clipPath></defs><rect x='13.5' y='12.25' width='95' height='69' clip-path='url(#c)' transform='skewX(6)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
        SvgMaskContent clip = Assert.Single(storage.MaskContents, mask => mask.ClipKind == SvgClipContentKind.ClipPath);
        SvgAuthoredPath enclosure = Assert.Single(storage.MaskContents[clip.BoundsClipContentIndex].Geometries).AuthoredPath
            ?? throw new InvalidOperationException("Missing construction enclosure.");
        Assert.Equal(new SvgDevicePoint[] { new(23, 19), new(99, 19), new(99, 75), new(23, 75) },
            enclosure.Commands[..^1].ToArray().Select(command => command.First));
        Assert.Equal(new SvgAffineTransform(1, 0, Math.Tan(6 * (Math.PI / 180)), 1, 0, 0), enclosure.SceneMap);
    }

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
