using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgClippedPathRecordingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stroke_recording_retains_the_unclipped_source_path_box(bool clipped)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'>"
            + "<defs><clipPath id='c'><rect x='12' y='4' width='40' height='36'/></clipPath></defs>"
            + "<path d='M8 32C16 8 40 8 56 32' fill='none' stroke='blue' stroke-width='5'"
            + (clipped ? " clip-path='url(#c)'" : "") + "/></svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(scene.SceneStorage);
            Assert.NotEmpty(scene.Geometries);
            foreach (SvgGeometry geometry in scene.Geometries)
            {
                SvgRecordedPaintValue paint = storage.RecordedPaintValues[geometry.RecordedPaint.Identity - 1];
                Assert.Equal(new SvgPaintBounds(8, 14, 56, 32), paint.ObjectBounds);
                Assert.Equal(new SvgPaintBounds(8, 14, 56, 32), paint.SourceObjectBounds?.Bounds);
                if (!paint.IsStroke)
                {
                    Assert.True(paint.IsEffectAnchor);
                    Assert.Null(paint.VisualBounds);
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
