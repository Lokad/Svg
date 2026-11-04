using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Retains the original hairline matrices as individually selectable canonical rendering research.</summary>
public sealed class SvgShaderHairlineOriginalTests
{
    public enum OriginalFamily { Geometry, Alpha }
    public enum OriginalRole { Candidate, Control }
    private readonly record struct NativeReference(string SourceSha256, string WindowsRgbaSha256, string LinuxRgbaSha256);

    private static readonly Lazy<IReadOnlyDictionary<string, NativeReference>> References = new(() =>
    {
        string path = Path.Combine(ResearchCorpus.Root, "shader-hairlines", "original-native.csv.gz");
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "release-baselines", "r86-hairline-qualification.json")));
        Assert.Equal(evidence.RootElement.GetProperty("nativeReferences").GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        using FileStream file = File.OpenRead(path);
        using GZipStream gzip = new(file, CompressionMode.Decompress, false);
        using StreamReader reader = new(gzip, System.Text.Encoding.UTF8, false, 1024, false);
        Assert.Equal("id,sourceSha256,windowsRgbaSha256,linuxRgbaSha256", reader.ReadLine());
        Dictionary<string, NativeReference> rows = new(StringComparer.Ordinal);
        while (reader.ReadLine() is {} line)
        {
            string[] cells = line.Split(',');
            Assert.Equal(4, cells.Length);
            Assert.True(rows.TryAdd(cells[0], new(cells[1], cells[2], cells[3])));
        }
        Assert.Equal(2000, rows.Count);
        return rows;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IEnumerable<object[]> OriginalCases()
    {
        foreach (OriginalFamily family in Enum.GetValues<OriginalFamily>())
        for (int index = 0; index < (family == OriginalFamily.Geometry ? 800 : 200); index++)
        foreach (OriginalRole role in Enum.GetValues<OriginalRole>())
            yield return [family, index, role];
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [Trait("Category", "Research")]
    [MemberData(nameof(OriginalCases))]
    public void Original_hairline_input_matches_its_native_pixels(OriginalFamily family, int index, OriginalRole role)
    {
        string prefix = family == OriginalFamily.Geometry ? "r86-hairline-review-" : "r86-hairline-alpha-";
        string id = prefix + index.ToString(CultureInfo.InvariantCulture) + (role == OriginalRole.Candidate ? "-candidate" : "-control");
        NativeReference reference = References.Value[id];
        byte[] input = System.Text.Encoding.UTF8.GetBytes(Recompose(family, index, role));
        Assert.Equal(reference.SourceSha256, Convert.ToHexStringLower(SHA256.HashData(input)));
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None);
        RgbaImage actual = PngRgbaDecoder.Decode(png, 1024);
        Assert.Equal(32, actual.Width);
        Assert.Equal(32, actual.Height);
        // Preserve PixelComparison's independent alpha-zero RGB convention.
        for (int offset = 0; offset < actual.Pixels.Length; offset += 4)
            if (actual.Pixels[offset + 3] == 0) actual.Pixels.AsSpan(offset, 3).Clear();
        Assert.Equal(OperatingSystem.IsWindows() ? reference.WindowsRgbaSha256 : reference.LinuxRgbaSha256,
            Convert.ToHexStringLower(SHA256.HashData(actual.Pixels)));
        Assert.Equal(reference.SourceSha256, Convert.ToHexStringLower(SHA256.HashData(input)));

        static string Recompose(OriginalFamily family, int index, OriginalRole role)
        {
            bool alphaFamily = family == OriginalFamily.Alpha;
            string fill = index % 2 == 0 ? "none" : "red"; index /= 2;
            string alpha = index % 2 == 0 ? "1" : ".75"; index /= 2;
            string pose = "translate(.5 .25)";
            if (!alphaFamily) { if (index % 2 != 0) pose = "rotate(15 16 16)"; index /= 2; }
            int widthIndex = index % (alphaFamily ? 2 : 4); index /= alphaFamily ? 2 : 4;
            int shapeIndex = index % 5, paintIndex = index / 5;
            string width = widthIndex switch { 0 => ".25", 1 => ".5", 2 => "1", _ => "2" };
            string paint = paintIndex switch
            {
                0 => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
                1 => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
                2 => "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='20'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
                3 => "<pattern id='p' patternUnits='userSpaceOnUse' width='16' height='16'><rect width='16' height='16' fill='blue'/></pattern>",
                _ => "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>"
            };
            string shape = shapeIndex switch
            {
                0 => "<path d='M4 4H24V24H4Z' ",
                1 => "<line x1='4' y1='6' x2='26' y2='23' ",
                2 => "<polyline points='4,24 4,4 24,4 24,24' ",
                3 => "<path d='M4 24Q4 4 24 4' ",
                _ => "<path d='M4 24C4 4 24 4 24 24' "
            };
            if (alphaFamily && role == OriginalRole.Control)
            {
                alpha = (width, alpha) switch
                {
                    (".25", "1") => "0.24705882352941178", (".25", ".75") => "0.1843137254901961",
                    (".5", "1") => "0.4980392156862745", _ => "0.37254901960784315"
                };
                width = "1";
            }
            string stroke = alphaFamily || role == OriginalRole.Candidate ? "url(#p)" : "blue";
            return "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + paint + "</defs><g transform='" + pose + "'>"
                + shape + "fill='" + fill + "' stroke='" + stroke + "' stroke-width='" + width + "' stroke-opacity='" + alpha + "'/></g></svg>";
        }
    }

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Original_hairline_matrix_retains_source_and_native_capture_provenance()
    {
        Assert.Equal(2000, References.Value.Count);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "release-baselines", "r86-hairline-qualification.json")));
        JsonElement root = evidence.RootElement;
        Assert.Equal(1000, root.GetProperty("originalPairs").GetInt32());
        Assert.Equal(12000, root.GetProperty("oracle").GetProperty("nativeComparisons").GetInt32());
        Assert.Equal(6, root.GetProperty("oracle").GetProperty("healthyFreshProcesses").GetInt32());
        Assert.Equal(2, root.GetProperty("results").GetArrayLength());
        foreach (JsonElement host in root.GetProperty("results").EnumerateArray())
        {
            Assert.Equal(2000, host.GetProperty("exactInputs").GetInt32());
            Assert.Equal(0, host.GetProperty("oldTargetsChanged").GetInt32());
            Assert.Equal(3, host.GetProperty("launches").GetArrayLength());
            foreach (JsonElement launch in host.GetProperty("launches").EnumerateArray())
            {
                Assert.True(launch.GetProperty("healthy").GetBoolean());
                Assert.True(launch.GetProperty("profileCleaned").GetBoolean());
                Assert.Equal(0, launch.GetProperty("terminalExitCode").GetInt32());
            }
        }
    }
}
