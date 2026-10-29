using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> IntrinsicImageSizes()
    {
        foreach (bool varied in new[] { false, true })
        foreach (string size in new[] { "height", "width", "missing", "percentage", "zero-height", "zero-width" })
        foreach (string aspect in new[] { "xMinYMin meet", "xMidYMid meet", "xMaxYMax meet", "xMidYMid slice", "none" })
            yield return [varied, size, aspect];
    }

    [Theory]
    [MemberData(nameof(IntrinsicImageSizes))]
    public async Task Omitted_image_dimensions_use_intrinsic_size_and_aspect_in_every_live_context(bool varied, string size, string aspect)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] pixels = varied ? [255, 0, 0, 255, 0, 0, 255, 255] : [255, 0, 0, 255, 255, 0, 0, 255];
        string png = Convert.ToBase64String(PngEncoder.Encode(2, 1, pixels, CancellationToken.None));
        foreach (string scope in new[] { "direct", "use", "symbol", "pattern", "nested" })
        foreach (bool forward in new[] { false, true })
        {
            byte[] input = Input(false), control = Input(true);
            byte[] expected = Render(control);
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

            byte[] Input(bool explicitSize)
            {
                string dimensions = explicitSize ? size switch
                {
                    "missing" => "width='2' height='1'",
                    "zero-height" or "zero-width" => "width='0' height='0'",
                    _ => "width='4' height='2'"
                } : size switch
                {
                    "height" => "height='2'", "width" => "width='4'", "percentage" => "width='50%'",
                    "zero-height" => "height='0'", "zero-width" => "width='0'", _ => ""
                };
                string image = "<image id='i' href='data:image/png;base64," + png + "' " + dimensions + " preserveAspectRatio='" + aspect + "'/>";
                string content = scope switch
                {
                    "use" => forward ? "<use href='#i' x='1'/><defs>" + image + "</defs>" : "<defs>" + image + "</defs><use href='#i' x='1'/>",
                    "symbol" => "<defs><symbol id='s' viewBox='0 0 8 8'>" + image + "</symbol></defs><use href='#s' width='8' height='8'/>",
                    "pattern" => "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'>" + image + "</pattern></defs><rect width='8' height='8' fill='url(#p)'/>",
                    "nested" => "<svg width='4' height='4' viewBox='0 0 8 8'>" + image + "</svg>",
                    _ => image
                };
                return Document(content);
            }
        }
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Intrinsic_image_sizes_supply_object_bounds_for_composed_effects(bool varied)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string png = Convert.ToBase64String(PngEncoder.Encode(2, 1, varied ? [255, 0, 0, 255, 0, 0, 255, 255] : [255, 0, 0, 255, 255, 0, 0, 255], CancellationToken.None));
        const string definitions = "<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect width='.5' height='1'/></clipPath>"
            + "<mask id='m' maskContentUnits='objectBoundingBox' maskUnits='objectBoundingBox' x='0' y='0' width='.5' height='1'><rect width='1' height='1' fill='white'/></mask>"
            + "<filter id='f' filterUnits='objectBoundingBox' x='0' y='0' width='1' height='1'><feFlood flood-color='blue'/></filter></defs>";
        foreach (string effect in new[] { "clip-path='url(#c)'", "mask='url(#m)'", "filter='url(#f)'", "filter='url(#f)' mask='url(#m)' opacity='.5'" })
        foreach (string transform in new[] { "", " transform='matrix(1 .1 .2 1 0 0)'" })
        {
            string image = "<image x='1' y='1' height='2' href='data:image/png;base64," + png + "' " + effect + transform + "/>";
            byte[] input = Document(definitions + image);
            byte[] control = Document(definitions + image.Replace("height='2'", "height='2' width='4'", StringComparison.Ordinal));
            if (transform.Length != 0 && effect.Contains("filter", StringComparison.Ordinal))
            {
                Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(control)).ErrorCode);
                Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
                continue;
            }
            byte[] expected = Render(control);
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    [Fact]
    public void Intrinsic_image_dimensions_preserve_decoded_storage_on_denial_cancellation_and_retry()
    {
        string png = Convert.ToBase64String(PngEncoder.Encode(2, 1, [255, 0, 0, 255, 0, 0, 255, 255], CancellationToken.None));
        byte[] input = Document("<defs><image id='i' href='data:image/png;base64," + png + "' height='2'/></defs><use href='#i' transform='matrix(1 .1 .2 1 0 0)'/>");
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token));
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.NotEqual(Render(Document("")), Render(input));
    }
}
