using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("<circle cx='1' cy='1' r='1' fill='red'/><text font-family='Example' font-size='100' y='3'>A</text>")]
    [InlineData("<ellipse cx='1' cy='1' rx='1' ry='1' fill='red'/><text font-family='Example' font-size='100' y='3'>A</text>")]
    public async Task Supported_ovals_preserve_following_font_discovery(string content)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + content + "</svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.False(document.ConvertToPng(CancellationToken.None).IsEmpty);
    }

    [Theory]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill='red' stroke='red'><tspan stroke='none' fill='blue'>A</tspan></text>", "fill='blue'")]
    [InlineData("<g stroke='red' fill='red'><text font-family='Example' font-size='100' y='3'><tspan>A</tspan></text></g><style>text>tspan{fill:blue;stroke:none}</style>", "fill='blue'")]
    [InlineData("<text font-family='Example' font-size='100' y='3' color='red' fill='currentColor'><tspan color='blue'>A</tspan></text>", "fill='blue'")]
    [InlineData("<g color='blue'><text font-family='Example' font-size='100' y='3' color='currentColor' fill='currentColor'>A</text></g>", "fill='blue'")]
    [InlineData("<text font-family='Example' font-size='100' y='3' color='blue' fill='currentColor'><tspan color='currentColor'>A</tspan></text>", "fill='blue'")]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill='red'><tspan fill='green' style='fill:blue!important'>A</tspan></text>", "fill='blue'")]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill='red'><tspan fill='green' style='fill:inherit!important'>A</tspan></text>", "fill='red'")]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill='none'><tspan fill='blue'>A</tspan></text>", "fill='blue'")]
    [InlineData("<text font-family='Example' font-size='100' y='3' visibility='hidden'><tspan visibility='visible' fill='blue'>A</tspan></text>", "fill='blue'")]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill='red' opacity='0.5'><tspan fill='blue'>A</tspan></text>", "fill='blue' opacity='0.5'")]
    [InlineData("<text font-family='Example' font-size='100' y='3' fill-opacity='0.5'><tspan fill='blue'>A</tspan></text>", "fill='blue' fill-opacity='0.5'")]
    public async Task Span_paint_overrides_match_flat_text_in_raw_and_prepared_outputs(string content, string expectedPaint)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + content + "</svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expectedInput = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' " + expectedPaint + ">A</text></svg>");
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("<text font-family='Example' display='none'><tspan display='inline' fill='blue'>A</tspan></text>")]
    [InlineData("<text font-family='Example'><tspan fill='none'>A</tspan></text>")]
    [InlineData("<text font-family='Example'><tspan visibility='hidden'>A</tspan></text>")]
    public async Task Entirely_unpainted_spans_need_no_font_program(string content)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'>" + content + "</svg>");
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'/>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected.ToArray(), document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Varying_paints_preserve_one_continuous_glyph_layout_and_release_owned_storage()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget budget = new(1_000_000);
        SvgTextStyle style = new(new SvgFontFaceRequest("Example", 400, false), 100, 0, 3, 0, 0, 0, SvgTextAnchor.Middle, "");
        SvgTextGlyphPaint red = new(new(new SvgColor(255, 0, 0, 255), 1), true);
        SvgTextGlyphPaint blue = new(new(new SvgColor(0, 0, 255, 255), 1), true);
        SvgTextPaintSpan[] paints = [new(0, 1, red), new(1, 2, blue), new(2, 3, red)];
        using (SvgTextRun expected = SvgTextRunBuilder.Create(style, "AAA", snapshot, budget, CancellationToken.None))
        using (SvgTextRun actual = SvgTextRunBuilder.Create(style, "AAA", [], [], paints, red, null, snapshot, budget, CancellationToken.None))
        {
            Assert.Equal(expected.Glyphs.ToArray(), actual.Glyphs.ToArray());
            Assert.Equal(new[] { red, blue, red }, Enumerable.Range(0, 3).Select(index => actual.GetGlyphPaint(index, SvgColor.Transparent)).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("A<tspan fill='blue'>A</tspan>A", "<text x='60' fill='blue'>A</text><text x='120' fill='red'>A</text>")]
    [InlineData("A<tspan fill='blue'>A<tspan fill='red'>A</tspan></tspan>", "<text x='60' fill='blue'>A</text><text x='120' fill='red'>A</text>")]
    [InlineData("A<tspan visibility='hidden'>A</tspan>A", "<text x='120' fill='red'>A</text>")]
    [InlineData("A<tspan fill='none'>A</tspan>A", "<text x='120' fill='red'>A</text>")]
    [InlineData("A<tspan display='none'>A</tspan>A", "<text x='60' fill='red'>A</text>")]
    public async Task Mixed_paint_and_visibility_preserve_independent_expected_character_positions(string content, string expectedTail)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='125' height='5'><g font-family='Example' font-size='100' fill='red'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + "<text y='3'>" + content + "</text></g></svg>");
        // A has a 600-unit advance in this 1,000-unit synthetic face: each glyph advances 60 SVG units.
        byte[] expectedInput = System.Text.Encoding.UTF8.GetBytes(prefix + "<text y='3'>A</text>" + expectedTail.Replace("<text ", "<text y='3' ", StringComparison.Ordinal) + "</g></svg>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Text_path_replay_preserves_varying_span_paints_after_extraction_disposal()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='125' height='5'><defs><path id='p' d='M0 3H200'/></defs><text font-family='Example' font-size='100' fill='red'><textPath href='#p'>A<tspan fill='blue'>A</tspan>A</textPath></text></svg>"u8.ToArray();
        byte[] expectedInput = "<svg xmlns='http://www.w3.org/2000/svg' width='125' height='5'><g font-family='Example' font-size='100' fill='red'><text y='3'>A</text><text x='60' y='3' fill='blue'>A</text><text x='120' y='3'>A</text></g></svg>"u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(input);
        Assert.Equal(expected, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("A<tspan stroke='none'>A</tspan>")]
    [InlineData("<tspan stroke='none'>A</tspan>A")]
    [InlineData("<tspan stroke='none'><tspan stroke='red'>A</tspan></tspan>")]
    public async Task Actually_painted_text_strokes_acquire_the_supplied_face(string content)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' stroke='red'>" + content + "</text></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Single(snapshot.Fonts);
        Assert.True(SvgConverter.ConvertToPng(input, new(new SvgViewport(96, 96), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).Length > 0);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    public async Task Varying_glyph_paint_denial_releases_material_arrays()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgTextStyle style = new(new SvgFontFaceRequest("Example", 400, false), 100, 0, 3, 0, 0, 0, SvgTextAnchor.Start, "");
        SvgTextGlyphPaint red = new(new(new SvgColor(255, 0, 0, 255), 1), true);
        SvgTextGlyphPaint blue = new(new(new SvgColor(0, 0, 255, 255), 1), true);
        SvgTextPaintSpan[] paints = Enumerable.Range(0, 128).Select(index => new SvgTextPaintSpan(index, index + 1, index % 2 == 0 ? red : blue)).ToArray();
        string text = new('A', 128);
        RenderBudget normal = new(100_000);
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, text, [], [], paints, red, null, snapshot, normal, CancellationToken.None))
            Assert.Equal(128, run.Glyphs.Length);
        Assert.Equal(0, normal.Snapshot().CurrentBytes);

        RenderBudget denied = new(4_500);
        SvgException failure = Assert.Throws<SvgException>(() => SvgTextRunBuilder.Create(style, text, [], [], paints, red, null, snapshot, denied, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Hidden_font_spans_acquire_advance_metrics_only_for_a_painted_containing_run()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='125' height='5'><text font-family='Example' font-size='100' y='3'><tspan font-weight='700' visibility='hidden'>A</tspan>A</text></svg>"u8.ToArray();
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, CancellationToken.None);
        Assert.Equal(new[] { 400, 700 }, requests.Select(request => request.Weight));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount); Assert.Equal(1, source.RequestCount);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg' width='125' height='5'><text font-family='Example' font-size='100' x='60' y='3'>A</text></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(expected, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        byte[] unpainted = "<svg xmlns='http://www.w3.org/2000/svg' width='125' height='5'><text font-family='Example' font-size='100' y='3'><tspan font-weight='700' visibility='hidden'>A</tspan></text></svg>"u8.ToArray();
        Assert.Empty(SvgFontRequestDiscovery.Discover(unpainted, CancellationToken.None));
    }

    [Fact]
    public async Task Font_size_spans_share_continuous_positions_and_release_owned_font_arrays_on_denial()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgTextStyle style = new(new SvgFontFaceRequest("Example", 400, false), 100, 0, 3, 0, 0, 0, SvgTextAnchor.Start, "")
        { Baseline = SvgTextBaseline.Middle };
        SvgInheritedTextStyle large = new("Example", 400, false, 100, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgInheritedTextStyle small = new("Example", 400, false, 50, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgTextFontSpan[] fonts = [new(0, 1, large), new(1, 2, small), new(2, 3, large)];
        RenderBudget budget = new(1_000_000);         using (SvgTextRun run = SvgTextRunBuilder.Create(style, "AAA", [], [], [], fonts, null, null, snapshot, budget, CancellationToken.None))
        {
            Assert.Equal(new[] { 0d, 60, 90 }, run.Glyphs.ToArray().Select(glyph => glyph.X));
            Assert.Equal(new[] { 28d, 15.5, 28 }, run.Glyphs.ToArray().Select(glyph => glyph.Y));
            Assert.Equal(new[] { 100d, 50, 100 }, Enumerable.Range(0, 3).Select(index => run.GetGlyphFont(index).FontSize));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(1_000_000);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget memory = new(budget.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgTextRunBuilder.Create(style, "AAA", [], [], [], fonts, null, null, snapshot, memory, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, memory.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Each_text_path_glyph_keeps_its_span_font_size_across_multiple_runs()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='250'><defs><path id='p' d='M0 105H200'/><path id='q' d='M0 230H200'/></defs><text font-family='Example' font-size='100'><textPath href='#p'>A<tspan font-size='50'>A</tspan>A</textPath></text><text font-family='Example' font-size='100'><textPath href='#q'><tspan font-size='50'>AA</tspan>A</textPath></text></svg>"u8.ToArray();
        byte[] expected = "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='250'><text font-family='Example' font-size='100' y='105'>A<tspan font-size='50'>A</tspan>A</text><text font-family='Example' font-size='100' y='230'><tspan font-size='50'>AA</tspan>A</text></svg>"u8.ToArray();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(expected, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Paint_interval_cancellation_releases_partial_layout_and_scopes()
    {
        RenderBudget budget = new(100_000);
        using CancellationTokenSource cancellation = new();
        using (SvgTextPaintOutput output = new(budget, 0, SvgTextPaintStyle.Default, cancellation.Token))
        {
            output.Append("A", 0, 0, 128, cancellation.Token);
            SvgTextPaintStyle blue = SvgTextPaintStyle.Default with { Paint = SvgPaint.Default with { FillPaint = new(new SvgColor(0, 0, 255, 255), 1) } };
            output.EnterStyleSpan(blue, new("Other", 400, false, 18, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start));
            output.Append("A", 0, 1, 128, cancellation.Token);
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => output.Append("A", 0, 1, 128, cancellation.Token));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
