using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgLeafClipPlacementTests
{
    private const string Clip = "<defs><clipPath id='c'><rect width='8' height='8'/></clipPath></defs>";

    [Theory]
    [InlineData("rect")]
    [InlineData("rounded-rect")]
    [InlineData("path")]
    [InlineData("polygon")]
    [InlineData("circle")]
    [InlineData("ellipse")]
    [InlineData("line")]
    [InlineData("image")]
    public void Leaf_clips_share_group_placement_in_both_definition_orders(string kind)
    {
        string shape = Shape(kind);
        foreach (string transform in new[] { "translate(8 8)", "matrix(2 0 0 1 4 8)", "matrix(-1 0 0 1 24 8)", "matrix(1 .25 0 1 8 8)", "translate(0 0)" })
        {
            byte[] expected = Render(Clip + "<g transform='" + transform + "' clip-path='url(#c)'>" + shape + "/></g>");
            foreach (bool ownTransform in new[] { false, true })
            foreach (bool forward in new[] { false, true })
            {
                string clipped = shape + " clip-path='url(#c)'" + (ownTransform ? " transform='" + transform + "'" : "") + "/>";
                string placed = ownTransform ? clipped : "<g transform='" + transform + "'>" + clipped + "</g>";
                Assert.Equal(expected, Render(forward ? placed + Clip : Clip + placed));
            }
        }
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public void Fractional_clips_preserve_paint_maps_parent_clips_and_nested_viewports(string paint)
    {
        string resource = paint switch
        {
            "linear" => "<linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            "pattern" => "<pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='4' fill='red'/></pattern>",
            _ => throw new ArgumentOutOfRangeException(nameof(paint)),
        };
        const string clip = "<defs><clipPath id='c'><rect x='.25' y='.5' width='8.5' height='7.25'/></clipPath></defs>";
        string resources = "<defs>" + resource + "<clipPath id='outer'><rect x='10' width='18' height='32'/></clipPath></defs>";
        foreach (string transform in new[] { "translate(8 8)", "matrix(2 0 0 1 4 8)", "matrix(-1 0 0 1 24 8)", "matrix(1 .25 0 1 8 8)" })
        foreach (bool viewport in new[] { false, true })
        {
            string drawing = "<g clip-path='url(#outer)' opacity='.625'>" + (viewport ? "<svg x='2' y='1' width='26' height='26' viewBox='0 0 26 26'>" : "")
                + "<g transform='" + transform + "'><rect x='-1' y='-1' width='18' height='18' fill='url(#p)' clip-path='url(#c)'/></g>"
                + (viewport ? "</svg>" : "") + "</g>";
            Assert.Equal(Render(resources + drawing + clip), Render(resources + clip + drawing));
        }
    }

    [Theory]
    [InlineData("rect")]
    [InlineData("circle")]
    public void Clip_placement_controls_cover_local_instances_and_pattern_content(string kind)
    {
        string shape = Shape(kind);
        string reference = "<defs>" + shape + " id='s'/></defs>";
        foreach (string transform in new[] { "translate(8 8)", "matrix(2 0 0 1 4 8)", "matrix(-1 0 0 1 24 8)", "matrix(1 .25 0 1 8 8)" })
        foreach (string viewport in new[] { "", "<svg x='2' y='1' width='28' height='28'>", "<svg width='32' height='32' viewBox='0 0 16 16'>" })
        {
            string end = viewport.Length == 0 ? string.Empty : "</svg>";
            string use = viewport + "<use href='#s' transform='" + transform + "' clip-path='url(#c)'/>" + end;
            byte[] control = Render(Clip + viewport + "<g transform='" + transform + "' clip-path='url(#c)'>" + shape + "/></g>" + end);
            foreach (bool sourceForward in new[] { false, true })
            {
                string drawing = sourceForward ? use + reference : reference + use;
                Assert.Equal(control, Render(Clip + drawing));
                Assert.Equal(control, Render(drawing + Clip));
            }
        }
        byte[] expected = Render(Clip + "<g transform='translate(8 8)' clip-path='url(#c)'>" + shape + "/></g>");
        string tile = "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'><g transform='translate(8 8)'>" + shape + " clip-path='url(#c)'/></g></pattern></defs>";
        const string target = "<rect width='32' height='32' fill='url(#p)'/>";
        Assert.Equal(expected, Render(Clip + tile + target));
        Assert.Equal(expected, Render(tile + Clip + target));
    }

    [Theory]
    [InlineData("translate(8 8)")]
    [InlineData("matrix(1 .25 0 1 8 8)")]
    public void Transformed_instance_clips_release_storage_after_denial_and_allow_retry(string transform)
    {
        string content = Clip + "<defs><rect id='s' width='16' height='16' fill='red'/></defs><use href='#s' transform='" + transform + "' clip-path='url(#c)'/>";
        byte[] input = Input(content);
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(Render(Clip + "<g transform='" + transform + "' clip-path='url(#c)'><rect width='16' height='16' fill='red'/></g>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Deferred_placement_releases_owners_on_denial_cancellation_and_missing_references()
    {
        const string drawing = "<rect width='16' height='16' fill='red' transform='translate(8 8)' clip-path='url(#c)'/>";
        byte[] input = Input(Clip + drawing);
        RenderBudget measured = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        RenderBudget missing = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(drawing), SvgRenderLimits.Default, missing, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, missing.Snapshot().CurrentBytes);
        Assert.Equal(Render("<rect x='8' y='8' width='8' height='8' fill='red'/>"), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static string Shape(string kind) => kind switch
    {
        "rect" => "<rect width='16' height='16' fill='red'",
        "rounded-rect" => "<rect width='16' height='16' rx='3' fill='red'",
        "path" => "<path d='M0 0H16V16H0Z' fill='red'",
        "polygon" => "<polygon points='0,0 16,0 16,16 0,16' fill='red'",
        "circle" => "<circle cx='8' cy='8' r='6' fill='red'",
        "ellipse" => "<ellipse cx='8' cy='8' rx='6' ry='4' fill='red'",
        "line" => "<line x1='0' y1='4' x2='16' y2='4' stroke='red' stroke-width='4'",
        "image" => "<image width='16' height='16' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(1, 1, [255, 0, 0, 255], CancellationToken.None)) + "'",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>");

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(Input(content), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
