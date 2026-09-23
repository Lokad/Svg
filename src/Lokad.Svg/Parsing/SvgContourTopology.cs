namespace Lokad.Svg.Parsing;

/// <summary>Classifies closed integer contour slices by edge direction without allocating.</summary>
internal enum SvgContourKind
{
    /// <summary>Every edge, including the implicit closing edge, is horizontal or vertical.</summary>
    Orthogonal,

    /// <summary>At least one edge, including the implicit closing edge, is diagonal.</summary>
    Diagonal,
}

/// <summary>Interprets budgeted point storage as typed closed contours with explicit topology.</summary>
internal static class SvgContourTopology
{
    /// <summary>Classifies one closed contour slice, following the implicit edge from its last point back to its first.</summary>
    internal static SvgContourKind Classify(ReadOnlySpan<SvgPoint> points, int start, int end)
    {
        if (start < 0 || start > points.Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start || end > points.Length) throw new ArgumentOutOfRangeException(nameof(end));
        for (int index = start; index < end; index++)
        {
            SvgPoint edgeStart = points[index];
            SvgPoint edgeEnd = index + 1 < end ? points[index + 1] : points[start];
            if (edgeStart.X != edgeEnd.X && edgeStart.Y != edgeEnd.Y) return SvgContourKind.Diagonal;
        }
        return SvgContourKind.Orthogonal;
    }
}