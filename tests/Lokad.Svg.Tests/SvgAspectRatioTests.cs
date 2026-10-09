namespace Lokad.Svg.Tests;

public sealed class SvgAspectRatioTests
{
    [Theory]
    [InlineData("xMinYMin")]
    [InlineData("xMidYMin")]
    [InlineData("xMaxYMin")]
    [InlineData("xMinYMid")]
    [InlineData("xMidYMid")]
    [InlineData("xMaxYMid")]
    [InlineData("xMinYMax")]
    [InlineData("xMidYMax")]
    [InlineData("xMaxYMax")]
    [InlineData("none")]
    public void Omitted_fit_matches_explicit_meet_in_root_viewport_nested_viewport_and_image(string alignment)
    {
        string[] documents =
        [
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 4 2' preserveAspectRatio='ASPECT'><rect width='4' height='2' fill='red'/></svg>",
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><svg x='1' y='1' width='6' height='6' viewBox='0 0 4 2' preserveAspectRatio='ASPECT'><rect width='4' height='2' fill='red'/></svg></svg>",
            "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='1' y='1' width='4' height='4' preserveAspectRatio='ASPECT'/></svg>"
        ];
        foreach (string document in documents)
        {
            byte[] implicitFit = System.Text.Encoding.UTF8.GetBytes(document.Replace("ASPECT", alignment, StringComparison.Ordinal));
            byte[] explicitFit = System.Text.Encoding.UTF8.GetBytes(document.Replace("ASPECT", alignment + " meet", StringComparison.Ordinal));
            Assert.Equal(SvgConverter.ConvertToPng(explicitFit, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(implicitFit, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        }
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("xMidYMid bogus")]
    [InlineData("xMidYMid meet slice")]
    [InlineData("defer xMidYMid meet")]
    public void Invalid_or_unimplemented_pattern_aspect_declarations_keep_an_explicit_outcome(string value)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse' preserveAspectRatio='" + value + "'><path d='M0 0h2v2H0z'/></pattern><rect width='8' height='8' fill='url(#p)'/></svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Xml_whitespace_separates_alignment_and_fit()
    {
        byte[] ordinary = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 4 2' preserveAspectRatio='xMaxYMax meet'><rect width='4' height='2' fill='red'/></svg>"u8.ToArray();
        byte[] spaced = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 4 2' preserveAspectRatio=' \t xMaxYMax \n  meet \r '><rect width='4' height='2' fill='red'/></svg>"u8.ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(ordinary, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(spaced, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }
}
