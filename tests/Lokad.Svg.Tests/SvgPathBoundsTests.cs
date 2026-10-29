using System.Globalization;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPathBoundsTests
{
    [Fact]
    public void Curve_bounds_match_original_compiled_native_extrema_and_evaluation()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        using JsonDocument input = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "path-bounds-native.json")));
        Assert.Equal("f8b66b7597c4cc859d3ed190e9c6872241e6721c", input.RootElement.GetProperty("nativePin").GetString());
        Dictionary<string, float[]> expected = File.ReadAllLines(Path.Combine(directory, "path-bounds-native.csv")).Skip(1)
            .Select(row => row.Split(',')).ToDictionary(fields => fields[0], fields => fields.Skip(1).Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray(), StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonElement testCase in input.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id = testCase.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case ID.");
            Assert.True(seen.Add(id));
            List<SvgAuthoredPathCommand> commands = new();
            foreach (JsonElement command in testCase.GetProperty("commands").EnumerateArray())
            {
                string verb = command.GetProperty("verb").GetString() ?? throw new InvalidDataException("Missing verb.");
                SvgDevicePoint[] points = command.GetProperty("points").EnumerateArray().Select(point => new SvgDevicePoint(point[0].GetSingle(), point[1].GetSingle())).ToArray();
                commands.Add(new(Enum.Parse<SvgAuthoredPathVerb>(verb), SvgAuthoredArcFlags.None, points[0],
                    points.Length > 1 ? points[1] : default,
                    command.TryGetProperty("weight", out JsonElement weight) ? new(weight.GetSingle(), 0) : points.Length > 2 ? points[2] : default));
            }
            SvgRectangle bounds = SvgPathBounds.Compute(commands.ToArray(), CancellationToken.None);
            float[] actual = [(float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height];
            Assert.True(expected[id].Select(BitConverter.SingleToUInt32Bits).SequenceEqual(actual.Select(BitConverter.SingleToUInt32Bits)),
                $"Native bounds differ for {id}.");
        }
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Bounds_cancellation_preserves_its_token()
    {
        SvgAuthoredPathCommand[] commands =
        [
            new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(4.125, 8.25), default, default),
            new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, new(48.125, -12.75), new(-38.25, 48.875), new(24.375, 20.5)),
            new(SvgAuthoredPathVerb.Arc, SvgAuthoredArcFlags.Sweep, new(9.125, 14.375), new(0, 0), new(42.625, 20.5)),
        ];
        SvgPathBounds.Compute(commands, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException error = Assert.Throws<OperationCanceledException>(() => SvgPathBounds.Compute(commands, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void Affine_curve_bounds_find_extrema_after_mapping_controls()
    {
        SvgAuthoredPathCommand[] commands =
        [
            new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(0, 0), default, default),
            new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, new(4, 8), new(8, 0), default),
        ];
        SvgRectangle bounds = SvgPathBounds.Compute(commands, new(1, 0, 1, 1, 0, 0), CancellationToken.None);
        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(9, bounds.Width);
        Assert.Equal(4, bounds.Height);
    }

    [Fact]
    public void Repeated_curve_bounds_queries_allocate_no_heap_storage()
    {
        SvgAuthoredPathCommand[] commands =
        [
            new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(4.125, 8.25), default, default),
            new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, new(24.75, -4.125), new(21.625, 20.375), default),
        ];
        // Warm the measured loop as well as the bounds implementation.
        QueryBounds(commands);
        QueryBounds(commands);
        long before = GC.GetAllocatedBytesForCurrentThread();
        QueryBounds(commands);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void QueryBounds(SvgAuthoredPathCommand[] input)
        {
            for (int index = 0; index < 1024; index++) SvgPathBounds.Compute(input, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("clip-pair-radial.svg")]
    [InlineData("raw-cubic-tangent-shader.svg")]
    [InlineData("aa-clip-root-shader.svg")]
    public async Task Six_outputs_agree_for_curved_gradient_boxes_and_clips(string source)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", source));
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }
}
