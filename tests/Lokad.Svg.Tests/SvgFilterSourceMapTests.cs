using System.Text.Json;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgFilterSourceMapTests
{
    [Fact]
    public void Recorded_drawing_is_prepared_before_native_capture_rounding()
    {
        SvgAffineTransform actual = AssertNativeCase(0);
        Assert.NotEqual(0, actual.B);
    }

    [Fact]
    public void Compensated_root_retains_native_zero_skew_without_snapping()
    {
        SvgAffineTransform actual = AssertNativeCase(1);
        Assert.Equal(0, actual.B);
        Assert.Equal(0, actual.C);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void One_ulp_input_changes_follow_native_preparation_rounding(int coefficient)
        => AssertNativeCase(coefficient + 2);

    private static SvgAffineTransform AssertNativeCase(int index)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "TestData", "native-source-stages.json")));
        JsonElement cases = document.RootElement.GetProperty("sourceCases");
        Assert.Equal(6, cases.GetArrayLength());
        JsonElement row = cases[index];
        Assert.Equal(index, row.GetProperty("id").GetInt32());
        SvgDrawingMap drawing = new(Matrix(row, "root"), SvgAffineTransform.Identity);
        SvgAffineTransform target = Matrix(row, "target"), capture = Matrix(row, "capture");
        SvgConverter.SvgFilterSourceMap source = new(Matrix(row, "source"), Matrix(row, "layer"), capture, drawing);
        SvgAffineTransform expected = Expected(row);
        SvgAffineTransform actual = source.MapPreparedDrawing(target, capture);
        Assert.Equal(expected, actual);
        if (index < 2)
            Assert.NotEqual(expected, capture.ComposeNative(drawing.Device).ComposeNative(target));
        else
        {
            int coefficient = index - 2;
            float original = (float)cases[0].GetProperty("target")[coefficient].GetDouble();
            Assert.Equal((double)MathF.BitIncrement(original), row.GetProperty("target")[coefficient].GetDouble());
        }
        return actual;

        static SvgAffineTransform Matrix(JsonElement row, string name)
        {
            JsonElement array = row.GetProperty(name);
            Assert.Equal(6, array.GetArrayLength());
            return new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble(),
                array[3].GetDouble(), array[4].GetDouble(), array[5].GetDouble());
        }

        static SvgAffineTransform Expected(JsonElement row)
        {
            JsonElement array = row.GetProperty("expectedBits");
            Assert.Equal(6, array.GetArrayLength());
            return new(Lane(0), Lane(1), Lane(2), Lane(3), Lane(4), Lane(5));
            float Lane(int lane) => BitConverter.UInt32BitsToSingle(array[lane].GetUInt32());
        }
    }
}
