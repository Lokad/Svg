namespace Lokad.Svg.Fonts;

/// <summary>
/// Validates the bounded standalone-TrueType subset required before glyph parsing.
/// This reader borrows its input and does not retain, copy, or cache font bytes.
/// </summary>
internal static class OpenTypeFont
{
    private const int MaximumTableRecords = 128;
    private const uint TrueTypeScalerType = 0x00010000;
    private const uint TrueTypeCollectionTag = 0x74746366;
    private const uint HeadTag = 0x68656164;
    private const uint MaxpTag = 0x6D617870;
    private const uint HheaTag = 0x68686561;
    private const uint HmtxTag = 0x686D7478;
    private const uint CmapTag = 0x636D6170;

    internal static OpenTypeFontMetrics ParseMetrics(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> data = bytes.Span;
        RequireRange(data, 0, 12, "SFNT header");
        uint scalerType = ReadUInt32(data, 0);
        if (scalerType == TrueTypeCollectionTag) throw new SvgException(SvgErrorCode.UnsupportedFeature, "TrueType collections are not supported for SVG text rendering.");
        if (scalerType != TrueTypeScalerType) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only standalone TrueType glyf fonts are supported for SVG text rendering.");

        int tableCount = ReadUInt16(data, 4);
        if (tableCount == 0 || tableCount > MaximumTableRecords) throw InvalidFont("The SFNT table count is outside the supported range.");
        RequireRange(data, 12, checked(tableCount * 16), "SFNT table directory");
        var tables = new Dictionary<uint, OpenTypeTableRange>(tableCount);
        for (int index = 0; index < tableCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int offset = 12 + index * 16;
            uint tag = ReadUInt32(data, offset);
            uint tableOffset = ReadUInt32(data, offset + 8);
            uint length = ReadUInt32(data, offset + 12);
            if (tableOffset > int.MaxValue || length > int.MaxValue) throw InvalidFont("An SFNT table range exceeds the supported input size.");
            RequireRange(data, (int)tableOffset, (int)length, "SFNT table");
            if (!tables.TryAdd(tag, new OpenTypeTableRange((int)tableOffset, (int)length))) throw InvalidFont("The SFNT directory contains duplicate table tags.");
        }

        OpenTypeTableRange head = RequiredTable(tables, HeadTag, "head");
        OpenTypeTableRange maxp = RequiredTable(tables, MaxpTag, "maxp");
        OpenTypeTableRange hhea = RequiredTable(tables, HheaTag, "hhea");
        OpenTypeTableRange hmtx = RequiredTable(tables, HmtxTag, "hmtx");
        RequireTableLength(head, 54, "head");
        RequireTableLength(maxp, 6, "maxp");
        RequireTableLength(hhea, 36, "hhea");

        ushort unitsPerEm = ReadUInt16(data, head.Offset + 18);
        if (unitsPerEm is < 16 or > 16_384) throw InvalidFont("The head unitsPerEm value is outside the supported range.");
        short indexToLocFormat = ReadInt16(data, head.Offset + 50);
        if (indexToLocFormat is not 0 and not 1) throw InvalidFont("The head indexToLocFormat value is invalid.");
        ushort glyphCount = ReadUInt16(data, maxp.Offset + 4);
        if (glyphCount == 0) throw InvalidFont("The maxp table declares no glyphs.");
        ushort numberOfHMetrics = ReadUInt16(data, hhea.Offset + 34);
        if (numberOfHMetrics == 0 || numberOfHMetrics > glyphCount) throw InvalidFont("The hhea numberOfHMetrics value is invalid.");
        int hmtxBytes = checked(numberOfHMetrics * 4 + (glyphCount - numberOfHMetrics) * 2);
        RequireTableLength(hmtx, hmtxBytes, "hmtx");
        return new OpenTypeFontMetrics(unitsPerEm, glyphCount, numberOfHMetrics, indexToLocFormat);
    }

