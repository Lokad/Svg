using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgEmptyFilterSourceTests
{
    [Fact]
    [Trait("Layer", "Mechanism")]
    public void An_unpainted_source_keeps_its_own_opacity_inside_the_parent_filter()
    {
        byte[] source = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter></defs><g filter='url(#f)'><rect width='32' height='32' fill='none' stroke='green' stroke-width='2' opacity='0'/></g></svg>"u8.ToArray();
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(source, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgGeometry geometry = Assert.Single(scene.Geometries);
            SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(scene.SceneStorage);
            SvgRecordedPaintValue paint = storage.RecordedPaintValues[geometry.RecordedPaint.Identity - 1];
            Assert.True(paint.IsEffectAnchor);
            Assert.False(paint.IsScopeAnchor);
            Assert.Equal(new SvgPaintBounds(0, 0, 32, 32), paint.ObjectBounds);
            SvgDrawCommand[] commands = scene.Commands.ToArray();
            int filter = Array.FindIndex(commands, c => c.Kind == SvgDrawCommandKind.BeginGroup && c.RegionKind == SvgLayerRegionKind.Filter);
            int opacity = Array.FindIndex(commands, c => c.Kind == SvgDrawCommandKind.BeginGroup && c.SourceOpacity.Value == 0);
            Assert.True(filter >= 0);
            Assert.True(opacity > filter);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void A_synthetic_empty_filter_scope_keeps_its_distinct_effect_carrier()
    {
        byte[] source = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter></defs><g filter='url(#f)' opacity='.5'/></svg>"u8.ToArray();
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(source, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgGeometry geometry = Assert.Single(scene.Geometries);
            SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(scene.SceneStorage);
            SvgRecordedPaintValue paint = storage.RecordedPaintValues[geometry.RecordedPaint.Identity - 1];
            Assert.True(paint.IsEffectAnchor);
            Assert.True(paint.IsScopeAnchor);
            Assert.Null(paint.ObjectBounds);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void No_ink_source_and_generated_filter_output_release_shared_owners_on_refusal_cancellation_and_retry()
    {
        byte[] source = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='green'/></pattern></defs><g filter='url(#f)'><rect width='32' height='32' fill='none' stroke='url(#p)' stroke-width='2' opacity='0'/></g></svg>"u8.ToArray();
        SvgRasterOwnershipAssertions.SharedBudgetReleasesAfterFailures(source, SvgRenderOptions.Default, 1_000_000);
    }
}
