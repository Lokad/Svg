using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class SvgGlyphLcdCoverageTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("seed")]
    [InlineData("chord")]
    [InlineData("control-x--2")]
    [InlineData("control-x--1")]
    [InlineData("control-x-1")]
    [InlineData("control-x-2")]
    [InlineData("control-y--2")]
    [InlineData("control-y--1")]
    [InlineData("control-y-1")]
    [InlineData("control-y-2")]
    [InlineData("reverse")]
    [InlineData("translated-x")]
    [InlineData("translated-y")]
    [InlineData("mirror-x")]
    [InlineData("mirror-y")]
    [InlineData("mirror-xy")]
    [InlineData("swap-axes")]
    [InlineData("mirror-x-reverse")]
    [InlineData("mirror-y-reverse")]
    [InlineData("scale-two")]
    [InlineData("scale-two-reverse")]
    public void Implicit_conic_centers_keep_native_coverage_independently_of_direction(string identity)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "glyph-conic-centers", "native-centers.json")));
        Assert.Equal(2048, document.RootElement.GetProperty("unitsPerEm").GetInt32());
        Assert.Equal(32, document.RootElement.GetProperty("em").GetInt32());
        JsonElement row = document.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("id").GetString() == identity);
        OpenTypeQuadraticSegment[] segments = row.GetProperty("segments").EnumerateArray()
            .Select(s => new OpenTypeQuadraticSegment(Point(s.GetProperty("start")), Point(s.GetProperty("control")), Point(s.GetProperty("end")))).ToArray();
        byte[] expected = Convert.FromBase64String(row.GetProperty("alphaBase64").GetString() ?? throw new InvalidDataException());
        JsonElement window = document.RootElement.GetProperty("sampleWindow");
        int left = window.GetProperty("x").GetInt32(), top = window.GetProperty("y").GetInt32();
        int columns = window.GetProperty("width").GetInt32(), rows = window.GetProperty("height").GetInt32();
        Assert.Equal(columns * rows, expected.Length);
        Span<byte> preblend = stackalloc byte[256];
        SvgFontMaskPreblend.BuildWindowsGrayTable(preblend, new(0, 0, 0, 255), CancellationToken.None);
        // The independently captured em-32 instruction-free Windows face
        // selects simple dropout control; it matters to one translated cap.
        SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(segments, false, true, false, SvgGlyphLcdMode.Symmetric, CancellationToken.None);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        RenderBudget budget = new();
        using (SvgGlyphLcdCoverage.ScanStorage storage = SvgGlyphLcdCoverage.ScanStorage.TryCreate(segments, SvgGlyphLcdMode.Symmetric, budget, CancellationToken.None) ?? throw new InvalidOperationException())
        {
            Span<int> counts = stackalloc int[10];
            for (int y = 0; y < rows; y++)
            {
                SvgGlyphLcdCoverage.ScanRow scan = storage.PrepareRow(segments, top + y, counts, CancellationToken.None);
                for (int x = 0; x < columns; x++)
                {
                    byte alpha = expected[y * columns + x];
                    Assert.Equal(alpha, preblend[SvgGlyphLcdCoverage.GetCoverage(segments, left + x, top + y, scratch, bounds, CancellationToken.None).Mean]);
                    Assert.Equal(alpha, preblend[SvgGlyphLcdCoverage.GetCoverage(scan, left + x, scratch, bounds, CancellationToken.None).Mean]);
                }
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static OpenTypeOutlinePoint Point(JsonElement point) => new(8 + point[0].GetInt32() / 64d, 48 - point[1].GetInt32() / 64d);
    }
}
