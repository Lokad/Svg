using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Text.Json;

namespace Lokad.Svg.Tests;

public sealed class SvgRecordedShaderCoordinatesTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(64)]
    [InlineData(65)]
    public void Shader_products_keep_native_preparation_before_external_bitmap_composition(int depth)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "TestData", "native-source-stages.json")));
        JsonElement row = document.RootElement.GetProperty("shaderCases").EnumerateArray()
            .Single(item => item.GetProperty("depth").GetInt32() == depth);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            SvgRecordedTransform recording = Chain(state, depth);
            long retained = budget.Snapshot().CurrentBytes;
            foreach (int index in new[] { 0, 1, 0 })
            {
                JsonElement origin = row.GetProperty("origins")[index];
                Assert.True(recording.TryMap(SvgShaderMatrix.FromAffine(Matrix(row, "output")), Matrix(row, "initial"),
                    recording.Map, budget, CancellationToken.None, out SvgShaderMatrix prepared));
                SvgAffineTransform capture = SvgAffineTransform.FromTranslation(-origin.GetProperty("x").GetInt32(),
                    -origin.GetProperty("y").GetInt32()).ComposeNative(Matrix(row, "capture"));
                SvgShaderMatrix local = SvgShaderMatrix.FromAffine(capture.ComposeNative(new(prepared.ScaleX, prepared.SkewY,
                    prepared.SkewX, prepared.ScaleY, prepared.TranslateX, prepared.TranslateY)));
                Assert.True(local.TryInvert(out SvgShaderMatrix actual));
                JsonElement bits = origin.GetProperty("expectedBits");
                Assert.Equal(6, bits.GetArrayLength());
                SvgShaderMatrix expected = new(Lane(0), Lane(2), Lane(4), Lane(1), Lane(3), Lane(5));
                Assert.Equal(expected, actual);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                float Lane(int lane) => BitConverter.UInt32BitsToSingle(bits[lane].GetUInt32());
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static SvgAffineTransform Matrix(JsonElement row, string name)
        {
            JsonElement array = row.GetProperty(name);
            Assert.Equal(6, array.GetArrayLength());
            return new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble(),
                array[3].GetDouble(), array[4].GetDouble(), array[5].GetDouble());
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(64)]
    [InlineData(65)]
    public void Bitmap_origin_changes_preserve_source_initial_and_recorded_stages(int depth)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            SvgRecordedTransform recording = Chain(state, depth);
            long retained = budget.Snapshot().CurrentBytes;
            SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateImage(new(1, 0, 0, 0, 1, 0), new(1, 0, 0, 0, 1, 0))
                .WithRecordedDrawing(recording, new(1, 0, 3, 0, 1, 5), SvgAffineTransform.FromScaleOffset(2, 2, 1, 2),
                    recording.Map, budget, CancellationToken.None);
            foreach (SvgCpuBitmapOrigin origin in new[] { new SvgCpuBitmapOrigin(0, 0), new SvgCpuBitmapOrigin(16, 32), new SvgCpuBitmapOrigin(0, 0) })
            {
                Assert.True(coordinates.TryMap(40, 60, origin, out var point));
                Assert.Equal((float)((40.5 - 4) / 2 - depth * .125), point.X);
                Assert.Equal((float)((60.5 - 7) / 2 - depth * .125), point.Y);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Deep_mapping_refusal_does_not_publish_a_cache_and_explicit_retry_releases_scratch()
    {
        const long allowance = 1_000_000;
        RenderBudget budget = new(new HostMemoryBudget(allowance));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            SvgRecordedTransform recording = Chain(state, 65);
            long retained = budget.Snapshot().CurrentBytes;
            SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateImage(new(1, 0, 0, 0, 1, 0), new(8, 0, 0, 0, 8, 0))
                .WithRecordedDrawing(recording, new(1, 0, 0, 0, 1, 0), SvgAffineTransform.Identity,
                    recording.Map, budget, CancellationToken.None);
            Assert.True(coordinates.TryLinear(20, 20, new(0, 0), out double initial));
            Assert.Equal(1.546875, initial);
            long scratch = ParserCollectionMemory.GetArrayByteCount<SvgAffineTransform>(65);
            using (RenderBudget.BudgetReservation borrowed = budget.Reserve(allowance - retained - scratch + 1, SvgMemoryCategory.Scene))
            {
                long denied = budget.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => coordinates.TryLinear(20, 20, new(16, 16), out _)).ErrorCode);
                Assert.Equal(denied, budget.Snapshot().CurrentBytes);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.True(coordinates.TryLinear(20, 20, new(16, 16), out double retry));
            Assert.Equal(initial, retry);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            SvgShaderCoordinates cancelled = coordinates.WithRecordedDrawing(recording, new(1, 0, 0, 0, 1, 0),
                SvgAffineTransform.Identity, recording.Map, budget, cancellation.Token);
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => cancelled.TryLinear(20, 20, new(16, 16), out _)).CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.True(coordinates.TryLinear(20, 20, new(0, 0), out double next));
            Assert.Equal(initial, next);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgRecordedTransform Chain(SvgDocumentState state, int depth)
    {
        SvgRecordedTransform? chain = null;
        for (int index = 1; index <= depth; index++)
            chain = state.Scene.RetainRecordedTransform(chain, SvgAffineTransform.FromTranslation(index * .125, index * .125), null);
        SvgRecordedTransform result = Assert.IsType<SvgRecordedTransform>(chain);
        Assert.Equal(depth, result.Count);
        return result;
    }
}
