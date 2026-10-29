using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgImageInstanceViewportTests
{
    [Theory]
    [InlineData(false, "width='50%' height='50%'")]
    [InlineData(true, "width='50%' height='50%'")]
    [InlineData(false, "x='50%' y='50%' width='25%' height='25%'")]
    [InlineData(true, "x='50%' y='50%' width='25%' height='25%'")]
    [InlineData(false, "x='25%' y='25%' width='8' height='8'")]
    [InlineData(true, "x='25%' y='25%' width='8' height='8'")]
    [InlineData(false, "width='8' height='8'")]
    [InlineData(true, "width='8' height='8'")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public void Instances_resolve_lengths_in_the_live_viewport(bool varied, string geometry)
    {
        string image = Image(varied, geometry);
        string definition = "<defs>" + image + " id='s'/></defs>";
        foreach (string viewport in new[] { "", "<svg width='16' height='16'>", "<svg width='32' height='32' viewBox='0 0 16 16'>", "<svg x='3' y='2' width='24' height='20' viewBox='0 0 12 10'>" })
        foreach (bool forward in new[] { false, true })
        foreach (string dimensions in new[] { "", " width='4' height='4'" })
        {
            string end = viewport.Length == 0 ? string.Empty : "</svg>";
            string use = viewport + "<use href='#s'" + dimensions + "/>" + end;
            Assert.True(Render(viewport + image + "/>" + end).AsSpan().SequenceEqual(Render(forward ? use + definition : definition + use)),
                "viewport=" + viewport + "; forward=" + forward + "; use dimensions=" + dimensions);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_definition_viewport_does_not_size_later_instances(bool varied)
    {
        string image = Image(varied, "width='50%' height='50%'");
        string definition = "<svg width='16' height='16'><defs>" + image + " id='s'/></defs></svg>";
        Assert.Equal(Render(image + "/>"), Render(definition + "<use href='#s'/>"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Symbols_resolve_image_lengths_with_and_without_viewbox(bool varied, bool viewBox)
    {
        string image = Image(varied, "width='50%' height='50%'");
        string box = viewBox ? " viewBox='0 0 16 16'" : string.Empty;
        string definition = "<defs><symbol id='s'" + box + ">" + image + "/></symbol></defs>";
        foreach (int size in new[] { 16, 24 })
            Assert.Equal(Render("<svg width='" + size + "' height='" + size + "'" + box + ">" + image + "/></svg>"),
                Render(definition + "<use href='#s' width='" + size + "' height='" + size + "'/>"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Referenced_groups_keep_the_live_image_viewport_in_both_definition_orders(bool varied)
    {
        string image = Image(varied, "width='50%' height='50%'");
        const string viewport = "<svg width='16' height='16'>";
        string definition = "<defs><g id='s'>" + image + "/></g></defs>";
        foreach (bool forward in new[] { false, true })
        {
            string use = viewport + "<use href='#s'/></svg>";
            Assert.Equal(Render(viewport + image + "/></svg>"), Render(forward ? use + definition : definition + use));
        }
    }

    [Theory]
    [InlineData("opacity='.5'")]
    [InlineData("clip-path='url(#c)'")]
    [InlineData("mask='url(#m)'")]
    [InlineData("filter='url(#f)'")]
    public void Live_image_lengths_keep_placement_and_composition(string effect)
    {
        const string resources = "<defs><clipPath id='c'><rect width='6' height='16'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='6' height='32' fill='white'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter></defs>";
        bool filtered = effect.StartsWith("filter", StringComparison.Ordinal);
        // Filters stay on the image source; filters on a use require group source bounds.
        string image = Image(true, "width='50%' height='50%'" + (filtered ? " " + effect : ""));
        string definition = "<defs>" + image + " id='s'/></defs>";
        const string viewport = "<svg width='16' height='16'>";
        foreach (string transform in new[] { "translate(2 3)", "scale(1.5 1)", "matrix(-1 0 0 1 16 0)" })
        {
            string attrs = " transform='" + transform + "' " + (filtered ? "" : effect) + "/>";
            string expected = resources + viewport + image + attrs + "</svg>";
            Assert.True(Render(expected).AsSpan().SequenceEqual(
                Render(resources + definition + viewport + "<use href='#s'" + attrs + "</svg>")), effect + "; " + transform);
        }
    }

    [Fact]
    public void Replayed_image_owners_release_on_denial_cancellation_and_retry()
    {
        string image = Image(true, "width='50%' height='50%'");
        byte[] input = Input("<defs>" + image + " id='s'/></defs><svg width='16' height='16'><use href='#s'/></svg>");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(Render("<svg width='16' height='16'>" + image + "/></svg>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static string Image(bool varied, string geometry)
    {
        byte[] pixels = varied ? [255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 0, 255] : [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        return "<image " + geometry + " preserveAspectRatio='none' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(2, 2, pixels, CancellationToken.None)) + "'";
    }

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
