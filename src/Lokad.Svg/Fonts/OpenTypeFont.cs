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
