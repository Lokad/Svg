using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgScopedClipMaskTests
{
    [Theory]
    [InlineData("scoped-card-diagram")]
    [InlineData("composed-quadratic-clip")]
    [InlineData("translated-mask-use")]
    public void Scoped_clips_and_masks_preserve_input_and_release_material_storage_after_denial_and_retry(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "scoped-clips-masks", identity + ".svg"));
        byte[] unchanged = input.ToArray();
        SvgRenderOptions options = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget measured = new(8_000_000);
        ParserWorkCounter work = new(1_000_000);
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(8_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget tight = new(Math.Max(1, measured.Snapshot().PeakBytes - 4_096));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, tight, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            Assert.Equal(expected, surface.Pixels);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("opacity='.625'")]
    [InlineData("style='opacity:.625'")]
    public void Mask_container_opacity_paints_only_explicitly_inheriting_children(string declaration)
    {
        byte[] opaque = Render(string.Empty, string.Empty);
        Assert.Equal(opaque, Render(declaration, string.Empty));
        byte[] inherited = Render(declaration, "opacity='inherit'");
        Assert.Equal(Render(string.Empty, "opacity='.625'"), inherited);
        Assert.NotEqual(opaque, inherited);

        static byte[] Render(string maskStyle, string childStyle)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='24'><defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='24' " + maskStyle + "><rect width='32' height='24' fill='white' " + childStyle + "/></mask></defs><rect width='32' height='24' fill='red' mask='url(#m)'/></svg>");
            return SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        }
    }
}
