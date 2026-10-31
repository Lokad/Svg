using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClipCanvasTests
{
    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Clip_consumer_rounds_parent_before_concatenating_child_placement()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><defs><clipPath id='c'><path d='M760 512C800 490 880 515 880 545L880 610H760Z'/></clipPath></defs><g transform='matrix(.490589 0 -.01152337 .695881 -352.0716 -337.17108)' clip-path='url(#c)'><path transform='matrix(.245003 0 .04088223 .278334 778.0912 526.6771)' d='M180 140A80 120 0 1 1 20 140A80 120 0 1 1 180 140Z'/></g></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
        SvgMaskContent content = Assert.Single(storage.MaskContents, mask => mask.ClipKind == SvgClipContentKind.ClipPath);
        SvgCanvasDrawMap stage = Assert.IsType<SvgCanvasDrawMap>(content.DrawMap);
        SvgAffineTransform parent = new(.4905889928340912, 0, -.011523370631039143, .6958810687065125, -352.07159423828125, -337.17108154296875);
        Assert.Equal(parent, stage.Parent);
        Assert.Equal(parent, stage.Source);
        Assert.Equal(SvgAffineTransform.Identity, stage.Boundary);
        SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
        Assert.Equal(new SvgAffineTransform(.12019577622413635, 0, .016849027946591377, .1936873495578766, 23.582275390625, 29.33355712890625),
            stage.Prepare(SvgAffineTransform.Identity, source.SceneMap));
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Bounding_box_clip_keeps_resource_coordinates_out_of_the_consumer_canvas_map()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><path d='M0 0H.75V.75H0Z M.25 .25H1V1H.25Z' clip-rule='evenodd'/></clipPath></defs><g transform='translate(4 2)' clip-path='url(#c)'><rect x='2' y='3' width='20' height='16'/></g></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
        SvgMaskContent content = Assert.Single(storage.MaskContents, mask => mask.ClipKind == SvgClipContentKind.ClipPath);
        SvgCanvasDrawMap stage = Assert.IsType<SvgCanvasDrawMap>(content.DrawMap);
        Assert.Equal(new SvgAffineTransform(1, 0, 0, 1, 4, 2), stage.Source);
        Assert.Equal(stage.Source, stage.Parent);
        Assert.Equal(SvgAffineTransform.Identity, stage.Boundary);
        SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(content.Geometries).AuthoredPath);
        Assert.Equal(new SvgAffineTransform(20, 0, 0, 16, 6, 5), path.SceneMap);
    }
}
