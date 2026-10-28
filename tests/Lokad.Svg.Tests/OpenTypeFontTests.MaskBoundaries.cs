using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> MaskInstanceMaps()
    {
        foreach (string shape in new[] { "<rect width='16' height='16'", "<path d='M0 0H16V16H0Z'", "<path d='M0 8Q8 -4 16 8Q8 20 0 8Z'" })
        foreach (var map in new[] { ("translate(0 0)", true), ("translate(2.25 1.5)", true), ("rotate(15 12 12)", false), ("skewX(15)", false) })
            yield return [shape, map.Item1, map.Item2];
    }

    [Theory]
    [MemberData(nameof(MaskInstanceMaps))]
    public async Task Masked_shape_sources_require_axis_aligned_instances_but_live_use_masks_admit_affine_maps(string shape, string map, bool axisAligned)
    {
        const string mask = "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='24' height='24'><rect x='-16' y='-16' width='64' height='64' fill='white'/></mask>";
        foreach (string route in new[] { "leaf", "symbol", "live-use" })
        {
            string source = shape + " id='r'" + (route == "leaf" ? " mask='url(#m)'" : "") + "/>";
            if (route == "symbol") source += "<symbol id='s'>" + shape + " mask='url(#m)'/></symbol>";
            string body = "<use href='#" + (route == "symbol" ? "s" : "r") + "'" + (route == "live-use" ? " mask='url(#m)'" : "") + "/>";
            await AssertSupportAdmission(MaskBoundaryInput(mask + source, body, map), axisAligned || route == "live-use");
        }
    }

    [Theory]
    [MemberData(nameof(MaskInstanceMaps))]
    public async Task Axis_aligned_spatial_mask_fraction_regions_require_content_covering_the_explicit_region(string shape, string map, bool axisAligned)
    {
        foreach (bool coversRegion in new[] { false, true })
        {
            string mask = "<mask id='m' maskUnits='objectBoundingBox' x='0' y='0' width='1' height='1'><circle cx='8' cy='8' r='" + (coversRegion ? "16" : "6") + "' fill='white'/></mask>";
            await AssertSupportAdmission(MaskBoundaryInput(mask, shape + " mask='url(#m)'/>", map), coversRegion || !axisAligned);
        }
    }

    [Theory]
    [InlineData("translate(0 0)")]
    [InlineData("rotate(15 12 12)")]
    public async Task A_mask_on_a_text_use_is_distinct_from_direct_masked_text_and_a_masked_text_source(string map)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        const string mask = "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='24' height='24'><rect x='-16' y='-16' width='64' height='64' fill='white'/></mask>";
        const string text = "<text x='2' y='14' font-family='Example' font-size='200'";
        foreach (string route in new[] { "direct", "source", "live-use" })
        {
            string source = route == "direct" ? "" : text + " id='t'" + (route == "source" ? " mask='url(#m)'" : "") + ">A</text>";
            string body = route == "direct" ? text + " mask='url(#m)'>A</text>" : "<use href='#t'" + (route == "live-use" ? " mask='url(#m)'" : "") + "/>";
            byte[] input = MaskBoundaryInput(mask + source, body, map);
            using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
            byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    private static byte[] MaskBoundaryInput(string definitions, string body, string map)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs><g transform='" + map + "'>" + body + "</g></svg>");
}
