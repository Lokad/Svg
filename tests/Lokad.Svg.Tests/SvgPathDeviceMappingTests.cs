using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPathDeviceMappingTests
{
    [Fact]
    public void Axis_aligned_path_maps_match_independent_native_float_vectors()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "path-scale-native.csv"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "path-scale-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal("f8b66b7597c4cc859d3ed190e9c6872241e6721c", metadata.RootElement.GetProperty("sourceRevision").GetString());
        string[] rows = File.ReadAllLines(Path.Combine(directory, "path-scale-native.csv"));
        Assert.Equal(180, rows.Length);
        foreach (string row in rows)
        {
            string[] fields = row.Split(',');
            SvgAffineTransform transform = SvgAffineTransform.FromScaleOffset(double.Parse(fields[2], CultureInfo.InvariantCulture), double.Parse(fields[3], CultureInfo.InvariantCulture), double.Parse(fields[4], CultureInfo.InvariantCulture), double.Parse(fields[5], CultureInfo.InvariantCulture));
            SvgContourBuilder builder = new(4, 1);
            // Parse an actual path so this verifies the common lowering boundary,
            // including narrowing of authored coordinates before mapped storage.
            SvgPathDataParser.ParsePath("M" + fields[6] + " " + fields[7], transform, builder, new(0.5, 64, 256), 1, CancellationToken.None, null);
            SvgDevicePoint point = builder.GetContour(0)[0];
            Assert.Equal(uint.Parse(fields[8], NumberStyles.HexNumber, CultureInfo.InvariantCulture), unchecked((uint)BitConverter.SingleToInt32Bits((float)point.X)));
            Assert.Equal(uint.Parse(fields[9], NumberStyles.HexNumber, CultureInfo.InvariantCulture), unchecked((uint)BitConverter.SingleToInt32Bits((float)point.Y)));
        }
    }

    [Fact]
    public void Nonfinite_float_device_maps_fail_with_a_categorized_limit()
    {
        SvgContourBuilder builder = new(4, 1);
        SvgException failure = Assert.Throws<SvgException>(() => SvgPathDataParser.ParsePath("M1e40 0", SvgAffineTransform.Identity, builder, new(0.5, 64, 256), 1, CancellationToken.None, null));
        Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode);
    }
}
