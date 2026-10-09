using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

internal static class SvgLeafMaskAssertions
{
    internal static void CommandSequence(SvgDrawCommand[] expected, SvgDrawCommand[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index] with { MaskRegion = null }, actual[index] with { MaskRegion = null });
            if (expected[index].MaskRegion is {} region)
            {
                SvgClipBox retained = Assert.IsType<SvgClipBox>(actual[index].MaskRegion);
                Assert.Equal(region.X, retained.X, 6);
                Assert.Equal(region.Y, retained.Y, 6);
                Assert.Equal(region.Width, retained.Width, 6);
                Assert.Equal(region.Height, retained.Height, 6);
            }
            else Assert.Null(actual[index].MaskRegion);
        }
    }

    internal static void UniformScope(SvgRoot root, byte value, SvgClipBox? region)
        => UniformScope(root, value, region, new(-1));

    internal static void UniformGroupScope(SvgRoot root, byte value, SvgClipBox? region)
        => UniformScope(root, value, region, new(1));

    private static void UniformScope(SvgRoot root, byte value, SvgClipBox? region, SvgRecordingScope recordingScope)
    {
        // An omitted expected region means a default region, which still owns a mask layer.
        bool isolates = root.Geometries.Length != 0;
        Assert.Equal(root.Geometries.Length + (isolates ? 2 : 0), root.Commands.Length);
        if (isolates)
        {
            SvgDrawCommand begin = root.Commands[0];
            Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, recordingScope.Value, 255, begin.MaskRegion, -1, value), begin);
            if (region is {} explicitRegion) Assert.Equal(explicitRegion, begin.MaskRegion);
            else Assert.IsType<SvgClipBox>(begin.MaskRegion);
            Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), root.Commands[^1]);
        }
        for (int index = 0; index < root.Geometries.Length; index++)
        {
            Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, index, 255, null, -1, 255), root.Commands[index + (isolates ? 1 : 0)]);
            Assert.Null(root.Geometries[index].LeafMask);
        }
    }
}
