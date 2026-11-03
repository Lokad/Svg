using System.Buffers.Binary;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Canonical_path_storage_is_exclusive_and_released_after_refusal_and_retry()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        ushort index = OpenTypeFont.GetGlyphIndex(font.Bytes, 'g', font.Metrics, CancellationToken.None);
        RenderBudget measured = new(new HostMemoryBudget(1_000_000));
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(font, new(index, 12, 36, 0), 24, new(18, 0, 0, 1, 0, 0), measured))
        using (SvgAuthoredPath path = source.CreateCanonicalPath(1, 0, 1, 0, measured, CancellationToken.None))
        {
            Assert.True(source.UsesCanonicalPath(1, 1));
            Assert.NotEmpty(path.Commands.ToArray());
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        HostMemoryBudget host = new(measured.Snapshot().PeakBytes - 1);
        RenderBudget refused = new(host);
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(font, new(index, 12, 36, 0), 24, new(18, 0, 0, 1, 0, 0), refused))
        {
            long retained = refused.Snapshot().CurrentBytes;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => source.CreateCanonicalPath(1, 0, 1, 0, refused, CancellationToken.None)).ErrorCode);
            Assert.Equal(retained, refused.Snapshot().CurrentBytes);
            host.Limit = measured.Snapshot().PeakBytes;
            using (SvgAuthoredPath retry = source.CreateCanonicalPath(1, 0, 1, 0, refused, CancellationToken.None)) Assert.NotEmpty(retry.Commands.ToArray());
            Assert.Equal(retained, refused.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => source.CreateCanonicalPath(1, 0, 1, 0, refused, cancellation.Token));
            Assert.Equal(retained, refused.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, refused.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public async Task Canonical_unused_mark_features_do_not_poison_a_plain_physical_font_partition()
    {
        byte[] good = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf")), bad = good.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bad, CancellationToken.None);
        OpenTypeGposMarkTable graph = Assert.IsType<OpenTypeGposMarkTable>(OpenTypeFont.GetMarkPositioning(bad, metrics, CancellationToken.None));
        int selected = Read16(bad, graph.MarkFeature + 4), lookup = graph.LookupList + Read16(bad, graph.LookupList + 2 + selected * 2);
        int extension = lookup + Read16(bad, lookup + 6);
        int subtable = extension + (int)BinaryPrimitives.ReadUInt32BigEndian(bad.AsSpan(extension + 4));
        int marks = subtable + Read16(bad, subtable + 8);
        WriteUInt16(bad, marks + Read16(bad, marks + 4), 2);
        using SvgFontSnapshot ordinary = await SvgFontSnapshot.CreateAsync([CreateResolution(bad)], CancellationToken.None);
        using SvgFontSnapshot canonical = await SvgFontSnapshot.CreateAsync([CreateResolution(good)], CancellationToken.None);
        SvgPreparedFont plain = Assert.Single(ordinary.Fonts), marked = Assert.Single(canonical.Fonts);
        SvgTextResolvedFontSpan[] fonts = [new(0, 1, new(plain, 24)), new(1, 4, new(marked, 24))];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (OwnedParserArray<SvgShapingGlyph> result = SvgTextShaping.Create(plain, 24, "Ai\u0307\u0301", 0, [], [], [], fonts, [], [], null, budget, CancellationToken.None))
        {
            Assert.Equal(new ushort[] { 36, 1766, 2661, 2663 }, result.Items.Select(glyph => glyph.GlyphIndex));
            Assert.Equal(new[] { -1, -1, 1, 2 }, result.Items.Select(glyph => glyph.Attachment.Parent));
            Assert.Equal(new[] { 0, 1, 2, 3 }, result.Items.Select(glyph => glyph.SourceOffset));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static int Read16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Canonical_shaping_releases_partial_and_exact_storage_after_refusal_cancellation_and_retry()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        RenderBudget measured = new(new HostMemoryBudget(1_000_000));
        using (OwnedParserArray<SvgShapingGlyph> result = Create(font, measured, CancellationToken.None)) Assert.NotEmpty(result.Items);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        HostMemoryBudget host = new(measured.Snapshot().PeakBytes - 1);
        RenderBudget refused = new(host);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Create(font, refused, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, refused.Snapshot().CurrentBytes);
        host.Limit = measured.Snapshot().PeakBytes;
        using (OwnedParserArray<SvgShapingGlyph> retry = Create(font, refused, CancellationToken.None)) Assert.NotEmpty(retry.Items);
        Assert.Equal(0, refused.Snapshot().CurrentBytes);

        RenderBudget partial = new(new HostMemoryBudget(1_000_000));
        using CancellationTokenSource cancellation = new();
        bool observedPartial = false;
        using (CmapLookupProbe.Arm(() =>
        {
            if (partial.Snapshot().CurrentBytes == 0) return;
            observedPartial = true;
            cancellation.Cancel();
        }))
        {
            OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => Create(font, partial, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        Assert.True(observedPartial);
        Assert.Equal(0, partial.Snapshot().CurrentBytes);
        using (OwnedParserArray<SvgShapingGlyph> retry = Create(font, partial, CancellationToken.None)) Assert.NotEmpty(retry.Items);
        Assert.Equal(0, partial.Snapshot().CurrentBytes);

        static OwnedParserArray<SvgShapingGlyph> Create(SvgPreparedFont font, RenderBudget budget, CancellationToken token)
            => SvgTextShaping.Create(font, 24, "i\u0307\u0301 j\u0307\u0323\u0301 \u012F\u0307\u0300", 0, [], [], [], [], [], [], null, budget, token);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("anchor-format", SvgErrorCode.UnsupportedFeature)]
    [InlineData("mark-count", SvgErrorCode.MalformedInput)]
    [InlineData("mark-class", SvgErrorCode.MalformedInput)]
    [InlineData("parent-count", SvgErrorCode.MalformedInput)]
    [InlineData("missing-anchor", SvgErrorCode.MalformedInput)]
    [InlineData("class-limit", SvgErrorCode.LimitExceeded)]
    [InlineData("filter-set", SvgErrorCode.MalformedInput)]
    [InlineData("definition-version", SvgErrorCode.MalformedInput)]
    [InlineData("mark-role", SvgErrorCode.UnsupportedFeature)]
    public async Task Canonical_attachment_admission_checks_selected_tables_and_releases_staging(string defect, SvgErrorCode expected)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        Mutate(bytes, defect);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        SvgException error = Assert.Throws<SvgException>(() => SvgTextShaping.Create(Assert.Single(fonts.Fonts), 24, "i\u0307\u0301", 0,
            [], [], [], [], [], [], null, budget, CancellationToken.None));
        Assert.Equal(expected, error.ErrorCode);
        Assert.True(budget.Snapshot().PeakBytes > 0);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static void Mutate(byte[] bytes, string defect)
        {
            OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
            OpenTypeGposMarkTable graph = Assert.IsType<OpenTypeGposMarkTable>(OpenTypeFont.GetMarkPositioning(bytes, metrics, CancellationToken.None));
            int index = Read16(bytes, graph.MarkFeature + 4);
            int lookup = graph.LookupList + Read16(bytes, graph.LookupList + 2 + index * 2);
            int extension = lookup + Read16(bytes, lookup + 6);
            int subtable = extension + (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(extension + 4));
            int marks = subtable + Read16(bytes, subtable + 8), parents = subtable + Read16(bytes, subtable + 10);
            switch (defect)
            {
                case "anchor-format": WriteUInt16(bytes, marks + Read16(bytes, marks + 4), 2); break;
                case "mark-count": WriteUInt16(bytes, marks, 0); break;
                case "mark-class": WriteUInt16(bytes, marks + 2, (ushort)Read16(bytes, subtable + 6)); break;
                case "parent-count": WriteUInt16(bytes, parents, (ushort)(Read16(bytes, parents) + 1)); break;
                case "missing-anchor": WriteUInt16(bytes, marks + 4, 0); break;
                case "class-limit": WriteUInt16(bytes, subtable + 6, 257); break;
                case "filter-set":
                    int selected = Read16(bytes, graph.MarkToMarkFeature + 4);
                    int filtered = graph.LookupList + Read16(bytes, graph.LookupList + 2 + selected * 2);
                    WriteUInt16(bytes, filtered + 6 + Read16(bytes, filtered + 4) * 2, ushort.MaxValue);
                    break;
                case "definition-version": WriteUInt16(bytes, Assert.IsType<OpenTypeTableRange>(metrics.GdefTable).Offset + 2, 0); break;
                case "mark-role":
                    int definition = Assert.IsType<OpenTypeTableRange>(metrics.GdefTable).Offset;
                    int classes = definition + Read16(bytes, definition + 4);
                    Assert.Equal(2, Read16(bytes, classes));
                    int markGlyph = OpenTypeFont.GetGlyphIndex(bytes, 0x307, metrics, CancellationToken.None);
                    bool changed = false;
                    for (int row = 0; row < Read16(bytes, classes + 2); row++)
                    {
                        int record = classes + 4 + row * 6;
                        if (markGlyph < Read16(bytes, record) || markGlyph > Read16(bytes, record + 2)) continue;
                        WriteUInt16(bytes, record + 4, 0); changed = true; break;
                    }
                    Assert.True(changed);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        }

        static int Read16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
    }
}
