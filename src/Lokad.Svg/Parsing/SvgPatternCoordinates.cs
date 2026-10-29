using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Shares immutable pattern-unit semantics while each paint retains its own unclipped source bounds.</summary>
/// <remarks>No geometry or pixels are owned here. Definition/paint metadata allowances cover this small
/// shared value; callers own cancellation and the replay scene. Resolution is allocation-free.</remarks>
internal sealed record SvgPatternCoordinates(bool ObjectBoxTiles, bool ObjectBoxContent)
{
    internal SvgPaintBounds? ObjectBox { get; init; }
    internal bool IsEmpty => ObjectBox is {} box && (box.Right <= box.Left || box.Bottom <= box.Top);

    internal SvgPatternMappedCoordinates Resolve(double x, double y, double width, double height,
        SvgAffineTransform placement, SvgViewBox? contentViewBox)
    {
        SvgPaintBounds box = ObjectBox ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern object units require retained source bounds.");
        double objectWidth = box.Right - box.Left, objectHeight = box.Bottom - box.Top;
        if (objectWidth <= 0 || objectHeight <= 0)
            throw new InvalidOperationException("An empty object box must not request a pattern texture.");
        if (ObjectBoxTiles)
        {
            x = box.Left + x * objectWidth;
            y = box.Top + y * objectHeight;
            width *= objectWidth;
            height *= objectHeight;
        }
        if (ObjectBoxContent && contentViewBox is null)
            contentViewBox = new(0, 0, width / objectWidth, height / objectHeight, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
            throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern object-box coordinates exceed the supported range.");
        return new(x, y, width, height, placement, contentViewBox);
    }
}

internal readonly record struct SvgPatternMappedCoordinates(double OriginX, double OriginY, double Width, double Height,
    SvgAffineTransform Placement, SvgViewBox? ContentViewBox);

/// <summary>Retains the drawing matrix separately from the pattern shader's local matrix.</summary>
internal sealed record SvgPatternPaintMatrices(SvgAffineTransform Target, SvgAffineTransform Pattern)
{
    internal SvgPatternPaintMatrices Place(SvgAffineTransform outer)
        => new(outer.ComposeChecked(Target), Pattern);
}
