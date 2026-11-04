using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed class SvgSingularTransformTests
{
    [Theory]
    [InlineData("scale(0)")]
    [InlineData("scale(0 1)")]
    [InlineData("scale(1 -0)")]
    [InlineData("matrix(0 0 0 1 24 20)")]
    [InlineData("matrix(1 1 1 1 20 20)")]
    [InlineData("translate(4 5) scale(0) rotate(30)")]
    [InlineData("rotate(30) scale(0 1) translate(4 5)")]
    [Trait("Layer", "Mechanism")]
    public void Finite_singular_lists_keep_their_map_without_inventing_an_inverse(string transform)
    {
        SvgAffineTransform map = SvgGeometrySyntax.ParseTransform(transform, CancellationToken.None);
        Assert.True(map.IsSingular);
        Assert.False(map.TryInvert(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Mechanism")]
    public void Collapsed_sources_retain_object_bounds_without_paint_geometry_or_scope_ownership(bool instance)
    {
        string rectangle = "<rect x='1' y='1' width='4' height='4'";
        string transform = " transform='matrix(1 1 1 1 20 20)'/>";
        byte[] input = Document("<defs><filter id='f' x='0' y='0' width='1' height='1'><feFlood flood-color='blue'/></filter>"
            + (instance ? rectangle + " id='s'/>" : "") + "</defs><g filter='url(#f)'><rect x='8' y='8' width='8' height='8'/>"
            + (instance ? "<use href='#s'" : rectangle) + transform + "</g>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            Assert.Equal(2, root.Geometries.Length);
            SvgGeometry source = root.Geometries[1];
            SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(root.SceneStorage);
            SvgRecordedPaintValue paint = storage.RecordedPaintValues[source.RecordedPaint.Identity - 1];
            Assert.True(paint.IsEffectAnchor);
            Assert.False(paint.IsScopeAnchor);
            Assert.Equal(new SvgPaintBounds(22, 22, 30, 30), paint.ObjectBounds);
            Assert.Null(paint.VisualBounds);
            Assert.Null(source.AuthoredPath);
            Assert.False(source.Rectangle.HasFill);
            Assert.Null(source.Rectangle.Stroke);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("group")]
    [InlineData("use")]
    [InlineData("pattern")]
    [Trait("Layer", "Mechanism")]
    public void Collapsed_placement_suppresses_unused_font_requests(string role)
    {
        string text = "<text x='2' y='14' font-family='Example' font-size='12'>Ag</text>";
        string body = role switch
        {
            "direct" => text.Replace("<text", "<text transform='scale(0)'", StringComparison.Ordinal),
            "group" => "<g transform='scale(0)'>" + text + "</g>",
            "use" => "<defs>" + text.Replace("<text", "<text id='s'", StringComparison.Ordinal) + "</defs><use href='#s' transform='scale(0)'/>",
            _ => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + text + "</pattern></defs><rect width='32' height='32' fill='url(#p)' transform='scale(0)'/>"
        };
        Assert.Empty(SvgFontRequestDiscovery.Discover(Document(body), CancellationToken.None));
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Enclosing_filter_bounds_and_live_aliases_preserve_font_requirements()
    {
        string text = "<text x='2' y='14' font-family='Example' font-size='12'>Ag</text>";
        byte[] bounds = Document("<defs><filter id='f'><feFlood flood-color='blue'/></filter></defs><g filter='url(#f)'><g transform='scale(0)'>" + text + "</g></g>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(bounds, CancellationToken.None)).Family);
        byte[] alias = Document("<g transform='scale(0)'>" + text.Replace("<text", "<text id='s'", StringComparison.Ordinal) + "</g><use href='#s'/>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(alias, CancellationToken.None)).Family);
    }

    private static byte[] Document(string body)
        => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");

    [Fact]
    [Trait("Layer", "Contract")]
    public void Collapsed_bounds_and_parent_filter_output_release_owners_on_refusal_cancellation_and_retry()
    {
        byte[] input = Document("<defs><filter id='f' x='0' y='0' width='1' height='1'><feFlood flood-color='blue'/></filter></defs>"
            + "<g filter='url(#f)'><rect x='8' y='8' width='8' height='8'/><path d='M1 1H5V5H1Z' stroke='red' stroke-width='2' vector-effect='non-scaling-stroke' transform='matrix(1 1 1 1 20 20)'/></g>");
        SvgRasterOwnershipAssertions.SharedBudgetReleasesAfterFailures(input, SvgRenderOptions.Default, 1_000_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Mechanism")]
    public void Collapsed_paths_keep_aggregate_geometry_command_accounting(bool hiddenByDisplay)
    {
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(defaults.MaximumInputBytes, defaults.MaximumDimension, defaults.MaximumPixels,
            defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, 6);
        byte[] input = Document("<g transform='scale(0)'" + (hiddenByDisplay ? " display='none'" : "")
            + "><path d='M1 1L2 2'/><path d='M1 1L2 2'/><path d='M1 1L2 2'/></g>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, budget, null, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
