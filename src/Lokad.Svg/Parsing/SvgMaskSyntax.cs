using System.Xml;
using System.Globalization;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one direct leaf geometry range whose mask reference resolves at document end.</summary>
internal readonly record struct PendingLeafMask(int FirstGeometryIndex, int GeometryCount, string MaskId, SvgViewportFrame? Parent, SvgLeafMaskOperation Operation);

/// <summary>Retains one masked group command range whose mask reference resolves at document end.</summary>
/// <remarks>Uniform and spatial group masks apply after child composition, independently of definition order.</remarks>
internal readonly record struct PendingGroupMask(string MaskId, byte Opacity, int FirstCommandIndex, int EndCommandIndex, int Depth, SvgViewportFrame? Parent);

/// <summary>Parses bounded mask definitions and retains leaf values or isolated group operations.</summary>
internal static class SvgMaskSyntax
{
    /// <summary>Parses an explicit userSpaceOnUse mask region, returning null for the default region.</summary>
    internal static SvgClipBox? ParseMaskRegion(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? units = reader.GetAttribute("maskUnits");
        string? contentUnits = reader.GetAttribute("maskContentUnits");
        if (contentUnits is not null && !contentUnits.Equals("userSpaceOnUse", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse mask content units are implemented.");
        string? x = reader.GetAttribute("x");
        string? y = reader.GetAttribute("y");
        string? width = reader.GetAttribute("width");
        string? height = reader.GetAttribute("height");
        bool isUserSpace = units is not null && units.Equals("userSpaceOnUse", StringComparison.Ordinal);
        bool isBoundingBox = units is null || units.Equals("objectBoundingBox", StringComparison.Ordinal);
        if (!isUserSpace && !isBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse and objectBoundingBox mask units are implemented.");
        if (x is null && y is null && width is null && height is null) return null;
        if (!isUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox mask regions are not implemented.");
        if (x is null || y is null || width is null || height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Default mask regions with userSpaceOnUse units are not implemented.");
        double regionX = SvgGeometrySyntax.ParseRectangleCoordinate(x);
        double regionY = SvgGeometrySyntax.ParseRectangleCoordinate(y);
        double regionWidth = SvgGeometrySyntax.ParseRectangleCoordinate(width);
        double regionHeight = SvgGeometrySyntax.ParseRectangleCoordinate(height);
        if (regionWidth < 0 || regionHeight < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Mask region dimensions cannot be negative.");
        return new SvgClipBox(regionX, regionY, regionWidth, regionHeight);
    }

    /// <summary>Parses explicit objectBoundingBox mask region fractions with specification defaults, returning null unless bounding-box units carry explicit attributes.</summary>
    internal static SvgClipBox? ParseMaskFractionRegion(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? units = reader.GetAttribute("maskUnits");
        if (units is not null && !units.Equals("objectBoundingBox", StringComparison.Ordinal)) return null;
        string? x = reader.GetAttribute("x");
        string? y = reader.GetAttribute("y");
        string? width = reader.GetAttribute("width");
        string? height = reader.GetAttribute("height");
        if (x is null && y is null && width is null && height is null) return null;
        double regionX = x is null ? -0.1 : ParseMaskFraction(x);
        double regionY = y is null ? -0.1 : ParseMaskFraction(y);
        double regionWidth = width is null ? 1.2 : ParseMaskFraction(width);
        double regionHeight = height is null ? 1.2 : ParseMaskFraction(height);
        if (regionWidth < 0 || regionHeight < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Mask region dimensions cannot be negative.");
        return new SvgClipBox(regionX, regionY, regionWidth, regionHeight);
    }

    private static double ParseMaskFraction(string value)
    {
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Mask region values exceed the configured syntax limit.");
        if (token.Length == 0 || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number mask region fractions are implemented.");
        return result;
    }
    internal static bool ParseMaskType(string? value)
    {
        if (value is null || value.Equals("luminance", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Equals("alpha", StringComparison.OrdinalIgnoreCase)) return true;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only luminance and alpha mask types are implemented.");
    }

    internal static string ParseMaskId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A mask reference is required.");
        if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local mask references are implemented.");
        return id;
    }

    /// <summary>Retains one direct-leaf mask reference for composition after geometry and clip resolution.</summary>
    internal static string? PendingMaskId(XmlReader reader, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("mask");
        if (reference is null) return null;
        string id = ParseMaskId(reference);
        return state.Strings.Copy(id);
    }

    /// <summary>Interprets one opaque mask paint value under its mask type.</summary>
    internal static byte InterpretValue(SvgColor color, bool isAlpha)
    {
        if (isAlpha) return color.Alpha;
        if (color.Red != color.Green || color.Red != color.Blue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only grayscale luminance mask content is implemented.");
        return SvgReferenceResolver.MultiplyAlpha(color.Red, color.Alpha);
    }

    /// <summary>Builds the default mask region from one masked fill bounding box.</summary>
    internal static SvgClipBox MaskRegion(SvgRectangle box)
        => new(box.X - box.Width / 10, box.Y - box.Height / 10, box.Width * 1.2, box.Height * 1.2);

    /// <summary>Resolves direct-leaf effect values and validates retained regions after clip resolution; paint remains unchanged.</summary>
    internal static void ResolvePendingLeafMasks(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        for (int pendingIndex = 0; pendingIndex < state.PendingLeafMasks.Count; pendingIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingLeafMask pending = state.PendingLeafMasks[pendingIndex];
            if (!state.Masks.TryGetValue(pending.MaskId, out byte value))
            {
                if (state.MaskContents.TryGetValue(pending.MaskId, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Spatial mask content on masked leaves is not implemented.");
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
            }
            pending.Operation.Value = value;
            pending.Operation.Resolved = true;
            if (pending.GeometryCount == 0) continue;
            SvgRectangle fallback = UnionBounds(state, pending, workCounter, cancellationToken);
            if (state.MaskContentBoxes.TryGetValue(pending.MaskId, out SvgClipBox content) && !content.Contains(fallback.X, fallback.Y, fallback.X + fallback.Width, fallback.Y + fallback.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering the masked geometry is implemented.");
            SvgClipBox region = ResolveDeferredRegion(state, pending, fallback);
            pending.Operation.Region = state.MaskRegions.TryGetValue(pending.MaskId, out _) || state.MaskFractionRegions.TryGetValue(pending.MaskId, out _) ? region : null;
            for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgLeafMaskComposition.ValidateRegion(state.Scene.GeometryAt(geometryIndex), region);
            }
        }

        static SvgRectangle UnionBounds(SvgDocumentState state, PendingLeafMask pending, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgRectangle bounds = BoundingBox(state.Scene.GeometryAt(geometryIndex));
                if (bounds.X < left) left = bounds.X;
                if (bounds.Y < top) top = bounds.Y;
                if (bounds.X + bounds.Width > right) right = bounds.X + bounds.Width;
                if (bounds.Y + bounds.Height > bottom) bottom = bounds.Y + bounds.Height;
            }
            return new SvgRectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, false, null);
        }

        static SvgClipBox ResolveDeferredRegion(SvgDocumentState state, PendingLeafMask pending, SvgRectangle fallback)
        {
            SvgViewportFrame? parent = pending.Parent;
            if (state.MaskRegions.TryGetValue(pending.MaskId, out SvgClipBox custom))
            {
                if (parent is not null && !parent.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions inside viewports are not implemented.");
                return custom;
            }
            if (state.MaskFractionRegions.TryGetValue(pending.MaskId, out SvgClipBox fractions))
            {
                if (parent is not null && !parent.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions inside viewports are not implemented.");
                SvgClipBox resolved = new(fallback.X + fractions.X * fallback.Width, fallback.Y + fractions.Y * fallback.Height, fractions.Width * fallback.Width, fractions.Height * fallback.Height);
                if (state.MaskContentBoxes.TryGetValue(pending.MaskId, out SvgClipBox cover) && !cover.Contains(resolved.X, resolved.Y, resolved.X + resolved.Width, resolved.Y + resolved.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering its custom region is implemented.");
                return resolved;
            }
            return MaskRegion(fallback);
        }
    }
    /// <summary>Requires retained mask content to cover the masked geometry bounding box.</summary>
    internal static void ValidateMaskCoverage(string reference, SvgDocumentState state, SvgRectangle fallback)
    {
        if (state.MaskContentBoxes.TryGetValue(ParseMaskId(reference), out SvgClipBox content) && !content.Contains(fallback.X, fallback.Y, fallback.X + fallback.Width, fallback.Y + fallback.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering the masked geometry is implemented.");
    }

    /// <summary>Requires every open masked-group scope to cover one lowered child, including stroked extents.</summary>
    internal static void ValidateMaskScopes(SvgDocumentState state, SvgGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.MaskScopes.Count == 0) return;
        SvgRectangle box = CoverageBox(geometry);
        for (int index = 0; index < state.MaskScopes.Count; index++)
        {
            if (!state.MaskScopes[index].Contains(box.X, box.Y, box.X + box.Width, box.Y + box.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering masked group children is implemented.");
        }
    }

    /// <summary>Requires every snapshotted masked-group scope to cover one deferred instance box.</summary>
    internal static void ValidateMaskScopeSnapshot(SvgDocumentState state, int start, int count, SvgRectangle box)
    {
        ArgumentNullException.ThrowIfNull(state);
        for (int index = start; index < start + count; index++)
        {
            if (!state.MaskScopeSnapshots[index].Contains(box.X, box.Y, box.X + box.Width, box.Y + box.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering masked group children is implemented.");
        }
    }

    /// <summary>Requires every open masked-group scope to cover each lowered band.</summary>
    internal static void ValidateMaskScopeBands(SvgDocumentState state, SvgGeometry[] geometries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(geometries);
        if (state.MaskScopes.Count == 0) return;
        foreach (SvgGeometry geometry in geometries) ValidateMaskScopes(state, geometry);
    }

    /// <summary>Builds the coverage box for one lowered geometry, expanding stroked rectangles by their half width.</summary>
    private static SvgRectangle CoverageBox(SvgGeometry geometry)
    {
        SvgRectangle box = BoundingBox(geometry);
        if (box.Stroke is SvgStroke stroke)
        {
            double halfWidth = stroke.Width / 2d;
            return box with { X = box.X - halfWidth, Y = box.Y - halfWidth, Width = box.Width + stroke.Width, Height = box.Height + stroke.Width };
        }
        return box;
    }

    /// <summary>Reports whether the referenced mask carries an explicit region.</summary>
    internal static bool HasCustomRegion(XmlReader reader, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("mask");
        return reference is not null && (state.MaskRegions.TryGetValue(ParseMaskId(reference), out _) || state.MaskFractionRegions.TryGetValue(ParseMaskId(reference), out _));
    }

    /// <summary>Resolves deferred group masks at document end, rewriting or inserting isolation scopes after use expansion.</summary>
    internal static void ResolvePendingGroupMasks(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        for (int pendingIndex = 0; pendingIndex < state.PendingGroupMasks.Count; pendingIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingGroupMask pending = state.PendingGroupMasks[pendingIndex];
            bool hasExplicitRegion = state.MaskRegions.TryGetValue(pending.MaskId, out SvgClipBox customRegion);
            SvgClipBox fractionRegion = default;
            bool hasFractionRegion = !hasExplicitRegion && state.MaskFractionRegions.TryGetValue(pending.MaskId, out fractionRegion);
            SvgClipBox? maskRegion = null;
            if (hasExplicitRegion || hasFractionRegion) maskRegion = ResolveGroupMaskRegion(state, pending, hasExplicitRegion, customRegion, fractionRegion, workCounter, cancellationToken);
            if (!state.Masks.TryGetValue(pending.MaskId, out byte value))
            {
                if (!state.MaskContents.TryGetValue(pending.MaskId, out SvgMaskContent content)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
                if (pending.Parent is not null && !pending.Parent.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Spatial mask content in transformed viewports is not implemented.");
                ValidateNoGlyphText(state, pending, workCounter, cancellationToken);
                int contentIndex = state.Scene.AddMaskContent(content);
                if (pending.Opacity != 255) RewriteGroupMaskScope(state, pending, pending.Opacity, 255, maskRegion, contentIndex);
                else InsertGroupMaskScope(state, pending, pending.Opacity, 255, maskRegion, contentIndex, workCounter, cancellationToken);
                continue;
            }
            if (state.MaskContentBoxes.TryGetValue(pending.MaskId, out SvgClipBox contentBox)) ValidateGroupMaskCoverage(state, pending, contentBox, workCounter, cancellationToken);
            if (pending.Opacity != 255) RewriteGroupMaskScope(state, pending, pending.Opacity, value, maskRegion, -1);
            // Mask the composed children. Baking this value into every paint changes
            // overlap alpha and coverage/color rounding, even for one opaque child.
            else if (value != 255 || maskRegion is not null) InsertGroupMaskScope(state, pending, pending.Opacity, value, maskRegion, -1, workCounter, cancellationToken);
            }

        static void ValidateNoGlyphText(SvgDocumentState state, PendingGroupMask pending, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
                if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                if (state.Scene.GeometryAt(command.GeometryIndex).Kind == SvgGeometryKind.GlyphContours) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text inside masked groups is not implemented.");
            }
        }

        static void ValidateGroupMaskCoverage(SvgDocumentState state, PendingGroupMask pending, SvgClipBox content, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
                if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                SvgGeometry geometry = state.Scene.GeometryAt(command.GeometryIndex);
                if (geometry.Kind == SvgGeometryKind.GlyphContours) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text inside masked groups is not implemented.");
                SvgRectangle box = CoverageBox(geometry);
                if (!content.Contains(box.X, box.Y, box.X + box.Width, box.Y + box.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering masked group children is implemented.");
            }
        }

        static void RewriteGroupMaskScope(SvgDocumentState state, PendingGroupMask pending, byte opacity, byte maskAlpha, SvgClipBox? maskRegion, int maskContentIndex)
        {
            SvgDrawCommand scope = state.Scene.CommandAt(pending.FirstCommandIndex);
            if (scope.Kind != SvgDrawCommandKind.BeginGroup) throw new InvalidOperationException("Deferred group mask resolution requires its isolation scope.");
            state.Scene.ReplaceCommand(pending.FirstCommandIndex, new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, opacity, maskRegion, maskContentIndex, maskAlpha));
        }

        static void InsertGroupMaskScope(SvgDocumentState state, PendingGroupMask pending, byte opacity, byte maskAlpha, SvgClipBox? maskRegion, int maskContentIndex, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            // A validated empty range has no pixels to isolate and shares its boundary
            // with following siblings. It must not acquire their commands.
            if (pending.FirstCommandIndex == pending.EndCommandIndex) return;
            for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
            {
                workCounter.Consume(cancellationToken);
                if ((opacity != 255 || maskAlpha != 255) && state.Scene.CommandAt(commandIndex).Kind == SvgDrawCommandKind.BeginGroup) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested group opacity requires separate compositing calibration.");
            }
            state.Scene.InsertCommands(pending.EndCommandIndex, [new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255)]);
            SvgReferenceResolver.ShiftPendingCommandRanges(state, pending.EndCommandIndex, 1, true, workCounter, cancellationToken);
            state.Scene.InsertCommands(pending.FirstCommandIndex, [new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, opacity, maskRegion, maskContentIndex, maskAlpha)]);
            SvgReferenceResolver.ShiftPendingCommandRanges(state, pending.FirstCommandIndex, 1, false, workCounter, cancellationToken);
        }

    }
    /// <summary>Resolves a custom group-mask region into root-user space for scope compositing.</summary>
    /// <remarks>Explicit boxes map through the captured ancestor frame (axis scales and offsets); bounding-box fractions resolve against the masked children union like direct leaves. Glyph rules stay separate; empty groups resolve degenerate regions that composite nothing.</remarks>
    private static SvgClipBox ResolveGroupMaskRegion(SvgDocumentState state, PendingGroupMask pending, bool hasExplicitRegion, SvgClipBox customRegion, SvgClipBox fractionRegion, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        SvgClipBox region;
        if (hasExplicitRegion) region = customRegion;
        else
        {
            SvgRectangle union = GroupChildrenUnion(state, pending, workCounter, cancellationToken);
            region = new SvgClipBox(union.X + fractionRegion.X * union.Width, union.Y + fractionRegion.Y * union.Height, fractionRegion.Width * union.Width, fractionRegion.Height * union.Height);
        }
        if (pending.Parent is null) return region;
        SvgViewportFrame parent = pending.Parent.Value;
        double x = region.X * parent.ScaleX + parent.OffsetX;
        double y = region.Y * parent.ScaleY + parent.OffsetY;
        double width = region.Width * parent.ScaleX;
        double height = region.Height * parent.ScaleY;
        if (width < 0) { x += width; width = -width; }
        if (height < 0) { y += height; height = -height; }
        return new SvgClipBox(x, y, width, height);
    }

    /// <summary>Unions coverage boxes over one masked group command range for bounding-box region resolution.</summary>
    private static SvgRectangle GroupChildrenUnion(SvgDocumentState state, PendingGroupMask pending, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        double left = double.PositiveInfinity;
        double top = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double bottom = double.NegativeInfinity;
        for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
        {
            workCounter.Consume(cancellationToken);
            SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
            if (command.Kind != SvgDrawCommandKind.Geometry) continue;
            SvgGeometry geometry = state.Scene.GeometryAt(command.GeometryIndex);
            if (geometry.Kind == SvgGeometryKind.GlyphContours) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text inside masked groups is not implemented.");
            SvgRectangle box = CoverageBox(geometry);
            if (box.X < left) left = box.X;
            if (box.Y < top) top = box.Y;
            if (box.X + box.Width > right) right = box.X + box.Width;
            if (box.Y + box.Height > bottom) bottom = box.Y + box.Height;
        }
        return new SvgRectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, false, null);
    }

    internal static SvgRectangle BoundingBox(SvgGeometry geometry)
    {
        return geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => geometry.Rectangle,
            SvgGeometryKind.AxisAlignedLine => geometry.AxisAlignedLine.ToStrokeBand(),
            SvgGeometryKind.OrthogonalBand => geometry.Rectangle,
            SvgGeometryKind.PatternFill => geometry.Pattern.Fill,
            SvgGeometryKind.GradientFill => geometry.Gradient.Fill,
            SvgGeometryKind.RadialGradient => geometry.Radial.Fill,
            SvgGeometryKind.GradientStroke => geometry.GradientStroke.OuterBox(),
            SvgGeometryKind.PolygonFill => PolygonBounds(geometry.Polygon.Points, geometry.Polygon.Color, geometry.Polygon.HasFill),
            SvgGeometryKind.GradientPolygon => PolygonBounds(geometry.GradientPolygon.Points, default, geometry.GradientPolygon.HasFill),
            SvgGeometryKind.RadialPolygon => PolygonBounds(geometry.RadialPolygon.Points, default, geometry.RadialPolygon.HasFill),
            SvgGeometryKind.PatternPolygon => PolygonBounds(geometry.PatternPolygon.Points, default, geometry.PatternPolygon.HasFill),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on this geometry are not implemented.")
        };

        static SvgRectangle PolygonBounds(SvgDevicePoint[] points, SvgColor color, bool hasFill)
        {
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            foreach (SvgDevicePoint vertex in points)
            {
                left = Math.Min(left, vertex.X);
                top = Math.Min(top, vertex.Y);
                right = Math.Max(right, vertex.X);
                bottom = Math.Max(bottom, vertex.Y);
            }
            return new SvgRectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), color, hasFill, null);
        }
    }

}
