using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgOpaqueBitmapClipTests
{
    [Theory]
    [InlineData(0, 0, 255)]
    [InlineData(25, 0, 64)]
    [InlineData(0, 16, 64)]
    [InlineData(25, 16, 16)]
    public void Coincident_analytic_clips_keep_independent_coverage(int x, int y, byte expected)
    {
        SvgClipBox rectangle = new(0, 0, 25.5, 16.5);
        SvgRasterClipMask mask = new(rectangle, rectangle);
        Assert.Equal(expected, mask.GetAlpha(26, x, y));
        Assert.Equal(expected, mask.GetAlphaAtOffset(26, (y * 26 + x) * 4));
        Assert.True(mask.IsOpaqueRectangle(new(0, 0, 25, 16), 26, CancellationToken.None));
        Assert.False(mask.IsOpaqueRectangle(new(0, 0, 26, 17), 26, CancellationToken.None));
    }

    [Fact]
    public void Tape_opacity_query_checks_the_entire_intersection_and_borrows_live_bytes()
    {
        byte[] pixels = new byte[8 * 6 * 4];
        for (int y = 1; y < 5; y++)
            for (int x = 1; x < 7; x++) pixels[(y * 8 + x) * 4 + 3] = 255;
        SvgRasterClipMask mask = new(pixels);
        SvgAnalyticClipBounds bitmap = new(1, 1, 7, 5);
        Assert.True(mask.IsOpaqueRectangle(bitmap, 8, CancellationToken.None));
        Assert.False(mask.IsOpaqueRectangle(new(0, 0, 8, 6), 8, CancellationToken.None));
        pixels[(2 * 8 + 3) * 4 + 3] = 254;
        Assert.False(mask.IsOpaqueRectangle(bitmap, 8, CancellationToken.None));
    }

    [Fact]
    public void Analytic_rectangle_opacity_query_keeps_its_partial_boundary()
    {
        SvgRasterClipMask mask = new(new SvgClipBox(1.25, .5, 4.5, 4));
        Assert.True(mask.IsOpaqueRectangle(new(2, 1, 5, 4), 8, CancellationToken.None));
        Assert.False(mask.IsOpaqueRectangle(new(1, 0, 6, 5), 8, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Opacity_query_observes_cancellation_for_each_coverage_representation(bool rectangle)
    {
        byte[] pixels = new byte[8 * 8 * 4];
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
        {
            SvgRasterClipMask mask = rectangle ? new(new SvgClipBox(0, 0, 8, 8)) : new(pixels);
            mask.IsOpaqueRectangle(new(0, 0, 8, 8), 8, canceled.Token);
        });
    }

    [Fact]
    public void Opaque_bitmap_clip_does_not_change_a_hairline_that_escapes_the_bitmap()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>"
            + "<path d='M16.25 20.5C86.75 12.125 146.5 135.75 147.25 162.625' fill='none' stroke='#b06030' stroke-width='1'/></svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgAuthoredPath path = scene.Geometries.Select(geometry => geometry.AuthoredPath)
                .First(source => source?.StrokeStyle is not null) ?? throw new InvalidOperationException();
            byte[] expected = new byte[64 * 64 * 4], actual = new byte[expected.Length], clip = new byte[expected.Length];
            for (int offset = 3; offset < clip.Length; offset += 4) clip[offset] = 255;
            SvgCpuRasterGrid grid = new(64, 64);
            SvgCpuRasterScope scope = new(new(0, 0, 64, 64), false);
            SvgDrawingMap drawing = new(SvgAffineTransform.Identity, null);
            SvgColor color = new(176, 96, 48, 255);
            Assert.True(SvgHairlineStroke.TryRasterize(path, drawing, color, null, null, SvgClipAntialiasing.None,
                SvgRasterClipMask.Empty, true, expected, 64, 64, grid, scope, budget, CancellationToken.None));
            Assert.True(SvgHairlineStroke.TryRasterize(path, drawing, color, null, null, SvgClipAntialiasing.None,
                new SvgRasterClipMask(clip), false, actual, 64, 64, grid, scope, budget, CancellationToken.None));
            Assert.True(expected.AsSpan().SequenceEqual(actual));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
