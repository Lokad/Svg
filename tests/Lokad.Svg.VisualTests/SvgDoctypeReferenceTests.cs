using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed class SvgDoctypeReferenceTests
{
    [Fact]
    public void Newly_admitted_legacy_inputs_keep_their_original_bytes_and_recorded_pngs()
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
            Assert.Equal(Required(after, "PngSha256"), Convert.ToHexString(SHA256.HashData(png)));
            Assert.Equal(JsonValueKind.Null, after.GetProperty("Oracle").ValueKind);
        }

        static string Required(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name);
    }
}
