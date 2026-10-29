using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPointListNumberTests
{
    public static IEnumerable<object[]> PaddedCoordinates()
    {
        foreach (string shape in new[] { "polygon", "polyline" })
        foreach (int slot in new[] { 0, 2, 3 })
        foreach (int zeroes in new[] { 38, 39, 40 })
        foreach (string paint in new[] { "fill", "stroke", "markers", "offset", "flood" })
        foreach (string consumer in new[] { "direct", "use", "pattern", "mask" })
            yield return [shape, slot, zeroes, paint, consumer];
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [MemberData(nameof(PaddedCoordinates))]
    public void Nonfinite_native_coordinate_discards_the_entire_list(string shape, int slot, int zeroes, string paint, string consumer)
    {
        var (control, input) = PaddedInputs(shape, slot, zeroes, paint, consumer);
        SvgRasterTestHelper.AssertPixelsEqual(control, input, SvgRenderOptions.Default);
    }

    public static IEnumerable<object[]> NumericForms()
    {
        foreach (string shape in new[] { "polygon", "polyline" })
        foreach (string consumer in new[] { "direct", "group", "use-flood", "symbol", "viewport", "pattern", "mask" })
        foreach ((string token, string expected) in new (string, string)[]
        {
            ("+" + new string('0', 39) + "4", ""), ("-" + new string('0', 39) + "4", ""),
            (new string('0', 39) + "4e-1", ""), ("4e39", ""), ("0e39", ""),
            ("4e-50", "0"), ("0e-999", "0"), (new string('0', 39) + ".4", ".4")
        }) yield return [shape, consumer, token, expected];
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [MemberData(nameof(NumericForms))]
    public void Signed_exponent_and_fraction_forms_keep_native_source_eligibility(string shape, string consumer, string token, string expected)
    {
        var (control, input) = NumericInputs(shape, consumer, token, expected);
        SvgRasterTestHelper.AssertPixelsEqual(control, input, SvgRenderOptions.Default);
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("direct", 0)]
    [InlineData("use", 0)]
    [InlineData("group", 1024)]
    [InlineData("use-flood", 1024)]
    public void Enclosing_flood_can_paint_over_an_invalid_child_while_the_childs_own_flood_cannot(string consumer, int paintedPixels)
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document("polygon", "4 4 24 4 " + new string('0', 39) + "24 24 4 24", "flood", consumer));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        int opaque = 0;
        for (int pixel = 0; pixel < 1024; pixel++)
        {
            byte alpha = surface.Pixels[pixel * 4 + 3];
            Assert.Equal(paintedPixels == 0 ? (byte)0 : (byte)255, alpha);
            if (alpha != 0) opaque++;
        }
        Assert.Equal(paintedPixels, opaque);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("direct")]
    [InlineData("use")]
    [InlineData("group")]
    [InlineData("pattern")]
    [InlineData("mask")]
    public void Invalid_source_releases_staging_and_preserves_borrowed_storage_on_denial_and_retry(string consumer)
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document("polygon", "4 4 24 4 " + new string('0', 39) + "24 24 4 24", "markers", consumer));
        SvgParseOwnershipAssertions.PreservesBorrowedStorageOnDenialCancellationAndRetry(input);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("4 4 24 4 {invalid} 24 4 24 9", SvgErrorCode.MalformedInput)]
    [InlineData("4 4 24 4 {invalid} 24 bad 24", SvgErrorCode.MalformedInput)]
    [InlineData("4 4 24 4 {invalid} 24 {long} 24", SvgErrorCode.LimitExceeded)]
    public void Invalid_native_value_does_not_bypass_later_syntax_validation(string template, SvgErrorCode error)
    {
        string points = template.Replace("{invalid}", new string('0', 39) + "4", StringComparison.Ordinal)
            .Replace("{long}", new string('0', 257) + "4", StringComparison.Ordinal);
        byte[] input = TextEncoding.UTF8.GetBytes(Document("polygon", points, "fill", "direct"));
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        Assert.Equal(error, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("polygon", 0, 38, "fill", "direct")]
    [InlineData("polyline", 2, 38, "markers", "use")]
    [InlineData("polygon", 3, 40, "flood", "pattern")]
    public void Complete_png_padded_coordinates_preserve_source_eligibility(string shape, int slot, int zeroes, string paint, string consumer)
    {
        var (control, input) = PaddedInputs(shape, slot, zeroes, paint, consumer);
        SvgRasterTestHelper.AssertPngEqual(control, input, SvgRenderOptions.Default);
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("polygon", "use-flood", "4e-50", "0")]
    [InlineData("polyline", "mask", "4e39", "")]
    [InlineData("polygon", "symbol", "0e39", "")]
    public void Complete_png_numeric_forms_preserve_source_eligibility(string shape, string consumer, string token, string expected)
    {
        var (control, input) = NumericInputs(shape, consumer, token, expected);
        SvgRasterTestHelper.AssertPngEqual(control, input, SvgRenderOptions.Default);
    }

    private static (byte[] Control, byte[] Input) PaddedInputs(string shape, int slot, int zeroes, string paint, string consumer)
    {
        string[] coordinates = ["4", "4", "24", "4", "24", "24", "4", "24"];
        string ordinary = string.Join(' ', coordinates);
        int digits = coordinates[slot * 2].Length;
        coordinates[slot * 2] = new string('0', zeroes) + coordinates[slot * 2];
        // Independently captured actual SVGs establish the float-place boundary:
        // 39 integer digits are finite; a 40th overflows even when it is zero.
        string expected = zeroes + digits <= 39 ? ordinary : string.Empty;
        return (TextEncoding.UTF8.GetBytes(Document(shape, expected, paint, consumer)),
            TextEncoding.UTF8.GetBytes(Document(shape, string.Join(' ', coordinates), paint, consumer)));
    }

    private static (byte[] Control, byte[] Input) NumericInputs(string shape, string consumer, string token, string expected)
    {
        string points = "4 4 24 4 " + token + " 24 4 24";
        string control = expected.Length == 0 ? string.Empty : "4 4 24 4 " + expected + " 24 4 24";
        return (TextEncoding.UTF8.GetBytes(Document(shape, control, "flood", consumer)),
            TextEncoding.UTF8.GetBytes(Document(shape, points, "flood", consumer)));
    }

    private static string Document(string shape, string points, string paint, string consumer)
    {
        string definitions = "<marker id='m' markerUnits='userSpaceOnUse' markerWidth='2' markerHeight='2'><rect width='2' height='2' fill='green'/></marker><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>"
            + (paint == "offset" ? "<feOffset dx='0' dy='0'/>" : "<feFlood flood-color='green'/>") + "</filter>";
        string style = paint switch
        {
            "stroke" => "fill='none' stroke='red' stroke-width='4'",
            "markers" => "fill='none' marker-start='url(#m)' marker-mid='url(#m)' marker-end='url(#m)'",
            "offset" or "flood" => "fill='red' filter='url(#f)'",
            _ => "fill='red'"
        };
        string source = "<" + shape + " id='s' points='" + points + "' " + style + "/>";
        string body;
        switch (consumer)
        {
            case "direct": body = source; break;
            case "group": body = "<g filter='url(#f)'>" + source + "</g>"; break;
            case "use": definitions += source; body = "<use href='#s'/>"; break;
            case "use-flood": definitions += source; body = "<use href='#s' filter='url(#f)'/>"; break;
            case "symbol": definitions += "<symbol id='q' viewBox='0 0 32 32'>" + source + "</symbol>"; body = "<use href='#q' width='32' height='32'/>"; break;
            case "viewport": body = "<svg width='32' height='32' viewBox='0 0 32 32'>" + source + "</svg>"; break;
            case "pattern": definitions += "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + source + "</pattern>"; body = "<rect width='32' height='32' fill='url(#p)'/>"; break;
            default: definitions += "<mask id='k' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + source + "</mask>"; body = "<rect width='32' height='32' fill='red' mask='url(#k)'/>"; break;
        }
        return "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + body + "</svg>";
    }
}
