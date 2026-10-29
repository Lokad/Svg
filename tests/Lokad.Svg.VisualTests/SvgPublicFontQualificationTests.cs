using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Qualifies four public requests through catalog/provider acquisition, independent metrics and the actual FontProbe executable.</summary>
public sealed class SvgPublicFontQualificationTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "public-font-qualification");

    public static IEnumerable<object[]> Requests()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task Public_requests_acquire_the_intended_face_match_independent_metrics_and_render_exactly_through_the_host(string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        string sourceDirectory = DirectoryPath;
        JsonElement visual = item;
        using JsonDocument? reused = item.TryGetProperty("sourceManifest", out JsonElement sourceManifest)
            ? JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, sourceManifest.GetString() ?? throw new InvalidDataException()))) : null;
        if (reused is not null)
        {
            sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(DirectoryPath, Required(item, "sourceManifest")))) ?? throw new InvalidDataException();
            visual = reused.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == Required(item, "sourceCase"));
        }
        byte[] input = File.ReadAllBytes(Path.Combine(sourceDirectory, Required(visual, "input")));
        Assert.Equal(Required(visual, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        string platform = OperatingSystem.IsLinux() ? "linux" : "windows";
        byte[] referenceBytes = File.ReadAllBytes(Path.Combine(sourceDirectory, Required(visual, platform == "linux" ? "linuxReference" : "reference")));
        Assert.Equal(Required(visual, platform == "linux" ? "linuxReferenceSha256" : "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(referenceBytes)));

        Dictionary<string, byte[]> programs = manifest.RootElement.GetProperty("programs").EnumerateArray().ToDictionary(
            p => Required(p, "relativePath"), p => File.ReadAllBytes(Path.Combine(DirectoryPath, Required(p, "source"))), StringComparer.Ordinal);
        TrackingProvider provider = new(programs);
        byte[] catalogBytes = File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json"));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(catalogBytes, "public-standalone-fonts");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        JsonElement request = item.GetProperty("request");
        SvgPreparedFont font = snapshot.GetFont(new(Required(request, "family"), request.GetProperty("weight").GetInt32(), request.GetProperty("italic").GetBoolean()));
        SvgFontPackResolution selection = font.Resolution.Resolution;
        Assert.Equal(Required(item, "expectedFile"), selection.Face.File.RelativePath);
        Assert.Equal(Required(item, "expectedResolvedFamily"), selection.Face.ResolvedFamily);
        Assert.Equal(request.GetProperty("weight").GetInt32(), selection.Face.Weight);
        Assert.Equal(request.GetProperty("italic").GetBoolean(), selection.Face.Italic);
        Assert.Equal(item.GetProperty("expectedFallback").GetBoolean(), selection.IsFallback);
        string[] acquisitions = item.GetProperty("expectedAcquisitions").EnumerateArray().Select(v => v.GetString() ?? throw new InvalidDataException()).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(acquisitions, provider.RequestedFiles.Order(StringComparer.Ordinal).ToArray());

        using JsonDocument metricsDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "metrics"))));
        JsonElement metrics = metricsDocument.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == Required(item, "metricsCase"));
        Assert.Equal((ushort)(metrics.TryGetProperty("unitsPerEm", out JsonElement units) ? units : metricsDocument.RootElement.GetProperty("unitsPerEm")).GetInt32(), font.UnitsPerEm);
        JsonElement expectedGlyphs = metrics.GetProperty("glyphs");
        SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(font, Required(metrics, "text"), metrics.GetProperty("fontSize").GetDouble(), expectedGlyphs[0].GetProperty("x").GetDouble(), expectedGlyphs[0].GetProperty("y").GetDouble(), CancellationToken.None);
        Assert.Equal(expectedGlyphs.GetArrayLength(), glyphs.Length);
        for (int index = 0; index < glyphs.Length; index++)
        {
            Assert.Equal(expectedGlyphs[index].GetProperty("glyphIndex").GetUInt16(), glyphs[index].GlyphIndex);
            Assert.Equal(expectedGlyphs[index].GetProperty("x").GetDouble(), glyphs[index].X, 10);
            Assert.Equal(expectedGlyphs[index].GetProperty("y").GetDouble(), glyphs[index].Y, 10);
            Assert.Equal(expectedGlyphs[index].GetProperty("advance").GetDouble(), glyphs[index].Advance, 10);
        }

        if (identity == "real-italic")
        {
            using JsonDocument provenance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-provenance.json")));
            Assert.True(provenance.RootElement.GetProperty("realItalic").GetBoolean());
            Assert.Equal(-12, provenance.RootElement.GetProperty("italicAngle").GetDouble());
            Assert.Equal(Required(provenance.RootElement, "sha256"), selection.Face.File.Sha256, StringComparer.OrdinalIgnoreCase);
            using JsonDocument proof = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-use-proof", "qualification.json")));
            foreach (JsonProperty variant in proof.RootElement.GetProperty(platform).EnumerateObject())
            {
                byte[] negative = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(variant.Value, "reference")));
                Assert.Equal(Required(variant.Value, "sha256"), Convert.ToHexStringLower(SHA256.HashData(negative)));
                Assert.Equal(3, variant.Value.GetProperty("captures").GetArrayLength());
                PixelComparison difference = PixelComparer.Compare(PngRgbaDecoder.Decode(referenceBytes, 184 * 76), PngRgbaDecoder.Decode(negative, 184 * 76));
                Assert.False(difference.IsExact);
                Assert.Equal(variant.Value.GetProperty("comparison").Deserialize<PixelComparison>(), difference);
            }
        }

        string temporaryParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string temporary = Path.Combine(temporaryParent, "lokad-svg-public-font-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(temporaryParent, Path.GetDirectoryName(temporary));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach ((string path, byte[] bytes) in programs)
            {
                string destination = Path.Combine(temporary, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidDataException());
                await File.WriteAllBytesAsync(destination, bytes, CancellationToken.None);
            }
            string output = Path.Combine(temporary, "output.png");
            ProcessStartInfo start = new("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            System.Runtime.Versioning.TargetFrameworkAttribute framework = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>(typeof(SvgPublicFontQualificationTests).Assembly)
                ?? throw new InvalidOperationException("The test assembly has no target framework.");
            string targetFramework = "net" + new System.Runtime.Versioning.FrameworkName(framework.FrameworkName).Version.ToString(2);
            string tool = Path.Combine(FindRepository(), "tools", "Lokad.Svg.FontProbe", "bin", "Release", targetFramework, "Lokad.Svg.FontProbe.dll");
            foreach (string argument in new[] { tool, "--manifest", Path.Combine(DirectoryPath, "font-pack.json"), "--pack-id", "public-standalone-fonts", "--font-root", temporary, "--svg", Path.GetFullPath(Path.Combine(sourceDirectory, Required(visual, "input"))), "--output", output })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("The public font host did not start.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, (await standardError).Trim());
            byte[] png = await File.ReadAllBytesAsync(output, CancellationToken.None);
            Assert.Equal("outcome=success\npngBytes=" + png.Length + "\nrequestedFontPrograms=" + acquisitions.Length,
                (await standardOutput).Trim().Replace("\r\n", "\n", StringComparison.Ordinal));
            int pixels = checked(visual.GetProperty("width").GetInt32() * visual.GetProperty("height").GetInt32());
            PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(referenceBytes, pixels), PngRgbaDecoder.Decode(png, pixels));
            Assert.True(comparison.IsExact, identity + ": " + comparison);
        }
        finally { Directory.Delete(temporary, true); }

        static string FindRepository()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
                for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "Lokad.Svg.slnx"))) return directory.FullName;
            throw new DirectoryNotFoundException("Public host qualification requires the repository's built FontProbe.");
        }
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException();

    /// <summary>Owns immutable public test arrays. Concurrent requests are logged safely; each read observes cancellation, performs no I/O and borrows bytes for the snapshot lifetime. Host budgets are tested separately.</summary>
    private sealed class TrackingProvider(IReadOnlyDictionary<string, byte[]> programs) : ISvgFontProgramSourceProvider
    {
        internal ConcurrentBag<string> RequestedFiles { get; } = new();

        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            RequestedFiles.Add(file.RelativePath);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-qualification-" + file.RelativePath, programs[file.RelativePath]));
        }
    }
}
