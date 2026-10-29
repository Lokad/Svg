using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Coverage_boundaries_stop_cross_face_kerning_and_ligatures_while_retaining_each_face_rules(bool ligatures)
    {
        byte[] primary = ligatures ? CreateLigatureFont(false) : CreateTriangleKernFont();
        byte[] secondary = primary.ToArray();
        OpenTypeTableRange cmap = OpenTypeFont.ParseMetrics(secondary, CancellationToken.None).Cmap ?? throw new InvalidDataException();
        WriteUInt16(secondary, cmap.Offset + 12 + 14, 'B');
        WriteUInt16(secondary, cmap.Offset + 12 + 20, 'B');
        WriteInt16(secondary, cmap.Offset + 12 + 24, 1 - 'B');
        (SvgFontPackCatalog catalog, StackFontProvider provider) = CreateCoveragePack(primary, secondary);
        byte[] svg = """<svg xmlns="http://www.w3.org/2000/svg"><text font-family="First,Second">ABAA</text></svg>"""u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        SvgTextStyle style = new(SvgFontFaceRequest.FromCss("First,Second", 400, false), 20, 0, 30, 0, 0, 0, SvgTextAnchor.Start, "label");
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "ABAA", snapshot, budget, CancellationToken.None))
        {
            Assert.Equal(ligatures ? 3 : 4, run.Glyphs.Length);
            Assert.Equal(new[] { 0d, 12, 24 }, run.Glyphs[..3].ToArray().Select(glyph => glyph.X));
            Assert.Equal("First", run.GetGlyphFont(0).Font.Resolution.Resolution.Face.RequestedFamily);
            Assert.Equal("Second", run.GetGlyphFont(1).Font.Resolution.Resolution.Face.RequestedFamily);
            Assert.Equal("First", run.GetGlyphFont(2).Font.Resolution.Resolution.Face.RequestedFamily);
            if (ligatures) Assert.Equal((ushort)2, run.Glyphs[2].GlyphIndex);
            else Assert.Equal(30, run.Glyphs[3].X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Supplementary_coverage_preserves_scalar_indices_and_the_selected_units_per_em()
    {
        (SvgFontPackCatalog catalog, StackFontProvider provider) = CreateCoveragePack(CreateTriangleFont(), CreateFormat12Font());
        byte[] svg = """<svg xmlns="http://www.w3.org/2000/svg"><text font-family="First,Second">A😀A</text></svg>"""u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        SvgTextStyle style = new(SvgFontFaceRequest.FromCss("First,Second", 400, false), 20, 0, 30, 0, 0, 0, SvgTextAnchor.Start, "supplementary");
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "A😀A", snapshot, budget, CancellationToken.None))
        {
            Assert.Equal(3, run.Glyphs.Length);
            Assert.Equal((ushort)2, run.Glyphs[1].GlyphIndex);
            Assert.Equal("Second", run.GetGlyphFont(1).Font.Resolution.Resolution.Face.RequestedFamily);
            Assert.Equal(20d * 600 / 1024, run.Glyphs[1].Advance);
            Assert.Equal(12 + 20d * 600 / 1024, run.Glyphs[2].X);
        }
        SvgException missing = Assert.Throws<SvgException>(() => SvgTextRunBuilder.Create(style, "A😀ñ", snapshot, budget, CancellationToken.None));
        Assert.Equal(241, missing.FontDiagnostic?.UnicodeScalar); Assert.Equal(2, missing.FontDiagnostic?.ScalarIndex);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Coverage_preparation_deduplicates_shared_programs_across_distinct_requests()
    {
        byte[] program = CreateTriangleFont();
        (SvgFontPackCatalog catalog, StackFontProvider provider) = CreateCoveragePack(program, program);
        byte[] svg = """<svg xmlns="http://www.w3.org/2000/svg"><text font-family="First">A</text><text font-family="Second">A</text></svg>"""u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        Assert.Equal(new[] { "first.ttf" }, provider.Requests);
        Assert.Equal(2, snapshot.Fonts.Count);
        Assert.Equal(new[] { "First", "Second" }, snapshot.Fonts.Select(font => font.Resolution.Resolution.Face.RequestedFamily));
    }

    private static (SvgFontPackCatalog Catalog, StackFontProvider Provider) CreateCoveragePack(byte[] first, byte[] second)
    {
        var programs = new Dictionary<string, byte[]> { ["first.ttf"] = first, ["second.ttf"] = second };
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            packId = "coverage",
            files = programs.Select(pair => new { relativePath = pair.Key, byteSize = pair.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(pair.Value)) }),
            families = new[]
            {
                new { requestedFamily = "First", resolvedFamily = "First", relativeFontFile = "first.ttf" },
                new { requestedFamily = "Second", resolvedFamily = "Second", relativeFontFile = "second.ttf" }
            }
        });
        return (SvgFontPackCatalog.Parse(manifest, "coverage"), new(programs));
    }
}
