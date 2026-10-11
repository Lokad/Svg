using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgObjectBoundingClipTests
{
    [Theory]
    [InlineData("stroke-linecap='butt'")]
    [InlineData("stroke-linecap='square'")]
    [InlineData("stroke-linecap='round'")]
    [InlineData("stroke-dasharray='3 2'")]
    public void Group_bounds_exclude_caps_and_dash_segments(string style)
    {
        string lines = "<g stroke='red' stroke-width='6' " + style + " clip-path='url(#c)'><line x1='16' y1='4' x2='16' y2='28'/><line x1='4' y1='16' x2='28' y2='16'/></g>";
        string normalized = "<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><circle cx='50%' cy='50%' r='40%'/></clipPath></defs>";
        string explicitClip = "<defs><clipPath id='c'><circle cx='16' cy='16' r='9.6'/></clipPath></defs>";
        string placed = lines.Replace("<g stroke=", "<g transform='translate(.125 0)' stroke=", StringComparison.Ordinal);
        Assert.Equal(Render(explicitClip + placed), Render(placed + normalized));
        RenderBudget budget = new(1_000_000);
        using SvgRoot root = SvgRootParser.Parse(Input(lines + normalized), SvgRenderLimits.Default, budget, CancellationToken.None);
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
        SvgAuthoredPath oval = Assert.Single(storage.MaskContents.SelectMany(mask => mask.Geometries).Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>());
        SvgRectangle bounds = oval.SceneMap.Apply(SvgPathBounds.Compute(oval.Commands, new(100_000), CancellationToken.None));
        Assert.Equal(6.4, bounds.X, 4);
        Assert.Equal(6.4, bounds.Y, 4);
        Assert.Equal(19.2, bounds.Width, 4);
        Assert.Equal(19.2, bounds.Height, 4);
    }

    [Theory]
    [InlineData("<line x1='4' y1='16' x2='28' y2='16' stroke='red' stroke-width='6' stroke-linecap='square' clip-path='url(#c)'/>")]
    [InlineData("<g clip-path='url(#c)'><line x1='4' y1='16' x2='28' y2='16' stroke='red' stroke-width='6'/></g>")]
    [InlineData("<defs><g id='source'><line x1='16' y1='4' x2='16' y2='28' stroke='red' stroke-width='6'/></g></defs><use href='#source' clip-path='url(#c)'/>")]
    public void Degenerate_geometry_bounds_clip_everything_without_removing_siblings(string content)
    {
        string sibling = "<rect width='3' height='3' fill='blue'/>";
        Assert.Equal(Render(sibling), Render("<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><circle cx='.5' cy='.5' r='.4'/></clipPath></defs>" + content + sibling));
    }

    [Fact]
    public void Empty_normalized_oval_clips_a_group()
    {
        Assert.Equal(Render(""), Render("<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><ellipse cx='.5' cy='.5' rx='0' ry='.4'/></clipPath></defs><g clip-path='url(#c)'><rect width='32' height='32' fill='red'/></g>"));
    }

    [Theory]
    [InlineData("rotate(12)")]
    [InlineData("skewX(10)")]
    [InlineData("scale(0 1)")]
    public void Unimplemented_referencing_maps_reject_and_release_sources(string transform)
    {
        byte[] input = Input("<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><circle cx='.5' cy='.5' r='.4'/></clipPath></defs><g transform='" + transform + "' clip-path='url(#c)'><rect width='24' height='24'/></g>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(100_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Group_replay_and_bounds_observe_limits_and_release_on_denial()
    {
        byte[] input = Input("<use href='#source' x='1.25' clip-path='url(#c)'/><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><circle cx='.5' cy='.5' r='.4'/></clipPath><g id='source' stroke='red' stroke-width='6'><line x1='16' y1='4' x2='16' y2='28'/><line x1='4' y1='16' x2='28' y2='16'/></g></defs>");
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(100_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 1, work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, new(100_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, new(100_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("transform='translate(2.25 1.5) scale(.8 1.1)' opacity='.4'")]
    [InlineData("visibility='hidden'")]
    public void Dormant_group_replay_preserves_root_style_transform_and_child_visibility(string attributes)
    {
        string children = "<rect width='20' height='20'/><g transform='translate(8 7)' fill='blue' visibility='visible'><rect width='8' height='8'/></g>";
        string group = "<g fill='red' " + attributes + ">" + children + "</g>";
        string definition = "<defs>" + group.Replace("<g fill=", "<g id='source' fill=", StringComparison.Ordinal) + "</defs>";
        Assert.Equal(Render(group), Render("<use href='#source'/>" + definition));
        Assert.Equal(Render(""), Render(definition));
    }

    [Theory]
    [InlineData("<g id='source'><rect id='source' width='4' height='4'/></g>")]
    [InlineData("<g id='source'><g id='child'/><g id='child'/></g>")]
    [InlineData("<g id='source'/><g id='source'/>")]
    public void Duplicate_dormant_group_ids_reject_and_release(string definitions)
    {
        RenderBudget budget = new(1_000_000);
        byte[] input = Input("<defs>" + definitions + "</defs>");
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(100_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");
    private static byte[] Render(string content) => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
