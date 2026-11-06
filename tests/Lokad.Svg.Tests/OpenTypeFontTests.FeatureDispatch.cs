using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.Testing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public enum TextFeatureProgram { Ccmp, Context, Normalization, Fallback }
    public enum CanonicalTestGraph { Singles, Multiple, Lookahead, Backtrack, Input }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(CanonicalTestGraph.Lookahead, "A", false, true)]
    [InlineData(CanonicalTestGraph.Lookahead, "AV", true, true)]
    [InlineData(CanonicalTestGraph.Lookahead, "VA", false, true)]
    [InlineData(CanonicalTestGraph.Lookahead, "ATV", true, true)]
    [InlineData(CanonicalTestGraph.Backtrack, "VA", true, true)]
    [InlineData(CanonicalTestGraph.Backtrack, "AV", false, true)]
    [InlineData(CanonicalTestGraph.Input, "AV", true, true)]
    [InlineData(CanonicalTestGraph.Input, "A", false, true)]
    [InlineData(CanonicalTestGraph.Input, "VA", false, true)]
    [InlineData(CanonicalTestGraph.Lookahead, "ATV", false, false)]
    [InlineData(CanonicalTestGraph.Lookahead, "AV", true, false)]
    public async Task Unsupported_coverage_context_requires_its_complete_bounded_input(CanonicalTestGraph graph, string text, bool relevant, bool filtered)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CanonicalSequenceProgram(graph, filtered))], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        if (relevant)
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
                SvgTextShaping.Create(font, 24, text, 0, [], [], [], [], [], null, budget, CancellationToken.None)).ErrorCode);
        else
        {
            using OwnedParserArray<SvgShapingGlyph> shaped = SvgTextShaping.Create(font, 24, text, 0, [], [], [], [], [], null, budget, CancellationToken.None);
            Assert.Equal(text.Length, shaped.Items.Length);
            for (int index = 0; index < text.Length; index++)
                Assert.Equal(OpenTypeFont.GetGlyphIndex(font.Bytes, text[index], font.Metrics, CancellationToken.None), shaped.Items[index].GlyphIndex);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0x41, false)]
    [InlineData(0xAD, true)]
    [InlineData(0xC1, true)]
    [InlineData(0x301, true)]
    [InlineData(0x34F, true)]
    [InlineData(0x3A9, false)]
    [InlineData(0x200C, true)]
    [InlineData(0x2126, true)]
    [InlineData(0x212B, true)]
    [InlineData(0xFB01, false)]
    [InlineData(0x10400, false)]
    [InlineData(0x1D400, false)]
    [InlineData(0x1F600, false)]
    public void Canonical_work_distinguishes_singletons_marks_ignorables_and_compatibility(int scalar, bool expected)
        => Assert.Equal(expected, SvgCanonicalText.RequiresNormalization(new Rune(scalar)));

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(TextFeatureProgram.Normalization, 0xC1, true)]
    [InlineData(TextFeatureProgram.Normalization, 0x1FA, false)]
    [InlineData(TextFeatureProgram.Normalization, 0x2126, false)]
    [InlineData(TextFeatureProgram.Normalization, 0x41, true)]
    [InlineData(TextFeatureProgram.Normalization, 0x301, true)]
    [InlineData(TextFeatureProgram.Fallback, 0xC1, true)]
    [InlineData(TextFeatureProgram.Ccmp, 0xC1, false)]
    public void Canonical_coverage_requires_a_direct_glyph_or_every_component(TextFeatureProgram program, int scalar, bool expected)
    {
        byte[] bytes = FeatureProgram(program);
        Assert.Equal(expected, SvgCanonicalText.HasCoverage(bytes, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), new Rune(scalar), CancellationToken.None));
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(TextFeatureProgram.Ccmp, 'V', 0)]
    [InlineData(TextFeatureProgram.Context, 'A', -100)]
    public async Task Unmarked_runs_execute_selected_features_and_keep_source_addresses(TextFeatureProgram program, char first, int adjustment)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(FeatureProgram(program))], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (OwnedParserArray<SvgShapingGlyph> shaped = SvgTextShaping.Create(font, 24, "AV", 0, [], [], [], [], [], null, budget, CancellationToken.None))
        {
            Assert.Equal(2, shaped.Items.Length);
            Assert.Equal(OpenTypeFont.GetGlyphIndex(font.Bytes, first, font.Metrics, CancellationToken.None), shaped.Items[0].GlyphIndex);
            Assert.Equal(OpenTypeFont.GetGlyphIndex(font.Bytes, 'V', font.Metrics, CancellationToken.None), shaped.Items[1].GlyphIndex);
            Assert.Equal(adjustment, shaped.Items[0].ContextAdvance);
            Assert.Equal(0, shaped.Items[0].SourceOffset);
            Assert.Equal(1, shaped.Items[1].SourceOffset);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public async Task A_cmap_only_program_lends_empty_shaping_storage()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "kerning-precedence", "PlainKern.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(0));
        using OwnedParserArray<SvgShapingGlyph> shaped = SvgTextShaping.Create(Assert.Single(fonts.Fonts), 24, "AV", 0, [], [], [], [], [], null, budget, CancellationToken.None);
        Assert.Empty(shaped.Items.ToArray());
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("V", false)]
    [InlineData("A", true)]
    public async Task Run_validation_preserves_irrelevant_forms_and_rejects_covered_unsupported_forms(string text, bool rejects)
    {
        byte[] bytes = FilteredCanonicalProgram();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            OpenTypeFont.GetCanonicalSubstitutions(bytes, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), CancellationToken.None)).ErrorCode);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        if (rejects)
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
                SvgTextShaping.Create(Assert.Single(fonts.Fonts), 24, text, 0, [], [], [], [], [], null, budget, CancellationToken.None)).ErrorCode);
        else
        {
            using OwnedParserArray<SvgShapingGlyph> shaped = SvgTextShaping.Create(Assert.Single(fonts.Fonts), 24, text, 0, [], [], [], [], [], null, budget, CancellationToken.None);
            Assert.Single(shaped.Items.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canonical_demand_keeps_the_primary_program_and_reuses_the_shared_allowance(bool coverageCatalog)
    {
        byte[] primary = FeatureProgram(TextFeatureProgram.Normalization), fallback = FeatureProgram(TextFeatureProgram.Fallback);
        byte[] unchanged = primary.ToArray();
        var files = new[]
        {
            new { relativePath = "primary.ttf", byteSize = primary.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(primary)) },
            new { relativePath = "fallback.ttf", byteSize = fallback.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fallback)) }
        };
        var families = new[]
        {
            new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = "primary.ttf" },
            new { requestedFamily = "Fallback", resolvedFamily = "Fallback", relativeFontFile = "fallback.ttf" }
        };
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { packId = "canonical-demand", files = files.Take(coverageCatalog ? 2 : 1), families = families.Take(coverageCatalog ? 2 : 1) });
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifest, "canonical-demand");
        DispatchFontProvider provider = new(new Dictionary<string, byte[]> { ["primary.ttf"] = primary, ["fallback.ttf"] = fallback });
        using TestMemoryBudget allowance = new(1_000_000);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(allowance);
        byte[] first = await RenderPrepared("\u00C1");
        Assert.Equal(["primary.ttf"], provider.Requests);
        Assert.Equal(0, allowance.Reserved);
        Assert.Equal(first, await RenderPrepared("A\u0301"));
        Assert.Equal(["primary.ttf", "primary.ttf"], provider.Requests);
        Assert.Equal(unchanged, primary);
        Assert.Equal(0, allowance.Reserved);

        async Task<byte[]> RenderPrepared(string text)
        {
            byte[] input = Document("<text font-family='Example, Fallback' font-size='8' x='1' y='8'>" + text + "</text>");
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None);
            using MemoryStream output = new();
            document.ConvertToPng(output, CancellationToken.None);
            Assert.True(output.Length > 0);
            return output.ToArray();
        }
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Unsupported_active_lookup_keeps_the_stream_and_allowance_reusable()
    {
        byte[] bytes = FilteredCanonicalProgram(), unchanged = bytes.ToArray();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        using TestMemoryBudget allowance = new(1_000_000);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        options = options.WithMemoryBudget(allowance);
        using MemoryStream output = new();
        SvgException failure = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(Document("<text font-family='Example' y='8'>A</text>"), output, options, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal(0, output.Length);
        Assert.True(output.CanWrite);
        Assert.Equal(0, allowance.Reserved);
        SvgConverter.ConvertToPng(Document("<text font-family='Example' y='8'>V</text>"), output, options, CancellationToken.None);
        Assert.True(output.Length > 0);
        Assert.True(output.CanWrite);
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, allowance.Reserved);
    }

    private static byte[] FeatureProgram(TextFeatureProgram program)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-feature-dispatch", program.ToString().ToLowerInvariant() + ".ttf"));

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canonical_lookup_admission_observes_the_previous_lookups_output(bool unsupported)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CanonicalSequenceProgram(unsupported ? CanonicalTestGraph.Multiple : CanonicalTestGraph.Singles, false))], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        if (unsupported)
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
                SvgTextShaping.Create(font, 24, "A", 0, [], [], [], [], [], null, budget, CancellationToken.None)).ErrorCode);
        else
        {
            using OwnedParserArray<SvgShapingGlyph> shaped = SvgTextShaping.Create(font, 24, "A", 0, [], [], [], [], [], null, budget, CancellationToken.None);
            Assert.Equal(OpenTypeFont.GetGlyphIndex(font.Bytes, 'A', font.Metrics, CancellationToken.None), Assert.Single(shaped.Items.ToArray()).GlyphIndex);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

    }

    private static byte[] CanonicalSequenceProgram(CanonicalTestGraph graph, bool filtered)
    {
        byte[] bytes = FeatureProgram(TextFeatureProgram.Ccmp);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        ushort a = OpenTypeFont.GetGlyphIndex(bytes, 'A', metrics, CancellationToken.None);
        ushort v = OpenTypeFont.GetGlyphIndex(bytes, 'V', metrics, CancellationToken.None);
        int table = bytes.Length, entry = 0, count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int index = 0; index < count; index++)
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12 + index * 16)) == 0x47535542) entry = 12 + index * 16;
        Assert.NotEqual(0, entry);
        bool context = graph is CanonicalTestGraph.Lookahead or CanonicalTestGraph.Backtrack or CanonicalTestGraph.Input;
        int length = context ? 110 : 98;
        Array.Resize(ref bytes, table + length);
        WriteUInt32(bytes, entry + 8, (uint)table);
        WriteUInt32(bytes, entry + 12, (uint)length);
        // One DFLT ccmp feature: A->V, then either V->A or an unsupported
        // MultipleSubst covering V. Its applicability changes after lookup 0.
        foreach ((int offset, ushort value) in new (int, ushort)[]
        {
            (0,1),(4,10),(6,30),(8,46),(10,1),(16,8),(18,4),(24,65535),(26,1),
            (30,1),(36,8),(40,2),(44,1),(46,2),(48,6),(50,26),
            (52,1),(56,1),(58,8),(60,1),(62,6),(64,1),(66,1),(68,1),(70,1),
            (72,1),(76,1),(78,8),(80,1),(82,6),(84,65535),(86,1),(88,1),(90,2)
        }) WriteUInt16(bytes, table + offset, value);
        WriteUInt32(bytes, table + 12, 0x44464C54);
        WriteUInt32(bytes, table + 32, 0x63636D70);
        WriteUInt16(bytes, table + 64, unchecked((ushort)(v - a)));
        WriteUInt16(bytes, table + 70, a);
        if (graph == CanonicalTestGraph.Multiple)
        {
            WriteUInt16(bytes, table + 72, 2);
            foreach ((int offset, ushort value) in new (int, ushort)[] { (82,8),(84,1),(86,14),(88,1),(90,1),(92,2),(94,1),(96,1) })
                WriteUInt16(bytes, table + offset, value);
            WriteUInt16(bytes, table + 92, v);
        }
        else
        {
            WriteUInt16(bytes, table + 84, unchecked((ushort)(a - v)));
            WriteUInt16(bytes, table + 90, v);
        }
        if (context)
        {
            WriteUInt16(bytes, table + 40, 1);
            WriteUInt16(bytes, table + 42, 1);
            WriteUInt16(bytes, table + 72, 6);
            WriteUInt16(bytes, table + 74, filtered ? (ushort)256 : (ushort)0);
            foreach ((int offset, ushort value) in new (int, ushort)[]
            {
                (80,3),(82,0),(84,1),(86,18),(88,1),(90,24),(92,1),(94,0),(96,0),
                (98,1),(100,1),(102,a),(104,1),(106,1),(108,v)
            }) WriteUInt16(bytes, table + offset, value);
            if (graph == CanonicalTestGraph.Backtrack)
                foreach ((int offset, ushort value) in new (int, ushort)[] { (82,1),(84,24),(86,1),(88,18),(90,0) }) WriteUInt16(bytes, table + offset, value);
            else if (graph == CanonicalTestGraph.Input)
                foreach ((int offset, ushort value) in new (int, ushort)[] { (84,2),(88,24),(90,0) }) WriteUInt16(bytes, table + offset, value);
        }
        return bytes;
    }

    private static byte[] FilteredCanonicalProgram()
    {
        byte[] bytes = FeatureProgram(TextFeatureProgram.Ccmp);
        OpenTypeGsubCompositionTable graph = Assert.IsType<OpenTypeGsubCompositionTable>(OpenTypeFont.GetCanonicalSubstitutions(bytes, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), CancellationToken.None));
        int index = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(graph.Feature + 4));
        int lookup = graph.LookupList + BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(graph.LookupList + 2 + index * 2));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(lookup + 2), 8);
        return bytes;
    }

    /// <summary>Serialized test provider; lends unchanged program regions and records each acquisition without caching or owning them.</summary>
    private sealed class DispatchFontProvider(IReadOnlyDictionary<string, byte[]> programs) : ISvgFontProgramSourceProvider
    {
        internal List<string> Requests { get; } = [];
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(file.RelativePath);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, programs[file.RelativePath]));
        }
    }
}
