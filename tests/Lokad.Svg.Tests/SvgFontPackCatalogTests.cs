using System.Text;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed class SvgFontPackCatalogTests
{

    [Fact]
    public void Does_not_expose_manifest_arrays_for_mutation()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse("""
            { "packId": "pack", "files": [{ "relativePath": "fonts/example.ttf", "byteSize": 1, "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }], "families": [{ "requestedFamily": "Example", "resolvedFamily": "Example", "relativeFontFile": "fonts/example.ttf" }], "fallbacks": [{ "family": "Fallback" }] }
            """u8.ToArray(), "pack");

        Assert.False(catalog.Files is SvgFontPackFile[]);
        Assert.False(catalog.Faces is SvgFontPackFace[]);
        Assert.False(catalog.FallbackFamilies is string[]);
        Assert.Throws<NotSupportedException>(() => ((IList<SvgFontPackFile>)catalog.Files)[0] = catalog.Files[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<SvgFontPackFace>)catalog.Faces)[0] = catalog.Faces[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)catalog.FallbackFamilies)[0] = "Other");
        Assert.Equal("fonts/example.ttf", Assert.Single(catalog.Files).RelativePath);
    }

    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void Parses_public_manifest_defaults_case_insensitively_and_ignores_unknown_properties()
    {
        SvgFontPackCatalog catalog = Parse("""
            {
              "PACKID": "test-pack",
              "files": [{ "relativePath": "files/regular.ttf", "byteSize": 12, "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ignored": true }],
              "families": [{ "requestedFamily": "Example", "resolvedFamily": "Example Resolved", "relativeFontFile": "files/regular.ttf" }],
              "fallbacks": [{ "family": "Example" }, { "family": "example" }],
              "ignored": "value"
            }
            """);

        SvgFontPackFace face = Assert.Single(catalog.Faces);
        Assert.Equal("test-pack", catalog.PackId);
        Assert.Equal(HashA, Assert.Single(catalog.Files).Sha256);
        Assert.Equal(400, face.Weight);
        Assert.False(face.Italic);
        Assert.Equal(0, face.FaceIndex);
        Assert.False(face.HasMathTable);
        Assert.Equal(new[] { "Example" }, catalog.FallbackFamilies);
    }

    [Fact]
    public void Resolves_requested_and_resolved_family_names_with_deterministic_style_selection()
    {
        SvgFontPackCatalog catalog = Parse(Manifest(
            """{ "requestedFamily": "Alias", "resolvedFamily": "Family", "relativeFontFile": "files/regular.ttf", "weight": 400, "italic": false }""",
            """{ "requestedFamily": "Alias", "resolvedFamily": "Family", "relativeFontFile": "files/bold.ttf", "weight": 700, "italic": false }""",
            """{ "requestedFamily": "Alias", "resolvedFamily": "Family", "relativeFontFile": "files/italic.ttf", "weight": 400, "italic": true }"""));

        SvgFontPackResolution alias = catalog.Resolve(new SvgFontFaceRequest("alias", 700, false));
        SvgFontPackResolution resolved = catalog.Resolve(new SvgFontFaceRequest("FAMILY", 400, true));

        Assert.False(alias.IsFallback);
        Assert.Equal("files/bold.ttf", alias.Face.File.RelativePath);
        Assert.False(resolved.IsFallback);
        Assert.Equal("files/italic.ttf", resolved.Face.File.RelativePath);
    }

    [Fact]
    public void Resolves_configured_fallback_before_the_deterministic_final_face()
    {
        SvgFontPackCatalog catalog = Parse(Manifest(
            """{ "requestedFamily": "First", "resolvedFamily": "First", "relativeFontFile": "files/first.ttf", "weight": 400, "italic": false }""",
            """{ "requestedFamily": "Fallback", "resolvedFamily": "Fallback", "relativeFontFile": "files/fallback.ttf", "weight": 400, "italic": false }""",
            """{ "requestedFamily": "Math", "resolvedFamily": "Math", "relativeFontFile": "files/math.ttf", "weight": 400, "italic": false, "hasMathTable": true }""",
            fallbacks: """[{ "family": "Fallback" }]"""));

        SvgFontPackResolution configured = catalog.Resolve(new SvgFontFaceRequest("missing", 400, false));
        SvgFontPackCatalog noFallback = Parse(Manifest(
            """{ "requestedFamily": "Math", "resolvedFamily": "Math", "relativeFontFile": "files/math.ttf", "weight": 400, "italic": false, "hasMathTable": true }""",
            """{ "requestedFamily": "Text", "resolvedFamily": "Text", "relativeFontFile": "files/text.ttf", "weight": 400, "italic": false }"""));
        SvgFontPackResolution final = noFallback.Resolve(new SvgFontFaceRequest("missing", 400, false));

        Assert.True(configured.IsFallback);
        Assert.Equal("files/fallback.ttf", configured.Face.File.RelativePath);
        Assert.True(final.IsFallback);
        Assert.Equal("files/text.ttf", final.Face.File.RelativePath);
    }

    [Theory]
    [InlineData("../file.ttf")]
    [InlineData("files\\file.ttf")]
    [InlineData("/file.ttf")]
    [InlineData("files/%2e%2e/file.ttf")]
    [InlineData("files//file.ttf")]
    public void Rejects_unsafe_font_file_paths(string path)
    {
        SvgException exception = Assert.Throws<SvgException>(() => Parse(Manifest("""{ "requestedFamily": "Example", "resolvedFamily": "Example", "relativeFontFile": "REPLACE", "weight": 400, "italic": false }""".Replace("REPLACE", path, StringComparison.Ordinal))));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_a_mismatched_pack_identifier()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(Manifest("""{ "requestedFamily": "Example", "resolvedFamily": "Example", "relativeFontFile": "files/regular.ttf", "weight": 400, "italic": false }""", "other-pack")), "test-pack"));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_an_unsafe_requested_pack_identifier()
    {
        Assert.Throws<ArgumentException>(() => SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(Manifest("""{ "requestedFamily": "Example", "resolvedFamily": "Example", "relativeFontFile": "files/regular.ttf", "weight": 400, "italic": false }""")), "../test-pack"));
    }

    [Fact]
    public void Rejects_invalid_manifest_integrity_metadata_and_oversized_manifests()
    {
        SvgException invalidHash = Assert.Throws<SvgException>(() => Parse("""
            { "packId": "test-pack", "files": [{ "relativePath": "files/a.ttf", "byteSize": 1, "sha256": "not-a-hash" }], "families": [{ "requestedFamily": "A", "resolvedFamily": "A", "relativeFontFile": "files/a.ttf" }] }
            """));
        SvgException oversized = Assert.Throws<SvgException>(() => SvgFontPackCatalog.Parse(new byte[1024 * 1024 + 1], "test-pack"));

        Assert.Equal(SvgErrorCode.MalformedInput, invalidHash.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, oversized.ErrorCode);
    }

    private static SvgFontPackCatalog Parse(string manifest) => SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");

    private static string Manifest(string firstFace, string? secondFace = null, string? thirdFace = null, string? fallbacks = null, string packId = "test-pack")
    {
        string?[] candidates = [firstFace, secondFace, thirdFace];
        string[] faces = candidates.Where(static face => face is not null).Cast<string>().ToArray();
        string[] paths = faces.Select(face => face.Contains("bold.ttf", StringComparison.Ordinal) ? "files/bold.ttf" : face.Contains("italic.ttf", StringComparison.Ordinal) ? "files/italic.ttf" : face.Contains("fallback.ttf", StringComparison.Ordinal) ? "files/fallback.ttf" : face.Contains("first.ttf", StringComparison.Ordinal) ? "files/first.ttf" : face.Contains("math.ttf", StringComparison.Ordinal) ? "files/math.ttf" : face.Contains("text.ttf", StringComparison.Ordinal) ? "files/text.ttf" : "files/regular.ttf").Distinct(StringComparer.Ordinal).ToArray();
        string files = string.Join(",", paths.Select((path, index) => "{ \"relativePath\": \"" + path + "\", \"byteSize\": " + (index + 1) + ", \"sha256\": \"" + (index == 0 ? HashA : HashB) + "\" }"));
        return "{ \"packId\": \"" + packId + "\", \"files\": [" + files + "], \"families\": [" + string.Join(",", faces) + "], \"fallbacks\": " + (fallbacks ?? "[]") + " }";
    }
}
