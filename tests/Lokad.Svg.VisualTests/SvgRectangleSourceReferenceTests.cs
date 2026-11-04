using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.VisualTests;

public sealed class SvgRectangleSourceReferenceTests
{
    private static readonly PaintReferenceBank Bank = new("rectangle-shader-frames", "admission-review.json");

    public static IEnumerable<object[]> Cases() => Bank.CaseIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public void Retained_solid_strokes_and_shader_fills_match_each_original_native_image(string id)
    {
        JsonElement row = Bank.GetCase(id);
        byte[] input = TextEncoding.UTF8.GetBytes(CreateSource(row));
        Assert.Equal(Required(row, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
        using SvgPngResult png = SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None);
        RgbaImage actual = PngRgbaDecoder.Decode(png.Memory, 2304);
        JsonElement native = row.GetProperty("native");
        Assert.Equal(native.GetProperty("width").GetInt32(), actual.Width);
        Assert.Equal(native.GetProperty("height").GetInt32(), actual.Height);
        for (int offset = 0; offset < actual.Pixels.Length; offset += 4)
            if (actual.Pixels[offset + 3] == 0) actual.Pixels.AsSpan(offset, 3).Clear();
        Assert.Equal(Required(native, "rgbaSha256"), Convert.ToHexStringLower(SHA256.HashData(actual.Pixels)));

        static string CreateSource(JsonElement row)
        {
            if (row.TryGetProperty("source", out JsonElement source))
                return source.GetString() ?? throw new InvalidDataException("Missing authored rectangle source.");
            JsonElement metadata = row.GetProperty("metadata");
            string width = Required(metadata, "width"), pose = Required(metadata, "pose");
            bool symbol = Required(metadata, "consumer") == "symbol";
            string rectangle = "<rect x='4' y='4' width='20' height='20' fill='url(#q)' stroke='blue' stroke-width='"
                + width + "' stroke-opacity='.75' />";
            string definition = symbol ? "<symbol id='s' viewBox='0 0 32 32'>" + rectangle + "</symbol>"
                : rectangle.Replace("/>", " id='s'/>", StringComparison.Ordinal);
            return "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
                + "<linearGradient id='q'><stop stop-color='yellow'/><stop offset='1' stop-color='cyan'/></linearGradient>"
                + definition + "</defs><g transform='" + pose + "'><use href='#s'"
                + (symbol ? " width='32' height='32'" : "") + "/></g></svg>";
        }
    }

    private static string Required(JsonElement row, string property)
        => row.GetProperty(property).GetString() ?? throw new InvalidDataException("Missing " + property + ".");
}
