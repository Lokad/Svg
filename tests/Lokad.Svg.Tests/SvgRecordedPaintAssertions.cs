using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

internal static class SvgRecordedPaintAssertions
{
    internal static void ShaderAlpha(SvgRoot scene, float alpha)
    {
        SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(scene.SceneStorage);
        Assert.All(scene.Geometries, geometry =>
        {
            Assert.True(geometry.RecordedPaint.Identity > 0);
            SvgRecordedPaintValue paint = storage.RecordedPaintValues[geometry.RecordedPaint.Identity - 1];
            Assert.Equal(alpha, paint.Alpha);
        });
    }

    internal static void ElementOpacity(SvgRoot scene, SvgPaintOpacity opacity)
    {
        SvgDrawCommand begin = Assert.Single(scene.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
        OpacityScope(begin, opacity);
    }

    internal static void OpacityScope(SvgDrawCommand command, SvgPaintOpacity opacity)
    {
        Assert.Equal(SvgDrawCommandKind.BeginGroup, command.Kind);
        Assert.True(command.RecordingScope.Value >= 0);
        Assert.Equal(opacity, command.SourceOpacity);
        Assert.Equal(255, command.MaskAlpha);
        Assert.Null(command.MaskRegion);
        Assert.Equal(-1, command.MaskContentIndex);
    }
}
