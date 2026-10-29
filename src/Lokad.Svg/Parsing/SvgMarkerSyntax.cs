using System.Globalization;
using System.Xml;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgMarkerOrientation { Angle, Auto, AutoStartReverse }

/// <summary>Retains a marker's local viewport and vertex-placement parameters.</summary>
internal readonly record struct SvgMarkerDefinition(double Width, double Height, double RefX, double RefY,
    bool StrokeWidthUnits, SvgMarkerOrientation Orientation, double Angle)
{
    internal bool ClipsViewport { get; init; } = true;
    /// <summary>Retains definition availability before resource placement and font-relative coordinate resolution.</summary>
    internal bool IsAvailable { get; init; } = true;
    internal SvgMarkerFontLengths? FontLengths { get; init; }
}

/// <summary>Retains ex factors and authored font context until a live marker needs font metrics.</summary>
/// <remarks>Immutable parser metadata borrows style strings and owns no programs. The existing
/// definition allowance covers it; unused or zero-size markers never acquire a font.</remarks>
internal sealed record SvgMarkerFontLengths(SvgInheritedTextStyle Font, double? Width, double? Height, double? RefX, double? RefY);

/// <summary>Retains inherited marker declarations independently of fill and stroke paint.</summary>
internal readonly record struct SvgMarkerStyle(string? Start, string? Mid, string? End)
{
    internal bool HasMarkers => Active(Start) || Active(Mid) || Active(End);
    internal static bool Active(string? value) => value is not null && !value.Equals("none", StringComparison.OrdinalIgnoreCase);

    internal static SvgMarkerStyle Resolve(SvgComputedStyle style, SvgMarkerStyle inherited)
        => new SvgMarkerStyle(style.MarkerStart, style.MarkerMid, style.MarkerEnd).InheritFrom(inherited);

    internal SvgMarkerStyle InheritFrom(SvgMarkerStyle inherited)
    {
        return new(Select(Start, inherited.Start), Select(Mid, inherited.Mid), Select(End, inherited.End));
        static string? Select(string? value, string? parent)
        {
            string? input = value is null ? null : SvgCssWhitespace.Trim(value);
            return input is null || input.Equals("inherit", StringComparison.OrdinalIgnoreCase) || input.Equals("unset", StringComparison.OrdinalIgnoreCase)
                ? parent : input.Equals("initial", StringComparison.OrdinalIgnoreCase) ? "none" : input;
        }
    }

    internal SvgMarkerStyle Retain(SvgDocumentState state, CancellationToken token)
    {
        return new(Copy(Start), Copy(Mid), Copy(End));
        string? Copy(string? value)
        {
            if (value is null) return null;
            token.ThrowIfCancellationRequested();
            return state.Strings.Copy(value);
        }
    }
}

internal static class SvgMarkerSyntax
{
    internal static void ValidateValue(ReadOnlySpan<char> value)
    {
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Marker references exceed the configured syntax limit.");
        ReadOnlySpan<char> input = SvgCssWhitespace.Trim(value);
        if (input.Equals("none", StringComparison.OrdinalIgnoreCase) || input.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            || input.Equals("unset", StringComparison.OrdinalIgnoreCase) || input.Equals("initial", StringComparison.OrdinalIgnoreCase)) return;
        if (!SvgLocalUrl.TryParseId(input.ToString(), out _))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only none or local marker references are implemented.");
    }

    internal static SvgMarkerDefinition Parse(XmlReader reader, SvgComputedStyle style, SvgViewportLengthBases bases, CancellationToken token)
        => Parse(reader, style, bases, SvgInheritedTextStyle.Default, default, token);

