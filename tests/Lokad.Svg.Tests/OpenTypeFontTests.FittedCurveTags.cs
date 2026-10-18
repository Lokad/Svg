using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(0x81, 0)]
    [InlineData(0x82, 0)]
    [InlineData(0x81, 1)]
    [InlineData(0x82, 1)]
    [InlineData(0x81, 2)]
    [InlineData(0x82, 2)]
    public void TrueType_fitted_curve_tags_survive_simple_child_and_parent_replay(byte opcode, int stage)
    {
        byte[] actual = MakeFont(opcode == 0x82, opcode, stage);
        byte[] expected = MakeFont(opcode == 0x81, 0, stage);
        byte[] unchanged = actual.ToArray();
        ushort glyph = stage == 0 ? (ushort)1 : (ushort)2;
        RenderBudget budget = new(long.MaxValue);
        TrueTypeHintTarget target = new(40, TrueTypeHintQueryFlags.None, TrueTypeHintControlScale.FullPrecision, TrueTypeHintPreparation.Single);
        using (OpenTypeGlyphContourStorage fitted = OpenTypeFont.GetTrueTypeContours(actual, glyph, OpenTypeFont.ParseMetrics(actual, CancellationToken.None), 15.625, target, budget, new(long.MaxValue), CancellationToken.None))
        using (OpenTypeGlyphContourStorage authored = OpenTypeFont.GetTrueTypeContours(expected, glyph, OpenTypeFont.ParseMetrics(expected, CancellationToken.None), 15.625, target, budget, new(long.MaxValue), CancellationToken.None))
            Assert.Equal(authored.Segments, fitted.Segments);
        Assert.Equal(unchanged, actual);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static byte[] MakeFont(bool onCurve, byte instruction, int instructionStage)
        {
            byte[] font = CreateTriangleFont();
            int oldGlyf = FindTableOffset(font, 0x676C7966);
            int loca = FindTableOffset(font, 0x6C6F6361), maxp = font.Length, glyf = maxp + 32;
            Array.Resize(ref font, glyf + 64);
            Array.Copy(font, oldGlyf, font, glyf, 52);
            WriteTable(font, 1, 0x6D617870, maxp, 32);
            WriteTable(font, 6, 0x676C7966, glyf, 64);
            WriteUInt32(font, maxp, 0x10000); WriteUInt16(font, maxp + 4, 3);
            WriteUInt16(font, maxp + 6, 3); WriteUInt16(font, maxp + 8, 1);
            WriteUInt16(font, maxp + 10, 3); WriteUInt16(font, maxp + 12, 1);
            WriteUInt16(font, maxp + 14, 2); WriteUInt16(font, maxp + 24, 8);
            WriteUInt16(font, maxp + 28, 1); WriteUInt16(font, maxp + 30, 1);
            WriteUInt32(font, loca + 12, 64);
            font[glyf + 15] = (byte)((font[glyf + 15] & ~1) | (onCurve ? 1 : 0));
            if (instruction != 0 && instructionStage != 2)
            {
                Array.Copy(font, glyf + 14, font, glyf + 18, 8);
                WriteUInt16(font, glyf + 12, 4);
                font[glyf + 14] = 0xB1; font[glyf + 15] = font[glyf + 16] = 1; font[glyf + 17] = instruction;
            }
            if (instructionStage != 0)
            {
                int parent = glyf + 32;
                WriteInt16(font, parent, -1);
                bool parentProgram = instruction != 0 && instructionStage == 2;
                WriteUInt16(font, parent + 10, parentProgram ? (ushort)259 : (ushort)3);
                WriteUInt16(font, parent + 12, 1);
                if (parentProgram)
                {
                    WriteUInt16(font, parent + 18, 4);
                    font[parent + 20] = 0xB1; font[parent + 21] = font[parent + 22] = 1; font[parent + 23] = instruction;
                }
            }
            return font;
        }
    }
}
