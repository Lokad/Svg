using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgGlyphViewportClipTests
{
    private static string FontDirectory => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels");
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(FontDirectory, "fonts", "NotoSans-Regular.ttf"));

    public static IEnumerable<object[]> ViewportCases()
    {
        foreach (string text in new[] { "II", "AVA", "Ag" })
        foreach (int size in new[] { 8, 10, 12, 16 })
        foreach (string aspect in new[] { "none", "xMidYMid meet", "xMaxYMin slice", "xMinYMax slice" })
        foreach (string x in new[] { "4", "4.5" })
        foreach (string y in new[] { "6", "6.25", "6.5", "6.75" })
        foreach (bool stroke in new[] { false, true }) yield return [text, size, aspect, x, y, stroke];
    }

    [Theory]
    [MemberData(nameof(ViewportCases))]
    [Trait("Layer", "Rendering")]
    public async Task Symbol_and_nested_viewport_keep_independently_measured_native_equivalence(string text, int size, string aspect, string x, string y, bool stroke)
    {
        // All 768 actual browser pairs establish this equivalence independently.
        // Shared glyph fitting/stroke fidelity remains a separate assertion scope.
        byte[] input = Document(text, size, aspect, x, y, stroke, "red", true);
        byte[] inline = Document(text, size, aspect, x, y, stroke, "red", false);
        SvgFontPackCatalog catalog = Catalog();
        FontProvider provider = new();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(inline, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("II", 10, "xMaxYMin slice", "4", "6.5", false)]
    [InlineData("II", 10, "xMaxYMin slice", "4.5", "6.5", false)]
    [InlineData("Ag", 10, "xMaxYMin slice", "4", "6.75", true)]
    [InlineData("AVA", 12, "xMinYMax slice", "4.5", "6.75", true)]
    [InlineData("II", 8, "none", "4", "6", false)]
    [InlineData("Ag", 16, "xMidYMid meet", "4.5", "6.25", false)]
    [Trait("Layer", "Contract")]
    public async Task Prepared_symbol_replay_preserves_selected_font_and_viewport_interactions(string text, int size, string aspect, string x, string y, bool stroke)
    {
        byte[] input = Document(text, size, aspect, x, y, stroke, "red", true);
        SvgFontPackCatalog catalog = Catalog();
        FontProvider provider = new();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("4")]
    [InlineData("4.5")]
    public async Task Fractional_symbol_clip_keeps_windows_native_glyph_mask_rounding(string x)
    {
        // These pixel values come from Windows Chrome with the committed font.
        // Linux uses its own fitting profile; the equivalence matrix runs there too.
        if (!OperatingSystem.IsWindows()) return;
        byte[] input = Document("II", 10, "xMaxYMin slice", x, "6.5", false, "black", true);
        SvgFontPackCatalog catalog = Catalog();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, new FontProvider(), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RgbaImage image = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray(), 1024);
        byte[] expected = new byte[32];
        if (x == "4") { expected[7] = 13; expected[8] = 104; expected[9] = 26; expected[12] = 81; expected[13] = 60; }
        else { expected[8] = 60; expected[9] = 81; expected[12] = 26; expected[13] = 104; expected[14] = 13; }
        for (int column = 0; column < 32; column++) Assert.Equal(expected[column], image.Pixels[(18 * 32 + column) * 4 + 3]);
    }

    private static SvgFontPackCatalog Catalog() => SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(FontDirectory, "font-pack.json")), "public-noto-sans");

    private static byte[] Document(string text, int size, string aspect, string x, string y, bool stroke, string fill, bool symbol)
    {
        string glyphs = "<text x='2' y='11' font-family='serif' font-size='" + size.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "' fill='" + fill + "'" + (stroke ? " stroke='blue' stroke-width='.5'" : "") + ">" + text + "</text>";
        string attributes = " viewBox='0 0 16 12' preserveAspectRatio='" + aspect + "'";
        string placement = " x='" + x + "' y='" + y + "' width='20' height='12'";
        string body = symbol ? "<defs><symbol id='s'" + attributes + ">" + glyphs + "</symbol></defs><use href='#s'" + placement + "/>"
            : "<svg" + attributes + placement + ">" + glyphs + "</svg>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
    }

    /// <summary>Borrows immutable committed OFL bytes, returns caller-owned sources, checks cancellation and permits concurrent requests.</summary>
    private sealed class FontProvider : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", FontBytes));
        }
    }
}
