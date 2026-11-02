using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Text.Json;

namespace Lokad.Svg.Tests;

public sealed class SvgCapturedCanvasCoordinatesTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(64)]
    [InlineData(65)]
    public void Canvas_products_keep_their_native_bits_before_source_capture(int depth)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "TestData", "native-canvas-recording.json")));
        JsonElement row = document.RootElement.GetProperty("canvasCases").EnumerateArray()
            .Single(item => item.GetProperty("depth").GetInt32() == depth);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            SvgRecordedTransform? chain = null;
            for (int index = 1; index <= depth; index++)
                chain = state.Scene.RetainRecordedTransform(chain, SvgAffineTransform.FromTranslation(index * .125, index * .125), null);
            SvgRecordedTransform recording = chain ?? throw new InvalidOperationException("Missing recorded chain.");
            long retained = budget.Snapshot().CurrentBytes;
            SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateImage(new(1, 0, 0, 0, 1, 0), new(1, 0, 0, 0, 1, 0))
                .WithCapturedCanvasDrawing(recording, SvgShaderMatrix.FromAffine(Matrix(row, "output")), Matrix(row, "initial"),
                    recording.Map, budget, CancellationToken.None, Matrix(row, "capture"));
            foreach (int index in new[] { 0, 1, 0 })
            {
                JsonElement origin = row.GetProperty("origins")[index];
                Assert.True(coordinates.TryGetMatrix(new(origin.GetProperty("x").GetInt32(), origin.GetProperty("y").GetInt32()),
                    out SvgShaderMatrix actual));
                JsonElement bits = origin.GetProperty("expectedBits");
                Assert.Equal(6, bits.GetArrayLength());
                SvgShaderMatrix expected = new(Lane(0), Lane(2), Lane(4), Lane(1), Lane(3), Lane(5));
                Assert.Equal(expected, actual);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                float Lane(int lane) => BitConverter.UInt32BitsToSingle(bits[lane].GetUInt32());
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Root_bitmap_precedes_recording_and_source_capture_follows_it()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "TestData", "native-canvas-recording.json")));
        JsonElement row = document.RootElement.GetProperty("rootCaptureCase");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            SvgRecordedTransform recording = Assert.IsType<SvgRecordedTransform>(
                state.Scene.RetainRecordedTransform(null, SvgAffineTransform.Identity, null));
            Assert.True(recording.TryMapCanvas(SvgShaderMatrix.FromAffine(Matrix(row, "output")), SvgAffineTransform.Identity,
                Matrix(row, "target"), budget, CancellationToken.None, out SvgShaderMatrix drawing));
            SvgAffineTransform captured = Matrix(row, "capture").ComposeNative(new(drawing.ScaleX, drawing.SkewY,
                drawing.SkewX, drawing.ScaleY, drawing.TranslateX, drawing.TranslateY));
            JsonElement bits = row.GetProperty("expectedBits");
            Assert.Equal(new SvgAffineTransform(Lane(0), Lane(1), Lane(2), Lane(3), Lane(4), Lane(5)), captured);
            float Lane(int index) => BitConverter.UInt32BitsToSingle(bits[index].GetUInt32());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgAffineTransform Matrix(JsonElement row, string name)
    {
        JsonElement values = row.GetProperty(name);
        Assert.Equal(6, values.GetArrayLength());
        return new(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble(),
            values[3].GetDouble(), values[4].GetDouble(), values[5].GetDouble());
    }
}
