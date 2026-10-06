using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed class SvgDoctypeReferenceTests
{
    [Fact]
    public void Legacy_declarations_preserve_original_input_identity_and_do_not_affect_pixels()
    {
        using JsonDocument regressions = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests/References/declaration-only-doctype.json")));
        JsonElement[] fixtures = regressions.RootElement.GetProperty("inputs").EnumerateArray().ToArray();
        Assert.Equal(new[] { "inv-007", "inv-012", "inv-014", "inv-020" }, fixtures.Select(fixture => Required(fixture, "id")).ToArray());
        foreach (JsonElement fixture in fixtures)
        {
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/candidates", Required(fixture, "id") + ".svg"));
            Assert.Equal(Required(fixture, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
            byte[] png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
            // Removing the inert declaration must not affect rendered pixels.
            byte[] twin = SvgConverter.ConvertToPng(WithoutDeclaration(input), SvgRenderOptions.Default, CancellationToken.None).ToArray();
            Assert.Equal(twin, png);
        }

        static string Required(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name);

        static byte[] WithoutDeclaration(byte[] source)
        {
            // These four fixed UTF-8 fixtures use PUBLIC identifiers with no
            // '>' inside their quoted strings. Preserve every other input byte.
            int start = source.AsSpan().IndexOf("<!DOCTYPE"u8);
            Assert.True(start >= 0);
            int end = source.AsSpan(start).IndexOf((byte)'>');
            Assert.True(end > 0);
            int length = end + 1;
            byte[] result = new byte[source.Length - length];
            source.AsSpan(0, start).CopyTo(result);
            source.AsSpan(start + length).CopyTo(result.AsSpan(start));
            return result;
        }
    }
}
