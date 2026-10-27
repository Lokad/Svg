using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanisms")]
public sealed class SvgFilterSourceHintTests
{
    [Theory]
    [InlineData(-1, 5, 11, 26, 23)]
    [InlineData(-.5, 5, 11, 26, 23)]
    [InlineData(-.01, 5, 11, 26, 23)]
    [InlineData(-.001, 5, 11, 26, 23)]
    [InlineData(-.0001, 5, 11, 26, 23)]
    [InlineData(0, 6, 11, 26, 22)]
    [InlineData(.0001, 6, 10, 27, 22)]
    [InlineData(.001, 6, 10, 27, 22)]
    [InlineData(.01, 6, 10, 27, 22)]
    [InlineData(.5, 6, 10, 27, 22)]
    [InlineData(1, 6, 10, 27, 22)]
    public void Source_capture_encloses_authored_offset_fringes_before_layer_scaling(double offset,
        float left, float top, float right, float bottom)
    {
        string dx = offset.ToString("R", CultureInfo.InvariantCulture), dy = (-offset).ToString("R", CultureInfo.InvariantCulture);
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
            + "<defs><filter id='f' filterUnits='userSpaceOnUse' x='-100' y='-100' width='200' height='200'>"
            + "<feOffset in='SourceAlpha' dx='" + dx + "' dy='" + dy + "'/></filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>");
        RenderBudget parser = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(32, 32), new(-100, -100, 100, 100));
            RenderBudget scratch = new(1_000_000);
            SvgPaintBounds hint = SvgFilterPlayback.SourceLayerBounds(resource, new(1, 1), context, new(6, 11, 26, 22),
                scratch, new(1_000), CancellationToken.None);
            // Native source capture encloses both source and shifted output in
            // parameter coordinates. Layer-space inset rounding belongs later.
            Assert.Equal(new SvgPaintBounds(left, top, right, bottom), SvgFilterPlayback.RoundParameterBounds(hint));
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }
}
