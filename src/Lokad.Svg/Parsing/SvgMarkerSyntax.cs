using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgMarkerOrientation { Angle, Auto, AutoStartReverse }

/// <summary>Retains a marker's local viewport and vertex-placement parameters.</summary>
internal readonly record struct SvgMarkerDefinition(double Width, double Height, double RefX, double RefY,
    bool StrokeWidthUnits, SvgMarkerOrientation Orientation, double Angle)
{
    internal bool ClipsViewport { get; init; } = true;
}

/// <summary>Retains inherited marker declarations independently of fill and stroke paint.</summary>
internal readonly record struct SvgMarkerStyle(string? Start, string? Mid, string? End)
{
    internal bool HasMarkers => Active(Start) || Active(Mid) || Active(End);
    internal static bool Active(string? value) => value is not null && !value.Equals("none", StringComparison.OrdinalIgnoreCase);

    internal static SvgMarkerStyle Resolve(SvgComputedStyle style, SvgMarkerStyle inherited)
    {
        return new(Select(style.MarkerStart, inherited.Start), Select(style.MarkerMid, inherited.Mid), Select(style.MarkerEnd, inherited.End));
        static string? Select(string? value, string? parent)
        {
            string? input = value?.Trim();
            return input is null || input.Equals("inherit", StringComparison.OrdinalIgnoreCase) || input.Equals("unset", StringComparison.OrdinalIgnoreCase)
                ? parent : input.Equals("initial", StringComparison.OrdinalIgnoreCase) ? "none" : input;
        }
    }

    internal SvgMarkerStyle Retain(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        return new(Copy(Start), Copy(Mid), Copy(End));
        string? Copy(string? value)
        {
            if (value is null) return null;
            work.ConsumeText(value.Length, token);
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
        ReadOnlySpan<char> input = value.Trim();
        if (input.Equals("none", StringComparison.OrdinalIgnoreCase) || input.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            || input.Equals("unset", StringComparison.OrdinalIgnoreCase) || input.Equals("initial", StringComparison.OrdinalIgnoreCase)) return;
        if (!SvgLocalUrl.TryParseId(input.ToString(), out _))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only none or local marker references are implemented.");
    }

    internal static SvgMarkerDefinition Parse(XmlReader reader, SvgViewportLengthBases bases, ParserWorkCounter work, CancellationToken token)
    {
        SvgElementAttributePolicy.ValidateMarker(reader, work, token);
        string overflow = reader.GetAttribute("overflow") ?? "hidden";
        bool visibleOverflow = overflow.AsSpan().Trim().Equals("visible", StringComparison.OrdinalIgnoreCase);
        if (!visibleOverflow) SvgNestedViewportSyntax.ValidateOverflow(overflow);
        _ = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), work, token);
        double width = Coordinate(reader.GetAttribute("markerWidth"), bases.Width, 3), height = Coordinate(reader.GetAttribute("markerHeight"), bases.Height, 3);
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Marker dimensions cannot be negative.");
        string units = reader.GetAttribute("markerUnits") ?? "strokeWidth";
        if (units is not ("strokeWidth" or "userSpaceOnUse"))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only strokeWidth and userSpaceOnUse marker units are implemented.");
        string orient = reader.GetAttribute("orient") ?? "0";
        SvgMarkerOrientation orientation = orient == "auto" ? SvgMarkerOrientation.Auto
            : orient == "auto-start-reverse" ? SvgMarkerOrientation.AutoStartReverse : SvgMarkerOrientation.Angle;
        double angle = orientation == SvgMarkerOrientation.Angle ? Number(orient, 0) : 0;
        double refX = Coordinate(reader.GetAttribute("refX"), bases.Width, 0), refY = Coordinate(reader.GetAttribute("refY"), bases.Height, 0);
        return new(width, height, refX, refY, units == "strokeWidth", orientation, angle) { ClipsViewport = !visibleOverflow };

        static double Coordinate(string? value, double? basis, double fallback)
        {
            if (value is not null && value.EndsWith('%'))
            {
                if (basis is not {} extent) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Percentage marker coordinates require a known enclosing viewport.");
                return (float)(Number(value[..^1], 0) * (float)extent / 100);
            }
            return Number(value, fallback);
        }
        static double Number(string? value, double fallback)
        {
            if (value is null) return fallback;
            if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Marker values exceed the configured syntax limit.");
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                throw new SvgException(SvgErrorCode.MalformedInput, "Marker values must be finite numbers.");
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
            double angle = MathF.Atan2((float)line.Y2 - line.Y1, (float)line.X2 - line.X1) * (180f / MathF.PI);
            positions[0] = new(SvgMarkerVertexRole.Start, new(line.X1, line.Y1), angle);
            positions[1] = new(SvgMarkerVertexRole.End, new(line.X2, line.Y2), angle);
        }
        catch { body.Discard(); throw; }
        return Attach(body, positions, markers, reader, context, paint, frame, state);
    }

    internal static SvgParsedGeometry AttachPath(SvgParsedGeometry body, XmlReader reader, SvgPaintableElementContext context,
        SvgRecordedPaintStyle paint, SvgViewportFrame? frame, SvgDocumentState state, SvgMarkerStyle markers, int maximumCommands)
    {
        if (!markers.HasMarkers) return body;
        BudgetedParserList<SvgMarkerPosition> positions;
        try { positions = SvgMarkerVertices.ReadPath(reader, maximumCommands, state, context.WorkCounter, context.CancellationToken); }
        catch { body.Discard(); throw; }
        using (positions) return Attach(body, positions.AsSpan(), markers, reader, context, paint, frame, state);
    }

    private static SvgParsedGeometry Attach(SvgParsedGeometry body, ReadOnlySpan<SvgMarkerPosition> positions,
        SvgMarkerStyle markers, XmlReader reader, SvgPaintableElementContext context,
        SvgRecordedPaintStyle paint, SvgViewportFrame? frame, SvgDocumentState state)
    {
        ParserWorkCounter work = context.WorkCounter;
        CancellationToken token = context.CancellationToken;
        using BudgetedParserList<SvgGeometry> output = new(state.Budget, SvgMemoryCategory.Geometry, work, token);
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
                    work.Consume(token);
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
                    work.Consume(token);
                    SvgPaintBounds bounds;
                    if (geometry.RecordedPaint.Identity > 0 && state.Scene.GetRecordedPaintValue(geometry.RecordedPaint).VisualBounds is {} visual)
                        bounds = visual;
                    else
                    {
                        SvgAuthoredPath? path = geometry.AuthoredPath?.StrokeOutline ?? geometry.AuthoredPath;
                        SvgRectangle box = path is null ? SvgMaskSyntax.BoundingBox(geometry) : SvgPathBounds.Compute(path.Commands, work, token);
                        bounds = new((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height));
                        if (path is not null) bounds = bounds.Map(path.SceneMap, work, token);
                    }
                    left = MathF.Min(left, bounds.Left); top = MathF.Min(top, bounds.Top);
                    right = MathF.Max(right, bounds.Right); bottom = MathF.Max(bottom, bounds.Bottom);
                }
                layerRegion = new(MathF.Floor(left), MathF.Floor(top), MathF.Ceiling(right) - MathF.Floor(left), MathF.Ceiling(bottom) - MathF.Floor(top))
                    { Antialiasing = SvgClipAntialiasing.None };
            }
            SvgPaintRecording.WrapInstance(output.AsMutableSpan(), paint.ElementOpacity, state.NewRecordingScope(), layerRegion, state, work, token);
            return new(output.Detach(), body.ElementCount);

            void AddBody(bool strokeOperation)
            {
                for (int index = 0; index < body.Geometries.Length; index++)
                {
                    work.Consume(token);
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
                work.Consume(token);
                string id = SvgGradientSyntax.ParsePaintId(value);
                if (!state.Definitions.TryGetValue(id, out SvgDefinition source) || source.Marker is not {} marker)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local marker references are implemented.");
                if (marker.Width == 0 || marker.Height == 0 || marker.StrokeWidthUnits && width == 0) return;
                double unitScale = marker.StrokeWidthUnits ? width : 1;
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
                    .ComposeChecked(SvgAffineTransform.FromRotationDegrees(angle))
                    .ComposeChecked(SvgAffineTransform.FromScale(unitScale, unitScale))
                    .ComposeChecked(SvgAffineTransform.FromTranslation(-anchor.X, -anchor.Y));
                // The marker's pose and content viewport are separate native matrices.
                // Round the pose before composing the enclosing source transform.
                SvgAffineTransform placement = context.ResolveTransform(reader).ComposeChecked(new(
                    (float)local.A, (float)local.B, (float)local.C, (float)local.D, (float)local.E, (float)local.F));
                SvgUseReference reference = new(id, 0, 0, marker.Width, marker.Height, placement, SvgAffineTransform.Identity,
                    255, context.ResolvePresentation(style, true).IsPainted, null, null, null)
                { IsMarker = true, IsDisplayed = context.InheritedPresentation.IsDisplayed && SvgPresentationResolver.ParseDisplay(style.Display),
                    IsVisible = SvgPresentationResolver.ParseVisibility(style.Visibility, context.InheritedPresentation.IsVisible),
                    InheritedFill = SvgPaint.Default, RecordingScope = state.NewRecordingScope(), AllowsSolidPaintAnalysis = true };
                SvgGeometry[] mapped = SvgReferenceResolver.Apply(source, reference, frame, state, work, token);
                int transferred = 0;
                try { for (; transferred < mapped.Length; transferred++) output.Add(mapped[transferred]); }
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
