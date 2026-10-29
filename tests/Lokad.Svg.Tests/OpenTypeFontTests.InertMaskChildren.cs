using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> InertMaskChildren()
    {
        foreach (string shape in new[] { "rect", "circle", "ellipse", "path", "use" })
        foreach (string suppression in new[] { "display", "visibility", "fill", "geometry" })
        foreach (string siblings in new[] { "none", "first", "last" })
        foreach (bool alpha in new[] { false, true }) yield return [shape, suppression, siblings, alpha];
    }

    [Theory]
    [MemberData(nameof(InertMaskChildren))]
    public async Task Inert_mask_children_preserve_siblings_independently_of_leaf_fast_paths(string shape, string suppression, string siblings, bool alpha)
    {
        string hidden = Shape();
        const string white = "<rect x='4' width='4' height='8' fill='white'/>";
        string content = siblings == "none" ? hidden : siblings == "first" ? hidden + white : white + hidden;
        byte[] input = Svg(content), expectedInput = Svg(siblings == "none" ? "" : white);
        byte[] expected = Render(expectedInput);
        Assert.Equal(expected, Render(input));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        string Shape()
        {
            string attribute = suppression == "display" ? "display='none'" : suppression == "visibility" ? "visibility='hidden'"
                : suppression == "fill" ? "fill='none'" : "";
            string fill = suppression == "fill" ? "" : " fill='white'";
            return shape switch
            {
                "rect" => "<rect width='" + (suppression == "geometry" ? "0" : "8") + "' height='8' " + attribute + fill + "/>",
                "circle" => "<circle cx='4' cy='4' r='" + (suppression == "geometry" ? "0" : "4") + "' " + attribute + fill + "/>",
                "ellipse" => "<ellipse cx='4' cy='4' rx='" + (suppression == "geometry" ? "0" : "4") + "' ry='3' " + attribute + fill + "/>",
                "path" => "<path d='" + (suppression == "geometry" ? "" : "M0 0H8V8H0Z") + "' " + attribute + fill + "/>",
                _ => "<use href='#" + (suppression == "geometry" ? "empty" : "source") + "' " + attribute + fill + "/>"
            };
        }
        byte[] Svg(string children)
            => Document("<defs><path id='source' d='M0 0H8V8H0Z'/><path id='empty' d=''/><mask id='m' mask-type='" + (alpha ? "alpha" : "luminance") + "'>" + children + "</mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/>");
    }

    [Theory]
    [InlineData("rect", false)]
    [InlineData("rect", true)]
    [InlineData("circle", false)]
    [InlineData("circle", true)]
    public void Stroke_only_mask_content_uses_the_existing_ordered_replay(string shape, bool alpha)
    {
        string leaf = shape == "rect" ? "<rect x='1' y='1' width='6' height='6' fill='none' stroke='white' stroke-width='2'/>"
            : "<circle cx='4' cy='4' r='3' fill='none' stroke='white' stroke-width='2'/>";
        byte[] Svg(string content) => Document("<defs><mask id='m' mask-type='" + (alpha ? "alpha" : "luminance") + "'>" + content + "</mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/>");
        Assert.Equal(Render(Svg("<g>" + leaf + "</g>")), Render(Svg(leaf)));
    }

    [Fact]
    public void Inert_mask_replay_observes_denial_cancellation_and_owner_cleanup()
    {
        byte[] input = Document("<defs><mask id='m'><circle r='0' fill='white'/><rect width='8' height='8' visibility='hidden' fill='white'/></mask></defs><rect width='8' height='8' mask='url(#m)'/>");
        RenderBudget measured = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) Assert.False(root.HasUnsupportedChild);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, cancellation.Token));
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }
}
