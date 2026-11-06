using System.Buffers.Binary;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Canonical_single_substitutions_use_delta_and_indexed_formats(ushort format)
    {
        byte[] bytes = CreateLigatureFont(false);
        OpenTypeTableRange table = Assert.IsType<OpenTypeTableRange>(OpenTypeFont.ParseMetrics(bytes, CancellationToken.None).Gsub);
        WriteUInt32(bytes, table.Offset + 32, 0x63636D70); // Select ccmp instead of liga.
        WriteUInt16(bytes, table.Offset + 48, 1);
        WriteUInt16(bytes, table.Offset + 56, format);
        WriteUInt16(bytes, table.Offset + 60, 1); // Delta +1 or one indexed substitute.
        if (format == 2) WriteUInt16(bytes, table.Offset + 62, 2);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        RenderBudget budget = new(new HostMemoryBudget(16_384));
        using (BudgetedParserList<SvgShapingGlyph> glyphs = new(budget, SvgMemoryCategory.Geometry, CancellationToken.None))
        {
            SvgCanonicalText.AppendGlyphs(font, "AAA", 0, 3, glyphs, CancellationToken.None);
            OpenTypeGsubCompositionTable graph = Assert.IsType<OpenTypeGsubCompositionTable>(OpenTypeFont.GetCanonicalSubstitutions(bytes, font.Metrics, CancellationToken.None));
            Assert.Equal(3, OpenTypeFont.ApplyCanonicalSubstitutions(bytes, graph, glyphs.AsMutableSpan(), CancellationToken.None));
            Assert.All(glyphs.AsSpan().ToArray(), glyph => Assert.Equal((ushort)2, glyph.GlyphIndex));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("gsub-empty-input", SvgErrorCode.MalformedInput)]
    [InlineData("gsub-input-limit", SvgErrorCode.LimitExceeded)]
    [InlineData("gsub-sequence", SvgErrorCode.MalformedInput)]
    [InlineData("gsub-reference", SvgErrorCode.MalformedInput)]
    [InlineData("gsub-recursion", SvgErrorCode.UnsupportedFeature)]
    [InlineData("gpos-empty-input", SvgErrorCode.MalformedInput)]
    [InlineData("gpos-input-limit", SvgErrorCode.LimitExceeded)]
    [InlineData("gpos-sequence", SvgErrorCode.MalformedInput)]
    [InlineData("gpos-reference", SvgErrorCode.MalformedInput)]
    [InlineData("gpos-value-format", SvgErrorCode.UnsupportedFeature)]
    public async Task Canonical_context_admission_checks_complete_rules_and_nested_actions(string defect, SvgErrorCode expected)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        Mutate(bytes, defect);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        SvgException error;
        if (defect.StartsWith("gpos-", StringComparison.Ordinal))
        {
            using BudgetedParserList<SvgShapingGlyph> glyphs = new(budget, SvgMemoryCategory.Geometry, CancellationToken.None);
            SvgCanonicalText.AppendGlyphs(font, "i\u0307\u0301", 0, 3, glyphs, CancellationToken.None);
            error = Assert.Throws<SvgException>(() => OpenTypeFont.ApplyContextualAdvances(bytes, font.Metrics, glyphs.AsMutableSpan(), CancellationToken.None));
            Assert.True(budget.Snapshot().PeakBytes > 0);
        }
        else
        {
            error = Assert.Throws<SvgException>(() => OpenTypeFont.GetCanonicalSubstitutions(bytes, font.Metrics, CancellationToken.None));
            Assert.Equal(0, budget.Snapshot().PeakBytes);
        }
        Assert.Equal(expected, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static void Mutate(byte[] bytes, string defect)
        {
            OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
            bool substitution = defect.StartsWith("gsub", StringComparison.Ordinal);
            int feature, lookupList, type;
            if (substitution)
            {
                OpenTypeGsubCompositionTable graph = Assert.IsType<OpenTypeGsubCompositionTable>(OpenTypeFont.GetCanonicalSubstitutions(bytes, metrics, CancellationToken.None));
                feature = graph.Feature; lookupList = graph.LookupList; type = 6;
            }
            else
            {
                OpenTypeGposPairTable graph = Assert.IsType<OpenTypeGposPairTable>(metrics.Gpos);
                feature = graph.FeatureOffset; lookupList = graph.LookupListOffset; type = 8;
            }
            int lookup = 0;
            for (int index = 0; index < Read16(bytes, feature + 2); index++)
            {
                int selected = Read16(bytes, feature + 4 + index * 2);
                int candidate = lookupList + Read16(bytes, lookupList + 2 + selected * 2);
                if (Read16(bytes, candidate) == type) { lookup = candidate; break; }
            }
            Assert.NotEqual(0, lookup);
            int context = lookup + Read16(bytes, lookup + 6), set = 0;
            for (int index = 0; index < Read16(bytes, context + 10); index++)
            {
                int relative = Read16(bytes, context + 12 + index * 2);
                if (relative != 0 && Read16(bytes, context + relative) != 0) { set = context + relative; break; }
            }
            Assert.NotEqual(0, set);
            int rule = set + Read16(bytes, set + 2);
            int inputField = rule + 2 + Read16(bytes, rule) * 2;
            int lookaheadField = inputField + 2 + (Read16(bytes, inputField) - 1) * 2;
            int actionField = lookaheadField + 2 + Read16(bytes, lookaheadField) * 2;
            if (defect.EndsWith("empty-input", StringComparison.Ordinal)) WriteUInt16(bytes, inputField, 0);
            else if (defect.EndsWith("input-limit", StringComparison.Ordinal)) WriteUInt16(bytes, inputField, 9);
            else if (defect.EndsWith("sequence", StringComparison.Ordinal)) WriteUInt16(bytes, actionField + 2, (ushort)Read16(bytes, inputField));
            else if (defect.EndsWith("reference", StringComparison.Ordinal)) WriteUInt16(bytes, actionField + 4, ushort.MaxValue);
            else
            {
                int selected = Read16(bytes, actionField + 4);
                int nested = lookupList + Read16(bytes, lookupList + 2 + selected * 2);
                if (substitution) WriteUInt16(bytes, nested, 6);
                else WriteUInt16(bytes, nested + Read16(bytes, nested + 6) + 4, 8);
            }
        }

        static int Read16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
    }
}
