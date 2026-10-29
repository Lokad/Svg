using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFloatNumberTests
{
    [Fact]
    public void Numeric_bits_match_three_independent_browser_captures()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        byte[] data = File.ReadAllBytes(Path.Combine(directory, "svg-number-browser.csv"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "svg-number-browser.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        Assert.Equal(3, metadata.RootElement.GetProperty("freshProcessCaptures").GetInt32());
        Assert.All(metadata.RootElement.GetProperty("captureStartupHealth").EnumerateArray(), x => Assert.Equal("healthy", x.GetString()));
        Assert.All(metadata.RootElement.GetProperty("captureProfilesCleaned").EnumerateArray(), x => Assert.True(x.GetBoolean()));
        string[] rows = File.ReadAllLines(Path.Combine(directory, "svg-number-browser.csv"));
        Assert.Equal(31, rows.Length);
        foreach (string row in rows)
        {
            string[] fields = row.Split(',');
            Assert.True(double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double validated));
            Assert.True(double.IsFinite(validated));
            uint expected = uint.Parse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.Equal(expected, uint.Parse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            float actual = SvgFloatNumber.ReadValidated(fields[0], CancellationToken.None);
            Assert.Equal(expected, unchecked((uint)BitConverter.SingleToInt32Bits(actual)));
        }
    }

    [Theory]
    [InlineData("scale(0.1686295142120) translate(0 251.198)", 42.35939407348633)]
    [InlineData("matrix(0.1686295142120 0 0 0.1686295142120 0 0) translate(0 251.198)", 42.35939587395478)]
    [InlineData("scale(0.1686295142120) rotate(0) translate(0 251.198)", 42.35939587395478)]
    public void Local_transform_lists_preserve_native_axis_and_full_matrix_precision(string value, double expectedTranslation)
    {
        SvgAffineTransform transform = SvgGeometrySyntax.ParseTransform(value, CancellationToken.None);
        Assert.Equal(expectedTranslation, transform.F);
    }

    [Fact]
    public void Enclosing_groups_compose_finished_local_maps_at_double_precision()
    {
        SvgAffineTransform scale = SvgGeometrySyntax.ParseTransform("scale(0.1686295142120)", CancellationToken.None);
        SvgAffineTransform translation = SvgGeometrySyntax.ParseTransform("translate(0 251.198)", CancellationToken.None);
        Assert.Equal(42.35939587395478, scale.ComposeChecked(translation).F);
    }

    [Fact]
    public void Numeric_reading_preserves_original_cancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => SvgFloatNumber.ReadValidated("1.9283746", cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }

    [Fact]
    public void Integrated_transform_and_path_readers_keep_range_and_grammar_owners()
    {
        SvgAffineTransform transform = SvgGeometrySyntax.ParseTransform("scale(1.9283746)", CancellationToken.None);
        Assert.Equal(0x3ff6d4fa, BitConverter.SingleToInt32Bits((float)transform.A));
        SvgContourBuilder builder = new(8, 1);
        SvgPathDataParser.ParsePath("M5. 0L6. 1", SvgAffineTransform.Identity, builder, new(0.5, 64, 256), 2, CancellationToken.None, null);
        Assert.Equal(new SvgDevicePoint(5, 0), builder.GetContour(0)[0]);
        SvgException malformed = Assert.Throws<SvgException>(() => SvgPathDataParser.ParsePath("M1e309 0", SvgAffineTransform.Identity, new(8, 1), new(0.5, 64, 256), 1, CancellationToken.None, null));
        Assert.Equal(SvgErrorCode.MalformedInput, malformed.ErrorCode);
        SvgException overflow = Assert.Throws<SvgException>(() => SvgPathDataParser.ParsePath("M3e38 0l3e38 0", SvgAffineTransform.Identity, new(8, 1), new(0.5, 64, 256), 2, CancellationToken.None, null));
        Assert.Equal(SvgErrorCode.LimitExceeded, overflow.ErrorCode);
    }
}
