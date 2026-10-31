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
    public async Task Filled_positions_record_the_combined_scale_without_changing_the_path_strike(bool onPath)
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
            if (onPath)
            {
                // A horizontal path retains the element map and its separate
                // strike, with the same recorded element scale as direct fill.
                Assert.Equal(1.8, source.GlyphMap.A, 12);
                Assert.Equal(1.8, source.GlyphMap.D, 12);
            }
            else
            {
                Assert.Equal(SvgAffineTransform.Identity, source.GlyphMap);
                // This float lies above the old 291.59999227523804 baseline.
                // Native font size was already 9 before this correction.
                // GposMarks has descent 128/1024: 21.6 rounds to 3 here.
                Assert.Equal((double)(float)291.6, source.LayoutBounds(SvgAffineTransform.Identity, CancellationToken.None).Bottom - 3);
                Assert.Equal(9, source.DeviceFontSize(5d / 12));
                using SvgGlyphRasterSource flat = SvgGlyphRasterSource.Create(Assert.Single(fonts.Fonts), new(2, 81.5, 121.5, 0), 9, budget);
                using OpenTypeGlyphContourStorage expected = flat.CreateWindowsContours(1, 0, 1, 0, budget, CancellationToken.None);
                float rasterScale = (float)(5d / 12);
                using OpenTypeGlyphContourStorage actual = source.CreateWindowsContours(rasterScale, 50, rasterScale, 0, budget, CancellationToken.None);
                Assert.Equal(expected.Segments.ToArray(), actual.Segments.ToArray());
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
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
