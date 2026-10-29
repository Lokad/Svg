using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgEmptyContourMappingTests
{
    public static IEnumerable<object[]> EmptySources()
    {
        foreach (string shape in new[] { "circle", "ellipse", "ellipse-fallback", "empty-path", "white-path", "polygon", "polyline", "move" })
        foreach (string paint in new[] { "solid", "linear", "radial", "pattern" })
        foreach (string role in new[] { "fill", "stroke", "both" })
        foreach (string consumer in new[] { "direct", "use", "symbol" })
        foreach (bool translated in new[] { false, true }) yield return [shape, paint, role, consumer, translated];
    }

    [Theory]
    [MemberData(nameof(EmptySources))]
    [Trait("Layer", "Rendering")]
    public void Empty_sources_preserve_blank_pixels_through_paint_and_instance_mapping(string shape, string paint, string role, string consumer, bool translated)
    {
        string start = shape switch
        {
            "circle" => "<circle cx='16' cy='16' r='0'",
            "ellipse" => "<ellipse cx='16' cy='16' rx='16' ry='0'",
            "ellipse-fallback" => "<ellipse cx='16' cy='16' rx='0'",
            "empty-path" => "<path d=''",
            "white-path" => "<path d=' &#9;&#10;&#13; '",
            "polygon" => "<polygon points=''",
            "polyline" => "<polyline points=' &#9; '",
            _ => "<path d='M8 8'"
        };
        string color = paint == "solid" ? "blue" : "url(#p)";
        string geometry = start + " fill='" + (role == "stroke" ? "none" : color) + "'" + (role == "fill" ? "" : " stroke='" + color + "' stroke-width='2'") + "/>";
        string transform = translated ? " transform='translate(1 1)'" : "";
        string body = consumer == "symbol" ? "<defs><symbol id='s' viewBox='0 0 32 32'>" + geometry + "</symbol></defs><use href='#s' width='32' height='32'" + transform + "/>"
            : consumer == "use" ? "<defs>" + geometry.Replace("/>", " id='s'/>", StringComparison.Ordinal) + "</defs><use href='#s'" + transform + "/>"
            : "<g" + transform + ">" + geometry + "</g>";
        SvgRasterTestHelper.AssertPixelsEqual(Document(""), Document(Resource(paint) + body), SvgRenderOptions.Default);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("solid", false)]
    [InlineData("solid", true)]
    [InlineData("linear", false)]
    [InlineData("linear", true)]
    [InlineData("radial", false)]
    [InlineData("radial", true)]
    [InlineData("pattern", false)]
    [InlineData("pattern", true)]
    public void Curve_mapping_releases_scratch_and_preserves_sources_on_denial_cancellation_and_retry(string paint, bool symbol)
    {
        string color = paint == "solid" ? "blue" : "url(#p)";
        string shape = "<path d='M4 16Q16 -8 28 16C24 28 8 28 4 16Z' fill='" + color + "'/>";
        string source = symbol ? "<symbol id='s' viewBox='0 0 32 32'>" + shape + "</symbol>" : shape.Replace("/>", " id='s'/>", StringComparison.Ordinal);
        byte[] input = Document(Resource(paint) + "<defs>" + source + "</defs><use href='#s' width='32' height='32' transform='translate(1 0)'/>");
        byte[] unchanged = input.ToArray();
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        for (long limit = 1; limit < measured.Snapshot().PeakBytes; limit += 512) DenyMemory(limit);
        DenyMemory(measured.Snapshot().PeakBytes - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        {
            SvgAuthoredPath[] paths = retry.Geometries.Select(g => g.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            Assert.NotEmpty(paths);
            SvgAuthoredPathCommand[][] commands = paths.Select(path => path.Commands.ToArray()).ToArray();
            long retained = measured.Snapshot().CurrentBytes;
            byte[] pixels;
            using (SvgConverter.RasterizedSurface first = SvgConverter.Rasterize(retry, SvgRenderOptions.Default, measured, CancellationToken.None))
            {
                Assert.Contains(first.Pixels, value => value != 0);
                pixels = first.Pixels.ToArray();
            }
            using (measured.Reserve(1_000_000 - retained - 1024, SvgMemoryCategory.Parser))
            {
                long held = measured.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(retry, SvgRenderOptions.Default, measured, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(held, measured.Snapshot().CurrentBytes);
            }
            using (SvgConverter.RasterizedSurface second = SvgConverter.Rasterize(retry, SvgRenderOptions.Default, measured, CancellationToken.None)) Assert.Equal(pixels, second.Pixels);
            for (int index = 0; index < paths.Length; index++) Assert.Equal(commands[index], paths[index].Commands.ToArray());
            Assert.Equal(retained, measured.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit + 73));
            using RenderBudget.BudgetReservation borrower = denied.Reserve(73, SvgMemoryCategory.Parser);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(73, denied.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Whitespace_normalization_preserves_authored_input_bytes_and_node_limits()
    {
        byte[] input = Document("<path d='" + new string(' ', 20_000) + "'/>");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        SvgRenderLimits normal = SvgRenderLimits.Default;
        foreach (bool inputLimit in new[] { false, true })
        {
            SvgRenderLimits tight = new(inputLimit ? input.Length - 1 : normal.MaximumInputBytes,
                normal.MaximumDimension, normal.MaximumPixels, normal.MaximumEncodedPngBytes, inputLimit ? normal.MaximumNodes : 1,
                normal.MaximumPathCommands, normal.MaximumXmlDepth, normal.MaximumAttributesPerElement);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, tight, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        SvgRasterTestHelper.AssertPngEqual(Document(""), input, SvgRenderOptions.Default);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void A_degenerate_drawable_path_still_counts_authored_commands()
    {
        SvgRenderLimits normal = SvgRenderLimits.Default;
        SvgRenderLimits tight = new(normal.MaximumInputBytes, normal.MaximumDimension,
            normal.MaximumPixels, normal.MaximumEncodedPngBytes, normal.MaximumNodes, 1, normal.MaximumXmlDepth, normal.MaximumAttributesPerElement);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(Document("<path d='M0 0L0 0'/>"), tight, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Document(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
    private static string Resource(string paint) => paint switch
    {
        "linear" => "<defs><linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient></defs>",
        "radial" => "<defs><radialGradient id='p' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='16'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></radialGradient></defs>",
        "pattern" => "<defs><pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><rect width='16' height='32' fill='blue'/><rect x='16' width='16' height='32' fill='red'/></pattern></defs>",
        _ => ""
    };
}
