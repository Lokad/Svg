using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRectangleInstanceViewportTests
{
    [Theory]
    [InlineData("x='50%' y='1' width='4' height='4'")]
    [InlineData("x='1' y='50%' width='4' height='4'")]
    [InlineData("width='50%' height='6'")]
    [InlineData("width='6' height='50%'")]
    [InlineData("width='12' height='10' rx='10%'")]
    [InlineData("width='12' height='10' ry='10%'")]
    [InlineData("width='50%' height='50%'")]
    [InlineData("width='50%' height='50%' rx='2'")]
    public void Percentage_attributes_bind_in_each_earlier_or_forward_instance(string attributes)
    {
        string shape = "<rect " + attributes + " fill='red'/>";
        string definition = "<defs>" + shape.Replace("<rect ", "<rect id='s' ", StringComparison.Ordinal) + "</defs>";
        foreach (string viewport in new[] { "width='16' height='16'", "width='16' height='16' viewBox='0 0 8 8'" })
        {
            byte[] expected = Render(Nested(shape, viewport));
            string instance = Nested("<use href='#s'/>", viewport);
            foreach (bool forward in new[] { false, true })
                Assert.Equal(expected, Render(forward ? instance + definition : definition + instance));
            Assert.Equal(expected, Render(definition + instance.Replace("<use ", "<use stroke='none' ", StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("symbol-viewbox")]
    [InlineData("symbol-leaf-alias")]
    [InlineData("group")]
    [InlineData("use-alias")]
    public void Retained_containers_and_aliases_preserve_the_live_percentage_base(string kind)
    {
        const string shape = "<rect width='50%' height='50%' fill='red'/>";
        const string instance = "<use href='#s'/>";
        string content = kind switch
        {
            "symbol" => "<defs><symbol id='s'>" + shape + "</symbol></defs><use href='#s' width='16' height='16'/>",
            "symbol-viewbox" => "<defs><symbol id='s' viewBox='0 0 8 8'>" + shape + "</symbol></defs><use href='#s' width='16' height='16'/>",
            "symbol-leaf-alias" => "<defs><symbol id='container'>" + shape.Replace("<rect ", "<rect id='s' ", StringComparison.Ordinal) + "</symbol></defs>" + Nested(instance, "width='16' height='16'"),
            "group" => "<defs><g id='s'>" + shape + "</g></defs>" + Nested(instance, "width='16' height='16'"),
            "use-alias" => "<defs>" + shape.Replace("<rect ", "<rect id='source' ", StringComparison.Ordinal) + "<symbol id='container'><use id='s' href='#source'/></symbol></defs>" + Nested(instance, "width='16' height='16'"),
            _ => throw new ArgumentException("Unexpected container.", nameof(kind))
        };
        Assert.Equal(Render("<rect width='8' height='8' fill='red'/>"), Render(content));
    }

    [Fact]
    public void Separate_instance_viewports_do_not_reuse_the_first_instances_dimensions()
    {
        const string source = "<defs><rect id='s' width='50%' height='50%' fill='red'/></defs>";
        string content = source + Nested("<use href='#s'/>", "width='16' height='16'")
            + Nested("<use href='#s'/>", "x='20' width='8' height='8'");
        Assert.Equal(Render("<rect width='8' height='8' fill='red'/><rect x='20' width='4' height='4' fill='red'/>"), Render(content));
    }

    [Theory]
    [InlineData("fill='blue' stroke='red' stroke-width='1'")]
    [InlineData("class='paint'")]
    [InlineData("fill='none' stroke='red' stroke-width='1' stroke-dasharray='2 1'")]
    [InlineData("opacity='.5' fill='blue' transform='translate(1 1)'")]
    public void Percentage_reconstruction_preserves_authored_paint_stroke_and_transform(string attributes)
    {
        string shape = "<rect id='s' width='50%' height='50%' rx='10%' " + attributes + "/>";
        const string style = "<style>.paint{fill:blue;stroke:red;stroke-width:1}</style>";
        string actual = "<defs>" + shape + "</defs>" + Nested("<use href='#s' fill='green'/>", "width='16' height='16'") + style;
        Assert.Equal(Render(Nested("<g fill='green'>" + shape + "</g>", "width='16' height='16'") + style), Render(actual));
    }

    [Theory]
    [InlineData("width='-1' height='50%'", SvgErrorCode.MalformedInput)]
    [InlineData("width='50%' height='2' rx='-1%'", SvgErrorCode.MalformedInput)]
    [InlineData("width='50%' height='2' transform='unknown(1)'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("width='50%' height='2' stroke='red' stroke-width='1em'", SvgErrorCode.UnsupportedFeature)]
    public void Unused_percentage_sources_keep_existing_geometry_admission(string attributes, SvgErrorCode code)
    {
        RenderBudget budget = new(1_000_000);
        Assert.Equal(code, Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input("<defs><rect id='s' " + attributes + "/></defs>"), SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Absolute_rectangle_sources_keep_their_existing_size()
    {
        const string shape = "<rect width='8' height='8' fill='red'/>";
        Assert.Equal(Render(Nested(shape, "width='16' height='16'")), Render("<defs><rect id='s' width='8' height='8' fill='red'/></defs>" + Nested("<use href='#s'/>", "width='16' height='16'")));
    }

    [Theory]
    [InlineData("<circle cx='50%' cy='50%' r='3' fill='red'/>")]
    [InlineData("<ellipse cx='50%' cy='50%' rx='3' ry='2' fill='red'/>")]
    public void Existing_relative_oval_replay_keeps_its_viewport_base(string shape)
        => Assert.Equal(Render(Nested(shape, "width='16' height='16'")), Render("<defs>" + shape.Replace(" fill=", " id='s' fill=", StringComparison.Ordinal) + "</defs>" + Nested("<use href='#s'/>", "width='16' height='16'")));

    [Fact]
    public void Denial_cancellation_and_retry_release_reconstructed_geometry()
    {
        const string source = "<defs><rect id='s' width='50%' height='50%' rx='10%' fill='red'/></defs>";
        string content = source + Nested("<use href='#s'/>", "width='16' height='16'");
        byte[] input = Input(content);
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
        Assert.Equal(Render(Nested("<rect width='50%' height='50%' rx='10%' fill='red'/>", "width='16' height='16'")), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static string Nested(string content, string attributes)
        => "<svg " + attributes + ">" + content + "</svg>";

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
