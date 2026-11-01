using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filled_direct_and_path_positions_keep_the_native_scaler_baseline(bool onPath)
    {
        string text = onPath
            ? "<defs><path id='p' d='M42 162H160'/></defs><text font-family='Example' font-size='12'><textPath href='#p'>A</textPath></text>"
            : "<text font-family='Example' font-size='12' x='42' y='162'>A</text>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='150' viewBox='0 0 480 360'><g transform='scale(1.8)'>" + text + "</g></svg>");
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(ReadKerningFlagFont("GposMarks"))], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None,
            SvgAffineTransform.FromScaleOffset(5d / 12, 5d / 12, 50, 0), null))
        {
            SvgGlyphRasterSource source = Assert.IsType<SvgGlyphRasterSource>(Assert.Single(scene.Geometries).Glyph.Source);
            if (!source.HasGlyphMap)
            {
                Assert.Equal(SvgAffineTransform.Identity, source.GlyphMap);
                // This float lies above the old 291.59999227523804 baseline.
                // Native font size was already 9 before this correction.
                // GposMarks has descent 128/1024: 21.6 rounds to 3 here.
                Assert.Equal((double)(float)291.6, source.LayoutBounds(SvgAffineTransform.Identity, CancellationToken.None).Bottom - 3);
                Assert.Equal(9, source.DeviceFontSize(5d / 12));
            }
            // Both source graphs must reach the same actual fitted outline and
            // origin as the independently browser-backed flat control. Do not
            // require a particular intermediate path-map representation.
            using SvgGlyphRasterSource flat = SvgGlyphRasterSource.Create(Assert.Single(fonts.Fonts), new(2, 81.5, 121.5, 0), 9, budget);
            using OpenTypeGlyphContourStorage expected = flat.CreateWindowsFittedContours(1, 0, 1, 0, budget, CancellationToken.None);
            float rasterScale = (float)(5d / 12);
            using OpenTypeGlyphContourStorage actual = source.CreateWindowsFittedContours(rasterScale, 50, rasterScale, 0, budget, CancellationToken.None);
            Assert.Equal(expected.Segments.ToArray(), actual.Segments.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Glyph_drawing_metadata_is_admitted_before_transform_and_transfers_through_retry()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(fonts.Fonts);
        using OpenTypeGlyphContourStorage decoded = OpenTypeFont.GetGlyphContours(font.Bytes, 1, font.Metrics, new RenderBudget(new HostMemoryBudget(10_000)), CancellationToken.None);
        // This single-contour, three-segment fixture previously needed 224 bytes.
        // Its independent affine drawing binding retains another 48 bytes.
        using HostMemoryBudget host = new(224);
        RenderBudget budget = new(host);
        bool transformed = false;
        using (GlyphOutlineProbe.Arm(() => transformed = true))
        {
            SvgException error = Assert.Throws<SvgException>(() => SvgGlyphOutline.GetContourStorage(font, new(1, 2, 3, 1), 100, decoded, budget, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        }
        Assert.False(transformed);
        Assert.Equal(0, host.Reserved);
        host.Limit = 272;
        using (SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(font, new(1, 2, 3, 1), 100, decoded, budget, CancellationToken.None))
        using (SvgGlyphGeometry retained = SvgGlyphGeometry.Create(contours, new SvgColor(0, 0, 0, 255)))
        {
            Assert.Equal(272, host.Reserved);
            Assert.Equal(3, Assert.Single(retained.Contours).Segments.Length);
            Assert.Equal(3, decoded.Segments.Length);
        }
        Assert.Equal(0, host.Reserved);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_root_recording_keeps_one_acquisition_and_the_flat_control_baseline()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='300' height='150' viewBox='0 0 480 360'><g transform='scale(1.8)'><text font-family='Example' font-size='12' x='42' y='162'>AVTo</text></g></svg>"u8.ToArray();
        byte[] flat = "<svg xmlns='http://www.w3.org/2000/svg' width='300' height='150'><text font-family='Example' font-size='9' x='81.5' y='121.5'>AVTo</text></svg>"u8.ToArray();
        byte[] program = ReadKerningFlagFont("GposMarks");
        (var catalog, var provider, var source) = CreateExamplePack(program);
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(flat, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }
}
