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
internal enum SvgRecordedPaintRole : byte { Fill = 0, Stroke = 1, MarkerContent = 2, EffectAnchor = 4 }
internal readonly record struct SvgRecordedPaintValue(SvgPaintOperationIdentity Identity, float Alpha, bool Foldable, SvgRecordedPaintRole Role, SvgPaintOrder PaintOrder, SvgRecordingScope Scope, SvgPaintOpacity ElementOpacity, SvgRecordingScope OpacityScope)
{
    internal SvgRecordedTransform? RecordingTransform { get; init; }
    /// <summary>Borrows the scene-owned native draw boundary independently of viewport clipping.</summary>
    internal SvgNativeCanvasStage? DrawMap { get; init; }
    // Resource paint stays independent when its host instance binds fill and stroke.
    internal bool IsStroke { get => (Role & SvgRecordedPaintRole.Stroke) != 0; init => Role = value ? Role | SvgRecordedPaintRole.Stroke : Role & ~SvgRecordedPaintRole.Stroke; }
    internal bool IsMarkerContent { get => (Role & SvgRecordedPaintRole.MarkerContent) != 0; init => Role = value ? Role | SvgRecordedPaintRole.MarkerContent : Role & ~SvgRecordedPaintRole.MarkerContent; }
    /// <summary>Marks a non-painting target carrying effects; only real source bounds contribute to ancestors.</summary>
    internal bool IsEffectAnchor { get => (Role & SvgRecordedPaintRole.EffectAnchor) != 0; init => Role = value ? Role | SvgRecordedPaintRole.EffectAnchor : Role & ~SvgRecordedPaintRole.EffectAnchor; }
    /// <summary>Retains the previous constructor contract; source recording assigns the stroke role explicitly.</summary>
    internal SvgRecordedPaintValue(SvgPaintOperationIdentity identity, float alpha, bool foldable, SvgRecordingScope scope, SvgPaintOpacity elementOpacity, SvgRecordingScope opacityScope)
        : this(identity, alpha, foldable, SvgRecordedPaintRole.Fill, SvgPaintOrder.FillStrokeMarkers, scope, elementOpacity, opacityScope) { }
    // Source visual bounds survive lowering; they do not describe a shader coordinate box or a clip.
    internal SvgPaintBounds? VisualBounds { get; init; }
    /// <summary>Retains native SVG visual bounds for source-image construction; painted ink stays in VisualBounds.</summary>
    internal SvgPaintBounds? SourceVisualBounds { get; init; }
    /// <summary>Retains SVG element visual bounds for filter output hints independently of recorded draws and captured ink.</summary>
    internal SvgPaintBounds? ElementVisualBounds { get; init; }
    internal SvgPaintBounds? ObjectBounds { get; init; }
    internal SvgSourceObjectBounds? SourceObjectBounds { get; init; }
}

/// <summary>Retains native authored object bounds separately from their scene placement.</summary>
/// <remarks>Immutable metadata owns no storage. A consumer composes into its own parameter
/// space before narrowing bounds, observing the caller's cancellation token.
/// Metadata maps require finite arithmetic; device admission belongs to the completed draw map.</remarks>
internal readonly record struct SvgSourceObjectBounds(SvgPaintBounds Bounds, SvgAffineTransform SceneMap);

