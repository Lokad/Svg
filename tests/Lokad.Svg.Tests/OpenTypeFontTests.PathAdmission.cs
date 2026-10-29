using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> RepeatedPathCloses()
    {
        foreach (string suffix in new[] { "Z", "ZZ", "z Zz", "Z M16 16", "Z L24 8Z" })
        foreach (string scope in new[] { "direct", "use", "symbol" }) yield return [suffix, scope];
    }

    [Theory]
    [MemberData(nameof(RepeatedPathCloses))]
    public async Task Redundant_closes_follow_general_path_semantics_without_changing_paint(string suffix, string scope)
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        foreach (string paint in new[] { "red", "url(#l)", "url(#r)", "url(#p)" })
        foreach (string style in new[] { "fill='" + paint + "'", "fill='none' stroke='" + paint + "' stroke-width='3' stroke-linecap='round'", "fill='" + paint + "' stroke='blue' stroke-width='2' stroke-dasharray='3 2'" })
        {
            byte[] input = Svg("M8 8H24V24H8Z" + suffix);
            string control = "M8 8H24V24H8Z" + (suffix.Contains('M') ? " M16 16" : suffix.Contains('L') ? " L24 8Z" : "");
            byte[] expected = Render(Svg(control));
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

            byte[] Svg(string path)
            {
                const string paints = "<linearGradient id='l'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='r'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='4' fill='red'/></pattern>";
                string shape = "<path id='s' d='" + path + "' " + style + "/>";
                string body = scope switch
                {
                    "use" => "<defs>" + paints + shape + "</defs><use href='#s'/>",
                    "symbol" => "<defs>" + paints + "<symbol id='q'>" + shape + "</symbol></defs><use href='#q'/>",
                    _ => "<defs>" + paints + "</defs>" + shape
                };
                return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
            }
        }
        Assert.Equal(0, provider.RequestCount);
        static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("ZZ")]
    [InlineData("Z M16 16")]
    [InlineData("Z L24 8Z")]
    public void Repeated_closes_preserve_authored_markers_across_numeric_spellings(string suffix)
    {
        string path = "M8 8H24V24H8Z" + suffix;
        string decimalPath = System.Text.RegularExpressions.Regex.Replace(path, "[0-9]+", match => match.Value + ".0");
        const string marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='3' markerHeight='3' orient='auto' overflow='visible'><path d='M0 0L3 1L0 3Z' fill='blue'/></marker></defs>";
        byte[] Svg(string data) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + marker
            + "<path d='" + data + "' fill='red' stroke='black' stroke-width='1' marker-start='url(#m)' marker-mid='url(#m)' marker-end='url(#m)'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(Svg(decimalPath), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(Svg(path), SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Repeated_closes_keep_original_command_limits_work_and_storage_lifetimes()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='M8 8H24V24H8Z" + new string('Z', 100) + "'/></svg>");
        RenderBudget measured = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None)) Assert.False(root.HasUnsupportedChild);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        SvgRenderLimits d = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(d.MaximumAccountedBytes, d.MaximumInputBytes, d.MaximumDimension, d.MaximumPixels,
            d.MaximumEncodedPngBytes, d.MaximumNodes, 8, d.MaximumXmlDepth, d.MaximumAttributesPerElement);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, measured, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, cancellation.Token));
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }
}
