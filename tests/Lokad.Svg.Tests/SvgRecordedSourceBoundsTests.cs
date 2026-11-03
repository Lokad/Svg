using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgRecordedSourceBoundsTests
{
    [Theory]
    [InlineData("<rect x='50.5' y='40.25' width='45.5' height='30.5'/>", "matrix(1.5 0 0 1.5 14.875 -5)", 89.875f, 55f, 158.875f, 101.5f)]
    [InlineData("<path d='M50.5 40.25H96V70.75H50.5Z'/>", "matrix(1.5 0 0 1.5 14.875 -5)", 89.875f, 55f, 158.875f, 101.5f)]
    [InlineData("<rect x='-2.25' y='-.25' width='1.5' height='2.5'/>", "matrix(1.5 0 0 -2 -3.5 4.25)", -8f, -1.75f, -3.5f, 6.25f)]
    public void Drawable_recording_encloses_before_placement_without_rounding_object_bounds(
        string shape, string transform, float left, float top, float right, float bottom)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='240' height='160'><g transform='" + transform + "'>" + shape + "</g></svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgSceneStorage scene = root.SceneStorage ?? throw new InvalidDataException();
            SvgRecordedPaintValue paint = Assert.Single(scene.RecordedPaintValues);
            Assert.Equal(new SvgPaintBounds(left, top, right, bottom), paint.SourceVisualBounds);
            SvgSourceObjectBounds source = paint.SourceObjectBounds ?? throw new InvalidDataException();
            Assert.NotEqual(paint.SourceVisualBounds, source.Bounds.Map(source.SceneMap, CancellationToken.None));
            Assert.Equal(source.Bounds.Map(source.SceneMap, CancellationToken.None), paint.ObjectBounds);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
