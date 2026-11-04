using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUseStrokeOpacityTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("", .375f, 96)]
    [InlineData("stroke-opacity='.625'", .625f, 159)]
    [InlineData("stroke-opacity='1'", 1f, 255)]
    public void Source_stroke_opacity_wins_independently_of_fill_opacity(string authored, float alpha, int colorAlpha)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='24'><defs><rect id='p' x='4' y='4' width='12' height='10' fill='#297fb3' stroke='#bc9334' stroke-width='2' " + authored + "/></defs><use href='#p' fill-opacity='.75' stroke-opacity='.375'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            Assert.Equal(2, scene.Geometries.Length);
            SvgGeometry fillGeometry = scene.Geometries[0], strokeGeometry = scene.Geometries[1];
            SvgSceneStorage storage = scene.SceneStorage ?? throw new InvalidOperationException();
            SvgRecordedPaintValue fill = storage.RecordedPaintValues[fillGeometry.RecordedPaint.Identity - 1];
            SvgRecordedPaintValue stroke = storage.RecordedPaintValues[strokeGeometry.RecordedPaint.Identity - 1];
            Assert.False(fill.IsStroke);
            Assert.True(stroke.IsStroke);
            Assert.Equal(.75f, fill.Alpha);
            Assert.Equal(alpha, stroke.Alpha);
            Assert.Equal(new SvgColor(188, 147, 52, checked((byte)colorAlpha)), strokeGeometry.Polygon.Color);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("direct-rectangle-transparent")]
    [InlineData("nested-use-authored-transparent")]
    [InlineData("shader-stroke-path-transparent")]
    [InlineData("shader-fill-path-solid-stroke-transparent")]
    [InlineData("new-use-curved-stroke-transparent")]
    public void Instance_opacity_and_unpainted_centerlines_release_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "use-stroke-opacity", identity + ".svg"));
        SvgRasterOwnershipAssertions.SharedBudgetReleasesAfterFailures(input, SvgRenderOptions.Default, 1_000_000);
    }
}
