using System.Buffers.Binary;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.VisualTests;

internal enum CompositeMetricsCase { ParentMetrics, ChildMetrics, TransformedChildMetrics, NestedChildMetrics }

/// <summary>Builds a diagnostic parent that copies its right phantom position onto its first outline point. Borrows the public face and returns a distinct owned byte array; no product input is modified.</summary>
internal static class CompositeMetricsFont
{
    internal const ushort ParentGlyph = 190;
    internal static byte[] Create(byte[] original, CompositeMetricsCase variant)
    {
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(original, CancellationToken.None);
        if (metrics.IndexToLocFormat != 1) throw new InvalidDataException("The diagnostic face requires long glyph offsets.");
        OpenTypeTableRange child = OpenTypeFont.GetGlyphDataRange(original, 88, CancellationToken.None);
        int contours = Read16(original, child.Offset);
        int pointCount = Read16(original, child.Offset + 10 + (contours - 1) * 2) + 1;
        bool selected = variant != CompositeMetricsCase.ParentMetrics;
        bool transformed = variant == CompositeMetricsCase.TransformedChildMetrics;
        bool nested = variant == CompositeMetricsCase.NestedChildMetrics;
        byte[] parent = Component(nested ? (ushort)191 : (ushort)88, selected, transformed, true);
        byte[] inner = Component(88, true, false, false);
        using MemoryStream glyf = new();
        byte[] loca = new byte[(metrics.GlyphCount + 1) * 4];
        for (ushort glyph = 0; glyph < metrics.GlyphCount; glyph++)
        {
            Write32(loca, glyph * 4, checked((uint)glyf.Length));
            if (glyph == ParentGlyph) glyf.Write(parent);
            else if (nested && glyph == 191) glyf.Write(inner);
            else
            {
                OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(original, glyph, CancellationToken.None);
                glyf.Write(original.AsSpan(range.Offset, range.Length));
            }
            if ((glyf.Length & 1) != 0) glyf.WriteByte(0);
        }
        Write32(loca, metrics.GlyphCount * 4, checked((uint)glyf.Length));
        byte[] bytes = new byte[original.Length + glyf.Length + loca.Length];
        original.CopyTo(bytes, 0);
        glyf.GetBuffer().AsSpan(0, checked((int)glyf.Length)).CopyTo(bytes.AsSpan(original.Length));
        loca.CopyTo(bytes, original.Length + checked((int)glyf.Length));
        Redirect(0x676C7966, original.Length, checked((int)glyf.Length));
        Redirect(0x6C6F6361, original.Length + checked((int)glyf.Length), loca.Length);
        Write16(bytes, metrics.Hmtx.Offset + ParentGlyph * 4, 1000);
        Write16(bytes, metrics.Hmtx.Offset + ParentGlyph * 4 + 2, 0);
        return bytes;

        byte[] Component(ushort glyph, bool useMetrics, bool scale, bool instructions)
        {
            byte[] program = instructions ? [0x01, 0xB8, (byte)((pointCount + 1) >> 8), (byte)(pointCount + 1), 0x10, 0xB0, 0, 0x3C] : [];
            byte[] result = new byte[18 + (scale ? 2 : 0) + (instructions ? 2 + program.Length : 0)];
            Write16(result, 0, ushort.MaxValue);
            Write16(result, 6, 1000); Write16(result, 8, 1000);
            Write16(result, 10, (ushort)(3 | (useMetrics ? 512 : 0) | (scale ? 8 : 0) | (instructions ? 256 : 0)));
            Write16(result, 12, glyph);
            Write16(result, 14, scale ? (ushort)100 : (ushort)0);
            Write16(result, 16, scale ? (ushort)50 : (ushort)0);
            int offset = 18;
            if (scale) { Write16(result, offset, 8192); offset += 2; }
            if (instructions) { Write16(result, offset, checked((ushort)program.Length)); program.CopyTo(result, offset + 2); }
            return result;
        }

        void Redirect(uint tag, int offset, int length)
        {
            int count = Read16(bytes, 4);
            for (int index = 0; index < count; index++)
            {
                int entry = 12 + index * 16;
                if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry)) != tag) continue;
                Write32(bytes, entry + 8, checked((uint)offset)); Write32(bytes, entry + 12, checked((uint)length)); return;
            }
            throw new InvalidDataException("The diagnostic face is missing a required table.");
        }
    }

    private static ushort Read16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
    private static void Write16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}
