using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Native_hint_targets_preserve_signed_half_scaling_in_points_and_control_values(bool windows, bool composite, bool useControl)
    {
        // Native 24px/2048-unit witnesses include both signed half ties and a
        // real MIAP read of -14 font units. A parent replays the same child.
        byte[] bytes = MakeFont(useControl);
        byte[] unchanged = bytes.ToArray();
        TrueTypeHintTarget target = new(windows ? 42 : 40,
            windows ? TrueTypeHintQueryFlags.Symmetric | TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned : TrueTypeHintQueryFlags.Grayscale,
            TrueTypeHintControlScale.FullPrecision, TrueTypeHintPreparation.Single)
            { Grid = windows ? TrueTypeHintGrid.ClearType : TrueTypeHintGrid.Physical };
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(bytes, composite ? (ushort)2 : (ushort)1,
            OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 24, target, budget, CancellationToken.None))
        {
            Assert.Equal(new OpenTypeOutlinePoint(windows ? -31 : -32, useControl ? windows ? -10 : -11 : windows ? -7 : -8), contours.Segments[0].Start);
            Assert.Equal(new OpenTypeOutlinePoint(560, 1058), contours.Segments[0].End);
            Assert.Equal(new OpenTypeOutlinePoint(821, windows ? -10 : -11), contours.Segments[1].End);
        }
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static byte[] MakeFont(bool control)
        {
            byte[] original = CreateFont(2048, 3, 2, 1);
            int tableCount = 7, dataStart = 12 + tableCount * 16;
            int maxp = original.Length + 16, glyf = maxp + 32, cvt = glyf + 128;
            byte[] font = new byte[cvt + 2];
            Array.Copy(original, font, dataStart);
            Array.Copy(original, dataStart, font, dataStart + 16, original.Length - dataStart);
            WriteUInt16(font, 4, 8);
            WriteUInt16(font, 6, 128); WriteUInt16(font, 8, 3); WriteUInt16(font, 10, 0);
            for (int index = 0; index < tableCount; index++)
                WriteUInt32(font, 12 + index * 16 + 8, checked((uint)(FindTableOffset(original, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(12 + index * 16, 4))) + 16)));
            WriteTable(font, 1, 0x6D617870, maxp, 32);
            WriteTable(font, 6, 0x676C7966, glyf, 128);
            WriteTable(font, 7, 0x63767420, cvt, 2);
            WriteUInt32(font, maxp, 0x10000); WriteUInt16(font, maxp + 4, 3);
            WriteUInt16(font, maxp + 6, 3); WriteUInt16(font, maxp + 8, 1);
            WriteUInt16(font, maxp + 10, 3); WriteUInt16(font, maxp + 12, 1);
            WriteUInt16(font, maxp + 14, 2); WriteUInt16(font, maxp + 24, 8);
            WriteUInt16(font, maxp + 26, 8); WriteUInt16(font, maxp + 28, 1); WriteUInt16(font, maxp + 30, 1);
            int loca = FindTableOffset(font, 0x6C6F6361), hmtx = FindTableOffset(font, 0x686D7478);
            WriteUInt32(font, loca + 8, 64); WriteUInt32(font, loca + 12, 128);
            WriteInt16(font, hmtx + 6, -42); WriteInt16(font, hmtx + 8, -42);
            WriteInt16(font, cvt, -14);
            WriteInt16(font, glyf, 1); WriteInt16(font, glyf + 2, -42); WriteInt16(font, glyf + 4, -14);
            WriteInt16(font, glyf + 6, 1094); WriteInt16(font, glyf + 8, 1410); WriteUInt16(font, glyf + 10, 2);
            byte[] program = control ? [0xB0, 2, 0x8D, 0x00, 0xB1, 0, 0, 0x3E] : [0xB0, 2, 0x8D];
            WriteUInt16(font, glyf + 12, (ushort)program.Length);
            program.CopyTo(font, glyf + 14);
            int flags = glyf + 14 + program.Length;
            font[flags] = font[flags + 1] = font[flags + 2] = 1;
            WriteInt16(font, flags + 3, -42); WriteInt16(font, flags + 5, 788); WriteInt16(font, flags + 7, 348);
            WriteInt16(font, flags + 9, -10); WriteInt16(font, flags + 11, 1420); WriteInt16(font, flags + 13, -1424);
            int parent = glyf + 64;
            WriteInt16(font, parent, -1); WriteInt16(font, parent + 2, -42); WriteInt16(font, parent + 4, -14);
            WriteInt16(font, parent + 6, 1094); WriteInt16(font, parent + 8, 1410);
            WriteUInt16(font, parent + 10, 3); WriteUInt16(font, parent + 12, 1);
            return font;
        }
    }
}
