using System.Xml;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

// Geometry retains two four-byte references; values belong to the scene arena.
internal readonly record struct SvgRecordedPaint(int Identity)
{
    internal static SvgRecordedPaint SourceStroke => new(-1);
    internal bool IsSourceStroke => Identity == -1;
}
internal readonly record struct SvgRecordingScope(int Value);
internal readonly record struct SvgPaintOperationIdentity(int Value);
[Flags]
internal enum SvgRecordedPaintRole : byte { Fill = 0, Stroke = 1, MarkerContent = 2 }
internal readonly record struct SvgRecordedPaintValue(SvgPaintOperationIdentity Identity, float Alpha, bool Foldable, SvgRecordedPaintRole Role, SvgPaintOrder PaintOrder, SvgRecordingScope Scope, SvgPaintOpacity ElementOpacity, SvgRecordingScope OpacityScope)
{
    // Resource paint stays independent when its host instance binds fill and stroke.
    internal bool IsStroke { get => (Role & SvgRecordedPaintRole.Stroke) != 0; init => Role = value ? Role | SvgRecordedPaintRole.Stroke : Role & ~SvgRecordedPaintRole.Stroke; }
    internal bool IsMarkerContent { get => (Role & SvgRecordedPaintRole.MarkerContent) != 0; init => Role = value ? Role | SvgRecordedPaintRole.MarkerContent : Role & ~SvgRecordedPaintRole.MarkerContent; }
    /// <summary>Retains the previous constructor contract; source recording assigns the stroke role explicitly.</summary>
    internal SvgRecordedPaintValue(SvgPaintOperationIdentity identity, float alpha, bool foldable, SvgRecordingScope scope, SvgPaintOpacity elementOpacity, SvgRecordingScope opacityScope)
        : this(identity, alpha, foldable, SvgRecordedPaintRole.Fill, SvgPaintOrder.FillStrokeMarkers, scope, elementOpacity, opacityScope) { }
    // Source visual bounds survive lowering; they do not describe a shader coordinate box or a clip.
    internal SvgPaintBounds? VisualBounds { get; init; }
    internal SvgPaintBounds? ObjectBounds { get; init; }
}

internal readonly record struct SvgPaintBounds(float Left, float Top, float Right, float Bottom)
{
    internal SvgPaintBounds Map(SvgAffineTransform placement, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        SvgDevicePoint first = SvgAuthoredPath.MapPoint(placement, new(Left, Top), new(0, 0));
        work.Consume(token);
        SvgDevicePoint second = SvgAuthoredPath.MapPoint(placement, new(Right, Top), new(0, 0));
        work.Consume(token);
        SvgDevicePoint third = SvgAuthoredPath.MapPoint(placement, new(Left, Bottom), new(0, 0));
        work.Consume(token);
        SvgDevicePoint fourth = SvgAuthoredPath.MapPoint(placement, new(Right, Bottom), new(0, 0));
        return new((float)Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X)),
            (float)Math.Min(Math.Min(first.Y, second.Y), Math.Min(third.Y, fourth.Y)),
            (float)Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X)),
            (float)Math.Max(Math.Max(first.Y, second.Y), Math.Max(third.Y, fourth.Y)));
    }
}
internal enum SvgPaintOrigin { Solid, PaintServer }
internal readonly record struct SvgRecordedPaintStyle(SvgPaintOpacity ElementOpacity, float FillAlpha, float FillShaderAlpha, float StrokeAlpha, float StrokeShaderAlpha, SvgPaintOrigin FillOrigin, SvgPaintOrigin StrokeOrigin, bool HasFillOperation, bool HasLocalClip, SvgRecordingScope Scope, SvgRecordingScope OpacityScope)
{
    internal SvgPaintBounds? VisualBounds { get; init; }
    internal SvgPaintBounds? ObjectBounds { get; init; }
    internal float StrokeBoundsInflation { get; init; }
    internal SvgPaintOrder PaintOrder { get; init; }
}

