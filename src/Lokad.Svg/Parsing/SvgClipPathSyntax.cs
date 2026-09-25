using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one direct leaf geometry range whose userSpace clip-path reference resolves at document end.</summary>
internal readonly record struct PendingLeafClip(int FirstGeometryIndex, int GeometryCount, string ClipId, SvgViewportFrame? Parent, bool AdmitsBoundingBox);

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