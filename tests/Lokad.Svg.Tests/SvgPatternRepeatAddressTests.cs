using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgPatternRepeatAddressTests
{
    // Independently compiled pinned raster-pipeline vectors:
    // Research/svgweb/pattern-repeat-native.cpp and pattern-repeat-addressing.json.
    [Theory]
    [InlineData(13, 3242196991u, 1095237631u, 11, 12, 1065353200u)]
    [InlineData(13, 3242196992u, 1095237632u, 12, 0, 0u)]
    [InlineData(13, 3243245567u, 1096286207u, 12, 0, 1065353200u)]
    [InlineData(13, 1096810495u, 3243769855u, 12, 0, 897581056u)]
    [InlineData(13, 855638016u, 1056964607u, 12, 1, 0u)]
    [InlineData(60, 3261857791u, 1114505215u, 58, 0, 1065353152u)]
    [InlineData(60, 3261857792u, 1114505216u, 59, 0, 0u)]
    [InlineData(60, 3262119935u, 1114767359u, 0, 0, 1065353152u)]
    [InlineData(60, 1114898431u, 3262251007u, 59, 0, 914358272u)]
    [InlineData(60, 855638016u, 1056964607u, 59, 1, 0u)]
    [InlineData(253, 3279683583u, 1132232703u, 251, 0, 1065352960u)]
    [InlineData(253, 3279683584u, 1132232704u, 252, 0, 0u)]
    [InlineData(253, 3279749119u, 1132298239u, 0, 0, 1065352960u)]
    [InlineData(253, 1132331007u, 3279781887u, 252, 0, 931135488u)]
    [InlineData(253, 855638016u, 1056964607u, 252, 1, 0u)]
    public void Native_taps_repeat_before_gather_clamping_and_select_independent_addresses(
        int size, uint translationBits, uint centerBits, int first, int second, uint fractionBits)
    {
        float translation = BitConverter.UInt32BitsToSingle(translationBits);
        SvgShaderMatrix drawing = new(1, 0, 0, 0, 1, 0);
        SvgShaderMatrix local = new(1, 0, translation, 0, 1, translation);
        SvgPatternGeometry pattern = new(default, 0, 0, size, size, default, 255, null)
        { NativeImageMap = drawing, NativeImageLocal = local };
        SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateImage(drawing, local);
        Assert.True(coordinates.TryMap(0, 0, default, out (float X, float Y) center));
        Assert.Equal(centerBits, BitConverter.SingleToUInt32Bits(center.X));
        Assert.Equal(centerBits, BitConverter.SingleToUInt32Bits(center.Y));

        SvgPatternSample sample = SvgPatternSample.Prepare(pattern, 0, 0, default, ref coordinates);
        Assert.Equal((first * size + first) * 4, sample.First);
        Assert.Equal((first * size + second) * 4, sample.Second);
        Assert.Equal((second * size + first) * 4, sample.Third);
        Assert.Equal((second * size + second) * 4, sample.Fourth);
        Assert.Equal(fractionBits, BitConverter.SingleToUInt32Bits(sample.FractionX));
        Assert.Equal(fractionBits, BitConverter.SingleToUInt32Bits(sample.FractionY));
    }
}
