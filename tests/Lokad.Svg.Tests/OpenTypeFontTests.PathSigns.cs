namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("M4 4H7V7H4Z")]
    [InlineData("M4 4L7 7H4Z")]
    [InlineData("M4 4Q5 1 7 4V7H4Z")]
    [InlineData("M4 4A2 2 0 0 1 7 4V7H4Z")]
    public async Task Integer_path_optimization_keeps_valid_leading_plus_signs(string data)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (string context in new[] { "direct", "use", "symbol", "pattern", "clip" })
        {
            byte[] control = Input(data, context);
            byte[] expected = Render(control);
            Assert.NotEqual(Render(Document("")), expected);
            foreach (string number in new[] { "+4", "+04", "+4.0", "+4e0", "+.4e1" })
            {
                byte[] input = Input(data.Replace("M4", "M" + number, StringComparison.Ordinal), context);
                Assert.Equal(expected, Render(input));
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
            }
        }

        byte[] Input(string path, string context)
        {
            string node = "<path d='" + path + "' fill='red'";
            string body = context switch
            {
                "use" => "<defs>" + node + " id='s'/></defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='s'>" + node + "/></symbol></defs><use href='#s'/>",
                "pattern" => "<defs><pattern id='s' width='8' height='8' patternUnits='userSpaceOnUse'>" + node + "/></pattern></defs><rect width='8' height='8' fill='url(#s)'/>",
                "clip" => "<defs><clipPath id='c'>" + node + "/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#c)'/>",
                _ => node + "/>"
            };
            return Document(body);
        }
    }
}