    internal static SvgMarkerDefinition Parse(XmlReader reader, SvgComputedStyle style, SvgViewportLengthBases bases,
        SvgInheritedTextStyle inheritedFont, InlineStyle sheet, CancellationToken token)
    {
        SvgElementAttributePolicy.ValidateMarker(reader, token);
        string overflow = style.Overflow ?? "hidden";
        bool visibleOverflow = SvgCssWhitespace.Trim(overflow.AsSpan()).Equals("visible", StringComparison.OrdinalIgnoreCase);
        if (!visibleOverflow) SvgNestedViewportSyntax.ValidateOverflow(overflow);
        _ = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), token);
        SvgInheritedTextStyle? font = null;
        double? widthEx = null, heightEx = null, refXEx = null, refYEx = null;
        double width = Coordinate(reader.GetAttribute("markerWidth"), bases.Width, 3, ref widthEx);
        double height = Coordinate(reader.GetAttribute("markerHeight"), bases.Height, 3, ref heightEx);
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Marker dimensions cannot be negative.");
        string units = reader.GetAttribute("markerUnits") ?? "strokeWidth";
        if (units is not ("strokeWidth" or "userSpaceOnUse"))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only strokeWidth and userSpaceOnUse marker units are implemented.");
        string authoredOrient = reader.GetAttribute("orient") ?? "0";
        ValidateLength(authoredOrient);
        string orient = SvgCssWhitespace.Trim(authoredOrient);
        SvgMarkerOrientation orientation = orient == "auto" ? SvgMarkerOrientation.Auto
            : orient == "auto-start-reverse" ? SvgMarkerOrientation.AutoStartReverse : SvgMarkerOrientation.Angle;
        double angle = orientation == SvgMarkerOrientation.Angle ? Angle(orient) : 0;
        double refX = Coordinate(reader.GetAttribute("refX"), bases.Width, 0, ref refXEx);
        double refY = Coordinate(reader.GetAttribute("refY"), bases.Height, 0, ref refYEx);
        SvgMarkerFontLengths? lengths = widthEx is null && heightEx is null && refXEx is null && refYEx is null ? null
            : new(font ?? throw new InvalidOperationException("Relative coordinates require their font context."), widthEx, heightEx, refXEx, refYEx);
        return new(width, height, refX, refY, units == "strokeWidth", orientation, angle) { ClipsViewport = !visibleOverflow, FontLengths = lengths };

        double Coordinate(string? value, double? basis, double fallback, ref double? ex)
        {
            if (value is null) return fallback;
            ValidateLength(value);
            ReadOnlySpan<char> input = value.AsSpan().Trim(SvgCssWhitespace.Characters);
            if (input.EndsWith('%'))
            {
                if (basis is not {} extent) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Percentage marker coordinates require a known enclosing viewport.");
                return Finite(Number(input[..^1], false) * (float)extent / 100);
            }
            if (input.EndsWith("em", StringComparison.OrdinalIgnoreCase) || input.EndsWith("ex", StringComparison.OrdinalIgnoreCase))
            {
                if (font is null)
                {
                    InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), token, true);
                    font = SvgTextInheritance.Resolve(reader, inheritedFont,
                        SvgInlineStyleParser.MergeFont(inline.TextFont, inline.Important, sheet.TextFont, sheet.Important), null, null);
                }
                double factor = Number(input[..^2], false);
                if (input.EndsWith("ex", StringComparison.OrdinalIgnoreCase))
                {
                    ex = factor;
                    return factor; // Only sign/zero are needed until live font metrics resolve the coordinate.
                }
                return Finite(factor * (float)font.FontSize);
            }
            double scale = 1;
            int suffix = 2;
            if (input.EndsWith("px", StringComparison.OrdinalIgnoreCase)) { }
            else if (input.EndsWith("in", StringComparison.OrdinalIgnoreCase)) scale = 96;
            else if (input.EndsWith("cm", StringComparison.OrdinalIgnoreCase)) scale = 96d / 2.54d;
            else if (input.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) scale = 96d / 25.4d;
            else if (input.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) scale = 96d / 72d;
            else if (input.EndsWith("pc", StringComparison.OrdinalIgnoreCase)) scale = 16;
            else if (input.EndsWith("q", StringComparison.OrdinalIgnoreCase)) { scale = 96d / 101.6d; suffix = 1; }
            else suffix = 0;
            return Finite(Number(suffix == 0 ? input : input[..^suffix], false) * scale);
        }

        double Angle(string value)
        {
            ReadOnlySpan<char> input = value.AsSpan().Trim(SvgCssWhitespace.Characters);
            double scale = 1;
            int suffix = 3;
            if (input.EndsWith("deg", StringComparison.Ordinal)) { }
            else if (input.EndsWith("grad", StringComparison.Ordinal)) { scale = .9; suffix = 4; }
            else if (input.EndsWith("rad", StringComparison.Ordinal)) scale = 180 / Math.PI;
            else if (input.EndsWith("turn", StringComparison.Ordinal)) { scale = 360; suffix = 4; }
            else suffix = 0;
            return Finite(Number(suffix == 0 ? input : input[..^suffix], true) * scale);
        }

        void ValidateLength(string value)
        {
            if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Marker values exceed the configured syntax limit.");
            token.ThrowIfCancellationRequested();
        }
        double Number(ReadOnlySpan<char> value, bool svgAngle)
        {
            if (SvgCssWhitespace.Trim(value).Length != value.Length || !double.TryParse(value, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                throw new SvgException(SvgErrorCode.MalformedInput, "Marker values must be finite numbers.");
            return Finite(svgAngle ? SvgFloatNumber.ReadValidated(value, token) : number);
        }
        static float Finite(double number)
        {
            if (!float.IsFinite((float)number)) throw new SvgException(SvgErrorCode.LimitExceeded, "Marker values exceed the finite float range.");
            return (float)number;
        }
    }
}

