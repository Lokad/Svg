namespace Lokad.Svg.Fonts;

/// <summary>
/// Validates the bounded standalone-TrueType subset required before glyph parsing.
/// This reader borrows its input and does not retain, copy, or cache font bytes.
/// </summary>
internal static class OpenTypeFont
{
    private const int MaximumTableRecords = 128;
    private const int MaximumSimpleGlyphContours = 4_096;
    private const int MaximumSimpleGlyphPoints = 32_768;
    private const int MaximumCompositeGlyphComponents = 64;
    private const int MaximumCompositeGlyphDepth = 8;
    private const int MaximumExpandedGlyphSegments = 65_536;
    private const uint TrueTypeScalerType = 0x00010000;
    private const uint TrueTypeCollectionTag = 0x74746366;
    private const uint HeadTag = 0x68656164;
    private const uint MaxpTag = 0x6D617870;
    private const uint HheaTag = 0x68686561;
    private const uint HmtxTag = 0x686D7478;
    private const uint CmapTag = 0x636D6170;
    private const uint LocaTag = 0x6C6F6361;
    private const uint GlyfTag = 0x676C7966;

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
        ReadOnlySpan<byte> data = bytes.Span;
        OpenTypeTableRange cmap = FindTable(data, CmapTag, "cmap");
        RequireTableLength(cmap, 4, "cmap");
        int encodingCount = ReadUInt16(data, cmap.Offset + 2);
        RequireTableLength(cmap, checked(4 + encodingCount * 8), "cmap");
        OpenTypeTableRange? format4Candidate = null;
        OpenTypeTableRange? format12Candidate = null;
        for (int index = 0; index < encodingCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int record = cmap.Offset + 4 + index * 8;
            ushort platform = ReadUInt16(data, record);
            ushort encoding = ReadUInt16(data, record + 2);
            uint relativeOffset = ReadUInt32(data, record + 4);
            if (relativeOffset > int.MaxValue || relativeOffset > cmap.Length - 2) throw InvalidFont("A cmap encoding record points outside its table.");
            int offset = checked(cmap.Offset + (int)relativeOffset);
            ushort format = ReadUInt16(data, offset);
            if (format == 4 && platform == 3 && (encoding == 1 || encoding == 0)) format4Candidate = new OpenTypeTableRange(offset, cmap.Length - (int)relativeOffset);
            if (format == 12 && platform == 3 && encoding == 10) format12Candidate = new OpenTypeTableRange(offset, cmap.Length - (int)relativeOffset);
        }
        if (scalar > 0xFFFF) return format12Candidate is OpenTypeTableRange selected12 ? GetFormat12GlyphIndex(data, selected12, scalar, metrics.GlyphCount) : throw new SvgException(SvgErrorCode.UnsupportedFeature, "The selected font has no supported supplementary-plane character map.");
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

