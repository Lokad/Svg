using System.Globalization;
using System.Text.Json;
using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientCoordinateTests
{
    [Fact]
    public void Gradient_transform_numbers_match_browser_dom_bits()
    {
        const string transform = "matrix(1.17 .23 -.31 .89 301.7 149.3)";
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/gradient-transform-browser.json")));
        Assert.Equal(3, evidence.RootElement.GetProperty("freshProcessCaptures").GetInt32());
        uint[] expected = evidence.RootElement.GetProperty("matrixBits").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
        Assert.Equal(6, expected.Length);
        foreach (string name in new[] { "linearGradient", "radialGradient" })
        {
            using StringReader input = new("<" + name + " gradientTransform='" + transform + "'/>");
            using XmlReader reader = XmlReader.Create(input);
            reader.MoveToContent();
            ParserWorkCounter work = new(1000);
            SvgGradientTransform actual = name == "linearGradient"
                ? SvgGradientSyntax.ParseLinearVector(reader, work, CancellationToken.None).Transform
                : SvgGradientSyntax.ParseRadialVector(reader, work, CancellationToken.None).Transform;
            Assert.Equal(expected, new[] { actual.A, actual.B, actual.C, actual.D, actual.E, actual.F }.Select(value => BitConverter.SingleToUInt32Bits((float)value)));
            Assert.True(work.Used > 0);
        }
    }

    [Fact]
    public void Gradient_transform_numeric_reading_keeps_shared_denial_and_cancellation()
    {
        using StringReader input = new("<radialGradient gradientTransform='scale(1.17)'/>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        SvgException denied = Assert.Throws<SvgException>(() => SvgGradientSyntax.ParseRadialVector(reader, new(0), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => SvgGradientSyntax.ParseRadialVector(reader, new(1000), cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }

    [Fact]
    public void Coordinate_and_normalized_material_stages_match_compiled_native_float_lanes_and_pixels()
    {
        string[] rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData/gradient-coordinates-native.csv"));
        Assert.Equal(193, rows.Length);
        SvgGradientStop[] stops = [new(0, new(23, 169, 205, 255)) { Alpha = .27f }, new(1, new(195, 213, 55, 255)) { Alpha = .81f }];
        SvgRectangle box = new(0, 0, 640, 320, default, true, null);
        SvgCpuRasterGrid grid = new(640, 320);
        foreach (string row in rows.Skip(1))
        {
            string[] v = row.Split(',');
            float[] values = v.Skip(1).Take(10).Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            SvgShaderCoordinates coordinates;
            if (v[0] == "linear")
            {
                SvgLinearGradient definition = new(stops[0].Color, stops[1].Color, values[0], values[1], values[2], values[3], 0, 1, false, true, SvgGradientSpread.Pad, stops, 1, 1, false);
                coordinates = SvgShaderCoordinates.CreateLinear(definition, box, values[4], values[8], values[6], values[9]);
            }
            else
            {
                bool general = values[5] != 0 || values[7] != 0;
                SvgGradientTransform paint = new(values[4], values[7], values[5], values[8], values[6], values[9]);
                Assert.True(paint.TryInvert(out SvgGradientTransform inverse));
                SvgRadialGradient definition = new(stops[0].Color, stops[1].Color, values[0], values[1], values[2], values[0], values[1], 0, 0, 1, false, true, SvgGradientSpread.Pad, stops, 1, 1, general, inverse, general);
                coordinates = SvgShaderCoordinates.CreateRadial(definition, box, general ? 1 : values[4], general ? 1 : values[8], general ? 0 : values[6], general ? 0 : values[9]);
            }
            int x = int.Parse(v[11], CultureInfo.InvariantCulture), y = int.Parse(v[12], CultureInfo.InvariantCulture);
            SvgCpuBitmapOrigin expectedOrigin = new(int.Parse(v[13], CultureInfo.InvariantCulture), int.Parse(v[14], CultureInfo.InvariantCulture));
            Assert.Equal(expectedOrigin, grid.GetBitmapOrigin(x, y));
            double position;
            bool sampled = v[0] == "linear" ? coordinates.TryLinear(x, y, grid, out position) : coordinates.TryRadial(x, y, grid, out position);
            Assert.True(sampled, row);
            Assert.Equal(uint.Parse(v[15], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits((float)position));
            // Repeating a sample exercises the cached bitmap matrix.
            Assert.True(v[0] == "linear" ? coordinates.TryLinear(x, y, grid, out double cached) : coordinates.TryRadial(x, y, grid, out cached));
            Assert.Equal(position, cached);
            SvgShaderPaint sample = SvgShaderPaint.Ramp(stops, stops[0].Color, stops[1].Color, 0, 1, Math.Clamp(position, 0, 1), 201, grid.GetGradientDither(x, y));
            uint[] expectedLanes = v.Skip(16).Take(4).Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            uint[] actualLanes = [BitConverter.SingleToUInt32Bits(sample.Red), BitConverter.SingleToUInt32Bits(sample.Green), BitConverter.SingleToUInt32Bits(sample.Blue), BitConverter.SingleToUInt32Bits(sample.Alpha)];
            Assert.True(expectedLanes.SequenceEqual(actualLanes), row + "; actual lanes " + string.Join(",", actualLanes));
            byte[] destination = [11, 23, 37, 97];
            sample.Blend(destination, 191);
            Assert.Equal(v.Skip(20).Select(value => byte.Parse(value, CultureInfo.InvariantCulture)), destination);
        }
    }

    [Fact]
    public void Bitmap_local_radial_matrix_order_matches_native_positions_material_lanes_and_pixels()
    {
        string[] rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData/radial-local-matrix-native.csv"));
        Assert.Equal(385, rows.Length);
        SvgGradientStop[] stops = [new(0, new(23, 169, 205, 255)) { Alpha = .27f }, new(1, new(195, 213, 55, 255)) { Alpha = .81f }];
        SvgRectangle box = new(0, 0, 1476, 742, default, true, null);
        SvgCpuRasterGrid grid = new(1476, 742);
        foreach (string row in rows.Skip(1))
        {
            string[] cells = row.Split(',');
            float[] values = cells.Take(15).Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            SvgShaderMatrix local = new(values[3], values[4], values[5], values[6], values[7], values[8]);
            SvgShaderMatrix ctm = new(values[9], values[10], values[11], values[12], values[13], values[14]);
            SvgGradientTransform forward = new(local.ScaleX, local.SkewY, local.SkewX, local.ScaleY, local.TranslateX, local.TranslateY);
            if (!forward.TryInvert(out SvgGradientTransform inverse)) throw new InvalidDataException("Singular fixture.");
            SvgRadialGradient gradient = new(stops[0].Color, stops[1].Color, values[0], values[1], values[2], values[0], values[1], 0, 0, 1, false, true, SvgGradientSpread.Pad, stops, 1, 1, true, inverse, true)
            { NativePaintMatrices = new(local, new(ctm.ScaleX, ctm.SkewY, ctm.SkewX, ctm.ScaleY, ctm.TranslateX, ctm.TranslateY)) };
            SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateRadial(gradient, box, 1, 1, 0, 0);
            int x = int.Parse(cells[15], CultureInfo.InvariantCulture), y = int.Parse(cells[16], CultureInfo.InvariantCulture);
            SvgCpuBitmapOrigin expectedOrigin = new(int.Parse(cells[17], CultureInfo.InvariantCulture), int.Parse(cells[18], CultureInfo.InvariantCulture));
            Assert.Equal(expectedOrigin, grid.GetBitmapOrigin(x, y));
            Assert.True(coordinates.TryRadial(x, y, grid, out double position), row);
            Assert.Equal(uint.Parse(cells[19], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits((float)position));
            SvgShaderPaint sample = SvgShaderPaint.Ramp(stops, stops[0].Color, stops[1].Color, 0, 1, Math.Clamp(position, 0, 1), 201, grid.GetGradientDither(x, y));
            uint[] lanes = [BitConverter.SingleToUInt32Bits(sample.Red), BitConverter.SingleToUInt32Bits(sample.Green), BitConverter.SingleToUInt32Bits(sample.Blue), BitConverter.SingleToUInt32Bits(sample.Alpha)];
            Assert.Equal(cells.Skip(20).Take(4).Select(x => uint.Parse(x, CultureInfo.InvariantCulture)), lanes);
            byte[] destination = [11, 23, 37, 97];
            sample.Blend(destination, 191);
            Assert.Equal(cells.Skip(24).Select(x => byte.Parse(x, CultureInfo.InvariantCulture)), destination);
        }
    }

    [Theory]
    [InlineData("translated-rectangle")]
    [InlineData("reflected-path")]
    public async Task Separate_radial_local_and_drawing_matrices_preserve_six_output_parity(string name)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/radial-local-matrix-" + name + ".svg"));
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
