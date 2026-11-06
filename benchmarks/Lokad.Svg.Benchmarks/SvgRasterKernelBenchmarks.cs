using BenchmarkDotNet.Attributes;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures bounded raster components independently of XML, scene lowering and PNG encoding.</summary>
[MemoryDiagnoser]
public class SvgRasterKernelBenchmarks
{
    private const int SampleCount = 256;
    private const int BlurSide = 64;
    private SvgDevicePoint[] _contour = [];
    private SvgShaderPaint[] _samples = [];
    private SvgShaderPaint[] _linearSamples = [];
    private SvgShaderPaint[] _sampleOutput = [];
    private byte[] _coverage = [];
    private byte[] _background = [];
    private byte[] _blendOutput = [];
    private byte[] _blurSource = [];
    private byte[] _blurOutput = [];
    private SvgFilterColorMatrix _matrix;
    private SvgGradientRamp _ramp;
    private RenderBudget _rampBudget = new();
    private readonly RenderBudget _blurBudget = new();

    [GlobalSetup]
    public void SetUp()
    {
        CleanUp();
        try
        {
            _contour = [new(2.125, 1.25), new(11.75, 2.5), new(14.25, 8.125),
                new(10.5, 14.75), new(3.25, 13.5), new(.75, 7.25)];
            _blurSource = new byte[BlurSide * BlurSide * 4];
            _blurOutput = new byte[_blurSource.Length];
            for (int y = 0; y < BlurSide; y++)
            for (int x = 0; x < BlurSide; x++)
            {
                int offset = (y * BlurSide + x) * 4;
                int alpha = (x * 17 + y * 29) & 255;
                _blurSource[offset] = (byte)(((x * 37) & 255) * alpha / 255);
                _blurSource[offset + 1] = (byte)(((y * 43) & 255) * alpha / 255);
                _blurSource[offset + 2] = (byte)((((x + y) * 19) & 255) * alpha / 255);
                _blurSource[offset + 3] = (byte)alpha;
            }
            _samples = new SvgShaderPaint[SampleCount];
            _linearSamples = new SvgShaderPaint[SampleCount];
            _sampleOutput = new SvgShaderPaint[SampleCount];
            _coverage = new byte[SampleCount];
            _background = new byte[SampleCount * 4];
            _blendOutput = new byte[_background.Length];
            for (int index = 0; index < SampleCount; index++)
            {
                _samples[index] = SvgShaderPaint.ReadPremultiplied(_blurSource.AsSpan(index * 4, 4));
                _linearSamples[index] = SvgFilterColorTransfer.Convert(_samples[index], SvgFilterColorSpace.LinearRgb);
                _coverage[index] = (byte)((index * 29) & 255);
                _background[index * 4] = 7;
                _background[index * 4 + 1] = 23;
                _background[index * 4 + 2] = 41;
                _background[index * 4 + 3] = 96;
            }
            _matrix = new([.65f, .2f, .05f, 0, .02f, .12f, .75f, .08f, 0, 0,
                .1f, .1f, .7f, 0, .03f, 0, 0, 0, .9f, .04f]);
            _rampBudget = new();
            _ramp = SvgGradientRamp.Create([new(0, new(20, 140, 210, 255)), new(.23, new(230, 50, 90, 140)),
                new(.71, new(30, 210, 70, 230)), new(1, new(210, 140, 30, 70))],
                SvgGradientSpread.Pad, _rampBudget, CancellationToken.None);
        }
        catch { CleanUp(); throw; }
    }

    [Benchmark(OperationsPerInvoke = SampleCount)]
    public double PolygonCoverage()
    {
        double total = 0;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
            total += SvgPolygonCoverage.GetExactCoverage(_contour, x, y, CancellationToken.None);
        return total;
    }

    /// <summary>Includes resetting the caller-owned background, then blends 256 premultiplied samples.</summary>
    [Benchmark(OperationsPerInvoke = SampleCount)]
    public byte[] BlendCoverage()
    {
        _background.CopyTo(_blendOutput, 0);
        for (int index = 0; index < SampleCount; index++)
            _samples[index].Blend(_blendOutput.AsSpan(index * 4, 4), _coverage[index]);
        return _blendOutput;
    }

    [Benchmark(OperationsPerInvoke = SampleCount)]
    public int SampleGradientRamp()
    {
        for (int index = 0; index < SampleCount; index++)
            _sampleOutput[index] = _ramp.SampleAbsolute((index + .5) / SampleCount, .8f, index & 15);
        return _sampleOutput.Length;
    }

    [Benchmark(OperationsPerInvoke = SampleCount)]
    public int ApplyColorMatrix()
    {
        for (int index = 0; index < SampleCount; index++) _sampleOutput[index] = _matrix.Apply(_samples[index]);
        return _sampleOutput.Length;
    }

    [Benchmark(OperationsPerInvoke = SampleCount)]
    public int ConvertToLinearRgb()
    {
        for (int index = 0; index < SampleCount; index++)
            _sampleOutput[index] = SvgFilterColorTransfer.Convert(_samples[index], SvgFilterColorSpace.LinearRgb);
        return _sampleOutput.Length;
    }

    [Benchmark(OperationsPerInvoke = SampleCount)]
    public int ConvertToSrgb()
    {
        for (int index = 0; index < SampleCount; index++)
            _sampleOutput[index] = SvgFilterColorTransfer.Convert(_linearSamples[index], SvgFilterColorSpace.Srgb);
        return _sampleOutput.Length;
    }

    [Benchmark]
    public byte[] BlurOneAxis()
    {
        SvgGaussianBlur.Apply(_blurSource, _blurOutput, BlurSide, BlurSide, new(2.5f, 0), _blurBudget, CancellationToken.None);
        return _blurOutput;
    }

    [Benchmark]
    public byte[] BlurTwoAxes()
    {
        SvgGaussianBlur.Apply(_blurSource, _blurOutput, BlurSide, BlurSide, new(2.5f, 1.5f), _blurBudget, CancellationToken.None);
        return _blurOutput;
    }

    [GlobalCleanup]
    public void CleanUp()
    {
        _ramp.Dispose();
        _ramp = default;
        _contour = [];
        _samples = [];
        _linearSamples = [];
        _sampleOutput = [];
        _coverage = [];
        _background = [];
        _blendOutput = [];
        _blurSource = [];
        _blurOutput = [];
    }
}
