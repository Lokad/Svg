namespace Lokad.Svg.Fonts;

using Lokad.Svg.Rendering;
using Lokad.Svg.Parsing;

/// <summary>
/// Validates the bounded standalone-TrueType subset required before glyph parsing.
/// This reader borrows its input and does not retain, copy, or cache font bytes.
/// </summary>
internal static partial class OpenTypeFont
{
    private const int MaximumTableRecords = 128;
    private const int MaximumSimpleGlyphContours = 4_096;
    private const int MaximumSimpleGlyphPoints = 32_768;
    private const int MaximumCompositeGlyphComponents = 64;
    private const int MaximumCompositeGlyphDepth = 8;
    private const int MaximumExpandedGlyphSegments = 65_536;
    private const ushort SupportedCompositeFlags = 1 | 2 | 4 | 8 | 32 | 64 | 128 | 256 | 512 | 1024 | 2048 | 4096;
    private const uint TrueTypeScalerType = 0x00010000;
    private const uint TrueTypeCollectionTag = 0x74746366;
    private const uint HeadTag = 0x68656164;
    private const uint MaxpTag = 0x6D617870;
    private const uint HheaTag = 0x68686561;
    private const uint HmtxTag = 0x686D7478;
    private const uint CmapTag = 0x636D6170;
    private const uint NameTag = 0x6E616D65;
    private const uint Os2Tag = 0x4F532F32;
    private const uint KernTag = 0x6B65726E;
    private const uint GposTag = 0x47504F53;
    private const uint GsubTag = 0x47535542;
    private const uint GdefTag = 0x47444546;
    private const uint KernFeatureTag = 0x6B65726E;
    private const uint DefaultScriptTag = 0x44464C54;
    private const uint LocaTag = 0x6C6F6361;
    private const uint GlyfTag = 0x676C7966;

