using BenchmarkDotNet.Attributes;
using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.Benchmarks;

/// <summary>Compares PNG encoding time/allocation across sinks and entropies; reports tracked storage separately.</summary>
[MemoryDiagnoser]
public class PngBufferBenchmarks
{
    private byte[] _highEntropy = [];
    private byte[] _lowEntropy = [];
    private byte[] _callerDestination = [];
    private byte[] _lowCallerDestination = [];
    private MemoryStream _stream = new();
    private MemoryStream _lowStream = new();
    private RenderBudget _encodeBudget = new RenderBudget();

    [GlobalSetup]
    public void SetUp()
    {
        _highEntropy = CreateHighEntropyPixels();
        _lowEntropy = CreateLowEntropyPixels();
        int highLength = PngEncoder.Encode(512, 512, _highEntropy, CancellationToken.None).Length;
        int lowLength = PngEncoder.Encode(512, 512, _lowEntropy, CancellationToken.None).Length;
        _callerDestination = new byte[highLength];
        _lowCallerDestination = new byte[lowLength];
        _stream.Dispose();
        _lowStream.Dispose();
        _stream = new MemoryStream(highLength);
        _lowStream = new MemoryStream(lowLength);
    }

    [GlobalCleanup]
    public void CleanUp()
    {
        _stream.Dispose();
        _lowStream.Dispose();
    }

    [Benchmark]
    public int EncodeHighEntropyOwned() => PngEncoder.Encode(512, 512, _highEntropy, CancellationToken.None).Length;

    [Benchmark]
    public int EncodeHighEntropyCallerMemory() => PngEncoder.EncodeInto(512, 512, _highEntropy, _callerDestination, CancellationToken.None);

    [Benchmark]
    public int EncodeHighEntropyStream()
    {
        ResetStream(_stream);
        return PngEncoder.EncodeTo(512, 512, _highEntropy, _stream, _encodeBudget, int.MaxValue, CancellationToken.None);
    }

    [Benchmark]
    public async Task<int> EncodeHighEntropyStreamAsync()
    {
        ResetStream(_stream);
        return await PngEncoder.EncodeToAsync(512, 512, _highEntropy, _stream, _encodeBudget, int.MaxValue, CancellationToken.None);
    }

    [Benchmark]
    public int EncodeLowEntropyOwned() => PngEncoder.Encode(512, 512, _lowEntropy, CancellationToken.None).Length;

    [Benchmark]
    public int EncodeLowEntropyCallerMemory() => PngEncoder.EncodeInto(512, 512, _lowEntropy, _lowCallerDestination, CancellationToken.None);

    [Benchmark]
    public int EncodeLowEntropyStream()
    {
        ResetStream(_lowStream);
        return PngEncoder.EncodeTo(512, 512, _lowEntropy, _lowStream, _encodeBudget, int.MaxValue, CancellationToken.None);
    }

    [Benchmark]
    public async Task<int> EncodeLowEntropyStreamAsync()
    {
        ResetStream(_lowStream);
        return await PngEncoder.EncodeToAsync(512, 512, _lowEntropy, _lowStream, _encodeBudget, int.MaxValue, CancellationToken.None);
    }

    /// <summary>Writes untimed tracked-storage observations; caller owns the writer and fixture lifetime.
    /// Encoder temporaries finish before each snapshot; source/caller stream buffers remain fixture-owned.
    /// Call only between setup and cleanup, without concurrent benchmark operations.</summary>
    internal void WriteMemoryReport(TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var report = new
        {
            measurement = "renderer-owned storage reservations; excludes caller inputs, stream capacity and process/native memory",
            width = 512,
            height = 512,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            librarySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(SvgConverter).Assembly.Location))),
            highEntropy = Observe(_highEntropy, _stream),
            lowEntropy = Observe(_lowEntropy, _lowStream),
        };
        destination.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        static PngStorageObservation Observe(byte[] pixels, MemoryStream stream)
        {
            string inputHash = Convert.ToHexStringLower(SHA256.HashData(pixels));
            RenderBudget owned = new(), streamed = new();
            byte[] png = PngEncoder.Encode(512, 512, pixels, owned, int.MaxValue, CancellationToken.None);
            ResetStream(stream);
            int length = PngEncoder.EncodeTo(512, 512, pixels, stream, streamed, int.MaxValue, CancellationToken.None);
            if (length != png.Length || !stream.GetBuffer().AsSpan(0, length).SequenceEqual(png))
                throw new InvalidOperationException("Owned and streamed PNG outputs differ.");
            RenderBudgetSnapshot ownedSnapshot = owned.Snapshot(), streamSnapshot = streamed.Snapshot();
            if (ownedSnapshot.CurrentBytes != 0 || streamSnapshot.CurrentBytes != 0)
                throw new InvalidOperationException("PNG observation retained encoder scratch.");
            if (inputHash != Convert.ToHexStringLower(SHA256.HashData(pixels)))
                throw new InvalidOperationException("PNG observation modified caller pixels.");
            return new(inputHash, length, Convert.ToHexStringLower(SHA256.HashData(png)), ownedSnapshot, streamSnapshot);
        }
    }

    private readonly record struct PngStorageObservation(string RgbaSha256, int EncodedBytes, string PngSha256,
        RenderBudgetSnapshot Owned, RenderBudgetSnapshot Stream);

    private static void ResetStream(MemoryStream stream)
    {
        stream.Position = 0;
        stream.SetLength(0);
    }

    private static byte[] CreateHighEntropyPixels()
    {
        byte[] pixels = new byte[512 * 512 * 4];
        uint state = 0x12345678;
        for (int index = 0; index < pixels.Length; index++) { state = state * 1664525 + 1013904223; pixels[index] = (byte)(state >> 24); }
        return pixels;
    }

    private static byte[] CreateLowEntropyPixels()
    {
        byte[] pixels = new byte[512 * 512 * 4];
        for (int index = 0; index < pixels.Length; index += 4) { pixels[index] = 200; pixels[index + 1] = 30; pixels[index + 2] = 40; pixels[index + 3] = 255; }
        return pixels;
    }
}
