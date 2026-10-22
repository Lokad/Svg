namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public void Oversized_unused_filter_and_marker_values_release_staging_and_allow_retry()
    {
        string value = new string(' ', 257) + "0";
        foreach (string resource in new[]
        {
            "<filter id='f' height='" + value + "'><feFlood/></filter>",
            "<filter id='f'><feFlood height='" + value + "'/></filter>",
            "<marker id='m' orient='" + value + "'/>",
            "<marker id='m' orient='" + value.Replace("0", "auto", StringComparison.Ordinal) + "'/>"
        })
        {
            byte[] input = Input(resource);
            Lokad.Svg.Rendering.RenderBudget budget = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Lokad.Svg.Parsing.SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            using (Lokad.Svg.Parsing.SvgRoot root = Lokad.Svg.Parsing.SvgRootParser.Parse(Input(""), SvgRenderLimits.Default, budget, CancellationToken.None)) Assert.False(root.HasUnsupportedChild);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }

        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + content + "</defs></svg>");
    }

    public static IEnumerable<object[]> FilterRegionTokenLimits()
    {
        foreach (bool primitive in new[] { false, true })
        foreach (string axis in new[] { "x", "y", "width", "height" })
        foreach (string unit in new[] { "", "px", "%" })
        foreach (bool leading in new[] { false, true })
        foreach (int length in new[] { 256, 257 }) yield return [primitive, axis, unit, leading, length];
    }

    [Theory]
    [MemberData(nameof(FilterRegionTokenLimits))]
    public async Task Filter_region_limits_apply_to_authored_lengths_before_trimming(bool primitive, string axis, string unit, bool leading, int length)
    {
        string number = "0" + unit;
        string padding = new(' ', length - number.Length);
        string value = leading ? padding + number : number + padding;
        await AssertAuthoredValueLimit(Input(value), Input(number), length > 256);

        byte[] Input(string token)
        {
            string attributes = "x='0' y='0' width='16' height='16'";
            attributes = attributes.Replace(axis + "='" + (axis is "x" or "y" ? "0" : "16") + "'", axis + "='" + token + "'", StringComparison.Ordinal);
            string filter = primitive ? "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='16' height='16'><feFlood flood-color='red' " + attributes + "/></filter>"
                : "<filter id='f' filterUnits='userSpaceOnUse' " + attributes + "><feFlood flood-color='red'/></filter>";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + filter + "</defs><rect width='16' height='16' filter='url(#f)'/></svg>");
        }
    }

    public static IEnumerable<object[]> MarkerOrientationTokenLimits()
    {
        foreach (string orient in new[] { "0", "90deg", "auto", "auto-start-reverse" })
        foreach (bool leading in new[] { false, true })
        foreach (int length in new[] { 256, 257 }) yield return [orient, leading, length];
    }

    [Theory]
    [MemberData(nameof(MarkerOrientationTokenLimits))]
    public async Task Marker_orientation_limits_apply_before_trimming_or_keyword_selection(string orient, bool leading, int length)
    {
        string padding = new(' ', length - orient.Length);
        string value = leading ? padding + orient : orient + padding;
        await AssertAuthoredValueLimit(Input(value), Input(orient), length > 256);

        static byte[] Input(string token) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='3' markerHeight='3' orient='" + token + "'><rect width='3' height='3' fill='red'/></marker></defs><path d='M8 8H24' marker-start='url(#m)'/></svg>");
    }

    private static async Task AssertAuthoredValueLimit(byte[] input, byte[] control, bool exceedsLimit)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        if (exceedsLimit)
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
            SvgException error = await Assert.ThrowsAsync<SvgException>(async () =>
            {
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                _ = prepared.ConvertToPng(CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        }
        else
        {
            byte[] expected = Render(control);
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
    }
}
