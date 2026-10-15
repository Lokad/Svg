using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed class SvgInstanceLengthBasesTests
{
    [Theory]
    [InlineData("leaf")]
    [InlineData("group")]
    [InlineData("symbol")]
    [InlineData("symbol-viewbox")]
    public void Relative_geometry_uses_the_live_instance_viewport(string kind)
    {
        const string oval = "<ellipse cx='50%' cy='50%' rx='20%' ry='30%' fill='#347bad'/>";
        string definition = kind switch
        {
            "leaf" => oval.Replace("<ellipse", "<ellipse id='shape'", StringComparison.Ordinal),
            "group" => "<g id='shape'>" + oval + "</g>",
            "symbol" => "<symbol id='shape'>" + oval + "</symbol>",
            "symbol-viewbox" => "<symbol id='shape' viewBox='0 0 20 12'>" + oval + "</symbol>",
            _ => throw new InvalidOperationException()
        };
        string instance = "<use href='#shape' x='12' y='14' width='40' height='24'/>";
        string inline = kind switch
        {
            "leaf" or "group" => "<g transform='translate(12 14)'>" + oval + "</g>",
            "symbol" => "<svg x='12' y='14' width='40' height='24'>" + oval + "</svg>",
            "symbol-viewbox" => "<svg x='12' y='14' width='40' height='24' viewBox='0 0 20 12'>" + oval + "</svg>",
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(Render(inline), Render("<defs>" + definition + "</defs>" + instance));
    }

    [Fact]
    public void A_shared_percentage_source_rebinds_after_each_viewport_change()
    {
        const string oval = "<circle cx='50%' cy='50%' r='20%' fill='#347bad'/>";
        const string instance = "<use href='#shape' width='999' height='777'/>";
        string first = "<svg x='3' y='4' width='48' height='32' viewBox='0 0 24 16'>CONTENT</svg>";
        string second = "<svg x='66' y='10' width='42' height='56' viewBox='0 0 12 16'>CONTENT</svg>";
        string repeated = first.Replace("CONTENT", instance, StringComparison.Ordinal) + second.Replace("CONTENT", instance, StringComparison.Ordinal);
        string inline = first.Replace("CONTENT", oval, StringComparison.Ordinal) + second.Replace("CONTENT", oval, StringComparison.Ordinal);
        Assert.Equal(Render(inline), Render("<defs>" + oval.Replace("<circle", "<circle id='shape'", StringComparison.Ordinal) + "</defs>" + repeated));
    }

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='120' height='80'>" + content + "</svg>"),
            SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
