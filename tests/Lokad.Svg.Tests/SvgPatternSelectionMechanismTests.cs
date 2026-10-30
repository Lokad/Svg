using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgPatternSelectionMechanismTests
{
    private enum Selection { None, Serif, Unsupported }
    private readonly record struct SelectionCase(string Source, string SourceSha256, Selection Expected);

    private static readonly Lazy<IReadOnlyDictionary<string, SelectionCase>> Inputs = new(static () =>
    {
        using JsonDocument corpus = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "pattern-selection-acceptance.json")));
        if (corpus.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unknown pattern-selection corpus schema.");
        return corpus.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(
            row => row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing source ID."),
            row => new SelectionCase(
                row.GetProperty("source").GetString() ?? throw new InvalidDataException("Missing source."),
                row.GetProperty("sourceSha256").GetString() ?? throw new InvalidDataException("Missing source hash."),
                row.GetProperty("selection").GetString() switch
                {
                    "none" => Selection.None,
                    "serif" => Selection.Serif,
                    "unsupported" => Selection.Unsupported,
                    _ => throw new InvalidDataException("Unknown font-selection expectation.")
                }), StringComparer.Ordinal);
    });

    public static IEnumerable<object[]> Cases()
    {
        foreach (string id in Inputs.Value.Keys) yield return [id];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original_resource_graph_selects_only_the_required_font(string id)
    {
        SelectionCase input = Inputs.Value[id];
        byte[] source = TextEncoding.UTF8.GetBytes(input.Source);
        Assert.Equal(input.SourceSha256, Convert.ToHexStringLower(SHA256.HashData(source)));

        if (input.Expected == Selection.Unsupported)
            Assert.Equal(SvgErrorCode.UnsupportedFeature,
                Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(source, CancellationToken.None)).ErrorCode);
        else
        {
            SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(source, CancellationToken.None);
            if (input.Expected == Selection.None) Assert.Empty(requests);
            else Assert.Equal("serif", Assert.Single(requests).Family);
        }
        Assert.Equal(input.SourceSha256, Convert.ToHexStringLower(SHA256.HashData(source)));
    }
}
