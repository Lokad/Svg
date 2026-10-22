using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClipRectangleSourceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Rectangle_ids_can_be_reused_before_or_after_their_clip_resource(bool rounded, bool forward)
    {
        string source = "<rect id='s' x='2' y='2' width='8' height='6' " + (rounded ? "rx='2'" : "") + "/>";
        const string instance = "<use href='#s' x='4' y='2' fill='red'/>";
        string resource = "<defs><clipPath id='c'>" + source + "</clipPath></defs>";
        Assert.Equal(Render("<defs>" + source + "</defs>" + instance), Render(forward ? instance + resource : resource + instance));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_same_rectangle_can_supply_both_clip_coverage_and_an_ordinary_instance(bool rounded)
    {
        string source = "<rect id='s' x='2' y='2' width='8' height='6' " + (rounded ? "rx='2'" : "") + "/>";
        const string instance = "<use href='#s' fill='red' clip-path='url(#c)'/>";
        string expected = "<defs>" + source + "<clipPath id='c'>" + source.Replace("id='s'", "", StringComparison.Ordinal) + "</clipPath></defs>" + instance;
        Assert.Equal(Render(expected), Render("<defs><clipPath id='c'>" + source + "</clipPath></defs>" + instance));
    }

    [Theory]
    [InlineData("fill='blue' stroke='red' stroke-width='2' opacity='.5' transform='translate(1 1)'", "")]
    [InlineData("fill='currentColor' stroke='currentColor' stroke-width='2'", "color='blue'")]
    [InlineData("fill='none' stroke='red' stroke-width='2' stroke-dasharray='2 1'", "")]
    [InlineData("class='paint'", "")]
    public void Ordinary_instances_keep_authored_paint_stroke_transform_and_cascade(string attributes, string useAttributes)
    {
        string source = "<rect id='s' x='2' y='2' width='8' height='6' rx='1' " + attributes + "/>";
        string instance = "<use href='#s' x='4' fill='green' " + useAttributes + "/>";
        const string style = "<style>.paint{fill:blue;stroke:red;stroke-width:2}</style>";
        Assert.Equal(Render("<defs>" + source + "</defs>" + instance + style),
            Render("<defs><clipPath id='c'>" + source + "</clipPath></defs>" + instance + style));
    }

    [Theory]
    [InlineData("style='visibility:hidden'", "", "")]
    [InlineData("", "visibility='hidden'", "")]
    [InlineData("", "visibility='visible'", "visibility='hidden'")]
    [InlineData("", "display='none'", "visibility='visible'")]
    public void Source_visibility_is_independent_of_the_clip_container(string container, string attributes, string useAttributes)
    {
        string source = "<rect id='s' x='2' y='2' width='8' height='6' " + attributes + "/>";
        string instance = "<use href='#s' fill='red' " + useAttributes + "/>";
        Assert.Equal(Render("<defs>" + source + "</defs>" + instance),
            Render("<defs><clipPath id='c' " + container + ">" + source + "</clipPath></defs>" + instance));
    }

    [Fact]
    public void Object_box_clip_percentages_are_reconstructed_in_the_instance_viewport()
    {
        const string source = "<rect id='s' width='50%' height='50%'/><rect width='.1' height='.1'/>";
        const string instance = "<use href='#s' fill='red'/>";
        Assert.Equal(Render("<defs>" + source + "</defs>" + instance),
            Render("<defs><clipPath id='c' clipPathUnits='objectBoundingBox'>" + source + "</clipPath></defs>" + instance));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_source_ids_fail_in_either_definition_order(bool clipFirst)
    {
        const string clip = "<clipPath id='c'><rect id='s' width='8' height='6'/></clipPath>";
        const string other = "<path id='s' d='M0 0H2V2H0Z'/>";
        byte[] input = Input("<defs>" + (clipFirst ? clip + other : other + clip) + "</defs>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Duplicate_ids_between_clip_rectangles_are_rejected()
        => Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render("<defs><clipPath id='c'><rect id='s' width='8' height='6'/><rect id='s' width='2' height='2'/></clipPath></defs>")).ErrorCode);

    [Theory]
    [InlineData("<rect id='s' width='-1' height='6'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<rect id='s' width='8' height='6' transform='unknown(1)'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<g id='s'><rect width='8' height='6'/></g>", SvgErrorCode.UnsupportedFeature)]
    public void Rectangle_source_retention_does_not_broaden_clip_admission(string source, SvgErrorCode code)
        => Assert.Equal(code, Assert.Throws<SvgException>(() => Render("<defs><clipPath id='c'>" + source + "</clipPath></defs>")).ErrorCode);

    [Fact]
    public void Denial_cancellation_and_retry_release_retained_rectangle_sources()
    {
        const string source = "<rect id='s' x='2' y='2' width='8' height='6' rx='2' fill='blue' stroke='red' stroke-width='1'/>";
        const string instance = "<use href='#s' x='4'/><use href='#s' y='8'/>";
        byte[] input = Input("<defs><clipPath id='c'>" + source + "</clipPath></defs>" + instance);
        RenderBudget baseline = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, baseline, null, work, CancellationToken.None)) { }
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
        RenderBudget denied = new(baseline.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(1_000_000);
        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token));
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(Render("<defs>" + source + "</defs>" + instance), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
