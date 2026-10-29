using System.Buffers.Binary;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(2, false, false)]
    [InlineData(4, true, false)]
    [InlineData(5, true, true)]
    public void Windows_unfitted_ranges_retain_authored_scanner_controls(byte scanType, bool smart, bool excludeStubs)
    {
        // Independent native controls retain SCANCTRL/SCANTYPE with fitting
        // disabled. Mode 2 removes thin stripes; modes 4/5 retain them.
        byte[] bytes = CreateUnfittedScannerFont([0xB8, 1, 255, 0x85, 0xB0, scanType, 0x8D], []);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 8, budget, CancellationToken.None))
        {
            Assert.Equal(smart, contours.SmartDropouts);
            Assert.False(contours.SimpleDropouts);
            Assert.Equal(excludeStubs, contours.ExcludeDropoutStubs);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Windows_unfitted_scanner_evaluation_discards_fitted_point_movements()
    {
        byte[] plain = CreateUnfittedScannerFont([], []);
        // Prep chooses smart scanning; the glyph moves point zero to Y=256.
        // Native disabled/default analyses retain the original outline while
        // the explicitly fitted analysis paints the moved band.
        byte[] bytes = CreateUnfittedScannerFont([0x00, 0xB0, 0, 0xB8, 1, 0, 0x48], [0xB8, 1, 255, 0x85, 0xB0, 4, 0x8D]);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        using (OpenTypeGlyphContourStorage expected = OpenTypeFont.GetWindowsUnfittedContours(plain, 1, OpenTypeFont.ParseMetrics(plain, CancellationToken.None), 8, budget, CancellationToken.None))
        using (OpenTypeGlyphContourStorage actual = OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, metrics, 8, budget, CancellationToken.None))
        using (OpenTypeGlyphContourStorage fitted = OpenTypeFont.GetTrueTypeContours(bytes, 1, metrics, 8, new OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode.Symmetric, true).CreateHintTarget(1), TrueTypeImpliedPointRounding.None, budget, CancellationToken.None))
        {
            Assert.Equal(expected.Segments, actual.Segments);
            Assert.NotEqual(actual.Segments[0].Start.Y, fitted.Segments[0].Start.Y);
            Assert.True(actual.SmartDropouts);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Windows_unfitted_scanner_search_skips_pushed_opcode_bytes()
    {
        // MPS remains irrelevant to this outline. Scanner-looking operand
        // bytes must not make the unsupported instruction necessary.
        byte[] bytes = CreateUnfittedScannerFont([0x4C, 0x40, 3, 0x85, 0x8D, 0x4C, 0x21], []);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 8, budget, CancellationToken.None))
            Assert.NotEmpty(contours.Segments);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Windows_unfitted_scanner_denial_releases_temporary_storage()
    {
        byte[] bytes = CreateUnfittedScannerFont([0xB8, 1, 255, 0x85, 0xB0, 4, 0x8D], []);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(256));
        SvgException error = Assert.Throws<SvgException>(() => OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 8, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] CreateUnfittedScannerFont(byte[] glyphProgram, byte[] prepProgram)
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("00010001FFFF000A"), true);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(bytes, 1, CancellationToken.None);
        Assert.True(glyph.Length >= 22 + glyphProgram.Length);
        Array.Copy(bytes, glyph.Offset + 14, bytes, glyph.Offset + 14 + glyphProgram.Length, 8);
        WriteUInt16(bytes, glyph.Offset + 12, (ushort)glyphProgram.Length);
        glyphProgram.CopyTo(bytes, glyph.Offset + 14);
        if (prepProgram.Length == 0) return bytes;

        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)), directoryEnd = 12 + count * 16;
        byte[] result = new byte[bytes.Length + 16 + prepProgram.Length];
        bytes.AsSpan(0, directoryEnd).CopyTo(result);
        bytes.AsSpan(directoryEnd).CopyTo(result.AsSpan(directoryEnd + 16));
        WriteUInt16(result, 4, (ushort)(count + 1));
        for (int index = 0; index < count; index++)
        {
            int at = 12 + index * 16;
            WriteUInt32(result, at + 8, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 8)) + 16);
        }
        WriteTable(result, count, 0x70726570, bytes.Length + 16, prepProgram.Length);
        prepProgram.CopyTo(result, bytes.Length + 16);
        return result;
    }
}
