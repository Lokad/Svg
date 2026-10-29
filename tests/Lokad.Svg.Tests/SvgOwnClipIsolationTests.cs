using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOwnClipIsolationTests
{
    public static IEnumerable<object[]> OwnClipPaints()
    {
        foreach (string shape in new[] { "rectangles", "paths", "solid-stroke", "shader-stroke" })
        foreach (string clip in new[] { "fractional", "integer", "contained", "half" })
        foreach (string opacity in new[] { "1", ".5", ".75" })
        foreach (string pose in new[] { "", "translate(.5 .25)" })
            yield return [shape, clip, opacity, pose];
    }

    [Theory]
    [MemberData(nameof(OwnClipPaints))]
    public void An_own_clip_matches_an_enclosing_clip_around_its_opacity_layer(string shape, string clip, string opacity, string pose)
    {
        string paints = shape switch
        {
            "rectangles" => "<rect x='4' y='4' width='20' height='20' fill='red'/><rect x='3' y='3' width='22' height='22' fill='blue' fill-opacity='.25'/>",
            "paths" => "<path d='M4 4H24V24H4Z' fill='red'/><path d='M3 3H25V25H3Z' fill='blue' fill-opacity='.25'/>",
            "solid-stroke" => "<path d='M4 4H24V24H4Z' fill='red' stroke='blue' stroke-width='.25' stroke-opacity='.75'/>",
            _ => "<path d='M4 4H24V24H4Z' fill='red' stroke='url(#p)' stroke-width='.25' stroke-opacity='.75'/>"
        };
        string bounds = clip switch
        {
            "fractional" => "x='5.25' y='3.75' width='19.5' height='22'",
            "integer" => "x='5' y='4' width='20' height='22'",
            "contained" => "x='0' y='0' width='32' height='32'",
            _ => "x='5.5' y='3.5' width='19' height='22'"
        };
        string definitions = "<defs><linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect " + bounds + "/></clipPath></defs>";
        string body = "<g transform='" + pose + "'>" + paints + "</g>";
        foreach (bool forward in new[] { false, true })
        {
            // These placements have independently equal native images. A clip
            // on a child group retains its separate per-paint semantics.
            Assert.Equal(Render("<g clip-path='url(#c)'><g opacity='" + opacity + "'>" + body + "</g></g>"),
                Render("<g opacity='" + opacity + "' clip-path='url(#c)'>" + body + "</g>"));

            byte[] Render(string content) => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? content + definitions : definitions + content) + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        }
    }

    [Theory]
    [InlineData(100, "0", false, 0, 0, 0)]
    [InlineData(100, "0", true, 0, 0, 0)]
    [InlineData(100, ".5", false, 191, 64, 128)]
    [InlineData(100, ".5", true, 191, 64, 128)]
    [InlineData(100, "1", false, 191, 64, 255)]
    [InlineData(100, "1", true, 191, 64, 255)]
    [InlineData(10000, "0", false, 0, 0, 0)]
    [InlineData(10000, "0", true, 0, 0, 0)]
    [InlineData(10000, ".5", false, 191, 64, 128)]
    [InlineData(10000, ".5", true, 191, 64, 128)]
    [InlineData(10000, "1", false, 191, 64, 255)]
    [InlineData(10000, "1", true, 191, 64, 255)]
    public void Large_axis_aligned_clip_extents_preserve_admission_and_native_pixels(int scale, string opacity, bool forward, byte red, byte blue, byte alpha)
    {
        string factor = scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string inverse = (1.0 / scale).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        const string definitions = "<defs><clipPath id='c'><rect width='1000000' height='1000000'/></clipPath></defs>";
        string body = "<g transform='scale(" + factor + ")' opacity='" + opacity + "' clip-path='url(#c)'><g transform='scale(" + inverse + ")'><rect x='4' y='4' width='20' height='20' fill='red'/><rect x='3' y='3' width='22' height='22' fill='blue' fill-opacity='.25'/></g></g>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>");
        foreach (double outputScale in new[] { 1.0, 1.5 })
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(null, outputScale, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
            {
                int sample = (int)(8 * outputScale);
                Assert.Equal(new byte[] { red, 0, blue, alpha }, surface.Pixels.AsSpan((sample * surface.Width + sample) * 4, 4).ToArray());
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Known_and_forward_own_clip_siblings_keep_linear_parser_allocation_growth(bool forward)
    {
        long small = Measure(32), large = Measure(512);
        Assert.True(large < small * 18, "Sixteen times as many sibling scopes must keep linear parser allocation growth.");

        long Measure(int count)
        {
            const string definitions = "<defs><clipPath id='c'><rect x='5.25' y='3.75' width='19.5' height='22'/></clipPath></defs>";
            System.Text.StringBuilder source = new("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>");
            if (!forward) source.Append(definitions);
            for (int index = 0; index < count; index++) source.Append("<g opacity='.5' clip-path='url(#c)'><rect width='24' height='24' fill='red'/><rect x='1' y='1' width='24' height='24' fill='blue'/></g><g><rect width='2' height='2' fill='green'/></g>");
            if (forward) source.Append(definitions);
            source.Append("</svg>");
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(40_000_000));
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            using (SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(source.ToString()), SvgRenderLimits.Default, budget, null, CancellationToken.None)) { }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }
    }
}
