using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public void Selected_component_metrics_require_device_replay_without_changing_unhinted_geometry()
    {
        byte[] bytes = CreateTriangleFont();
        OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(bytes, 2, CancellationToken.None);
        WriteInt16(bytes, range.Offset, -1);
        WriteUInt16(bytes, range.Offset + 10, 515);
        WriteUInt16(bytes, range.Offset + 12, 1);
        WriteInt16(bytes, range.Offset + 14, 0);
        WriteInt16(bytes, range.Offset + 16, 0);
        Assert.Equal(OpenTypeFont.GetGlyphContours(bytes, 1, CancellationToken.None), OpenTypeFont.GetGlyphContours(bytes, 2, CancellationToken.None));
        byte[] unchanged = bytes.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        Assert.True(OpenTypeFont.NeedsCompositeDeviceFitting(bytes, 2, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), budget, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);
    }

    [Fact]
    public void Rounded_composite_offsets_are_metadata_for_device_fitting_and_preserve_unhinted_expansion()
    {
        byte[] bytes = CreateTriangleFont();
        OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(bytes, 2, CancellationToken.None);
        WriteInt16(bytes, range.Offset, -1);
        WriteUInt16(bytes, range.Offset + 10, 7);
        WriteUInt16(bytes, range.Offset + 12, 1);
        WriteInt16(bytes, range.Offset + 14, 3);
        WriteInt16(bytes, range.Offset + 16, -4);
        OpenTypeCompositeGlyphComponent component = Assert.Single(OpenTypeFont.GetCompositeGlyphComponents(bytes, 2, CancellationToken.None));
        Assert.Equal((ushort)7, component.Flags);
        OpenTypeQuadraticSegment first = OpenTypeFont.GetGlyphContours(bytes, 2, CancellationToken.None)[0];
        OpenTypeQuadraticSegment source = OpenTypeFont.GetGlyphContours(bytes, 1, CancellationToken.None)[0];
        Assert.Equal(source.Start.X + 3, first.Start.X);
        Assert.Equal(source.Start.Y - 4, first.Start.Y);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        Assert.True(OpenTypeFont.NeedsCompositeDeviceFitting(bytes, 2, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), budget, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Composite_point_assembly_rejects_cycles_and_releases_child_owners()
    {
        byte[] bytes = CreateTriangleFont();
        OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(bytes, 2, CancellationToken.None);
        WriteInt16(bytes, range.Offset, -1);
        WriteUInt16(bytes, range.Offset + 10, 7);
        WriteUInt16(bytes, range.Offset + 12, 2);
        WriteInt16(bytes, range.Offset + 14, 0);
        WriteInt16(bytes, range.Offset + 16, 0);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetLatinLightContours(bytes, 2, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 32, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("0.01")]
    public async Task Device_hinting_collapse_renders_an_empty_glyph(string fontSize)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='" + fontSize + "' y='3'>A</text></svg>");
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
            Assert.Equal(0, DecodeAlpha(png, x, y));
    }

    [Fact]
    public async Task Linux_simple_hint_stage_preserves_previously_admitted_composite_rendering()
    {
        byte[] bytes = CreateTriangleFont();
        OpenTypeTableRange composite = OpenTypeFont.GetGlyphDataRange(bytes, 2, CancellationToken.None);
        WriteInt16(bytes, composite.Offset, -1);
        WriteUInt16(bytes, composite.Offset + 10, 3);
        WriteUInt16(bytes, composite.Offset + 12, 1);
        WriteInt16(bytes, composite.Offset + 14, 0);
        WriteInt16(bytes, composite.Offset + 16, 0);
        int cmap = FindTableOffset(bytes, 0x636D6170);
        WriteInt16(bytes, cmap + 12 + 24, -63); // A maps to the admitted composite.
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        var budget = new RenderBudget(new HostMemoryBudget(10_000));
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(font, new(2, 0, 3, 0), 100, budget))
            Assert.False(source.HasSimpleOutline(CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.True(SvgConverter.ConvertToPng(input, options, CancellationToken.None).Length > 0);
    }
}
