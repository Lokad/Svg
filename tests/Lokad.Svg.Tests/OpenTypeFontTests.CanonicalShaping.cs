using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData((int)SvgGlyphMetricTarget.Windows)]
    [InlineData((int)SvgGlyphMetricTarget.Linux)]
    public async Task Canonical_fitting_preserves_the_qualified_paint_origin_and_source_scale(int target)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        var layout = SvgGlyphLayout.CreateStorageAndMetrics(Assert.Single(fonts.Fonts), "i\u0307\u0301 j\u0307\u0323\u0301", 24, 12, 112, 0, 0, true,
            [], [new(0, 12, 112, 0, 0)], [], [], [], [], SvgTextAnchor.Start, new(1, (SvgGlyphMetricTarget)target, false),
            new SvgTextFitting(320, SvgTextLengthAdjust.SpacingAndGlyphs), budget, "fitting", CancellationToken.None);
        using (layout.Storage)
        {
            // Retain the established paint-origin multiply/add order. Native DOM
            // character queries use a distinct fused coordinate evaluation; applying
            // that here changes the qualified scaled fi mask's quarter-pixel bucket.
            Assert.Equal(12, layout.Storage.Glyphs[0].X);
            Assert.Equal(320f / 18.625f, layout.Fitting.Scale);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0xAC, false)]
    [InlineData(0xAD, true)]
    [InlineData(0xAE, false)]
    [InlineData(0x34E, false)]
    [InlineData(0x34F, true)]
    [InlineData(0x350, false)]
    [InlineData(0x200B, true)]
    [InlineData(0xE0100, true)]
    [InlineData(0xE0FFF, true)]
    [InlineData(0xE1000, false)]
    public void Canonical_default_ignorables_use_pinned_category_boundaries(int scalar, bool expected)
        => Assert.Equal(expected, SvgCanonicalText.IsDefaultIgnorable(new(scalar)));

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("\u0131\u0303)", 50)]
    [InlineData("\u0131\u0303(", 0)]
    [InlineData("\u0131\u0303\u0301)", 50)]
    [InlineData("\u0131\u0303\u034F)", 50)]
    [InlineData("\u0131\u0303\u0301\u034F)", 50)]
    [InlineData("\u0131\u0303", 0)]
    public async Task Canonical_context_advances_match_complete_classes_and_marks_zero_after_gpos(string text, int expected)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (BudgetedParserList<SvgShapingGlyph> glyphs = new(budget, SvgMemoryCategory.Geometry, CancellationToken.None))
        {
            SvgCanonicalText.AppendGlyphs(font, text, 0, text.Length, glyphs, CancellationToken.None);
            OpenTypeFont.ApplyContextualAdvances(bytes, font.Metrics, glyphs.AsMutableSpan(), CancellationToken.None);
            Assert.Equal(expected, glyphs.AsSpan().ToArray().Single(glyph => glyph.Scalar.Value == 0x303).ContextAdvance);
        }
        using (OwnedParserArray<SvgShapingGlyph> final = SvgTextShaping.Create(font, 24, text, 0, [], [], [], [], [], [], null, budget, CancellationToken.None))
            Assert.All(final.Items, glyph => Assert.Equal(0, glyph.ContextAdvance));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("i\u0307\u0301", new ushort[] { 1766, 2661, 2663 }, new int[] { -1, 0, 1 }, new int[] { 0, 129, 272 }, new int[] { 0, 0, 201 })]
    [InlineData("\u0131\u0301\u0307", new ushort[] { 1766, 2663, 2661 }, new int[] { -1, 0, 1 }, new int[] { 0, 401, -272 }, new int[] { 0, 0, 229 })]
    [InlineData("j\u0307\u0323\u0301", new ushort[] { 1767, 2694, 2661, 2663 }, new int[] { -1, 0, 0, 2 }, new int[] { 0, 320, 129, 272 }, new int[] { 0, -240, 0, 201 })]
    [InlineData("ff\u0301", new ushort[] { 1651, 2663 }, new int[] { -1, 0 }, new int[] { 0, 899 }, new int[] { 0, 229 })]
    [InlineData("ff\u0301\u0307", new ushort[] { 1651, 2663, 2661 }, new int[] { -1, 0, 1 }, new int[] { 0, 899, -272 }, new int[] { 0, 229, 229 })]
    public async Task Canonical_shaping_applies_selected_contexts_and_filtered_attachment_chains(
        string text, ushort[] indices, int[] parents, int[] x, int[] y)
    {
        // Independent HarfBuzz 8.3 font-unit vectors, bound to the committed Noto
        // program. The production code neither loads nor depends on HarfBuzz.
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (BudgetedParserList<SvgShapingGlyph> glyphs = new(budget, SvgMemoryCategory.Geometry, CancellationToken.None))
        {
            SvgCanonicalText.AppendGlyphs(font, text, 0, text.Length, glyphs, CancellationToken.None);
            OpenTypeGsubCompositionTable substitutions = Assert.IsType<OpenTypeGsubCompositionTable>(OpenTypeFont.GetCanonicalSubstitutions(bytes, font.Metrics, CancellationToken.None));
            int count = OpenTypeFont.ApplyCanonicalSubstitutions(bytes, substitutions, glyphs.AsMutableSpan(), CancellationToken.None);
            while (glyphs.Count > count) glyphs.RemoveLast();
            OpenTypeGsubLigatureTable ligatures = Assert.IsType<OpenTypeGsubLigatureTable>(OpenTypeFont.GetLatinLigatures(bytes, font.Metrics, CancellationToken.None));
            count = OpenTypeFont.ApplyLatinLigatures(bytes, ligatures, glyphs.AsMutableSpan(), CancellationToken.None);
            while (glyphs.Count > count) glyphs.RemoveLast();
            SvgCanonicalText.MergeClusters(glyphs.AsMutableSpan(), CancellationToken.None);
            OpenTypeGposMarkTable marks = Assert.IsType<OpenTypeGposMarkTable>(OpenTypeFont.GetMarkPositioning(bytes, font.Metrics, CancellationToken.None));
            OpenTypeFont.AttachMarks(bytes, marks, glyphs.AsMutableSpan(), CancellationToken.None);
            SvgShapingGlyph[] actual = glyphs.AsSpan().ToArray();
            Assert.Equal(indices, actual.Select(glyph => glyph.GlyphIndex));
            Assert.Equal(parents, actual.Select(glyph => glyph.Attachment.Parent));
            Assert.Equal(x, actual.Select(glyph => glyph.Attachment.X));
            Assert.Equal(y, actual.Select(glyph => glyph.Attachment.Y));
            Assert.All(actual, glyph => { Assert.Equal(0, glyph.ClusterStart); Assert.Equal(text.Length, glyph.ClusterEnd); });
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("i\u0301", "\u00ED", new int[] { 0 })]
    [InlineData("I\u0301", "\u00CD", new int[] { 0 })]
    [InlineData("i\u0323", "\u1ECB", new int[] { 0 })]
    [InlineData("i\u0307\u0301", "i\u0307\u0301", new int[] { 0, 1, 2 })]
    [InlineData("j\u0307\u0323\u0301", "j\u0323\u0307\u0301", new int[] { 0, 2, 1, 3 })]
    [InlineData("x\u0301\u0323", "x\u0323\u0301", new int[] { 0, 2, 1 })]
    [InlineData("\u0301\u0323", "\u0323\u0301", new int[] { 1, 0 })]
    [InlineData("x\u0301\u0307", "x\u0301\u0307", new int[] { 0, 1, 2 })]
    [InlineData("x\u0301\u034F\u0323", "x\u0301\u034F\u0323", new int[] { 0, 1, 2, 3 })]
    public async Task Canonical_shaping_keeps_source_owners_separate_from_shared_clusters(string text, string expected, int[] owners)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (BudgetedParserList<SvgShapingGlyph> glyphs = new(budget, SvgMemoryCategory.Geometry, CancellationToken.None))
        {
            SvgCanonicalText.AppendGlyphs(Assert.Single(fonts.Fonts), text, 0, text.Length, glyphs, CancellationToken.None);
            Assert.Equal(expected.EnumerateRunes().ToArray(), glyphs.AsSpan().ToArray().Select(x => x.Scalar));
            Assert.Equal(owners, glyphs.AsSpan().ToArray().Select(x => x.SourceOffset));
            Assert.All(glyphs.AsSpan().ToArray(), glyph => { Assert.Equal(0, glyph.ClusterStart); Assert.Equal(text.Length, glyph.ClusterEnd); });
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0x00ED, "i\u0301")]
    [InlineData(0x01FA, "A\u030A\u0301")]
    [InlineData(0x1F82, "\u03B1\u0313\u0300\u0345")]
    [InlineData(0x212B, "A\u030A")]
    [InlineData(0x0344, "\u0308\u0301")]
    [InlineData(0xFB01, "\uFB01")]
    [InlineData(0x1D15E, "\U0001D157\U0001D165")]
    [InlineData(0xAC00, "\u1100\u1161")]
    [InlineData(0xD7A3, "\u1112\u1175\u11C2")]
    public void Canonical_decomposition_is_recursive_without_compatibility_folding(int scalar, string expected)
    {
        Span<Rune> parts = stackalloc Rune[SvgCanonicalText.MaximumDecomposedScalars];
        int count = SvgCanonicalText.Decompose(new(scalar), parts);
        Assert.Equal(expected.EnumerateRunes().ToArray(), parts[..count].ToArray());
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0x69, 0x301, 0xED)]
    [InlineData(0x41, 0x30A, 0xC5)]
    [InlineData(0xC5, 0x301, 0x1FA)]
    [InlineData(0x1100, 0x1161, 0xAC00)]
    [InlineData(0xAC00, 0x11A8, 0xAC01)]
    [InlineData(0x1112, 0x1175, 0xD788)]
    [InlineData(0xD788, 0x11C2, 0xD7A3)]
    [InlineData(0x308, 0x301, 0)] // U+0344 is a full-composition exclusion.
    [InlineData(0x1D157, 0x1D165, 0)] // Musical symbols also remain decomposed.
    [InlineData(0x1100, 0x1160, 0)]
    [InlineData(0x1100, 0x1176, 0)]
    [InlineData(0xAC00, 0x11A7, 0)]
    [InlineData(0xAC00, 0x11C3, 0)]
    public void Canonical_composition_keeps_exclusions_and_hangul_boundaries(int first, int second, int expected)
    {
        Assert.Equal(expected != 0, SvgCanonicalText.TryCompose(new(first), new(second), out Rune composite));
        Assert.Equal(expected, composite.Value);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0x300, 230, true)]
    [InlineData(0x2FF, 0, false)]
    [InlineData(0x323, 220, true)]
    [InlineData(0x315, 232, true)]
    [InlineData(0x345, 240, true)]
    [InlineData(0x34F, 0, true)]
    [InlineData(0x41, 0, false)]
    [InlineData(0x1D165, 216, true)]
    public void Canonical_class_and_unicode_mark_category_are_distinct(int scalar, byte canonicalClass, bool mark)
        => Assert.Equal((canonicalClass, mark), SvgCanonicalText.GetProperties(new(scalar)));
}
