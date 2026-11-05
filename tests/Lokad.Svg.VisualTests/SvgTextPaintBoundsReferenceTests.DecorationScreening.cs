using System.Globalization;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgTextPaintBoundsReferenceTests
{
    public static IEnumerable<object[]> DecorationScreeningCases() => CaptureCases("decoration-screening.json");

    [Theory]
    [MemberData(nameof(DecorationScreeningCases))]
    [Trait("Layer", "Rendering")]
    [Trait("Category", "Research")]
    public Task Full_decoration_screen_preserves_each_original_native_target(string id)
        => AssertNativeImage("decoration-screening.json", id);

    [Fact]
    [Trait("Layer", "Integrity")]
    [Trait("Category", "Research")]
    public void Full_decoration_screen_retains_sources_fonts_and_healthy_native_triples()
        => AssertDecorationCapture("decoration-screening.json", 684, 684);

    private static byte[] CreateDecorationScreeningSource(JsonElement row)
    {
        JsonElement recipe = row.GetProperty("recipe");
        string role = Required(row, "role");
        if (role is not ("candidate" or "control")) throw new InvalidDataException("Unknown decoration role.");
        string paint = Required(recipe, "paint");
        string body;
        switch (Required(row, "group"))
        {
            case "decoration-scopes":
            {
                string lines = Required(recipe, "lines"), child = Required(recipe, "child"), ink = Required(recipe, "ink"), map = Required(recipe, "transform");
                string size = recipe.GetProperty("size").GetInt32().ToString(CultureInfo.InvariantCulture);
                string declaration = role == "candidate" ? $" text-decoration='{child}'" : "";
                body = $"<g transform='{map}'><text x='2' y='22' fill='red' text-decoration='{lines}'>I<tspan fill='{paint}' font-size='{size}'{declaration}>{ink}</tspan>I</text></g>";
                break;
            }
            case "decoration-isolation":
            {
                string line = Required(recipe, "line"), content = Required(recipe, "content"), map = Required(recipe, "transform");
                string size = recipe.GetProperty("size").GetInt32().ToString(CultureInfo.InvariantCulture);
                bool hidden = recipe.GetProperty("hidden").GetBoolean();
                string declaration = role == "candidate" ? $" text-decoration='{line}'" : "";
                body = $"<g transform='{map}'><text x='2' y='22' fill='red'{declaration}>" + Ink("I", hidden)
                    + $"<tspan fill='{paint}' font-size='{size}'{declaration}>" + Ink(content, hidden) + "</tspan>" + Ink("I", hidden) + "</text></g>";
                break;
            }
            case "decoration-overlap":
            {
                string line = Required(recipe, "line"), shape = Required(recipe, "shape");
                string delta = recipe.GetProperty("delta").GetInt32().ToString(CultureInfo.InvariantCulture);
                string declaration = role == "candidate" ? $" text-decoration='{line}'" : "";
                body = $"<text x='2' y='22' fill='red'{declaration}>I<tspan fill='{paint}' font-size='19'>{shape}</tspan><tspan dx='{delta}'>I</tspan></text>";
                break;
            }
            default: throw new InvalidDataException("Unknown decoration cohort.");
        }
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' font-family='serif' font-size='12'>" + body + "</svg>");

        static string Ink(string text, bool hidden) => hidden ? "<tspan fill='none'>" + text + "</tspan>" : text;
    }
}