internal readonly record struct SvgPaintBounds(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Maps an SVG layout rectangle in double precision before retaining float bounds.</summary>
    /// <remarks>Layout/filter hints follow SVG element mapping; recorded draw bounds use the software bitmap's float map.
    /// Inputs remain borrowed, the operation allocates nothing and every corner observes cancellation.</remarks>
    internal SvgPaintBounds MapLayout(SvgAffineTransform placement, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var first = placement.ApplyToUserPoint(Left, Top);
        token.ThrowIfCancellationRequested();
        var second = placement.ApplyToUserPoint(Right, Top);
        token.ThrowIfCancellationRequested();
        var third = placement.ApplyToUserPoint(Left, Bottom);
        token.ThrowIfCancellationRequested();
        var fourth = placement.ApplyToUserPoint(Right, Bottom);
        SvgPaintBounds result = new((float)Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X)),
            (float)Math.Min(Math.Min(first.Y, second.Y), Math.Min(third.Y, fourth.Y)),
            (float)Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X)),
            (float)Math.Max(Math.Max(first.Y, second.Y), Math.Max(third.Y, fourth.Y)));
        if (!float.IsFinite(result.Left) || !float.IsFinite(result.Top) || !float.IsFinite(result.Right) || !float.IsFinite(result.Bottom))
            throw new SvgException(SvgErrorCode.LimitExceeded, "SVG layout bounds exceed the admitted numeric range.");
        return result;
    }

    internal SvgPaintBounds Map(SvgAffineTransform placement, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SvgDevicePoint first = SvgAuthoredPath.MapPoint(placement, new(Left, Top), new(0, 0));
        token.ThrowIfCancellationRequested();
        SvgDevicePoint second = SvgAuthoredPath.MapPoint(placement, new(Right, Top), new(0, 0));
        token.ThrowIfCancellationRequested();
        SvgDevicePoint third = SvgAuthoredPath.MapPoint(placement, new(Left, Bottom), new(0, 0));
        token.ThrowIfCancellationRequested();
        SvgDevicePoint fourth = SvgAuthoredPath.MapPoint(placement, new(Right, Bottom), new(0, 0));
        return new((float)Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X)),
            (float)Math.Min(Math.Min(first.Y, second.Y), Math.Min(third.Y, fourth.Y)),
            (float)Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X)),
            (float)Math.Max(Math.Max(first.Y, second.Y), Math.Max(third.Y, fourth.Y)));
    }
}
internal enum SvgPaintOrigin { Solid, PaintServer }
internal readonly record struct SvgRecordedPaintStyle(SvgPaintOpacity ElementOpacity, float FillAlpha, float FillShaderAlpha, float StrokeAlpha, float StrokeShaderAlpha, SvgPaintOrigin FillOrigin, SvgPaintOrigin StrokeOrigin, bool HasFillOperation, SvgRecordingScope Scope, SvgRecordingScope OpacityScope)
{
    internal SvgRecordedTransform? RecordingTransform { get; init; }
    internal SvgPaintBounds? VisualBounds { get; init; }
    internal SvgPaintBounds? ObjectBounds { get; init; }
    internal SvgPaintBounds? SourceObjectBox { get; init; }
    internal SvgAffineTransform SourceObjectMap { get; init; } = SvgAffineTransform.Identity;
    internal float StrokeBoundsInflation { get; init; }
    internal SvgPaintOrder PaintOrder { get; init; }
    internal bool IsSuppressedSource { get; init; }
}

// Retains source paint and recording state until playback; parser budgets own staging and scene values.
internal static class SvgPaintRecording
{
    /// <summary>Distinguishes nonrenderable shape/image sources from eligible transparent input.</summary>
    /// <remarks>Callers still lower and validate the element. This only gates its own filter;
    /// enclosing containers and instances can filter transparent child input.</remarks>
    internal static bool IsFilterSource(XmlReader reader, SvgPaintableElementContext context)
    {
        switch (reader.LocalName)
        {
            case "rect":
                SvgRectangleSyntax rectangle = SvgRectangleSyntax.Parse(reader, context.LengthBases);
                return rectangle.Width > 0 && rectangle.Height > 0;
            case "circle":
            case "ellipse":
                SvgEllipseSyntax ellipse = reader.LocalName == "circle" ? SvgEllipseSyntax.ParseCircle(reader, context.LengthBases) : SvgEllipseSyntax.ParseEllipse(reader, context.LengthBases);
                return ellipse.RadiusX > 0 && ellipse.RadiusY > 0;
            case "image":
                SvgImageSyntax image = SvgImageSyntax.Parse(reader, context.LengthBases.Width, context.LengthBases.Height);
                return image.Href.Length > 0 && image.Width != 0 && image.Height != 0;
            default:
                return true;
        }
    }