    internal static OpenTypeFontMetrics ParseMetrics(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        => ParseMetrics(bytes, null, cancellationToken);

    /// <summary>Parses core metrics while charging visited SFNT directory records to one operation work counter.</summary>
    internal static OpenTypeFontMetrics ParseMetrics(ReadOnlyMemory<byte> bytes, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        Consume(workCounter, cancellationToken);
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
            Consume(workCounter, cancellationToken);
            int offset = 12 + index * 16;
            uint tag = ReadUInt32(data, offset);
            uint tableOffset = ReadUInt32(data, offset + 8);
            uint length = ReadUInt32(data, offset + 12);
            if (tableOffset > int.MaxValue || length > int.MaxValue) throw InvalidFont("An SFNT table range exceeds the supported input size.");
            RequireRange(data, (int)tableOffset, (int)length, "SFNT table");
            if (!tables.TryAdd(tag, new OpenTypeTableRange((int)tableOffset, (int)length))) throw InvalidFont("The SFNT directory contains duplicate table tags.");
            FontMetricsProbe.TableRecordVisited();
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
        if (tables.TryGetValue(NameTag, out OpenTypeTableRange name)) ValidateNameTable(data, name, workCounter, cancellationToken);
        if (tables.TryGetValue(Os2Tag, out OpenTypeTableRange os2)) ValidateOs2Table(data, os2, workCounter, cancellationToken);
        OpenTypeKernTable? kern = tables.TryGetValue(KernTag, out OpenTypeTableRange kernTable) ? ParseKernTable(data, kernTable, glyphCount, workCounter, cancellationToken) : null;
        OpenTypeGposPairTable? gpos = tables.TryGetValue(GposTag, out OpenTypeTableRange gposTable) ? ParseGposPairTable(data, gposTable, tables.TryGetValue(GdefTag, out OpenTypeTableRange gdef) ? gdef : null, glyphCount, workCounter, cancellationToken) : null;
        OpenTypeFontLineMetrics lineMetrics = ReadLineMetrics(data, hhea, tables.TryGetValue(Os2Tag, out OpenTypeTableRange lineOs2) ? lineOs2 : null);
        return new OpenTypeFontMetrics(unitsPerEm, glyphCount, numberOfHMetrics, indexToLocFormat, hmtx, tables.TryGetValue(CmapTag, out OpenTypeTableRange cmap) ? cmap : null, kern, gpos, tables.TryGetValue(GsubTag, out OpenTypeTableRange gsub) ? gsub : null, tables.TryGetValue(LocaTag, out OpenTypeTableRange loca) ? loca : null, tables.TryGetValue(GlyfTag, out OpenTypeTableRange glyf) ? glyf : null, (ReadUInt16(bytes.Span, head.Offset + 16) & 8) != 0, lineMetrics);

        static OpenTypeFontLineMetrics ReadLineMetrics(ReadOnlySpan<byte> data, OpenTypeTableRange hhea, OpenTypeTableRange? os2)
        {
            int ascent = ReadInt16(data, hhea.Offset + 4), descent = ReadInt16(data, hhea.Offset + 6);
            int windowsAscent = Math.Max(0, ascent), windowsDescent = Math.Max(0, -descent);
            if (os2 is {} table && table.Length >= 78)
            {
                int typoAscent = ReadInt16(data, table.Offset + 68), typoDescent = ReadInt16(data, table.Offset + 70);
                windowsAscent = ReadUInt16(data, table.Offset + 74); windowsDescent = ReadUInt16(data, table.Offset + 76);
                bool useTypo = (ReadUInt16(data, table.Offset + 62) & 128) != 0;
                if (useTypo || ascent == 0 && descent == 0)
                {
                    bool hasTypo = useTypo || typoAscent != 0 || typoDescent != 0;
                    ascent = hasTypo ? typoAscent : windowsAscent;
                    descent = hasTypo ? typoDescent : -windowsDescent;
                }
                if (useTypo) { windowsAscent = typoAscent; windowsDescent = -typoDescent; }
            }
            int xHeight = os2 is { Length: >= 88 } heightTable && ReadUInt16(data, heightTable.Offset) >= 2
                ? Math.Max(0, (int)ReadInt16(data, heightTable.Offset + 86)) : 0;
            return new(ascent, descent, windowsAscent, windowsDescent) { XHeight = xHeight };
        }
    }

    /// <summary>Maps one Unicode scalar through a validated Unicode format-4 or format-12 character map.</summary>
    internal static ushort GetGlyphIndex(ReadOnlyMemory<byte> bytes, uint scalar, CancellationToken cancellationToken)
        => GetGlyphIndex(bytes, scalar, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Maps one Unicode scalar using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static ushort GetGlyphIndex(ReadOnlyMemory<byte> bytes, uint scalar, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
        => GetGlyphIndex(bytes, scalar, metrics, null, cancellationToken);

    /// <summary>Maps one Unicode scalar with the conversion work counter used by direct-text layout.</summary>
    internal static ushort GetGlyphIndex(ReadOnlyMemory<byte> bytes, uint scalar, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF) throw new ArgumentOutOfRangeException(nameof(scalar));
        CmapLookupProbe.Started();
        Consume(workCounter, cancellationToken);
        ReadOnlySpan<byte> data = bytes.Span;
        OpenTypeTableRange cmap = metrics.Cmap ?? throw InvalidFont("The SFNT font is missing its cmap table.");
        RequireTableLength(cmap, 4, "cmap");
        int encodingCount = ReadUInt16(data, cmap.Offset + 2);
        RequireTableLength(cmap, checked(4 + encodingCount * 8), "cmap");
        OpenTypeTableRange? format4Candidate = null;
        OpenTypeTableRange? format12Candidate = null;
        for (int index = 0; index < encodingCount; index++)
        {
            Consume(workCounter, cancellationToken);
            int record = cmap.Offset + 4 + index * 8;
            ushort platform = ReadUInt16(data, record);
            ushort encoding = ReadUInt16(data, record + 2);
            uint relativeOffset = ReadUInt32(data, record + 4);
            if (relativeOffset > int.MaxValue || relativeOffset > cmap.Length - 2) throw InvalidFont("A cmap encoding record points outside its table.");
            int offset = checked(cmap.Offset + (int)relativeOffset);
            ushort format = ReadUInt16(data, offset);
            bool isUnicodeFormat4 = format == 4 && (platform == 0 || (platform == 3 && encoding == 1));
            bool isUnicodeFormat12 = format == 12 && (platform == 0 || (platform == 3 && encoding == 10));
            if (isUnicodeFormat4) format4Candidate = new OpenTypeTableRange(offset, cmap.Length - (int)relativeOffset);
            if (isUnicodeFormat12) format12Candidate = new OpenTypeTableRange(offset, cmap.Length - (int)relativeOffset);
        }
        if (format12Candidate is OpenTypeTableRange selected12) return GetFormat12GlyphIndex(data, selected12, scalar, metrics.GlyphCount, workCounter, cancellationToken);
        if (format4Candidate is not OpenTypeTableRange selected4) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The selected font has no supported Unicode cmap format 4 table.");
        OpenTypeTableRange format4 = selected4;
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
        ValidateFormat4Segments(data, format4, length, segments, endCodes, startCodes, ranges, workCounter, cancellationToken);
        for (int index = 0; index < segments; index++)
        {
            Consume(workCounter, cancellationToken);
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

    private static ushort GetFormat12GlyphIndex(ReadOnlySpan<byte> data, OpenTypeTableRange table, uint scalar, ushort glyphCount, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        RequireTableLength(table, 16, "cmap format 12");
        uint length = ReadUInt32(data, table.Offset + 4);
        uint groups = ReadUInt32(data, table.Offset + 12);
        if (length < 16 || length > (uint)table.Length || groups > (length - 16) / 12) throw InvalidFont("The cmap format 12 length is invalid.");
        uint previousEnd = 0;
        ushort glyph = 0;
        for (uint index = 0; index < groups; index++)
        {
            Consume(workCounter, cancellationToken);
            int offset = checked(table.Offset + 16 + (int)index * 12);
            uint start = ReadUInt32(data, offset);
            uint end = ReadUInt32(data, offset + 4);
            uint startGlyph = ReadUInt32(data, offset + 8);
            if (start > end || end > 0x10FFFF || (index != 0 && start <= previousEnd)) throw InvalidFont("The cmap format 12 groups are not strictly ordered.");
            previousEnd = end;
            uint groupLength = end - start;
            if (startGlyph > uint.MaxValue - groupLength) throw InvalidFont("The cmap format 12 group glyph range overflows.");
            uint lastGlyph = startGlyph + groupLength;
            if (lastGlyph >= glyphCount) throw InvalidFont("The cmap maps a scalar to a glyph outside maxp.");
            if (scalar >= start && scalar <= end) glyph = (ushort)(startGlyph + scalar - start);
        }
        return glyph;
    }

    private static void ValidateFormat4Segments(ReadOnlySpan<byte> data, OpenTypeTableRange format4, int length, int segments, int endCodes, int startCodes, int ranges, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        int glyphIndexArray = checked(ranges + segments * 2);
        ushort previousEnd = 0;
        for (int index = 0; index < segments; index++)
        {
            Consume(workCounter, cancellationToken);
            ushort end = ReadUInt16(data, endCodes + index * 2);
            ushort start = ReadUInt16(data, startCodes + index * 2);
            if (start > end || (index != 0 && end <= previousEnd)) throw InvalidFont("The cmap format 4 segments are not strictly ordered.");
            previousEnd = end;
            ushort rangeOffset = ReadUInt16(data, ranges + index * 2);
            if (rangeOffset == 0) continue;
            if ((rangeOffset & 1) != 0) throw InvalidFont("The cmap format 4 glyph-index offset is not word-aligned.");
            int firstGlyph = checked(ranges + index * 2 + rangeOffset);
            int lastGlyph = checked(firstGlyph + (end - start) * 2);
            if (firstGlyph < glyphIndexArray || lastGlyph > format4.Offset + length - 2) throw InvalidFont("The cmap format 4 glyph-index array is truncated.");
        }
        if (previousEnd != ushort.MaxValue) throw InvalidFont("The cmap format 4 segments lack the required sentinel.");
    }

    /// <summary>Gets the validated horizontal advance for one existing glyph index.</summary>
    internal static ushort GetAdvanceWidth(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
        => GetAdvanceWidth(bytes, glyphIndex, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Gets one advance using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static ushort GetAdvanceWidth(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
        => GetAdvanceWidth(bytes, glyphIndex, metrics, null, cancellationToken);

    /// <summary>Gets one advance with the conversion work counter used by direct-text layout.</summary>
    internal static ushort GetAdvanceWidth(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (glyphIndex >= metrics.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyphIndex));
        Consume(workCounter, cancellationToken);
        ReadOnlySpan<byte> data = bytes.Span;
        OpenTypeTableRange hmtx = metrics.Hmtx;
        int metricIndex = Math.Min(glyphIndex, metrics.NumberOfHMetrics - 1);
        return ReadUInt16(data, hmtx.Offset + metricIndex * 4);
    }

    /// <summary>Gets the admitted legacy horizontal kerning adjustment for one adjacent glyph pair.</summary>
    internal static short GetKerningAdjustment(ReadOnlyMemory<byte> bytes, ushort leftGlyphIndex, ushort rightGlyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (leftGlyphIndex >= metrics.GlyphCount) throw new ArgumentOutOfRangeException(nameof(leftGlyphIndex));
        if (rightGlyphIndex >= metrics.GlyphCount) throw new ArgumentOutOfRangeException(nameof(rightGlyphIndex));
        if (metrics.Kern is not OpenTypeKernTable kern) return 0;
        ReadOnlySpan<byte> data = bytes.Span;
        int low = 0;
        int high = kern.PairCount;
        while (low < high)
        {
            Consume(workCounter, cancellationToken);
            int index = low + (high - low) / 2;
            int offset = kern.PairOffset + index * 6;
            ushort candidateLeft = ReadUInt16(data, offset);
            ushort candidateRight = ReadUInt16(data, offset + 2);
            if (candidateLeft < leftGlyphIndex || candidateLeft == leftGlyphIndex && candidateRight < rightGlyphIndex) low = index + 1;
            else if (candidateLeft > leftGlyphIndex || candidateLeft == leftGlyphIndex && candidateRight > rightGlyphIndex) high = index;
            else return ReadInt16(data, offset + 4);
        }
        return 0;
    }

    /// <summary>Gets the validated borrowed glyf-table range for one glyph, which may be empty.</summary>
    internal static OpenTypeTableRange GetGlyphDataRange(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
        => GetGlyphDataRange(bytes, glyphIndex, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Gets one borrowed glyph range using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static OpenTypeTableRange GetGlyphDataRange(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
        => GetGlyphDataRange(bytes, glyphIndex, metrics, null, cancellationToken);

    /// <summary>Gets one borrowed glyph range while charging the loca lookup to a conversion work counter.</summary>
    internal static OpenTypeTableRange GetGlyphDataRange(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        Consume(workCounter, cancellationToken);
        if (glyphIndex >= metrics.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyphIndex));
        ReadOnlySpan<byte> data = bytes.Span;
        OpenTypeTableRange loca = metrics.Loca ?? throw InvalidFont("The SFNT font is missing its loca table.");
        OpenTypeTableRange glyf = metrics.Glyf ?? throw InvalidFont("The SFNT font is missing its glyf table.");
        int entrySize = metrics.IndexToLocFormat == 0 ? 2 : 4;
        RequireTableLength(loca, checked((metrics.GlyphCount + 1) * entrySize), "loca");
        int start = ReadGlyphLocation(data, loca, glyphIndex, metrics.IndexToLocFormat);
        int end = ReadGlyphLocation(data, loca, glyphIndex + 1, metrics.IndexToLocFormat);
        if (end < start || end > glyf.Length) throw InvalidFont("The loca table contains an invalid glyph range.");
        return new OpenTypeTableRange(checked(glyf.Offset + start), end - start);
    }

    /// <summary>Reads a nonempty glyph header after validating its loca/glyf range.</summary>
    internal static OpenTypeGlyphHeader? GetGlyphHeader(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
        => GetGlyphHeader(bytes, glyphIndex, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Reads one glyph header using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static OpenTypeGlyphHeader? GetGlyphHeader(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
        => GetGlyphHeader(bytes, glyphIndex, metrics, null, cancellationToken);

    /// <summary>Reads one glyph header while charging its loca lookup to a conversion work counter.</summary>
    internal static OpenTypeGlyphHeader? GetGlyphHeader(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        OpenTypeTableRange glyph = GetGlyphDataRange(bytes, glyphIndex, metrics, workCounter, cancellationToken);
        return ParseGlyphHeader(bytes, glyph);
    }

    /// <summary>Reads one glyph header from a range already validated and charged by its caller.</summary>
    private static OpenTypeGlyphHeader? ParseGlyphHeader(ReadOnlyMemory<byte> bytes, OpenTypeTableRange glyph)
    {
        if (glyph.Length == 0) return null;
        if (glyph.Length < 10) throw InvalidFont("A nonempty glyf record is shorter than its header.");
        ReadOnlySpan<byte> data = bytes.Span;
        short contourCount = ReadInt16(data, glyph.Offset);
        return new OpenTypeGlyphHeader(contourCount, ReadInt16(data, glyph.Offset + 2), ReadInt16(data, glyph.Offset + 4), ReadInt16(data, glyph.Offset + 6), ReadInt16(data, glyph.Offset + 8));
    }

    /// <summary>Reads bounded contour endpoints from a simple glyph and excludes its hint instructions.</summary>
    internal static OpenTypeSimpleGlyphStructure GetSimpleGlyphStructure(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
        => GetSimpleGlyphStructure(bytes, glyphIndex, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Reads simple-glyph structure using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static OpenTypeSimpleGlyphStructure GetSimpleGlyphStructure(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
        => GetSimpleGlyphStructure(bytes, glyphIndex, metrics, null, cancellationToken);

    /// <summary>Reads simple-glyph structure while charging its contour records to a conversion work counter.</summary>
    internal static OpenTypeSimpleGlyphStructure GetSimpleGlyphStructure(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        using OpenTypeSimpleGlyphStructureStorage structure = GetSimpleGlyphStructure(bytes, glyphIndex, metrics, null, workCounter, cancellationToken);
        return structure.Value;
    }

    /// <summary>Reads simple-glyph structure and owns its endpoint array until point decoding completes.</summary>
    private static OpenTypeSimpleGlyphStructureStorage GetSimpleGlyphStructure(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, RenderBudget? budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => GetGlyphStructure(bytes, glyphIndex, metrics, budget, workCounter, cancellationToken, false);

    /// <summary>Reads one glyph header and simple metadata, optionally leaving composite decoding to the point assembler.</summary>
    private static OpenTypeSimpleGlyphStructureStorage GetGlyphStructure(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, RenderBudget? budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken, bool allowComposite)
    {
        OpenTypeTableRange glyph = GetGlyphDataRange(bytes, glyphIndex, metrics, workCounter, cancellationToken);
        if (glyph.Length == 0) return new(new OpenTypeSimpleGlyphStructure([], 0, glyph), null);
        OpenTypeGlyphHeader? header = ParseGlyphHeader(bytes, glyph);
        if (header is not OpenTypeGlyphHeader value) throw new InvalidOperationException("A nonempty glyph did not return a header.");
        if (value.IsComposite)
        {
            if (!allowComposite) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Composite TrueType glyph decoding is not implemented yet.");
            return new(new([], 0, glyph), null, value);
        }
        if (value.ContourCount > MaximumSimpleGlyphContours) throw new SvgException(SvgErrorCode.LimitExceeded, "The simple glyph contour count exceeds the supported limit.");
        if (value.ContourCount == 0 && glyph.Length == 10) return new(new([], 10, glyph), null);
        ReadOnlySpan<byte> data = bytes.Span;
        int endpointBytes = checked(value.ContourCount * 2);
        int instructionLengthOffset = checked(glyph.Offset + 10 + endpointBytes);
        if (instructionLengthOffset > glyph.Offset + glyph.Length - 2) throw InvalidFont("The simple glyf contour endpoints are truncated.");
        ushort instructionLength = ReadUInt16(data, instructionLengthOffset);
        int pointDataOffset = checked(instructionLengthOffset + 2 + instructionLength);
        if (pointDataOffset > glyph.Offset + glyph.Length) throw InvalidFont("The simple glyf instructions are truncated.");
        RenderBudget.BudgetReservation? reservation = value.ContourCount == 0 ? null : budget?.Reserve(ParserCollectionMemory.GetArrayByteCount<ushort>(value.ContourCount), SvgMemoryCategory.Geometry);
        ushort[] endpoints;
        try { endpoints = new ushort[value.ContourCount]; }
        catch { reservation?.Dispose(); throw; }
        GlyphDecoderBudgetProbe.Reserved(GlyphDecoderBudgetPhase.SimpleEndpoints, budget);
        ushort previous = 0;
        try
        {
            for (int index = 0; index < endpoints.Length; index++)
            {
                Consume(workCounter, cancellationToken);
                ushort endpoint = ReadUInt16(data, glyph.Offset + 10 + index * 2);
                if (index != 0 && endpoint <= previous) throw InvalidFont("The simple glyf contour endpoints are not strictly increasing.");
                endpoints[index] = endpoint;
                previous = endpoint;
            }
            return new(new OpenTypeSimpleGlyphStructure(endpoints, pointDataOffset - glyph.Offset, glyph), reservation);
        }
        catch
        {
            reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Decodes bounded simple-glyph flags and delta coordinates into borrowed-font outline points.</summary>
    internal static OpenTypeGlyphPoint[] GetSimpleGlyphPoints(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
        => GetSimpleGlyphPoints(bytes, glyphIndex, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Decodes simple-glyph points using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static OpenTypeGlyphPoint[] GetSimpleGlyphPoints(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
        => GetSimpleGlyphPoints(bytes, glyphIndex, metrics, null, cancellationToken);

    /// <summary>Decodes simple-glyph points while charging flag and coordinate records to a conversion work counter.</summary>
    internal static OpenTypeGlyphPoint[] GetSimpleGlyphPoints(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        OpenTypeSimpleGlyphStructure structure = GetSimpleGlyphStructure(bytes, glyphIndex, metrics, workCounter, cancellationToken);
        return GetSimpleGlyphPoints(bytes, structure, workCounter, cancellationToken);
    }

    /// <summary>Decodes point flags and coordinates from already-validated simple-glyph structure.</summary>
    private static OpenTypeGlyphPoint[] GetSimpleGlyphPoints(ReadOnlyMemory<byte> bytes, OpenTypeSimpleGlyphStructure structure, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (structure.ContourEndpoints.Length == 0) return [];
        int pointCount = checked(structure.ContourEndpoints[^1] + 1);
        if (pointCount > MaximumSimpleGlyphPoints) throw new SvgException(SvgErrorCode.LimitExceeded, "The simple glyph point count exceeds the supported limit.");
        OpenTypeTableRange glyph = structure.GlyphRange;
        ReadOnlySpan<byte> data = bytes.Span;
        int cursor = checked(glyph.Offset + structure.PointDataOffset);
        int end = checked(glyph.Offset + glyph.Length);
        var flags = new byte[pointCount];
        for (int index = 0; index < flags.Length;)
        {
            Consume(workCounter, cancellationToken);
            if (cursor >= end) throw InvalidFont("The simple glyf flags are truncated.");
            byte flag = data[cursor++];
            int repeats = 1;
            if ((flag & 8) != 0)
            {
                if (cursor >= end) throw InvalidFont("The simple glyf flag repetition is truncated.");
                repeats += data[cursor++];
            }
            if (repeats > flags.Length - index) throw InvalidFont("The simple glyf flag repetition exceeds its point count.");
            flags.AsSpan(index, repeats).Fill(flag);
            index += repeats;
        }
        var points = new OpenTypeGlyphPoint[pointCount];
        int x = 0;
        for (int index = 0; index < points.Length; index++)
        {
            Consume(workCounter, cancellationToken);
            x = checked(x + ReadCoordinate(data, ref cursor, end, flags[index], 2, 16, "horizontal"));
            points[index] = new OpenTypeGlyphPoint(x, 0, (flags[index] & 1) != 0);
        }
        int y = 0;
        for (int index = 0; index < points.Length; index++)
        {
            Consume(workCounter, cancellationToken);
            y = checked(y + ReadCoordinate(data, ref cursor, end, flags[index], 4, 32, "vertical"));
            points[index] = points[index] with { Y = y };
        }
        return points;
    }

    /// <summary>Decodes points while retaining their reservation until quadratic lowering has completed.</summary>
    private static OpenTypeGlyphPointStorage GetSimpleGlyphPoints(ReadOnlyMemory<byte> bytes, OpenTypeSimpleGlyphStructure structure, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (structure.ContourEndpoints.Length == 0) return new([], null);
        int pointCount = checked(structure.ContourEndpoints[^1] + 1);
        if (pointCount > MaximumSimpleGlyphPoints) throw new SvgException(SvgErrorCode.LimitExceeded, "The simple glyph point count exceeds the supported limit.");
        OpenTypeTableRange glyph = structure.GlyphRange;
        ReadOnlySpan<byte> data = bytes.Span;
        int cursor = checked(glyph.Offset + structure.PointDataOffset);
        int end = checked(glyph.Offset + glyph.Length);
        using RenderBudget.BudgetReservation flagsReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount(pointCount, sizeof(byte)), SvgMemoryCategory.Geometry);
        byte[] flags = new byte[pointCount];
        GlyphDecoderBudgetProbe.Reserved(GlyphDecoderBudgetPhase.SimpleFlags, budget);
        for (int index = 0; index < flags.Length;)
        {
            Consume(workCounter, cancellationToken);
            if (cursor >= end) throw InvalidFont("The simple glyf flags are truncated.");
            byte flag = data[cursor++];
            int repeats = 1;
            if ((flag & 8) != 0)
            {
                if (cursor >= end) throw InvalidFont("The simple glyf flag repetition is truncated.");
                repeats += data[cursor++];
            }
            if (repeats > flags.Length - index) throw InvalidFont("The simple glyf flag repetition exceeds its point count.");
            flags.AsSpan(index, repeats).Fill(flag);
            index += repeats;
        }
        RenderBudget.BudgetReservation reservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<OpenTypeGlyphPoint>(pointCount), SvgMemoryCategory.Geometry);
        OpenTypeGlyphPoint[] points;
        try { points = new OpenTypeGlyphPoint[pointCount]; }
        catch { reservation.Dispose(); throw; }
        GlyphDecoderBudgetProbe.Reserved(GlyphDecoderBudgetPhase.SimplePoints, budget);
        try
        {
            int x = 0;
            for (int index = 0; index < points.Length; index++)
            {
                Consume(workCounter, cancellationToken);
                x = checked(x + ReadCoordinate(data, ref cursor, end, flags[index], 2, 16, "horizontal"));
                points[index] = new OpenTypeGlyphPoint(x, 0, (flags[index] & 1) != 0);
            }
            int y = 0;
            for (int index = 0; index < points.Length; index++)
            {
                Consume(workCounter, cancellationToken);
                y = checked(y + ReadCoordinate(data, ref cursor, end, flags[index], 4, 32, "vertical"));
                points[index] = points[index] with { Y = y };
            }
            return new(points, reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    /// <summary>Builds closed quadratic segments from decoded simple-glyph contours, charging contour and point records like the budgeted decoder.</summary>
    internal static OpenTypeQuadraticSegment[] BuildQuadraticContours(ReadOnlySpan<OpenTypeGlyphPoint> points, ReadOnlySpan<ushort> endpoints, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        using var segments = new OpenTypeSegmentAccumulator(null, GlyphDecoderBudgetPhase.None, GlyphDecoderBudgetPhase.None, GlyphDecoderBudgetPhase.None, GlyphDecoderBudgetPhase.None, workCounter, cancellationToken);
        BuildQuadraticContours(points, endpoints, segments, false, TrueTypeImpliedPointRounding.None, workCounter, cancellationToken);
        return segments.ToArray();
    }

    private static void BuildQuadraticContours(ReadOnlySpan<OpenTypeGlyphPoint> points, ReadOnlySpan<ushort> endpoints, OpenTypeSegmentAccumulator segments, bool retainContourBoundaries, TrueTypeImpliedPointRounding impliedPointRounding, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (endpoints.Length == 0) return;
        if (points.Length == 0 || endpoints[^1] != points.Length - 1) throw new ArgumentException("Contour endpoints must cover the supplied points.", nameof(endpoints));
        int first = 0;
        foreach (ushort lastValue in endpoints)
        {
            Consume(workCounter, cancellationToken);
            int firstSegment = segments.Count;
            int last = lastValue;
            if (last < first) throw new ArgumentException("Contour endpoints must be strictly increasing.", nameof(endpoints));
            OpenTypeGlyphPoint initial = points[first];
            OpenTypeGlyphPoint final = points[last];
            OpenTypeOutlinePoint start = initial.IsOnCurve ? ToOutlinePoint(initial) : final.IsOnCurve ? ToOutlinePoint(final) : ContourMidpoint(ToOutlinePoint(final), ToOutlinePoint(initial), impliedPointRounding);
            OpenTypeOutlinePoint current = start;
            OpenTypeOutlinePoint? control = null;
            for (int index = first; index <= last; index++)
            {
                Consume(workCounter, cancellationToken);
                OpenTypeGlyphPoint point = points[index];
                if (point.IsOnCurve)
                {
                    OpenTypeOutlinePoint destination = ToOutlinePoint(point);
                    if (control is OpenTypeOutlinePoint pending) segments.Add(new OpenTypeQuadraticSegment(current, pending, destination));
                    else if (current != destination) segments.Add(new OpenTypeQuadraticSegment(current, destination, destination));
                    current = destination;
                    control = null;
                }
                else if (control is OpenTypeOutlinePoint pending)
                {
                    OpenTypeOutlinePoint implied = ContourMidpoint(pending, ToOutlinePoint(point), impliedPointRounding);
                    segments.Add(new OpenTypeQuadraticSegment(current, pending, implied));
                    current = implied;
                    control = ToOutlinePoint(point);
                }
                else control = ToOutlinePoint(point);
            }
            if (control is OpenTypeOutlinePoint closingControl) segments.Add(new OpenTypeQuadraticSegment(current, closingControl, start));
            else if (current != start) segments.Add(new OpenTypeQuadraticSegment(current, start, start));
            if (retainContourBoundaries && segments.Count != firstSegment) segments.CompleteContour();
            first = last + 1;
        }

        static OpenTypeOutlinePoint ContourMidpoint(OpenTypeOutlinePoint first, OpenTypeOutlinePoint second, TrueTypeImpliedPointRounding rounding)
        {
            OpenTypeOutlinePoint result = Midpoint(first, second);
            return rounding switch
            {
                TrueTypeImpliedPointRounding.Down => new(Math.Floor(result.X), Math.Floor(result.Y)),
                TrueTypeImpliedPointRounding.TowardZero => new(Math.Truncate(result.X), Math.Truncate(result.Y)),
                _ => result
            };
        }
    }

    /// <summary>Decodes a bounded simple glyph into closed quadratic outline segments.</summary>
    internal static OpenTypeQuadraticSegment[] GetSimpleGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
        => GetSimpleGlyphContours(bytes, glyphIndex, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Decodes simple-glyph contours using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static OpenTypeQuadraticSegment[] GetSimpleGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
        => GetSimpleGlyphContours(bytes, glyphIndex, metrics, null, cancellationToken);

    /// <summary>Decodes simple-glyph contours while charging decoded records to a conversion work counter.</summary>
    internal static OpenTypeQuadraticSegment[] GetSimpleGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        OpenTypeSimpleGlyphStructure structure = GetSimpleGlyphStructure(bytes, glyphIndex, metrics, workCounter, cancellationToken);
        if (structure.ContourEndpoints.Length == 0) return [];
        OpenTypeGlyphPoint[] points = GetSimpleGlyphPoints(bytes, structure, workCounter, cancellationToken);
        return BuildQuadraticContours(points, structure.ContourEndpoints, workCounter, cancellationToken);
    }

    /// <summary>Decodes simple-glyph arrays and retains every temporary reservation through exact decoded-segment materialization.</summary>
    private static OpenTypeGlyphContourStorage GetBudgetedSimpleGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        using OpenTypeSimpleGlyphStructureStorage structure = GetSimpleGlyphStructure(bytes, glyphIndex, metrics, budget, workCounter, cancellationToken);
        if (structure.Value.ContourEndpoints.Length == 0) return new([], [], null, null);
        using OpenTypeGlyphPointStorage points = GetSimpleGlyphPoints(bytes, structure.Value, budget, workCounter, cancellationToken);
        using var segments = new OpenTypeSegmentAccumulator(budget, GlyphDecoderBudgetPhase.SimpleSegmentCapacity, GlyphDecoderBudgetPhase.SimpleSegmentExact, GlyphDecoderBudgetPhase.SimpleContourBoundaryCapacity, GlyphDecoderBudgetPhase.SimpleContourBoundaryExact, workCounter, cancellationToken);
        BuildQuadraticContours(points.Points, structure.Value.ContourEndpoints, segments, true, TrueTypeImpliedPointRounding.None, workCounter, cancellationToken);
        return segments.Detach();
    }

    /// <summary>Reads bounded composite-glyph component metadata without recursively expanding outlines.</summary>
    internal static OpenTypeCompositeGlyphComponent[] GetCompositeGlyphComponents(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
        => GetCompositeGlyphComponents(bytes, glyphIndex, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Reads composite-glyph components using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static OpenTypeCompositeGlyphComponent[] GetCompositeGlyphComponents(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
        => GetCompositeGlyphComponents(bytes, glyphIndex, metrics, null, cancellationToken);

    /// <summary>Reads composite-glyph components while charging each component record to a conversion work counter.</summary>
    internal static OpenTypeCompositeGlyphComponent[] GetCompositeGlyphComponents(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        using var components = new OpenTypeCompositeComponentAccumulator(null, workCounter, cancellationToken);
        ReadCompositeGlyphComponents(bytes, glyphIndex, metrics, components, workCounter, cancellationToken);
        return components.ToArray();
    }

    /// <summary>Reads composite components into a budgeted owner until recursive expansion has consumed them.</summary>
    private static OpenTypeCompositeGlyphComponentStorage GetBudgetedCompositeGlyphComponents(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        using var components = new OpenTypeCompositeComponentAccumulator(budget, workCounter, cancellationToken);
        OpenTypeTableRange instructions = ReadCompositeGlyphComponents(bytes, glyphIndex, metrics, components, workCounter, cancellationToken);
        return components.Detach(instructions);
    }

    /// <summary>Validates and appends the component records of one composite glyph.</summary>
    private static OpenTypeTableRange ReadCompositeGlyphComponents(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, OpenTypeCompositeComponentAccumulator components, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        OpenTypeTableRange glyph = GetGlyphDataRange(bytes, glyphIndex, metrics, workCounter, cancellationToken);
        OpenTypeGlyphHeader? header = ParseGlyphHeader(bytes, glyph);
        if (header is not OpenTypeGlyphHeader value || !value.IsComposite) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The requested glyph is not a composite TrueType glyph.");
        ReadOnlySpan<byte> data = bytes.Span;
        int cursor = glyph.Offset + 10;
        int end = glyph.Offset + glyph.Length;
        bool hasInstructions = false;
        ushort flags;
        do
        {
            Consume(workCounter, cancellationToken);
            if (components.Count == MaximumCompositeGlyphComponents) throw new SvgException(SvgErrorCode.LimitExceeded, "The composite glyph component count exceeds the supported limit.");
            if (cursor > end - 4) throw InvalidFont("The composite glyf component is truncated.");
            flags = ReadUInt16(data, cursor);
            ushort componentGlyph = ReadUInt16(data, cursor + 2);
            cursor += 4;
            if ((flags & ~SupportedCompositeFlags) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The composite glyf component uses flags outside the admitted placement and transform subset.");
            if ((flags & (2048 | 4096)) == (2048 | 4096)) throw InvalidFont("The composite glyph requests both scaled and unscaled offsets.");
            ushort scaleFlags = (ushort)(flags & (8 | 64 | 128));
            if (scaleFlags is not 0 and not 8 and not 64 and not 128) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The composite glyf component combines incompatible scale encodings.");
            bool words = (flags & 1) != 0;
            bool xyValues = (flags & 2) != 0;
            hasInstructions |= (flags & 256) != 0;
            int argumentBytes = words ? 4 : 2;
            if (cursor > end - argumentBytes) throw InvalidFont("The composite glyf arguments are truncated.");
            if (!xyValues) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Point-matched composite TrueType glyph components are not implemented yet.");
            int horizontalOffset = words ? ReadInt16(data, cursor) : unchecked((sbyte)data[cursor]);
            int verticalOffset = words ? ReadInt16(data, cursor + (words ? 2 : 1)) : unchecked((sbyte)data[cursor + 1]);
            cursor += argumentBytes;
            OpenTypeAffineTransform transform = ReadCompositeTransform(data, ref cursor, end, flags);
            components.Add(new OpenTypeCompositeGlyphComponent(componentGlyph, horizontalOffset, verticalOffset, transform, flags));
        } while ((flags & 32) != 0);
        if (hasInstructions)
        {
            if (cursor > end - 2) throw InvalidFont("The composite glyf instruction length is truncated.");
            ushort instructionLength = ReadUInt16(data, cursor);
            cursor += 2;
            if (cursor > end - instructionLength) throw InvalidFont("The composite glyf instructions are truncated.");
            return new(cursor, instructionLength);
        }
        return default;
    }

    /// <summary>Expands bounded simple and composite glyphs into transformed quadratic segments.</summary>
    internal static OpenTypeQuadraticSegment[] GetGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
        => GetGlyphContours(bytes, glyphIndex, ParseMetrics(bytes, cancellationToken), cancellationToken);

    /// <summary>Expands glyph contours using metrics already validated for these unchanged caller-owned bytes.</summary>
    internal static OpenTypeQuadraticSegment[] GetGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenTypeGlyphHeader? header = GetGlyphHeader(bytes, glyphIndex, metrics, cancellationToken);
        if (header is null || (!header.Value.IsComposite && header.Value.ContourCount == 0)) return [];
        using var active = new OpenTypeGlyphRecursionTracker(MaximumCompositeGlyphDepth + 1, null, null, cancellationToken);
        using var contours = new OpenTypeSegmentAccumulator(null, GlyphDecoderBudgetPhase.None, GlyphDecoderBudgetPhase.None, GlyphDecoderBudgetPhase.None, GlyphDecoderBudgetPhase.None, null, cancellationToken);
        ExpandGlyph(bytes, glyphIndex, metrics, active, 0, OpenTypeOutlineTransform.Identity, contours, header, null, cancellationToken);
        return contours.ToArray();
    }

    /// <summary>Expands one glyph and owns the budget reservation for the decoded segment array until its consumer disposes the storage.</summary>
    internal static OpenTypeGlyphContourStorage GetGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();
        OpenTypeGlyphHeader? header = GetGlyphHeader(bytes, glyphIndex, metrics, workCounter, cancellationToken);
        if (header is null || (!header.Value.IsComposite && header.Value.ContourCount == 0)) return new([], [], null, null);
        using var active = new OpenTypeGlyphRecursionTracker(MaximumCompositeGlyphDepth + 1, budget, workCounter, cancellationToken);
        using var contours = new OpenTypeSegmentAccumulator(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, workCounter, cancellationToken);
        ExpandGlyph(bytes, glyphIndex, metrics, active, 0, OpenTypeOutlineTransform.Identity, contours, header, workCounter, cancellationToken);
        return contours.Detach();
    }

    private static void ExpandGlyph(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, OpenTypeGlyphRecursionTracker active, int depth, OpenTypeOutlineTransform transform, OpenTypeSegmentAccumulator contours, OpenTypeGlyphHeader? knownHeader, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        GlyphExpansionProbe.Started();
        Consume(workCounter, cancellationToken);
        if (glyphIndex >= metrics.GlyphCount) throw InvalidFont("A composite glyf component references a glyph outside maxp.");
        if (depth > MaximumCompositeGlyphDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "The composite glyph nesting depth exceeds the supported limit.");
        if (!active.TryEnter(glyphIndex)) throw InvalidFont("The composite glyph graph contains a cycle.");
        try
        {
            OpenTypeGlyphHeader? header = knownHeader ?? GetGlyphHeader(bytes, glyphIndex, metrics, workCounter, cancellationToken);
            if (header is null) return;
            if (!header.Value.IsComposite)
            {
                if (contours.Budget is RenderBudget budget)
                {
                    using OpenTypeGlyphContourStorage decoded = GetBudgetedSimpleGlyphContours(bytes, glyphIndex, metrics, budget, workCounter, cancellationToken);
                    AppendDecodedContours(decoded, transform, contours, workCounter, cancellationToken);
                }
                else AppendDecodedSegments(GetSimpleGlyphContours(bytes, glyphIndex, metrics, workCounter, cancellationToken), transform, contours, workCounter, cancellationToken);
                return;
            }
            if (contours.Budget is RenderBudget componentBudget)
            {
                using OpenTypeCompositeGlyphComponentStorage components = GetBudgetedCompositeGlyphComponents(bytes, glyphIndex, metrics, componentBudget, workCounter, cancellationToken);
                foreach (OpenTypeCompositeGlyphComponent component in components.Components)
                {
                    ExpandGlyph(bytes, component.GlyphIndex, metrics, active, depth + 1, transform.Compose(component), contours, null, workCounter, cancellationToken);
                }
            }
            else foreach (OpenTypeCompositeGlyphComponent component in GetCompositeGlyphComponents(bytes, glyphIndex, metrics, workCounter, cancellationToken)) ExpandGlyph(bytes, component.GlyphIndex, metrics, active, depth + 1, transform.Compose(component), contours, null, workCounter, cancellationToken);
        }
        finally { active.Exit(glyphIndex); }

        static void AppendDecodedSegments(ReadOnlySpan<OpenTypeQuadraticSegment> segments, OpenTypeOutlineTransform transform, OpenTypeSegmentAccumulator contours, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        {
            foreach (OpenTypeQuadraticSegment segment in segments)
            {
                Consume(workCounter, cancellationToken);
                if (contours.Count == MaximumExpandedGlyphSegments) throw new SvgException(SvgErrorCode.LimitExceeded, "The expanded glyph segment count exceeds the supported limit.");
                contours.Add(Transform(segment, transform));
            }
        }

        static void AppendDecodedContours(OpenTypeGlyphContourStorage decoded, OpenTypeOutlineTransform transform, OpenTypeSegmentAccumulator contours, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        {
            int first = 0;
            foreach (int end in decoded.ContourEnds)
            {
                if (end <= first || end > decoded.Segments.Length) throw new InvalidOperationException("Decoded glyph contour boundaries are invalid.");
                AppendDecodedSegments(decoded.Segments.AsSpan(first, end - first), transform, contours, workCounter, cancellationToken);
                contours.CompleteContour();
                first = end;
            }
            if (first != decoded.Segments.Length) throw new InvalidOperationException("Decoded glyph contour boundaries do not cover their segments.");
        }
    }

    private static OpenTypeQuadraticSegment Transform(OpenTypeQuadraticSegment segment, OpenTypeOutlineTransform transform)
        => new(transform.Apply(segment.Start), transform.Apply(segment.Control), transform.Apply(segment.End));

    private static OpenTypeAffineTransform ReadCompositeTransform(ReadOnlySpan<byte> data, ref int cursor, int end, ushort flags)
    {
        int values = (flags & 128) != 0 ? 4 : (flags & 64) != 0 ? 2 : (flags & 8) != 0 ? 1 : 0;
        if (cursor > end - values * 2) throw InvalidFont("The composite glyf transform is truncated.");
        double first = values == 0 ? 1 : ReadF2Dot14(data, cursor);
        double second = values == 4 ? ReadF2Dot14(data, cursor + 2) : 0;
        double third = values == 4 ? ReadF2Dot14(data, cursor + 4) : 0;
        double fourth = values == 4 ? ReadF2Dot14(data, cursor + 6) : values == 2 ? ReadF2Dot14(data, cursor + 2) : first;
        cursor += values * 2;
        // OpenType stores (xScale, scale01, scale10, yScale), whereas the
        // renderer stores row coefficients (XX, XY, YX, YY).
        return new OpenTypeAffineTransform(first, third, second, fourth);
    }

    private static double ReadF2Dot14(ReadOnlySpan<byte> data, int offset) => ReadInt16(data, offset) / 16384d;

    private static OpenTypeOutlinePoint ToOutlinePoint(OpenTypeGlyphPoint point) => new(point.X, point.Y);

    private static OpenTypeOutlinePoint Midpoint(OpenTypeOutlinePoint left, OpenTypeOutlinePoint right) => new((left.X + right.X) / 2d, (left.Y + right.Y) / 2d);

    private static int ReadCoordinate(ReadOnlySpan<byte> data, ref int cursor, int end, byte flag, byte shortMask, byte sameMask, string axis)
    {
        if ((flag & shortMask) != 0)
        {
            if (cursor >= end) throw InvalidFont("The simple glyf " + axis + " coordinates are truncated.");
            int delta = data[cursor++];
            return (flag & sameMask) != 0 ? delta : -delta;
        }
        if ((flag & sameMask) != 0) return 0;
        if (cursor > end - 2) throw InvalidFont("The simple glyf " + axis + " coordinates are truncated.");
        int value = ReadInt16(data, cursor);
        cursor += 2;
        return value;
    }

    private static int ReadGlyphLocation(ReadOnlySpan<byte> data, OpenTypeTableRange loca, int glyphIndex, short indexToLocFormat)
    {
        int offset = checked(loca.Offset + glyphIndex * (indexToLocFormat == 0 ? 2 : 4));
        uint location = indexToLocFormat == 0 ? checked((uint)ReadUInt16(data, offset) * 2) : ReadUInt32(data, offset);
        if (location > int.MaxValue) throw InvalidFont("The loca table contains an unsupported glyph offset.");
        return (int)location;
    }

    /// <summary>Validates optional name-table records and their borrowed string-storage bounds without selecting a face from embedded names.</summary>
    private static void ValidateNameTable(ReadOnlySpan<byte> data, OpenTypeTableRange table, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        RequireTableLength(table, 6, "name");
        ushort version = ReadUInt16(data, table.Offset);
        if (version is not 0 and not 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-zero and version-one name tables are supported.");
        int nameCount = ReadUInt16(data, table.Offset + 2);
        int recordOffset = table.Offset + 6;
        int recordsEnd = checked(recordOffset + nameCount * 12);
        RequireTableRange(table, recordOffset, checked(nameCount * 12), "name records");
        int languageTagOffset = recordsEnd;
        int languageTagCount = 0;
        if (version == 1)
        {
            RequireTableRange(table, languageTagOffset, 2, "name language-tag count");
            languageTagCount = ReadUInt16(data, languageTagOffset);
            languageTagOffset += 2;
            RequireTableRange(table, languageTagOffset, checked(languageTagCount * 4), "name language-tag records");
        }
        int metadataEnd = checked(languageTagOffset + languageTagCount * 4);
        int storageOffset = ReadUInt16(data, table.Offset + 4);
        int storageStart = checked(table.Offset + storageOffset);
        int tableEnd = checked(table.Offset + table.Length);
        if (storageStart < metadataEnd || storageStart > tableEnd) throw InvalidFont("The name string-storage offset is invalid.");

        ulong previousKey = 0;
        for (int index = 0; index < nameCount; index++)
        {
            Consume(workCounter, cancellationToken);
            int record = recordOffset + index * 12;
            ushort platform = ReadUInt16(data, record);
            ushort encoding = ReadUInt16(data, record + 2);
            ushort language = ReadUInt16(data, record + 4);
            ushort name = ReadUInt16(data, record + 6);
            if (version == 1 && language >= 0x8000 && language - 0x8000 >= languageTagCount) throw InvalidFont("A name record references a missing language-tag record.");
            ulong key = ((ulong)platform << 48) | ((ulong)encoding << 32) | ((ulong)language << 16) | name;
            if (index != 0 && key < previousKey) throw InvalidFont("The name records are not sorted.");
            previousKey = key;
            ValidateNameStorageRange(storageStart, tableEnd, ReadUInt16(data, record + 8), ReadUInt16(data, record + 10), "name string");
        }
        for (int index = 0; index < languageTagCount; index++)
        {
            Consume(workCounter, cancellationToken);
            int record = languageTagOffset + index * 4;
            int length = ReadUInt16(data, record);
            if ((length & 1) != 0) throw InvalidFont("A name language-tag string has an invalid UTF-16BE length.");
            ValidateNameStorageRange(storageStart, tableEnd, length, ReadUInt16(data, record + 2), "name language-tag string");
        }
    }

    private static void ValidateNameStorageRange(int storageStart, int tableEnd, int length, int relativeOffset, string name)
    {
        int offset = checked(storageStart + relativeOffset);
        if (offset < storageStart || offset > tableEnd - length) throw InvalidFont("The " + name + " range is outside name string storage.");
    }

    /// <summary>Validates the version-specific minimum length of optional OS/2 metadata without applying its style metrics.</summary>
    private static void ValidateOs2Table(ReadOnlySpan<byte> data, OpenTypeTableRange table, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        Consume(workCounter, cancellationToken);
        RequireTableLength(table, 2, "OS/2");
        ushort version = ReadUInt16(data, table.Offset);
        int minimumLength = version switch
        {
            0 => 68,
            1 => 86,
            2 or 3 or 4 => 96,
            5 => 100,
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only OS/2 versions zero through five are supported.")
        };
        RequireTableLength(table, minimumLength, "OS/2");
    }

    private static int GetTableOffset(OpenTypeTableRange table, int parentOffset, int relativeOffset, int minimumLength, string name)
    {
        if (relativeOffset == 0) throw InvalidFont("The " + name + " offset is missing.");
        int offset = checked(parentOffset + relativeOffset);
        RequireTableRange(table, offset, minimumLength, name);
        return offset;
    }

    private static void RequireTableRange(OpenTypeTableRange table, int offset, int length, string name)
    {
        if (length < 0 || offset < table.Offset || offset > table.Offset + table.Length - length) throw InvalidFont("The " + name + " range is outside its containing table.");
    }

    /// <summary>Validates one horizontal legacy format-0 kerning subtable without retaining or allocating pair storage.</summary>
    private static OpenTypeKernTable ParseKernTable(ReadOnlySpan<byte> data, OpenTypeTableRange table, ushort glyphCount, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        RequireTableLength(table, 4, "kern");
        if (ReadUInt16(data, table.Offset) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-zero legacy kern tables are supported.");
        if (ReadUInt16(data, table.Offset + 2) != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only one legacy kern subtable is supported.");
        RequireTableLength(table, 18, "kern");
        int subtableOffset = table.Offset + 4;
        if (ReadUInt16(data, subtableOffset) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-zero legacy kern subtables are supported.");
        int subtableLength = ReadUInt16(data, subtableOffset + 2);
        if (subtableLength < 14 || subtableLength != table.Length - 4) throw InvalidFont("The legacy kern subtable length is invalid.");
        if (ReadUInt16(data, subtableOffset + 4) != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only horizontal format-zero legacy kern subtables are supported.");
        int pairCount = ReadUInt16(data, subtableOffset + 6);
        int expectedLength = checked(14 + pairCount * 6);
        if (subtableLength != expectedLength) throw InvalidFont("The legacy kern pair records are truncated.");
        ValidateKernSearchFields(data, subtableOffset, pairCount);
        int pairOffset = subtableOffset + 14;
        ushort previousLeft = 0;
        ushort previousRight = 0;
        for (int index = 0; index < pairCount; index++)
        {
            Consume(workCounter, cancellationToken);
            int offset = pairOffset + index * 6;
            ushort left = ReadUInt16(data, offset);
            ushort right = ReadUInt16(data, offset + 2);
            if (left >= glyphCount || right >= glyphCount) throw InvalidFont("A legacy kern pair references a glyph outside maxp.");
            if (index != 0 && (left < previousLeft || left == previousLeft && right <= previousRight)) throw InvalidFont("The legacy kern pairs are not strictly ordered.");
            previousLeft = left;
            previousRight = right;
        }
        return new OpenTypeKernTable(table, pairOffset, pairCount);
    }

    private static void ValidateKernSearchFields(ReadOnlySpan<byte> data, int subtableOffset, int pairCount)
    {
        int searchRange = ReadUInt16(data, subtableOffset + 8);
        int entrySelector = ReadUInt16(data, subtableOffset + 10);
        int rangeShift = ReadUInt16(data, subtableOffset + 12);
        int largestPowerOfTwo = 1;
        int expectedEntrySelector = 0;
        while (largestPowerOfTwo * 2 <= pairCount)
        {
            largestPowerOfTwo *= 2;
            expectedEntrySelector++;
        }
        int expectedSearchRange = pairCount == 0 ? 0 : largestPowerOfTwo * 6;
        int expectedRangeShift = pairCount * 6 - expectedSearchRange;
        if (searchRange != expectedSearchRange || entrySelector != expectedEntrySelector || rangeShift != expectedRangeShift) throw InvalidFont("The legacy kern search fields are invalid.");
    }

    private static ushort ReadGlyphFromRange(ReadOnlySpan<byte> data, OpenTypeTableRange table, int length, int rangeOffsetLocation, ushort rangeOffset, uint relativeGlyphIndex)
    {
        int glyphOffset = checked(rangeOffsetLocation + rangeOffset + checked((int)relativeGlyphIndex * 2));
        if (glyphOffset < table.Offset || glyphOffset > table.Offset + length - 2) throw InvalidFont("The cmap glyph-index array is truncated.");
        return ReadUInt16(data, glyphOffset);
    }

    private static ushort ApplyDelta(ushort glyph, short delta) => glyph == 0 ? (ushort)0 : unchecked((ushort)(glyph + delta));

    private static void Consume(ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (workCounter is null) cancellationToken.ThrowIfCancellationRequested();
        else workCounter.Consume(cancellationToken);
    }

    private static OpenTypeTableRange FindTable(ReadOnlySpan<byte> data, uint wantedTag, string name)
        => FindTable(data, wantedTag, name, null, CancellationToken.None);

    private static OpenTypeTableRange FindTable(ReadOnlySpan<byte> data, uint wantedTag, string name, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        int count = ReadUInt16(data, 4);
        for (int index = 0; index < count; index++)
        {
            Consume(workCounter, cancellationToken);
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
internal readonly record struct OpenTypeFontMetrics(ushort UnitsPerEm, ushort GlyphCount, ushort NumberOfHMetrics, short IndexToLocFormat, OpenTypeTableRange Hmtx, OpenTypeTableRange? Cmap, OpenTypeKernTable? Kern, OpenTypeGposPairTable? Gpos, OpenTypeTableRange? Gsub, OpenTypeTableRange? Loca, OpenTypeTableRange? Glyf, bool ForceIntegerHintingSize, OpenTypeFontLineMetrics LineMetrics);

/// <summary>Retains bounded alphabetic line metrics in font units without owning additional font storage.</summary>
internal readonly record struct OpenTypeFontLineMetrics(int Ascender, int Descender, int WindowsAscender, int WindowsDescender)
{
    internal int XHeight { get; init; }
}

internal readonly record struct OpenTypeTableRange(int Offset, int Length);

/// <summary>Identifies validated borrowed pair records from one admitted legacy kern table.</summary>
internal readonly record struct OpenTypeKernTable(OpenTypeTableRange Range, int PairOffset, int PairCount);

/// <summary>Identifies the validated borrowed records for one admitted GPOS PairPos format-1 kern lookup.</summary>
internal readonly record struct OpenTypeGposPairTable(OpenTypeTableRange Range, int FeatureOffset, int LookupListOffset, int PairPosOffset, OpenTypeTableRange? Gdef, int GlyphClassOffset);

/// <summary>Represents the validated fixed header of one nonempty TrueType glyf record.</summary>
internal readonly record struct OpenTypeGlyphHeader(short ContourCount, short XMinimum, short YMinimum, short XMaximum, short YMaximum)
{
    internal bool IsComposite => ContourCount < 0;
}

/// <summary>Represents bounded simple-glyph contour metadata and its validated range before flag and coordinate decoding.</summary>
internal readonly record struct OpenTypeSimpleGlyphStructure(ushort[] ContourEndpoints, int PointDataOffset, OpenTypeTableRange GlyphRange);

/// <summary>Represents one decoded simple-glyph point in font units.</summary>
internal readonly record struct OpenTypeGlyphPoint(int X, int Y, bool IsOnCurve);

/// <summary>Represents one floating-point point in a decoded TrueType outline.</summary>
internal readonly record struct OpenTypeOutlinePoint(double X, double Y);

/// <summary>Represents one quadratic segment; a line has its control equal to its end.</summary>
internal readonly record struct OpenTypeQuadraticSegment(OpenTypeOutlinePoint Start, OpenTypeOutlinePoint Control, OpenTypeOutlinePoint End);

/// <summary>Owns simple-glyph endpoint storage until point decoding no longer needs it.</summary>
internal sealed class OpenTypeSimpleGlyphStructureStorage : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    internal OpenTypeSimpleGlyphStructureStorage(OpenTypeSimpleGlyphStructure value, RenderBudget.BudgetReservation? reservation)
        : this(value, reservation, null) { }

    internal OpenTypeSimpleGlyphStructureStorage(OpenTypeSimpleGlyphStructure value, RenderBudget.BudgetReservation? reservation, OpenTypeGlyphHeader? compositeHeader)
    {
        Value = value;
        _reservation = reservation;
        CompositeHeader = compositeHeader;
    }

    internal OpenTypeSimpleGlyphStructure Value { get; }

    internal OpenTypeGlyphHeader? CompositeHeader { get; }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns simple-glyph point storage until quadratic lowering no longer needs it.</summary>
internal sealed class OpenTypeGlyphPointStorage : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    internal OpenTypeGlyphPointStorage(OpenTypeGlyphPoint[] points, RenderBudget.BudgetReservation? reservation)
    {
        Points = points;
        _reservation = reservation;
    }

    internal OpenTypeGlyphPoint[] Points { get; }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns decoded glyph segments, structural contour boundaries and their temporary geometry reservations until SVG-coordinate transformation completes.</summary>
internal sealed class OpenTypeGlyphContourStorage : IDisposable
{
    private RenderBudget.BudgetReservation? _segmentReservation;
    private RenderBudget.BudgetReservation? _contourReservation;

    internal OpenTypeGlyphContourStorage(OpenTypeQuadraticSegment[] segments, int[] contourEnds, RenderBudget.BudgetReservation? segmentReservation, RenderBudget.BudgetReservation? contourReservation)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(contourEnds);
        int previous = 0;
        foreach (int end in contourEnds)
        {
            if (end <= previous || end > segments.Length) throw new ArgumentException("Contour boundaries must be strictly increasing segment ends.", nameof(contourEnds));
            previous = end;
        }
        if ((segments.Length == 0) != (contourEnds.Length == 0) || previous != segments.Length) throw new ArgumentException("Contour boundaries must cover every decoded segment.", nameof(contourEnds));
        Segments = segments;
        ContourEnds = contourEnds;
        _segmentReservation = segmentReservation;
        _contourReservation = contourReservation;
    }

    internal OpenTypeQuadraticSegment[] Segments { get; }

    /// <summary>Gets validated exclusive segment ends for each nonempty glyph contour in decoder order.</summary>
    internal int[] ContourEnds { get; }

    /// <summary>Gets or sets the fitted glyph's smart dropout policy before its exclusive owner starts rasterization.</summary>
    internal bool SmartDropouts { get; set; }

    public void Dispose()
    {
        _segmentReservation?.Dispose();
        _segmentReservation = null;
        _contourReservation?.Dispose();
        _contourReservation = null;
    }
}

/// <summary>Owns exact composite-component records until recursive expansion has consumed them.</summary>
internal sealed class OpenTypeCompositeGlyphComponentStorage : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    internal OpenTypeCompositeGlyphComponentStorage(OpenTypeCompositeGlyphComponent[] components, RenderBudget.BudgetReservation? reservation, OpenTypeTableRange instructions)
    {
        Components = components;
        _reservation = reservation;
        Instructions = instructions;
    }

    internal OpenTypeCompositeGlyphComponent[] Components { get; }

    internal OpenTypeTableRange Instructions { get; }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Builds decoded segments and structural contour boundaries with reservation-backed capacity growth when glyph expansion belongs to a conversion budget.</summary>
internal sealed class OpenTypeSegmentAccumulator : IDisposable
{
    private readonly RenderBudget? _budget;
    private readonly GlyphDecoderBudgetPhase _capacityPhase;
    private readonly GlyphDecoderBudgetPhase _exactPhase;
    private readonly GlyphDecoderBudgetPhase _contourCapacityPhase;
    private readonly GlyphDecoderBudgetPhase _contourExactPhase;
    private readonly ParserWorkCounter? _workCounter;
    private readonly CancellationToken _cancellationToken;
    private OpenTypeQuadraticSegment[] _segments = [];
    private int[] _contourEnds = [];
    private RenderBudget.BudgetReservation? _reservation;
    private RenderBudget.BudgetReservation? _contourReservation;
    private int _count;
    private int _contourCount;

    internal OpenTypeSegmentAccumulator(RenderBudget? budget, GlyphDecoderBudgetPhase capacityPhase, GlyphDecoderBudgetPhase exactPhase, GlyphDecoderBudgetPhase contourCapacityPhase, GlyphDecoderBudgetPhase contourExactPhase, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        _budget = budget;
        _capacityPhase = capacityPhase;
        _exactPhase = exactPhase;
        _contourCapacityPhase = contourCapacityPhase;
        _contourExactPhase = contourExactPhase;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
    }

    internal int Count => _count;

    /// <summary>Provides the conversion budget for short-lived decoder buffers that feed this accumulator.</summary>
    internal RenderBudget? Budget => _budget;

    internal void Add(OpenTypeQuadraticSegment segment)
    {
        if (_count == _segments.Length) Grow();
        _segments[_count++] = segment;
    }

    /// <summary>Records the exclusive end of one nonempty structural contour after all of its segments have been appended.</summary>
    internal void CompleteContour()
    {
        if (_count == 0 || (_contourCount != 0 && _contourEnds[_contourCount - 1] == _count)) return;
        if (_contourCount == _contourEnds.Length) GrowContourEnds();
        Consume();
        _contourEnds[_contourCount++] = _count;
    }

    internal OpenTypeQuadraticSegment[] ToArray()
    {
        if (_count == 0) return [];
        var copy = new OpenTypeQuadraticSegment[_count];
        CopySegments(copy);
        return copy;
    }

    /// <summary>Materializes exact decoded segment and contour-boundary storage while both grown and exact arrays remain reserved.</summary>
    internal OpenTypeGlyphContourStorage Detach()
    {
        if (_count == 0)
        {
            Dispose();
            return new([], [], null, null);
        }
        if (_contourCount == 0 || _contourEnds[_contourCount - 1] != _count) throw new InvalidOperationException("Every decoded segment must belong to one structural contour.");
        if (_budget is null) return new(ToArray(), ToContourEndArray(), null, null);
        RenderBudget.BudgetReservation segmentReservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<OpenTypeQuadraticSegment>(_count), SvgMemoryCategory.Geometry);
        OpenTypeQuadraticSegment[] exactSegments;
        try
        {
            exactSegments = new OpenTypeQuadraticSegment[_count];
            GlyphDecoderBudgetProbe.Reserved(_exactPhase, _budget);
            CopySegments(exactSegments);
        }
        catch
        {
            segmentReservation.Dispose();
            throw;
        }
        RenderBudget.BudgetReservation? contourReservation = null;
        try
        {
            contourReservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<int>(_contourCount), SvgMemoryCategory.Geometry);
            var exactContourEnds = new int[_contourCount];
            GlyphDecoderBudgetProbe.Reserved(_contourExactPhase, _budget);
            CopyContourEnds(exactContourEnds);
            Dispose();
            return new(exactSegments, exactContourEnds, segmentReservation, contourReservation);
        }
        catch
        {
            contourReservation?.Dispose();
            segmentReservation.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
        _contourReservation?.Dispose();
        _contourReservation = null;
        _segments = [];
        _contourEnds = [];
        _count = 0;
        _contourCount = 0;
    }

    private void Grow()
    {
        int capacity = _segments.Length == 0 ? 4 : checked(_segments.Length * 2);
        RenderBudget.BudgetReservation? replacementReservation = _budget?.Reserve(ParserCollectionMemory.GetArrayByteCount<OpenTypeQuadraticSegment>(capacity), SvgMemoryCategory.Geometry);
        try
        {
            var replacement = new OpenTypeQuadraticSegment[capacity];
            CopySegments(replacement);
            GlyphDecoderBudgetProbe.Reserved(_capacityPhase, _budget);
            _reservation?.Dispose();
            _reservation = replacementReservation;
            _segments = replacement;
        }
        catch
        {
            replacementReservation?.Dispose();
            throw;
        }
    }

    private void CopySegments(Span<OpenTypeQuadraticSegment> destination)
    {
        for (int index = 0; index < _count; index++)
        {
            Consume();
            destination[index] = _segments[index];
        }
    }

    private int[] ToContourEndArray()
    {
        if (_contourCount == 0) return [];
        var copy = new int[_contourCount];
        CopyContourEnds(copy);
        return copy;
    }

    private void GrowContourEnds()
    {
        int capacity = _contourEnds.Length == 0 ? 4 : checked(_contourEnds.Length * 2);
        RenderBudget.BudgetReservation? replacementReservation = _budget?.Reserve(ParserCollectionMemory.GetArrayByteCount<int>(capacity), SvgMemoryCategory.Geometry);
        try
        {
            var replacement = new int[capacity];
            CopyContourEnds(replacement);
            GlyphDecoderBudgetProbe.Reserved(_contourCapacityPhase, _budget);
            _contourReservation?.Dispose();
            _contourReservation = replacementReservation;
            _contourEnds = replacement;
        }
        catch
        {
            replacementReservation?.Dispose();
            throw;
        }
    }

    private void CopyContourEnds(Span<int> destination)
    {
        for (int index = 0; index < _contourCount; index++)
        {
            Consume();
            destination[index] = _contourEnds[index];
        }
    }

    private void Consume()
    {
        if (_workCounter is null) _cancellationToken.ThrowIfCancellationRequested();
        else _workCounter.Consume(_cancellationToken);
    }
}

/// <summary>Builds composite-component records with reservation-backed capacity and exact-copy ownership.</summary>
internal sealed class OpenTypeCompositeComponentAccumulator : IDisposable
{
    private readonly RenderBudget? _budget;
    private readonly ParserWorkCounter? _workCounter;
    private readonly CancellationToken _cancellationToken;
    private OpenTypeCompositeGlyphComponent[] _components = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _count;

    internal OpenTypeCompositeComponentAccumulator(RenderBudget? budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        _budget = budget;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
    }

    internal int Count => _count;

    internal void Add(OpenTypeCompositeGlyphComponent component)
    {
        if (_count == _components.Length) Grow();
        _components[_count++] = component;
    }

    internal OpenTypeCompositeGlyphComponent[] ToArray()
    {
        if (_count == 0) return [];
        var copy = new OpenTypeCompositeGlyphComponent[_count];
        CopyComponents(copy);
        return copy;
    }

    /// <summary>Materializes exact component storage while preserving the temporary capacity reservation through the copy.</summary>
    internal OpenTypeCompositeGlyphComponentStorage Detach(OpenTypeTableRange instructions)
    {
        if (_count == 0)
        {
            Dispose();
            return new([], null, instructions);
        }
        if (_budget is null) return new(ToArray(), null, instructions);
        RenderBudget.BudgetReservation reservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<OpenTypeCompositeGlyphComponent>(_count), SvgMemoryCategory.Geometry);
        OpenTypeCompositeGlyphComponent[] exact;
        try { exact = new OpenTypeCompositeGlyphComponent[_count]; }
        catch { reservation.Dispose(); throw; }
        GlyphDecoderBudgetProbe.Reserved(GlyphDecoderBudgetPhase.CompositeComponentsExact, _budget);
        try { CopyComponents(exact); }
        catch { reservation.Dispose(); throw; }
        Dispose();
        return new(exact, reservation, instructions);
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
        _components = [];
        _count = 0;
    }

    private void Grow()
    {
        int capacity = _components.Length == 0 ? 4 : checked(_components.Length * 2);
        RenderBudget.BudgetReservation? replacementReservation = _budget?.Reserve(ParserCollectionMemory.GetArrayByteCount<OpenTypeCompositeGlyphComponent>(capacity), SvgMemoryCategory.Geometry);
        try
        {
            var replacement = new OpenTypeCompositeGlyphComponent[capacity];
            CopyComponents(replacement);
            GlyphDecoderBudgetProbe.Reserved(GlyphDecoderBudgetPhase.CompositeComponentsCapacity, _budget);
            _reservation?.Dispose();
            _reservation = replacementReservation;
            _components = replacement;
        }
        catch
        {
            replacementReservation?.Dispose();
            throw;
        }
    }

    private void CopyComponents(Span<OpenTypeCompositeGlyphComponent> destination)
    {
        for (int index = 0; index < _count; index++)
        {
            Consume();
            destination[index] = _components[index];
        }
    }

    private void Consume()
    {
        if (_workCounter is null) _cancellationToken.ThrowIfCancellationRequested();
        else _workCounter.Consume(_cancellationToken);
    }
}

/// <summary>Owns the bounded active composite-glyph stack used for cycle detection during one expansion.</summary>
internal sealed class OpenTypeGlyphRecursionTracker : IDisposable
{
    private readonly ParserWorkCounter? _workCounter;
    private readonly CancellationToken _cancellationToken;
    private ushort[] _glyphs;
    private RenderBudget.BudgetReservation? _reservation;
    private int _count;

    internal OpenTypeGlyphRecursionTracker(int capacity, RenderBudget? budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
        _reservation = budget?.Reserve(ParserCollectionMemory.GetArrayByteCount<ushort>(capacity), SvgMemoryCategory.Geometry);
        try { _glyphs = new ushort[capacity]; }
        catch { _reservation?.Dispose(); throw; }
        GlyphDecoderBudgetProbe.Reserved(GlyphDecoderBudgetPhase.ActiveGlyphs, budget);
    }

    /// <summary>Returns false when the glyph is already active; entries must exit in last-in-first-out order.</summary>
    internal bool TryEnter(ushort glyph)
    {
        for (int index = 0; index < _count; index++)
        {
            Consume();
            if (_glyphs[index] == glyph) return false;
        }
        if (_count == _glyphs.Length) throw new InvalidOperationException("The composite glyph recursion tracker is smaller than its validated depth limit.");
        _glyphs[_count++] = glyph;
        return true;
    }

    internal void Exit(ushort glyph)
    {
        if (_count == 0 || _glyphs[_count - 1] != glyph) throw new InvalidOperationException("Composite glyph recursion did not exit in stack order.");
        _count--;
    }

    public void Dispose()
    {
        _glyphs = [];
        _count = 0;
        _reservation?.Dispose();
        _reservation = null;
    }

    private void Consume()
    {
        if (_workCounter is null) _cancellationToken.ThrowIfCancellationRequested();
        else _workCounter.Consume(_cancellationToken);
    }
}

/// <summary>Represents the linear part of one validated composite-glyph transform.</summary>
internal readonly record struct OpenTypeAffineTransform(double XX, double XY, double YX, double YY);

/// <summary>Represents an affine transform accumulated while expanding a composite glyph.</summary>
internal readonly record struct OpenTypeOutlineTransform(double XX, double XY, double YX, double YY, double TX, double TY)
{
    internal static OpenTypeOutlineTransform Identity => new(1, 0, 0, 1, 0, 0);

    internal OpenTypeOutlinePoint Apply(OpenTypeOutlinePoint point) => new(XX * point.X + XY * point.Y + TX, YX * point.X + YY * point.Y + TY);

    internal OpenTypeOutlineTransform Compose(OpenTypeCompositeGlyphComponent component)
    {
        (double x, double y) = component.GetOffset(OpenTypeCompositeOffsetMath.NativeLength);
        return new(XX * component.Transform.XX + XY * component.Transform.YX, XX * component.Transform.XY + XY * component.Transform.YY, YX * component.Transform.XX + YY * component.Transform.YX, YX * component.Transform.XY + YY * component.Transform.YY, XX * x + XY * y + TX, YX * x + YY * y + TY);
    }
}

/// <summary>Selects the pinned native length arithmetic without changing authored component metadata.</summary>
internal enum OpenTypeCompositeOffsetMath { NativeLength, FontationsLength, DirectWrite }

/// <summary>Represents one validated, non-expanded component in a composite TrueType glyph.</summary>
internal readonly record struct OpenTypeCompositeGlyphComponent(ushort GlyphIndex, int HorizontalOffset, int VerticalOffset, OpenTypeAffineTransform Transform, ushort Flags)
{
    /// <summary>Resolves the component's design-unit translation before device scaling and optional grid rounding.</summary>
    /// <remarks>Allocation-free scalar setup after the caller charges the component visit. Native scaled offsets
    /// use the two matrix-row lengths rather than transforming the translation vector; raw metadata stays unchanged.</remarks>
    internal OpenTypeOutlinePoint GetOffset(OpenTypeCompositeOffsetMath math)
    {
        if ((Flags & 2048) == 0 || (Flags & (8 | 64 | 128)) == 0) return new(HorizontalOffset, VerticalOffset);
        if (math == OpenTypeCompositeOffsetMath.DirectWrite)
            return new(HorizontalOffset * Math.Max(Math.Abs(Transform.XX), Math.Abs(Transform.YX)), VerticalOffset * Math.Max(Math.Abs(Transform.XY), Math.Abs(Transform.YY)));
        return new(Scale(HorizontalOffset, Transform.XX, Transform.XY, math), Scale(VerticalOffset, Transform.YY, Transform.YX, math));

        static int Scale(int value, double first, double second, OpenTypeCompositeOffsetMath math)
        {
            double x = first * 65536, y = second * 65536;
            long length = math == OpenTypeCompositeOffsetMath.FontationsLength
                ? Math.Max(Math.Abs((long)x), Math.Abs((long)y)) + (3 * Math.Min(Math.Abs((long)x), Math.Abs((long)y)) >> 3)
                : (long)Math.Floor(Math.Sqrt(x * x + y * y) + .5);
            long product = value * length;
            return checked((int)(Math.Sign(product) * ((Math.Abs(product) + 32768) >> 16)));
        }
    }
}
