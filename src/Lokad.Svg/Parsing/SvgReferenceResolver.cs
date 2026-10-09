using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Represents a parsed direct local use reference before its target is resolved.</summary>
internal readonly record struct SvgUseReference(string Id, double X, double Y, double? Width, double? Height, SvgAffineTransform Transform, SvgAffineTransform InheritedTransform, byte Opacity, bool IsPainted, SvgPaint? FillOverride, string? PaintServerId, SvgUseStrokeOverride? StrokeOverride)
{
    /// <summary>Records the absence of an instance opacity layer, using the validated source value rather than its rounded alpha byte.</summary>
    internal bool AllowsSolidPaintAnalysis { get; init; }
    internal SvgRecordingScope RecordingScope { get; init; }
    internal SvgPaintOpacity SourceOpacity { get; init; } = SvgPaintOpacity.Opaque;
    internal SvgPaint InheritedFill { get; init; } = SvgPaint.Default;
    internal SvgPaintOpacity InheritedStrokeOpacity { get; init; } = SvgPaintOpacity.Opaque;
    internal SvgInheritedStrokeStyle InheritedStroke { get; init; }
    internal string? InheritedFillRule { get; init; }
    internal string? SourceMaskReference { get; init; }
    internal string? PaintServerFill { get; init; }
    internal bool IsDisplayed { get; init; } = true;
    internal bool IsVisible { get; init; } = true;
}

/// <summary>Retains one clip-path reference whose box needs a forward definition or clip-path definition.</summary>
internal readonly record struct SvgDeferredClip(string ClipId, SvgViewportFrame? Parent, int Depth);

/// <summary>Records the placeholders that must be replaced when a forward local use reference resolves.</summary>
internal readonly record struct PendingUse(SvgSceneSlot Slot, SvgUseReference Reference, SvgViewportFrame? Frame, byte? Mask, string? MaskReference, int MaskScopeStart, int MaskScopeCount, SvgDeferredClip? DeferredClip);

/// <summary>Applies typed local references through their geometry-specific supported transforms.</summary>
internal static class SvgReferenceResolver
{
    /// <summary>Resolves an already parsed local reference immediately or retains its typed placeholder for document-end resolution.</summary>

    internal static void ResolveOrDefer(SvgUseReference reference, BudgetedParserDictionary<string, SvgDefinition> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, SvgViewportFrame? frame, byte? mask, string? maskReference, SvgDeferredClip? deferredClip, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        if (deferredClip is null && (maskReference is null || mask is not null) && definitions.TryGetValue(reference.Id, out SvgDefinition definition) && definition.SymbolContent is null)
        {
            SvgGeometry[] applied = ApplyInstanceMask(definition, reference, frame, mask, maskReference, state, workCounter, cancellationToken);
            scene.AddGeometries(applied);
        }
        else
        {
            int scopeStart = SnapshotMaskScopes(state, workCounter, cancellationToken);
            pendingUses.Add(new PendingUse(scene.AddReferencePlaceholder(), reference, frame, mask, maskReference, scopeStart, state.MaskScopeSnapshots.Count - scopeStart, deferredClip));
        }
    }

