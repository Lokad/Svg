using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

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
        => UniformScope(root, value, region, new(-1), null);

    internal static void UniformScope(SvgRoot root, byte value, SvgClipBox? region, SvgClipBox paintRegion)
        => UniformScope(root, value, region, new(-1), paintRegion);

    internal static void UniformGroupScope(SvgRoot root, byte value, SvgClipBox? region)
        => UniformScope(root, value, region, new(1), null);

    internal static void UniformScopeWithViewport(SvgRoot root, byte value, SvgClipBox? region,
        SvgClipBox sourceViewport, SvgAffineTransform map)
        => UniformScopeWithViewport(root, value, region, sourceViewport, map, null);

    internal static void UniformScopeWithViewport(SvgRoot root, byte value, SvgClipBox? region,
        SvgClipBox sourceViewport, SvgAffineTransform map, SvgClipBox? paintRegion)
    {
        Assert.Equal(5, root.Commands.Length);
        SvgDrawCommand begin = root.Commands[0];
        int contentIndex = paintRegion is null ? -1 : PaintRegion(root, begin.MaskContentIndex, paintRegion.Value);
        Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, begin.MaskRegion, contentIndex, value), begin);
        if (region is {} explicitRegion) Assert.Equal(explicitRegion, begin.MaskRegion);
        else Assert.IsType<SvgClipBox>(begin.MaskRegion);
        Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), root.Commands[4]);
        ViewportScope(root, 1, sourceViewport, map);
    }

    internal static void ViewportScope(SvgRoot root, int start, SvgClipBox sourceViewport, SvgAffineTransform map)
    {
        SvgDrawCommand begin = root.Commands[start];
        Assert.Equal(SvgDrawCommandKind.BeginGroup, begin.Kind);
        Assert.True(begin.RecordingScope.Value >= 0);
        Assert.Equal(SvgPaintOpacity.Opaque, begin.SourceOpacity);
        Assert.Equal(255, begin.MaskAlpha);
        Assert.Equal(SvgLayerRegionKind.AffineViewport, begin.RegionKind);
        Assert.IsType<SvgClipBox>(begin.MaskRegion);
        SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(root.SceneStorage);
        Assert.InRange(begin.MaskContentIndex, 0, storage.MaskContents.Length - 1);
        SvgMaskContent content = storage.MaskContents[begin.MaskContentIndex];
        Assert.Equal(SvgClipContentKind.AffineViewport, content.ClipKind);
        SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(content.Geometries).AuthoredPath);
        SvgRectangle bounds = SvgPathBounds.Compute(path.Commands, new(int.MaxValue), CancellationToken.None);
        Assert.Equal((sourceViewport.X, sourceViewport.Y, sourceViewport.Width, sourceViewport.Height),
            (bounds.X, bounds.Y, bounds.Width, bounds.Height));
        Assert.Equal(map, path.SceneMap);
        Assert.Equal(path.SceneMap, Assert.IsType<SvgViewportDrawMap>(content.DrawMap).Source);
        Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255), root.Commands[start + 1]);
        Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), root.Commands[start + 2]);
        Assert.Null(Assert.Single(root.Geometries).LeafMask);
    }

    private static void UniformScope(SvgRoot root, byte value, SvgClipBox? region, SvgRecordingScope recordingScope, SvgClipBox? paintRegion)
    {
        // An omitted expected region means a default region, which still owns a mask layer.
        bool isolates = root.Geometries.Length != 0;
        Assert.Equal(root.Geometries.Length + (isolates ? 2 : 0), root.Commands.Length);
        if (isolates)
        {
            SvgDrawCommand begin = root.Commands[0];
            int contentIndex = paintRegion is null ? -1 : PaintRegion(root, begin.MaskContentIndex, paintRegion.Value);
            Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, recordingScope.Value, 255, begin.MaskRegion, contentIndex, value), begin);
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
    private static int PaintRegion(SvgRoot root, int index, SvgClipBox expected)
    {
        SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(root.SceneStorage);
        Assert.InRange(index, 0, storage.MaskContents.Length - 1);
        SvgMaskContent content = storage.MaskContents[index];
        Assert.Equal(expected, content.SourcePaintRegion);
        Assert.Equal((byte)255, content.ConstantAlpha);
        Assert.Equal((byte)255, content.Luminance);
        Assert.True(content.IsAlpha);
        Assert.Empty(content.Geometries);
        Assert.Null(content.VectorContent);
        Assert.Null(content.ClipKind);
        Assert.Equal(-1, content.BoundsClipContentIndex);
        return index;
    }

}
