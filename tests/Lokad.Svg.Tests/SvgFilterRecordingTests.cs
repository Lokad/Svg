using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgFilterRecordingTests
{
    [Theory]
    [InlineData("leaf")]
    [InlineData("group")]
    [InlineData("symbol")]
    public void Solid_filter_sources_retain_their_canvas_recording(string source)
    {
        const string path = "<path d='M2 2H20V18H2Z'/>";
        string content = source switch
        {
            "leaf" => "<g transform='translate(4 6)' filter='url(#f)'><path filter='url(#f)' d='M2 2H20V18H2Z'/></g>",
            "group" => "<g transform='translate(4 6)' filter='url(#f)'>" + path + "</g>",
            "symbol" => "<defs><symbol id='s'><g transform='translate(4 6)' filter='url(#f)'>" + path + "</g></symbol></defs><use href='#s'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'>"
            + "<defs><filter id='f'><feGaussianBlur stdDeviation='.5'/></filter></defs>" + content + "</svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgDrawCommand[] commands = scene.Commands.Where(item => item.Kind == SvgDrawCommandKind.BeginGroup
                && item.RegionKind == SvgLayerRegionKind.Filter).ToArray();
            Assert.NotEmpty(commands);
            SvgFilterArena filters = Assert.IsType<SvgFilterArena>(scene.FilterResources);
            foreach (SvgDrawCommand command in commands)
            {
                SvgRecordedTransform recording = Assert.IsType<SvgRecordedTransform>(filters.BindingAt(command.MaskContentIndex).RecordingTransform);
                Assert.True(recording.TryMapCanvas(new(1, 0, 0, 0, 1, 0), SvgAffineTransform.Identity,
                    recording.Map, budget, CancellationToken.None, out SvgShaderMatrix drawing));
                Assert.Equal(new SvgShaderMatrix(1, 0, 4, 0, 1, 6), drawing);
            }
            SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(scene.SceneStorage);
            SvgRecordedPaintValue[] paints = scene.Commands.Where(command => command.Kind == SvgDrawCommandKind.Geometry)
                .Select(command => storage.RecordedPaintValues[scene.Geometries[command.GeometryIndex].RecordedPaint.Identity - 1])
                .Where(paint => !paint.IsEffectAnchor).ToArray();
            Assert.NotEmpty(paints);
            Assert.All(paints, paint => Assert.IsType<SvgRecordedTransform>(paint.RecordingTransform));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
