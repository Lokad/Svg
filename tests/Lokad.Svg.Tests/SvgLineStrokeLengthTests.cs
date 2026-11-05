using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgLineStrokeLengthTests
{
    [Theory]
    [InlineData("8", 8)]
    [InlineData("8px", 8)]
    [InlineData("8PX", 8)]
    [InlineData("6pt", 8)]
    [InlineData(".5pc", 8)]
    [InlineData("1in", 96)]
    [InlineData("2.54cm", 96)]
    [InlineData("25.4mm", 96)]
    [InlineData("101.6q", 96)]
    [InlineData("0pt", 0)]
    [InlineData("-0q", 0)]
    [InlineData("+0mm", 0)]
    public void Axis_line_lowering_consumes_the_resolved_even_width(string width, int expected)
    {
        using SvgRoot root = Parse("", "<line x1='8' y1='8' x2='24' y2='8' stroke='blue' stroke-width='" + width + "'/>");
        if (expected == 0) Assert.Empty(root.Geometries);
        else Assert.Equal(expected, Assert.Single(root.Geometries).AxisAlignedLine.StrokeWidth);
    }

    [Theory]
    [InlineData("attribute")]
    [InlineData("inline")]
    [InlineData("inherited")]
    [InlineData("stylesheet")]
    public void Width_resolution_precedes_optimized_vertical_line_dispatch(string declaration)
    {
        string definition = declaration == "stylesheet" ? "<style>line{stroke-width:6pt}</style>" : "";
        string attributes = declaration switch
        {
            "attribute" => " stroke-width='6pt'",
            "inline" => " style='stroke-width:6pt'",
            "inherited" or "stylesheet" => "",
            _ => throw new ArgumentOutOfRangeException(nameof(declaration))
        };
        string line = "<line x1='8' y1='8' x2='8' y2='24' stroke='blue'" + attributes + "/>";
        using SvgRoot root = Parse(definition, declaration == "inherited" ? "<g stroke-width='6pt'>" + line + "</g>" : line);
        Assert.Equal(8, Assert.Single(root.Geometries).AxisAlignedLine.StrokeWidth);
    }

    [Theory]
    [InlineData(".75pt")]
    [InlineData("1pt")]
    public void Odd_and_fractional_resolved_widths_keep_native_outline_dispatch(string width)
    {
        using SvgRoot root = Parse("", "<line x1='8' y1='8' x2='24' y2='8' stroke='blue' stroke-width='" + width + "'/>");
        Assert.Equal(SvgGeometryKind.PolygonFill, Assert.Single(root.Geometries).Kind);
    }

    [Theory]
    [InlineData("-1pt")]
    [InlineData("1610612736pt")]
    [InlineData("1 pt")]
    [InlineData("1em")]
    public void Source_width_validation_keeps_its_error_before_integer_lowering(string width)
    {
        SvgException error = Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = Parse("", "<line x1='8' y1='8' x2='24' y2='8' stroke='blue' stroke-width='" + width + "'/>");
        });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    private static SvgRoot Parse(string definition, string body)
        => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + definition + body + "</svg>"),
            SvgRenderLimits.Default, CancellationToken.None);
}
