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
            ParserWorkCounter work = new(100);
            float actual = SvgFloatNumber.ReadValidated(fields[0], work, CancellationToken.None);
            Assert.Equal(expected, unchecked((uint)BitConverter.SingleToInt32Bits(actual)));
            Assert.True(work.Used > 0);
        }
    }

    [Fact]
    public void Numeric_reading_denial_and_cancellation_preserve_their_contracts()
    {
        SvgException denied = Assert.Throws<SvgException>(() => SvgFloatNumber.ReadValidated("1.9283746", new(0), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => SvgFloatNumber.ReadValidated("1.9283746", new(100), cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }

    [Fact]
    public void Integrated_transform_and_path_readers_keep_range_and_grammar_owners()
    {
        SvgAffineTransform transform = SvgGeometrySyntax.ParseTransform("scale(1.9283746)", new(100), CancellationToken.None);
        Assert.Equal(0x3ff6d4fa, BitConverter.SingleToInt32Bits((float)transform.A));
        SvgContourBuilder builder = new(8, 1);
        SvgPathDataParser.ParsePath("M5. 0L6. 1", SvgAffineTransform.Identity, builder, new(0.5, 64, 256), 2, new(100), CancellationToken.None, null);
        Assert.Equal(new SvgDevicePoint(5, 0), builder.GetContour(0)[0]);
        SvgException malformed = Assert.Throws<SvgException>(() => SvgPathDataParser.ParsePath("M1e309 0", SvgAffineTransform.Identity, new(8, 1), new(0.5, 64, 256), 1, new(100), CancellationToken.None, null));
        Assert.Equal(SvgErrorCode.MalformedInput, malformed.ErrorCode);
        SvgException overflow = Assert.Throws<SvgException>(() => SvgPathDataParser.ParsePath("M3e38 0l3e38 0", SvgAffineTransform.Identity, new(8, 1), new(0.5, 64, 256), 2, new(100), CancellationToken.None, null));
        Assert.Equal(SvgErrorCode.LimitExceeded, overflow.ErrorCode);
        SvgException denied = Assert.Throws<SvgException>(() => SvgGeometrySyntax.ParseTransform("scale(1.9283746)", new(0), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
    }
}
