using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClipVisualBoundsTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("stroke='none' stroke-width='2'", "", 2, 3, 18, 15)]
    [InlineData("stroke='black' stroke-width='0'", "", 2, 3, 18, 15)]
    [InlineData("stroke='black' stroke-width='2'", "", -2, -1, 22, 19)]
    [InlineData("stroke='url(#missing)' stroke-width='2'", "", -2, -1, 22, 19)]
    [InlineData("stroke='black' stroke-width='2' stroke-opacity='0'", "", -2, -1, 22, 19)]
    [InlineData("stroke='black' stroke-width='2' stroke-dasharray='1 20'", "", -2, -1, 22, 19)]
    [InlineData("stroke='black' stroke-width='2' stroke-linejoin='round'", "", 1, 2, 19, 16)]
    [InlineData("stroke='black' stroke-width='2' stroke-linecap='square' stroke-miterlimit='1'", "", 0, 1, 20, 17)]
    [InlineData("", "stroke='black' stroke-width='2'", -2, -1, 22, 19)]
    [InlineData("stroke='none'", "stroke='black' stroke-width='2'", 2, 3, 18, 15)]
    public void Path_visual_enclosure_keeps_stroke_presentation_out_of_clip_coverage(string childStyle, string containerStyle,
        int left, int top, int right, int bottom)
    {
        string child = "<path d='M2 3H18V15Q10 15 2 15Z' " + childStyle + "/>";
        using SvgRoot root = Parse(child, containerStyle);
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
        SvgMaskContent clip = Assert.Single(storage.MaskContents, content => content.ClipKind == SvgClipContentKind.ClipPath);
        SvgAuthoredPath silhouette = Assert.IsType<SvgAuthoredPath>(Assert.Single(clip.Geometries).AuthoredPath);
        Assert.Null(silhouette.StrokeStyle);
        Assert.Equal(new SvgRectangle(2, 3, 16, 12, default, true, null), SvgPathBounds.Compute(silhouette.Commands, CancellationToken.None));
        AssertEnclosure(storage, clip, left, top, right, bottom);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<rect x='2' y='3' width='16' height='12' rx='2' stroke='black' stroke-width='2'/>", 1, 2, 19, 16)]
    [InlineData("<rect x='2' y='3' width='16' height='12' stroke='black' stroke-width='2'/><path d='M24 4H28V8H24Z'/>", 1, 2, 28, 16)]
    [InlineData("<circle cx='8' cy='9' r='5' stroke='black' stroke-width='2' stroke-linecap='square'/><path d='M24 4H28V8H24Z'/>", 2, 3, 28, 15)]
    [InlineData("<ellipse cx='8' cy='9' rx='5' ry='3' stroke='black' stroke-width='2' stroke-linecap='square'/><path d='M24 4H28V8H24Z'/>", 2, 4, 28, 13)]
    [InlineData("<rect x='2' y='3' width='16' height='12'/><rect x='6' y='5' width='8' height='6' stroke='black' stroke-width='20'/><path d='M26 4L30 4L26 8Z'/>", -4, -5, 30, 21)]
    public void Primitive_and_union_visual_bounds_keep_the_original_clip_silhouettes(string children,
        int left, int top, int right, int bottom)
    {
        using SvgRoot root = Parse(children, "");
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
        SvgMaskContent clip = Assert.Single(storage.MaskContents, content => content.ClipKind == SvgClipContentKind.ClipPath);
        AssertEnclosure(storage, clip, left, top, right, bottom);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("black")]
    [InlineData("url(#missing)")]
    public void Referenced_clip_children_keep_visual_strokes_without_constructing_stroke_paint(string stroke)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
            + "<path id='s' d='M2 3H18V15Q10 15 2 15Z' stroke='" + stroke + "' stroke-width='2'/>"
            + "<clipPath id='c'><use href='#s'/></clipPath></defs><rect width='32' height='32' clip-path='url(#c)'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
        SvgMaskContent clip = Assert.Single(storage.MaskContents, content => content.ClipKind == SvgClipContentKind.ClipPath);
        Assert.Null(Assert.IsType<SvgAuthoredPath>(Assert.Single(clip.Geometries).AuthoredPath).StrokeStyle);
        AssertEnclosure(storage, clip, -2, -1, 22, 19);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Contained_rectangle_visual_outsets_do_not_enlarge_the_object_box_clip_silhouette()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect x='.25' y='.25' width='.5' height='.5' stroke='black' stroke-width='.2'/><rect x='.4' y='.4' width='.1' height='.1' stroke='black' stroke-width='1'/></clipPath></defs><rect width='32' height='32' fill='red' clip-path='url(#c)'/></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect x='8' y='8' width='16' height='16' fill='red'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static SvgRoot Parse(string children, string containerStyle)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c' "
            + containerStyle + ">" + children + "</clipPath></defs><rect width='32' height='32' clip-path='url(#c)'/></svg>");
        return SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
    }

    private static void AssertEnclosure(SvgSceneStorage storage, SvgMaskContent clip, int left, int top, int right, int bottom)
    {
        Assert.InRange(clip.BoundsClipContentIndex, 0, storage.MaskContents.Length - 1);
        SvgMaskContent enclosure = storage.MaskContents[clip.BoundsClipContentIndex];
        SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(enclosure.Geometries).AuthoredPath);
        Assert.Equal(new SvgRectangle(left, top, right - left, bottom - top, default, true, null),
            SvgPathBounds.Compute(path.Commands, CancellationToken.None));
    }
}
