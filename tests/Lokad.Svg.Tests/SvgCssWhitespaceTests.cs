using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgCssWhitespaceTests
{
    [Theory]
    [MemberData(nameof(SvgCssSelectorTests.ClassSeparators), MemberType = typeof(SvgCssSelectorTests))]
    public void Selector_boundaries_preserve_non_ascii_identifier_characters(int character, bool whitespace)
    {
        string separator = char.ConvertFromUtf32(character);
        foreach (string selector in new[] { "g" + separator + "rect", separator + "rect", "rect" + separator,
            ".outer" + separator + ".hot", "rect[id" + separator + "=" + separator + "'box']" })
        foreach (bool retained in new[] { false, true })
        {
            string content = "<g class='outer'><rect id='box' class='hot' width='8' height='8'/></g>";
            if (retained) content = "<defs><symbol id='s'>" + content + "</symbol></defs><use href='#s'/>";
            string input = "<style>" + selector + "{fill:red}</style>" + content;
            if (whitespace) Assert.Equal(Render("<rect width='8' height='8' fill='red'/>"), Render(input));
            else Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
        }
    }

    private static byte[] Render(string body)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>" + body + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
