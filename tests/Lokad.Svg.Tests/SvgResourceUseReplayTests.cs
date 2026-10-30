using Lokad.Svg.Rendering;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgResourceUseReplayTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("M3 24Q16 0 29 24Z", "Quadratic")]
    [InlineData("M3 24C3 0 29 0 29 24Z", "Cubic")]
    [InlineData("M3 24A13 13 0 0 1 29 24Z", "Conic")]
    public void Referenced_clip_registration_preserves_native_curves_and_local_placement(string data, string verb)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><use href='#s' x='.25' y='.5'/></clipPath><path id='s' d='" + data + "'/></defs><rect width='32' height='32' clip-path='url(#c)'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
        SvgAuthoredPath path = Assert.Single(storage.MaskContents.Where(mask => mask.ClipKind == SvgClipContentKind.ClipPath)
            .SelectMany(mask => mask.Geometries)
            .Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>());
        SvgAuthoredPathVerb expected = Enum.Parse<SvgAuthoredPathVerb>(verb);
        // Native clip construction materializes the child placement before
        // reference/device mapping. Curves retain controls; arcs normalize to
        // rational conics, never flattened chords. These are exact operands.
        Assert.Equal(SvgAffineTransform.Identity, path.SceneMap);
        SvgAuthoredPathCommand move = new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(3.25, 24.5), default, default);
        SvgAuthoredPathCommand close = new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default);
        SvgAuthoredPathCommand[] commands = expected switch
        {
            SvgAuthoredPathVerb.Quadratic => [move,
                new(expected, SvgAuthoredArcFlags.None, new(16.25, .5), new(29.25, 24.5), default), close],
            SvgAuthoredPathVerb.Cubic => [move,
                new(expected, SvgAuthoredArcFlags.None, new(3.25, .5), new(29.25, .5), new(29.25, 24.5)), close],
            SvgAuthoredPathVerb.Conic => [move,
                new(expected, SvgAuthoredArcFlags.None, new(3.25, 11.5), new(16.25, 11.5), new(.70710677f, 0)),
                new(expected, SvgAuthoredArcFlags.None, new(29.25, 11.5), new(29.25, 24.5), new(.70710677f, 0)), close],
            _ => throw new InvalidOperationException("Unexpected native curve."),
        };
        Assert.Equal(commands, path.Commands.ToArray());
    }

    [Theory]
    [InlineData("<mask id='m'><use href='#missing'/></mask>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<clipPath id='c'><use href='#missing'/></clipPath>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<mask id='m'><use href='#s'/></mask><mask id='m'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<clipPath id='c'><use href='#s'/></clipPath><clipPath id='c'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<mask id='m'><use href='#s' mask='url(#m)'/></mask>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<mask id='m'><use href='#s'/><use href='#missing'/></mask>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<clipPath id='c' clipPathUnits='objectBoundingBox'><use href='#s'/></clipPath>", SvgErrorCode.UnsupportedFeature)]
    public void Rejected_resource_references_release_partial_owners(string resource, SvgErrorCode code)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24'><defs><path id='s' d='M2 3L20 1 22 20 3 22Z'/>" + resource + "</defs><rect width='24' height='24'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(code, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Anonymous_resource_references_remain_unused_and_release_retained_attributes()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24'><defs><path id='s' d='M2 3L20 1 22 20 3 22Z'/><mask><use href='#s'/></mask><clipPath><use href='#s'/></clipPath></defs><rect width='24' height='24'/></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) Assert.Single(root.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<rect width='24' height='24' fill='none' stroke='url(#p)' stroke-width='2'/>", "source-bound fill geometry")]
    [InlineData("<defs><path id='s' d='M2 3L20 1 22 20 3 22Z' fill='url(#p)'/></defs><use href='#s' x='1'/>", "placed geometry references")]
    public void General_linear_paint_combinations_preserve_admission_and_cleanup(string target, string message)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24'><defs><linearGradient id='p' gradientUnits='userSpaceOnUse' x2='24' gradientTransform='matrix(1 .2 .3 2 0 0)'><stop/><stop offset='1' stop-color='white'/></linearGradient></defs>" + target + "</svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        if (message == "placed geometry references")
        {
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Contains(surface.Pixels, value => value != 0);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return;
        }
        SvgException error = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Contains(message, error.Message);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