    private static ushort GetFormat12GlyphIndex(ReadOnlySpan<byte> data, OpenTypeTableRange table, uint scalar, ushort glyphCount)
    {
        RequireTableLength(table, 16, "cmap format 12");
        uint length = ReadUInt32(data, table.Offset + 4);
        uint groups = ReadUInt32(data, table.Offset + 12);
        if (length < 16 || length > (uint)table.Length || groups > (length - 16) / 12) throw InvalidFont("The cmap format 12 length is invalid.");
        uint previousEnd = 0;
        for (uint index = 0; index < groups; index++)
        {
            int offset = checked(table.Offset + 16 + (int)index * 12);
            uint start = ReadUInt32(data, offset);
            uint end = ReadUInt32(data, offset + 4);
            uint startGlyph = ReadUInt32(data, offset + 8);
            if (start > end || end > 0x10FFFF || (index != 0 && start <= previousEnd)) throw InvalidFont("The cmap format 12 groups are not strictly ordered.");
            previousEnd = end;
            if (scalar < start || scalar > end) continue;
            uint offsetWithinGroup = scalar - start;
            if (startGlyph > uint.MaxValue - offsetWithinGroup) throw InvalidFont("The cmap format 12 group glyph range overflows.");
            uint glyph = startGlyph + offsetWithinGroup;
            if (glyph >= glyphCount) throw InvalidFont("The cmap maps a scalar to a glyph outside maxp.");
            return (ushort)glyph;
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

    /// <summary>Gets the validated borrowed glyf-table range for one glyph, which may be empty.</summary>
    internal static OpenTypeTableRange GetGlyphDataRange(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
    {
        OpenTypeFontMetrics metrics = ParseMetrics(bytes, cancellationToken);
        if (glyphIndex >= metrics.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyphIndex));
        ReadOnlySpan<byte> data = bytes.Span;
        OpenTypeTableRange loca = FindTable(data, LocaTag, "loca");
        OpenTypeTableRange glyf = FindTable(data, GlyfTag, "glyf");
        int entrySize = metrics.IndexToLocFormat == 0 ? 2 : 4;
        RequireTableLength(loca, checked((metrics.GlyphCount + 1) * entrySize), "loca");
        int start = ReadGlyphLocation(data, loca, glyphIndex, metrics.IndexToLocFormat);
        int end = ReadGlyphLocation(data, loca, glyphIndex + 1, metrics.IndexToLocFormat);
        if (end < start || end > glyf.Length) throw InvalidFont("The loca table contains an invalid glyph range.");
        return new OpenTypeTableRange(checked(glyf.Offset + start), end - start);
    }

    /// <summary>Reads a nonempty glyph header after validating its loca/glyf range.</summary>
    internal static OpenTypeGlyphHeader? GetGlyphHeader(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
    {
        OpenTypeTableRange glyph = GetGlyphDataRange(bytes, glyphIndex, cancellationToken);
        if (glyph.Length == 0) return null;
        if (glyph.Length < 10) throw InvalidFont("A nonempty glyf record is shorter than its header.");
        ReadOnlySpan<byte> data = bytes.Span;
        short contourCount = ReadInt16(data, glyph.Offset);
        return new OpenTypeGlyphHeader(contourCount, ReadInt16(data, glyph.Offset + 2), ReadInt16(data, glyph.Offset + 4), ReadInt16(data, glyph.Offset + 6), ReadInt16(data, glyph.Offset + 8));
    }

    /// <summary>Reads bounded contour endpoints from a simple glyph and excludes its hint instructions.</summary>
    internal static OpenTypeSimpleGlyphStructure GetSimpleGlyphStructure(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
    {
        OpenTypeTableRange glyph = GetGlyphDataRange(bytes, glyphIndex, cancellationToken);
        if (glyph.Length == 0) return new OpenTypeSimpleGlyphStructure([], 0);
        OpenTypeGlyphHeader? header = GetGlyphHeader(bytes, glyphIndex, cancellationToken);
        if (header is not OpenTypeGlyphHeader value) throw new InvalidOperationException("A nonempty glyph did not return a header.");
        if (value.IsComposite) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Composite TrueType glyph decoding is not implemented yet.");
        if (value.ContourCount > MaximumSimpleGlyphContours) throw new SvgException(SvgErrorCode.LimitExceeded, "The simple glyph contour count exceeds the supported limit.");
        ReadOnlySpan<byte> data = bytes.Span;
        int endpointBytes = checked(value.ContourCount * 2);
        int instructionLengthOffset = checked(glyph.Offset + 10 + endpointBytes);
        if (instructionLengthOffset > glyph.Offset + glyph.Length - 2) throw InvalidFont("The simple glyf contour endpoints are truncated.");
        ushort instructionLength = ReadUInt16(data, instructionLengthOffset);
        int pointDataOffset = checked(instructionLengthOffset + 2 + instructionLength);
        if (pointDataOffset > glyph.Offset + glyph.Length) throw InvalidFont("The simple glyf instructions are truncated.");
        var endpoints = new ushort[value.ContourCount];
        ushort previous = 0;
        for (int index = 0; index < endpoints.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ushort endpoint = ReadUInt16(data, glyph.Offset + 10 + index * 2);
            if (index != 0 && endpoint <= previous) throw InvalidFont("The simple glyf contour endpoints are not strictly increasing.");
            endpoints[index] = endpoint;
            previous = endpoint;
        }
        return new OpenTypeSimpleGlyphStructure(endpoints, pointDataOffset - glyph.Offset);
    }

    /// <summary>Decodes bounded simple-glyph flags and delta coordinates into borrowed-font outline points.</summary>
    internal static OpenTypeGlyphPoint[] GetSimpleGlyphPoints(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
    {
        OpenTypeSimpleGlyphStructure structure = GetSimpleGlyphStructure(bytes, glyphIndex, cancellationToken);
        if (structure.ContourEndpoints.Length == 0) return [];
        int pointCount = checked(structure.ContourEndpoints[^1] + 1);
        if (pointCount > MaximumSimpleGlyphPoints) throw new SvgException(SvgErrorCode.LimitExceeded, "The simple glyph point count exceeds the supported limit.");
        OpenTypeTableRange glyph = GetGlyphDataRange(bytes, glyphIndex, cancellationToken);
        ReadOnlySpan<byte> data = bytes.Span;
        int cursor = checked(glyph.Offset + structure.PointDataOffset);
        int end = checked(glyph.Offset + glyph.Length);
        var flags = new byte[pointCount];
        for (int index = 0; index < flags.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            x = checked(x + ReadCoordinate(data, ref cursor, end, flags[index], 2, 16, "horizontal"));
            points[index] = new OpenTypeGlyphPoint(x, 0, (flags[index] & 1) != 0);
        }
        int y = 0;
        for (int index = 0; index < points.Length; index++)
        {
            y = checked(y + ReadCoordinate(data, ref cursor, end, flags[index], 4, 32, "vertical"));
            points[index] = points[index] with { Y = y };
        }
        return points;
    }

    /// <summary>Builds closed quadratic segments from decoded simple-glyph contours, including implied on-curve points.</summary>
    internal static OpenTypeQuadraticSegment[] BuildQuadraticContours(ReadOnlySpan<OpenTypeGlyphPoint> points, ReadOnlySpan<ushort> endpoints)
    {
        if (endpoints.Length == 0) return [];
        if (points.Length == 0 || endpoints[^1] != points.Length - 1) throw new ArgumentException("Contour endpoints must cover the supplied points.", nameof(endpoints));
        var segments = new List<OpenTypeQuadraticSegment>(points.Length * 2);
        int first = 0;
        foreach (ushort lastValue in endpoints)
        {
            int last = lastValue;
            if (last < first) throw new ArgumentException("Contour endpoints must be strictly increasing.", nameof(endpoints));
            OpenTypeGlyphPoint initial = points[first];
            OpenTypeGlyphPoint final = points[last];
            OpenTypeOutlinePoint start = initial.IsOnCurve ? ToOutlinePoint(initial) : final.IsOnCurve ? ToOutlinePoint(final) : Midpoint(ToOutlinePoint(final), ToOutlinePoint(initial));
            OpenTypeOutlinePoint current = start;
            OpenTypeOutlinePoint? control = null;
            for (int index = first; index <= last; index++)
            {
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
                    OpenTypeOutlinePoint implied = Midpoint(pending, ToOutlinePoint(point));
                    segments.Add(new OpenTypeQuadraticSegment(current, pending, implied));
                    current = implied;
                    control = ToOutlinePoint(point);
                }
                else control = ToOutlinePoint(point);
            }
            if (control is OpenTypeOutlinePoint closingControl) segments.Add(new OpenTypeQuadraticSegment(current, closingControl, start));
            else if (current != start) segments.Add(new OpenTypeQuadraticSegment(current, start, start));
            first = last + 1;
        }
        return segments.ToArray();
    }

    /// <summary>Decodes a bounded simple glyph into closed quadratic outline segments.</summary>
    internal static OpenTypeQuadraticSegment[] GetSimpleGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
    {
        OpenTypeSimpleGlyphStructure structure = GetSimpleGlyphStructure(bytes, glyphIndex, cancellationToken);
        if (structure.ContourEndpoints.Length == 0) return [];
        OpenTypeGlyphPoint[] points = GetSimpleGlyphPoints(bytes, glyphIndex, cancellationToken);
        return BuildQuadraticContours(points, structure.ContourEndpoints);
    }

    /// <summary>Reads bounded composite-glyph component metadata without recursively expanding outlines.</summary>
    internal static OpenTypeCompositeGlyphComponent[] GetCompositeGlyphComponents(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
    {
        OpenTypeTableRange glyph = GetGlyphDataRange(bytes, glyphIndex, cancellationToken);
        OpenTypeGlyphHeader? header = GetGlyphHeader(bytes, glyphIndex, cancellationToken);
        if (header is not OpenTypeGlyphHeader value || !value.IsComposite) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The requested glyph is not a composite TrueType glyph.");
        ReadOnlySpan<byte> data = bytes.Span;
        int cursor = glyph.Offset + 10;
        int end = glyph.Offset + glyph.Length;
        var components = new List<OpenTypeCompositeGlyphComponent>();
        ushort flags;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (components.Count == MaximumCompositeGlyphComponents) throw new SvgException(SvgErrorCode.LimitExceeded, "The composite glyph component count exceeds the supported limit.");
            if (cursor > end - 4) throw InvalidFont("The composite glyf component is truncated.");
            flags = ReadUInt16(data, cursor);
            ushort componentGlyph = ReadUInt16(data, cursor + 2);
            cursor += 4;
            bool words = (flags & 1) != 0;
            bool xyValues = (flags & 2) != 0;
            int argumentBytes = words ? 4 : 2;
            if (cursor > end - argumentBytes) throw InvalidFont("The composite glyf arguments are truncated.");
            if (!xyValues) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Point-matched composite TrueType glyph components are not implemented yet.");
            int horizontalOffset = words ? ReadInt16(data, cursor) : unchecked((sbyte)data[cursor]);
            int verticalOffset = words ? ReadInt16(data, cursor + (words ? 2 : 1)) : unchecked((sbyte)data[cursor + 1]);
            cursor += argumentBytes;
            OpenTypeAffineTransform transform = ReadCompositeTransform(data, ref cursor, end, flags);
            components.Add(new OpenTypeCompositeGlyphComponent(componentGlyph, horizontalOffset, verticalOffset, transform));
        } while ((flags & 32) != 0);
        return components.ToArray();
    }

    /// <summary>Expands bounded simple and composite glyphs into transformed quadratic segments.</summary>
    internal static OpenTypeQuadraticSegment[] GetGlyphContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, CancellationToken cancellationToken)
    {
        OpenTypeFontMetrics metrics = ParseMetrics(bytes, cancellationToken);
        var active = new HashSet<ushort>();
        var contours = new List<OpenTypeQuadraticSegment>();
        ExpandGlyph(bytes, glyphIndex, metrics, active, 0, OpenTypeOutlineTransform.Identity, contours, cancellationToken);
        return contours.ToArray();
    }

    private static void ExpandGlyph(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, HashSet<ushort> active, int depth, OpenTypeOutlineTransform transform, List<OpenTypeQuadraticSegment> contours, CancellationToken cancellationToken)
    {
        if (glyphIndex >= metrics.GlyphCount) throw InvalidFont("A composite glyf component references a glyph outside maxp.");
        if (depth > MaximumCompositeGlyphDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "The composite glyph nesting depth exceeds the supported limit.");
        if (!active.Add(glyphIndex)) throw InvalidFont("The composite glyph graph contains a cycle.");
        try
        {
            OpenTypeGlyphHeader? header = GetGlyphHeader(bytes, glyphIndex, cancellationToken);
            if (header is null) return;
            if (!header.Value.IsComposite)
            {
                foreach (OpenTypeQuadraticSegment segment in GetSimpleGlyphContours(bytes, glyphIndex, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (contours.Count == MaximumExpandedGlyphSegments) throw new SvgException(SvgErrorCode.LimitExceeded, "The expanded glyph segment count exceeds the supported limit.");
                    contours.Add(Transform(segment, transform));
                }
                return;
            }
            foreach (OpenTypeCompositeGlyphComponent component in GetCompositeGlyphComponents(bytes, glyphIndex, cancellationToken))
            {
                ExpandGlyph(bytes, component.GlyphIndex, metrics, active, depth + 1, transform.Compose(component), contours, cancellationToken);
            }
        }
        finally { active.Remove(glyphIndex); }
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
        return new OpenTypeAffineTransform(first, second, third, fourth);
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

/// <summary>Represents the validated fixed header of one nonempty TrueType glyf record.</summary>
internal readonly record struct OpenTypeGlyphHeader(short ContourCount, short XMinimum, short YMinimum, short XMaximum, short YMaximum)
{
    internal bool IsComposite => ContourCount < 0;
}

/// <summary>Represents bounded simple-glyph contour metadata before flag and coordinate decoding.</summary>
internal readonly record struct OpenTypeSimpleGlyphStructure(ushort[] ContourEndpoints, int PointDataOffset);

/// <summary>Represents one decoded simple-glyph point in font units.</summary>
internal readonly record struct OpenTypeGlyphPoint(int X, int Y, bool IsOnCurve);

/// <summary>Represents one floating-point point in a decoded TrueType outline.</summary>
internal readonly record struct OpenTypeOutlinePoint(double X, double Y);

/// <summary>Represents one quadratic segment; a line has its control equal to its end.</summary>
internal readonly record struct OpenTypeQuadraticSegment(OpenTypeOutlinePoint Start, OpenTypeOutlinePoint Control, OpenTypeOutlinePoint End);

/// <summary>Represents the linear part of one validated composite-glyph transform.</summary>
internal readonly record struct OpenTypeAffineTransform(double XX, double XY, double YX, double YY);

/// <summary>Represents an affine transform accumulated while expanding a composite glyph.</summary>
internal readonly record struct OpenTypeOutlineTransform(double XX, double XY, double YX, double YY, double TX, double TY)
{
    internal static OpenTypeOutlineTransform Identity => new(1, 0, 0, 1, 0, 0);

    internal OpenTypeOutlinePoint Apply(OpenTypeOutlinePoint point) => new(XX * point.X + XY * point.Y + TX, YX * point.X + YY * point.Y + TY);

    internal OpenTypeOutlineTransform Compose(OpenTypeCompositeGlyphComponent component)
        => new(XX * component.Transform.XX + XY * component.Transform.YX, XX * component.Transform.XY + XY * component.Transform.YY, YX * component.Transform.XX + YY * component.Transform.YX, YX * component.Transform.XY + YY * component.Transform.YY, XX * component.HorizontalOffset + XY * component.VerticalOffset + TX, YX * component.HorizontalOffset + YY * component.VerticalOffset + TY);
}

/// <summary>Represents one validated, non-expanded component in a composite TrueType glyph.</summary>
internal readonly record struct OpenTypeCompositeGlyphComponent(ushort GlyphIndex, int HorizontalOffset, int VerticalOffset, OpenTypeAffineTransform Transform);
