using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed class SvgDoctypeReferenceTests
{
    [Fact]
    public void Legacy_declarations_preserve_original_input_identity_and_do_not_affect_pixels()
    {
        using JsonDocument checkpoint = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests/References/declaration-only-doctype.json")));
        JsonElement[] changes = checkpoint.RootElement.GetProperty("priorBank").GetProperty("dispositionChanges").EnumerateArray().ToArray();
        Assert.Equal(new[] { "inv-007", "inv-012", "inv-014", "inv-020" }, changes.Select(change => Required(change, "id")).ToArray());
        foreach (JsonElement change in changes)
        {
            JsonElement before = change.GetProperty("before");
            JsonElement after = change.GetProperty("after");
            Assert.Equal("MalformedInput", Required(before, "Outcome"));
            Assert.Equal("renders", Required(after, "Outcome"));
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/candidates", Required(change, "id") + ".svg"));
            string hash = Convert.ToHexString(SHA256.HashData(input));
            Assert.Equal(Required(before, "InputSha256"), hash);
            Assert.Equal(Required(after, "InputSha256"), hash);
            byte[] png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
            // The old PNG is a historical renderer observation, without an
            // independent oracle. Compare this declaration's semantic twin.
            byte[] twin = SvgConverter.ConvertToPng(WithoutDeclaration(input), SvgRenderOptions.Default, CancellationToken.None).ToArray();
            Assert.Equal(twin, png);
            Assert.Equal(JsonValueKind.Null, after.GetProperty("Oracle").ValueKind);
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
