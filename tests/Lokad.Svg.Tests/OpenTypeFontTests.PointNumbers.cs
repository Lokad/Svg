namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("polygon")]
    [InlineData("polyline")]
    public async Task Point_lists_use_bare_numbers_in_every_geometry_consumer(string element)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (string context in new[] { "direct", "use", "symbol", "pattern", "stroke", "marker", "gradient" })
        {
            byte[] control = Input("1", context);
            byte[] expected = Render(control);
            Assert.NotEqual(Render(Document("")), expected);
            foreach (string valid in new[] { "+1", "01", "1.0", "1e0", ".1e1" })
                Assert.Equal(expected, Render(Input(valid, context)));
            foreach (string invalid in new[] { "1px", "1PX", "1.0px", "1e0px", "1%", "1pt", "1\u2003", "\u20031" })
            {
                byte[] input = Input(invalid, context);
                Assert.Throws<SvgException>(() => Render(input));
                await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
            }
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(control, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }

        byte[] Input(string number, string context)
        {
            string paint = context == "stroke" ? "fill='none' stroke='red' stroke-width='2'"
                : context == "gradient" ? "fill='url(#g)'" : "fill='red'";
            string node = "<" + element + " points='" + number + " 1 7 1 7 7 1 7' " + paint;
            string body = context switch
            {
                "use" => "<defs>" + node + " id='s'/></defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='s'>" + node + "/></symbol></defs><use href='#s'/>",
                "pattern" => "<defs><pattern id='s' width='8' height='8' patternUnits='userSpaceOnUse'>" + node + "/></pattern></defs><rect width='8' height='8' fill='url(#s)'/>",
                "marker" => "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='2' markerHeight='2'><rect width='2' height='2'/></marker></defs>" + node + " marker-end='url(#m)'/>",
                "gradient" => "<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs>" + node + "/>",
                _ => node + "/>"
            };
            return Document(body);
        }
    }
}