// Retains source paint and recording state until playback; parser budgets own staging and scene values.
internal static class SvgPaintRecording
{
    /// <summary>Retains material glyph bounds for filtered source groups; glyph color/opacity remains baked once in its existing paint.</summary>
    internal static SvgGeometry RecordGlyph(SvgGlyphGeometry glyph, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgGeometry geometry = SvgGeometry.FromGlyphContours(glyph);
        if (state.FilterGroupDepth <= 0) return geometry;
        float left = float.PositiveInfinity, top = float.PositiveInfinity;
        float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        foreach (SvgGlyphQuadraticContour contour in glyph.Contours)
        foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
        {
            work.Consume(token);
            Include(segment.Start); Include(segment.Control); Include(segment.End);
        }
        SvgPaintBounds bounds = float.IsPositiveInfinity(left) ? default : new(left, top, right, bottom);
        float inflation = (float)Math.Max(2, glyph.StrokeBoundsInflation);
        SvgRecordedPaintValue paint = new(state.NewPaintOperationIdentity(), glyph.Color.Alpha / 255f, false, new SvgRecordingScope(1), SvgPaintOpacity.Opaque, new SvgRecordingScope(1))
        {
            ObjectBounds = bounds,
            VisualBounds = new(bounds.Left - inflation, bounds.Top - inflation, bounds.Right + inflation, bounds.Bottom + inflation)
        };
        return geometry with { RecordedPaint = state.Scene.RegisterRecordedPaint(paint) };

        void Include(SvgGlyphOutlinePoint point)
        {
            left = MathF.Min(left, (float)point.X); top = MathF.Min(top, (float)point.Y);
            right = MathF.Max(right, (float)point.X); bottom = MathF.Max(bottom, (float)point.Y);
        }
    }
    internal static SvgRecordedPaintStyle Resolve(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgViewportFrame? frame)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        bool fillServer = style.Fill is not null && SvgLocalUrl.TryParseId(style.Fill, out _);
        SvgPaint fill = SvgPaintResolver.Resolve(fillServer ? "white" : style.Fill, style.FillOpacity, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        SvgColor stroke = style.Stroke is null || style.Stroke == "none" || SvgLocalUrl.TryParseId(style.Stroke, out _) ? new(255, 255, 255, 255)
            : SvgPaintResolver.ResolveStrokeColor(style.Stroke, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        SvgPaintOpacity strokeOpacity = SvgColorParser.ParsePaintOpacity(style.StrokeOpacity);
        SvgRecordingScope opacityScope = context.RecordingScope.Value == 0 ? new(1) : context.RecordingScope;
        if (reader.GetAttribute("clip-path") is not null || context.ResolveTransform(reader) != context.InheritedPresentation.Transform)
            opacityScope = state.NewRecordingScope();
        SvgRecordingScope drawScope = reader.GetAttribute("mask") is null ? opacityScope : state.NewRecordingScope();
        float strokeBoundsInflation = 0;
        string? strokePaint = style.Stroke ?? context.InheritedStroke.Stroke;
        if (reader.LocalName is "path" or "polygon" or "polyline" or "rect" or "circle" or "ellipse" or "line" && strokePaint is not null && strokePaint != "none")
        {
            float half = (float)SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"), reader.LocalName) / 2f;
            float expansion = reader.LocalName is "path" or "polygon" or "polyline" && SvgStrokeStyleSyntax.ParseLinejoin(style.Linejoin, true) == SvgStrokeJoin.Miter
                ? MathF.Max((float)SvgStrokeStyleSyntax.ParseMiterLimit(style.Miterlimit), 1) : 1;
            if (reader.LocalName != "rect" && SvgStrokeStyleSyntax.ParseLinecap(style.Linecap) == SvgStrokeCap.Square) expansion = MathF.Max(expansion, MathF.Sqrt(2));
            strokeBoundsInflation = half * expansion;
        }
        return new(style.GetOpacity(), SvgPaintResolver.ResolveSolidPaint(fill).Alpha,
            fill.FillOpacity.Alpha / 255f, (stroke.Alpha / 255f) * strokeOpacity.Value, strokeOpacity.Alpha / 255f,
            fillServer ? SvgPaintOrigin.PaintServer : SvgPaintOrigin.Solid,
            style.Stroke is not null && SvgLocalUrl.TryParseId(style.Stroke, out _) ? SvgPaintOrigin.PaintServer : SvgPaintOrigin.Solid,
            fill.HasFill, reader.GetAttribute("clip-path") is not null, drawScope, opacityScope) { PaintOrder = style.PaintOrder ?? context.InheritedPresentation.PaintOrder, ObjectBounds = PrimitiveBounds(reader, context, frame, 0), VisualBounds = strokeBoundsInflation > 0 ? PrimitiveBounds(reader, context, frame, strokeBoundsInflation) : null, StrokeBoundsInflation = strokeBoundsInflation };

        static SvgPaintBounds? PrimitiveBounds(XmlReader input, SvgPaintableElementContext element, SvgViewportFrame? viewport, float inflation)
        {
            element.WorkCounter.Consume(element.CancellationToken);
            SvgPaintBounds bounds;
            switch (input.LocalName)
            {
                case "rect":
                    SvgRectangleSyntax rectangle = SvgRectangleSyntax.Parse(input, element.LengthBases);
                    bounds = new((float)rectangle.X, (float)rectangle.Y, (float)(rectangle.X + rectangle.Width), (float)(rectangle.Y + rectangle.Height));
                    break;
                case "circle":
                case "ellipse":
                    SvgEllipseSyntax ellipse = input.LocalName == "circle" ? SvgEllipseSyntax.ParseCircle(input, element.LengthBases) : SvgEllipseSyntax.ParseEllipse(input, element.LengthBases);
                    bounds = new((float)(ellipse.CenterX - ellipse.RadiusX), (float)(ellipse.CenterY - ellipse.RadiusY),
                        (float)(ellipse.CenterX + ellipse.RadiusX), (float)(ellipse.CenterY + ellipse.RadiusY));
                    break;
                case "line":
                    SvgLineSyntax line = SvgLineSyntax.Parse(input);
                    bounds = new(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Max(line.X1, line.X2), Math.Max(line.Y1, line.Y2));
                    break;
                default:
                    return null;
            }
            if (inflation > 0) bounds = new(bounds.Left - inflation, bounds.Top - inflation, bounds.Right + inflation, bounds.Bottom + inflation);
            SvgAffineTransform placement = element.ResolveTransform(input);
            if (viewport is {} parent)
                placement = SvgAffineTransform.FromScaleOffset(parent.ScaleX, parent.ScaleY, parent.OffsetX, parent.OffsetY).ComposeChecked(placement);
            return bounds.Map(placement, element.WorkCounter, element.CancellationToken);
        }
    }

    internal static SvgParsedGeometry Attach(SvgParsedGeometry parsed, SvgRecordedPaintStyle source, string element, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        try
        {
            SvgPaintOperationIdentity fillId = state.NewPaintOperationIdentity();
            SvgPaintOperationIdentity strokeId = state.NewPaintOperationIdentity();
            SvgRecordedPaint fillSolid = default, fillShader = default, strokeSolid = default, strokeShader = default;
            Span<SvgGeometry> values = parsed.Storage.Items;
            SvgPaintBounds? objectBounds = source.ObjectBounds;
            SvgPaintBounds? visualBounds = source.VisualBounds;
            if (parsed.SourceObjectBox is {} inputBox)
            {
                objectBounds = inputBox.Map(parsed.SourceObjectMap, work, token);
                if (source.StrokeBoundsInflation > 0)
                {
                    float inflation = source.StrokeBoundsInflation;
                    visualBounds = new SvgPaintBounds(MathF.Floor(inputBox.Left - inflation), MathF.Floor(inputBox.Top - inflation),
                        MathF.Ceiling(inputBox.Right + inflation), MathF.Ceiling(inputBox.Bottom + inflation)).Map(parsed.SourceObjectMap, work, token);
                }
            }
            if (objectBounds is null && values.Length != 0)
            {
                SvgGeometry first = values[0];
                if (first.AuthoredPath is {} path)
                {
                    SvgRectangle box = SvgPathBounds.Compute(path.Commands, work, token);
                    objectBounds = new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height)).Map(path.SceneMap, work, token);
                    if (source.StrokeBoundsInflation > 0)
                    {
                        float inflation = source.StrokeBoundsInflation;
                        visualBounds = new SvgPaintBounds(MathF.Floor((float)box.X - inflation), MathF.Floor((float)box.Y - inflation),
                            MathF.Ceiling((float)(box.X + box.Width) + inflation), MathF.Ceiling((float)(box.Y + box.Height) + inflation)).Map(path.SceneMap, work, token);
                    }
                }
                else if (element is "polygon" or "polyline")
                {
                    float left = float.PositiveInfinity, top = float.PositiveInfinity;
                    float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                    foreach (SvgGeometry part in values)
                    {
                        work.Consume(token);
                        if (part.RecordedPaint.IsSourceStroke) continue;
                        SvgRectangle box = part.Kind == SvgGeometryKind.GradientFill ? part.Gradient.Box
                            : part.Kind == SvgGeometryKind.RadialGradient ? part.Radial.Box : SvgMaskSyntax.BoundingBox(part);
                        left = MathF.Min(left, (float)box.X); top = MathF.Min(top, (float)box.Y);
                        right = MathF.Max(right, (float)(box.X + box.Width)); bottom = MathF.Max(bottom, (float)(box.Y + box.Height));
                    }
                    if (!float.IsPositiveInfinity(left)) objectBounds = new(left, top, right, bottom);
                }
            }
            for (int index = 0; index < values.Length; index++)
            {
                work.Consume(token);
                SvgGeometry geometry = values[index];
                bool stroke = element == "line" || geometry.RecordedPaint.IsSourceStroke;
                bool shader = geometry.Kind is SvgGeometryKind.GradientFill or SvgGeometryKind.RadialGradient or SvgGeometryKind.PatternFill or SvgGeometryKind.GradientPolygon or SvgGeometryKind.RadialPolygon or SvgGeometryKind.PatternPolygon;
                bool aggregate = geometry.Kind == SvgGeometryKind.GradientStroke;
                bool rectangleStroke = geometry.Kind switch
                {
                    SvgGeometryKind.Rectangle => geometry.Rectangle.Stroke is not null,
                    SvgGeometryKind.GradientFill => geometry.Gradient.Stroke is not null,
                    SvgGeometryKind.RadialGradient => geometry.Radial.Stroke is not null,
                    SvgGeometryKind.PatternFill => geometry.Pattern.Fill.Stroke is not null,
                    _ => false,
                };
                float alpha = shader ? stroke ? source.StrokeShaderAlpha : source.FillShaderAlpha : stroke ? source.StrokeAlpha : source.FillAlpha;
                if (!shader && (stroke ? source.StrokeOrigin : source.FillOrigin) == SvgPaintOrigin.PaintServer)
                    alpha = geometry.Kind switch
                    {
                        SvgGeometryKind.Rectangle or SvgGeometryKind.OrthogonalBand => geometry.Rectangle.Color.Alpha / 255f,
                        SvgGeometryKind.PolygonFill => geometry.Polygon.Color.Alpha / 255f,
                        _ => alpha,
                    };
                bool foldable = !(element == "line" && source.HasFillOperation);
                SvgRecordedPaint paint;
                if (aggregate) paint = geometry.GradientStroke.Fill.HasFill
                    ? GetPaint(ref fillSolid, fillId, source.FillAlpha, foldable)
                    : GetPaint(ref strokeShader, strokeId, source.StrokeShaderAlpha, foldable);
                else if (stroke && shader) paint = GetPaint(ref strokeShader, strokeId, alpha, foldable);
                else if (stroke) paint = GetPaint(ref strokeSolid, strokeId, alpha, foldable);
                else if (shader) paint = GetPaint(ref fillShader, fillId, alpha, foldable);
                else paint = GetPaint(ref fillSolid, fillId, alpha, foldable);
                values[index] = geometry with
                {
                    RecordedPaint = paint,
                    RecordedStrokePaint = aggregate && geometry.GradientStroke.Fill.HasFill && geometry.GradientStroke.HasStroke
                        ? GetPaint(ref strokeShader, strokeId, source.StrokeShaderAlpha, foldable)
                        : rectangleStroke ? GetPaint(ref strokeSolid, strokeId, source.StrokeAlpha, true) : default,
                };
            }
            if (source.PaintOrder.StrokeFirst() && values.Length > 1)
            {
                // Lowerers can emit several native pieces for either operation. A
                // stable partition changes only operation order, preserving each
                // native piece and its ordering within the fill or stroke.
                using BudgetedParserList<SvgGeometry> ordered = new(state.Budget, SvgMemoryCategory.Parser, work, token);
                for (int pass = 0; pass < 2; pass++)
                    foreach (SvgGeometry geometry in values)
                    {
                        work.Consume(token);
                        if (state.Scene.GetRecordedPaintValue(geometry.RecordedPaint).IsStroke == (pass == 0)) ordered.Add(geometry);
                    }
                ordered.AsSpan().CopyTo(values);
            }
            return parsed;

            SvgRecordedPaint GetPaint(ref SvgRecordedPaint cached, SvgPaintOperationIdentity identity, float alpha, bool foldable)
            {
                if (cached.Identity == 0) cached = state.Scene.RegisterRecordedPaint(new(identity, alpha, foldable, source.Scope, source.ElementOpacity, source.OpacityScope) { PaintOrder = source.PaintOrder, IsStroke = identity == strokeId, VisualBounds = visualBounds, ObjectBounds = objectBounds });
                return cached;
            }
        }
        catch { parsed.Discard(); throw; }
    }

    /// <summary>Adopts a lowered leaf batch into a replay scene, rebinding recorded paints from the document scene.</summary>
    /// <remarks>The target owns adopted geometry. Failure disposes all source values idempotently; both builders retain their bounded work/cancellation contracts.</remarks>
    internal static void AdoptLeafScene(SvgDefinition lowered, SvgSceneBuilder builder, SvgDocumentState state)
    {
        try
        {
            foreach (SvgGeometry geometry in lowered.Geometries)
            {
                SvgRecordedPaint fill = geometry.RecordedPaint.Identity > 0 ? builder.RegisterRecordedPaint(state.Scene.GetRecordedPaintValue(geometry.RecordedPaint)) : geometry.RecordedPaint;
                SvgRecordedPaint stroke = geometry.RecordedStrokePaint.Identity > 0 ? builder.RegisterRecordedPaint(state.Scene.GetRecordedPaintValue(geometry.RecordedStrokePaint)) : geometry.RecordedStrokePaint;
                builder.AddGeometry(geometry with { RecordedPaint = fill, RecordedStrokePaint = stroke });
            }
        }
        catch { foreach (SvgGeometry geometry in lowered.Geometries) geometry.Dispose(); throw; }
    }

    internal static void FinishScene(SvgDocumentState state, int first, ParserWorkCounter work, CancellationToken token)
    {
        if (first >= state.Scene.GeometryCount) return;
        SvgRecordedPaintValue source = state.Scene.GetRecordedPaintValue(state.Scene.GeometryAt(first).RecordedPaint);
        SvgPaintOpacity opacity = source.ElementOpacity;
        if (opacity.Value == 1 || state.Scene.GeometryAt(first).RecordedPaint.Identity == 0) return;
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true; operation.Value = 255; operation.SourceOpacity = opacity; operation.RecordingScope = source.OpacityScope;
        SvgLeafMaskBinding? lastInner = null;
        SvgLeafMaskBinding? lastBinding = null;
        for (int index = first; index < state.Scene.GeometryCount; index++)
        {
            work.Consume(token);
            SvgGeometry geometry = state.Scene.GeometryAt(index);
            if (lastBinding is null || !ReferenceEquals(lastInner, geometry.LeafMask))
            {
                lastInner = geometry.LeafMask;
                lastBinding = SvgLeafMaskComposition.NewBinding(state, operation, lastInner);
            }
            state.Scene.AssignRecordedGeometry(index, geometry with { LeafMask = lastBinding });
            ClearElementOpacity(geometry, state);
        }
    }

    internal static void FinishDefinition(SvgGeometry[] geometries, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (geometries.Length == 0 || geometries[0].RecordedPaint.Identity == 0) return;
        SvgRecordedPaintValue source = state.Scene.GetRecordedPaintValue(geometries[0].RecordedPaint);
        if (source.ElementOpacity.Value == 1) return;
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true; operation.Value = 255; operation.SourceOpacity = source.ElementOpacity; operation.RecordingScope = source.OpacityScope;
        SvgLeafMaskBinding? lastInner = null;
        SvgLeafMaskBinding? binding = null;
        for (int index = 0; index < geometries.Length; index++)
        {
            work.Consume(token);
            SvgGeometry geometry = geometries[index];
            if (binding is null || !ReferenceEquals(lastInner, geometry.LeafMask))
            {
                lastInner = geometry.LeafMask;
                binding = SvgLeafMaskComposition.NewBinding(state, operation, lastInner);
            }
            geometries[index] = geometry with { LeafMask = binding };
            ClearElementOpacity(geometry, state);
        }
    }
    private static void ClearElementOpacity(SvgGeometry geometry, SvgDocumentState state)
    {
        if (geometry.RecordedPaint.Identity != 0)
            state.Scene.ChangeRecordedPaint(geometry.RecordedPaint, state.Scene.GetRecordedPaintValue(geometry.RecordedPaint) with { ElementOpacity = SvgPaintOpacity.Opaque });
        if (geometry.RecordedStrokePaint.Identity != 0)
            state.Scene.ChangeRecordedPaint(geometry.RecordedStrokePaint, state.Scene.GetRecordedPaintValue(geometry.RecordedStrokePaint) with { ElementOpacity = SvgPaintOpacity.Opaque });
    }

    internal static SvgGeometry[] WrapInstance(SvgGeometry[] geometries, SvgPaintOpacity opacity, SvgRecordingScope scope, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        WrapInstance(geometries.AsSpan(), opacity, scope, state, work, token);
        return geometries;
    }

    internal static void WrapInstance(Span<SvgGeometry> geometries, SvgPaintOpacity opacity, SvgRecordingScope scope, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        => WrapInstance(geometries, opacity, scope, null, state, work, token);

    /// <summary>Wraps a borrowed geometry span with owned parser metadata for opacity and an optional outer viewport clip.
    /// Metadata reserves before allocation; each geometry update charges work and cancellation. No native geometry owner changes.</summary>
    internal static void WrapInstance(Span<SvgGeometry> geometries, SvgPaintOpacity opacity, SvgRecordingScope scope, SvgClipBox? viewportClip, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        => WrapInstance(geometries, opacity, scope, viewportClip, null, state, work, token);

    internal static void WrapInstance(Span<SvgGeometry> geometries, SvgPaintOpacity opacity, SvgRecordingScope scope, SvgClipBox? viewportClip, SvgAffineViewportClip? affineViewport, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if ((opacity.Value == 1 && viewportClip is null && affineViewport is null) || geometries.Length == 0) return;
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true; operation.Value = 255; operation.SourceOpacity = opacity; operation.RecordingScope = scope;
        operation.Region = viewportClip;
        operation.AffineViewport = affineViewport;
        operation.RegionKind = affineViewport is not null ? SvgLayerRegionKind.AffineViewport
            : viewportClip is { Antialiasing: SvgClipAntialiasing.None } ? SvgLayerRegionKind.Bounds
            : viewportClip is null ? SvgLayerRegionKind.Mask : SvgLayerRegionKind.Viewport;
        SvgLeafMaskBinding? lastInner = null;
        SvgLeafMaskBinding? binding = null;
        for (int index = 0; index < geometries.Length; index++)
        {
            work.Consume(token);
            SvgGeometry geometry = geometries[index];
            if (binding is null || !ReferenceEquals(lastInner, geometry.LeafMask))
            {
                lastInner = geometry.LeafMask;
                binding = SvgLeafMaskComposition.NewBinding(state, operation, lastInner);
            }
            geometries[index] = geometry with { LeafMask = binding };
        }
    }
}
