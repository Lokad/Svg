using TextEncoding = System.Text.Encoding;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRoundedRectOrderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Svg_source_commands_preserve_the_recorded_browser_start_and_order(int index)
    {
        using JsonDocument vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/rounded-rect-svg-order.json")));
        JsonElement vector = vectors.RootElement.GetProperty("cases")[index];
        string svg = vector.GetProperty("svg").GetString() ?? throw new InvalidDataException("The native vector has no SVG input.");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(TextEncoding.UTF8.GetBytes(svg), SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(scene.Geometries.Single(static geometry => geometry.AuthoredPath is not null).AuthoredPath);
            JsonElement.ArrayEnumerator recorded = vector.GetProperty("commands").EnumerateArray();
            Assert.Equal(vector.GetProperty("commands").GetArrayLength(), source.Commands.Length);
            int cursor = 0;
            foreach (JsonElement command in recorded)
            {
                SvgAuthoredPathCommand actual = source.Commands[cursor++];
                string verb = command.GetProperty("verb").GetString() ?? throw new InvalidDataException("The native command has no verb.");
                Assert.Equal(Enum.Parse<SvgAuthoredPathVerb>(verb), actual.Verb);
                JsonElement points = command.GetProperty("points");
                if (points.GetArrayLength() > 0) Assert.Equal(Point(points[0]), actual.First);
                if (points.GetArrayLength() > 1) Assert.Equal(Point(points[1]), actual.Second);
                if (actual.Verb == SvgAuthoredPathVerb.Conic) Assert.Equal((float)command.GetProperty("conicWeight").GetDouble(), (float)actual.Third.X);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static SvgDevicePoint Point(JsonElement point) => new(point.GetProperty("x").GetDouble(), point.GetProperty("y").GetDouble());
    }
}
