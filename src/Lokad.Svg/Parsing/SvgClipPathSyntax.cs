using System.Xml;

using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one direct leaf geometry range whose userSpace clip-path reference resolves at document end.</summary>
internal readonly record struct PendingLeafClip(int FirstGeometryIndex, int GeometryCount, string ClipId, SvgViewportFrame? Parent, bool AdmitsBoundingBox, bool SubjectMapIsIdentity);

/// <summary>Retains one group command range whose userSpace clip-path reference resolves at document end, after use expansion.</summary>
internal readonly record struct PendingGroupClip(string ClipId, SvgViewportFrame? Parent, int FirstCommandIndex, int EndCommandIndex, int Depth, int ScaledPushesAtOpen, int ScaledPushesAtClose);

/// <summary>Parses the bounded clip-path reference syntax shared by clipped containers and leaves.</summary>
internal static class SvgClipPathSyntax
{
    internal static string ParseClipId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A clip-path reference is required.");
        if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local clip-path references are implemented.");
        return id;
    }

    /// <summary>Validates the admitted no-op clip rule shared by attributes and style declarations.</summary>
    internal static void ValidateClipRule(ReadOnlySpan<char> value)
    {
        if (value.Trim().Equals("nonzero".AsSpan(), StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("evenodd".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero and evenodd clip rules are implemented.");
    }

    /// <summary>Parses the clip-path coordinate system, defaulting to userSpaceOnUse.</summary>
    internal static bool ParseClipUnits(string? value)
    {
        if (value is null || value.Equals("userSpaceOnUse", StringComparison.Ordinal)) return false;
        if (value.Equals("objectBoundingBox", StringComparison.Ordinal)) return true;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse and objectBoundingBox clip-path units are implemented.");
    }


    /// <summary>Resolves the effective clip for one rectangle from the viewport frame with objectBoundingBox fractions scaled by its placement.</summary>
    internal static SvgViewportFrame? ResolveLeafClipOrPlacement(XmlReader reader, SvgDocumentState state, SvgViewportFrame? frame, SvgRectangle placement)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("clip-path");
        if (reference is null) return frame;
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
        string id = ParseClipId(reference);
        if (state.ObjectClips.TryGetValue(id, out SvgClipBox fractions))
        {
            SvgClipBox resolved = new(placement.X + fractions.X * placement.Width, placement.Y + fractions.Y * placement.Height, fractions.Width * placement.Width, fractions.Height * placement.Height);
            SvgClipBox clip = frame is null ? resolved : frame.Value.Clip.Intersect(resolved);
            double offsetX = frame?.OffsetX ?? 0;
            double offsetY = frame?.OffsetY ?? 0;
            double scaleX = frame?.ScaleX ?? 1;
            double scaleY = frame?.ScaleY ?? 1;
            return new SvgViewportFrame(offsetX, offsetY, scaleX, scaleY, frame?.ViewportWidth, frame?.ViewportHeight, clip, reader.Depth);
        }
        return ResolveLeafClip(reader, state);
    }
    /// <summary>Reports the unknown clip-path reference of one direct leaf for end-of-document resolution, or nothing when absent or already defined.</summary>
    internal static string? PendingClipId(XmlReader reader, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("clip-path");
        if (reference is null) return null;
        string id = ParseClipId(reference);
        if (state.Clips.TryGetValue(id, out _) || state.ObjectClips.TryGetValue(id, out _)) return null;
        return state.Strings.Copy(id);
    }

    /// <summary>Applies deferred userSpace clip paths to direct leaf geometry ranges at document end.</summary>
    internal static void ResolvePendingLeafClips(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        for (int pendingIndex = 0; pendingIndex < state.PendingLeafClips.Count; pendingIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingLeafClip pending = state.PendingLeafClips[pendingIndex];
            SvgViewportFrame? parent = pending.Parent;
            if (parent is not null && parent.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
            SvgClipBox clip;
            if (state.Clips.TryGetValue(pending.ClipId, out SvgClipBox box))
            {
                clip = parent is null ? box : parent.Value.Clip.Intersect(box);
            }
            else if (state.ObjectClips.TryGetValue(pending.ClipId, out SvgClipBox fractions))
            {
                if (!pending.AdmitsBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths are only implemented on rectangles, bands and straight contours.");
                if (pending.GeometryCount == 0) continue;
                clip = BoundingBoxClip(state, pending, fractions, parent, workCounter, cancellationToken);
            }
            else if (state.ClipPolygons.TryGetValue(pending.ClipId, out SvgClipPolygon clipPolygon))
            {
                if (!pending.SubjectMapIsIdentity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Polygon clip-paths with transformed content are not implemented.");
                if (parent is not null && !parent.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Polygon clip-paths in transformed viewports are not implemented.");
                if (state.GroupUseExpanded) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Polygon clip-paths with use instances are not implemented.");
                for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
                {
                    workCounter.Consume(cancellationToken);
                    state.Scene.ReplaceGeometry(geometryIndex, ApplyPolygonClip(state.Scene.GeometryAt(geometryIndex), clipPolygon, state.Budget, workCounter, cancellationToken));
                }
                continue;
            }
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
            for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
            {
                workCounter.Consume(cancellationToken);
                state.Scene.ReplaceGeometry(geometryIndex, ApplyLeafClip(state.Scene.GeometryAt(geometryIndex), clip));
            }
        }

        static SvgClipBox BoundingBoxClip(SvgDocumentState state, PendingLeafClip pending, SvgClipBox fractions, SvgViewportFrame? parent, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgRectangle bounds = SvgMaskSyntax.BoundingBox(state.Scene.GeometryAt(geometryIndex));
                if (bounds.X < left) left = bounds.X;
                if (bounds.Y < top) top = bounds.Y;
                if (bounds.X + bounds.Width > right) right = bounds.X + bounds.Width;
                if (bounds.Y + bounds.Height > bottom) bottom = bounds.Y + bounds.Height;
            }
            SvgClipBox resolved = new(left + fractions.X * (right - left), top + fractions.Y * (bottom - top), fractions.Width * (right - left), fractions.Height * (bottom - top));
            return parent is null ? resolved : parent.Value.Clip.Intersect(resolved);
        }

        static SvgGeometry ApplyPolygonClip(SvgGeometry geometry, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(budget);
            ArgumentNullException.ThrowIfNull(workCounter);
            return geometry.Kind switch
            {
                SvgGeometryKind.PolygonFill => ClipSolidPolygon(geometry.Polygon, clip, budget, workCounter, cancellationToken),
                SvgGeometryKind.GradientPolygon => ClipGradientPolygon(geometry.GradientPolygon, clip, budget, workCounter, cancellationToken),
                SvgGeometryKind.RadialPolygon => ClipRadialPolygon(geometry.RadialPolygon, clip, budget, workCounter, cancellationToken),
                SvgGeometryKind.PatternPolygon => ClipPatternPolygon(geometry.PatternPolygon, clip, budget, workCounter, cancellationToken),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Polygon clip-paths on this geometry are not implemented."),
            };

            static SvgGeometry ClipSolidPolygon(SvgPolygonFill polygon, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                (OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<int> signs) = IntersectSubjectContours(polygon.Points, polygon.ContourEnds, polygon.Signs, clip.Points, budget, workCounter, cancellationToken);
                try
                {
                    SvgPolygonFill clipped = SvgPolygonFill.Create(points.Items, ends.Items, polygon.Signs is null ? null : signs.Items, polygon.Color, polygon.HasFill, polygon.IsEvenOdd, polygon.Mapping, polygon.Clip, null, budget);
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    return SvgGeometry.FromPolygonFill(clipped);
                }
                catch
                {
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    throw;
                }
            }

            static SvgGeometry ClipGradientPolygon(SvgGradientPolygon polygon, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                (OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<int> signs) = IntersectSubjectContours(polygon.Points, polygon.ContourEnds, polygon.Signs, clip.Points, budget, workCounter, cancellationToken);
                try
                {
                    SvgGradientPolygon clipped = SvgGradientPolygon.Create(points.Items, ends.Items, polygon.Signs is null ? null : signs.Items, polygon.Gradient, polygon.Box, polygon.HasFill, polygon.IsEvenOdd, polygon.Opacity, polygon.Mapping, polygon.Clip, null, budget);
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    return SvgGeometry.FromGradientPolygon(clipped);
                }
                catch
                {
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    throw;
                }
            }

            static SvgGeometry ClipRadialPolygon(SvgRadialPolygon polygon, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                (OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<int> signs) = IntersectSubjectContours(polygon.Points, polygon.ContourEnds, polygon.Signs, clip.Points, budget, workCounter, cancellationToken);
                try
                {
                    SvgRadialPolygon clipped = SvgRadialPolygon.Create(points.Items, ends.Items, polygon.Signs is null ? null : signs.Items, polygon.Gradient, polygon.Box, polygon.HasFill, polygon.IsEvenOdd, polygon.Opacity, polygon.Mapping, polygon.Clip, null, budget);
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    return SvgGeometry.FromRadialPolygon(clipped);
                }
                catch
                {
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    throw;
                }
            }

            static SvgGeometry ClipPatternPolygon(SvgPatternPolygon polygon, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                (OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<int> signs) = IntersectSubjectContours(polygon.Points, polygon.ContourEnds, polygon.Signs, clip.Points, budget, workCounter, cancellationToken);
                try
                {
                    SvgPatternPolygon clipped = SvgPatternPolygon.Create(points.Items, ends.Items, polygon.Signs is null ? null : signs.Items, polygon.Content, polygon.OriginX, polygon.OriginY, polygon.TileWidth, polygon.TileHeight, polygon.HasFill, polygon.IsEvenOdd, polygon.Opacity, polygon.Mapping, polygon.Clip, budget);
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    return SvgGeometry.FromPatternPolygon(clipped);
                }
                catch
                {
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    throw;
                }
            }
        }

        static (OwnedParserArray<SvgDevicePoint> Points, OwnedParserArray<int> Ends, OwnedParserArray<int> Signs) IntersectSubjectContours(SvgDevicePoint[] points, int[] ends, int[]? signs, SvgDevicePoint[] clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(points);
            ArgumentNullException.ThrowIfNull(ends);
            ArgumentNullException.ThrowIfNull(clip);
            ArgumentNullException.ThrowIfNull(budget);
            ArgumentNullException.ThrowIfNull(workCounter);
            using BudgetedParserList<SvgDevicePoint> kept = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<int> keptEnds = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<int> keptSigns = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<SvgDevicePoint> ping = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<SvgDevicePoint> pong = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            bool clipCounterClockwise = ClipPolygonArea(clip, workCounter, cancellationToken) > 0;
            int start = 0;
            for (int contour = 0; contour < ends.Length; contour++)
            {
                workCounter.Consume(cancellationToken);
                int end = ends[contour];
                ping.Clear();
                for (int index = start; index < end; index++)
                {
                    workCounter.Consume(cancellationToken);
                    SvgDevicePoint vertex = points[index];
                    if (ping.Count > 0 && ping[ping.Count - 1] == vertex) continue;
                    ping.Add(vertex);
                }
                if (ping.Count > 1 && ping[0] == ping[ping.Count - 1]) ping.RemoveLast();
                if (ping.Count >= 3) ClipRingToConvexPolygon(ping, pong, clip, clipCounterClockwise, workCounter, cancellationToken);
                else ping.Clear();
                int keptBefore = kept.Count;
                for (int index = 0; index < ping.Count; index++)
                {
                    workCounter.Consume(cancellationToken);
                    SvgDevicePoint vertex = ping[index];
                    if (kept.Count > keptBefore && kept[kept.Count - 1] == vertex) continue;
                    kept.Add(vertex);
                }
                if (kept.Count > keptBefore && kept[keptBefore] == kept[kept.Count - 1]) kept.RemoveLast();
                if (kept.Count - keptBefore >= 3)
                {
                    keptEnds.Add(kept.Count);
                    if (signs is not null) keptSigns.Add(contour < signs.Length ? signs[contour] : 0);
                }
                else while (kept.Count > keptBefore) kept.RemoveLast();
                start = end;
            }
            return (kept.Detach(), keptEnds.Detach(), keptSigns.Detach());

            static double ClipPolygonArea(SvgDevicePoint[] ring, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                double twiceArea = 0;
                for (int index = 0; index < ring.Length; index++)
                {
                    if ((index & 1023) == 0) workCounter.Consume(cancellationToken);
                    SvgDevicePoint current = ring[index];
                    SvgDevicePoint next = ring[(index + 1) % ring.Length];
                    twiceArea += (current.X * next.Y) - (next.X * current.Y);
                }
                return twiceArea / 2;
            }

            static void ClipRingToConvexPolygon(BudgetedParserList<SvgDevicePoint> ping, BudgetedParserList<SvgDevicePoint> pong, SvgDevicePoint[] clip, bool clipCounterClockwise, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                bool outputInPing = true;
                for (int edge = 0; edge < clip.Length; edge++)
                {
                    workCounter.Consume(cancellationToken);
                    SvgDevicePoint a = clip[edge];
                    SvgDevicePoint b = clip[(edge + 1) % clip.Length];
                    BudgetedParserList<SvgDevicePoint> source = outputInPing ? ping : pong;
                    BudgetedParserList<SvgDevicePoint> target = outputInPing ? pong : ping;
                    target.Clear();
                    if (source.Count == 0)
                    {
                        ping.Clear();
                        return;
                    }
                    SvgDevicePoint s = source[source.Count - 1];
                    bool sInside = IsInsideClipHalfPlane(s, a, b, clipCounterClockwise);
                    for (int index = 0; index < source.Count; index++)
                    {
                        workCounter.Consume(cancellationToken);
                        SvgDevicePoint e = source[index];
                        bool eInside = IsInsideClipHalfPlane(e, a, b, clipCounterClockwise);
                        if (eInside)
                        {
                            if (!sInside) target.Add(ClipEdgeIntersection(s, e, a, b, workCounter, cancellationToken));
                            target.Add(e);
                        }
                        else if (sInside) target.Add(ClipEdgeIntersection(s, e, a, b, workCounter, cancellationToken));
                        s = e;
                        sInside = eInside;
                    }
                    outputInPing = !outputInPing;
                }
                if (outputInPing) return;
                ping.Clear();
                for (int index = 0; index < pong.Count; index++)
                {
                    workCounter.Consume(cancellationToken);
                    ping.Add(pong[index]);
                }
            }

            static bool IsInsideClipHalfPlane(SvgDevicePoint point, SvgDevicePoint a, SvgDevicePoint b, bool clipCounterClockwise)
            {
                double cross = ((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X));
                return clipCounterClockwise ? cross >= 0 : cross <= 0;
            }

            static SvgDevicePoint ClipEdgeIntersection(SvgDevicePoint s, SvgDevicePoint e, SvgDevicePoint a, SvgDevicePoint b, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                workCounter.Consume(cancellationToken);
                double dx = e.X - s.X;
                double dy = e.Y - s.Y;
                double ex = b.X - a.X;
                double ey = b.Y - a.Y;
                double denominator = (dx * ey) - (dy * ex);
                if (denominator == 0) return e;
                double t = ((a.X - s.X) * ey - (a.Y - s.Y) * ex) / denominator;
                return new SvgDevicePoint(s.X + (t * dx), s.Y + (t * dy));
            }
        }
    }
    /// <summary>Applies deferred userSpace clip paths to direct group command ranges at document end, after use expansion.</summary>
    internal static void ResolvePendingGroupClips(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        for (int pendingIndex = 0; pendingIndex < state.PendingGroupClips.Count; pendingIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingGroupClip pending = state.PendingGroupClips[pendingIndex];
            SvgViewportFrame? parent = pending.Parent;
            if (parent is not null && parent.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
            if (pending.ScaledPushesAtClose != pending.ScaledPushesAtOpen) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
            SvgClipBox clip;
            if (state.Clips.TryGetValue(pending.ClipId, out SvgClipBox box))
            {
                clip = parent is null ? box : parent.Value.Clip.Intersect(box);
            }
            else if (state.ObjectClips.TryGetValue(pending.ClipId, out _))
            {
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Forward bounding-box clip-path definitions on groups are not implemented.");
            }
            else if (state.ClipPolygons.TryGetValue(pending.ClipId, out _))
            {
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Polygon clip-paths on groups are not implemented.");
            }
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
            for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
                if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                state.Scene.ReplaceGeometry(command.GeometryIndex, ApplyLeafClip(state.Scene.GeometryAt(command.GeometryIndex), clip));
            }
        }
    }

    private static SvgGeometry ApplyLeafClip(SvgGeometry geometry, SvgClipBox clip)
    {
        if (geometry.Kind == SvgGeometryKind.Rectangle)
        {
            SvgRectangle rectangle = geometry.Rectangle;
            if (rectangle.Stroke is null) return SvgGeometry.FromRectangle(clip.ClipRectangle(rectangle));
            double halfWidth = rectangle.Stroke.Value.Width / 2d;
            if (!clip.Contains(rectangle.X - halfWidth, rectangle.Y - halfWidth, rectangle.X + rectangle.Width + halfWidth, rectangle.Y + rectangle.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a clip-path boundary are not implemented.");
            return geometry;
        }
        return geometry.Kind switch
        {
            SvgGeometryKind.OrthogonalBand => SvgGeometry.FromOrthogonalBand(clip.ClipRectangle(geometry.Rectangle)),
            SvgGeometryKind.AxisAlignedLine => clip.ClipLine(geometry.AxisAlignedLine),
            SvgGeometryKind.PolygonFill => SvgGeometry.FromPolygonFill(geometry.Polygon.Stamped(255, clip)),
            SvgGeometryKind.GradientPolygon => SvgGeometry.FromGradientPolygon(geometry.GradientPolygon.Stamped(255, clip)),
            SvgGeometryKind.RadialPolygon => SvgGeometry.FromRadialPolygon(geometry.RadialPolygon.Stamped(255, clip)),
            SvgGeometryKind.PatternPolygon => SvgGeometry.FromPatternPolygon(geometry.PatternPolygon.Stamped(255, clip)),
            SvgGeometryKind.GradientFill => SvgGeometry.FromGradientFill(geometry.Gradient.ClipTo(clip)),
            SvgGeometryKind.RadialGradient => SvgGeometry.FromRadialFill(geometry.Radial.ClipTo(clip)),
            SvgGeometryKind.PatternFill => SvgGeometry.FromPatternFill(geometry.Pattern.ClipTo(clip)),
            SvgGeometryKind.GradientStroke => SvgGeometry.FromGradientStroke(geometry.GradientStroke.ClipTo(clip)),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Forward clip-path definitions on this geometry are not implemented."),
        };
    }

    /// <summary>Resolves the effective clip for one rendered leaf or use instance from the viewport frame and an optional clip-path attribute.</summary>
    internal static SvgViewportFrame? ResolveLeafClip(XmlReader reader, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("clip-path");
        if (reference is null) return state.EffectiveFrame;
        string clipId = ParseClipId(reference);
        if (state.DeferringLeafClip && !state.Clips.TryGetValue(clipId, out _) && !state.ObjectClips.TryGetValue(clipId, out _)) return state.EffectiveFrame;
        if (state.EffectiveFrame is not null && state.EffectiveFrame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
        if (state.ObjectClips.TryGetValue(clipId, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths are only implemented on rectangles, bands and straight contours.");
        if (!state.Clips.TryGetValue(clipId, out SvgClipBox box)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
        SvgViewportFrame? parent = state.EffectiveFrame;
        SvgClipBox clip = parent is null ? box : parent.Value.Clip.Intersect(box);
        double offsetX = parent?.OffsetX ?? 0;
        double offsetY = parent?.OffsetY ?? 0;
        double scaleX = parent?.ScaleX ?? 1;
        double scaleY = parent?.ScaleY ?? 1;
        return new SvgViewportFrame(offsetX, offsetY, scaleX, scaleY, parent?.ViewportWidth, parent?.ViewportHeight, clip, reader.Depth);
    }
}