/// <summary>Places marker content through bounded symbol replay, preserving its source paint and viewport clip.</summary>
internal static class SvgMarkerRenderer
{
    internal static SvgParsedGeometry AttachLine(SvgParsedGeometry body, XmlReader reader, SvgPaintableElementContext context,
        SvgRecordedPaintStyle paint, SvgViewportFrame? frame, SvgDocumentState state, SvgMarkerStyle markers)
    {
        if (!markers.HasMarkers) return body;
        Span<SvgMarkerPosition> positions = stackalloc SvgMarkerPosition[2];
        try
        {
            SvgLineSyntax line = SvgLineSyntax.Parse(reader);
            double angle = MathF.Atan2((float)line.Y2 - (float)line.Y1, (float)line.X2 - (float)line.X1) * (180f / MathF.PI);
            positions[0] = new(SvgMarkerVertexRole.Start, new(line.X1, line.Y1), angle);
            positions[1] = new(SvgMarkerVertexRole.End, new(line.X2, line.Y2), angle);
        }
        catch { body.Discard(); throw; }
        return Attach(body, positions, markers, reader, context, paint, frame, state, int.MaxValue);
    }

    internal static SvgParsedGeometry AttachVertices(SvgParsedGeometry body, XmlReader reader, SvgPaintableElementContext context,
        SvgRecordedPaintStyle paint, SvgViewportFrame? frame, SvgDocumentState state, SvgMarkerStyle markers, int maximumCommands)
    {
        if (!markers.HasMarkers) return body;
        BudgetedParserList<SvgMarkerPosition> positions;
        try { positions = SvgMarkerVertices.Read(reader, maximumCommands, state, context.CancellationToken); }
        catch { body.Discard(); throw; }
        using (positions) return Attach(body, positions.AsSpan(), markers, reader, context, paint, frame, state, maximumCommands);
    }

