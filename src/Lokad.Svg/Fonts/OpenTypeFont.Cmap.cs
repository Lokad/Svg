namespace Lokad.Svg.Fonts;

internal static partial class OpenTypeFont
{
    private enum CmapKind { Format4, Format12 }
    private readonly record struct CmapLayout(CmapKind Kind, OpenTypeTableRange Table, int Count);

    /// <summary>Maps a synchronous batch against unchanged borrowed font bytes, validating on its first actual lookup.</summary>
    /// <remarks>Owns no storage and retains no shared cache. Keep the byte owner alive and unchanged;
    /// use this mutable stack-only reader on one thread. Each scalar retains the lookup cancellation
    /// checkpoint. Failed or canceled validation publishes no layout, so a subsequent lookup can retry.</remarks>
    internal ref struct CmapLookup
    {
        private readonly ReadOnlyMemory<byte> _bytes;
        private readonly OpenTypeTableRange? _cmap;
        private readonly ushort _glyphCount;
        private CmapLayout? _layout;

        internal CmapLookup(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics)
        {
            _bytes = bytes;
            _cmap = metrics.Cmap;
            _glyphCount = metrics.GlyphCount;
            _layout = null;
        }

        internal ushort GetGlyphIndex(uint scalar, CancellationToken token)
        {
            if (scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF) throw new ArgumentOutOfRangeException(nameof(scalar));
            CmapLookupProbe.Started();
            token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> data = _bytes.Span;
            if (_layout is not CmapLayout layout)
            {
                layout = PrepareCmap(data, _cmap, _glyphCount, token);
                _layout = layout;
            }
            int first = 0, last = layout.Count;
            int ends = layout.Table.Offset + (layout.Kind == CmapKind.Format4 ? 14 : 20);
            int stride = layout.Kind == CmapKind.Format4 ? 2 : 12;
            while (first < last)
            {
                token.ThrowIfCancellationRequested();
                int middle = first + (last - first) / 2;
                uint end = layout.Kind == CmapKind.Format4 ? ReadUInt16(data, ends + middle * stride) : ReadUInt32(data, ends + middle * stride);
                if (scalar > end) first = middle + 1;
                else last = middle;
            }
            if (first == layout.Count) return 0;
            if (layout.Kind == CmapKind.Format12)
            {
                int group = layout.Table.Offset + 16 + first * 12;
                uint start = ReadUInt32(data, group);
                return scalar < start ? (ushort)0 : (ushort)(ReadUInt32(data, group + 8) + scalar - start);
            }
            int starts = ends + layout.Count * 2 + 2;
            ushort startCode = ReadUInt16(data, starts + first * 2);
            if (scalar < startCode) return 0;
            int deltas = starts + layout.Count * 2, ranges = deltas + layout.Count * 2;
            short delta = ReadInt16(data, deltas + first * 2);
            ushort rangeOffset = ReadUInt16(data, ranges + first * 2);
            ushort glyph;
            if (rangeOffset == 0) glyph = unchecked((ushort)(scalar + delta));
            else
            {
                int at = checked(ranges + first * 2 + rangeOffset + checked((int)(scalar - startCode) * 2));
                if (at < layout.Table.Offset || at > layout.Table.Offset + layout.Table.Length - 2)
                    throw InvalidFont("The cmap glyph-index array is truncated.");
                glyph = ReadUInt16(data, at);
                if (glyph != 0) glyph = unchecked((ushort)(glyph + delta));
            }
            if (glyph >= _glyphCount && glyph != 0) throw InvalidFont("The cmap maps a scalar to a glyph outside maxp.");
            return glyph;

            static CmapLayout PrepareCmap(ReadOnlySpan<byte> data, OpenTypeTableRange? range,
                ushort glyphCount, CancellationToken token)
            {
                CmapLookupProbe.Validating();
                token.ThrowIfCancellationRequested();
                OpenTypeTableRange cmap = range ?? throw InvalidFont("The SFNT font is missing its cmap table.");
                RequireTableLength(cmap, 4, "cmap");
                int encodingCount = ReadUInt16(data, cmap.Offset + 2);
                RequireTableLength(cmap, checked(4 + encodingCount * 8), "cmap");
                OpenTypeTableRange? format4 = null, format12 = null;
                for (int index = 0; index < encodingCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    int record = cmap.Offset + 4 + index * 8;
                    ushort platform = ReadUInt16(data, record), encoding = ReadUInt16(data, record + 2);
                    uint relative = ReadUInt32(data, record + 4);
                    if (relative > int.MaxValue || relative > cmap.Length - 2) throw InvalidFont("A cmap encoding record points outside its table.");
                    int offset = checked(cmap.Offset + (int)relative);
                    ushort format = ReadUInt16(data, offset);
                    if (format == 4 && (platform == 0 || platform == 3 && encoding == 1)) format4 = new(offset, cmap.Length - (int)relative);
                    if (format == 12 && (platform == 0 || platform == 3 && encoding == 10)) format12 = new(offset, cmap.Length - (int)relative);
                }
                if (format12 is {} selected12)
                {
                    RequireTableLength(selected12, 16, "cmap format 12");
                    uint length = ReadUInt32(data, selected12.Offset + 4), groups = ReadUInt32(data, selected12.Offset + 12);
                    if (length < 16 || length > (uint)selected12.Length || groups > (length - 16) / 12) throw InvalidFont("The cmap format 12 length is invalid.");
                    uint previousEnd = 0;
                    for (uint index = 0; index < groups; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        int offset = checked(selected12.Offset + 16 + (int)index * 12);
                        uint start = ReadUInt32(data, offset), end = ReadUInt32(data, offset + 4), startGlyph = ReadUInt32(data, offset + 8);
                        if (start > end || end > 0x10FFFF || index != 0 && start <= previousEnd) throw InvalidFont("The cmap format 12 groups are not strictly ordered.");
                        previousEnd = end;
                        uint count = end - start;
                        if (startGlyph > uint.MaxValue - count) throw InvalidFont("The cmap format 12 group glyph range overflows.");
                        if (startGlyph + count >= glyphCount) throw InvalidFont("The cmap maps a scalar to a glyph outside maxp.");
                    }
                    return new(CmapKind.Format12, new(selected12.Offset, (int)length), (int)groups);
                }
                if (format4 is not {} selected4) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The selected font has no supported Unicode cmap format 4 table.");
                RequireTableLength(selected4, 16, "cmap format 4");
                int size = ReadUInt16(data, selected4.Offset + 2), segmentsX2 = ReadUInt16(data, selected4.Offset + 6);
                if (size < 16 || size > selected4.Length) throw InvalidFont("The cmap format 4 length is invalid.");
                if (segmentsX2 == 0 || (segmentsX2 & 1) != 0) throw InvalidFont("The cmap format 4 segment count is invalid.");
                int segments = segmentsX2 / 2;
                if (checked(16 + segments * 8) > size) throw InvalidFont("The cmap format 4 arrays are truncated.");
                int endCodes = selected4.Offset + 14, startCodes = endCodes + segments * 2 + 2;
                int ranges = startCodes + segments * 4, glyphs = checked(ranges + segments * 2);
                ushort previous = 0;
                for (int index = 0; index < segments; index++)
                {
                    token.ThrowIfCancellationRequested();
                    ushort end = ReadUInt16(data, endCodes + index * 2), start = ReadUInt16(data, startCodes + index * 2);
                    if (start > end || index != 0 && end <= previous) throw InvalidFont("The cmap format 4 segments are not strictly ordered.");
                    previous = end;
                    ushort relative = ReadUInt16(data, ranges + index * 2);
                    if (relative == 0) continue;
                    if ((relative & 1) != 0) throw InvalidFont("The cmap format 4 glyph-index offset is not word-aligned.");
                    int firstGlyph = checked(ranges + index * 2 + relative), lastGlyph = checked(firstGlyph + (end - start) * 2);
                    if (firstGlyph < glyphs || lastGlyph > selected4.Offset + size - 2) throw InvalidFont("The cmap format 4 glyph-index array is truncated.");
                }
                if (previous != ushort.MaxValue) throw InvalidFont("The cmap format 4 segments lack the required sentinel.");
                return new(CmapKind.Format4, new(selected4.Offset, size), segments);
            }
        }
    }
}
