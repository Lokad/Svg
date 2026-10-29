using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

/// <summary>Checks native rectangle content while borrowing its scene owner.</summary>
internal static class SvgPatternContentAssertions
{
    internal static SvgRectangle NativePatternRectangle(SvgVectorContent content)
    {
        SvgGeometry geometry = Assert.Single(content.Scene.Geometries);
        Assert.Equal(SvgGeometryKind.Rectangle, geometry.Kind);
        return geometry.Rectangle;
    }
}
