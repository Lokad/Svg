using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgAlphaBoundaryMechanismTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData/filter-alpha-boundaries.json")));
        foreach (JsonElement row in vectors.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing vector id."),
                row.GetProperty("color").GetString() ?? throw new InvalidDataException("Missing color."),
                row.GetProperty("opacity").GetString() ?? throw new InvalidDataException("Missing opacity."),
                row.GetProperty("expectedAlpha").GetByte()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Parsed_alpha_and_constant_store_match_native_boundary(string id, string color, string opacity, byte expectedAlpha)
    {
        Assert.NotEmpty(id);
        SvgSolidPaint paint = SvgColorParser.ParsePaint(color, SvgRootParser.CssNamedColors);
        SvgPaintOpacity scale = SvgColorParser.ParsePaintOpacity(opacity);
        SvgGradientStop stop = new(0, paint.Color) { Alpha = paint.Alpha * scale.Value };
        Assert.Equal(expectedAlpha, SvgShaderPaint.ConstantGradient(stop, 1).Alpha);
    }
}
