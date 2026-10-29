using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("linearGradient", "x1='0' x2='125'")]
    [InlineData("radialGradient", "cx='62' cy='4' r='62'")]
    [InlineData("pattern", "width='4' height='4'")]
    public async Task Forward_text_paints_preserve_inherited_opacity_and_solid_span_overrides(string kind, string coordinates)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='125' height='8'><g font-family='Example' font-size='100' fill='url(#p)' fill-opacity='.5'>";
        string definition = kind == "pattern"
            ? "<pattern id='p' patternUnits='userSpaceOnUse' " + coordinates + "><rect width='2' height='4' fill='red'/><rect x='2' width='2' height='4' fill='green'/></pattern>"
            : "<" + kind + " id='p' gradientUnits='userSpaceOnUse' " + coordinates + "><stop stop-color='red'/><stop offset='1' stop-color='green'/></" + kind + ">";
        string suffix = "</g><defs>" + definition + "</defs></svg>";
        byte[] input = TextEncoding.UTF8.GetBytes(prefix + "<text y='5'>A<tspan fill='blue'>A</tspan>A</text>" + suffix);
        // This face advances 60 units at size 100. Independent runs supply the expected positions.
        byte[] direct = TextEncoding.UTF8.GetBytes(prefix + "<text y='5'>A</text><text x='60' y='5' fill='blue'>A</text><text x='120' y='5'>A</text>" + suffix);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(direct, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
        Assert.True(DecodeAlpha(expected, 1, 3) > 0);
    }

    [Fact]
    public async Task Object_box_text_paint_uses_whole_fragment_width_and_rounded_line_metrics()
    {
        byte[] bytes = CreateTriangleFont();
        int hhea = FindTableOffset(bytes, 0x68686561);
        WriteInt16(bytes, hhea + 4, 800); WriteInt16(bytes, hhea + 6, -200);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgTextStyle style = new(new("Example", 400, false), 24.37, 12, 32, 0, 0, 0, SvgTextAnchor.Start, "");
        SvgTextGlyphPaint paint = new(new(new(255, 255, 255, 255), 1), true) { FillServer = "url(#g)" };
        SvgGlyphDeviceMetrics metrics = new(1, OperatingSystem.IsWindows() ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, false);
        RenderBudget budget = new(1_000_000);
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "AAA", [], [], [], paint, metrics, snapshot, budget, CancellationToken.None))
        {
            Assert.Single(run.PaintFragments.ToArray());
            SvgRectangle box = SvgTextLayoutBounds.Measure(run, [], SvgAffineTransform.Identity, budget, CancellationToken.None);
            Assert.Equal(12, box.X); Assert.Equal(13, box.Y);
            // FreeType quantizes size to 26.6 before advancing; DirectWrite keeps scalar metrics.
            Assert.Equal(OperatingSystem.IsWindows() ? 43.875 : 43.859375, box.Width);
            Assert.Equal(24, box.Height);
            Assert.True(run.Glyphs[^1].X + run.Glyphs[^1].Advance < box.X + box.Width);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<path id='p' transform='translate(0 1)' d='M0 5H100'/>", "<text><textPath href='#p'>A</textPath></text>")]
    [InlineData("<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='20' height='16'><rect width='20' height='16' fill='white'/><rect x='1' width='2' height='16' fill='#888'/></mask>", "<text y='6' mask='url(#m)'>A</text>")]
    public async Task Text_resources_exclude_definition_ancestor_maps_but_retain_their_own_coordinates(string resource, string label)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16' font-family='Example' font-size='100'>";
        byte[] input = TextEncoding.UTF8.GetBytes(prefix + label + "<defs><g transform='translate(7 5)'>" + resource + "</g></defs></svg>");
        byte[] direct = TextEncoding.UTF8.GetBytes(prefix + label + "<defs>" + resource + "</defs></svg>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(direct, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        Assert.True(DecodeAlpha(expected, 1, 4) > 0);
    }

    [Theory]
    [InlineData("fill='url(#missing)'", "A")]
    [InlineData("fill='none'", "<tspan fill='url(#missing)'>A</tspan>")]
    [InlineData("fill='none' stroke='url(#missing)'", "A")]
    public async Task Missing_painted_text_servers_reject_before_font_acquisition(string paint, string content)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' " + paint + ">" + content + "</text></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () =>
        {
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal(0, provider.RequestCount); Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Deferred_shader_continuations_keep_source_order_and_release_their_owners()
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='265' height='8' font-family='Example' font-size='100'><g fill='url(#g)'>";
        const string suffix = "</g><defs><path id='p' d='M0 5H200'/><linearGradient id='g' gradientUnits='userSpaceOnUse' x2='250'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs></svg>";
        byte[] input = TextEncoding.UTF8.GetBytes(prefix + "<text y='5'>A<textPath href='#p'>A</textPath>A</text>" + suffix);
        byte[] separate = TextEncoding.UTF8.GetBytes(prefix + "<text y='5'>A</text><text><textPath href='#p'>A</textPath></text><text x='200' y='5'>A</text>" + suffix);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget budget = new(4_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
            Assert.Equal(SvgConverter.ConvertToPng(separate, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(root, options, budget, CancellationToken.None).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dormant_forward_paint_waits_for_the_registry_and_live_uses_keep_their_paint(bool liveUse)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='125' height='8' font-family='Example' font-size='100' fill='url(#g)'>";
        const string label = "<text y='5'>A<textPath href='#p'>A</textPath>A</text>";
        const string path = "<path id='p' d='M0 5H200'/>";
        const string gradient = "<linearGradient id='g' gradientUnits='userSpaceOnUse' x2='125'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        string instance = liveUse ? "<use href='#p' stroke='url(#g)' stroke-width='2'/>" : "";
        byte[] input = TextEncoding.UTF8.GetBytes(prefix + label + "<defs>" + path + gradient + "</defs>" + instance + "</svg>");
        byte[] ordered = TextEncoding.UTF8.GetBytes(prefix + "<defs>" + gradient + path + "</defs>" + label + instance + "</svg>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget budget = new(4_000_000);
        byte[] expected = SvgConverter.ConvertToPng(ordered, options, CancellationToken.None).ToArray();
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
            Assert.Equal(expected, SvgConverter.ConvertToPng(root, options, budget, CancellationToken.None).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using SvgPreparedDocument prepared = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.True(DecodeAlpha(expected, liveUse ? 50 : 1, liveUse ? 5 : 3) > 0);
    }

    [Fact]
    public void Unused_dormant_geometry_does_not_demand_an_inherited_missing_paint()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' fill='url(#missing)'><defs><path id='unused' d='M0 0H8V8Z'/></defs><rect width='8' height='8' fill='blue'/></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='blue'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Live_dormant_geometry_keeps_missing_paint_failure_and_releases_partial_source_owners()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' fill='url(#missing)'><use href='#p'/><defs><path id='p' d='M0 0H8V8Z'/></defs></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Focal_circle_text_paint_shares_the_area_evaluator_at_opaque_glyph_interiors(int focalX)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        string definition = "<defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='0' cy='16' r='30' fx='" + focalX + "' fy='16' fr='2'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs>";
        byte[] input = TextEncoding.UTF8.GetBytes(prefix + definition + "<text font-family='Example' font-size='1000' y='30' fill='url(#g)'>A</text></svg>");
        byte[] area = TextEncoding.UTF8.GetBytes(prefix + definition + "<rect width='32' height='32' fill='url(#g)'/></svg>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        ReadOnlyMemory<byte> actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None);
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng(area, options, CancellationToken.None);
        // These points are inside the enlarged triangle, away from glyph-mask antialiasing.
        foreach (int x in new[] { 12, 15 }) Assert.Equal(DecodePixel(expected, x, 18), DecodePixel(actual, x, 18));
        Assert.Equal(255, DecodeAlpha(actual, 12, 18));
    }
}