    /// <summary>Maps one Unicode scalar through a validated format-4 character map.</summary>
    internal static ushort GetGlyphIndex(ReadOnlyMemory<byte> bytes, uint scalar, CancellationToken cancellationToken)
    {
        if (scalar > 0x10FFFF) throw new ArgumentOutOfRangeException(nameof(scalar));
        OpenTypeFontMetrics metrics = ParseMetrics(bytes, cancellationToken);
        if (scalar > 0xFFFF) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The selected font has no supported supplementary-plane character map.");
        ReadOnlySpan<byte> data = bytes.Span;
        OpenTypeTableRange cmap = FindTable(data, CmapTag, "cmap");
        RequireTableLength(cmap, 4, "cmap");
        int encodingCount = ReadUInt16(data, cmap.Offset + 2);
        RequireTableLength(cmap, checked(4 + encodingCount * 8), "cmap");
        OpenTypeTableRange? selected = null;
        for (int index = 0; index < encodingCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int record = cmap.Offset + 4 + index * 8;
            ushort platform = ReadUInt16(data, record);
            ushort encoding = ReadUInt16(data, record + 2);
            uint relativeOffset = ReadUInt32(data, record + 4);
            if (relativeOffset > int.MaxValue || relativeOffset > cmap.Length - 2) throw InvalidFont("A cmap encoding record points outside its table.");
            int offset = checked(cmap.Offset + (int)relativeOffset);
            if (ReadUInt16(data, offset) == 4 && (platform == 3 && (encoding == 1 || encoding == 0))) selected = new OpenTypeTableRange(offset, cmap.Length - (int)relativeOffset);
        }
        if (selected is not OpenTypeTableRange format4) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The selected font has no supported Unicode cmap format 4 table.");
        RequireTableLength(format4, 16, "cmap format 4");
        int length = ReadUInt16(data, format4.Offset + 2);
        if (length < 16 || length > format4.Length) throw InvalidFont("The cmap format 4 length is invalid.");
        int segmentsX2 = ReadUInt16(data, format4.Offset + 6);
        if (segmentsX2 == 0 || (segmentsX2 & 1) != 0) throw InvalidFont("The cmap format 4 segment count is invalid.");
        int segments = segmentsX2 / 2;
        int required = checked(16 + segments * 8);
        if (required > length) throw InvalidFont("The cmap format 4 arrays are truncated.");
        int endCodes = format4.Offset + 14;
        int startCodes = endCodes + segments * 2 + 2;
        int deltas = startCodes + segments * 2;
        int ranges = deltas + segments * 2;
        for (int index = 0; index < segments; index++)
        {
            ushort end = ReadUInt16(data, endCodes + index * 2);
            if (scalar > end) continue;
            ushort start = ReadUInt16(data, startCodes + index * 2);
            if (scalar < start) return 0;
            short delta = ReadInt16(data, deltas + index * 2);
            ushort rangeOffset = ReadUInt16(data, ranges + index * 2);
            ushort glyph = rangeOffset == 0
                ? unchecked((ushort)(scalar + delta))
                : ApplyDelta(ReadGlyphFromRange(data, format4, length, ranges + index * 2, rangeOffset, scalar - start), delta);
            if (glyph >= metrics.GlyphCount && glyph != 0) throw InvalidFont("The cmap maps a scalar to a glyph outside maxp.");
            return glyph;
        }
        return 0;
    }

    /// <summary>Gets the validated horizontal advance for one existing glyph index.</summary>
    internal static ushort GetAdvanceWidth(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
    {
        OpenTypeFontMetrics metrics = ParseMetrics(bytes, cancellationToken);
        if (glyphIndex >= metrics.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyphIndex));
        ReadOnlySpan<byte> data = bytes.Span;
        OpenTypeTableRange hmtx = FindTable(data, HmtxTag, "hmtx");
        int metricIndex = Math.Min(glyphIndex, metrics.NumberOfHMetrics - 1);
        return ReadUInt16(data, hmtx.Offset + metricIndex * 4);
    }

    private static ushort ReadGlyphFromRange(ReadOnlySpan<byte> data, OpenTypeTableRange table, int length, int rangeOffsetLocation, ushort rangeOffset, uint relativeGlyphIndex)
    {
        int glyphOffset = checked(rangeOffsetLocation + rangeOffset + checked((int)relativeGlyphIndex * 2));
        if (glyphOffset < table.Offset || glyphOffset > table.Offset + length - 2) throw InvalidFont("The cmap glyph-index array is truncated.");
        return ReadUInt16(data, glyphOffset);
    }

    private static ushort ApplyDelta(ushort glyph, short delta) => glyph == 0 ? (ushort)0 : unchecked((ushort)(glyph + delta));

    private static OpenTypeTableRange FindTable(ReadOnlySpan<byte> data, uint wantedTag, string name)
    {
        int count = ReadUInt16(data, 4);
        for (int index = 0; index < count; index++)
        {
            int entry = 12 + index * 16;
            if (ReadUInt32(data, entry) != wantedTag) continue;
            uint offset = ReadUInt32(data, entry + 8);
            uint length = ReadUInt32(data, entry + 12);
            if (offset > int.MaxValue || length > int.MaxValue) throw InvalidFont("The SFNT " + name + " table range exceeds the supported input size.");
            RequireRange(data, (int)offset, (int)length, "SFNT " + name + " table");
            return new OpenTypeTableRange((int)offset, (int)length);
        }
        throw InvalidFont("The SFNT font is missing its " + name + " table.");
    }

    private static OpenTypeTableRange RequiredTable(IReadOnlyDictionary<uint, OpenTypeTableRange> tables, uint tag, string name)
        => tables.TryGetValue(tag, out OpenTypeTableRange range) ? range : throw InvalidFont("The SFNT font is missing its " + name + " table.");

    private static void RequireTableLength(OpenTypeTableRange range, int minimumLength, string name)
    {
        if (range.Length < minimumLength) throw InvalidFont("The SFNT " + name + " table is truncated.");
    }

    private static void RequireRange(ReadOnlySpan<byte> data, int offset, int length, string name)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length) throw InvalidFont("The " + name + " range is outside the font program.");
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) => (ushort)((data[offset] << 8) | data[offset + 1]);

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset) => unchecked((short)ReadUInt16(data, offset));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static SvgException InvalidFont(string message) => new(SvgErrorCode.MalformedInput, message);
}

/// <summary>Represents validated core metrics from borrowed standalone-TrueType data.</summary>
internal readonly record struct OpenTypeFontMetrics(ushort UnitsPerEm, ushort GlyphCount, ushort NumberOfHMetrics, short IndexToLocFormat);

internal readonly record struct OpenTypeTableRange(int Offset, int Length);