    private static SvgParsedGeometry Attach(SvgParsedGeometry body, ReadOnlySpan<SvgMarkerPosition> positions,
        SvgMarkerStyle markers, XmlReader reader, SvgPaintableElementContext context,
        SvgRecordedPaintStyle paint, SvgViewportFrame? frame, SvgDocumentState state, int maximumCommands)
    {
        // A later instance map must see the marker's native corners, rather than
        // a rectangle flattened during the host's source construction.
        static SvgGeometry RetainRectangle(SvgGeometry geometry, SvgDocumentState state, CancellationToken token)
        {
            SvgRectangle box = geometry.Rectangle;
            SvgPaint paint = new(new(box.Color, box.Color.Alpha / 255f), box.HasFill, SvgPaintOpacity.Opaque, new(box.Color, box.Color.Alpha / 255f), false);
            SvgResolvedPaintableElement element = new(default, SvgAffineTransform.Identity, new(paint, box.Color, null, 255, box.HasFill), 1);
            using BudgetedParserList<SvgPoint> points = new(state.Budget, SvgMemoryCategory.Geometry, token);
            using BudgetedParserList<int> ends = new(state.Budget, SvgMemoryCategory.Geometry, token);
            using SvgOrthogonalGeometryLowering lowering = new(points, ends, "rect", false, false, int.MaxValue, state.Budget, token);
            SvgViewportFrame? clipFrame = box.Clip is {} clip ? new(0, 0, 1, 1, null, null, clip, -1) : null;
            using SvgParsedGeometry result = lowering.LowerRectangleSourceFill(box.X, box.Y, box.Width, box.Height, element, clipFrame, state.Budget);
            if (result.Geometries.Length != 1) throw new InvalidOperationException("A retained marker rectangle requires one contour.");
            return result.Geometries[0] with { RecordedPaint = geometry.RecordedPaint, RecordedStrokePaint = geometry.RecordedStrokePaint, LeafMask = geometry.LeafMask };
        }
        CancellationToken token = context.CancellationToken;
        using BudgetedParserList<SvgGeometry> output = new(state.Budget, SvgMemoryCategory.Geometry, token);
        try
        {
            SvgComputedStyle style = context.ResolveStyle(reader);
            double width = SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width") ?? "1", reader.LocalName);
            ReadOnlySpan<int> order = paint.PaintOrder switch
            {
                SvgPaintOrder.FillStrokeMarkers => [0, 1, 2],
                SvgPaintOrder.FillMarkersStroke => [0, 2, 1],
                SvgPaintOrder.StrokeFillMarkers => [1, 0, 2],
                SvgPaintOrder.StrokeMarkersFill => [1, 2, 0],
                SvgPaintOrder.MarkersFillStroke => [2, 0, 1],
                SvgPaintOrder.MarkersStrokeFill => [2, 1, 0],
                _ => throw new InvalidOperationException("Unexpected marker paint order.")
            };
            foreach (int operation in order)
            {
                if (operation != 2) { AddBody(operation == 1); continue; }
                foreach (SvgMarkerPosition position in positions)
                {
                    token.ThrowIfCancellationRequested();
                    string? value = position.Role switch
                    {
                        SvgMarkerVertexRole.Start => markers.Start,
                        SvgMarkerVertexRole.Mid => markers.Mid,
                        SvgMarkerVertexRole.End => markers.End,
                        _ => throw new InvalidOperationException("Unexpected marker vertex role.")
                    };
                    Add(value, position.Point.X, position.Point.Y, position.Role == SvgMarkerVertexRole.Start, position.Angle);
                }
            }
            SvgClipBox? layerRegion = null;
            if (paint.ElementOpacity.Value != 1 && output.Count != 0)
            {
                // Native SVG isolation rounds the complete element's visual bounds
                // in user coordinates before mapping the layer into its bitmap.
                float left = float.PositiveInfinity, top = float.PositiveInfinity;
                float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                foreach (SvgGeometry geometry in output.AsSpan())
                {
                    token.ThrowIfCancellationRequested();
                    SvgPaintBounds bounds;
                    if (geometry.RecordedPaint.Identity > 0 && state.Scene.GetRecordedPaintValue(geometry.RecordedPaint).VisualBounds is {} visual)
                        bounds = visual;
                    else
                    {
                        SvgAuthoredPath? path = geometry.AuthoredPath?.StrokeOutline ?? geometry.AuthoredPath;
                        SvgRectangle box = path is null ? SvgMaskSyntax.BoundingBox(geometry) : SvgPathBounds.Compute(path.Commands, token);
                        bounds = new((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height));
                        if (path is not null) bounds = bounds.Map(path.SceneMap, token);
                    }
                    left = MathF.Min(left, bounds.Left); top = MathF.Min(top, bounds.Top);
                    right = MathF.Max(right, bounds.Right); bottom = MathF.Max(bottom, bounds.Bottom);
                }
                layerRegion = new(MathF.Floor(left), MathF.Floor(top), MathF.Ceiling(right) - MathF.Floor(left), MathF.Ceiling(bottom) - MathF.Floor(top))
                    { Antialiasing = SvgClipAntialiasing.None };
            }
            SvgPaintRecording.WrapInstance(output.AsMutableSpan(), paint.ElementOpacity, state.NewRecordingScope(), layerRegion, state, token);
            return SvgNativeVisualBounds.Cull(new(output.Detach(), body.ElementCount), reader, context, paint, frame, maximumCommands, state);

            void AddBody(bool strokeOperation)
            {
                for (int index = 0; index < body.Geometries.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgGeometry geometry = body.Geometries[index];
                    if (geometry.RecordedPaint.Identity == 0 || state.Scene.GetRecordedPaintValue(geometry.RecordedPaint).IsStroke != strokeOperation) continue;
                    output.Add(geometry);
                    // Each successful insertion transfers this owner once. A failed
                    // insertion leaves it in body for the common denial cleanup.
                    body.Storage.Items[index] = default;
                }
            }
            void Add(string? value, double x, double y, bool start, double tangent)
            {
                if (!SvgMarkerStyle.Active(value)) return;
                token.ThrowIfCancellationRequested();
                string id = SvgGradientSyntax.ParsePaintId(value);
                if (!state.Definitions.TryGetValue(id, out SvgDefinition source) || source.Marker is not {} marker)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local marker references are implemented.");
                if (!marker.IsAvailable || marker.Width == 0 || marker.Height == 0 || marker.StrokeWidthUnits && width == 0
                    || !context.InheritedPresentation.IsDisplayed || style.GetDisplay() == SvgDisplay.None
                    || !SvgPresentationResolver.ParseVisibility(style.Visibility, context.InheritedPresentation.IsVisible)) return;
                if (marker.FontLengths is {} lengths)
                {
                    SvgFontSnapshot snapshot = state.TextReplay?.Fonts
                        ?? throw new SvgException(SvgErrorCode.UnresolvedResource, "Font-relative marker coordinates require a supplied font snapshot.");
                    string family = lengths.Font.Family ?? throw new SvgException(SvgErrorCode.UnresolvedResource, "Font-relative marker coordinates require a font family.");
                    SvgPreparedFont font = snapshot.GetFont(SvgFontFaceRequest.FromCss(family, lengths.Font.Weight, lengths.Font.Italic, null, token), id, token);
                    int xHeight = font.GetXHeight(token);
                    float ex = xHeight > 0 ? (float)lengths.Font.FontSize * xHeight / font.UnitsPerEm : (float)lengths.Font.FontSize * .5f;
                    marker = marker with { Width = Resolve(lengths.Width, marker.Width), Height = Resolve(lengths.Height, marker.Height),
                        RefX = Resolve(lengths.RefX, marker.RefX), RefY = Resolve(lengths.RefY, marker.RefY) };

                    double Resolve(double? factor, double absolute)
                    {
                        if (factor is not {} value) return absolute;
                        float coordinate = (float)value * ex;
                        if (!float.IsFinite(coordinate)) throw new SvgException(SvgErrorCode.LimitExceeded, "Marker font-relative coordinates exceed the finite range.");
                        return coordinate;
                    }
                }
                if (marker.Width == 0 || marker.Height == 0) return;
                double unitScale = marker.StrokeWidthUnits ? width : 1;
                bool nonScalingUnits = marker.StrokeWidthUnits && style.NonScalingStroke;
                if (nonScalingUnits)
                {
                    SvgAffineTransform host = context.StrokeHostMap.ComposeChecked(
                        SvgViewportFrame.MapGeometry(context.ResolveTransform(reader), frame));
                    if (!host.TryInvert(out _)) return;
                    // Stroke units use the root-mean-square scale of both axes,
                    // including the live instance, viewport and output maps.
                    float scale = (float)Math.Sqrt(((host.A * host.A + host.B * host.B)
                        + (host.C * host.C + host.D * host.D)) / 2);
                    unitScale = (float)width / scale;
                    if (!double.IsFinite(unitScale)) return;
                }
                double angle = marker.Orientation == SvgMarkerOrientation.Angle ? marker.Angle : tangent;
                if (start && marker.Orientation == SvgMarkerOrientation.AutoStartReverse) angle += 180;
                SvgDevicePoint anchor = new(marker.RefX, marker.RefY);
                if (source.SymbolViewBox is {} viewBox)
                {
                    (double sx, double sy, double ox, double oy) = SvgGeometrySyntax.MapViewBox(viewBox, marker.Width, marker.Height);
                    SvgDevicePoint mappedAnchor = SvgAffineTransform.FromScaleOffset(sx, sy, ox, oy).Apply(anchor.X, anchor.Y);
                    anchor = new((float)mappedAnchor.X, (float)mappedAnchor.Y);
                }
                SvgAffineTransform local = SvgAffineTransform.FromTranslation(x, y)
                    .ComposeChecked(SvgAffineTransform.FromRotationDegrees(angle));
                // Inverse stroke normalization can exceed the source coordinate
                // range before the enclosing map cancels it. Keep that computed
                // pose finite; ordinary reference placement validates the result.
                local = nonScalingUnits
                    ? local.Compose(SvgAffineTransform.FromScale(unitScale, unitScale))
                        .Compose(SvgAffineTransform.FromTranslation(-anchor.X, -anchor.Y))
                    : local.ComposeChecked(SvgAffineTransform.FromScale(unitScale, unitScale))
                        .ComposeChecked(SvgAffineTransform.FromTranslation(-anchor.X, -anchor.Y));
                // The marker's pose and content viewport are separate native matrices.
                // Keep the rounded pose separate from its source and output canvas maps.
                double offsetX = nonScalingUnits ? Math.Clamp(local.E, -float.MaxValue, float.MaxValue) : local.E;
                double offsetY = nonScalingUnits ? Math.Clamp(local.F, -float.MaxValue, float.MaxValue) : local.F;
                SvgAffineTransform pose = new((float)local.A, (float)local.B, (float)local.C, (float)local.D, (float)offsetX, (float)offsetY);
                SvgAffineTransform parentMap = context.ResolveTransform(reader);
                SvgAffineTransform placement = parentMap.ComposeNative(pose);
                SvgNativeCanvasStage drawMap = state.Scene.RetainNativeCanvasStage(new(SvgViewportFrame.MapGeometry(parentMap, frame), pose, SvgViewportFrame.MapGeometry(placement, frame)));
                SvgUseReference reference = new(id, 0, 0, marker.Width, marker.Height, placement, SvgAffineTransform.Identity,
                    255, SvgPresentationResolver.IsPainted(style, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible), null, null, null)
                { IsMarker = true, DrawMap = drawMap, HasInverseStrokePose = nonScalingUnits, IsDisplayed = context.InheritedPresentation.IsDisplayed && (style.GetDisplay() != SvgDisplay.None),
                    IsVisible = SvgPresentationResolver.ParseVisibility(style.Visibility, context.InheritedPresentation.IsVisible),
                    InheritedFill = SvgPaint.Default, RecordingScope = state.NewRecordingScope(), AllowsSolidPaintAnalysis = true };
                bool previousPicture = state.IsResourcePicture;
                SvgGeometry[] mapped;
                state.IsResourcePicture = true;
                try { mapped = SvgReferenceResolver.Apply(source, reference, frame, state, token); }
                finally { state.IsResourcePicture = previousPicture; }
                int transferred = 0;
                try
                {
                    for (; transferred < mapped.Length; transferred++)
                    {
                        token.ThrowIfCancellationRequested();
                        SvgGeometry geometry = mapped[transferred];
                        if (context.RetainInstanceStrokePath && !context.StrokeHostMap.IsAxisAligned
                            && geometry.Kind == SvgGeometryKind.Rectangle
                            && geometry.Rectangle is { Width: > 0, Height: > 0, Stroke: null })
                            mapped[transferred] = geometry = RetainRectangle(geometry, state, token);
                        if (geometry.RecordedPaint.Identity > 0)
                            state.Scene.ChangeRecordedPaint(geometry.RecordedPaint, state.Scene.GetRecordedPaintValue(geometry.RecordedPaint) with { IsMarkerContent = true, DrawMap = drawMap });
                        if (geometry.RecordedStrokePaint.Identity > 0)
                            state.Scene.ChangeRecordedPaint(geometry.RecordedStrokePaint, state.Scene.GetRecordedPaintValue(geometry.RecordedStrokePaint) with { IsMarkerContent = true, DrawMap = drawMap });
                        output.Add(geometry);
                    }
                }
                catch { for (int index = transferred; index < mapped.Length; index++) mapped[index].Dispose(); throw; }
            }
        }
        catch
        {
            foreach (SvgGeometry geometry in output.AsSpan()) geometry.Dispose();
            body.Discard();
            throw;
        }
        finally { body.Dispose(); }
    }
}
