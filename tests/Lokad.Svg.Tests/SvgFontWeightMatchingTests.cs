using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed partial class SvgFontPackCatalogTests
{
    [Theory]
    [InlineData(300, new int[] { 100, 400 }, 100)]
    [InlineData(700, new int[] { 600, 900 }, 900)]
    [InlineData(400, new int[] { 300, 500 }, 500)]
    [InlineData(450, new int[] { 400, 500 }, 500)]
    [InlineData(500, new int[] { 400, 600 }, 400)]
    [InlineData(300, new int[] { 300, 400 }, 300)]
    [InlineData(700, new int[] { 600, 700, 900 }, 700)]
    [InlineData(350, new int[] { 100, 360 }, 100)]
    [InlineData(550, new int[] { 500, 900 }, 900)]
    [InlineData(499, new int[] { 100, 501 }, 100)]
    [InlineData(501, new int[] { 100, 502 }, 502)]
    [InlineData(400, new int[] { 300, 600 }, 300)]
    [InlineData(500, new int[] { 300, 600 }, 300)]
    [InlineData(400, new int[] { 400, 500 }, 400)]
    [InlineData(500, new int[] { 400, 500 }, 500)]
    [InlineData(1, new int[] { 1, 1000 }, 1)]
    [InlineData(1000, new int[] { 1, 1000 }, 1000)]
    public void CSS_weight_fallback_follows_directional_order_independently_of_manifest_order(int requested, int[] weights, int expected)
    {
        SvgFontFaceRequest request = SvgFontFaceRequest.FromCss("Example", requested, false);
        SvgFontSelectionPolicy policy = new(new Dictionary<string, string>(), []);
        foreach (int[] ordered in new[] { weights, weights.Reverse().ToArray() })
        {
            SvgFontPackCatalog catalog = WeightCatalog(ordered);
            Assert.Equal(expected, catalog.Resolve(request).Face.Weight);
            Assert.Equal(expected, catalog.ResolveFamilies(request, policy).Face.Weight);
        }
    }

    [Theory]
    [InlineData(300, new int[] { 100, 400 }, 400)]
    [InlineData(700, new int[] { 600, 900 }, 600)]
    [InlineData(400, new int[] { 300, 500 }, 300)]
    [InlineData(450, new int[] { 400, 500 }, 400)]
    [InlineData(500, new int[] { 300, 600 }, 600)]
    public void Literal_weight_requests_keep_nearest_weight_and_deterministic_ties(int requested, int[] weights, int expected)
    {
        foreach (int[] ordered in new[] { weights, weights.Reverse().ToArray() })
            Assert.Equal(expected, WeightCatalog(ordered).Resolve(new SvgFontFaceRequest("Example", requested, false)).Face.Weight);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CSS_weight_matching_keeps_italic_precedence_over_weight_distance(bool italic)
    {
        SvgFontPackCatalog catalog = Parse(Manifest("test-pack", "[]",
            "{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/regular.ttf\", \"weight\": 900, \"italic\": " + italic.ToString().ToLowerInvariant() + " }",
            "{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/italic.ttf\", \"weight\": 700, \"italic\": " + (!italic).ToString().ToLowerInvariant() + " }"));
        SvgFontPackFace face = catalog.Resolve(SvgFontFaceRequest.FromCss("Example", 700, italic)).Face;
        Assert.Equal(900, face.Weight);
        Assert.Equal(italic, face.Italic);
    }

    [Fact]
    public void CSS_weight_matching_keeps_equivalent_face_tie_breakers()
    {
        string[] faces =
        [
            """{ "requestedFamily": "Example", "resolvedFamily": "Zeta", "relativeFontFile": "files/regular.ttf", "weight": 500 }""",
            """{ "requestedFamily": "Example", "resolvedFamily": "Alpha", "relativeFontFile": "files/regular.ttf", "weight": 500 }""",
            """{ "requestedFamily": "Example", "resolvedFamily": "Alpha", "relativeFontFile": "files/bold.ttf", "weight": 500, "faceIndex": 1 }""",
            """{ "requestedFamily": "Example", "resolvedFamily": "Alpha", "relativeFontFile": "files/bold.ttf", "weight": 500 }"""
        ];
        foreach (string[] ordered in new[] { faces, faces.Reverse().ToArray() })
        {
            SvgFontPackFace selected = Parse(Manifest("test-pack", "[]", ordered)).Resolve(SvgFontFaceRequest.FromCss("Example", 400, false)).Face;
            Assert.Equal("Alpha", selected.ResolvedFamily);
            Assert.Equal("files/bold.ttf", selected.File.RelativePath);
            Assert.Equal(0, selected.FaceIndex);
        }
    }

    [Fact]
    public void CSS_weight_matching_keeps_family_order_and_host_fallback_authorization()
    {
        SvgFontPackCatalog catalog = Parse(Manifest("test-pack", "[]",
            """{ "requestedFamily": "First", "resolvedFamily": "First", "relativeFontFile": "files/first.ttf", "weight": 300 }""",
            """{ "requestedFamily": "Other", "resolvedFamily": "Other", "relativeFontFile": "files/fallback.ttf", "weight": 700 }"""));
        Assert.Equal("First", catalog.Resolve(SvgFontFaceRequest.FromCss("First, Other", 700, false)).Face.ResolvedFamily);
        SvgFontFaceRequest missing = SvgFontFaceRequest.FromCss("Absent", 700, false);
        SvgFontSelectionPolicy denied = new(new Dictionary<string, string>(), []);
        Assert.Equal(SvgErrorCode.UnresolvedResource, Assert.Throws<SvgException>(() => catalog.ResolveFamilies(missing, denied)).ErrorCode);
        SvgFontSelectionPolicy allowed = new(new Dictionary<string, string>(), ["First"]);
        SvgFontPackResolution fallback = catalog.ResolveFamilies(missing, allowed);
        Assert.Equal("First", fallback.Face.ResolvedFamily);
        Assert.True(fallback.IsFallback);
    }

    [Fact]
    public void CSS_weight_matching_preserves_bounded_scans_and_cancellation()
    {
        SvgFontPackCatalog catalog = WeightCatalog([600, 900]);
        SvgFontFaceRequest request = SvgFontFaceRequest.FromCss("Example", 700, false);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            catalog.Resolve(request, new ParserWorkCounter(1), CancellationToken.None)).ErrorCode);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            catalog.Resolve(request, new ParserWorkCounter(100), cancellation.Token)).CancellationToken);
        Assert.Equal(900, catalog.Resolve(request).Face.Weight);
    }

    private static SvgFontPackCatalog WeightCatalog(int[] weights)
        => Parse(JsonSerializer.Serialize(new
        {
            packId = "test-pack",
            files = weights.Select(weight => new { relativePath = $"w{weight:D4}.ttf", byteSize = 1, sha256 = HashA }),
            families = weights.Select(weight => new { requestedFamily = "Example", resolvedFamily = "Example", relativeFontFile = $"w{weight:D4}.ttf", weight })
        }));
}
