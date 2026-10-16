using Lokad.Svg.Rendering;
using Lokad.Svg.Fonts;

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
        if (!_map.TryInvert(out _inverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box resources require a nonsingular referencing map.");
        _left = _top = double.PositiveInfinity;
        _right = _bottom = double.NegativeInfinity;
        _hasGeometry = false;
    }

    internal void Add(SvgGeometry geometry, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        SvgRectangle bounds;
        if (geometry.AuthoredPath is {} source)
        {
            if (_map.IsAxisAligned && source.SceneMap.IsAxisAligned)
                bounds = _inverse.Apply(source.SceneMap.Apply(source.StrokeStyle?.SourceBounds ?? SvgPathBounds.Compute(source.Commands, work, token)));
            else
            {
                SvgAffineTransform local = source.SceneMap == _map ? SvgAffineTransform.Identity : _inverse.ComposeChecked(source.SceneMap);
                bounds = source.StrokeStyle?.SourceBounds is {} retained && local.IsAxisAligned
                    ? local.Apply(retained) : SvgPathBounds.Compute(source.Commands, local, work, token);
            }
        }
        else if (geometry.Kind == SvgGeometryKind.GlyphContours)
            bounds = SvgGlyphOutline.Bounds(geometry.Glyph.Contours, _inverse, work, token);
        else if (geometry.Kind == SvgGeometryKind.AxisAlignedLine)
        {
            SvgAxisAlignedLine line = geometry.AxisAlignedLine;
            bounds = new(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2),
                Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1), default, true, null);
            bounds = MapBox(bounds, _inverse, work, token);
        }
        else if (geometry.Kind is SvgGeometryKind.Rectangle or SvgGeometryKind.PatternFill or SvgGeometryKind.GradientFill or SvgGeometryKind.RadialGradient or SvgGeometryKind.EmbeddedImage
            or SvgGeometryKind.OrthogonalBand or SvgGeometryKind.PolygonFill or SvgGeometryKind.GradientPolygon
            or SvgGeometryKind.RadialPolygon or SvgGeometryKind.PatternPolygon or SvgGeometryKind.GradientStroke)
            bounds = MapBox(SvgMaskSyntax.RecordedObjectBox(geometry, state, work, token), _inverse, work, token);
        else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box curved clips require retained source geometry bounds.");
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
            throw new SvgException(SvgErrorCode.LimitExceeded, "Bounding-box clip coordinates exceed the finite range.");
        _left = Math.Min(_left, bounds.X); _top = Math.Min(_top, bounds.Y);
        _right = Math.Max(_right, bounds.X + bounds.Width); _bottom = Math.Max(_bottom, bounds.Y + bounds.Height);
        _hasGeometry = true;

        static SvgRectangle MapBox(SvgRectangle box, SvgAffineTransform map, ParserWorkCounter work, CancellationToken token)
        {
            if (map.IsAxisAligned) return map.Apply(box);
            SvgPaintBounds bounds = new((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height));
            SvgPaintBounds mapped = bounds.Map(map, work, token);
            return new(mapped.Left, mapped.Top, mapped.Right - mapped.Left, mapped.Bottom - mapped.Top, default, true, null);
        }
    }

    internal SvgRectangle SourceBounds => _hasGeometry ? new(_left, _top, Math.Max(0, _right - _left), Math.Max(0, _bottom - _top), default, true, null) : default;

    /// <summary>Binds one normalized rectangle through the same geometric target map as curved clip content.</summary>
    internal SvgLeafMaskOperation Register(SvgClipBox content, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true;
        if (!_hasGeometry || _right <= _left || _bottom <= _top || content.Width <= 0 || content.Height <= 0)
        {
            operation.Value = 0;
            return operation;
        }
        SvgAffineTransform map = _map.ComposeChecked(SvgAffineTransform.FromScaleOffset(_right - _left, _bottom - _top, _left, _top));
        operation.Value = 255;
        operation.RegionKind = SvgLayerRegionKind.AffineViewport;
        operation.AffineViewport = new(content, map);
        return operation;
    }

    internal SvgLeafMaskOperation Register(SvgClipPolygon content, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        => Register(content, null, state, work, token);

    /// <summary>Retains the mapped native resource when source geometry may transfer to another scene.</summary>
    /// <remarks>A null id registers immediately in the active owner. Otherwise emission registers the
    /// borrowed document resource in the final geometry owner, avoiding scene-local content indices
    /// crossing a source transfer. Bounds, empty targets, work and cancellation use the same route.</remarks>
    internal SvgLeafMaskOperation Register(SvgClipPolygon content, string? nativeId, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
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
        if (nativeId is not null)
        {
            operation.NativeClip = new(nativeId, map);
            operation.Value = 255;
            operation.RegionKind = SvgLayerRegionKind.AffineViewport;
            return operation;
        }
        (SvgClipBox region, int contentIndex) = content.Register(map, true, state, state.Scene, work, token);
        operation.Value = 255;
        operation.Region = content.UsesMaskFallback ? null : region;
        operation.ContentIndex = contentIndex;
        operation.RegionKind = content.UsesMaskFallback ? SvgLayerRegionKind.Mask : SvgLayerRegionKind.AffineViewport;
        return operation;
    }
}
