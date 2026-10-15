using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Collects a referencing element's geometry bounds independently of its painted stroke extent.</summary>
/// <remarks>Bounds include zero-width/height child geometry before union. The caller keeps geometries
/// alive; source commands remain borrowed. Visits and native bound evaluation charge work and observe
/// cancellation. Collection owns no storage; registration copies clip content into the scene owner.</remarks>
internal struct SvgObjectBoundingClip
{
    private readonly SvgAffineTransform _map;
    private readonly SvgAffineTransform _inverse;
    private double _left, _top, _right, _bottom;
    private bool _hasGeometry;

    internal SvgObjectBoundingClip(SvgAffineTransform transform, SvgViewportFrame? parent)
    {
        _map = parent is {} frame
            ? SvgAffineTransform.FromScaleOffset(frame.ScaleX, frame.ScaleY, frame.OffsetX, frame.OffsetY).ComposeChecked(transform)
            : transform;
        if (!_map.IsAxisAligned || !_map.TryInvert(out _inverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box curved clips require a nonsingular axis-aligned referencing map.");
        _left = _top = double.PositiveInfinity;
        _right = _bottom = double.NegativeInfinity;
        _hasGeometry = false;
    }

    internal void Add(SvgGeometry geometry, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        SvgRectangle bounds;
        if (geometry.AuthoredPath is {} source)
        {
            if (!source.SceneMap.IsAxisAligned)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box curved clips require axis-aligned source geometry maps.");
            bounds = source.SceneMap.Apply(source.StrokeStyle?.SourceBounds ?? SvgPathBounds.Compute(source.Commands, work, token));
        }
        else if (geometry.Kind == SvgGeometryKind.AxisAlignedLine)
        {
            SvgAxisAlignedLine line = geometry.AxisAlignedLine;
            bounds = new(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2),
                Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1), default, true, null);
        }
        else if (geometry.Kind is SvgGeometryKind.Rectangle or SvgGeometryKind.PatternFill or SvgGeometryKind.GradientFill or SvgGeometryKind.RadialGradient)
            bounds = SvgMaskSyntax.BoundingBox(geometry);
        else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box curved clips require retained source geometry bounds.");
        bounds = _inverse.Apply(bounds);
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
            throw new SvgException(SvgErrorCode.LimitExceeded, "Bounding-box clip coordinates exceed the finite range.");
        _left = Math.Min(_left, bounds.X); _top = Math.Min(_top, bounds.Y);
        _right = Math.Max(_right, bounds.X + bounds.Width); _bottom = Math.Max(_bottom, bounds.Y + bounds.Height);
        _hasGeometry = true;
    }

    internal SvgLeafMaskOperation Register(SvgClipPolygon content, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (content.NestedClipId is not null)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box nested resource clips are not implemented.");
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true;
        if (!_hasGeometry || _right <= _left || _bottom <= _top || content.Points.Length == 0)
        {
            operation.Value = 0;
            return operation;
        }
        SvgAffineTransform map = _map.ComposeChecked(SvgAffineTransform.FromScaleOffset(_right - _left, _bottom - _top, _left, _top));
        (SvgClipBox region, int contentIndex) = content.Register(map, true, state, state.Scene, work, token);
        operation.Value = 255;
        operation.Region = content.UsesMaskFallback ? null : region;
        operation.ContentIndex = contentIndex;
        operation.RegionKind = content.UsesMaskFallback ? SvgLayerRegionKind.Mask : SvgLayerRegionKind.AffineViewport;
        return operation;
    }
}
