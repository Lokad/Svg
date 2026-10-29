using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("<rect width='0' height='8'")]
    [InlineData("<circle r='0'")]
    [InlineData("<ellipse rx='0' ry='4'")]
    [InlineData("<image width='8' height='8'")]
    public async Task Enclosing_containers_can_filter_suppressed_child_sources(string source)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        const string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><feFlood flood-color='red'/></filter>";
        byte[] expected = Render(Document("<rect width='8' height='8' fill='red'/>"));
        foreach (bool instance in new[] { false, true })
        {
            string body = instance ? "<defs>" + source + " id='s' filter='url(#f)'/></defs><use href='#s' filter='url(#f)'/>"
                : "<g filter='url(#f)'>" + source + " filter='url(#f)'/></g>";
            byte[] input = Document("<defs>" + filter + "</defs>" + body);
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    public static IEnumerable<object[]> SuppressedFilterSources()
    {
        foreach (string source in new[]
        {
            "<rect width='0' height='8'", "<rect width='8' height='0'",
            "<circle cx='4' cy='4'", "<circle cx='4' cy='4' r='0'",
            "<ellipse cx='4' cy='4' rx='0' ry='4'", "<ellipse cx='4' cy='4' rx='4' ry='0'",
            "<image width='8' height='8'", "<image width='8' height='8' href=''",
            "<image width='0' height='8' href='PNG'", "<image width='8' height='0' href='PNG'",
            "<image width='0' height='8' href='VARIED'", "<image width='8' height='0' href='VARIED'"
        })
            foreach (string context in new[] { "direct", "group", "use", "symbol", "pattern" }) yield return [source, context];
    }

    [Theory]
    [MemberData(nameof(SuppressedFilterSources))]
    public async Task Nonrenderable_leaves_do_not_activate_their_own_filters(string source, string context)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string png = "data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(1, 1, [255, 0, 0, 255], CancellationToken.None));
        source = source.Replace("PNG", png, StringComparison.Ordinal);
        source = source.Replace("VARIED", "data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(2, 1, [255, 0, 0, 255, 0, 0, 255, 255], CancellationToken.None)), StringComparison.Ordinal);
        foreach (SvgColor canvas in new[] { SvgColor.Transparent, new SvgColor(255, 255, 255, 255) })
        {
            SvgRenderOptions options = new(null, 1, canvas, SvgRenderLimits.Default);
            byte[] expected = SvgConverter.ConvertToPng(Input(""), options, CancellationToken.None).ToArray();
            foreach (string presentation in new[] { "", " fill='none'", " stroke='blue' stroke-width='2'", " visibility='hidden'" })
            {
                if (source.StartsWith("<image", StringComparison.Ordinal) && presentation is " fill='none'" or " stroke='blue' stroke-width='2'") continue;
                byte[] input = Input(" filter='url(#f)'" + presentation);
                Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None);
                Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
            }
        }

        byte[] Input(string effect)
        {
            string node = source + effect;
            string body = context switch
            {
                "group" => "<g opacity='.5'>" + node + "/></g>",
                "use" => "<defs>" + node + " id='s'/></defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='s'>" + node + "/></symbol></defs><use href='#s'/>",
                "pattern" => "<defs><pattern id='s' patternUnits='userSpaceOnUse' width='8' height='8'>" + node + "/></pattern></defs><rect width='8' height='8' fill='url(#s)'/>",
                _ => node + "/>"
            };
            return Document("<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><feFlood flood-color='red'/></filter></defs>" + body + "<rect width='1' height='1' fill='lime'/>");
        }
    }

    [Fact]
    public void Zero_size_varied_images_release_decoded_storage_and_still_validate_the_source()
    {
        string png = Convert.ToBase64String(PngEncoder.Encode(2, 1, [255, 0, 0, 255, 0, 0, 255, 255], CancellationToken.None));
        byte[] input = Document("<image width='0' height='8' href='data:image/png;base64," + png + "'/>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancelled.Token));
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface pixels = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.All(pixels.Pixels, value => Assert.Equal((byte)0, value));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render(Document("<image width='0' height='8' href='data:image/png;base64,broken'/>"))).ErrorCode);
    }
}
