using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOpaqueGradientCoverageTests
{
    public static IEnumerable<object[]> NativeVectors()
    {
        foreach (string row in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData/opaque-gradient-native.csv")).Skip(1))
            yield return [row];
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [MemberData(nameof(NativeVectors))]
    public void Material_replay_matches_compiled_native_coverage_stages(string row)
    {
        string[] columns = row.Split(',');
        uint[] values = columns.Skip(1).Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        SvgShaderPaint paint = new(BitConverter.UInt32BitsToSingle(values[0]), BitConverter.UInt32BitsToSingle(values[1]),
            BitConverter.UInt32BitsToSingle(values[2]), BitConverter.UInt32BitsToSingle(values[3]));
        byte[] destination = values.Skip(4).Take(4).Select(value => (byte)value).ToArray();
        SvgAnalyticPaint.Shader(paint, values[9] != 0).Blend(destination, (byte)values[8], (SvgAnalyticPaintKind)values[10]);
        Assert.Equal(values.Skip(11).Select(value => (byte)value), destination);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Binary_region_preserves_program_opacity_and_forwards_fringe_pairs_as_direct_runs()
    {
        float halfByte = BitConverter.UInt32BitsToSingle(989888641);
        SvgAnalyticPaint paint = SvgAnalyticPaint.Shader(new(halfByte, halfByte, halfByte, 1), true).ThroughBinaryRegion();
        byte[] destination = [255, 255, 255, 255];
        paint.Blend(destination, 255, SvgAnalyticPaintKind.Pair);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, destination);
    }

    public enum Program { Linear, TransparentStop, PaintOpacity, Hairline, Radial, OffsetCone, ConcentricCone }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(Program.Linear)]
    [InlineData(Program.TransparentStop)]
    [InlineData(Program.PaintOpacity)]
    [InlineData(Program.Hairline)]
    [InlineData(Program.Radial)]
    [InlineData(Program.OffsetCone)]
    [InlineData(Program.ConcentricCone)]
    public void Sampler_uses_program_opacity_and_factory_identity_instead_of_the_sample_alpha(Program program)
    {
        RenderBudget budget = new(new HostMemoryBudget(10_000));
        SvgGradientStop[] stops = [new(0, new(192, 250, 255, 255)) { Alpha = program == Program.TransparentStop ? .5f : 1 }, new(1, new(249, 255, 255, 255))];
        using SvgGradientRamp ramp = SvgGradientRamp.Create(stops, SvgGradientSpread.Pad, budget, CancellationToken.None);
        SvgShaderSamplingContext context = new(new(640, 640), new(0, 0, 640, 640), null, null, null, SvgAffineTransform.Identity, 1, 1, 0, 0);
        float alpha = program == Program.PaintOpacity ? .5f : 1;
        SvgShaderSampler sampler;
        if (program is Program.Radial or Program.OffsetCone or Program.ConcentricCone)
        {
            SvgRadialGradient radial = new(stops[0].Color, stops[1].Color, 0, 0, 8, program == Program.OffsetCone ? 1 : 0, 0,
                program == Program.ConcentricCone ? 2 : 0, 0, 1, false, true, SvgGradientSpread.Pad, stops, 1, 1, false, SvgGradientTransform.Identity, false);
            sampler = SvgShaderSampler.Radial(radial, default, ramp, SvgShaderCoordinates.CreateRadial(radial, default, 1, 1, 0, 0), context, alpha);
        }
        else
        {
            SvgLinearGradient linear = new(stops[0].Color, stops[1].Color, 0, 0, 1, 0, 0, 1, false, true, SvgGradientSpread.Pad, stops, 1, 1, false);
            sampler = SvgShaderSampler.Linear(linear, default, ramp, SvgShaderCoordinates.CreateLinear(linear, default, 1, 1, 0, 0), context, alpha);
        }
        if (program == Program.Hairline)
        {
            sampler.ApplyHairlineCoverage(.5f);
            alpha = 127 / 255f;
        }
        byte[] destination = [174, 182, 188, 255];
        sampler.Sample(494, 101, CancellationToken.None).Blend(destination, 192, SvgAnalyticPaintKind.Direct);
        byte[] expected = [174, 182, 188, 255];
        if (program is Program.Linear or Program.Radial) expected = [231, 237, 238, 255];
        else if (alpha == 1) expected = [230, 237, 238, 255];
        else ramp.SampleAbsolute(1, alpha, 34).Blend(expected, 192);
        Assert.Equal(expected, destination);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, 0, false, true)]
    [InlineData(0.000030517578125, 0, false, true)]
    [InlineData(0.000030517581762978807, 0, false, false)]
    [InlineData(0, 0.000030517578125, false, true)]
    [InlineData(0, 0.000030517581762978807, false, false)]
    [InlineData(1, 0, false, false)]
    [InlineData(0, 8, false, true)]
    [InlineData(0, 8, true, false)]
    public void Radial_factory_reduction_keeps_native_float_thresholds_and_conical_identity(double focus, double inner, bool repeat, bool radialFactory)
    {
        SvgRadialGradient gradient = new(default, default, 0, 0, 8, focus, 0, inner, 0, 1, false, true,
            repeat ? SvgGradientSpread.Repeat : SvgGradientSpread.Pad, [], 1, 1, false, SvgGradientTransform.Identity, false);
        Assert.Equal(radialFactory, SvgShaderCoordinates.UsesRadialFactory(gradient));
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Factory_identity_uses_borrowed_declaration_before_resolved_placement()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='4' cy='4' r='3' fx='4.25' fy='4' gradientTransform='scale(2)'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><rect width='16' height='16' fill='url(#g)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRadialGradient gradient = Assert.Single(root.Geometries).Radial.Gradient;
        Assert.NotNull(gradient.SourceDefinition);
        gradient = gradient with { FocalX = gradient.CenterX, FocalY = gradient.CenterY, FocalRadius = 0 };
        Assert.False(SvgShaderCoordinates.UsesRadialFactory(gradient));
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(3, false)]
    [InlineData(256, true)]
    public void Prepared_program_opacity_survives_inline_allocated_and_constant_ramps(int count, bool opaque)
    {
        SvgGradientStop[] stops = Enumerable.Range(0, count).Select(index => new SvgGradientStop(count <= 1 ? 0 : index / (double)(count - 1),
            count == 256 || (index & 1) == 0 ? new(255, 0, 0, 255) : new(0, 0, 255, 255)) { Alpha = !opaque && index == 0 ? .5f : 1 }).ToArray();
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (SvgGradientRamp ramp = SvgGradientRamp.Create(stops, SvgGradientSpread.Pad, budget, CancellationToken.None))
        {
            Assert.Equal(opaque, ramp.IsOpaque);
            if (count != 0) stops[0] = stops[0] with { Alpha = opaque ? .5f : 1 };
            Assert.Equal(opaque, ramp.IsOpaque);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
