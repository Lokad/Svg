using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgScopedClipMaskTests
{
    [Theory]
    [InlineData("symbol")]
    [InlineData("mask")]
    public void Nested_clip_stencils_preserve_color_and_ownership_through_retained_sources(string placement)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><clipPath id='a'><circle cx='8' cy='8' r='7'/></clipPath><clipPath id='b'><path d='M0 0H16V16H0Z' clip-path='url(#a)'/></clipPath>";
        const string rectangle = "<rect width='16' height='16' fill='#d45a32'/>";
        string ordinary = prefix + "</defs><g clip-path='url(#b)'>" + rectangle + "</g></svg>";
        string source = placement == "symbol"
            ? prefix + "<symbol id='s'><g clip-path='url(#b)'>" + rectangle + "</g></symbol></defs><use href='#s'/></svg>"
            : prefix + "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='64' height='64'><g clip-path='url(#b)'><rect width='16' height='16' fill='white'/></g></mask></defs><rect width='16' height='16' fill='#d45a32' mask='url(#m)'/></svg>";
        byte[] expected = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(ordinary), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(source), SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.Equal(expected, SvgConverter.ConvertToPng(root, SvgRenderOptions.Default, budget, CancellationToken.None).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("a", "", SvgErrorCode.MalformedInput)]
    [InlineData("b", "<clipPath id='b'><path d='M0 0H8V8H0Z' clip-path='url(#a)'/></clipPath>", SvgErrorCode.MalformedInput)]
    [InlineData("missing", "", SvgErrorCode.UnsupportedFeature)]
    public void Nested_clip_resource_failures_release_partial_stencils(string nested, string additional, SvgErrorCode expected)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='a'><path d='M0 0H8V8H0Z' clip-path='url(#" + nested + ")'/></clipPath>" + additional + "</defs><g clip-path='url(#a)'><rect width='8' height='8' fill='red'/></g></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

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
