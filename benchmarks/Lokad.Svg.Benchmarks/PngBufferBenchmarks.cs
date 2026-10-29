using BenchmarkDotNet.Attributes;
using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Benchmarks;

/// <summary>Compares PNG output allocation traffic and accounted peaks across sinks and entropies near the encoded-output limit.</summary>
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
    private int _highLength;
    private int _lowLength;

    [GlobalSetup]
    public void SetUp()
    {
        _highEntropy = CreateHighEntropyPixels();
        _lowEntropy = CreateLowEntropyPixels();
        _highLength = PngEncoder.Encode(512, 512, _highEntropy, CancellationToken.None).Length;
        _lowLength = PngEncoder.Encode(512, 512, _lowEntropy, CancellationToken.None).Length;
        _callerDestination = new byte[_highLength];
        _lowCallerDestination = new byte[_lowLength];
        _stream = new MemoryStream(_highLength);
        _lowStream = new MemoryStream(_lowLength);
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

    [Benchmark]
    public long HighEntropyOwnedPeak()
    {
        RenderBudget budget = new RenderBudget();
        PngEncoder.Encode(512, 512, _highEntropy, budget, int.MaxValue, CancellationToken.None);
        return budget.Snapshot().PeakBytes;
    }

    [Benchmark]
    public long HighEntropyStreamPeak()
    {
        RenderBudget budget = new RenderBudget();
        ResetStream(_stream);
        PngEncoder.EncodeTo(512, 512, _highEntropy, _stream, budget, int.MaxValue, CancellationToken.None);
        return budget.Snapshot().PeakBytes;
    }

    [Benchmark]
    public long LowEntropyOwnedPeak()
    {
        RenderBudget budget = new RenderBudget();
        PngEncoder.Encode(512, 512, _lowEntropy, budget, int.MaxValue, CancellationToken.None);
        return budget.Snapshot().PeakBytes;
    }

    [Benchmark]
    public long LowEntropyStreamPeak()
    {
        RenderBudget budget = new RenderBudget();
        ResetStream(_lowStream);
        PngEncoder.EncodeTo(512, 512, _lowEntropy, _lowStream, budget, int.MaxValue, CancellationToken.None);
        return budget.Snapshot().PeakBytes;
    }

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