    /// <summary>Maps definition geometries into instance space, charging one work unit per mapped polygon point inside the mapping loops and observing cancellation. Mapped point arrays are budget-reserved per mapper and released on failure.</summary>
    internal static SvgGeometry[] Apply(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workCounter);
        if (definition.StrokeContent is {} strokeContent && (reference.InheritedStroke != default || strokeContent.Content.Nodes[0].Style.Stroke != default || reference.PaintServerId is not null || !reference.Transform.IsAxisAligned))
            return SvgSymbolContentRenderer.Apply(definition with { SymbolContent = strokeContent }, reference, frame, state, workCounter, cancellationToken);
        if (definition.SymbolContent is not null) return SvgSymbolContentRenderer.Apply(definition, reference, frame, state, workCounter, cancellationToken);
        if (definition.LeafSource is SvgDefinitionLeafSource source)
        {
            if (reference.PaintServerId is not null && (definition.SourceFillBindings ?? definition.SymbolFillBindings) is {} authoredBindings)
            {
                bool inheritsFill = false;
                foreach (SvgSymbolFillBinding binding in authoredBindings) { workCounter.Consume(cancellationToken); inheritsFill |= binding.IsInherited; }
                if (!inheritsFill) reference = reference with { PaintServerId = null, PaintServerFill = null };
            }
            // An instanced path is one native draw. Replaying generated integer
            // bands as separate rectangles changes fractional AA and paint order.
            bool retainMappedContour = source.LocalName == "path" && definition.Geometries.Length > 0
                && (definition.Geometries[0].Kind is SvgGeometryKind.OrthogonalBand or SvgGeometryKind.Rectangle
                    || definition.Geometries[0].AuthoredPath is { StrokeStyle: null })
                || !reference.Transform.IsAxisAligned && source.LocalName is "path" or "rect" or "polygon" or "polyline";
            if (retainMappedContour || source.RequiresConstruction(reference.InheritedStroke, reference.FillOverride ?? reference.InheritedFill, reference.InheritedFillRule, reference.PaintServerFill))
            {
                foreach (SvgGeometry geometry in definition.Geometries)
                {
                    workCounter.Consume(cancellationToken);
                    if (geometry.LeafMask is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Instance stroke construction on masked definitions is not implemented.");
                }
                SvgDefinition constructed = source.Lower(reference.InheritedStroke, reference.FillOverride ?? reference.InheritedFill, reference.InheritedFillRule, reference.PaintServerFill, retainMappedContour, state);
                try { return Apply(constructed with { SymbolViewBox = definition.SymbolViewBox }, reference with { StrokeOverride = null, PaintServerId = null, PaintServerFill = null }, frame, state, workCounter, cancellationToken); }
                finally { foreach (SvgGeometry geometry in constructed.Geometries) geometry.Dispose(); }
            }
            reference = reference with { StrokeOverride = null };
        }
        if (definition.SymbolViewBox is SvgViewBox symbolBox) return ApplySymbol(definition, symbolBox, reference, frame, state, workCounter, cancellationToken);
        definition = definition with { SymbolFillBindings = definition.SymbolFillBindings ?? definition.SourceFillBindings };
        if (reference.StrokeOverride is not null && reference.PaintServerId is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroked paint-server use instances are not implemented.");
        if (reference.StrokeOverride is { Join: SvgStrokeJoin.Round } && (definition.InstanceStrokeMiterLimit is null || definition.Geometries.Length != 1 || definition.Geometries[0].Kind != SvgGeometryKind.PolygonFill))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Round-joined use strokes require an unstyled path definition.");
        if (reference.PaintServerId is not null)
        {
            SvgGeometry lowered = LowerDirectInstancePaintFill(definition, reference, frame, state);
            try
            {
                using SvgRecordingMapper mapper = SvgRecordingMapper.Create(state, reference.RecordingScope, workCounter, cancellationToken);
                return [TryApplyShaderOverride(lowered, definition.Geometries[0].RecordedPaint, mapper, state, workCounter, cancellationToken)];
            }
            catch { lowered.Dispose(); throw; }
        }
        RequireUniformOutlineScales(definition, reference);
        RequireAdmittedInstanceMagnification(definition, reference);
        using InstanceFillGeometries instanceFill = ApplyInstanceFill(definition, reference, state, workCounter, cancellationToken);
        SvgGeometry[] sources = instanceFill.Geometries;
        using BudgetedParserList<SvgGeometry> applied = new(state.Budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        SvgLeafMaskMapper? maskMapper = null;
        try
        {
            using BudgetedParserDictionary<SvgRecordedPaint, SvgRecordedPaint> mappedPaints = new(state.Budget, SvgMemoryCategory.Parser, 96);
            using SvgRecordingMapper recordingMapper = SvgRecordingMapper.Create(state, reference.RecordingScope, workCounter, cancellationToken);
            for (int index = 0; index < sources.Length; index++)
            {
                int firstApplied = applied.Count;
                SvgGeometry authoredSource = sources[index] with { AuthoredPath = definition.Geometries[index].AuthoredPath };
                switch (sources[index].Kind)
                {
                    case SvgGeometryKind.Rectangle:
                        AddInstanceGeometry(applied, ClipStrokedInstance(ApplyRectangle(sources[index].Rectangle, reference), frame) with { SolidPaint = reference.AllowsSolidPaintAnalysis ? sources[index].SolidPaint : null });
                        break;
                    case SvgGeometryKind.OrthogonalBand:
                        AddInstanceGeometry(applied, SvgGeometry.FromRectangle(ClipRectangle(ApplyRectangle(sources[index].Rectangle, reference), frame)));
                        break;
                    case SvgGeometryKind.AxisAlignedLine:
                        AddInstanceGeometry(applied, ClipLine(ApplyAxisAlignedLine(sources[index].AxisAlignedLine, reference), frame));
                        break;
                    case SvgGeometryKind.PolygonFill:
                        AddInstanceGeometry(applied, ApplyPolygon(authoredSource, reference, frame, state, workCounter, cancellationToken));
                        if (reference.StrokeOverride is not null)
                        {
                            if (definition.InstanceStrokeMiterLimit is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroke styles on use elements require an unstyled single-contour definition.");
                            AddInstanceGeometries(applied, ApplyInstanceStrokeOutlines(applied[^1], definition, reference, reference.StrokeOverride.Value, frame, state, workCounter, cancellationToken));
                        }
                        break;
                    case SvgGeometryKind.GradientPolygon:
                        AddInstanceGeometry(applied, ApplyGradientPolygon(authoredSource, reference, frame, state, workCounter, cancellationToken));
                        break;
                    case SvgGeometryKind.RadialPolygon:
                        AddInstanceGeometry(applied, ApplyRadialPolygon(authoredSource, reference, frame, state, workCounter, cancellationToken));
                        break;
                    case SvgGeometryKind.PatternPolygon:
                        AddInstanceGeometry(applied, ApplyPatternPolygon(authoredSource, reference, frame, state, workCounter, cancellationToken));
                        break;
                    case SvgGeometryKind.GradientFill:
                        AddInstanceGeometry(applied, LowerEmbeddedInstanceGradientFill(sources[index].Gradient, reference, frame));
                        break;
                    case SvgGeometryKind.RadialGradient:
                        AddInstanceGeometry(applied, LowerEmbeddedInstanceRadialFill(sources[index].Radial, reference, frame, state.Budget));
                        break;
                    case SvgGeometryKind.PatternFill:
                        AddInstanceGeometry(applied, LowerEmbeddedInstancePatternFill(sources[index].Pattern, reference, frame));
                        break;
                    case SvgGeometryKind.GradientStroke:
                        AddInstanceGeometry(applied, LowerEmbeddedInstanceGradientStroke(sources[index].GradientStroke, reference, frame));
                        break;
                    default: throw new SvgException(SvgErrorCode.UnsupportedFeature, "The referenced SVG geometry is not implemented.");
                }
                SvgUseReference paintReference = reference;
                if (definition.SymbolFillBindings is {} symbolBindings)
                {
                    SvgSymbolFillBinding fillBinding = symbolBindings[index];
                    SvgPaint fill = reference.FillOverride ?? reference.InheritedFill;
                    paintReference = reference with { FillOverride = fillBinding.ResolvesFill ? fillBinding.Resolve(fill, reference.InheritedStrokeOpacity) : null };
                }
                SvgRecordedPaint fillPaint = MapRecordedPaint(definition.Geometries[index].RecordedPaint, paintReference, state, mappedPaints, recordingMapper, workCounter, cancellationToken, false, definition.SymbolFillBindings?[index] is { IsStroke: true, IsStrokePaintServer: true } shaderStroke ? shaderStroke.StrokeOpacity ?? reference.InheritedStrokeOpacity : definition.SymbolFillBindings?[index] is { IsPaintServer: true } shaderFill ? shaderFill.FillOpacity ?? reference.InheritedFill.FillOpacity : null, PlainInstancePlacement(reference, frame));
                SvgSymbolFillBinding? strokeBinding = definition.SymbolFillBindings is {} paintBindings ? paintBindings[index] : null;
                SvgUseReference strokeReference = strokeBinding is { AuthoredStroke: not null } solidStroke ? reference with { FillOverride = solidStroke.ResolveStroke(reference.FillOverride ?? reference.InheritedFill, reference.InheritedStrokeOpacity) } : reference;
                SvgRecordedPaint strokePaint = MapRecordedPaint(definition.Geometries[index].RecordedStrokePaint, strokeReference, state, mappedPaints, recordingMapper, workCounter, cancellationToken, strokeBinding?.AuthoredStroke is null, strokeBinding is { IsStrokePaintServer: true } shaderBinding ? shaderBinding.StrokeOpacity ?? reference.InheritedStrokeOpacity : null, PlainInstancePlacement(reference, frame));
                SvgRecordedPaint addedStroke = default;
                if (reference.StrokeOverride is SvgUseStrokeOverride instanceStroke && applied.Count > firstApplied + 1)
                {
                    SvgRecordedPaintValue original = state.Scene.GetRecordedPaintValue(definition.Geometries[index].RecordedPaint);
                    float alpha = SvgPaintResolver.ResolveSolidPaint(instanceStroke.Paint).Alpha;
                    addedStroke = state.Scene.RegisterRecordedPaint(new(state.NewPaintOperationIdentity(), alpha, true,
                        recordingMapper.Map(original.Scope), SvgPaintOpacity.Opaque, recordingMapper.Map(original.OpacityScope)) { IsStroke = true });
                }
                for (int mapped = firstApplied; mapped < applied.Count; mapped++)
                    applied[mapped] = applied[mapped] with { RecordedPaint = mapped > firstApplied && addedStroke.Identity != 0 ? addedStroke : fillPaint, RecordedStrokePaint = strokePaint };
                if (definition.Geometries[index].LeafMask is SvgLeafMaskBinding binding)
                {
                    maskMapper ??= SvgLeafMaskMapper.Create(state, PlainInstancePlacement(reference, frame), workCounter, cancellationToken).WithRecordingMapper(recordingMapper);
                    SvgLeafMaskBinding? copied = maskMapper.Map(binding);
                    for (int mapped = firstApplied; mapped < applied.Count; mapped++)
                    {
                        workCounter.Consume(cancellationToken);
                        applied[mapped] = applied[mapped] with { LeafMask = copied };
                    }
                }
            }

            return RetainInstanceBatch(applied.AsSpan(), state, workCounter, cancellationToken);
        }
        catch { foreach (SvgGeometry geometry in applied.AsSpan()) geometry.Dispose(); throw; }
        finally
        {
            maskMapper?.Dispose();
        }

        static SvgGeometry[] ApplyInstanceStrokeOutlines(SvgGeometry placedGeometry, SvgDefinition definition, SvgUseReference reference, SvgUseStrokeOverride stroke, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(workCounter);
            double miterLimit = stroke.MiterLimit ?? definition.InstanceStrokeMiterLimit ?? throw new InvalidOperationException("Instance stroke expansion requires a retained definition miter limit.");
            if (!reference.Transform.TryGetUniformScale(out double placementScale)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled use strokes are implemented.");
            double frameScaleX = frame?.ScaleX ?? 1;
            double frameScaleY = frame?.ScaleY ?? 1;
            if (frame is not null && Math.Abs(frameScaleX) != Math.Abs(frameScaleY)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled use strokes are implemented.");
            double deviceWidth = stroke.Width * Math.Abs(placementScale) * Math.Abs(frameScaleX);
            if (Math.Abs(deviceWidth - Math.Round(deviceWidth)) <= 1e-9) deviceWidth = Math.Round(deviceWidth);
            if (!double.IsFinite(deviceWidth) || deviceWidth <= 0 || deviceWidth > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the admitted numeric range.");
            SvgPolygonFill placed = placedGeometry.Polygon;
            if (placedGeometry.AuthoredPath is SvgAuthoredPath retained)
            {
                // Stroke the authored centerline, including contacts and curves,
                // rather than a fill boundary changed by contour separation.
                using RenderBudget.BudgetReservation owner = state.Budget.Reserve(128, SvgMemoryCategory.Geometry);
                using BudgetedParserList<SvgPoint> noPoints = new(state.Budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
                using BudgetedParserList<int> noEnds = new(state.Budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
                using SvgOrthogonalGeometryLowering lowering = new(noPoints, noEnds, "path", true, false,
                    int.MaxValue, state.Budget, workCounter, cancellationToken);
                using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Scene, workCounter, cancellationToken);
                foreach (SvgAuthoredPathCommand command in retained.Commands) commands.Add(command);
                SvgColor nativeColor = SvgPaintResolver.ResolveFillColor(stroke.Paint, 255);
                nativeColor = new(nativeColor.Red, nativeColor.Green, nativeColor.Blue, MultiplyAlpha(nativeColor.Alpha, reference.Opacity));
                SvgViewportFrame? clipFrame = placed.Clip is SvgClipBox clip ? new(0, 0, 1, 1, null, null, clip, 0) : null;
                using SvgParsedGeometry native = lowering.LowerNativeStrokeCommands(commands, retained.SceneMap,
                    deviceWidth, deviceWidth, miterLimit, SvgStrokeCap.Butt, stroke.Join, null, 0, null,
                    true, nativeColor, reference.IsPainted, state.OutputMagnification, clipFrame, state.Budget);
                try { return native.Geometries.ToArray(); }
                catch { native.Discard(); throw; }
            }
            long wantedPoints = 6L * placed.Points.Length + 8;
            if (stroke.Join == SvgStrokeJoin.Round)
            {
                int fanSegments = SvgStrokeOutline.MaxArcFanSegments(deviceWidth / 2, Math.PI);
                try { wantedPoints = checked(4L * placed.Points.Length * (fanSegments + 1) + 32); }
                catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
            }
            int pointCapacity = wantedPoints > SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints ? SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints : (int)wantedPoints;
            long wantedContours;
            try { wantedContours = checked(2L * placed.ContourEnds.Length + 2); }
            catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
            int contourCapacity = wantedContours > SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints ? SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints : (int)wantedContours;
            long scratchBytes;
            try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
            catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
            using RenderBudget.BudgetReservation scratch = state.Budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
            SvgContourBuilder outlines = new(pointCapacity, contourCapacity);
            for (int contour = 0; contour < placed.ContourEnds.Length; contour++)
            {
                if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                int start = contour == 0 ? 0 : placed.ContourEnds[contour - 1];
                SvgStrokeOutline.StrokeClosedPolyline(placed.Points.AsSpan(start, placed.ContourEnds[contour] - start), deviceWidth, miterLimit, stroke.Join, outlines, cancellationToken);
            }
            using BudgetedParserList<SvgDevicePoint> mapped = new(state.Budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<int> ends = new(state.Budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            for (int contour = 0; contour < outlines.ContourCount; contour++)
            {
                foreach (SvgDevicePoint vertex in outlines.GetContour(contour))
                {
                    workCounter.Consume(cancellationToken);
                    mapped.Add(vertex);
                }

                workCounter.Consume(cancellationToken);
                ends.Add(mapped.Count);
            }
            SvgOrthogonalGeometryLowering.ChargePairs(workCounter, (long)ends.Count * (ends.Count - 1) / 2, cancellationToken);
            SvgDevicePoint[] retainedOutlines = mapped.AsSpan().ToArray();
            int[] outlineEnds = ends.AsSpan().ToArray();
            SvgContourNesting.MergeIdenticalContours(retainedOutlines, outlineEnds, workCounter, out SvgDevicePoint[] mergedOutlines, out int[] mergedEnds, cancellationToken);
            int[] roles = new int[mergedEnds.Length];
            SvgOrthogonalGeometryLowering.ChargePairs(workCounter, (long)mergedEnds.Length * (mergedEnds.Length - 1) / 2, cancellationToken);
            SvgContourNesting.ComputeWindingRoles(mergedOutlines, mergedEnds, roles, false, cancellationToken);
            for (int contour = 0; contour < mergedEnds.Length; contour++)
            {
                if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                int start = contour == 0 ? 0 : mergedEnds[contour - 1];
                if (roles[contour] == 0 && mergedEnds[contour] - start >= 3) roles[contour] = 1;
            }
            SvgColor color = SvgPaintResolver.ResolveFillColor(stroke.Paint, 255);
            color = new SvgColor(color.Red, color.Green, color.Blue, MultiplyAlpha(color.Alpha, reference.Opacity));
            return [SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(mergedOutlines, mergedEnds, roles, color, reference.IsPainted, false, placed.Mapping, placed.Clip, null, state.Budget))];
        }

        static SvgGeometry LowerEmbeddedInstancePatternFill(SvgPatternGeometry band, SvgUseReference instance, SvgViewportFrame? viewportFrame)
        {
            SvgPatternTile tile = new(SvgGeometry.FromRectangle(band.Content) with { SolidPaint = band.ContentPaint }, band.OriginX, band.OriginY, band.TileWidth, band.TileHeight, SvgAffineTransform.Identity) { VectorContent = band.VectorContent };
            SvgRectangle placement = InstancePlacement(band.Fill, instance);
            SvgUseReference composed = instance with { Opacity = MultiplyAlpha(band.Opacity, instance.Opacity), IsPainted = band.Fill.HasFill && instance.IsPainted };
            return LowerInstancePatternFill(tile, placement, composed, viewportFrame, MapInstanceSolidStroke(band.Fill, band.Stroke, instance, viewportFrame), band.ShaderPlacement);
        }

        static SvgGeometry ClipStrokedInstance(SvgRectangle rectangle, SvgViewportFrame? frame)
        {
            if (frame is null) return SvgGeometry.FromRectangle(rectangle);
            if (rectangle.Stroke is SvgStroke stroke)
            {
                SvgRectangle shifted = frame.Value.Shift(rectangle);
                double halfWidth = stroke.Width / 2d;
                if (!frame.Value.Clip.Contains(shifted.X - halfWidth, shifted.Y - halfWidth, shifted.X + shifted.Width + halfWidth, shifted.Y + shifted.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            }
            return SvgGeometry.FromRectangle(frame.Value.ClipShifted(rectangle));
        }

        static SvgGeometry LowerEmbeddedInstanceGradientFill(SvgGradientGeometry band, SvgUseReference instance, SvgViewportFrame? viewportFrame)
        {
            SvgRectangle placement = InstancePlacement(band.Fill, instance);
            SvgRectangle box = InstanceBox(band.Box, instance, viewportFrame);
            SvgUseReference composed = instance with { Opacity = MultiplyAlpha(band.Opacity, instance.Opacity), IsPainted = band.Fill.HasFill && instance.IsPainted };
            return LowerInstanceGradientFill(band.Gradient, placement, box, composed, viewportFrame, MapInstanceSolidStroke(band.Fill, band.Stroke, instance, viewportFrame));
        }

        static SvgGeometry LowerEmbeddedInstanceRadialFill(SvgRadialGeometry radial, SvgUseReference instance, SvgViewportFrame? viewportFrame, RenderBudget budget)
        {
            SvgRectangle placement = InstancePlacement(radial.Fill, instance);
            SvgRectangle box = InstanceBox(radial.Box, instance, viewportFrame);
            SvgRadialGradient circle = radial.Gradient;
            if (circle.IsUserSpace) circle = circle.HasGeneralTransform ? SvgGradientSyntax.ShiftGeneralRadial(circle, instance.X, instance.Y) : circle with { CenterX = circle.CenterX + instance.X, CenterY = circle.CenterY + instance.Y, FocalX = circle.FocalX + instance.X, FocalY = circle.FocalY + instance.Y };
            SvgUseReference composed = instance with { Opacity = MultiplyAlpha(radial.Opacity, instance.Opacity), IsPainted = radial.Fill.HasFill && instance.IsPainted };
            return LowerPlacedRadialFill(circle, placement, box, composed.Opacity, composed.IsPainted, instance.Transform, viewportFrame, budget, MapInstanceSolidStroke(radial.Fill, radial.Stroke, instance, viewportFrame));
        }

        static SvgGeometry LowerEmbeddedInstanceGradientStroke(SvgGradientStrokeGeometry stroke, SvgUseReference instance, SvgViewportFrame? viewportFrame)
        {
            SvgLinearGradient vector = stroke.Gradient;
            if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 + instance.X, Y1 = vector.Y1 + instance.Y, X2 = vector.X2 + instance.X, Y2 = vector.Y2 + instance.Y };
            vector = SvgGradientSyntax.MapUserSpaceVector(instance.Transform, vector, viewportFrame);
            vector = SvgGradientSyntax.MirrorBoundingBoxVector(instance.Transform, vector);
            SvgRectangle fill = InstancePlacement(stroke.Fill, instance);
            SvgRectangle top = InstancePlacement(stroke.Top, instance);
            SvgRectangle bottom = InstancePlacement(stroke.Bottom, instance);
            SvgRectangle left = InstancePlacement(stroke.Left, instance);
            SvgRectangle right = InstancePlacement(stroke.Right, instance);
            SvgRectangle box = InstancePlacement(stroke.Box, instance);
            if (viewportFrame is not null)
            {
                SvgRectangle extents = InstancePlacement(stroke.OuterBox(), instance);
                if (!viewportFrame.Value.Clip.Contains(extents.X, extents.Y, extents.X + extents.Width, extents.Y + extents.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
                fill = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(fill));
                top = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(top));
                bottom = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(bottom));
                left = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(left));
                right = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(right));
                box = viewportFrame.Value.Shift(box);
            }
            byte opacity = MultiplyAlpha(stroke.Opacity, instance.Opacity);
            bool painted = stroke.Fill.HasFill && instance.IsPainted;
            SvgColor baseColor = stroke.Fill.Color;
            SvgRectangle finished = fill with { Color = new SvgColor(baseColor.Red, baseColor.Green, baseColor.Blue, MultiplyAlpha(baseColor.Alpha, instance.Opacity)), HasFill = painted };
            return SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(finished, top, bottom, left, right, vector, box, opacity, stroke.HasStroke && instance.IsPainted));
        }
    }

    internal static SvgAffineTransform PlainInstancePlacement(SvgUseReference reference, SvgViewportFrame? frame)
    {
        SvgAffineTransform placement = reference.Transform.ComposeChecked(SvgAffineTransform.FromScaleOffset(1, 1, reference.X, reference.Y));
        return frame is {} viewport ? SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(placement) : placement;
    }

    internal static SvgAffineTransform SymbolInstancePlacement(SvgViewBox box, SvgUseReference reference, SvgViewportFrame? frame)
    {
        if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol placement requires known instance dimensions.");
        (double sx, double sy, double ox, double oy) = SvgGeometrySyntax.MapViewBox(box, reference.Width.Value, reference.Height.Value);
        SvgAffineTransform placement = reference.Transform.ComposeChecked(SvgAffineTransform.FromScaleOffset(sx, sy, ox + reference.X, oy + reference.Y));
        return frame is {} viewport ? SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(placement) : placement;
    }

    private static SvgGeometry ApplyPolygon(SvgGeometry source, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgPolygonFill polygon = source.Polygon;
        ArgumentNullException.ThrowIfNull(state);
        SvgAffineTransform instanceMap = reference.Transform;
        using RenderBudget.BudgetReservation? mappedReservation = polygon.Points.Length == 0 ? null : state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(polygon.Points.Length) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(polygon.QuadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(polygon.CubicSpans.Length), SvgMemoryCategory.Geometry);
        SvgDevicePoint[] mapped = new SvgDevicePoint[polygon.Points.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgDevicePoint placed = instanceMap.Apply(polygon.Points[index].X + reference.X, polygon.Points[index].Y + reference.Y);
            if (frame is not null)
            {
                double shiftedX = placed.X * frame.Value.ScaleX + frame.Value.OffsetX;
                double shiftedY = placed.Y * frame.Value.ScaleY + frame.Value.OffsetY;
                if (!double.IsFinite(shiftedX) || !double.IsFinite(shiftedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                placed = new SvgDevicePoint(shiftedX, shiftedY);
            }
            mapped[index] = placed;
        }
        SvgClipBox? clip = polygon.Clip;
        if (frame is not null) clip = clip is null ? frame.Value.Clip : clip.Value.Intersect(frame.Value.Clip);
        SvgColor color = new(polygon.Color.Red, polygon.Color.Green, polygon.Color.Blue, MultiplyAlpha(polygon.Color.Alpha, reference.Opacity));
        SvgQuadSpan[] quadSpans = MapUseQuadSpans(polygon.QuadSpans, instanceMap, reference.X, reference.Y, frame, workCounter, cancellationToken);
        SvgCubicSpan[] cubicSpans = MapUseCubicSpans(polygon.CubicSpans, instanceMap, reference.X, reference.Y, frame, workCounter, cancellationToken);
        SvgGeometry result = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(mapped, polygon.ContourEnds, polygon.Signs, quadSpans, cubicSpans, color, polygon.HasFill && reference.IsPainted, polygon.IsEvenOdd, polygon.Mapping, clip, null, state.Budget));
        return source.CopyAuthoredPathTo(result, PlainInstancePlacement(reference, frame), state.Budget, workCounter, cancellationToken);
    }

    /// <summary>Maps retained quad-span controls through one plain use-instance placement, preserving span ranges so the analytic walker consumes instance curves like direct geometry.</summary>
    private static SvgQuadSpan[] MapUseQuadSpans(SvgQuadSpan[] spans, SvgAffineTransform instanceMap, double offsetX, double offsetY, SvgViewportFrame? frame, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(workCounter);
        SvgQuadSpan[] mapped = new SvgQuadSpan[spans.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgQuadSpan span = spans[index];
            SvgDevicePoint placed = instanceMap.Apply(span.Control.X + offsetX, span.Control.Y + offsetY);
            double x = placed.X;
            double y = placed.Y;
            if (frame is not null)
            {
                x = x * frame.Value.ScaleX + frame.Value.OffsetX;
                y = y * frame.Value.ScaleY + frame.Value.OffsetY;
            }
            if (!double.IsFinite(x) || !double.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
            mapped[index] = span with { Control = new SvgDevicePoint(x, y) };
        }
        return mapped;
    }

    /// <summary>Maps retained cubic-span controls through one plain use-instance placement, preserving span ranges so the analytic walker consumes instance curves like direct geometry.</summary>
    private static SvgCubicSpan[] MapUseCubicSpans(SvgCubicSpan[] spans, SvgAffineTransform instanceMap, double offsetX, double offsetY, SvgViewportFrame? frame, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(workCounter);
        SvgCubicSpan[] mapped = new SvgCubicSpan[spans.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgCubicSpan span = spans[index];
            SvgDevicePoint first = instanceMap.Apply(span.FirstControl.X + offsetX, span.FirstControl.Y + offsetY);
            SvgDevicePoint second = instanceMap.Apply(span.SecondControl.X + offsetX, span.SecondControl.Y + offsetY);
            double firstX = first.X;
            double firstY = first.Y;
            double secondX = second.X;
            double secondY = second.Y;
            if (frame is not null)
            {
                firstX = firstX * frame.Value.ScaleX + frame.Value.OffsetX;
                firstY = firstY * frame.Value.ScaleY + frame.Value.OffsetY;
                secondX = secondX * frame.Value.ScaleX + frame.Value.OffsetX;
                secondY = secondY * frame.Value.ScaleY + frame.Value.OffsetY;
            }
            if (!double.IsFinite(firstX) || !double.IsFinite(firstY) || !double.IsFinite(secondX) || !double.IsFinite(secondY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
            mapped[index] = new SvgCubicSpan(span.Contour, span.StartIndex, span.EndIndex, new SvgDevicePoint(firstX, firstY), new SvgDevicePoint(secondX, secondY));
        }
        return mapped;
    }

    private static SvgGeometry ApplyGradientPolygon(SvgGeometry source, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgGradientPolygon outline = source.GradientPolygon;
        ArgumentNullException.ThrowIfNull(state);
        SvgAffineTransform instanceMap = reference.Transform;
        using RenderBudget.BudgetReservation? mappedReservation = outline.Points.Length == 0 ? null : state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(outline.Points.Length) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(outline.QuadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(outline.CubicSpans.Length), SvgMemoryCategory.Geometry);
        SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgDevicePoint placed = instanceMap.Apply(outline.Points[index].X + reference.X, outline.Points[index].Y + reference.Y);
            if (frame is not null)
            {
                double shiftedX = placed.X * frame.Value.ScaleX + frame.Value.OffsetX;
                double shiftedY = placed.Y * frame.Value.ScaleY + frame.Value.OffsetY;
                if (!double.IsFinite(shiftedX) || !double.IsFinite(shiftedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                placed = new SvgDevicePoint(shiftedX, shiftedY);
            }
            mapped[index] = placed;
        }
        SvgLinearGradient vector = outline.Gradient;
        if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 + reference.X, Y1 = vector.Y1 + reference.Y, X2 = vector.X2 + reference.X, Y2 = vector.Y2 + reference.Y };
        SvgRectangle box = InstanceBox(outline.Box, reference, frame);
        vector = SvgGradientSyntax.MapUserSpaceVector(reference.Transform, vector, frame);
        vector = SvgGradientSyntax.MirrorBoundingBoxVector(reference.Transform, vector);
        SvgClipBox? clip = outline.Clip;
        if (frame is not null) clip = clip is null ? frame.Value.Clip : clip.Value.Intersect(frame.Value.Clip);
        byte opacity = MultiplyAlpha(outline.Opacity, reference.Opacity);
        SvgQuadSpan[] quadSpans = MapUseQuadSpans(outline.QuadSpans, instanceMap, reference.X, reference.Y, frame, workCounter, cancellationToken);
        SvgCubicSpan[] cubicSpans = MapUseCubicSpans(outline.CubicSpans, instanceMap, reference.X, reference.Y, frame, workCounter, cancellationToken);
        SvgGeometry result = SvgGeometry.FromGradientPolygon(SvgGradientPolygon.Create(mapped, outline.ContourEnds, outline.Signs, quadSpans, cubicSpans, vector, box, outline.HasFill && reference.IsPainted, outline.IsEvenOdd, opacity, outline.Mapping, clip, null, state.Budget));
        return source.CopyAuthoredPathTo(result, PlainInstancePlacement(reference, frame), state.Budget, workCounter, cancellationToken);
    }

    private static SvgGeometry ApplyRadialPolygon(SvgGeometry source, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgRadialPolygon outline = source.RadialPolygon;
        ArgumentNullException.ThrowIfNull(state);
        SvgAffineTransform instanceMap = reference.Transform;
        using RenderBudget.BudgetReservation? mappedReservation = outline.Points.Length == 0 ? null : state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(outline.Points.Length) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(outline.QuadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(outline.CubicSpans.Length), SvgMemoryCategory.Geometry);
        SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgDevicePoint placed = instanceMap.Apply(outline.Points[index].X + reference.X, outline.Points[index].Y + reference.Y);
            if (frame is not null)
            {
                double shiftedX = placed.X * frame.Value.ScaleX + frame.Value.OffsetX;
                double shiftedY = placed.Y * frame.Value.ScaleY + frame.Value.OffsetY;
                if (!double.IsFinite(shiftedX) || !double.IsFinite(shiftedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                placed = new SvgDevicePoint(shiftedX, shiftedY);
            }
            mapped[index] = placed;
        }
        SvgRadialGradient circle = outline.Gradient;
        if (circle.IsUserSpace) circle = circle.HasGeneralTransform ? SvgGradientSyntax.ShiftGeneralRadial(circle, reference.X, reference.Y) : circle with { CenterX = circle.CenterX + reference.X, CenterY = circle.CenterY + reference.Y, FocalX = circle.FocalX + reference.X, FocalY = circle.FocalY + reference.Y };
        SvgRectangle box = InstanceBox(outline.Box, reference, frame);
        circle = SvgGradientSyntax.MapUserSpaceRadial(reference.Transform, circle, frame);
        SvgClipBox? clip = outline.Clip;
        if (frame is not null) clip = clip is null ? frame.Value.Clip : clip.Value.Intersect(frame.Value.Clip);
        byte opacity = MultiplyAlpha(outline.Opacity, reference.Opacity);
        SvgQuadSpan[] quadSpans = MapUseQuadSpans(outline.QuadSpans, instanceMap, reference.X, reference.Y, frame, workCounter, cancellationToken);
        SvgCubicSpan[] cubicSpans = MapUseCubicSpans(outline.CubicSpans, instanceMap, reference.X, reference.Y, frame, workCounter, cancellationToken);
        SvgGeometry result = SvgGeometry.FromRadialPolygon(SvgRadialPolygon.Create(mapped, outline.ContourEnds, outline.Signs, quadSpans, cubicSpans, circle, box, outline.HasFill && reference.IsPainted, outline.IsEvenOdd, opacity, outline.Mapping, clip, null, state.Budget));
        return source.CopyAuthoredPathTo(result, PlainInstancePlacement(reference, frame), state.Budget, workCounter, cancellationToken);
    }

    private static SvgGeometry ApplyPatternPolygon(SvgGeometry source, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgPatternPolygon outline = source.PatternPolygon;
        ArgumentNullException.ThrowIfNull(state);
        SvgAffineTransform instanceMap = reference.Transform;
        using RenderBudget.BudgetReservation? mappedReservation = outline.Points.Length == 0 ? null : state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(outline.Points.Length) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(outline.QuadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(outline.CubicSpans.Length), SvgMemoryCategory.Geometry);
        SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgDevicePoint placed = instanceMap.Apply(outline.Points[index].X + reference.X, outline.Points[index].Y + reference.Y);
            if (frame is not null)
            {
                double shiftedX = placed.X * frame.Value.ScaleX + frame.Value.OffsetX;
                double shiftedY = placed.Y * frame.Value.ScaleY + frame.Value.OffsetY;
                if (!double.IsFinite(shiftedX) || !double.IsFinite(shiftedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                placed = new SvgDevicePoint(shiftedX, shiftedY);
            }
            mapped[index] = placed;
        }
        SvgClipBox? clip = outline.Clip;
        if (frame is not null) clip = clip is null ? frame.Value.Clip : clip.Value.Intersect(frame.Value.Clip);
        byte opacity = MultiplyAlpha(outline.Opacity, reference.Opacity);
        SvgQuadSpan[] quadSpans = MapUseQuadSpans(outline.QuadSpans, instanceMap, reference.X, reference.Y, frame, workCounter, cancellationToken);
        SvgCubicSpan[] cubicSpans = MapUseCubicSpans(outline.CubicSpans, instanceMap, reference.X, reference.Y, frame, workCounter, cancellationToken);
        SvgGeometry result = SvgGeometry.FromPatternPolygon(SvgPatternPolygon.Create(mapped, outline.ContourEnds, outline.Signs, quadSpans, cubicSpans, outline.Content, outline.ContentPaint, outline.OriginX, outline.OriginY, outline.TileWidth, outline.TileHeight, outline.HasFill && reference.IsPainted, outline.IsEvenOdd, opacity, outline.Mapping, clip, outline.VectorContent, outline.VectorContent is null ? SvgAffineTransform.Identity : PlainInstancePlacement(reference, frame).ComposeChecked(outline.ShaderPlacement), state.Budget));
        return source.CopyAuthoredPathTo(result, PlainInstancePlacement(reference, frame), state.Budget, workCounter, cancellationToken);
    }

    /// <summary>Rejects non-uniformly scaled outline strokes on use instances, whose pre-scale miters would silently distort.</summary>
    private static void RequireUniformOutlineScales(SvgDefinition definition, SvgUseReference reference)
    {
        if (reference.Transform.IsUniformScale()) return;
        foreach (SvgGeometry source in definition.Geometries)
        {
            if (source.Kind is SvgGeometryKind.GradientPolygon or SvgGeometryKind.RadialPolygon or SvgGeometryKind.PatternPolygon) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled outline strokes on use instances are implemented.");
        }
    }

    /// <summary>Rejects magnified symbol viewBox maps of tolerance-flattened curve outlines, whose lowering-time subdivision error would magnify past the admitted device tolerance.</summary>
    private static void RequireAdmittedSymbolMagnification(SvgGeometry geometry, SvgPolygonMapping mapping, double viewBoxScale, SvgUseReference instance)
    {
        if (mapping != SvgPolygonMapping.FlattenedCurves || geometry.AuthoredPath is not null) return;
        double magnification = viewBoxScale * Math.Max(ColumnLength(instance.Transform.A, instance.Transform.B), ColumnLength(instance.Transform.C, instance.Transform.D));
        if (magnification > 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only unmagnified symbol instances of flattened curve outlines are implemented.");
    }

    /// <summary>Rejects magnified use instances of tolerance-flattened curve outlines, whose lowering-time subdivision error would magnify past the admitted device tolerance.</summary>
    /// <remarks>Column lengths within one part per million of unity pass as authoring noise: 1e-6 times the device extent stays far below the 1/256 coverage quantum, while true magnification keeps the gate for scaled re-flattening.</remarks>
    /// <remarks>Straight outlines map exactly under any affine instance transform, and unit-or-smaller integer scales never magnify the quarter-pixel flattening budget; only FlattenedCurves content under a magnifying integer scale rejects.</remarks>
    private static void RequireAdmittedInstanceMagnification(SvgDefinition definition, SvgUseReference reference)
    {
        if (ColumnLength(reference.Transform.A, reference.Transform.B) <= 1.000001 && ColumnLength(reference.Transform.C, reference.Transform.D) <= 1.000001) return;
        foreach (SvgGeometry source in definition.Geometries)
        {
            SvgPolygonMapping? mapping = source.Kind switch
            {
                SvgGeometryKind.PolygonFill => source.Polygon.Mapping,
                SvgGeometryKind.GradientPolygon => source.GradientPolygon.Mapping,
                SvgGeometryKind.RadialPolygon => source.RadialPolygon.Mapping,
                SvgGeometryKind.PatternPolygon => source.PatternPolygon.Mapping,
                _ => null,
            };
            if (mapping == SvgPolygonMapping.FlattenedCurves && source.AuthoredPath is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only unmagnified use instances of flattened curve outlines are implemented.");
        }
    }



    private static SvgGeometry ApplySymbolInstance(SvgGeometry geometry, SvgViewBox symbolBox, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (geometry.Kind == SvgGeometryKind.PolygonFill) return ApplySymbolPolygon(geometry, symbolBox, reference, frame, state, workCounter, cancellationToken);
        bool isBand;
        SvgRectangle box;
        if (geometry.Kind == SvgGeometryKind.Rectangle)
        {
            if (geometry.Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
            box = geometry.Rectangle;
            isBand = false;
        }
        else if (geometry.Kind == SvgGeometryKind.OrthogonalBand)
        {
            box = geometry.Rectangle;
            isBand = true;
        }
        else if (geometry.Kind == SvgGeometryKind.AxisAlignedLine)
        {
            box = geometry.AxisAlignedLine.ToStrokeBand();
            isBand = true;
        }
        else throw new SvgException(SvgErrorCode.UnsupportedFeature, "The referenced SVG geometry is not implemented.");
        if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        SvgRectangle mapped = MapSymbolBox(box, symbolBox, reference.Width.Value, reference.Height.Value);
        SvgRectangle placed = TranslateRectangle(mapped, reference.X, reference.Y);
        SvgClipBox localViewport = new(reference.X, reference.Y, reference.Width.Value, reference.Height.Value);
        SvgRectangle transformed = reference.Transform.Apply(localViewport.ClipRectangle(placed));
        transformed = transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), HasFill = transformed.HasFill && reference.IsPainted };
        SvgClipBox viewport = reference.Transform.Apply(new SvgClipBox(reference.X, reference.Y, reference.Width.Value, reference.Height.Value));
        if (frame is not null)
        {
            transformed = frame.Value.Shift(transformed);
            viewport = new SvgClipBox(viewport.X + frame.Value.OffsetX, viewport.Y + frame.Value.OffsetY, viewport.Width, viewport.Height);
            viewport = viewport.Intersect(frame.Value.Clip);
        }
        SvgRectangle clipped = viewport.ClipRectangle(transformed);
        return isBand ? SvgGeometry.FromOrthogonalBand(clipped) : SvgGeometry.FromRectangle(clipped) with { SolidPaint = reference.AllowsSolidPaintAnalysis ? geometry.SolidPaint : null };


        static SvgGeometry ApplySymbolPolygon(SvgGeometry geometry, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            SvgPolygonFill source = geometry.Polygon;
            ArgumentNullException.ThrowIfNull(document);
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            if (scaleX != scaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform symbol viewBox scaling of polygon content is not implemented.");
            RequireAdmittedSymbolMagnification(geometry, source.Mapping, scaleX, instance);
            using RenderBudget.BudgetReservation mappedReservation = document.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(source.Points.Length), SvgMemoryCategory.Geometry);
            SvgDevicePoint[] mapped = new SvgDevicePoint[source.Points.Length];
            for (int index = 0; index < mapped.Length; index++)
            {
                workCounter.Consume(cancellationToken);
                double contentX = source.Points[index].X * scaleX + offsetX + instance.X;
                double contentY = source.Points[index].Y * scaleY + offsetY + instance.Y;
                SvgDevicePoint placedPoint = instance.Transform.Apply(contentX, contentY);
                double placedX = placedPoint.X;
                double placedY = placedPoint.Y;
                if (viewportFrame is not null)
                {
                    placedX = placedX * viewportFrame.Value.ScaleX + viewportFrame.Value.OffsetX;
                    placedY = placedY * viewportFrame.Value.ScaleY + viewportFrame.Value.OffsetY;
                }
                if (!double.IsFinite(placedX) || !double.IsFinite(placedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                mapped[index] = new SvgDevicePoint(placedX, placedY);
            }
            SvgClipBox viewport = instance.Transform.Apply(new SvgClipBox(instance.X, instance.Y, instance.Width.Value, instance.Height.Value));
            if (viewportFrame is not null)
            {
                viewport = new SvgClipBox(viewport.X + viewportFrame.Value.OffsetX, viewport.Y + viewportFrame.Value.OffsetY, viewport.Width, viewport.Height);
                viewport = viewport.Intersect(viewportFrame.Value.Clip);
            }
            SvgClipBox clip;
            if (source.Clip is null) clip = viewport;
            else
            {
                SvgClipBox content = source.Clip.Value;
                SvgClipBox shifted = new(content.X * scaleX + offsetX + instance.X, content.Y * scaleY + offsetY + instance.Y, content.Width * scaleX, content.Height * scaleY) { Antialiasing = content.Antialiasing };
                SvgClipBox placed = instance.Transform.Apply(shifted);
                if (viewportFrame is not null) placed = new SvgClipBox(placed.X + viewportFrame.Value.OffsetX, placed.Y + viewportFrame.Value.OffsetY, placed.Width, placed.Height) { Antialiasing = placed.Antialiasing };
                clip = placed.Intersect(viewport);
            }
            SvgColor color = new(source.Color.Red, source.Color.Green, source.Color.Blue, MultiplyAlpha(source.Color.Alpha, instance.Opacity));
            SvgQuadSpan[] quadSpans = MapSymbolQuadSpans(source.QuadSpans, scaleX, scaleY, offsetX, offsetY, instance, viewportFrame, workCounter, cancellationToken);
            SvgCubicSpan[] cubicSpans = MapSymbolCubicSpans(source.CubicSpans, scaleX, scaleY, offsetX, offsetY, instance, viewportFrame, workCounter, cancellationToken);
            SvgGeometry result = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(mapped, source.ContourEnds, source.Signs, quadSpans, cubicSpans, color, source.HasFill && instance.IsPainted, source.IsEvenOdd, source.Mapping, clip, null, document.Budget));
            SvgAffineTransform placement = PlainInstancePlacement(instance, viewportFrame).ComposeChecked(SvgAffineTransform.FromScaleOffset(scaleX, scaleY, offsetX, offsetY));
            return geometry.CopyAuthoredPathTo(result, placement, document.Budget, workCounter, cancellationToken);
        }

        static SvgQuadSpan[] MapSymbolQuadSpans(SvgQuadSpan[] spans, double scaleX, double scaleY, double offsetX, double offsetY, SvgUseReference instance, SvgViewportFrame? viewportFrame, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            SvgQuadSpan[] mapped = new SvgQuadSpan[spans.Length];
            for (int index = 0; index < mapped.Length; index++)
            {
                workCounter.Consume(cancellationToken);
                SvgQuadSpan span = spans[index];
                SvgDevicePoint placed = MapSymbolControl(span.Control, scaleX, scaleY, offsetX, offsetY, instance, viewportFrame, workCounter, cancellationToken);
                mapped[index] = span with { Control = placed };
            }
            return mapped;
        }

        static SvgCubicSpan[] MapSymbolCubicSpans(SvgCubicSpan[] spans, double scaleX, double scaleY, double offsetX, double offsetY, SvgUseReference instance, SvgViewportFrame? viewportFrame, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            SvgCubicSpan[] mapped = new SvgCubicSpan[spans.Length];
            for (int index = 0; index < mapped.Length; index++)
            {
                workCounter.Consume(cancellationToken);
                SvgCubicSpan span = spans[index];
                SvgDevicePoint first = MapSymbolControl(span.FirstControl, scaleX, scaleY, offsetX, offsetY, instance, viewportFrame, workCounter, cancellationToken);
                SvgDevicePoint second = MapSymbolControl(span.SecondControl, scaleX, scaleY, offsetX, offsetY, instance, viewportFrame, workCounter, cancellationToken);
                mapped[index] = new SvgCubicSpan(span.Contour, span.StartIndex, span.EndIndex, first, second);
            }
            return mapped;
        }

        static SvgDevicePoint MapSymbolControl(SvgDevicePoint control, double scaleX, double scaleY, double offsetX, double offsetY, SvgUseReference instance, SvgViewportFrame? viewportFrame, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            workCounter.Consume(cancellationToken);
            double contentX = control.X * scaleX + offsetX + instance.X;
            double contentY = control.Y * scaleY + offsetY + instance.Y;
            SvgDevicePoint placedPoint = instance.Transform.Apply(contentX, contentY);
            double x = placedPoint.X;
            double y = placedPoint.Y;
            if (viewportFrame is not null)
            {
                x = x * viewportFrame.Value.ScaleX + viewportFrame.Value.OffsetX;
                y = y * viewportFrame.Value.ScaleY + viewportFrame.Value.OffsetY;
            }
            if (!double.IsFinite(x) || !double.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
            return new SvgDevicePoint(x, y);
        }
    }

    /// <summary>Maps one symbol-content box through a viewBox into use-viewport coordinates.</summary>
    internal static SvgRectangle MapSymbolBox(SvgRectangle box, SvgViewBox viewBox, double viewportWidth, double viewportHeight)
    {
        (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, viewportWidth, viewportHeight);
        SvgClipBox? clip = box.Clip is {} source ? new SvgClipBox(source.X * scaleX + offsetX, source.Y * scaleY + offsetY, source.Width * scaleX, source.Height * scaleY) { Antialiasing = source.Antialiasing } : null;
        return box with { X = box.X * scaleX + offsetX, Y = box.Y * scaleY + offsetY, Width = box.Width * scaleX, Height = box.Height * scaleY, Clip = clip };
    }

    /// <summary>Maps an instance, validates coverage, then retains its mask around the complete ordered paint batch.</summary>
    private static SvgGeometry[] ApplyInstanceMask(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, byte? mask, string? maskReference, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgGeometry[] sources = Apply(definition, reference with { Opacity = 255 }, frame, state, workCounter, cancellationToken);
        try
        {
            foreach (SvgGeometry geometry in sources) SvgMaskSyntax.ValidateMaskScopes(state, geometry);
            if (mask is null) return SvgPaintRecording.WrapInstance(sources, reference.SourceOpacity, reference.RecordingScope, state, workCounter, cancellationToken);
            ApplyMask(sources, frame, mask, maskReference, state, workCounter, cancellationToken);
            return SvgPaintRecording.WrapInstance(sources, reference.SourceOpacity, reference.RecordingScope, state, workCounter, cancellationToken);
        }
        catch { foreach (SvgGeometry geometry in sources) geometry.Dispose(); throw; }
    }

    /// <summary>Retains a mask around a caller-owned batch without adopting or disposing its geometry.</summary>
    /// <remarks>All metadata belongs to the document; cache growth and scans charge memory/work and observe cancellation.
    /// The caller releases geometry on failure and applies element opacity after this outer mask is attached.</remarks>
    internal static void ApplyMask(Span<SvgGeometry> geometries, SvgViewportFrame? frame, byte? mask, string? maskReference,
        SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if ((maskReference is null && mask is null) || geometries.IsEmpty) return;
        if (mask is null)
        {
            string id = SvgMaskSyntax.ParseMaskId(maskReference ?? throw new InvalidOperationException("An unresolved mask requires its source reference."));
            if (!state.Masks.TryGetValue(id, out byte value))
            {
                if (state.MaskContents.TryGetValue(id, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Spatial mask content on masked leaves is not implemented.");
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
            }
            mask = value;
        }
        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        foreach (SvgGeometry geometry in geometries)
        {
            workCounter.Consume(cancellationToken);
            if (maskReference is not null) SvgMaskSyntax.ValidateMaskCoverage(maskReference, state, SvgMaskSyntax.BoundingBox(geometry));
            SvgRectangle box = SvgMaskSyntax.RecordedObjectBox(geometry, state);
            left = Math.Min(left, box.X); top = Math.Min(top, box.Y);
            right = Math.Max(right, box.X + box.Width); bottom = Math.Max(bottom, box.Y + box.Height);
        }
        SvgRectangle fallback = new(left, top, right - left, bottom - top, default, false, null);
        SvgClipBox region = ResolveInstanceMaskRegion(maskReference, frame, state, fallback);
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Value = mask.Value; operation.Resolved = true;
        operation.Region = region;
        using BudgetedParserDictionary<SvgLeafMaskBinding, SvgLeafMaskBindingReference> bindings = new(state.Budget, SvgMemoryCategory.Parser, 96);
        SvgLeafMaskBinding? noInner = null;
        for (int index = 0; index < geometries.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgLeafMaskComposition.ValidateRegion(geometries[index], region);
            SvgLeafMaskBinding? inner = geometries[index].LeafMask;
            SvgLeafMaskBinding outer;
            if (inner is null) outer = noInner ??= SvgLeafMaskComposition.NewBinding(state, operation, null);
            else if (bindings.TryGetValue(inner, out SvgLeafMaskBindingReference cached)) outer = cached.Binding;
            else { outer = SvgLeafMaskComposition.NewBinding(state, operation, inner); bindings.TryAdd(inner, new(outer)); }
            geometries[index] = geometries[index] with { LeafMask = outer };
        }

        static SvgClipBox ResolveInstanceMaskRegion(string? maskReference, SvgViewportFrame? frame, SvgDocumentState document, SvgRectangle fallback)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (maskReference is not null && document.MaskRegions.TryGetValue(SvgMaskSyntax.ParseMaskId(maskReference), out SvgClipBox custom))
            {
                if (frame is not null && !frame.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions inside viewports are not implemented.");
                return custom;
            }
            if (maskReference is not null && document.MaskFractionRegions.TryGetValue(SvgMaskSyntax.ParseMaskId(maskReference), out SvgClipBox fractions))
            {
                if (frame is not null && !frame.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions inside viewports are not implemented.");
                SvgClipBox resolved = new(fallback.X + fractions.X * fallback.Width, fallback.Y + fractions.Y * fallback.Height, fractions.Width * fallback.Width, fractions.Height * fallback.Height);
                if (document.MaskContentBoxes.TryGetValue(SvgMaskSyntax.ParseMaskId(maskReference), out SvgClipBox cover) && !cover.Contains(resolved.X, resolved.Y, resolved.X + resolved.Width, resolved.Y + resolved.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering its custom region is implemented.");
                return resolved;
            }
            return SvgMaskSyntax.MaskRegion(fallback);
        }
    }
    private static SvgRectangle ClipRectangle(SvgRectangle rectangle, SvgViewportFrame? frame) => frame is null ? rectangle : frame.Value.ClipShifted(rectangle);

    private static SvgGeometry ClipLine(SvgAxisAlignedLine line, SvgViewportFrame? frame) => frame is null ? SvgGeometry.FromAxisAlignedLine(line) : frame.Value.ClipShifted(line);

    private static void AddInstanceGeometry(BudgetedParserList<SvgGeometry> destination, SvgGeometry geometry)
    {
        try { destination.Add(geometry); }
        catch { geometry.Dispose(); throw; }
    }

    private static void AddInstanceGeometries(BudgetedParserList<SvgGeometry> destination, SvgGeometry[] geometries)
    {
        try { destination.AddRange(geometries); }
        catch { foreach (SvgGeometry geometry in geometries) geometry.Dispose(); throw; }
    }

    /// <summary>Owns only newly recolored fill channels; authored stroke and fill channels remain borrowed from the definition.</summary>
    private readonly struct InstanceFillGeometries : IDisposable
    {
        private readonly RenderBudget.BudgetReservation? _owner;
        private readonly SvgSymbolFillBinding[]? _bindings;
        internal InstanceFillGeometries(SvgGeometry[] geometries, SvgSymbolFillBinding[]? bindings, RenderBudget.BudgetReservation? owner)
        {
            Geometries = geometries;
            _bindings = bindings;
            _owner = owner;
        }
        internal SvgGeometry[] Geometries { get; }
        public void Dispose()
        {
            if (_owner is null) return;
            for (int index = 0; index < Geometries.Length; index++)
                if (_bindings is null || _bindings[index].ResolvesFill) Geometries[index].Dispose();
            _owner.Dispose();
        }
    }

    private static InstanceFillGeometries ApplyInstanceFill(SvgDefinition definition, SvgUseReference reference, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgSymbolFillBinding[]? bindings = definition.SymbolFillBindings;
        if (bindings is null)
        {
            if (reference.FillOverride is null) return new(definition.Geometries, null, null);
            if (definition.Geometries.Length != 1 || !definition.AllowsInstanceFillOverride)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible single-geometry definition.");
        }
        else
        {
            bool resolvesFill = false;
            foreach (SvgSymbolFillBinding binding in bindings)
            {
                work.Consume(token);
                resolvesFill |= binding.ResolvesFill;
            }
            if (!resolvesFill) return new(definition.Geometries, bindings, null);
        }
        RenderBudget.BudgetReservation owner = state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(definition.Geometries.Length), SvgMemoryCategory.Parser);
        SvgGeometry[] sources;
        try { sources = new SvgGeometry[definition.Geometries.Length]; }
        catch { owner.Dispose(); throw; }
        int retained = 0;
        try
        {
            for (int index = 0; index < sources.Length; index++)
            {
                work.Consume(token);
                SvgGeometry source = definition.Geometries[index];
                if (bindings is not null && !bindings[index].ResolvesFill) sources[index] = source;
                else
                {
                    SvgPaint fill = reference.FillOverride ?? reference.InheritedFill;
                    if (bindings is not null)
                    {
                        fill = bindings[index].Resolve(fill, reference.InheritedStrokeOpacity);
                    }
                    sources[index] = Recolor(source, fill, bindings is null ? null : bindings[index], reference.FillOverride ?? reference.InheritedFill, reference.InheritedStrokeOpacity, state);
                }
                retained++;
            }
            return new(sources, bindings, owner);
        }
        catch
        {
            for (int index = 0; index < retained; index++)
                if (bindings is null || bindings[index].ResolvesFill) sources[index].Dispose();
            owner.Dispose();
            throw;
        }

        static SvgGeometry Recolor(SvgGeometry source, SvgPaint fill, SvgSymbolFillBinding? binding, SvgPaint instancePaint, SvgPaintOpacity inheritedStrokeOpacity, SvgDocumentState document)
        {
            SvgColor color = SvgPaintResolver.ResolveFillColor(fill, 255);
            SvgStroke? stroke = source.Kind == SvgGeometryKind.Rectangle ? source.Rectangle.Stroke : null;
            if (stroke is {} original && binding is { AuthoredStroke: not null } solidStroke)
                stroke = original with { Color = SvgPaintResolver.ResolveFillColor(solidStroke.ResolveStroke(instancePaint, inheritedStrokeOpacity), 255) };
            return source.Kind switch
            {
                SvgGeometryKind.Rectangle => SvgGeometry.FromRectangle(source.Rectangle with { Color = color, HasFill = fill.HasFill, Stroke = stroke }) with { SolidPaint = source.SolidPaint is null ? null : SvgPaintResolver.ResolveSolidPaint(fill) },
                SvgGeometryKind.OrthogonalBand => SvgGeometry.FromOrthogonalBand(source.Rectangle with { Color = color, HasFill = fill.HasFill }),
                SvgGeometryKind.AxisAlignedLine => SvgGeometry.FromAxisAlignedLine(source.AxisAlignedLine with { Color = color, IsPainted = fill.HasFill }),
                SvgGeometryKind.PolygonFill => SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(source.Polygon.Points, source.Polygon.ContourEnds, source.Polygon.Signs, source.Polygon.QuadSpans, source.Polygon.CubicSpans, color, fill.HasFill, source.Polygon.IsEvenOdd, source.Polygon.Mapping, source.Polygon.Clip, null, document.Budget)),
                SvgGeometryKind.GradientStroke => SvgGeometry.FromGradientStroke(new(source.GradientStroke.Fill with { Color = color, HasFill = fill.HasFill }, source.GradientStroke.Top, source.GradientStroke.Bottom, source.GradientStroke.Left, source.GradientStroke.Right, source.GradientStroke.Gradient, source.GradientStroke.Box, source.GradientStroke.Opacity, source.GradientStroke.HasStroke)),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inherited fill on this symbol geometry is not implemented."),
            };
        }
    }

    /// <summary>Reserves a mapped batch before copying; the document holds its staging-array reservation until teardown.</summary>
    internal static SvgGeometry[] RetainInstanceBatch(ReadOnlySpan<SvgGeometry> geometries, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (geometries.IsEmpty) return [];
        RenderBudget.BudgetReservation reservation = state.Budget.Reserve(128 + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(geometries.Length), SvgMemoryCategory.Parser);
        try
        {
            SvgGeometry[] retained = new SvgGeometry[geometries.Length];
            for (int index = 0; index < retained.Length; index++)
            {
                work.Consume(token);
                retained[index] = geometries[index];
            }
            state.DefinitionReservations.Add(reservation);
            return retained;
        }
        catch { reservation.Dispose(); throw; }
    }

    private static SvgRecordedPaint MapRecordedPaint(SvgRecordedPaint source, SvgUseReference reference, SvgDocumentState state, BudgetedParserDictionary<SvgRecordedPaint, SvgRecordedPaint> cache, SvgRecordingMapper mapper, ParserWorkCounter work, CancellationToken token, bool stroke, SvgPaintOpacity? shaderOpacity, SvgAffineTransform placement)
    {
        if (source.Identity == 0 || reference.PaintServerId is not null) return default;
        SvgRecordedPaintValue value = state.Scene.GetRecordedPaintValue(source);
        work.Consume(token);
        if (cache.TryGetValue(source, out SvgRecordedPaint previous)) return previous;
        float alpha = shaderOpacity is SvgPaintOpacity shader ? shader.Alpha / 255f
            : !stroke && reference.FillOverride is SvgPaint fill ? SvgPaintResolver.ResolveSolidPaint(fill).Alpha : value.Alpha;
        SvgRecordedPaint result = state.Scene.RegisterRecordedPaint(new(mapper.MapPaintIdentity(value.Identity), alpha, value.Foldable, mapper.Map(value.Scope), value.ElementOpacity, mapper.Map(value.OpacityScope)) { IsStroke = value.IsStroke, VisualBounds = value.VisualBounds?.Map(placement, work, token), ObjectBounds = value.ObjectBounds?.Map(placement, work, token) });
        cache.TryAdd(source, result);
        return result;
    }

    private static SvgGeometry TryApplyShaderOverride(SvgGeometry geometry, SvgRecordedPaint source, SvgRecordingMapper mapper, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (source.Identity == 0) return geometry;
        work.Consume(token);
        SvgRecordedPaintValue original = state.Scene.GetRecordedPaintValue(source);
        float alpha = geometry.Kind switch
        {
            SvgGeometryKind.Rectangle or SvgGeometryKind.OrthogonalBand => geometry.Rectangle.Color.Alpha / 255f,
            SvgGeometryKind.GradientFill => geometry.Gradient.Opacity / 255f,
            SvgGeometryKind.RadialGradient => geometry.Radial.Opacity / 255f,
            SvgGeometryKind.PatternFill => geometry.Pattern.Opacity / 255f,
            _ => throw new InvalidOperationException("Unexpected instance paint-server geometry."),
        };
        SvgRecordedPaint paint = state.Scene.RegisterRecordedPaint(new(state.NewPaintOperationIdentity(), alpha, original.Foldable, mapper.Map(original.Scope), SvgPaintOpacity.Opaque, mapper.Map(original.OpacityScope)));
        return geometry with { RecordedPaint = paint };
    }

    private static SvgGeometry[] ApplySymbol(SvgDefinition definition, SvgViewBox symbolBox, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (reference.StrokeOverride is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroked use instances of symbols are not implemented.");
        if (reference.PaintServerId is null)
        {
            RequireUniformOutlineScales(definition, reference);
            using InstanceFillGeometries instanceFill = ApplyInstanceFill(definition, reference, state, workCounter, cancellationToken);
            SvgGeometry[] sources = instanceFill.Geometries;
            using BudgetedParserList<SvgGeometry> applied = new(state.Budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
            SvgLeafMaskMapper? maskMapper = null;
            try
            {
                using BudgetedParserDictionary<SvgRecordedPaint, SvgRecordedPaint> mappedPaints = new(state.Budget, SvgMemoryCategory.Parser, 96);
                using SvgRecordingMapper recordingMapper = SvgRecordingMapper.Create(state, state.NewRecordingScope(), workCounter, cancellationToken);
                for (int index = 0; index < sources.Length; index++)
                {
                    int firstApplied = applied.Count;
                    bool expandedStroke = sources[index].Kind == SvgGeometryKind.Rectangle && sources[index].Rectangle.Stroke is not null;
                    SvgGeometry authoredSource = sources[index] with { AuthoredPath = definition.Geometries[index].AuthoredPath };
                    if (expandedStroke)
                    {
                        AddInstanceGeometries(applied, ApplySymbolStrokedRectangle(sources[index].Rectangle, symbolBox, reference, frame, state, workCounter, cancellationToken));
                    }
                    else AddInstanceGeometry(applied, sources[index].Kind switch
                    {
                        SvgGeometryKind.GradientFill => ApplySymbolGradientFill(sources[index].Gradient, symbolBox, reference, frame, state, workCounter, cancellationToken),
                        SvgGeometryKind.PatternFill => ApplySymbolPatternFill(sources[index].Pattern, symbolBox, reference, frame, state, workCounter, cancellationToken),
                        SvgGeometryKind.RadialGradient => ApplySymbolRadialFill(sources[index].Radial, symbolBox, reference, frame, state, workCounter, cancellationToken),
                        SvgGeometryKind.GradientStroke => ApplySymbolGradientStroke(sources[index].GradientStroke, symbolBox, reference, frame, state, workCounter, cancellationToken),
                        SvgGeometryKind.GradientPolygon => ApplySymbolGradientPolygon(authoredSource, symbolBox, reference, frame, state, workCounter, cancellationToken),
                        SvgGeometryKind.RadialPolygon => ApplySymbolRadialPolygon(authoredSource, symbolBox, reference, frame, state, workCounter, cancellationToken),
                        SvgGeometryKind.PatternPolygon => ApplySymbolPatternPolygon(authoredSource, symbolBox, reference, frame, state, workCounter, cancellationToken),
                        _ => ApplySymbolInstance(authoredSource, symbolBox, reference, frame, state, workCounter, cancellationToken),
                    });
                    SvgUseReference paintReference = reference;
                    if (definition.SymbolFillBindings is {} symbolBindings)
                    {
                        SvgSymbolFillBinding fillBinding = symbolBindings[index];
                        SvgPaint fill = reference.FillOverride ?? reference.InheritedFill;
                        paintReference = reference with { FillOverride = fillBinding.ResolvesFill ? fillBinding.Resolve(fill, reference.InheritedStrokeOpacity) : null };
                    }
                    SvgRecordedPaint fillPaint = MapRecordedPaint(definition.Geometries[index].RecordedPaint, paintReference, state, mappedPaints, recordingMapper, workCounter, cancellationToken, false, definition.SymbolFillBindings?[index] is { IsStroke: true, IsStrokePaintServer: true } shaderStroke ? shaderStroke.StrokeOpacity ?? reference.InheritedStrokeOpacity : definition.SymbolFillBindings?[index] is { IsPaintServer: true } shaderFill ? shaderFill.FillOpacity ?? reference.InheritedFill.FillOpacity : null, SymbolInstancePlacement(symbolBox, reference, frame));
                    SvgSymbolFillBinding? strokeBinding = definition.SymbolFillBindings is {} paintBindings ? paintBindings[index] : null;
                    SvgUseReference strokeReference = strokeBinding is { AuthoredStroke: not null } solidStroke ? reference with { FillOverride = solidStroke.ResolveStroke(reference.FillOverride ?? reference.InheritedFill, reference.InheritedStrokeOpacity) } : reference;
                    SvgRecordedPaint strokePaint = MapRecordedPaint(definition.Geometries[index].RecordedStrokePaint, strokeReference, state, mappedPaints, recordingMapper, workCounter, cancellationToken, strokeBinding?.AuthoredStroke is null, strokeBinding is { IsStrokePaintServer: true } shaderBinding ? shaderBinding.StrokeOpacity ?? reference.InheritedStrokeOpacity : null, SymbolInstancePlacement(symbolBox, reference, frame));
                    for (int mapped = firstApplied; mapped < applied.Count; mapped++)
                        applied[mapped] = applied[mapped] with { RecordedPaint = expandedStroke && mapped > firstApplied ? strokePaint : fillPaint };
                    if (definition.Geometries[index].LeafMask is SvgLeafMaskBinding binding)
                    {
                        maskMapper ??= SvgLeafMaskMapper.Create(state, SymbolInstancePlacement(symbolBox, reference, frame), workCounter, cancellationToken).WithRecordingMapper(recordingMapper);
                        SvgLeafMaskBinding? copied = maskMapper.Map(binding);
                        for (int mapped = firstApplied; mapped < applied.Count; mapped++)
                        {
                            workCounter.Consume(cancellationToken);
                            applied[mapped] = applied[mapped] with { LeafMask = copied };
                        }
                    }
                }
                return RetainInstanceBatch(applied.AsSpan(), state, workCounter, cancellationToken);
            }
            catch { foreach (SvgGeometry geometry in applied.AsSpan()) geometry.Dispose(); throw; }
            finally
            {
                maskMapper?.Dispose();
            }

        static SvgGeometry[] ApplySymbolStrokedRectangle(SvgRectangle source, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (source.Stroke is not SvgStroke sourceStroke) throw new InvalidOperationException("Symbol stroke expansion requires a stroked rectangle.");
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double _, double _) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            if (scaleX != scaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform symbol viewBox scaling of rectangle strokes is not implemented.");
            if (!instance.Transform.IsUniformScale()) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled rectangle strokes on use instances are implemented.");
            double scaledWidth = (double)sourceStroke.Width * Math.Abs(scaleX) * Math.Abs(UniformFactor(instance.Transform));
            SvgRectangle mapped = MapSymbolBox(source, viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle placed = TranslateRectangle(mapped, instance.X, instance.Y);
            if (placed.X != Math.Truncate(placed.X) || placed.Y != Math.Truncate(placed.Y) || placed.Width != Math.Truncate(placed.Width) || placed.Height != Math.Truncate(placed.Height) || !double.IsFinite(scaledWidth) || scaledWidth < 2 || scaledWidth > int.MaxValue || scaledWidth != Math.Truncate(scaledWidth) || (int)scaledWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer-mapped even-width symbol-held rectangle strokes are implemented.");
            SvgGeometry[] expanded = new SvgGeometry[instance.IsPainted ? 5 : 1];
            expanded[0] = ApplySymbolInstance(SvgGeometry.FromRectangle(source with { Stroke = null }), viewBox, instance, viewportFrame, document, workCounter, cancellationToken);
            if (instance.IsPainted)
            {
                double sourceHalfWidth = sourceStroke.Width / 2d;
                SvgRectangle[] sources =
                [
                    new(source.X - sourceHalfWidth, source.Y - sourceHalfWidth, source.Width + sourceStroke.Width, sourceStroke.Width, sourceStroke.Color, true, null) { Clip = source.Clip },
                    new(source.X - sourceHalfWidth, source.Y + source.Height - sourceHalfWidth, source.Width + sourceStroke.Width, sourceStroke.Width, sourceStroke.Color, true, null) { Clip = source.Clip },
                    new(source.X - sourceHalfWidth, source.Y + sourceHalfWidth, sourceStroke.Width, source.Height - sourceStroke.Width, sourceStroke.Color, true, null) { Clip = source.Clip },
                    new(source.X + source.Width - sourceHalfWidth, source.Y + sourceHalfWidth, sourceStroke.Width, source.Height - sourceStroke.Width, sourceStroke.Color, true, null) { Clip = source.Clip },
                ];
                for (int band = 0; band < sources.Length; band++)
                    expanded[band + 1] = ApplySymbolInstance(SvgGeometry.FromRectangle(sources[band]), viewBox, instance, viewportFrame, document, workCounter, cancellationToken);
            }
            return expanded;
        }

        static SvgGeometry ApplySymbolGradientPolygon(SvgGeometry geometry, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            SvgGradientPolygon source = geometry.GradientPolygon;
            ArgumentNullException.ThrowIfNull(document);
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            if (scaleX != scaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform symbol viewBox scaling of polygon content is not implemented.");
            RequireAdmittedSymbolMagnification(geometry, source.Mapping, scaleX, instance);
            using RenderBudget.BudgetReservation mappedReservation = document.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(source.Points.Length), SvgMemoryCategory.Geometry);
            SvgDevicePoint[] mapped = new SvgDevicePoint[source.Points.Length];
            for (int index = 0; index < mapped.Length; index++)
            {
                workCounter.Consume(cancellationToken);
                double contentX = source.Points[index].X * scaleX + offsetX + instance.X;
                double contentY = source.Points[index].Y * scaleY + offsetY + instance.Y;
                SvgDevicePoint placedPoint = instance.Transform.Apply(contentX, contentY);
                double placedX = placedPoint.X;
                double placedY = placedPoint.Y;
                if (viewportFrame is not null)
                {
                    placedX = placedX * viewportFrame.Value.ScaleX + viewportFrame.Value.OffsetX;
                    placedY = placedY * viewportFrame.Value.ScaleY + viewportFrame.Value.OffsetY;
                }
                if (!double.IsFinite(placedX) || !double.IsFinite(placedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                mapped[index] = new SvgDevicePoint(placedX, placedY);
            }
            SvgLinearGradient vector = source.Gradient;
            if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 * scaleX + offsetX + instance.X, Y1 = vector.Y1 * scaleY + offsetY + instance.Y, X2 = vector.X2 * scaleX + offsetX + instance.X, Y2 = vector.Y2 * scaleY + offsetY + instance.Y, ReferenceScaleX = vector.ReferenceScaleX * scaleX, ReferenceScaleY = vector.ReferenceScaleY * scaleY };
            SvgRectangle contentBox = source.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            vector = SvgGradientSyntax.MapUserSpaceVector(instance.Transform, vector, viewportFrame);
            vector = SvgGradientSyntax.MirrorBoundingBoxVector(instance.Transform, vector);
            SvgClipBox viewport = instance.Transform.Apply(new SvgClipBox(instance.X, instance.Y, instance.Width.Value, instance.Height.Value));
            if (viewportFrame is not null)
            {
                viewport = new SvgClipBox(viewport.X + viewportFrame.Value.OffsetX, viewport.Y + viewportFrame.Value.OffsetY, viewport.Width, viewport.Height);
                viewport = viewport.Intersect(viewportFrame.Value.Clip);
            }
            SvgClipBox clip;
            if (source.Clip is null) clip = viewport;
            else
            {
                SvgClipBox content = source.Clip.Value;
                SvgClipBox shifted = new(content.X * scaleX + offsetX + instance.X, content.Y * scaleY + offsetY + instance.Y, content.Width * scaleX, content.Height * scaleY) { Antialiasing = content.Antialiasing };
                SvgClipBox placed = instance.Transform.Apply(shifted);
                if (viewportFrame is not null) placed = new SvgClipBox(placed.X + viewportFrame.Value.OffsetX, placed.Y + viewportFrame.Value.OffsetY, placed.Width, placed.Height) { Antialiasing = placed.Antialiasing };
                clip = placed.Intersect(viewport);
            }
            byte opacity = MultiplyAlpha(source.Opacity, instance.Opacity);
            SvgGeometry result = SvgGeometry.FromGradientPolygon(SvgGradientPolygon.Create(mapped, source.ContourEnds, source.Signs, vector, box, source.HasFill && instance.IsPainted, source.IsEvenOdd, opacity, source.Mapping, clip, null, document.Budget));
            SvgAffineTransform placement = PlainInstancePlacement(instance, viewportFrame).ComposeChecked(SvgAffineTransform.FromScaleOffset(scaleX, scaleY, offsetX, offsetY));
            return geometry.CopyAuthoredPathTo(result, placement, document.Budget, workCounter, cancellationToken);
        }
        static SvgGeometry ApplySymbolGradientFill(SvgGradientGeometry band, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle fill = ApplySymbolInstance(SvgGeometry.FromRectangle(band.Fill with { Stroke = band.Stroke }), viewBox, instance, viewportFrame, document, workCounter, cancellationToken).Rectangle;
            SvgRectangle contentBox = band.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            SvgLinearGradient vector = band.Gradient;
            if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 * scaleX + offsetX + instance.X, Y1 = vector.Y1 * scaleY + offsetY + instance.Y, X2 = vector.X2 * scaleX + offsetX + instance.X, Y2 = vector.Y2 * scaleY + offsetY + instance.Y, ReferenceScaleX = vector.ReferenceScaleX * scaleX, ReferenceScaleY = vector.ReferenceScaleY * scaleY };
            return LowerPlacedGradientFill(vector, fill, box, MultiplyAlpha(band.Opacity, instance.Opacity), fill.HasFill, instance.Transform, viewportFrame, fill.Stroke);
        }
        static SvgGeometry ApplySymbolPatternFill(SvgPatternGeometry band, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle fill = ApplySymbolInstance(SvgGeometry.FromRectangle(band.Fill with { Stroke = band.Stroke }), viewBox, instance, viewportFrame, document, workCounter, cancellationToken).Rectangle;
            double originX = band.OriginX * scaleX + offsetX;
            double originY = band.OriginY * scaleY + offsetY;
            double tileWidth = band.TileWidth * scaleX;
            double tileHeight = band.TileHeight * scaleY;
            double contentX = band.Content.X * scaleX + offsetX;
            double contentY = band.Content.Y * scaleY + offsetY;
            double contentWidth = band.Content.Width * scaleX;
            double contentHeight = band.Content.Height * scaleY;
            if (!IsIntegerGrid(originX) || !IsIntegerGrid(originY) || !IsIntegerGrid(tileWidth) || !IsIntegerGrid(tileHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer-mapped symbol-held pattern tiles are implemented.");
            SvgRectangle content = new(contentX, contentY, contentWidth, contentHeight, band.Content.Color, band.Content.HasFill, band.Content.Stroke);
            SvgPatternTile tile = new(SvgGeometry.FromRectangle(content) with { SolidPaint = band.ContentPaint }, band.VectorContent is null ? (int)originX : band.OriginX, band.VectorContent is null ? (int)originY : band.OriginY, band.VectorContent is null ? (int)tileWidth : band.TileWidth, band.VectorContent is null ? (int)tileHeight : band.TileHeight, SvgAffineTransform.Identity) { VectorContent = band.VectorContent };
            SvgUseReference composed = instance with { Opacity = MultiplyAlpha(band.Opacity, instance.Opacity), IsPainted = band.Fill.HasFill && instance.IsPainted };
            return LowerInstancePatternFill(tile, fill, composed, viewportFrame, fill.Stroke, band.VectorContent is null ? band.ShaderPlacement : SvgAffineTransform.FromScaleOffset(scaleX, scaleY, offsetX, offsetY).ComposeChecked(band.ShaderPlacement));

            static bool IsIntegerGrid(double value)
                => double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue;
        }

        static SvgGeometry ApplySymbolRadialFill(SvgRadialGeometry radial, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle fill = ApplySymbolInstance(SvgGeometry.FromRectangle(radial.Fill with { Stroke = radial.Stroke }), viewBox, instance, viewportFrame, document, workCounter, cancellationToken).Rectangle;
            SvgRectangle contentBox = radial.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            SvgRadialGradient circle = radial.Gradient;
            if (circle.IsUserSpace) circle = circle.HasGeneralTransform ? SvgGradientSyntax.ScaleGeneralRadial(circle, scaleX, scaleY, offsetX + instance.X, offsetY + instance.Y) : circle with { CenterX = circle.CenterX * scaleX + offsetX + instance.X, CenterY = circle.CenterY * scaleY + offsetY + instance.Y, FocalX = circle.FocalX * scaleX + offsetX + instance.X, FocalY = circle.FocalY * scaleY + offsetY + instance.Y, Radius = circle.Radius * Math.Abs(scaleX), FocalRadius = circle.FocalRadius * Math.Abs(scaleX), ReferenceScaleX = circle.ReferenceScaleX * scaleX, ReferenceScaleY = circle.ReferenceScaleY * scaleY };
            return LowerPlacedRadialFill(circle, fill, box, MultiplyAlpha(radial.Opacity, instance.Opacity), fill.HasFill, instance.Transform, viewportFrame, document.Budget, fill.Stroke);
        }

        static SvgGeometry ApplySymbolRadialPolygon(SvgGeometry geometry, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            SvgRadialPolygon source = geometry.RadialPolygon;
            ArgumentNullException.ThrowIfNull(document);
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            if (scaleX != scaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform symbol viewBox scaling of polygon content is not implemented.");
            RequireAdmittedSymbolMagnification(geometry, source.Mapping, scaleX, instance);
            using RenderBudget.BudgetReservation mappedReservation = document.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(source.Points.Length), SvgMemoryCategory.Geometry);
            SvgDevicePoint[] mapped = new SvgDevicePoint[source.Points.Length];
            for (int index = 0; index < mapped.Length; index++)
            {
                workCounter.Consume(cancellationToken);
                double contentX = source.Points[index].X * scaleX + offsetX + instance.X;
                double contentY = source.Points[index].Y * scaleY + offsetY + instance.Y;
                SvgDevicePoint placedPoint = instance.Transform.Apply(contentX, contentY);
                double placedX = placedPoint.X;
                double placedY = placedPoint.Y;
                if (viewportFrame is not null)
                {
                    placedX = placedX * viewportFrame.Value.ScaleX + viewportFrame.Value.OffsetX;
                    placedY = placedY * viewportFrame.Value.ScaleY + viewportFrame.Value.OffsetY;
                }
                if (!double.IsFinite(placedX) || !double.IsFinite(placedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                mapped[index] = new SvgDevicePoint(placedX, placedY);
            }
            SvgRadialGradient circle = source.Gradient;
            if (circle.IsUserSpace) circle = circle.HasGeneralTransform ? SvgGradientSyntax.ScaleGeneralRadial(circle, scaleX, scaleY, offsetX + instance.X, offsetY + instance.Y) : circle with { CenterX = circle.CenterX * scaleX + offsetX + instance.X, CenterY = circle.CenterY * scaleY + offsetY + instance.Y, FocalX = circle.FocalX * scaleX + offsetX + instance.X, FocalY = circle.FocalY * scaleY + offsetY + instance.Y, Radius = circle.Radius * Math.Abs(scaleX), FocalRadius = circle.FocalRadius * Math.Abs(scaleX) };
            SvgRectangle contentBox = source.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            circle = SvgGradientSyntax.MapUserSpaceRadial(instance.Transform, circle, viewportFrame);
            SvgClipBox viewport = instance.Transform.Apply(new SvgClipBox(instance.X, instance.Y, instance.Width.Value, instance.Height.Value));
            if (viewportFrame is not null)
            {
                viewport = new SvgClipBox(viewport.X + viewportFrame.Value.OffsetX, viewport.Y + viewportFrame.Value.OffsetY, viewport.Width, viewport.Height);
                viewport = viewport.Intersect(viewportFrame.Value.Clip);
            }
            SvgClipBox clip;
            if (source.Clip is null) clip = viewport;
            else
            {
                SvgClipBox content = source.Clip.Value;
                SvgClipBox shifted = new(content.X * scaleX + offsetX + instance.X, content.Y * scaleY + offsetY + instance.Y, content.Width * scaleX, content.Height * scaleY) { Antialiasing = content.Antialiasing };
                SvgClipBox placed = instance.Transform.Apply(shifted);
                if (viewportFrame is not null) placed = new SvgClipBox(placed.X + viewportFrame.Value.OffsetX, placed.Y + viewportFrame.Value.OffsetY, placed.Width, placed.Height) { Antialiasing = placed.Antialiasing };
                clip = placed.Intersect(viewport);
            }
            byte opacity = MultiplyAlpha(source.Opacity, instance.Opacity);
            SvgGeometry result = SvgGeometry.FromRadialPolygon(SvgRadialPolygon.Create(mapped, source.ContourEnds, source.Signs, circle, box, source.HasFill && instance.IsPainted, source.IsEvenOdd, opacity, source.Mapping, clip, null, document.Budget));
            SvgAffineTransform placement = PlainInstancePlacement(instance, viewportFrame).ComposeChecked(SvgAffineTransform.FromScaleOffset(scaleX, scaleY, offsetX, offsetY));
            return geometry.CopyAuthoredPathTo(result, placement, document.Budget, workCounter, cancellationToken);
        }

        static SvgGeometry ApplySymbolPatternPolygon(SvgGeometry geometry, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            SvgPatternPolygon source = geometry.PatternPolygon;
            ArgumentNullException.ThrowIfNull(document);
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            if (scaleX != scaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform symbol viewBox scaling of polygon content is not implemented.");
            RequireAdmittedSymbolMagnification(geometry, source.Mapping, scaleX, instance);
            if (viewportFrame is not null && viewportFrame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
            using RenderBudget.BudgetReservation mappedReservation = document.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(source.Points.Length), SvgMemoryCategory.Geometry);
            SvgDevicePoint[] mapped = new SvgDevicePoint[source.Points.Length];
            for (int index = 0; index < mapped.Length; index++)
            {
                workCounter.Consume(cancellationToken);
                double contentX = source.Points[index].X * scaleX + offsetX + instance.X;
                double contentY = source.Points[index].Y * scaleY + offsetY + instance.Y;
                SvgDevicePoint placedPoint = instance.Transform.Apply(contentX, contentY);
                double placedX = placedPoint.X;
                double placedY = placedPoint.Y;
                if (viewportFrame is not null)
                {
                    placedX = placedX * viewportFrame.Value.ScaleX + viewportFrame.Value.OffsetX;
                    placedY = placedY * viewportFrame.Value.ScaleY + viewportFrame.Value.OffsetY;
                }
                if (!double.IsFinite(placedX) || !double.IsFinite(placedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                mapped[index] = new SvgDevicePoint(placedX, placedY);
            }
            double gridOriginX = source.OriginX * scaleX + offsetX;
            double gridOriginY = source.OriginY * scaleY + offsetY;
            double gridTileWidth = source.TileWidth * scaleX;
            double gridTileHeight = source.TileHeight * scaleY;
            double gridContentX = source.Content.X * scaleX + offsetX;
            double gridContentY = source.Content.Y * scaleY + offsetY;
            double gridContentWidth = source.Content.Width * scaleX;
            double gridContentHeight = source.Content.Height * scaleY;
            if (!IsIntegerGrid(gridOriginX) || !IsIntegerGrid(gridOriginY) || !IsIntegerGrid(gridTileWidth) || !IsIntegerGrid(gridTileHeight) || !IsIntegerGrid(gridContentX) || !IsIntegerGrid(gridContentY) || !IsIntegerGrid(gridContentWidth) || !IsIntegerGrid(gridContentHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer-mapped symbol-held pattern tiles are implemented.");
            SvgRectangle content = new(gridContentX, gridContentY, gridContentWidth, gridContentHeight, source.Content.Color, source.Content.HasFill, source.Content.Stroke);
            SvgClipBox viewport = instance.Transform.Apply(new SvgClipBox(instance.X, instance.Y, instance.Width.Value, instance.Height.Value));
            if (viewportFrame is not null)
            {
                viewport = new SvgClipBox(viewport.X + viewportFrame.Value.OffsetX, viewport.Y + viewportFrame.Value.OffsetY, viewport.Width, viewport.Height);
                viewport = viewport.Intersect(viewportFrame.Value.Clip);
            }
            SvgClipBox clip;
            if (source.Clip is null) clip = viewport;
            else
            {
                SvgClipBox shiftedSource = source.Clip.Value;
                SvgClipBox shifted = new(shiftedSource.X * scaleX + offsetX + instance.X, shiftedSource.Y * scaleY + offsetY + instance.Y, shiftedSource.Width * scaleX, shiftedSource.Height * scaleY) { Antialiasing = shiftedSource.Antialiasing };
                SvgClipBox placed = instance.Transform.Apply(shifted);
                if (viewportFrame is not null) placed = new SvgClipBox(placed.X + viewportFrame.Value.OffsetX, placed.Y + viewportFrame.Value.OffsetY, placed.Width, placed.Height) { Antialiasing = placed.Antialiasing };
                clip = placed.Intersect(viewport);
            }
            byte opacity = MultiplyAlpha(source.Opacity, instance.Opacity);
            SvgAffineTransform placement = PlainInstancePlacement(instance, viewportFrame).ComposeChecked(SvgAffineTransform.FromScaleOffset(scaleX, scaleY, offsetX, offsetY));
            SvgGeometry result = SvgGeometry.FromPatternPolygon(SvgPatternPolygon.Create(mapped, source.ContourEnds, source.Signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), content, source.ContentPaint, source.VectorContent is null ? (int)gridOriginX : source.OriginX, source.VectorContent is null ? (int)gridOriginY : source.OriginY, source.VectorContent is null ? (int)gridTileWidth : source.TileWidth, source.VectorContent is null ? (int)gridTileHeight : source.TileHeight, source.HasFill && instance.IsPainted, source.IsEvenOdd, opacity, source.Mapping, clip, source.VectorContent, source.VectorContent is null ? SvgAffineTransform.Identity : placement.ComposeChecked(source.ShaderPlacement), document.Budget));
            return geometry.CopyAuthoredPathTo(result, placement, document.Budget, workCounter, cancellationToken);

            static bool IsIntegerGrid(double value)
                => double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue;
        }

        static SvgGeometry ApplySymbolGradientStroke(SvgGradientStrokeGeometry stroke, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle fill = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Fill), viewBox, instance, viewportFrame, document, workCounter, cancellationToken).Rectangle;
            SvgRectangle top = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Top), viewBox, instance, viewportFrame, document, workCounter, cancellationToken).Rectangle;
            SvgRectangle bottom = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Bottom), viewBox, instance, viewportFrame, document, workCounter, cancellationToken).Rectangle;
            SvgRectangle left = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Left), viewBox, instance, viewportFrame, document, workCounter, cancellationToken).Rectangle;
            SvgRectangle right = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Right), viewBox, instance, viewportFrame, document, workCounter, cancellationToken).Rectangle;
            SvgRectangle contentBox = stroke.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            SvgLinearGradient vector = stroke.Gradient;
            if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 * scaleX + offsetX + instance.X, Y1 = vector.Y1 * scaleY + offsetY + instance.Y, X2 = vector.X2 * scaleX + offsetX + instance.X, Y2 = vector.Y2 * scaleY + offsetY + instance.Y, ReferenceScaleX = vector.ReferenceScaleX * scaleX, ReferenceScaleY = vector.ReferenceScaleY * scaleY };
            vector = SvgGradientSyntax.MapUserSpaceVector(instance.Transform, vector, viewportFrame);
            vector = SvgGradientSyntax.MirrorBoundingBoxVector(instance.Transform, vector);
            byte opacity = MultiplyAlpha(stroke.Opacity, instance.Opacity);
            SvgColor baseColor = stroke.Fill.Color;
            SvgRectangle finished = fill with { Color = new SvgColor(baseColor.Red, baseColor.Green, baseColor.Blue, MultiplyAlpha(baseColor.Alpha, instance.Opacity)), HasFill = fill.HasFill };
            return SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(finished, top, bottom, left, right, vector, box, opacity, stroke.HasStroke && instance.IsPainted));
        }
        }

        SvgGeometry shader = ApplySymbolPaintServerFill(definition, symbolBox, reference, frame, state, workCounter, cancellationToken);
        try
        {
            using SvgRecordingMapper mapper = SvgRecordingMapper.Create(state, state.NewRecordingScope(), workCounter, cancellationToken);
            return [TryApplyShaderOverride(shader, definition.Geometries[0].RecordedPaint, mapper, state, workCounter, cancellationToken)];
        }
        catch { shader.Dispose(); throw; }
    }

    /// <summary>Preserves the independently painted solid border while mapping a shader rectangle instance.</summary>
    private static SvgStroke? MapInstanceSolidStroke(SvgRectangle fill, SvgStroke? stroke, SvgUseReference reference, SvgViewportFrame? frame)
    {
        if (stroke is null) return null;
        SvgRectangle placed = ApplyRectangle(fill with { Stroke = stroke }, reference);
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        if (frame is not null)
        {
            placed = frame.Value.Shift(placed);
            double half = placed.Stroke is SvgStroke mapped ? mapped.Width / 2 : 0;
            if (!frame.Value.Clip.Contains(placed.X - half, placed.Y - half, placed.X + placed.Width + half, placed.Y + placed.Height + half))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
        }
        return placed.Stroke;
    }

    private static SvgGeometry LowerDirectInstancePaintFill(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (definition.Geometries.Length != 1 || definition.Geometries[0].Kind != SvgGeometryKind.Rectangle || !definition.AllowsInstanceFillOverride) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible rectangle definition.");
        if (definition.Geometries[0].Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        string paintId = reference.PaintServerId ?? throw new InvalidOperationException("Instance paint lowering requires a paint-server reference.");
        if (state.Patterns.TryGetValue(paintId, out SvgPatternTile tile)) return LowerInstancePatternFill(tile, InstancePlacement(definition.Geometries[0].Rectangle, reference), reference, frame, null, SvgAffineTransform.Identity);
        if (state.Gradients.TryGetValue(paintId, out SvgLinearGradient gradient)) return LowerInstanceGradientFill(gradient, InstancePlacement(definition.Geometries[0].Rectangle, reference), InstanceBox(definition.Geometries[0].Rectangle, reference, frame), reference, frame, null);
        if (state.RadialGradients.TryGetValue(paintId, out SvgRadialGradient circle)) return LowerInstanceRadialFill(circle, InstancePlacement(definition.Geometries[0].Rectangle, reference), InstanceBox(definition.Geometries[0].Rectangle, reference, frame), reference, frame, state.Budget);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
    }

    private static SvgRectangle InstancePlacement(SvgRectangle definitionBox, SvgUseReference reference)
        => reference.Transform.Apply(TranslateRectangle(definitionBox, reference.X, reference.Y) with { Color = default });

    /// <summary>Moves original paint edges and their retained clip together before instance transforms.</summary>
    private static SvgRectangle TranslateRectangle(SvgRectangle rectangle, double x, double y)
    {
        SvgClipBox? clip = rectangle.Clip is {} source ? source with { X = source.X + x, Y = source.Y + y } : null;
        return rectangle with { X = rectangle.X + x, Y = rectangle.Y + y, Clip = clip };
    }

    private static SvgRectangle InstanceBox(SvgRectangle definitionBox, SvgUseReference reference, SvgViewportFrame? frame)
    {
        SvgRectangle box = InstancePlacement(definitionBox, reference);
        return frame is null ? box : frame.Value.Shift(box);
    }

    private static SvgGeometry LowerInstanceGradientFill(SvgLinearGradient gradient, SvgRectangle placement, SvgRectangle box, SvgUseReference reference, SvgViewportFrame? frame, SvgStroke? stroke)
    {
        if (gradient.IsUserSpace) gradient = gradient with { X1 = gradient.X1 + reference.X, Y1 = gradient.Y1 + reference.Y, X2 = gradient.X2 + reference.X, Y2 = gradient.Y2 + reference.Y };
        return LowerPlacedGradientFill(gradient, placement, box, reference.Opacity, reference.IsPainted, reference.Transform, frame, stroke);
    }

    private static SvgGeometry LowerInstanceRadialFill(SvgRadialGradient circle, SvgRectangle placement, SvgRectangle box, SvgUseReference reference, SvgViewportFrame? frame, RenderBudget budget)
    {
        if (circle.IsUserSpace) circle = circle.HasGeneralTransform ? SvgGradientSyntax.ShiftGeneralRadial(circle, reference.X, reference.Y) : circle with { CenterX = circle.CenterX + reference.X, CenterY = circle.CenterY + reference.Y, FocalX = circle.FocalX + reference.X, FocalY = circle.FocalY + reference.Y };
        return LowerPlacedRadialFill(circle, placement, box, reference.Opacity, reference.IsPainted, reference.Transform, frame, budget, null);
    }

    private static SvgGeometry LowerPlacedGradientFill(SvgLinearGradient vector, SvgRectangle placement, SvgRectangle box, byte opacity, bool painted, SvgAffineTransform vectorTransform, SvgViewportFrame? frame, SvgStroke? stroke)
    {
        vector = SvgGradientSyntax.MapUserSpaceVector(vectorTransform, vector, frame);
        vector = SvgGradientSyntax.MirrorBoundingBoxVector(vectorTransform, vector);
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        return FinishInstanceGradientFill(vector, box, placement, opacity, painted, stroke);
    }

    private static SvgGeometry LowerInstancePatternFill(SvgPatternTile tile, SvgRectangle placement, SvgUseReference reference, SvgViewportFrame? frame, SvgStroke? stroke, SvgAffineTransform sourcePlacement)
    {
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
        if (!reference.Transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned pattern transforms are implemented.");
        if (reference.Transform.A < 0 || reference.Transform.D < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        return FinishInstancePatternFill(tile, placement, reference.Opacity, reference.IsPainted, stroke, PlainInstancePlacement(reference, frame).ComposeChecked(sourcePlacement));
    }

    private static SvgGeometry FinishInstancePatternFill(SvgPatternTile tile, SvgRectangle placement, byte opacity, bool painted, SvgStroke? stroke, SvgAffineTransform shaderPlacement)
    {
        return SvgPatternSyntax.LowerPatternFillGeometry(tile, placement, painted, opacity, stroke, shaderPlacement);
    }

    private static SvgGeometry LowerPlacedRadialFill(SvgRadialGradient circle, SvgRectangle placement, SvgRectangle box, byte opacity, bool painted, SvgAffineTransform circleTransform, SvgViewportFrame? frame, RenderBudget budget, SvgStroke? stroke)
    {
        circle = SvgGradientSyntax.MapUserSpaceRadial(circleTransform, circle, frame);
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        return FinishInstanceRadialFill(circle, box, placement, opacity, painted, budget, stroke);
    }

    private static SvgGeometry FinishInstanceRadialFill(SvgRadialGradient circle, SvgRectangle box, SvgRectangle placement, byte opacity, bool painted, RenderBudget budget, SvgStroke? stroke)
    {
        SvgRectangle fill = placement with { HasFill = painted && !circle.IsEmpty };
        return SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, fill, opacity, stroke, budget));
    }

    private static SvgGeometry FinishInstanceGradientFill(SvgLinearGradient gradient, SvgRectangle box, SvgRectangle placement, byte opacity, bool painted, SvgStroke? stroke)
    {
        SvgRectangle fill = placement with { HasFill = painted && !gradient.IsEmpty };
        if (gradient.Stops.Length > 1 && gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Stops.Length > 1 && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
        }
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, opacity, stroke));
    }

    private static SvgGeometry ApplySymbolPaintServerFill(SvgDefinition definition, SvgViewBox symbolBox, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (definition.Geometries.Length != 1 || definition.Geometries[0].Kind != SvgGeometryKind.Rectangle || !definition.AllowsInstanceFillOverride) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible rectangle definition.");
        if (definition.Geometries[0].Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        string paintId = reference.PaintServerId ?? throw new InvalidOperationException("Instance paint lowering requires a paint-server reference.");
        SvgRectangle fillBox = ApplySymbolInstance(definition.Geometries[0], symbolBox, reference, frame, state, workCounter, cancellationToken).Rectangle with { Color = default };
        (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(symbolBox, reference.Width.Value, reference.Height.Value);
        SvgRectangle contentBox = definition.Geometries[0].Rectangle;
        SvgRectangle box = reference.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + reference.X, contentBox.Y * scaleY + offsetY + reference.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
        if (frame is not null) box = frame.Value.Shift(box);
        if (state.Patterns.TryGetValue(paintId, out SvgPatternTile tile))
        {
            if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
            if (!reference.Transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned pattern transforms are implemented.");
            if (reference.Transform.A < 0 || reference.Transform.D < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
            return FinishInstancePatternFill(tile, fillBox, reference.Opacity, reference.IsPainted, null, PlainInstancePlacement(reference, frame));
        }
        if (state.Gradients.TryGetValue(paintId, out SvgLinearGradient gradient))
        {
            if (gradient.IsUserSpace) gradient = gradient with { X1 = (gradient.X1 * scaleX + offsetX) + reference.X, Y1 = (gradient.Y1 * scaleY + offsetY) + reference.Y, X2 = (gradient.X2 * scaleX + offsetX) + reference.X, Y2 = (gradient.Y2 * scaleY + offsetY) + reference.Y, ReferenceScaleX = gradient.ReferenceScaleX * scaleX, ReferenceScaleY = gradient.ReferenceScaleY * scaleY };
            gradient = SvgGradientSyntax.MapUserSpaceVector(reference.Transform, gradient, frame);
            gradient = SvgGradientSyntax.MirrorBoundingBoxVector(reference.Transform, gradient);
            return FinishInstanceGradientFill(gradient, box, fillBox, reference.Opacity, reference.IsPainted, null);
        }
        if (state.RadialGradients.TryGetValue(paintId, out SvgRadialGradient circle))
        {
            if (circle.IsUserSpace) circle = circle.HasGeneralTransform ? SvgGradientSyntax.ScaleGeneralRadial(circle, scaleX, scaleY, offsetX + reference.X, offsetY + reference.Y) : circle with { CenterX = (circle.CenterX * scaleX + offsetX) + reference.X, CenterY = (circle.CenterY * scaleY + offsetY) + reference.Y, FocalX = (circle.FocalX * scaleX + offsetX) + reference.X, FocalY = (circle.FocalY * scaleY + offsetY) + reference.Y, Radius = circle.Radius * Math.Abs(scaleX), FocalRadius = circle.FocalRadius * Math.Abs(scaleX), ReferenceScaleX = circle.ReferenceScaleX * scaleX, ReferenceScaleY = circle.ReferenceScaleY * scaleY };
            circle = SvgGradientSyntax.MapUserSpaceRadial(reference.Transform, circle, frame);
            return FinishInstanceRadialFill(circle, box, fillBox, reference.Opacity, reference.IsPainted, state.Budget, null);
        }
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
    }

    private static SvgRectangle ApplyRectangle(SvgRectangle rectangle, SvgUseReference reference)
    {
        SvgStroke? stroke = MapInstanceStroke(rectangle.Stroke, reference);
        SvgRectangle transformed = reference.Transform.Apply(TranslateRectangle(rectangle, reference.X, reference.Y));
        return transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), HasFill = transformed.HasFill && reference.IsPainted, Stroke = reference.IsPainted ? stroke : null };

        static SvgStroke? MapInstanceStroke(SvgStroke? stroke, SvgUseReference reference)
        {
            if (stroke is null) return null;
            if (!reference.Transform.IsUniformScale()) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled rectangle strokes on use instances are implemented.");
            double scaledWidth = stroke.Value.Width * Math.Abs(UniformFactor(reference.Transform));
            if (!double.IsFinite(scaledWidth) || scaledWidth != Math.Truncate(scaledWidth) || scaledWidth > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer effective rectangle stroke widths on use instances are implemented.");
            int width = checked((int)scaledWidth);
            SvgColor color = stroke.Value.Color;
            return new SvgStroke(new SvgColor(color.Red, color.Green, color.Blue, MultiplyAlpha(color.Alpha, reference.Opacity)), width);
        }
    }

    private static SvgAxisAlignedLine ApplyAxisAlignedLine(SvgAxisAlignedLine line, SvgUseReference reference)
    {
        if (reference.X != Math.Truncate(reference.X) || reference.Y != Math.Truncate(reference.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integral axis-aligned line use offsets are implemented.");
        SvgAxisAlignedLine transformed = reference.Transform.Apply(line with { X1 = line.X1 + reference.X, Y1 = line.Y1 + reference.Y, X2 = line.X2 + reference.X, Y2 = line.Y2 + reference.Y });
        return transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), IsPainted = transformed.IsPainted && reference.IsPainted };
    }

    internal static void ResolvePendingUses(BudgetedParserDictionary<string, SvgDefinition> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int commandShift = 0;
        for (int pendingUseIndex = 0; pendingUseIndex < pendingUses.Count; pendingUseIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingUse pendingUse = pendingUses[pendingUseIndex];
            if (!definitions.TryGetValue(pendingUse.Reference.Id, out SvgDefinition definition) && !state.RenderedGroupDefinitions.TryGetValue(pendingUse.Reference.Id, out definition))
            {
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only supported local geometry use references are implemented.");
            }
            InsertResolvedUse(definition, pendingUse, scene, state, ref commandShift, workCounter, cancellationToken);
        }
    }

    /// <summary>Maps one pending use through its frame, clip, mask and scope checks without touching the scene.</summary>
    private static SvgGeometry[] ResolveUseGeometries(SvgDefinition definition, PendingUse pendingUse, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        SvgViewportFrame? frame = pendingUse.Frame;
        if (pendingUse.DeferredClip is SvgDeferredClip deferred) frame = ResolveDeferredClipFrame(definition, pendingUse.Reference, deferred, state);
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Use instances in scaled viewports are not implemented.");
        byte? mask = pendingUse.Mask;
        if (pendingUse.MaskReference is not null && mask is null) mask = ResolveDeferredMaskValue(pendingUse.MaskReference, state);
        SvgGeometry[] applied = ApplyInstanceMask(definition, pendingUse.Reference, frame, mask, pendingUse.MaskReference, state, workCounter, cancellationToken);
        try
        {
            if (pendingUse.MaskScopeCount > 0) foreach (SvgGeometry geometry in applied) SvgMaskSyntax.ValidateMaskScopeSnapshot(state, pendingUse.MaskScopeStart, pendingUse.MaskScopeCount, SvgMaskSyntax.BoundingBox(geometry));
            return applied;
        }
        catch { foreach (SvgGeometry geometry in applied) geometry.Dispose(); throw; }
    }

    /// <summary>Inserts one resolved use instance into its scene placeholder, advancing the shared command shift.</summary>
    private static void InsertResolvedUse(SvgDefinition definition, PendingUse pendingUse, SvgSceneBuilder scene, SvgDocumentState state, ref int commandShift, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        SvgGeometry[] applied = ResolveUseGeometries(definition, pendingUse, state, workCounter, cancellationToken);
        int adjustedCommand = pendingUse.Slot.CommandIndex + commandShift;
        int insertedCommands = scene.ReplaceWithMany(pendingUse.Slot with { CommandIndex = adjustedCommand }, applied);
        commandShift += insertedCommands;
        ShiftPendingCommandRanges(state, adjustedCommand + 1, insertedCommands, true, workCounter, cancellationToken);
    }

    private static byte ResolveDeferredMaskValue(string maskReference, SvgDocumentState document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string maskId = SvgMaskSyntax.ParseMaskId(maskReference);
        if (!document.Masks.TryGetValue(maskId, out byte value)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
        return value;
    }

    private static SvgViewportFrame ResolveDeferredClipFrame(SvgDefinition definition, SvgUseReference reference, SvgDeferredClip deferred, SvgDocumentState document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.ObjectClips.TryGetValue(deferred.ClipId, out SvgClipBox fractions))
        {
            if (document.Clips.TryGetValue(deferred.ClipId, out SvgClipBox box))
            {
                if (deferred.Parent is not null && deferred.Parent.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
                SvgClipBox userClip = deferred.Parent is null ? box : deferred.Parent.Value.Clip.Intersect(box);
                return new SvgViewportFrame(deferred.Parent?.OffsetX ?? 0, deferred.Parent?.OffsetY ?? 0, deferred.Parent?.ScaleX ?? 1, deferred.Parent?.ScaleY ?? 1, deferred.Parent?.ViewportWidth, deferred.Parent?.ViewportHeight, userClip, deferred.Depth);
            }
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
        }
        if (deferred.Parent is not null && deferred.Parent.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
        SvgRectangle instanceBox;
        if (definition.SymbolViewBox is SvgViewBox symbolBox)
        {
            if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            foreach (SvgGeometry source in definition.Geometries)
            {
                SvgRectangle box = SvgMaskSyntax.BoundingBox(source);
                SvgRectangle mapped = MapSymbolBox(box, symbolBox, reference.Width.Value, reference.Height.Value);
                SvgRectangle placed = reference.Transform.Apply(new SvgRectangle(mapped.X + reference.X, mapped.Y + reference.Y, mapped.Width, mapped.Height, default, true, null));
                left = Math.Min(left, placed.X);
                top = Math.Min(top, placed.Y);
                right = Math.Max(right, placed.X + placed.Width);
                bottom = Math.Max(bottom, placed.Y + placed.Height);
            }
            SvgRectangle contentBox = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, true, null);
            SvgClipBox viewport = reference.Transform.Apply(new SvgClipBox(reference.X, reference.Y, reference.Width.Value, reference.Height.Value));
            instanceBox = viewport.ClipRectangle(contentBox);
        }
        else
        {
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            foreach (SvgGeometry source in definition.Geometries)
            {
                SvgRectangle box = SvgMaskSyntax.BoundingBox(source);
                SvgRectangle placed = reference.Transform.Apply(new SvgRectangle(box.X + reference.X, box.Y + reference.Y, box.Width, box.Height, default, true, null));
                left = Math.Min(left, placed.X);
                top = Math.Min(top, placed.Y);
                right = Math.Max(right, placed.X + placed.Width);
                bottom = Math.Max(bottom, placed.Y + placed.Height);
            }
            instanceBox = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, true, null);
        }
        SvgClipBox resolved = new(instanceBox.X + fractions.X * instanceBox.Width, instanceBox.Y + fractions.Y * instanceBox.Height, fractions.Width * instanceBox.Width, fractions.Height * instanceBox.Height);
        SvgClipBox clip = deferred.Parent is null ? resolved : deferred.Parent.Value.Clip.Intersect(resolved);
        return new SvgViewportFrame(deferred.Parent?.OffsetX ?? 0, deferred.Parent?.OffsetY ?? 0, deferred.Parent?.ScaleX ?? 1, deferred.Parent?.ScaleY ?? 1, deferred.Parent?.ViewportWidth, deferred.Parent?.ViewportHeight, clip, deferred.Depth);
    }

    /// <summary>Returns the uniform scale factor of an axis-aligned or uniform transform for width scaling.</summary>
    private static double UniformFactor(SvgAffineTransform transform)
    {
        if (transform.IsAxisAligned) return transform.A;
        if (!transform.TryGetUniformScale(out double uniform)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled instance transforms are implemented.");
        return uniform;
    }

    /// <summary>Returns one axis column length for magnification checks, exact for axis-aligned transforms.</summary>
    private static double ColumnLength(double x, double y) => Math.Sqrt(x * x + y * y);

    /// <summary>Shifts retained command ranges after an insertion, keeping deferred group clips and masks aligned.</summary>
    /// <remarks>Starts at an inserted end marker or expanded-reference boundary move right.
    /// Starts at an inserted begin marker stay in place so the containing range includes its child scope.
    /// Each visited range consumes bounded work and observes cancellation; the scene owner releases partial insertions on failure.</remarks>
    internal static void ShiftPendingCommandRanges(SvgDocumentState state, int insertPosition, int insertedCount, bool shiftStartsAtInsertion, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (insertedCount == 0) return;
        for (int groupIndex = 0; groupIndex < state.PendingGroupClips.Count; groupIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingGroupClip group = state.PendingGroupClips[groupIndex];
            int first = group.FirstCommandIndex > insertPosition || shiftStartsAtInsertion && group.FirstCommandIndex == insertPosition ? group.FirstCommandIndex + insertedCount : group.FirstCommandIndex;
            int end = group.EndCommandIndex >= insertPosition ? group.EndCommandIndex + insertedCount : group.EndCommandIndex;
            state.PendingGroupClips[groupIndex] = group with { FirstCommandIndex = first, EndCommandIndex = end };
        }
        for (int maskIndex = 0; maskIndex < state.PendingGroupMasks.Count; maskIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingGroupMask mask = state.PendingGroupMasks[maskIndex];
            int first = mask.FirstCommandIndex > insertPosition || shiftStartsAtInsertion && mask.FirstCommandIndex == insertPosition ? mask.FirstCommandIndex + insertedCount : mask.FirstCommandIndex;
            int end = mask.EndCommandIndex >= insertPosition ? mask.EndCommandIndex + insertedCount : mask.EndCommandIndex;
            state.PendingGroupMasks[maskIndex] = mask with { FirstCommandIndex = first, EndCommandIndex = end };
        }
    }

    /// <summary>Copies the open masked-group scope boxes into the retained snapshot store, returning the snapshot start.</summary>
    private static int SnapshotMaskScopes(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int start = state.MaskScopeSnapshots.Count;
        foreach (SvgClipBox scope in state.MaskScopes.AsSpan())
        {
            workCounter.Consume(cancellationToken);
            state.MaskScopeSnapshots.Add(scope);
        }
        return start;
    }

    internal static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}
