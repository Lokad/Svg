using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLeafMaskCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copied_leaf_masks_keep_fill_stroke_together_and_separate_sibling_and_instance_values(bool forward)
    {
        RenderBudget budget = new(10_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(Input(forward), SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            Assert.Equal(6, scene.Geometries.Length);
            Assert.Equal(new byte[] { 64, 192, 64, 192 }, scene.Commands.Where(c => c.Kind == SvgDrawCommandKind.BeginGroup).Select(c => c.MaskAlpha));
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, scene.Commands.Where(c => c.Kind == SvgDrawCommandKind.Geometry).Select(c => c.GeometryIndex));
            Assert.Equal(14, scene.Commands.Length);
            Assert.Equal(SvgDrawCommandKind.Geometry, scene.Commands[1].Kind);
            Assert.Equal(SvgDrawCommandKind.Geometry, scene.Commands[2].Kind);
            Assert.Equal(SvgDrawCommandKind.EndGroup, scene.Commands[3].Kind);
            Assert.Equal(SvgDrawCommandKind.Geometry, scene.Commands[8].Kind);
            Assert.Equal(SvgDrawCommandKind.Geometry, scene.Commands[9].Kind);
            Assert.Equal(SvgDrawCommandKind.EndGroup, scene.Commands[10].Kind);
            Assert.All(scene.Geometries, geometry =>
            {
                Assert.Null(geometry.LeafMask);
                Assert.Equal(255, geometry.Polygon.Color.Alpha);
            });
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Copied_regions_move_with_the_source_mask_without_turning_into_geometry_clips()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='49' height='43'><defs><mask id='m' maskUnits='userSpaceOnUse' x='9' y='10' width='17' height='13'><rect width='79' height='73' fill='#808080'/></mask></defs><g id='g'><rect x='4.375' y='5.25' width='29.125' height='23.5' rx='5.25' fill='#267eb5' mask='url(#m)'/></g><use href='#g' x='5' y='4'/></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgDrawCommand[] scopes = scene.Commands.Where(c => c.Kind == SvgDrawCommandKind.BeginGroup).ToArray();
            Assert.Equal(2, scopes.Length);
            Assert.Equal(new SvgClipBox(9, 10, 17, 13), scopes[0].MaskRegion);
            Assert.Equal(new SvgClipBox(14, 14, 17, 13), scopes[1].MaskRegion);
            Assert.All(scene.Geometries, geometry =>
            {
                Assert.Null(geometry.Polygon.Clip);
                Assert.Null(geometry.LeafMask);
                Assert.Equal(255, geometry.Polygon.Color.Alpha);
            });
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Root_opacity_stays_outside_the_composed_leaf_mask()
    {
        using SvgRoot scene = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' opacity='0.5'><defs><mask id='m'><rect width='8' height='8' fill='#404040'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new[]
        {
            new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, 0, new SvgPaintOpacity(128, 0.5f), null, -1, 255),
            new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, new(-0.8, -0.8, 9.6, 9.6), -1, 64),
            new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255),
            new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255),
            new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255),
        }, scene.Commands);
        Assert.Equal(255, scene.Geometries[0].Rectangle.Color.Alpha);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partial_mapping_and_command_rewrite_release_owners_on_memory_and_token_denial(bool forward)
    {
        byte[] input = Input(forward);
        RenderBudget measured = new(10_000_000);
        using (SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        RenderBudget denied = new(10_000_000);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Masked_fill_stroke_surfaces_release_storage_on_memory_and_token_denial(bool forward)
    {
        RenderBudget sceneBudget = new(10_000_000), measured = new(10_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(Input(forward), SvgRenderLimits.Default, sceneBudget, null, CancellationToken.None))
        {
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None)) {}
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, tight, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            RenderBudget denied = new(10_000_000);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, cancellation.Token));
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    public void Deep_instance_chains_preserve_scope_counts_and_release_every_reserved_owner(int depth)
    {
        byte[] input = Chain(depth);
        RenderBudget budget = new(10_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            int scopes = (depth + 1) * (depth + 2) / 2;
            Assert.Equal(depth + 1, scene.Geometries.Length);
            Assert.Equal(scopes, scene.Commands.Count(c => c.Kind == SvgDrawCommandKind.BeginGroup));
            Assert.Equal(scopes, scene.Commands.Count(c => c.Kind == SvgDrawCommandKind.EndGroup));
            Assert.All(scene.Geometries, geometry => Assert.Null(geometry.LeafMask));
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(16_384);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Masked_instances_release_mapped_geometry_when_validation_or_binding_allocation_fails(bool forward, bool unbake, bool nested)
    {
        const string mask = "<mask id='m'><rect width='80' height='80' fill='#808080'/></mask>";
        const string rectangle = "<rect id='p' x='2' y='2' width='12' height='12' rx='3' fill='red'/>";
        const string group = "<g transform='translate(4 2)'><g id='p'><rect x='2' y='2' width='12' height='12' rx='3' fill='red' mask='url(#m)'/></g></g>";
        const string nestedGroup = "<g transform='translate(4 2)'><g id='p'><use href='#q' mask='url(#m)'/></g></g><g id='q'><rect x='2' y='2' width='12' height='12' rx='3' fill='red' mask='url(#m)'/></g>";
        const string instance = "<use href='#p' x='2' y='3' mask='url(#m)'/>";
        string source = nested ? nestedGroup : unbake ? group : "<defs>" + rectangle + "</defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><defs>" + mask + "</defs>" + (forward ? instance + source : source + instance) + "</svg>");
        RenderBudget measured = new(1_000_000);
        using (SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long maximum = 256; maximum < measured.Snapshot().PeakBytes; maximum += 256)
        {
            RenderBudget budget = new(maximum);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    private static byte[] Input(bool forward)
    {
        const string masks = "<defs><mask id='a'><rect width='79' height='73' fill='#404040'/></mask><mask id='b'><rect width='79' height='73' fill='#c0c0c0'/></mask></defs>";
        const string body = "<g id='g'><rect x='4.375' y='5.25' width='23.125' height='19.5' rx='5.25' ry='3.75' fill='#267eb5' stroke='#b95873' stroke-width='3' mask='url(#a)'/><rect x='17.25' y='12.375' width='18.5' height='17.25' rx='3.5' fill='#9d456b' mask='url(#b)'/></g><use href='#g' x='3' y='2'/>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='49' height='43'>" + (forward ? body + masks : masks + body) + "</svg>");
    }

    private static byte[] Chain(int depth)
    {
        StringBuilder input = new("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><defs><mask id='m'><rect width='2' height='2' fill='#808080'/></mask></defs><g id='g0'><rect width='2' height='2' fill='#267eb5' mask='url(#m)'/></g>");
        for (int index = 1; index <= depth; index++) input.Append("<g id='g").Append(index).Append("'><use href='#g").Append(index - 1).Append("' mask='url(#m)'/></g>");
        return System.Text.Encoding.UTF8.GetBytes(input.Append("</svg>").ToString());
    }
}
