namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("100%,0,0")]
    [InlineData("255,0%,0")]
    [InlineData("255,0,0%")]
    [InlineData("100%,0%,0")]
    [InlineData("100%,0,0%")]
    [InlineData("255,0%,0%")]
    public async Task Legacy_rgb_channels_use_a_single_unit_grammar(string channels)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (string function in new[] { "rgb", "rgba" })
        {
            string value = function + "(" + channels + (function == "rgba" ? ",1" : "") + ")";
            foreach (string location in new[] { "attribute", "inline", "rule", "stroke", "stop", "flood" })
            {
                byte[] input = Input(location, value);
                Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
                SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
                Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
            }

            string modern = function + "(" + channels.Replace(',', ' ') + (function == "rgba" ? "/1" : "") + ")";
            foreach (string location in new[] { "attribute", "inline", "rule", "stroke", "stop", "flood" })
            {
                byte[] expected = Render(Input(location, "red"));
                Assert.Equal(expected, Render(Input(location, modern)));
                using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(Input(location, modern), catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
                Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
            }
        }

        byte[] Input(string location, string value)
        {
            string body = location switch
            {
                "inline" => "<rect width='8' height='8' style='fill:" + value + "'/>",
                "rule" => "<style>rect{fill:" + value + "}</style><rect width='8' height='8'/>",
                "stroke" => "<path d='M0 4H8' stroke-width='4' stroke='" + value + "'/>",
                "stop" => "<defs><linearGradient id='p'><stop stop-color='" + value + "'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>",
                "flood" => "<defs><filter id='f'><feFlood flood-color='" + value + "'/></filter></defs><rect width='8' height='8' filter='url(#f)'/>",
                _ => "<rect width='8' height='8' fill='" + value + "'/>"
            };
            return Document(body);
        }
    }

    [Theory]
    [InlineData("hsl(0 100% 50%)")]
    [InlineData("hsla(0 100% 50%)")]
    [InlineData("hsla(0 100% 50%/1)")]
    [InlineData("rgb(255 0 0)")]
    [InlineData("rgba(255 0 0)")]
    [InlineData("rgba(255 0 0/1)")]
    [InlineData("rgb(100%,0%,0%)")]
    [InlineData("rgba(100%,0%,0%,1)")]
    public async Task Modern_color_function_aliases_have_optional_alpha(string value)
    {
        byte[] control = Document("<rect width='8' height='8' fill='red'/>");
        byte[] input = Document("<rect width='8' height='8' fill='" + value + "'/>");
        byte[] expected = Render(control);
        Assert.Equal(expected, Render(input));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }
}
