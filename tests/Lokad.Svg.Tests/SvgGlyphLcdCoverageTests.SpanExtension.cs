using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [InlineData(false, false, 8, 170, 255, 170)]
    [InlineData(false, true, 8, 170, 255, 213)]
    [InlineData(false, false, 9, 85, 0, 0)]
    [InlineData(false, true, 9, 128, 43, 0)]
    [InlineData(true, false, 8, 170, 255, 170)]
    [InlineData(true, true, 8, 170, 255, 213)]
    [InlineData(true, false, 9, 85, 0, 0)]
    [InlineData(true, true, 9, 128, 43, 0)]
    public void Source_family_span_extension_matches_native_in_both_LCD_profiles(bool symmetric, bool extend, int x, byte red, byte green, byte blue)
    {
        // Original CC0 rectangles, independently captured with DirectWrite:
        // UPEM 1024, em 16, origin (8,256), points (0,64)..(64,192).
        OpenTypeQuadraticSegment[] segments = Rectangle(8, 253, 9, 255);
        SvgGlyphLcdMode mode = symmetric ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural;
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, false, false, false, mode, CancellationToken.None) with { ExtendRight = extend };
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[8];
        SvgGlyphRgbCoverage expected = new(red, green, blue);
        Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(segments, x, 254, scratch, bounds, CancellationToken.None));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, mode, budget, CancellationToken.None) ?? throw new InvalidOperationException())
        {
            Span<int> counts = stackalloc int[10];
            Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(storage.PrepareRow(segments, 254, counts, CancellationToken.None), x, scratch, bounds, CancellationToken.None));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_span_extension_does_not_create_ink_in_an_empty_subpixel_interval(bool symmetric)
    {
        // The independent one-font-unit rectangle stays empty with SCANTYPE 2;
        // native widening extends set cells rather than geometric bounds.
        OpenTypeQuadraticSegment[] segments = Rectangle(8, 253, 8 + 1d / 64, 255);
        SvgGlyphLcdMode mode = symmetric ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural;
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, false, false, false, mode, CancellationToken.None) with { ExtendRight = true };
        for (int x = 7; x <= 9; x++)
            Assert.Equal(default(SvgGlyphRgbCoverage), SvgGlyphLcdCoverage.GetCoverage(segments, x, 254, new SvgGlyphLcdCoverage.Crossing[8], bounds, CancellationToken.None));
    }
}
