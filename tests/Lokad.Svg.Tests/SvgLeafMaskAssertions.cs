using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

internal static class SvgLeafMaskAssertions
{
    internal static void UniformScope(SvgRoot root, byte value, SvgClipBox? region)
    {
        bool isolates = value != 255 || region is not null;
        Assert.Equal(root.Geometries.Length + (isolates ? 2 : 0), root.Commands.Length);
        if (isolates)
        {
            Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, region, -1, value), root.Commands[0]);
            Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), root.Commands[^1]);
        }
        for (int index = 0; index < root.Geometries.Length; index++)
        {
            Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, index, 255, null, -1, 255), root.Commands[index + (isolates ? 1 : 0)]);
            Assert.Null(root.Geometries[index].LeafMask);
        }
    }
}
