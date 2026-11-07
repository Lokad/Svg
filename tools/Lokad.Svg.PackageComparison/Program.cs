using Lokad.Svg;
using Lokad.Svg.Fonts;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;

bool observeProcess = args is [_, _, "--observe-process"];
if (args.Length != 2 && !observeProcess) throw new ArgumentException("Expected manifest, new output directory and optional --observe-process.");
long entry = observeProcess ? Stopwatch.GetTimestamp() : 0;
double bufferedReadMilliseconds = 0;
string manifestPath = Path.GetFullPath(args[0]);
string directory = Path.GetDirectoryName(manifestPath) ?? throw new InvalidDataException();
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Use a new output directory to preserve previous observations.");
Directory.CreateDirectory(output);
using JsonDocument manifest = JsonDocument.Parse(ReadInput(manifestPath, observeProcess, ref bufferedReadMilliseconds));
JsonElement root = manifest.RootElement;
if (observeProcess && root.GetProperty("cases").GetArrayLength() != 1)
    throw new ArgumentException("Process observation requires a single-document manifest.");
HashSet<string> requestedFonts = root.GetProperty("cases").EnumerateArray()
    .SelectMany(item => item.GetProperty("fonts").EnumerateArray())
    .Select(item => item.GetString() ?? throw new InvalidDataException("Missing font name.")).ToHashSet(StringComparer.Ordinal);
long inputStarted = observeProcess ? Stopwatch.GetTimestamp() : 0;
SvgFontPackCatalog? catalog = !observeProcess || requestedFonts.Count != 0
    ? SvgFontPackCatalog.Parse(ReadInput(Path.Combine(directory, "font-pack.json"), observeProcess, ref bufferedReadMilliseconds), "upstream-noto-sans") : null;
Dictionary<string, (JsonElement Metadata, byte[] Bytes)> programs = new(StringComparer.Ordinal);
foreach (JsonElement program in root.GetProperty("fontPrograms").EnumerateArray())
{
    string path = Required(program, "file");
    if (observeProcess && !requestedFonts.Contains(Path.GetFileName(path))) continue;
    byte[] bytes = ReadInput(Path.Combine(directory, path), observeProcess, ref bufferedReadMilliseconds);
    if (Hash(bytes) != Required(program, "sha256")) throw new InvalidDataException("A supplied font changed.");
    programs.Add(Path.GetFileName(path), (program, bytes));
}
// Input I/O and font acquisition are deliberately outside conversion calls.
var inputs = root.GetProperty("cases").EnumerateArray().Select(item =>
{
    byte[] bytes = ReadInput(Path.Combine(directory, Required(item, "input")), observeProcess, ref bufferedReadMilliseconds);
    if (Hash(bytes) != Required(item, "sourceSha256")) throw new InvalidDataException("A document changed.");
    return (Metadata: item, Bytes: bytes);
}).ToArray();
double inputMilliseconds = observeProcess ? Stopwatch.GetElapsedTime(inputStarted).TotalMilliseconds : 0;
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
            SvgFontPackCatalog selectedCatalog = catalog ?? throw new InvalidDataException("Missing font catalog.");
            resolutions.Add(selectedCatalog.Bind(selectedCatalog.Resolve(request), new SvgMemoryFontProgramSource(file, program)));
            fontHashes.Add(Hash(program));
        }
        SvgRenderOptions options = new(new SvgViewport(width, height), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        if (resolutions.Count != 0)
        {
            snapshot = await SvgFontSnapshot.CreateAsync(resolutions, CancellationToken.None);
            options = new(new SvgViewport(width, height), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        }
        stage = "conversion";
        if (observeProcess)
        {
            Console.WriteLine("SVG_CONSUMER_READY " + JsonSerializer.Serialize(new
            {
                id, inputMilliseconds, bufferedReadMilliseconds, entryToReadyMilliseconds = Stopwatch.GetElapsedTime(entry).TotalMilliseconds,
                sourceSha256 = Hash(bytes),
            }));
            while (true)
            {
                string command = Console.ReadLine() ?? throw new EndOfStreamException("Expected ping or render.");
                if (command == "render") break;
                if (command != "ping") throw new InvalidDataException("Expected ping or render.");
                Console.WriteLine("SVG_CONSUMER_PONG");
            }
        }
        long conversionStarted = observeProcess ? Stopwatch.GetTimestamp() : 0;
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(bytes, options, CancellationToken.None);
        if (observeProcess)
            Console.WriteLine("SVG_CONSUMER_RENDERED " + JsonSerializer.Serialize(new
            {
                id, conversionMilliseconds = Stopwatch.GetElapsedTime(conversionStarted).TotalMilliseconds,
            }));
        byte[] repeated = SvgConverter.ConvertToPng(bytes, options, CancellationToken.None).ToArray();
        if (!png.Span.SequenceEqual(repeated)) throw new InvalidDataException("Repeated conversion changed pixels or encoding.");
        if (Hash(bytes) != Required(item, "sourceSha256")) throw new InvalidDataException("Conversion modified caller input.");
        long writeStarted = observeProcess ? Stopwatch.GetTimestamp() : 0;
        File.WriteAllBytes(Path.Combine(output, id + ".png"), png.Span);
        if (observeProcess)
            Console.WriteLine("SVG_CONSUMER_WRITTEN " + JsonSerializer.Serialize(new
            {
                id, bytes = png.Length, bufferedWriteMilliseconds = Stopwatch.GetElapsedTime(writeStarted).TotalMilliseconds,
            }));
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
    timedExecutions = observeProcess ? 1 : 0,
    scope = observeProcess
        ? "Single-document public consumer with requested font bytes only. Ready includes manifest/input/font I/O, validation and acquisition; ping/pong precedes rendering. First conversion timer excludes pipe waits and output writes; repeated conversion verifies exactness. Buffered-write timing includes File.WriteAllBytes without fsync. No warmed throughput, physical cold-cache I/O, IPC-only roundtrip or native-memory claim."
        : "Same-process public conversion with fixed input/font bytes. No internal raster-only, allocation or performance measurement."
};
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(output, "report.json"), json + "\n");
Console.WriteLine(json);

static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");

static byte[] ReadInput(string path, bool observe, ref double milliseconds)
{
    long start = observe ? Stopwatch.GetTimestamp() : 0;
    byte[] bytes = File.ReadAllBytes(path);
    if (observe) milliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    return bytes;
}
