using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNonScalingMarkerTests
{
    public static IEnumerable<object[]> MarkerConsumers()
    {
        foreach (string shape in new[] { "line", "path", "polyline" })
        foreach (string consumer in new[] { "direct", "use", "symbol", "viewport", "pattern", "mask" })
        foreach (string position in shape == "line" ? new[] { "start", "end" } : new[] { "start", "mid", "end" })
        foreach (bool magnified in new[] { false, true })
            yield return [shape, consumer, position, magnified];
    }

    [Theory]
    [MemberData(nameof(MarkerConsumers))]
    public void Non_scaling_stroke_markers_match_explicit_user_space_sizes(string shape, string consumer, string position, bool magnified)
    {
        // The two power-of-two placements have explicit reference widths: a
        // two-pixel stroke uses one source unit at 2x, and four at one half.
        // Native pattern tiles retain their separate source width of two units.
        byte[] expected = SvgConverter.ConvertToPng(Document(shape, consumer, position, magnified, true), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(Document(shape, consumer, position, magnified, false), SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Symbol_viewbox_preserves_non_scaling_line_width_without_markers()
    {
        byte[] expected = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><line x1='12' y1='12' x2='20' y2='12' stroke='black' stroke-width='2'/></svg>");
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><symbol id='s' viewBox='0 0 16 16'><line x1='6' y1='6' x2='10' y2='6' stroke='black' stroke-width='2' vector-effect='non-scaling-stroke'/></symbol></defs><use href='#s' width='32' height='32'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("scale(1 2.6457513110645907)")]
    [InlineData("matrix(2 1 0 1.7320508075688772 0 0)")]
    [InlineData("matrix(-2 -1 0 -1.7320508075688772 32 32)")]
    public void Both_transform_axes_determine_marker_units(string transform)
    {
        // Each map has a two-unit RMS scale, despite its unequal axis lengths.
        byte[] expected = SvgConverter.ConvertToPng(Document("userSpaceOnUse"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(Document("strokeWidth"), SvgRenderOptions.Default, CancellationToken.None).ToArray());

        byte[] Document(string units) => TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><marker id='m' markerUnits='"
            + units + "' markerWidth='4' markerHeight='4' refX='2' refY='2'><rect width='4' height='4' fill='red'/></marker></defs><path d='M6 8H12V14' fill='none' stroke='black' stroke-width='2' vector-effect='non-scaling-stroke' marker-mid='url(#m)' transform='" + transform + "'/></svg>");
    }

    [Theory]
    [InlineData("1e-10")]
    public void Computed_inverse_units_preserve_finite_marker_placement(string scale)
    {
        byte[] expected = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect x='12' y='12' width='8' height='8' fill='red'/></svg>");
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><marker id='m' markerUnits='strokeWidth' markerWidth='4' markerHeight='4' refX='2' refY='2'><rect width='4' height='4' fill='red'/></marker></defs><g transform='translate(16 16) scale("
            + scale + ")'><path d='M0 0H1' fill='none' stroke='none' stroke-width='2' vector-effect='non-scaling-stroke' marker-start='url(#m)'/></g></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("1e-20", false)]
    [InlineData("1e-20", true)]
    [InlineData("1e-38", false)]
    [InlineData("1e-38", true)]
    [InlineData("1e9", false)]
    [InlineData("1e9", true)]
    public void Extreme_host_maps_preserve_native_nonpainting_strokes_and_markers(string scale, bool markers)
    {
        // Independently captured SVGs paint nothing at these host maps. A
        // mathematically cancelled canvas matrix alone still paints a square;
        // this boundary belongs to SVG playback, including plain round strokes.
        string definitions = markers ? "<defs><marker id='m' markerUnits='strokeWidth' markerWidth='4' markerHeight='4' refX='2' refY='2'><rect width='4' height='4' fill='red'/></marker></defs>" : "";
        string paint = markers ? "fill='none' stroke='none' stroke-width='2' vector-effect='non-scaling-stroke' marker-start='url(#m)'"
            : "fill='none' stroke='blue' stroke-width='8' stroke-linecap='round' vector-effect='non-scaling-stroke'";
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + definitions
            + "<g transform='translate(16 16) scale(" + scale + ")'><path d='M0 0H1' " + paint + "/></g></svg>");
        byte[] empty = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'/>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(empty, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("8388607", 16, "M0 0H1", true)]
    [InlineData("8388608", 16, "M0 0H1", false)]
    [InlineData("8388609", 16, "M0 0H1", false)]
    [InlineData("8e-16", 0, "M0 0H1", false)]
    [InlineData("8e-16", 4, "M0 0H1", true)]
    [InlineData("8e-16", 16, "M0 0H1", false)]
    [InlineData("9e-16", 16, "M0 0H1", true)]
    [InlineData("1e9", 16, "M0 0H1M0 1", false)]
    [InlineData("1e9", 16, "M0 0H1M0 1H0", true)]
    [InlineData("1e9", 16, "M0 0H1V1H0Z", true)]
    [InlineData("1e9", 16, "M0 0Q1 0 1 1", true)]
    public void Native_visual_bounds_preserve_clamping_and_nonempty_neighbors(string scale, int origin, string path, bool paints)
    {
        // The boundary depends on source bounds and viewport intersection, not
        // only scale. A trailing move adds no extent; a zero-length drawn
        // subpath does. Each actual SVG/control is independently calibrated.
        string position = origin.ToString(System.Globalization.CultureInfo.InvariantCulture);
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><marker id='m' markerUnits='strokeWidth' markerWidth='4' markerHeight='4' refX='2' refY='2'><rect width='4' height='4' fill='red'/></marker></defs><g transform='translate("
            + position + " " + position + ") scale(" + scale + ")'><path d='" + path + "' fill='none' stroke='none' stroke-width='2' vector-effect='non-scaling-stroke' marker-start='url(#m)'/></g></svg>");
        string coordinate = (origin - 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
        byte[] expected = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
            + (paints ? "<rect x='" + coordinate + "' y='" + coordinate + "' width='8' height='8' fill='red'/>" : "") + "</svg>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("1e7", false, false)]
    [InlineData("1e7", false, true)]
    [InlineData("1e7", true, false)]
    [InlineData("1e7", true, true)]
    [InlineData("1e9", false, false)]
    [InlineData("1e9", false, true)]
    [InlineData("1e9", true, false)]
    [InlineData("1e9", true, true)]
    public void Culled_drawing_retains_eligible_owning_and_ancestor_filters(string scale, bool ancestor, bool flood)
    {
        byte[] input = FilteredBoundary(scale, ancestor, flood);
        byte[] expected = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
            + (flood ? "<rect width='32' height='32' fill='green'/>" : "") + "</svg>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Culled_drawing_preserves_borrowed_budget_on_denial_cancellation_and_retry(bool ancestor, bool flood)
    {
        byte[] input = FilteredBoundary("1e9", ancestor, flood);
        RenderBudget budget = new(1_000_000);
        long peak;
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
            {
                peak = budget.Snapshot().PeakBytes;
                using SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
                Assert.Equal(flood ? (byte)255 : (byte)0, raster.Pixels[3]);
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input,
                SvgRenderLimits.Default, budget, null, cancellation.Token)).CancellationToken);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(peak - 1);
        using (RenderBudget.BudgetReservation borrowed = denied.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input,
                SvgRenderLimits.Default, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("1", false, "pattern", 18, 8, 4, 4)]
    [InlineData("1", false, "mask", 16, 6, 8, 8)]
    [InlineData("1", false, "symbol", 16, 6, 8, 8)]
    [InlineData("1", true, "pattern", 18, 8, 4, 4)]
    [InlineData("1", true, "mask", 16, 6, 8, 8)]
    [InlineData("1", true, "symbol", 16, 6, 8, 8)]
    [InlineData("1e-20", false, "pattern", 6, 6, 4, 4)]
    [InlineData("1e-20", false, "mask", 4, 4, 8, 8)]
    [InlineData("1e-20", false, "symbol", 0, 0, 0, 0)]
    [InlineData("1e-20", true, "pattern", 6, 6, 4, 4)]
    [InlineData("1e-20", true, "mask", 4, 4, 8, 8)]
    [InlineData("1e-20", true, "symbol", 0, 0, 0, 0)]
    [InlineData("1e+07", false, "pattern", 0, 0, 0, 0)]
    [InlineData("1e+07", false, "mask", 4, 4, 8, 8)]
    [InlineData("1e+07", false, "symbol", 4, 4, 8, 8)]
    [InlineData("1e+07", true, "pattern", 6, 6, 4, 4)]
    [InlineData("1e+07", true, "mask", 4, 4, 8, 8)]
    [InlineData("1e+07", true, "symbol", 4, 4, 8, 8)]
    public void Resource_pictures_preserve_native_local_and_document_bounds(string scale, bool own, string consumer, int x, int y, int width, int height)
    {
        // Picture recording can paint outside document drawing bounds. Its
        // local empty-bounds check still distinguishes a source's own map.
        string transform = scale == "1" ? "" : "translate(16 16) scale(" + scale + ")";
        string path = scale == "1" ? "M40 20H44" : "M0 0H1";
        string source = "<path d='" + path + "' fill='none' stroke='none' stroke-width='2' vector-effect='non-scaling-stroke' marker-start='url(#m)'"
            + (own ? " transform='" + transform + "'" : "") + "/>";
        if (!own) source = "<g transform='" + transform + "'>" + source + "</g>";
        string resource = consumer switch
        {
            "pattern" => "<pattern id='r' patternUnits='userSpaceOnUse' width='32' height='32' viewBox='0 0 64 64'>" + source + "</pattern>",
            "mask" => "<mask id='r' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g transform='scale(.5)'>" + source + "</g></mask>",
            _ => "<symbol id='r' viewBox='0 0 64 64'>" + source + "</symbol>"
        };
        string body = consumer switch
        {
            "pattern" => "<rect width='32' height='32' fill='url(#r)'/>",
            "mask" => "<rect width='32' height='32' fill='red' mask='url(#r)'/>",
            _ => "<use href='#r' width='32' height='32'/>"
        };
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><marker id='m' markerUnits='strokeWidth' markerWidth='4' markerHeight='4' refX='2' refY='2'><rect width='4' height='4' fill='red'/></marker>" + resource + "</defs>" + body + "</svg>");
        byte[] expected = TextEncoding.UTF8.GetBytes(FormattableString.Invariant($"<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect x='{x}' y='{y}' width='{width}' height='{height}' fill='red'/></svg>"));
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("8388607", "M0 0H1", 158)]
    [InlineData("8388607", "M0 0H1H2", 158)]
    [InlineData("8388607", "M0 0H1Z", 128)]
    [InlineData("8388607", "M0 0Q1 0 2 0", 158)]
    [InlineData("8388608", "M0 0H1", 0)]
    [InlineData("8388608", "M0 0H1H2", 158)]
    [InlineData("8388608", "M0 0H1Z", 128)]
    [InlineData("8388608", "M0 0Q1 0 2 0", 158)]
    [InlineData("10000000", "M0 0H1", 0)]
    [InlineData("10000000", "M0 0H1H2", 158)]
    [InlineData("10000000", "M0 0H1Z", 128)]
    [InlineData("10000000", "M0 0Q1 0 2 0", 158)]
    [InlineData("33554431", "M0 0H1", 0)]
    [InlineData("33554431", "M0 0H1H2", 0)]
    [InlineData("33554431", "M0 0H1Z", 0)]
    [InlineData("33554431", "M0 0Q1 0 2 0", 0)]
    [InlineData("33554432", "M0 0H1", 0)]
    [InlineData("33554432", "M0 0H1H2", 0)]
    [InlineData("33554432", "M0 0H1Z", 0)]
    [InlineData("33554432", "M0 0Q1 0 2 0", 0)]
    [InlineData("1000000000", "M0 0H1", 0)]
    [InlineData("1000000000", "M0 0H1H2", 0)]
    [InlineData("1000000000", "M0 0H1Z", 0)]
    [InlineData("1000000000", "M0 0Q1 0 2 0", 0)]
    public void Native_control_bounds_keep_path_miters_distinct_from_single_lines(string scale, string path, int expectedPixels)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='translate(16 16) scale(" + scale + ")'><path d='" + path + "' fill='none' stroke='blue' stroke-width='8' stroke-linecap='round' vector-effect='non-scaling-stroke'/></g></svg>");
        RenderBudget budget = new(1_000_000);
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        int pixels = 0;
        for (int index = 3; index < raster.Pixels.Length; index += 4) if (raster.Pixels[index] != 0) pixels++;
        Assert.Equal(expectedPixels, pixels);
    }

    private static byte[] FilteredBoundary(string scale, bool ancestor, bool flood)
    {
        string filter = flood ? "<feFlood flood-color='green'/>" : "<feOffset dx='0' dy='0'/>";
        string shape = "<path d='M0 0H1' fill='none' stroke='none' stroke-width='2' vector-effect='non-scaling-stroke' marker-start='url(#m)'"
            + (ancestor ? "" : " filter='url(#f)'") + "/>";
        if (ancestor) shape = "<g filter='url(#f)'>" + shape + "</g>";
        return TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><marker id='m' markerUnits='strokeWidth' markerWidth='4' markerHeight='4' refX='2' refY='2'><rect width='4' height='4' fill='red'/></marker><filter id='f' filterUnits='userSpaceOnUse' x='-16' y='-16' width='32' height='32'>"
            + filter + "</filter></defs><g transform='translate(16 16) scale(" + scale + ")'>" + shape + "</g></svg>");
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("use")]
    [InlineData("symbol")]
    [InlineData("viewport")]
    [InlineData("pattern")]
    [InlineData("mask")]
    public void Marker_replay_releases_owners_on_denial_cancellation_and_retry(string consumer)
    {
        byte[] input = Document("path", consumer, "mid", true, false);
        RenderBudget budget = new(1_000_000);
        long parserPeak;
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
            {
                parserPeak = budget.Snapshot().PeakBytes;
                using SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
                Assert.Equal((byte)255, raster.Pixels[(12 * 32 + 20) * 4 + 3]);
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input,
                SvgRenderLimits.Default, budget, null, cancellation.Token)).CancellationToken);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(parserPeak - 1);
        using (RenderBudget.BudgetReservation borrowed = denied.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input,
                SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    private static byte[] Document(string shape, string consumer, string position, bool magnified, bool explicitSize)
    {
        string scale = magnified ? "2" : ".5";
        string extent = magnified ? "16" : "64";
        string markerScale = !explicitSize ? "1" : consumer == "pattern" ? "2" : magnified ? "1" : "4";
        string markerExtent = !explicitSize ? "4" : consumer == "pattern" ? "8" : magnified ? "4" : "16";
        string reference = !explicitSize ? "2" : consumer == "pattern" ? "4" : magnified ? "2" : "8";
        string marker = "<marker id='m' markerUnits='" + (explicitSize ? "userSpaceOnUse" : "strokeWidth")
            + "' markerWidth='" + markerExtent + "' markerHeight='" + markerExtent + "' refX='" + reference + "' refY='" + reference
            + "'><path d='M0 0H4V4H0Z' fill='red' transform='scale(" + markerScale + ")'/></marker>";
        string source = shape switch
        {
            "line" => "<line x1='6' y1='6' x2='10' y2='6'",
            "path" => "<path d='M6 6H10V10'",
            _ => "<polyline points='6 6 10 6 10 10'"
        };
        source += " id='q' fill='none' stroke='black' stroke-width='2' vector-effect='non-scaling-stroke' marker-" + position + "='url(#m)'/>";
        string definitions = marker;
        string placed;
        switch (consumer)
        {
            case "use": definitions += source; placed = "<use href='#q' transform='scale(" + scale + ")'/>"; break;
            case "symbol": definitions += "<symbol id='s' viewBox='0 0 " + extent + " " + extent + "'>" + source + "</symbol>";
                placed = "<use href='#s' width='32' height='32'/>"; break;
            case "viewport": placed = "<svg width='32' height='32' viewBox='0 0 " + extent + " " + extent + "'>" + source + "</svg>"; break;
            case "pattern": definitions += "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32' viewBox='0 0 " + extent + " " + extent + "'>" + source + "</pattern>";
                placed = "<rect width='32' height='32' fill='url(#p)'/>"; break;
            case "mask": definitions += "<mask id='k' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g transform='scale(" + scale + ")'>" + source + "</g></mask>";
                placed = "<rect width='32' height='32' fill='red' mask='url(#k)'/>"; break;
            default: placed = "<g transform='scale(" + scale + ")'>" + source + "</g>"; break;
        }
        return TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + placed + "</svg>");
    }
}
