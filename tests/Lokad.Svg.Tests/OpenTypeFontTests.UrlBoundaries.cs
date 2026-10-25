namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("fill")]
    [InlineData("stroke")]
    [InlineData("clip-path")]
    [InlineData("mask")]
    [InlineData("filter")]
    [InlineData("marker-end")]
    public async Task Resource_urls_do_not_trim_characters_into_a_different_fragment(string property)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (string context in new[] { "direct", "use", "symbol", "pattern" })
            foreach (string location in property is "clip-path" or "mask" ? new[] { "attribute" } : new[] { "attribute", "inline", "rule" })
            {
                byte[] expected = Render(Input("url(#p)", location, context));
                Assert.NotEqual(Render(Document("")), expected);
                foreach (string valid in new[] { "url( '#p' )", "url('#p ')", "URL(#p )" })
                {
                    byte[] input = Input(valid, location, context);
                    Assert.Equal(expected, Render(input));
                    using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                    Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
                }
                foreach (string invalid in new[] { "url('# p')", "url(' #p')", "url(# p)" })
                {
                    byte[] input = Input(invalid, location, context);
                    Assert.True(Record.Exception(() => Render(input)) is SvgException, context + ":" + location + ":" + invalid);
                    await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
                }
            }

        byte[] Input(string url, string location, string context)
        {
            string resource = property switch
            {
                "clip-path" => "<clipPath id='p'><rect width='4' height='8'/></clipPath>",
                "mask" => "<mask id='p' mask-type='alpha'><rect width='4' height='8' fill='white'/></mask>",
                "filter" => "<filter id='p'><feFlood flood-color='red'/></filter>",
                "marker-end" => "<marker id='p' markerUnits='userSpaceOnUse' markerWidth='2' markerHeight='2'><rect width='2' height='2' fill='red'/></marker>",
                _ => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='8' y2='0'><stop stop-color='red'/></linearGradient>"
            };
            string value = url.Replace("'", "&quot;", StringComparison.Ordinal);
            string declaration = location == "rule" ? "<style>.target{" + property + ":" + value + "}</style>" : "";
            string attributes = location == "inline" ? "style='" + property + ":" + value + "'" : location == "rule" ? "class='target'" : property + "='" + value + "'";
            string node = property is "stroke" or "marker-end" ? "<path d='M0 4H6' stroke-width='2' " + (property == "marker-end" ? "stroke='blue' " : "") : "<rect width='8' height='8' ";
            node += attributes;
            string body = context switch
            {
                "use" => "<defs>" + node + " id='s'/></defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='s'>" + node + "/></symbol></defs><use href='#s'/>",
                "pattern" => "<defs><pattern id='s' patternUnits='userSpaceOnUse' width='8' height='8'>" + node + "/></pattern></defs><rect width='8' height='8' fill='url(#s)'/>",
                _ => node + "/>"
            };
            return Document(declaration + "<defs>" + resource + "</defs>" + body);
        }
    }
}
