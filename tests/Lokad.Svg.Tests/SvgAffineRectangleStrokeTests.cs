using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAffineRectangleStrokeTests
{
    [Theory]
    [InlineData("skewY(45)")]
    [InlineData("rotate(17 24 20)")]
    public void Naming_an_affine_rectangle_preserves_live_and_referenced_output(string transform)
    {
        string source = "<rect x='12' y='8' width='32' height='24' fill='#2168ad' transform='" + transform + "'/>";
        string named = source.Replace("<rect ", "<rect id='source' ", StringComparison.Ordinal);
        byte[] expected = Render(source);
        Assert.Equal(expected, Render(named));
        Assert.Equal(expected, Render("<defs>" + named + "</defs><use href='#source'/>"));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(Input(named), SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.NotEmpty(scene.Geometries.ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes / 2));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(Input(named), SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("0", "24", "matrix(1 .25 -.3 1 8 2)")]
    [InlineData("32", "0", "matrix(1 .25 -.3 1 8 2)")]
    [InlineData("0", "24", "matrix(0 1 -1 0 60 0)")]
    [InlineData("32", "0", "matrix(0 1 -1 0 60 0)")]
    public void Empty_mapped_rectangles_do_not_paint_or_remove_siblings(string width, string height, string transform)
    {
        string sibling = "<rect width='4' height='4' fill='blue'/>";
        string rectangle = "<rect width='" + width + "' height='" + height + "' stroke='red' stroke-width='8' transform='" + transform + "'/>";
        Assert.Equal(Render(sibling), Render(rectangle + sibling));
    }

    [Theory]
    [InlineData("-.5")]
    [InlineData("2147483648")]
    public void Invalid_mapped_stroke_widths_reject_and_release(string width)
    {
        byte[] input = Input("<rect x='12' y='12' width='32' height='24' fill='none' stroke='red' stroke-width='" + width + "' transform='matrix(1 .25 -.3 1 8 2)'/>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("userSpaceOnUse")]
    [InlineData("objectBoundingBox")]
    public void Rectangular_clip_maps_preserve_unit_outcomes_and_release(string units)
    {
        byte[] input = Input("<defs><clipPath id='c' clipPathUnits='" + units + "'><rect width='1' height='1'/></clipPath></defs><rect width='32' height='24' stroke='red' transform='rotate(17)' clip-path='url(#c)'/>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            if (units == "userSpaceOnUse")
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
                Assert.Equal(96, surface.Width); Assert.Equal(72, surface.Height);
                // The unchanged original source has these two nontransparent pixels in all healthy native captures.
                for (int y = 0; y < surface.Height; y++) for (int x = 0; x < surface.Width; x++)
                {
                    SvgColor expected = x == 0 && y == 0 ? new(162, 0, 0, 214)
                        : x == 0 && y == 1 ? new(20, 0, 0, 25) : SvgColor.Transparent;
                    int offset = (y * surface.Width + x) * 4;
                    Assert.Equal(expected, new SvgColor(surface.Pixels[offset], surface.Pixels[offset + 1], surface.Pixels[offset + 2], surface.Pixels[offset + 3]));
                }
            }
            else
                Assert.Equal(Render("<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><path d='M0 0 H1 V1 H0 Z'/></clipPath></defs><rect width='32' height='24' stroke='red' transform='rotate(17)' clip-path='url(#c)'/>"),
                    SvgConverter.ConvertToPng(root, SvgRenderOptions.Default, budget, CancellationToken.None).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quarter_turn_leaf_and_group_clips_keep_source_coordinates_and_siblings(bool group)
    {
        const string definitions = "<defs><clipPath id='c'><rect x='8' y='6' width='16' height='12'/></clipPath></defs>";
        const string map = "matrix(0 1 -1 0 48 4)";
        const string sibling = "<rect x='80' y='60' width='4' height='4' fill='blue'/>";
        string source = group
            ? "<g transform='" + map + "' clip-path='url(#c)'><rect width='32' height='24' stroke='red' stroke-width='2'/></g>"
            : "<rect width='32' height='24' stroke='red' stroke-width='2' transform='" + map + "' clip-path='url(#c)'/>";
        Assert.Equal(Render("<rect x='30' y='12' width='12' height='16'/>" + sibling), Render(definitions + source + sibling));
    }

    [Fact]
    public void General_disjoint_clip_is_nonpainting_and_keeps_following_siblings()
    {
        const string sibling = "<rect x='80' y='60' width='4' height='4' fill='blue'/>";
        Assert.Equal(Render(sibling), Render("<rect width='24' height='20' stroke='red' transform='matrix(1 .25 -.3 1 8 2)' clip-path='url(#later)'/><defs><clipPath id='later'><rect x='100' y='100' width='8' height='8'/></clipPath></defs>" + sibling));
    }

    [Fact]
    public void General_resource_clip_keeps_fractional_viewport_coverage_once()
    {
        const string source = "<defs><clipPath id='c'><rect x='4' y='-4' width='20' height='44'/></clipPath></defs><rect x='-4' y='-4' width='40' height='40' stroke='red' transform='matrix(0 1 -1 0 32 0)' clip-path='url(#c)'/>";
        const string expected = "<rect x='-4' y='4' width='40' height='20'/>";
        const string prefix = "<svg x='8.25' y='6.5' width='64' height='56' viewBox='0 0 32 28'>";
        const string suffix = "</svg><rect x='80' y='60' width='4' height='4' fill='blue'/>";
        Assert.Equal(Render(prefix + expected + suffix), Render(prefix + source + suffix));
    }

    [Fact]
    public void Affine_rectangle_clipping_keeps_native_vertical_piece_combination()
    {
        byte[] input = Input("<svg x='8' y='6' width='64' height='56' viewBox='0 0 32 28'><g transform='translate(2.25 1.5)'><rect x='-1' y='1' width='18' height='18' fill='#d53847' transform='matrix(1 .2 -.3 1 3 2)'/></g></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            // Three stable native captures give 216, 144, 72 on this straight edge.
            Assert.Equal(216, surface.Pixels[(48 * 96 + 43) * 4 + 3]);
            Assert.Equal(144, surface.Pixels[(49 * 96 + 43) * 4 + 3]);
            Assert.Equal(72, surface.Pixels[(50 * 96 + 43) * 4 + 3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void General_rectangle_source_replay_observes_memory_and_cancellation()
    {
        byte[] input = Input("<g transform='translate(2.25 1.5)'><rect x='12' y='12' width='32' height='24' fill='orange' stroke='blue' stroke-width='3.75' transform='matrix(1 .25 -.3 1 8 2)'/></g>");
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='72'>" + content + "</svg>");
    private static byte[] Render(string content) => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
