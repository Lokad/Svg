using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgImageSourceEffectsTests
{
    private const string Resources = "<defs><clipPath id='c'><rect width='6' height='32'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='6' height='32' fill='white' opacity='.5'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter></defs>";

    [Theory]
    [InlineData(false, "opacity='.5'")]
    [InlineData(true, "opacity='.5'")]
    [InlineData(false, "clip-path='url(#c)'")]
    [InlineData(true, "clip-path='url(#c)'")]
    [InlineData(false, "mask='url(#m)'")]
    [InlineData(true, "mask='url(#m)'")]
    [InlineData(false, "filter='url(#f)'")]
    [InlineData(true, "filter='url(#f)'")]
    [InlineData(false, "opacity='.5' clip-path='url(#c)' mask='url(#m)' filter='url(#f)'")]
    [InlineData(true, "opacity='.5' clip-path='url(#c)' mask='url(#m)' filter='url(#f)'")]
    public void Referenced_images_keep_source_effects_through_viewport_clipping(bool varied, string effect)
    {
        string image = Image(varied, effect);
        string definition = "<defs>" + image + " id='s'/></defs>";
        foreach (string viewport in new[] { "", "<svg width='16' height='16'>", "<svg x='3' y='2' width='24' height='20' viewBox='0 0 12 10'>" })
        foreach (bool forward in new[] { false, true })
        {
            string end = viewport.Length == 0 ? "" : "</svg>";
            string use = viewport + "<use href='#s'/>" + end;
            Assert.True(Render(Resources + viewport + image + "/>" + end).AsSpan().SequenceEqual(Render(Resources + (forward ? use + definition : definition + use))),
                effect + "; viewport=" + viewport + "; forward=" + forward);
        }
    }

    [Theory]
    [InlineData("translate(2 3)")]
    [InlineData("scale(1.5 1)")]
    [InlineData("matrix(-1 0 0 1 16 0)")]
    public void Filtered_image_instances_keep_the_same_placement_as_direct_images(string transform)
    {
        string image = Image(true, "filter='url(#f)'");
        string attributes = " transform='" + transform + "'/>";
        Assert.Equal(Render(Resources + "<svg width='16' height='16'>" + image + attributes + "</svg>"),
            Render(Resources + "<defs>" + image + " id='s'/></defs><svg width='16' height='16'><use href='#s'" + attributes + "</svg>"));
    }

    [Theory]
    [InlineData(-4, 0, 4, 0)]
    [InlineData(12, 0, -8, 0)]
    [InlineData(0, -4, 0, 4)]
    [InlineData(0, 12, 0, -8)]
    public void Viewport_clips_filter_output_after_source_pixels_move_inside(int x, int y, int dx, int dy)
    {
        string filter = "<defs><filter id='f' filterUnits='userSpaceOnUse' x='-16' y='-16' width='64' height='64'><feOffset dx='" + dx + "' dy='" + dy + "'/></filter></defs>";
        string image = Image(false, "x='" + x + "' y='" + y + "' filter='url(#f)'");
        string control = "<svg width='16' height='16'><rect x='" + (x + dx) + "' y='" + (y + dy) + "' width='8' height='8' fill='red'/></svg>";
        Assert.Equal(Render(control), Render(filter + "<svg width='16' height='16'>" + image + "/></svg>"));
        Assert.Equal(Render(control), Render(filter + "<defs>" + image + " id='s'/></defs><svg width='16' height='16'><use href='#s'/></svg>"));
    }

    [Fact]
    public void Replayed_image_effect_owners_release_on_denial_cancellation_and_retry()
    {
        string image = Image(true, "opacity='.5' clip-path='url(#c)' mask='url(#m)' filter='url(#f)'");
        byte[] input = Input(Resources + "<defs>" + image + " id='s'/></defs><svg width='16' height='16'><use href='#s'/></svg>");
        RenderBudget measured = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(1_000_000);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, cancellation.Token)).CancellationToken);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        Assert.Equal(Render(Resources + "<svg width='16' height='16'>" + image + "/></svg>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static string Image(bool varied, string attributes)
    {
        byte[] pixels = varied ? [255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 0, 255] : [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        return "<image width='8' height='8' preserveAspectRatio='none' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(2, 2, pixels, CancellationToken.None)) + "' " + attributes;
    }

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
