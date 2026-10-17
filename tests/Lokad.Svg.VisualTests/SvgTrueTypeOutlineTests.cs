using System.Buffers.Binary;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

/// <summary>Checks independent intermediate grid-fit geometry, without claiming Windows font pixel acceptance.</summary>
public sealed class SvgTrueTypeOutlineTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels");
    private static TrueTypeHintTarget Target => new(35, TrueTypeHintQueryFlags.Grayscale, TrueTypeHintControlScale.FractionalFontUnits, TrueTypeHintPreparation.DefaultThenTarget);

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument witnesses = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "true-type-contours.json")));
        foreach (JsonElement item in witnesses.RootElement.GetProperty("cases").EnumerateArray())
            foreach (JsonElement version in item.GetProperty("interpreterVersions").EnumerateArray())
                yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing native geometry identity."), version.GetInt32()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Managed_instruction_fitted_contours_match_independent_native_geometry(string identity, int interpreterVersion)
    {
        using JsonDocument witnesses = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "true-type-contours.json")));
        JsonElement item = witnesses.RootElement.GetProperty("cases").EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == identity);
        byte[] bytes = Font();
        byte[] original = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        var budget = new RenderBudget(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(bytes, item.GetProperty("glyphIndex").GetUInt16(), metrics, item.GetProperty("deviceSize").GetDouble(), Target with { Version = interpreterVersion }, budget, new(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(item.GetProperty("contourEnds").EnumerateArray().Select(end => end.GetInt32()), contours.ContourEnds);
            JsonElement expected = item.GetProperty("segments");
            Assert.Equal(expected.GetArrayLength(), contours.Segments.Length);
            for (int index = 0; index < contours.Segments.Length; index++)
            {
                OpenTypeQuadraticSegment segment = contours.Segments[index];
                Assert.Equal(expected[index].EnumerateArray().Select(value => value.GetDouble()), new[] { segment.Start.X, segment.Start.Y, segment.Control.X, segment.Control.Y, segment.End.X, segment.End.Y });
            }
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void Denied_decoder_or_machine_storage_releases_prior_owners(long maximumBytes)
    {
        byte[] bytes = Font(); byte[] original = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        var budget = new RenderBudget(maximumBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetTrueTypeContours(bytes, 36, metrics, 48, Target, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    [InlineData(5000)]
    [InlineData(10000)]
    [InlineData(20000)]
    public void Expired_decode_and_instruction_work_releases_storage_and_allows_repeatable_retry(long maximumWork)
    {
        byte[] bytes = Font(); byte[] original = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetTrueTypeContours(bytes, 36, metrics, 48, Target, budget, new(maximumWork), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (OpenTypeGlyphContourStorage retry = OpenTypeFont.GetTrueTypeContours(bytes, 36, metrics, 48, Target, budget, new(long.MaxValue), CancellationToken.None)) Assert.Equal(673, retry.Segments[0].End.Y);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(32768)]
    public void Invalid_device_sizes_fail_before_owned_storage(double size)
    {
        byte[] bytes = Font();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetTrueTypeContours(bytes, 36, metrics, size, Target, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Fact]
    public void Rejects_unrepresentable_device_scales_before_decode_allocation()
    {
        byte[] bytes = Font();
        int head = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(TableEntry(bytes, 0x68656164) + 8)));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(head + 18), 16);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetTrueTypeContours(bytes, 36, metrics, 32767, Target, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Fact]
    public void Cancellation_is_observed_before_decode_allocation()
    {
        byte[] bytes = Font();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var budget = new RenderBudget(long.MaxValue);
        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetTrueTypeContours(bytes, 36, metrics, 48, Target, budget, new(long.MaxValue), cancellation.Token));
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData(0, SvgErrorCode.MalformedInput)] // maxp version
    [InlineData(1, SvgErrorCode.MalformedInput)] // zone count
    [InlineData(2, SvgErrorCode.UnsupportedFeature)] // IDEF declarations
    [InlineData(3, SvgErrorCode.MalformedInput)] // odd CVT bytes
    [InlineData(4, SvgErrorCode.MalformedInput)] // duplicate prep
    [InlineData(5, SvgErrorCode.LimitExceeded)] // oversized CVT
    [InlineData(6, SvgErrorCode.LimitExceeded)] // oversized fpgm
    public void Categorizes_hint_table_boundaries_and_releases_decoded_points(int mutation, SvgErrorCode expected)
    {
        byte[] bytes = Font();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        int maxp = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(TableEntry(bytes, 0x6D617870) + 8)));
        switch (mutation)
        {
            case 0: BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(maxp), 0); break;
            case 1: BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(maxp + 14), 3); break;
            case 2: BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(maxp + 22), 1); break;
            case 3: BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(TableEntry(bytes, 0x63767420) + 12), 1); break;
            case 4: BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(TableEntry(bytes, 0x6E616D65)), 0x70726570); break;
            case 5: ExtendTable(0x63767420, 8194); break;
            case 6: ExtendTable(0x6670676D, 65536); break;
        }
        byte[] original = bytes.ToArray(); var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => OpenTypeFont.GetTrueTypeContours(bytes, 36, metrics, 48, Target, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, bytes);

        void ExtendTable(uint tag, int length)
        {
            int at = TableEntry(bytes, tag);
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 8)));
            Array.Resize(ref bytes, Math.Max(bytes.Length, checked(offset + length)));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 12), checked((uint)length));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsupported_glyph_instructions_release_decoder_and_interpreter_storage(bool verticalPhantom)
    {
        byte[] bytes = Font();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        OpenTypeSimpleGlyphStructure structure = OpenTypeFont.GetSimpleGlyphStructure(bytes, 36, metrics, CancellationToken.None);
        int lengthOffset = structure.GlyphRange.Offset + 10 + structure.ContourEndpoints.Length * 2;
        int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(lengthOffset));
        Span<byte> instructions = bytes.AsSpan(lengthOffset + 2, length); instructions.Clear();
        if (verticalPhantom)
        {
            instructions[0] = 0xB0; instructions[1] = checked((byte)(structure.ContourEndpoints[^1] + 3)); instructions[2] = 0x46;
        }
        else instructions[0] = 0x0A;
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetTrueTypeContours(bytes, 36, metrics, 48, Target, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Scaled_offset_flag_preserves_untransformed_accent_contours()
    {
        byte[] bytes = Font();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        ushort glyph = OpenTypeFont.GetGlyphIndex(bytes, 'é', metrics, CancellationToken.None);
        RenderBudget baseline = new(long.MaxValue);
        OpenTypeQuadraticSegment[] expected;
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(bytes, glyph, metrics, 28, Target, baseline, new(long.MaxValue), CancellationToken.None)) expected = contours.Segments.ToArray();
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
        OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(bytes, glyph, CancellationToken.None);
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(range.Offset + 10));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(range.Offset + 10), (ushort)(flags | 2048));
        var budget = new RenderBudget(long.MaxValue);
        byte[] unchanged = bytes.ToArray();
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(bytes, glyph, metrics, 28, Target, budget, new(long.MaxValue), CancellationToken.None)) Assert.Equal(expected, contours.Segments);
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Font() => File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSans-Regular.ttf"));

    private static int TableEntry(byte[] bytes, uint tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int index = 0; index < count; index++)
        {
            int at = 12 + index * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at)) == tag) return at;
        }
        throw new InvalidDataException("The pinned font is missing a required test table.");
    }
}