    /// <summary>Creates a non-painting target that carries effects without contributing geometry bounds.</summary>
    /// <remarks>The scene owns charged paint metadata; the returned value owns no native storage.
    /// Registration shares the scene's work allowance and cancellation.</remarks>
    internal static SvgGeometry CreateEffectAnchor(SvgSceneBuilder scene, SvgDocumentState state, SvgRecordingScope scope)
    {
        SvgRecordedPaintValue paint = new(state.NewPaintOperationIdentity(), 0, false,
            scope, SvgPaintOpacity.Opaque, scope) { IsEffectAnchor = true };
        return SvgGeometry.FromRectangle(default) with { RecordedPaint = scene.RegisterRecordedPaint(paint) };
    }

    /// <summary>Records an empty eligible source for an active filter without inventing painted geometry.</summary>
    /// <remarks>Only empty batches need fallback bounds parsing. Native parsers retain authored limits,
    /// work and cancellation; the document owns paint metadata and the returned batch owns its container.</remarks>
    internal static SvgParsedGeometry Attach(SvgParsedGeometry parsed, SvgRecordedPaintStyle source, XmlReader reader,
        SvgPaintableElementContext context, SvgViewportFrame? frame, int maximumCommands, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (parsed.Geometries.Length != 0 || state.FilterGroupDepth <= 0 || !context.InheritedPresentation.IsDisplayed
            || style.GetDisplay() == SvgDisplay.None)
        {
            SvgParsedGeometry recorded = Attach(parsed, source, reader.LocalName, state, context.CancellationToken);
            return (style.NonScalingStroke || reader.LocalName is "polygon" or "polyline") && !SvgMarkerStyle.Resolve(style, context.InheritedPresentation.Markers).HasMarkers
                ? SvgNativeVisualBounds.Cull(recorded, reader, context, source, frame, maximumCommands, state) : recorded;
        }
        try
        {
            SvgPaintBounds? bounds = source.ObjectBounds;
            if (bounds is null)
            {
                SvgRectangle box;
                if (reader.LocalName == "path")
                {
                    using SvgRenderablePathData data = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumCommands, state.Budget, context.CancellationToken);
                    if (data.Value.Length == 0) return parsed;
                    using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Parser, context.CancellationToken);
                    SvgPathDataParser.ParseCommands(data.Value, maximumCommands, context.CancellationToken, commands);
                    box = SvgPathBounds.Compute(commands.AsSpan(), context.CancellationToken);
                }
                else if (reader.LocalName is "polygon" or "polyline")
                {
                    using BudgetedParserList<SvgFractionalPoint> points = SvgOrthogonalGeometryTokenizer.ParseFractionalPointList(reader.GetAttribute("points") ?? string.Empty,
                        reader.LocalName, maximumCommands, state.Budget, context.CancellationToken, 0);
                    if (points.Count == 0) return parsed;
                    float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                    foreach (SvgFractionalPoint point in points.AsSpan())
                    {
                        context.CancellationToken.ThrowIfCancellationRequested();
                        left = MathF.Min(left, (float)point.X); right = MathF.Max(right, (float)point.X);
                        top = MathF.Min(top, (float)point.Y); bottom = MathF.Max(bottom, (float)point.Y);
                    }
                    box = new(left, top, right - left, bottom - top, default, false, null);
                }
                else return parsed;
                bounds = new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height))
                    .Map(SvgViewportFrame.MapGeometry(context.ResolveTransform(reader), frame), context.CancellationToken);
            }
            SvgRecordedPaintValue paint = new(state.NewPaintOperationIdentity(), 0, false, source.Scope, source.ElementOpacity, source.OpacityScope)
            { IsEffectAnchor = true, ObjectBounds = bounds, VisualBounds = source.VisualBounds ?? bounds, PaintOrder = source.PaintOrder };
            using BudgetedGeometryBuilder anchor = new(state.Budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            anchor.Add(SvgGeometry.FromRectangle(default) with { RecordedPaint = state.Scene.RegisterRecordedPaint(paint) });
            parsed.Dispose();
            return new(anchor.Detach(), parsed.ElementCount);
        }
        catch { parsed.Discard(); throw; }
    }

    internal static SvgRecordedPaintStyle Resolve(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgViewportFrame? frame)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        _ = SvgPaintResolver.ResolvePaintServers(ref style, state, reader.LocalName, context.NamedColors);
        bool fillServer = style.Fill is not null && SvgPaintReference.TryParseId(style.Fill, out _);
        SvgPaint fill = SvgPaintResolver.Resolve(fillServer ? "white" : style.Fill, style.FillOpacity, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        SvgSolidPaint stroke = style.Stroke is null || style.Stroke == "none" || SvgPaintReference.TryParseId(style.Stroke, out _) ? new(new(255, 255, 255, 255), 1)
            : SvgPaintResolver.ResolveStrokePaint(style.Stroke, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        SvgPaintOpacity strokeOpacity = SvgColorParser.ParsePaintOpacity(style.StrokeOpacity);
        SvgRecordingScope opacityScope = context.RecordingScope.Value == 0 ? new(1) : context.RecordingScope;
        if (SvgLocalUrl.ReadReference(reader, "clip-path") is not null || context.ResolveTransform(reader) != context.InheritedPresentation.Transform)
            opacityScope = state.NewRecordingScope();
        SvgRecordingScope drawScope = SvgLocalUrl.ReadReference(reader, "mask") is null ? opacityScope : state.NewRecordingScope();
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
        SvgPaintBounds? objectBounds = PrimitiveBounds(reader, context, frame, 0, out SvgPaintBounds? sourceObjectBox, out SvgAffineTransform sourceObjectMap, out bool suppressedSource);
        return new(style.GetOpacity(), SvgPaintResolver.ResolveSolidPaint(fill).Alpha,
            fill.FillOpacity.Alpha / 255f, stroke.Alpha * strokeOpacity.Value, strokeOpacity.Alpha / 255f,
            fillServer ? SvgPaintOrigin.PaintServer : SvgPaintOrigin.Solid,
            style.Stroke is not null && SvgPaintReference.TryParseId(style.Stroke, out _) ? SvgPaintOrigin.PaintServer : SvgPaintOrigin.Solid,
            fill.HasFill, drawScope, opacityScope) { RecordingTransform = context.InheritedPresentation.RecordingTransform, IsSuppressedSource = suppressedSource || !context.InheritedPresentation.IsDisplayed || style.GetDisplay() == SvgDisplay.None, PaintOrder = style.PaintOrder ?? context.InheritedPresentation.PaintOrder, ObjectBounds = objectBounds, SourceObjectBox = sourceObjectBox, SourceObjectMap = sourceObjectMap, VisualBounds = strokeBoundsInflation > 0 ? PrimitiveBounds(reader, context, frame, strokeBoundsInflation, out _, out _, out _) : null, StrokeBoundsInflation = strokeBoundsInflation };

        static SvgPaintBounds? PrimitiveBounds(XmlReader input, SvgPaintableElementContext element, SvgViewportFrame? viewport, float inflation, out SvgPaintBounds? sourceObjectBox, out SvgAffineTransform sourceObjectMap, out bool suppressedSource)
        {
            sourceObjectBox = null;
            sourceObjectMap = SvgAffineTransform.Identity;
            suppressedSource = false;
            element.CancellationToken.ThrowIfCancellationRequested();
            SvgPaintBounds bounds;
            switch (input.LocalName)
            {
                case "rect":
                    SvgRectangleSyntax rectangle = SvgRectangleSyntax.Parse(input, element.LengthBases);
                    suppressedSource = rectangle.Width == 0 || rectangle.Height == 0;
                    bounds = new((float)rectangle.X, (float)rectangle.Y, (float)(rectangle.X + rectangle.Width), (float)(rectangle.Y + rectangle.Height));
                    break;
                case "image":
                    // Image lowering records bounds after decoding resolves intrinsic dimensions.
                    SvgImageSyntax image = SvgImageSyntax.Parse(input, element.LengthBases.Width, element.LengthBases.Height);
                    suppressedSource = image.Href.Length == 0 || image.Width == 0 || image.Height == 0;
                    return null;
                case "circle":
                case "ellipse":
                    SvgEllipseSyntax ellipse = input.LocalName == "circle" ? SvgEllipseSyntax.ParseCircle(input, element.LengthBases) : SvgEllipseSyntax.ParseEllipse(input, element.LengthBases);
                    suppressedSource = ellipse.RadiusX == 0 || ellipse.RadiusY == 0;
                    bounds = new((float)(ellipse.CenterX - ellipse.RadiusX), (float)(ellipse.CenterY - ellipse.RadiusY),
                        (float)(ellipse.CenterX + ellipse.RadiusX), (float)(ellipse.CenterY + ellipse.RadiusY));
                    break;
                case "line":
                    SvgLineSyntax line = SvgLineSyntax.Parse(input);
                    bounds = new((float)Math.Min(line.X1, line.X2), (float)Math.Min(line.Y1, line.Y2), (float)Math.Max(line.X1, line.X2), (float)Math.Max(line.Y1, line.Y2));
                    break;
                default:
                    return null;
            }
            sourceObjectBox = bounds;
            if (inflation > 0) bounds = new(bounds.Left - inflation, bounds.Top - inflation, bounds.Right + inflation, bounds.Bottom + inflation);
            SvgAffineTransform placement = element.ResolveTransform(input);
            if (viewport is {} parent)
                placement = SvgAffineTransform.FromScaleOffset(parent.ScaleX, parent.ScaleY, parent.OffsetX, parent.OffsetY).ComposeChecked(placement);
            sourceObjectMap = placement;
            return bounds.Map(placement, element.CancellationToken);
        }
    }

    internal static SvgParsedGeometry Attach(SvgParsedGeometry parsed, SvgRecordedPaintStyle source, string element, SvgDocumentState state, CancellationToken token)
    {
        try
        {
            SvgPaintOperationIdentity fillId = state.NewPaintOperationIdentity();
            SvgPaintOperationIdentity strokeId = state.NewPaintOperationIdentity();
            SvgRecordedPaint fillSolid = default, fillShader = default, strokeSolid = default, strokeShader = default;
            Span<SvgGeometry> values = parsed.Storage.Items;
            SvgPaintBounds? objectBounds = source.ObjectBounds;
            SvgPaintBounds? sourceObjectBox = parsed.SourceObjectBox ?? source.SourceObjectBox;
            SvgAffineTransform sourceObjectMap = parsed.SourceObjectBox is not null ? parsed.SourceObjectMap : source.SourceObjectMap;
            SvgPaintBounds? visualBounds = source.VisualBounds;
            if (parsed.SourceObjectBox is {} inputBox)
            {
                objectBounds = inputBox.Map(parsed.SourceObjectMap, token);
                if (source.StrokeBoundsInflation > 0)
                {
                    float inflation = source.StrokeBoundsInflation;
                    visualBounds = new SvgPaintBounds(MathF.Floor(inputBox.Left - inflation), MathF.Floor(inputBox.Top - inflation),
                        MathF.Ceiling(inputBox.Right + inflation), MathF.Ceiling(inputBox.Bottom + inflation)).Map(parsed.SourceObjectMap, token);
                }
            }
            if (objectBounds is null && values.Length != 0)
            {
                // A path's non-painting fill can precede its retained centerline.
                // Other elements retain primitive or point-list bounds separately;
                // their lowered stroke paths must not replace those source bounds.
                SvgAuthoredPath? sourcePath = values[0].AuthoredPath;
                if (sourcePath is null && element == "path")
                {
                    foreach (SvgGeometry geometry in values)
                    {
                        token.ThrowIfCancellationRequested();
                        if (geometry.AuthoredPath is not { StrokeStyle: not null } candidate) continue;
                        sourcePath = candidate;
                        break;
                    }
                }
                if (sourcePath is {} path)
                {
                    SvgRectangle box = SvgPathBounds.Compute(path.Commands, token);
                    sourceObjectBox = new((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height));
                    sourceObjectMap = path.SceneMap;
                    objectBounds = new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height)).Map(path.SceneMap, token);
                    if (source.StrokeBoundsInflation > 0)
                    {
                        float inflation = source.StrokeBoundsInflation;
                        visualBounds = new SvgPaintBounds(MathF.Floor((float)box.X - inflation), MathF.Floor((float)box.Y - inflation),
                            MathF.Ceiling((float)(box.X + box.Width) + inflation), MathF.Ceiling((float)(box.Y + box.Height) + inflation)).Map(path.SceneMap, token);
                    }
                }
                else if (element is "polygon" or "polyline")
                {
                    float left = float.PositiveInfinity, top = float.PositiveInfinity;
                    float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                    foreach (SvgGeometry part in values)
                    {
                        token.ThrowIfCancellationRequested();
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
                token.ThrowIfCancellationRequested();
                SvgGeometry geometry = values[index];
                if (sourceObjectBox is {} patternBox)
                {
                    if (geometry.Kind == SvgGeometryKind.PatternFill) geometry.Pattern.BindObjectBox(patternBox);
                    else if (geometry.Kind == SvgGeometryKind.PatternPolygon) geometry.PatternPolygon.BindObjectBox(patternBox);
                }
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
                using BudgetedParserList<SvgGeometry> ordered = new(state.Budget, SvgMemoryCategory.Parser, token);
                for (int pass = 0; pass < 2; pass++)
                    foreach (SvgGeometry geometry in values)
                    {
                        token.ThrowIfCancellationRequested();
                        if (state.Scene.GetRecordedPaintValue(geometry.RecordedPaint).IsStroke == (pass == 0)) ordered.Add(geometry);
                    }
                ordered.AsSpan().CopyTo(values);
            }
            return parsed;

            SvgRecordedPaint GetPaint(ref SvgRecordedPaint cached, SvgPaintOperationIdentity identity, float alpha, bool foldable)
            {
                if (cached.Identity == 0)
                {
                    bool anchor = source.IsSuppressedSource || identity == fillId && !source.HasFillOperation;
                    cached = state.Scene.RegisterRecordedPaint(new(identity, alpha, foldable, source.Scope, source.ElementOpacity, source.OpacityScope)
                    {
                        RecordingTransform = source.RecordingTransform, PaintOrder = source.PaintOrder, IsStroke = identity == strokeId,
                        IsEffectAnchor = anchor, VisualBounds = anchor ? null : visualBounds,
                        ObjectBounds = source.IsSuppressedSource ? null : objectBounds,
                        SourceObjectBounds = !source.IsSuppressedSource && sourceObjectBox is {} rawBox ? new(rawBox, sourceObjectMap) : null
                    });
                }
                return cached;
            }
        }
        catch { parsed.Discard(); throw; }
    }

    /// <summary>Adopts a lowered leaf batch into a replay scene, rebinding recorded paints from the document scene.</summary>
    /// <remarks>The target owns adopted geometry. Failure disposes all source values idempotently; both builders retain their bounded cancellation contracts.</remarks>
    internal static void AdoptLeafScene(SvgDefinition lowered, SvgSceneBuilder builder, SvgDocumentState state,
CancellationToken token)
    {
        SvgLeafMaskMapper? masks = null;
        try
        {
            foreach (SvgGeometry geometry in lowered.Geometries)
            {
                if (geometry.LeafMask is not null && !ReferenceEquals(state.Scene, builder))
                    masks ??= SvgLeafMaskMapper.CreateSceneTransfer(state, state.Scene, builder, token);
                builder.AddGeometry(RebindLeafScene(geometry, state.Scene, builder, masks));
            }
        }
        catch { foreach (SvgGeometry geometry in lowered.Geometries) geometry.Dispose(); throw; }
        finally { masks?.Dispose(); }
    }

    /// <summary>Rebinds borrowed paints and scene-local effects before owned geometry moves into another scene.</summary>
    /// <remarks>Geometry ownership stays with the caller until adoption. Destination registration
    /// reserves scene storage and checks cancellation; references are scene-local.</remarks>
    internal static SvgGeometry RebindLeafScene(SvgGeometry geometry, SvgSceneBuilder source, SvgSceneBuilder destination, SvgLeafMaskMapper? masks)
    {
        SvgRecordedPaint fill = geometry.RecordedPaint.Identity > 0
            ? destination.RegisterRecordedPaint(source.GetRecordedPaintValue(geometry.RecordedPaint)) : geometry.RecordedPaint;
        SvgRecordedPaint stroke = geometry.RecordedStrokePaint.Identity > 0
            ? destination.RegisterRecordedPaint(source.GetRecordedPaintValue(geometry.RecordedStrokePaint)) : geometry.RecordedStrokePaint;
        return geometry with { RecordedPaint = fill, RecordedStrokePaint = stroke, LeafMask = masks is null ? geometry.LeafMask : masks.Map(geometry.LeafMask) };
    }

    internal static void FinishScene(SvgDocumentState state, int first, CancellationToken token)
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
            token.ThrowIfCancellationRequested();
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

    internal static void FinishDefinition(SvgGeometry[] geometries, SvgDocumentState state, CancellationToken token)
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
            token.ThrowIfCancellationRequested();
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

    internal static SvgGeometry[] WrapInstance(SvgGeometry[] geometries, SvgPaintOpacity opacity, SvgRecordingScope scope, SvgDocumentState state, CancellationToken token)
    {
        WrapInstance(geometries.AsSpan(), opacity, scope, state, token);
        return geometries;
    }

    internal static void WrapInstance(Span<SvgGeometry> geometries, SvgPaintOpacity opacity, SvgRecordingScope scope, SvgDocumentState state, CancellationToken token)
        => WrapInstance(geometries, opacity, scope, null, state, token);

    /// <summary>Wraps a borrowed geometry span with owned parser metadata for opacity and an optional outer viewport clip.
    /// Metadata reserves before allocation; each geometry update observes cancellation. No native geometry owner changes.</summary>
    internal static void WrapInstance(Span<SvgGeometry> geometries, SvgPaintOpacity opacity, SvgRecordingScope scope, SvgClipBox? viewportClip, SvgDocumentState state, CancellationToken token)
        => WrapInstance(geometries, opacity, scope, viewportClip, null, state, token);

    internal static void WrapInstance(Span<SvgGeometry> geometries, SvgPaintOpacity opacity, SvgRecordingScope scope, SvgClipBox? viewportClip, SvgAffineViewportClip? affineViewport, SvgDocumentState state, CancellationToken token)
    {
        if ((opacity.Value == 1 && viewportClip is null && affineViewport is null) || geometries.Length == 0) return;
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true; operation.Value = 255; operation.SourceOpacity = opacity; operation.RecordingScope = scope;
        operation.Region = viewportClip;
        operation.AffineViewport = affineViewport;
        operation.RegionKind = affineViewport is not null ? SvgLayerRegionKind.AffineViewport
            : viewportClip is { Antialiasing: SvgClipAntialiasing.None } ? SvgLayerRegionKind.Bounds
            : viewportClip is null ? SvgLayerRegionKind.Mask : SvgLayerRegionKind.Viewport;
        WrapOperation(geometries, operation, state, token);
    }

    /// <summary>Retains an authored local filter until its owning scene emits the completed source range.</summary>
    internal static void WrapFilter(Span<SvgGeometry> geometries, string? filter, SvgAffineTransform transform, SvgViewportLengthBases lengthBases,
        SvgDocumentState state, CancellationToken token)
    {
        if (geometries.IsEmpty) return;
        if (SvgRetainedFilter.Create(filter, transform, lengthBases, state) is {} retained)
            WrapFilter(geometries, retained, state, token);
    }

    /// <summary>Retains an isolated filter around an ordered borrowed batch without copying geometry or resource storage.</summary>
    internal static void WrapFilter(Span<SvgGeometry> geometries, SvgRetainedFilter filter, SvgDocumentState state, CancellationToken token)
    {
        if (geometries.IsEmpty) return;
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true; operation.PreservesIsolation = true; operation.Value = 255;
        operation.RegionKind = SvgLayerRegionKind.Filter; operation.Filter = filter; operation.RecordingScope = state.NewRecordingScope();
        WrapOperation(geometries, operation, state, token);
    }

    private static void WrapOperation(Span<SvgGeometry> geometries, SvgLeafMaskOperation operation, SvgDocumentState state, CancellationToken token)
    {
        SvgLeafMaskBinding? lastInner = null;
        SvgLeafMaskBinding? binding = null;
        for (int index = 0; index < geometries.Length; index++)
        {
            token.ThrowIfCancellationRequested();
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
