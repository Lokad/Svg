using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, "HAgJuP8=")]
    [InlineData(.25, "JRAAbP8=")]
    [InlineData(.5, "LBMBSP4=")]
    [InlineData(.75, "QBcFF9c=")]
    public async Task Windows_scan_curves_preserve_fitted_turns_and_match_native_phase_alpha(double phase, string nativeAlpha)
    {
        // Original ScanTurn.ttf; independent three-process Windows vectors are
        // recorded in windows-scan-boundary/native-alpha.json. Only the numerical
        // phase varies here; the SVG fixture uses one canonical conversion.
        byte[] program = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-scan-boundary", "ScanTurn.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(fonts.Fonts), new(2, 8 + phase, 30, 0), 32, budget))
        using (OpenTypeGlyphContourStorage fitted = source.CreateWindowsFittedContours(1, 0, 1, 0, budget, CancellationToken.None))
        using (OpenTypeGlyphContourStorage scan = source.CreateWindowsScanContours(1, 0, 1, 0, budget, CancellationToken.None))
        {
            Assert.Contains(fitted.Segments.ToArray(), TurnsVertically);
            Assert.DoesNotContain(scan.Segments.ToArray(), TurnsVertically);
            Assert.Equal(fitted.WindowsLcdMode, scan.WindowsLcdMode);
            Assert.Equal(fitted.SimpleDropouts, scan.SimpleDropouts);
            SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(scan.Segments, scan.SmartDropouts,
                scan.SimpleDropouts, scan.ExcludeDropoutStubs, scan.WindowsLcdMode, CancellationToken.None);
            int count = scan.Segments.Length * 2;
            using RenderBudget.BudgetReservation scratchOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphLcdCoverage.Crossing>(count), SvgMemoryCategory.Temporary);
            SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[count];
            Span<byte> preblend = stackalloc byte[256];
            SvgFontMaskPreblend.BuildWindowsGrayTable(preblend, new(0, 0, 0, 255), CancellationToken.None);
            byte[] expected = Convert.FromBase64String(nativeAlpha);
            for (int index = 0; index < expected.Length; index++)
            {
                SvgGlyphRgbCoverage coverage = SvgGlyphLcdCoverage.GetCoverage(scan.Segments, 16 + index, 28, scratch, bounds, CancellationToken.None);
                Assert.Equal(expected[index], preblend[coverage.Mean]);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static bool TurnsVertically(OpenTypeQuadraticSegment segment)
            => segment.Control.Y > Math.Max(segment.Start.Y, segment.End.Y)
                || segment.Control.Y < Math.Min(segment.Start.Y, segment.End.Y);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_scan_failure_releases_fitted_temporary_and_allows_retry(bool cancel)
    {
        byte[] program = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-scan-boundary", "ScanTurn.ttf"));
        byte[] unchanged = program.ToArray();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        using HostMemoryBudget host = new(long.MaxValue);
        RenderBudget budget = new(host);
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(fonts.Fonts), new(2, 8.25, 30, 0), 32, budget))
        {
            long retained = host.Reserved;
            using CancellationTokenSource cancellation = new();
            bool observed = false;
            using (GlyphDecoderBudgetProbe.Arm((phase, _) =>
            {
                if (phase != GlyphDecoderBudgetPhase.ExpandedSegmentCapacity || observed) return;
                observed = true;
                if (cancel) cancellation.Cancel();
                else host.Limit = host.Reserved;
            }))
            {
                if (cancel)
                    Assert.ThrowsAny<OperationCanceledException>(() => source.CreateWindowsScanContours(1, 0, 1, 0, budget, cancellation.Token));
                else
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => source.CreateWindowsScanContours(1, 0, 1, 0, budget, CancellationToken.None)).ErrorCode);
            }
            Assert.True(observed);
            Assert.Equal(retained, host.Reserved);
            Assert.Equal(cancel ? 0 : 1, budget.Snapshot().DeniedReservationCount);
            host.Limit = long.MaxValue;
            using (OpenTypeGlyphContourStorage retry = source.CreateWindowsScanContours(1, 0, 1, 0, budget, CancellationToken.None))
            {
                Assert.NotEmpty(retry.Segments.ToArray());
                Assert.True(host.Reserved > retained);
            }
            Assert.Equal(retained, host.Reserved);
        }
        Assert.Equal(0, host.Reserved);
        Assert.Equal(unchanged, program);
    }
}
