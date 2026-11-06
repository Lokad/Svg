using Lokad.Svg;
using Lokad.Svg.Fonts;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Expected the upstream document manifest and a new output directory.");
string manifestPath = Path.GetFullPath(args[0]);
string directory = Path.GetDirectoryName(manifestPath) ?? throw new InvalidDataException();
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Use a new output directory to preserve previous observations.");
Directory.CreateDirectory(output);
using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
JsonElement root = manifest.RootElement;
SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "font-pack.json")), "upstream-noto-sans");
Dictionary<string, (JsonElement Metadata, byte[] Bytes)> programs = new(StringComparer.Ordinal);
foreach (JsonElement program in root.GetProperty("fontPrograms").EnumerateArray())
{
    string path = Required(program, "file");
    byte[] bytes = File.ReadAllBytes(Path.Combine(directory, path));
    if (Hash(bytes) != Required(program, "sha256")) throw new InvalidDataException("A supplied font changed.");
    programs.Add(Path.GetFileName(path), (program, bytes));
}
// Input I/O and font acquisition are deliberately outside conversion calls.
var inputs = root.GetProperty("cases").EnumerateArray().Select(item =>
{
    byte[] bytes = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
    if (Hash(bytes) != Required(item, "sourceSha256")) throw new InvalidDataException("A document changed.");
    return (Metadata: item, Bytes: bytes);
}).ToArray();
List<object> rows = [];
foreach ((JsonElement item, byte[] bytes) in inputs)
{
    string id = Required(item, "id");
    if (Path.GetFileName(id) != id) throw new InvalidDataException("Fixture IDs must be file names.");
    if (item.GetProperty("scale").GetDouble() != 1 || Required(item, "background") != "transparent")
        throw new InvalidDataException("This comparison requires the original unit-scale transparent recipe.");
    int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
    SvgFontSnapshot? snapshot = null;
    string stage = "font-setup";
    List<string> fontHashes = [];
    try
    {
        List<SvgFontProgramResolution> resolutions = [];
        foreach (JsonElement name in item.GetProperty("fonts").EnumerateArray())
        {
            string file = name.GetString() ?? throw new InvalidDataException();
            (JsonElement metadata, byte[] program) = programs[file];
            SvgFontFaceRequest request = new(Required(metadata, "family"), metadata.GetProperty("weight").GetInt32(), metadata.GetProperty("italic").GetBoolean());
            resolutions.Add(catalog.Bind(catalog.Resolve(request), new SvgMemoryFontProgramSource(file, program)));
            fontHashes.Add(Hash(program));
        }
        SvgRenderOptions options = new(new SvgViewport(width, height), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        if (resolutions.Count != 0)
        {
            snapshot = await SvgFontSnapshot.CreateAsync(resolutions, CancellationToken.None);
            options = new(new SvgViewport(width, height), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        }
        stage = "conversion";
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(bytes, options, CancellationToken.None);
        byte[] repeated = SvgConverter.ConvertToPng(bytes, options, CancellationToken.None).ToArray();
        if (!png.Span.SequenceEqual(repeated)) throw new InvalidDataException("Repeated conversion changed pixels or encoding.");
        if (Hash(bytes) != Required(item, "sourceSha256")) throw new InvalidDataException("Conversion modified caller input.");
        File.WriteAllBytes(Path.Combine(output, id + ".png"), png.Span);
        rows.Add(new { id, outcome = "rendered", width, height, sourceSha256 = Hash(bytes), pngSha256 = Hash(png.Span), fontHashes, repeatedConversions = 2 });
    }
    catch (SvgException exception)
    {
        rows.Add(new { id, outcome = "rejected", stage, errorCode = exception.ErrorCode.ToString(), width, height, sourceSha256 = Hash(bytes), fontHashes });
    }
    finally { snapshot?.Dispose(); }
}
Assembly library = typeof(SvgConverter).Assembly;
var report = new
{
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    librarySha256 = Hash(File.ReadAllBytes(library.Location)),
    libraryTarget = library.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? throw new InvalidDataException(),
    libraryVersion = library.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? throw new InvalidDataException(),
    rows,
    timedExecutions = 0,
    scope = "Same-process public conversion with fixed input/font bytes. No internal raster-only, allocation or performance measurement."
};
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(output, "report.json"), json + "\n");
Console.WriteLine(json);

static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
