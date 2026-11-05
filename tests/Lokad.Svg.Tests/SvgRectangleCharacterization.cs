using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

/// <summary>Projects parser geometry into rectangle rows for existing characterization assertions.</summary>
internal static class SvgRectangleCharacterization
{
    /// <summary>Copies the borrowed geometry into a test-owned array, preserving stroke bands and clipped bounds.</summary>
    internal static SvgRectangle[] CharacterizeRectangles(this SvgGeometry[] source)
    {
        ReadOnlySpan<SvgGeometry> geometries = source;
        SvgRectangle[] rectangles = new SvgRectangle[geometries.Length];
        for (int index = 0; index < geometries.Length; index++)
        {
            rectangles[index] = geometries[index].Kind switch
            {
                SvgGeometryKind.Rectangle => geometries[index].Rectangle,
                SvgGeometryKind.AxisAlignedLine => geometries[index].AxisAlignedLine.ToStrokeBand(),
                SvgGeometryKind.OrthogonalBand => geometries[index].Rectangle,
                _ => throw new SvgException(SvgErrorCode.MalformedInput, "SVG scene contains unsupported geometry.")
            };
            if (rectangles[index].Clip is {} clip)
            {
                SvgRectangle rectangle = rectangles[index];
                double left = Math.Max(rectangle.X, clip.X), top = Math.Max(rectangle.Y, clip.Y);
                double right = Math.Min(rectangle.X + rectangle.Width, clip.Right), bottom = Math.Min(rectangle.Y + rectangle.Height, clip.Bottom);
                rectangles[index] = rectangle with { X = left, Y = top, Width = Math.Max(0, right - left), Height = Math.Max(0, bottom - top), Clip = null };
            }
        }
        return rectangles;
    }
}
