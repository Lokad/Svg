using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAuthoredPathTests
{
    private const string Path = "<path d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/>";
    private const string Contact = "M4 4 C4 1 20 1 20 4 L20 20 C20 23 4 23 4 20 Z M12 8 C12 5 28 5 28 8 L28 24 C28 27 12 27 12 24 Z";
    private const string Retraced = "M4 4 C4 1 20 1 20 4 L20 20 L4 20 L4 4 C4 1 20 1 20 4 L4 20 Z";

    [Theory]
    [InlineData("solid")]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public async Task Bitmap_border_curves_preserve_all_six_outputs(string paint)
    {
        string definition = paint switch
        {
            "solid" => "",
            "linear" => "<defs><linearGradient id='p'><stop stop-color='#40b080'/><stop offset='1' stop-color='#40b080'/></linearGradient></defs>",
            "radial" => "<defs><radialGradient id='p'><stop stop-color='#40b080'/><stop offset='1' stop-color='#40b080'/></radialGradient></defs>",
            "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='12' height='12'><rect width='12' height='12' fill='#40b080'/></pattern></defs>",
            _ => throw new ArgumentOutOfRangeException(nameof(paint)),
        };
        string fill = paint == "solid" ? "#40b080" : "url(#p)";
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='600' height='600' viewBox='0 0 1200 1200'>" + definition
            + "<path fill='" + fill + "' d='M19.137 37.319 C891.619 11.137 23.913 819.619 1091.813 1137.319 L41.137 1121.619 Z'/></svg>");
        SvgRenderOptions options = new(new SvgViewport(600, 600), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public async Task Contained_bitmap_curves_preserve_all_six_outputs(string paint)
    {
        string definition = paint switch
        {
            "solid" => "",
            "linear" => "<defs><linearGradient id='p'><stop stop-color='#40b080'/><stop offset='1' stop-color='#906030'/></linearGradient></defs>",
            "radial" => "<defs><radialGradient id='p'><stop stop-color='#40b080'/><stop offset='1' stop-color='#906030'/></radialGradient></defs>",
            "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='12' height='12'><rect width='12' height='12' fill='#40b080'/></pattern></defs>",
            _ => throw new ArgumentOutOfRangeException(nameof(paint)),
        };
        string fill = paint == "solid" ? "#40b080" : "url(#p)";
        string viewport = paint == "pattern" ? "" : " viewBox='0 0 395.681 402.978'";
        string path = paint == "pattern"
            ? "M640.145 370.923C639.237 350.413 661.783 342.129 670.917 360.627L656.143 358.291L663.617 378.413Z"
            : "M320.145 184.923C319.237 174.413 331.783 171.129 334.917 180.627L328.143 179.291L331.617 188.413Z";
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='792' height='806'" + viewport + ">" + definition
            + "<path fill='" + fill + "' d='" + path + "'/></svg>");
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Fact]
    public void Contained_bitmap_mapping_denial_releases_scratch_and_preserves_source_curves()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='792' height='806'><path d='M640.137 350.319C635.619 343.137 661.913 341.619 671.813 368.319L644.137 377.619Z'/></svg>");
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new ParserWorkCounter(100_000), CancellationToken.None))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            using SvgPreparedPathContours mapped = authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            using SvgPreparedPathContours source = authored.PrepareSource(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            SvgDevicePoint[] originalPoints = source.Points.ToArray();
            SvgCubicSpan[] originalCubics = source.CubicSpans.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            SvgAnalyticContourPreparation preparation = new(mapped.Points, mapped.ContourEnds, [], false, null, SvgPolygonMapping.NativeFillEdges,
                mapped.QuadSpans, mapped.CubicSpans, new(635, 341, 672, 378), 792, 806, source, authored.SceneMap);
            long setup;
            long storage;
            using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 635, 672, 341, 378, new(508, 764), new(508, 254), null,
                SvgAnalyticPaintClip.Binary, 100_000, budget, out long _, out bool eligible, CancellationToken.None))
            {
                Assert.True(eligible);
                storage = budget.Snapshot().CurrentBytes - retained;
            }
            using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 635, 672, 341, 378, new(508, 764), new(508, 254), null,
                SvgAnalyticPaintClip.Binary, 100_000, budget, out setup, out bool eligible, CancellationToken.None)) Assert.True(eligible);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            try
            {
                using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 635, 672, 341, 378, new(508, 764), new(508, 254), null,
                    SvgAnalyticPaintClip.Binary, setup - 1, budget, out long _, out bool _, CancellationToken.None);
                Assert.Fail("Contained mapping and fixed-state translation must fit the work limit.");
            }
            catch (SvgException error) { Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode); }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            RenderBudget tight = new(storage - 1);
            try
            {
                using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 635, 672, 341, 378, new(508, 764), new(508, 254), null,
                    SvgAnalyticPaintClip.Binary, 100_000, tight, out long _, out bool _, CancellationToken.None);
                Assert.Fail("Contained construction must release its edge storage after denial.");
            }
            catch (SvgException error) { Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode); }
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            try
            {
                using SvgAnalyticCoverage canceled = SvgAnalyticCoverage.Prepare(ref preparation, 635, 672, 341, 378, new(508, 764), new(508, 254), null,
                    SvgAnalyticPaintClip.Binary, 100_000, budget, out long _, out bool _, cancellation.Token);
                Assert.Fail("Cached contained construction must observe cancellation.");
            }
            catch (OperationCanceledException error) { Assert.Equal(cancellation.Token, error.CancellationToken); }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.Equal(originalPoints, source.Points);
            Assert.Equal(originalCubics, source.CubicSpans);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Denied_bitmap_curve_construction_releases_scratch_and_preserves_borrowed_sources()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='600' height='600'><path d='M19.137 37.319 C451.619 11.137 23.913 419.619 591.813 537.319 L41.137 521.619 Z'/></svg>");
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new ParserWorkCounter(100_000), CancellationToken.None))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            using SvgPreparedPathContours mapped = authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            using SvgPreparedPathContours source = authored.PrepareSource(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            SvgDevicePoint[] originalPoints = source.Points.ToArray();
            SvgCubicSpan[] originalCubics = source.CubicSpans.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            SvgAnalyticContourPreparation preparation = new(mapped.Points, mapped.ContourEnds, [], false, null, SvgPolygonMapping.NativeFillEdges,
                mapped.QuadSpans, mapped.CubicSpans, new(0, 0, 600, 600), 600, 600, source, authored.SceneMap);
            SvgAnalyticClipContext clip = new(new(0, 254, 256, 510), new(0, 254));
            long setupWork;
            byte[] expected = new byte[256 * 4];
            using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 256, 0, 600, new(0, 256), clip, SvgAnalyticPaintClip.Binary,
                100_000, budget, out setupWork, out bool eligible, CancellationToken.None))
            {
                Assert.True(eligible);
                walker.PrepareRow(287, out _, CancellationToken.None);
                for (int x = 0; x < 256; x++) walker.PaintPixel(x, expected.AsSpan(x * 4, 4), SvgAnalyticPaint.Solid(new(64, 176, 128, 255)), SvgAnalyticClipCoverage.Full);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using (SvgAnalyticCoverage measured = SvgAnalyticCoverage.Prepare(ref preparation, 0, 256, 0, 600, new(0, 256), clip, SvgAnalyticPaintClip.Binary,
                100_000, budget, out setupWork, out bool eligible, CancellationToken.None))
            {
                Assert.True(eligible);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            try
            {
                using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 0, 256, 0, 600, new(0, 256), clip, SvgAnalyticPaintClip.Binary,
                    setupWork - 1, budget, out _, out _, CancellationToken.None);
                Assert.Fail("Expected construction work denial.");
            }
            catch (SvgException error) { Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode); }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            byte[] actual = new byte[expected.Length];
            using (SvgAnalyticCoverage retry = SvgAnalyticCoverage.Prepare(ref preparation, 0, 256, 0, 600, new(0, 256), clip, SvgAnalyticPaintClip.Binary,
                100_000, budget, out _, out bool eligible, CancellationToken.None))
            {
                Assert.True(eligible);
                retry.PrepareRow(287, out _, CancellationToken.None);
                for (int x = 0; x < 256; x++) retry.PaintPixel(x, actual.AsSpan(x * 4, 4), SvgAnalyticPaint.Solid(new(64, 176, 128, 255)), SvgAnalyticClipCoverage.Full);
            }
            Assert.Equal(expected, actual);
            Assert.Equal(originalPoints, source.Points);
            Assert.Equal(originalCubics, source.CubicSpans);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Integer_contact_replay_preserves_contours_with_a_small_scratch_budget(bool isEvenOdd)
    {
        RenderBudget sceneBudget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='" + (isEvenOdd ? "evenodd" : "nonzero") + "' d='M1 1H29V29H1Z M12 1V8H6 M18 22H26V29'/>", sceneBudget))
        {
            SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            SvgAuthoredPathCommand[] commands = path.Commands.ToArray();
            RenderBudget replayBudget = new(16 * 1024);
            using (SvgPreparedPathContours mapped = path.Prepare(SvgAffineTransform.Identity, isEvenOdd, replayBudget, new ParserWorkCounter(100_000), CancellationToken.None))
            {
                Assert.Equal(3, mapped.ContourEnds.Length);
                Assert.Equal(isEvenOdd ? new[] { 1, 1, 1 } : null, mapped.Signs);
                Assert.Empty(mapped.QuadSpans);
                Assert.Empty(mapped.CubicSpans);
            }
            Assert.Equal(commands, path.Commands.ToArray());
            Assert.Equal(0, replayBudget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Round_joined_rectangle_instances_match_direct_strokes()
    {
        const string definition = "<rect id='p' width='16' height='16'/>";
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definition + "</defs><use href='#p' fill='none' stroke='red' stroke-linejoin='round'/></svg>");
        byte[] direct = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='16' height='16' fill='none' stroke='red' stroke-linejoin='round'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Authored_stroke_paint_wins_when_the_instance_supplies_join_mechanics()
    {
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] direct = TextEncoding.UTF8.GetBytes(prefix + "<path d='" + Retraced + "' fill='none' stroke='blue' stroke-linejoin='round'/></svg>");
        byte[] instance = TextEncoding.UTF8.GetBytes(prefix + "<defs><path id='p' d='" + Retraced + "' stroke='blue'/></defs><use href='#p' fill='none' stroke='red' stroke-linejoin='round'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(instance, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(".5", "miter")]
    [InlineData(".5", "round")]
    [InlineData("4", "miter")]
    [InlineData("4", "round")]
    public void Stroked_instances_keep_the_authored_centerline_and_match_direct_paths(string width, string join)
    {
        string style = " fill='none' stroke='red' stroke-width='" + width + "' stroke-linejoin='" + join + "'";
        string direct = "<path d='" + Retraced + "'" + style + "/>";
        string instance = "<defs><path id='p' d='" + Retraced + "'/></defs><use href='#p'" + style + "/>";
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] expected = SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(prefix + direct + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] input = TextEncoding.UTF8.GetBytes(prefix + instance + "</svg>");
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(SvgRenderLimits.Default.MaximumWorkUnits), CancellationToken.None))
        {
            SvgGeometry stroke = Assert.Single(root.Geometries.ToArray(), geometry => geometry.Kind == SvgGeometryKind.PolygonFill && geometry.Polygon.HasFill);
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(stroke.AuthoredPath);
            Assert.NotNull(authored.StrokeStyle);
            Assert.Equal(2, authored.Commands.ToArray().Count(command => command.Verb == SvgAuthoredPathVerb.Cubic));
            Assert.Equal(SvgPolygonMapping.NativeStrokeEdges, stroke.Polygon.Mapping);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false, false, 255)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    public void Coincident_curved_contours_observe_winding_and_parity(bool isEvenOdd, bool reverse, byte alpha)
    {
        const string first = "M4 4 C4 1 20 1 20 4 L20 20 C20 23 4 23 4 20 Z";
        string second = reverse ? "M4 4 L4 20 C4 23 20 23 20 20 L20 4 C20 1 4 1 4 4 Z" : first;
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='" + (isEvenOdd ? "evenodd" : "nonzero") + "' d='" + first + " " + second + "'/>", budget))
        {
            SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
            Assert.Equal(2, polygon.ContourEnds.Length);
            Assert.Equal(isEvenOdd ? new[] { 1, 1 } : null, polygon.Signs);
            using SvgConverter.RasterizedSurface raster = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(alpha, raster.Pixels[(6 * raster.Width + 6) * 4 + 3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<path fill='red' d='" + Contact + "'/>")]
    [InlineData("<path fill='red' fill-rule='evenodd' d='" + Contact + "'/>")]
    [InlineData("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><path fill='url(#g)' d='" + Contact + "'/>")]
    [InlineData("<defs><radialGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><path fill='url(#g)' d='" + Contact + "'/>")]
    [InlineData("<defs><pattern id='g' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='blue'/></pattern></defs><path fill='url(#g)' d='" + Contact + "'/>")]
    [InlineData("<defs><clipPath id='c'><rect x='7.25' y='6.75' width='17.5' height='17.5'/></clipPath><path id='p' d='" + Contact + "'/></defs><use href='#p' x='1.25' y='.75' clip-path='url(#c)' fill='red'/>")]
    [InlineData("<defs><path id='p' d='" + Retraced + "'/></defs><use href='#p' fill='none' stroke='red' stroke-width='4'/>")]
    [InlineData("<path fill='red' d='M1 1H29V29H1Z M12 1V8H6 M18 22H26V29'/>")]
    [InlineData("<path fill='red' fill-rule='evenodd' d='M1 1H29V29H1Z M12 1V8H6 M18 22H26V29'/>")]
    [InlineData("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><path fill='url(#g)' d='M1 1H29V12H12V29H1Z'/>")]
    [InlineData("<defs><radialGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><path fill='url(#g)' d='M1 1H29V12H12V29H1Z'/>")]
    [InlineData("<defs><pattern id='g' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='blue'/></pattern></defs><path fill='url(#g)' d='M1 1H29V12H12V29H1Z'/>")]
    [InlineData("<path fill='red' d='M0 0L4 4L4 0L0 4Z M3 1L2 0L4 0Z'/>")]
    [InlineData("<path fill='red' fill-rule='evenodd' d='M1 1L11 3L4 12Z M7 0L15 5L9 13Z M0 8L6 8L6 14Z'/>")]
    [InlineData("<path fill='red' d='M0 0L2 1L4 0L2 1L2 4Z M4 0L6 0L6 2Z'/>")]
    [InlineData("<path fill='#DC143C' fill-rule='evenodd' d='M5 43h60L5 5v80h60L5 25z'/>")]
    public async Task Directed_contacts_render_through_six_public_outputs(string body)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
        SvgRenderOptions options = SvgRenderOptions.Default;
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Directed_contact_admission_releases_partial_storage_after_work_or_memory_denial(bool isEvenOdd, bool strokeInstance)
    {
        string body = strokeInstance
            ? "<defs><path id='p' d='" + Retraced + "'/></defs><use href='#p' fill='none' stroke='red' stroke-width='4'/>"
            : "<path fill-rule='" + (isEvenOdd ? "evenodd" : "nonzero") + "' d='" + Contact + "'/>";
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget normal = new(limits.MaximumAccountedBytes);
        long work;
        using (SvgRoot root = SvgRootParser.Parse(input, limits, normal, null, new(limits.MaximumWorkUnits), CancellationToken.None)) work = root.ParserWorkUnits;
        Assert.Equal(0, normal.Snapshot().CurrentBytes);
        RenderBudget workBudget = new(limits.MaximumAccountedBytes);
        SvgException workDenied = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, workBudget, null, new(work - 1), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, workDenied.ErrorCode);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
        RenderBudget memoryBudget = new(normal.Snapshot().PeakBytes - 1);
        SvgException memoryDenied = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, memoryBudget, null, new(limits.MaximumWorkUnits), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, memoryDenied.ErrorCode);
        Assert.Equal(0, memoryBudget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException cancelled = Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, limits, workBudget, null, new(limits.MaximumWorkUnits), cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(input, limits, workBudget, null, new(limits.MaximumWorkUnits), CancellationToken.None))
            Assert.NotEmpty(retry.Geometries.ToArray());
        Assert.Equal(0, workBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Crossing_replay_keeps_original_curves_without_scene_surgery(bool isEvenOdd)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='" + (isEvenOdd ? "evenodd" : "nonzero") + "' d='M0,0 C2,2 6,6 8,8 L8,0 C6,2 2,6 0,8 Z'/>", budget))
        {
            SvgGeometry geometry = Assert.Single(root.Geometries);
            Assert.Single(geometry.Polygon.ContourEnds);
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
            using SvgPreparedPathContours mapped = authored.Prepare(SvgAffineTransform.Identity, isEvenOdd, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Single(mapped.ContourEnds);
            Assert.Equal(2, mapped.CubicSpans.Length);
            Assert.Equal(isEvenOdd ? new[] { 1 } : null, mapped.Signs);
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(mapped.Points, mapped.ContourEnds, mapped.Signs ?? [], isEvenOdd, mapped.Signs, SvgPolygonMapping.NativeFillEdges, mapped.QuadSpans, mapped.CubicSpans, 0, 8, 0, 8, 16, 16, budget, out _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(3, out _, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(3, pixel, SvgAnalyticPaint.Solid(new(255, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
            // Three fresh pinned-browser captures record this native crossing.
            Assert.Equal(130, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Even_odd_replay_keeps_all_authored_contours_including_crossings(bool isEvenOdd)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='" + (isEvenOdd ? "evenodd" : "nonzero") + "' d='M0,0 C2,2 6,6 8,8 L8,0 C6,2 2,6 0,8 Z M10,0 C12,2 16,6 18,8 L18,0 C16,2 12,6 10,8 Z'/>", budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            using SvgPreparedPathContours mapped = authored.Prepare(SvgAffineTransform.Identity, isEvenOdd, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Equal(2, mapped.ContourEnds.Length);
            Assert.Equal(isEvenOdd ? new[] { 1, 1 } : null, mapped.Signs);
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(mapped.Points, mapped.ContourEnds, mapped.Signs ?? [], isEvenOdd, mapped.Signs, SvgPolygonMapping.NativeFillEdges, 0, 18, 0, 8, 32, 16, budget, out _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(2, out _, CancellationToken.None);
            foreach (int x in new[] { 1, 11 })
            {
                byte[] pixel = new byte[4];
                walker.PaintPixel(x, pixel, SvgAnalyticPaint.Solid(new(255, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                Assert.Equal(255, pixel[3]);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancelled_even_odd_replay_releases_storage_and_preserves_the_token()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='evenodd' d='M1,1 Q15,1 15,15 L1,15 Z M5,5 Q11,5 11,11 L5,11 Z'/>", budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            OperationCanceledException cancelled = Assert.ThrowsAny<OperationCanceledException>(() => authored.Prepare(SvgAffineTransform.Identity, true, budget, new ParserWorkCounter(100_000), cancellation.Token));
            Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgPreparedPathContours retry = authored.Prepare(SvgAffineTransform.Identity, true, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Equal(new[] { 1, 1 }, retry.Signs);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Denied_crossing_replay_keeps_its_authored_owner_and_releases_scratch()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='evenodd' d='M0,0 C2,2 6,6 8,8 L8,0 C6,2 2,6 0,8 Z'/>", budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            long retained = budget.Snapshot().CurrentBytes;
            using (budget.Reserve(SvgRenderLimits.Default.MaximumAccountedBytes - retained - 100, SvgMemoryCategory.Surface))
            {
                long padded = budget.Snapshot().CurrentBytes;
                SvgException denied = Assert.Throws<SvgException>(() => authored.Prepare(SvgAffineTransform.Identity, true, budget, new ParserWorkCounter(100_000), CancellationToken.None));
                Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
                Assert.Equal(padded, budget.Snapshot().CurrentBytes);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgPreparedPathContours retry = authored.Prepare(SvgAffineTransform.Identity, true, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Single(retry.ContourEnds);
            Assert.Equal(2, retry.CubicSpans.Length);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<defs><path id='p' d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/></defs><use href='#p' fill='red'/><use href='#p' x='1' fill='blue'/>")]
    [InlineData("<defs><symbol id='p' viewBox='0 0 16 16'>" + Path + "</symbol></defs><use href='#p' width='16' height='16' fill='red'/><use href='#p' width='16' height='16' x='1' fill='blue'/>")]
    [InlineData("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><symbol id='p' viewBox='0 0 16 16'><path fill='url(#g)' d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/></symbol></defs><use href='#p' width='16' height='16'/><use href='#p' width='16' height='16' x='1'/>")]
    public void Repeated_instances_keep_independent_authored_storage_and_release_it(string body)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse(body, budget))
        {
            SvgGeometry[] visible = root.Geometries.Where(g => g.Kind != SvgGeometryKind.Invalid).ToArray();
            Assert.Equal(2, visible.Length);
            SvgAuthoredPath first = Assert.IsType<SvgAuthoredPath>(visible[0].AuthoredPath);
            SvgAuthoredPath second = Assert.IsType<SvgAuthoredPath>(visible[1].AuthoredPath);
            Assert.NotSame(first, second);
            Assert.Equal(first.Commands.ToArray(), second.Commands.ToArray());
            Assert.NotEqual(first.SceneMap.E, second.SceneMap.E);
            using var raster = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Object_bounding_box_paint_keeps_scene_coordinates_during_authored_replay()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><g transform='scale(2)'><path fill='url(#g)' d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/></g>", budget))
        {
            SvgGeometry geometry = Assert.Single(root.Geometries);
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
            SvgRectangle sceneBox = geometry.GradientPolygon.Box;
            SvgDevicePoint[] scenePoints = geometry.GradientPolygon.Points.ToArray();
            using SvgPreparedPathContours mapped = authored.Prepare(SvgAffineTransform.FromScaleOffset(.5, .5, 0, 0), false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Equal(sceneBox, geometry.GradientPolygon.Box);
            Assert.Equal(scenePoints, geometry.GradientPolygon.Points);
            Assert.Equal(2, authored.SceneMap.A);
            Assert.Equal((double)(float)1.137, mapped.Points[0].X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Denied_replay_releases_partial_arrays_and_keeps_the_source(long workLimit)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse(Path, budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            long retained = budget.Snapshot().CurrentBytes;
            SvgException denied = Assert.Throws<SvgException>(() => authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(workLimit), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgPreparedPathContours retry = authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.NotEmpty(retry.Points);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Denied_authored_copy_releases_its_replacement_and_preserves_the_source()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse(Path, budget))
        {
            SvgGeometry source = Assert.Single(root.Geometries);
            long retained = budget.Snapshot().CurrentBytes;
            SvgPolygonFill polygon = source.Polygon;
            SvgGeometry replacement = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.Color, polygon.HasFill, polygon.IsEvenOdd, polygon.Mapping, polygon.Clip, null, budget));
            SvgException denied = Assert.Throws<SvgException>(() => source.CopyAuthoredPathTo(replacement, SvgAffineTransform.Identity, budget, new ParserWorkCounter(0), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.NotEmpty(Assert.IsType<SvgAuthoredPath>(source.AuthoredPath).Commands.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Denied_endpoint_arc_replay_releases_scratch_and_can_retry(bool denyWork)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path d='M12,4 A8,8 0 1 1 20,12 Z'/>", budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            long retained = budget.Snapshot().CurrentBytes;
            if (denyWork)
            {
                SvgException denied = Assert.Throws<SvgException>(() => authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(authored.Commands.Length + 2), CancellationToken.None));
                Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            }
            else
            {
                using (budget.Reserve(SvgRenderLimits.Default.MaximumAccountedBytes - retained - 100, SvgMemoryCategory.Surface))
                {
                    long padded = budget.Snapshot().CurrentBytes;
                    SvgException denied = Assert.Throws<SvgException>(() => authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None));
                    Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
                    Assert.Equal(padded, budget.Snapshot().CurrentBytes);
                }
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgPreparedPathContours retry = authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Equal(3, retry.QuadSpans.Length);
            Assert.Empty(retry.CubicSpans);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgRoot Parse(string body, RenderBudget budget)
        => SvgRootParser.Parse(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 32 32'>" + body + "</svg>"), SvgRenderLimits.Default, budget, null, new ParserWorkCounter(SvgRenderLimits.Default.MaximumWorkUnits), CancellationToken.None);
}
