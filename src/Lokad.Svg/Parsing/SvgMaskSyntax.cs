using System.Xml;
using System.Globalization;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one direct leaf geometry range whose mask reference resolves at document end.</summary>
/// <remarks>The conversion owns Scene through binding and emission; this range borrows its builder.</remarks>
internal readonly record struct PendingLeafMask(int FirstGeometryIndex, int GeometryCount, string MaskId, SvgViewportFrame? Parent, SvgAffineTransform Transform, SvgLeafMaskOperation Operation, SvgSceneBuilder Scene);

/// <summary>Retains one masked group command range whose mask reference resolves at document end.</summary>
/// <remarks>Uniform and spatial group masks apply after child composition, independently of definition order.
/// The conversion owns Scene through binding and emission; this range borrows its builder.
/// HasOwnFilter describes this target's effect, independently of filters within its command range.</remarks>
internal readonly record struct PendingGroupMask(string MaskId, int FirstCommandIndex, int EndCommandIndex, int Depth, SvgViewportFrame? Parent, SvgAffineTransform Transform, SvgSceneBuilder Scene, bool HasOwnFilter);

/// <summary>Parses bounded mask definitions and retains leaf values or isolated group operations.</summary>
internal static class SvgMaskSyntax
{
    internal static bool IsDisabled(string id, SvgDocumentState state)
        => state.MaskContents.TryGetValue(id, out SvgMaskContent content) && content.IsDisabled;

    /// <summary>Registers an identity reference without a content region; staged arrays release here and other snapshots remain document-owned.</summary>
    internal static void RegisterDisabled(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        string id = state.Strings.Copy(state.MaskId ?? throw new InvalidOperationException("Mask registration requires an identifier."));
        if (state.Masks.TryGetValue(id, out _) || !state.MaskContents.TryAdd(id, new([], 255, state.MaskIsAlpha) { IsDisabled = true })
            || !state.Masks.TryAdd(id, 255)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskContentGeometries is {} geometries)
        {
            foreach (SvgGeometry geometry in geometries) geometry.Dispose();
            state.MaskContentGeometries = null;
        }
    }

    /// <summary>Parses an explicit userSpaceOnUse mask region, returning null for the default region.</summary>
    internal static SvgClipBox? ParseMaskRegion(XmlReader reader, SvgViewportLengthBases bases)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? units = reader.GetAttribute("maskUnits");
        string? x = reader.GetAttribute("x");
        string? y = reader.GetAttribute("y");
        string? width = reader.GetAttribute("width");
        string? height = reader.GetAttribute("height");
        bool isUserSpace = units is not null && units.Equals("userSpaceOnUse", StringComparison.Ordinal);
        bool isBoundingBox = units is null || units.Equals("objectBoundingBox", StringComparison.Ordinal);
        if (!isUserSpace && !isBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse and objectBoundingBox mask units are implemented.");
        if (x is null && y is null && width is null && height is null) return null;
        if (!isUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox mask regions are not implemented.");
        double regionX = SvgGeometrySyntax.ParseViewportCoordinate(x ?? "-10%", bases.Width, SvgViewportCoordinateKind.Rectangle);
        double regionY = SvgGeometrySyntax.ParseViewportCoordinate(y ?? "-10%", bases.Height, SvgViewportCoordinateKind.Rectangle);
        double regionWidth = SvgGeometrySyntax.ParseViewportCoordinate(width ?? "120%", bases.Width, SvgViewportCoordinateKind.Rectangle);
        double regionHeight = SvgGeometrySyntax.ParseViewportCoordinate(height ?? "120%", bases.Height, SvgViewportCoordinateKind.Rectangle);
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
        ReadOnlySpan<char> token = value.AsSpan().Trim(SvgCssWhitespace.Characters);
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Mask region values exceed the configured syntax limit.");
        if (token.Length == 0 || !double.TryParse(token, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number mask region fractions are implemented.");
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

    /// <summary>Validates a direct-leaf mask reference and retains displayed targets for later composition.</summary>
    internal static string? PendingMaskId(XmlReader reader, SvgDocumentState state, SvgPaintableElementContext context)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        if (state.DeferringFilterMask) return null;
        string? reference = SvgLocalUrl.ReadReference(reader, "mask");
        if (reference is null) return null;
        string id = ParseMaskId(reference);
        SvgComputedStyle style = context.ResolveStyle(reader, reader.LocalName == "text");
        if (!context.InheritedPresentation.IsDisplayed || style.GetDisplay() == SvgDisplay.None
            || !HasPaintableTarget(reader, SvgPresentationResolver.ParseVisibility(style.Visibility, context.InheritedPresentation.IsVisible), style.Filter)) return null;
        return IsDisabled(id, state) ? null : state.Strings.Copy(id);
    }

    /// <summary>Excludes known nonpainting primitive declarations while preserving descendant visibility and effect output.</summary>
    /// <remarks>Only exact declared zero lengths are excluded. A 100-unit percentage base preserves the authored
    /// number independently of the live viewport; positive extents are never narrowed to guessed native bounds.
    /// Geometry parsing still validates and lowers the element under its actual viewport and shared budget.</remarks>
    internal static bool HasPaintableTarget(XmlReader reader, bool visible, string? ownFilter)
    {
        switch (reader.LocalName)
        {
            case "rect":
                if ((Dimension(reader, "width") ?? 0) == 0 || (Dimension(reader, "height") ?? 0) == 0) return false;
                break;
            case "circle":
                if ((Dimension(reader, "r") ?? 0) == 0) return false;
                break;
            case "ellipse":
                double? x = Dimension(reader, "rx"), y = Dimension(reader, "ry");
                if ((x ?? y ?? 0) == 0 || (y ?? x ?? 0) == 0) return false;
                break;
            case "image":
                if (string.IsNullOrEmpty(SvgLocalReferenceSyntax.ReadHref(reader)) || Dimension(reader, "width") == 0 || Dimension(reader, "height") == 0) return false;
                break;
            case "path":
                if (string.IsNullOrWhiteSpace(reader.GetAttribute("d"))) return false;
                break;
            case "line": case "polygon": case "polyline": break;
            default: return true;
        }
        return visible || SvgLocalUrl.GetReference(ownFilter) is not null;

        static double? Dimension(XmlReader element, string attribute)
            => element.GetAttribute(attribute) is {} value ? SvgGeometrySyntax.ParseViewportDimension(value, 100) : null;
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
            if (IsDisabled(pending.MaskId, state))
            {
                pending.Operation.Value = 255;
                pending.Operation.Resolved = true;
                continue;
            }
            SvgSceneBuilder? previous = state.DefinitionReplayScene;
            state.DefinitionReplayScene = pending.Scene;
            try
            {
                SvgMaskContent? spatialContent = null;
                SvgAffineTransform spatialMap = pending.Transform;
                if (pending.Parent is {} maskParent) spatialMap = SvgAffineTransform.FromScaleOffset(maskParent.ScaleX, maskParent.ScaleY, maskParent.OffsetX, maskParent.OffsetY).ComposeChecked(spatialMap);
                if (!state.Masks.TryGetValue(pending.MaskId, out byte value))
                {
                    if (!state.MaskContents.TryGetValue(pending.MaskId, out SvgMaskContent spatial)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
                    spatialContent = spatial;
                    value = 255;
                }
                if (!spatialMap.IsAxisAligned && state.MaskContents.TryGetValue(pending.MaskId, out SvgMaskContent retained))
                { spatialContent = retained; value = 255; }
                pending.Operation.Value = value;
                pending.Operation.Resolved = true;
                if (pending.GeometryCount == 0) continue;
                SvgRectangle fallback = UnionBounds(state, pending, workCounter, cancellationToken);
                SvgGeometry first = state.Scene.GeometryAt(pending.FirstGeometryIndex);
                SvgRectangle objectBox = state.Scene.GetRecordedPaintValue(first.RecordedPaint).ObjectBounds is not null
                    ? RecordedObjectBox(first, state, workCounter, cancellationToken) : fallback;
                SvgMaskTarget? nativeTarget = null;
                if (!spatialMap.IsAxisAligned || pending.Parent is { IsTrivial: false })
                {
                    SvgObjectBoundingClip collector = new(pending.Transform, pending.Parent);
                    for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                        collector.Add(state.Scene.GeometryAt(index), state, workCounter, cancellationToken);
                    nativeTarget = SvgMaskTarget.Create(pending.MaskId, spatialMap, collector.SourceBounds, state);
                }
                if (spatialContent is {} placedContent)
                    pending.Operation.ContentIndex = state.Scene.AddMaskContent(SvgMaskContentReplay.PlaceForTarget(pending.MaskId, placedContent, spatialMap, objectBox, nativeTarget, state, workCounter, cancellationToken));
                if (pending.Operation.ContentIndex < 0 && state.MaskContents.TryGetValue(pending.MaskId, out SvgMaskContent uniform)
                    && uniform.UniformSource is not null && state.MaskContentBoxes.TryGetValue(pending.MaskId, out SvgClipBox uniformBox))
                {
                    SvgAffineTransform map = pending.Transform;
                    if (pending.Parent is {} parent) map = SvgAffineTransform.FromScaleOffset(parent.ScaleX, parent.ScaleY, parent.OffsetX, parent.OffsetY).ComposeChecked(map);
                    bool covers = map.IsAxisAligned && map.Apply(uniformBox).Contains(fallback.X, fallback.Y, fallback.X + fallback.Width, fallback.Y + fallback.Height);
                    if (!covers)
                    {
                        SvgMaskContent spatial = SvgMaskContentReplay.ResolveUniformSource(pending.MaskId, map, state, workCounter, cancellationToken);
                        pending.Operation.ContentIndex = state.Scene.AddMaskContent(spatial);
                        pending.Operation.Value = 255;
                    }
                }
                if (pending.Operation.ContentIndex < 0 && !HasUniformSource(state, pending.MaskId) && state.MaskContentBoxes.TryGetValue(pending.MaskId, out SvgClipBox content) && !content.Contains(fallback.X, fallback.Y, fallback.X + fallback.Width, fallback.Y + fallback.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering the masked geometry is implemented.");
                SvgClipBox region = nativeTarget is {} native ? native.SceneBounds(workCounter, cancellationToken) : ResolveDeferredRegion(state, pending, objectBox);
                pending.Operation.Region = region;
                for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
                {
                    workCounter.Consume(cancellationToken);
                    SvgLeafMaskComposition.ValidateRegion(state.Scene.GeometryAt(geometryIndex), region);
                }
            }
            finally { state.DefinitionReplayScene = previous; }
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
                SvgRectangle bounds = ResourceBounds(state.Scene.GeometryAt(geometryIndex), workCounter, cancellationToken);
                if (bounds.X < left) left = bounds.X;
                if (bounds.Y < top) top = bounds.Y;
                if (bounds.X + bounds.Width > right) right = bounds.X + bounds.Width;
                if (bounds.Y + bounds.Height > bottom) bottom = bounds.Y + bounds.Height;
            }
            return new SvgRectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, false, null);
        }

        static SvgClipBox ResolveDeferredRegion(SvgDocumentState state, PendingLeafMask pending, SvgRectangle fallback)
        {
            if (state.MaskRegions.TryGetValue(pending.MaskId, out SvgClipBox custom))
            {
                return pending.Transform.Apply(custom);
            }
            if (state.MaskFractionRegions.TryGetValue(pending.MaskId, out SvgClipBox fractions))
            {
                SvgClipBox resolved = new(fallback.X + fractions.X * fallback.Width, fallback.Y + fractions.Y * fallback.Height, fractions.Width * fallback.Width, fractions.Height * fallback.Height);
                if (!HasUniformSource(state, pending.MaskId) && state.MaskContentBoxes.TryGetValue(pending.MaskId, out SvgClipBox cover) && !cover.Contains(resolved.X, resolved.Y, resolved.X + resolved.Width, resolved.Y + resolved.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering its custom region is implemented.");
                return resolved;
            }
            return MaskRegion(fallback);
        }
    }
    /// <summary>Requires retained mask content to cover the masked geometry bounding box.</summary>
    internal static void ValidateMaskCoverage(string reference, SvgDocumentState state, SvgRectangle fallback)
    {
        string id = ParseMaskId(reference);
        // Instance masks retain their existing admission until their own mapped
        // spatial binding is resolved; a source snapshot alone does not add coverage.
        if (state.MaskContentBoxes.TryGetValue(id, out SvgClipBox content) && !content.Contains(fallback.X, fallback.Y, fallback.X + fallback.Width, fallback.Y + fallback.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering the masked geometry is implemented.");
    }

    internal static bool HasUniformSource(SvgDocumentState state, string id)
        => state.MaskContents.TryGetValue(id, out SvgMaskContent content) && content.UniformSource is not null;

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

    /// <summary>Resolves deferred group masks at document end, binding each authored isolation scope after use expansion.</summary>
    internal static void ResolvePendingGroupMasks(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => ResolvePendingGroupMasks(state, 0, workCounter, cancellationToken);

    /// <summary>Binds an appended source-local suffix before its scene transfers to its parent.</summary>
    internal static void ResolvePendingGroupMasks(SvgDocumentState state, int first, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        for (int pendingIndex = first; pendingIndex < state.PendingGroupMasks.Count; pendingIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingGroupMask pending = state.PendingGroupMasks[pendingIndex];
            SvgSceneBuilder? previous = state.DefinitionReplayScene;
            state.DefinitionReplayScene = pending.Scene;
            try
            {
                if (IsDisabled(pending.MaskId, state))
                {
                    RewriteGroupMaskScope(state, pending, 255, null, -1);
                    continue;
                }
                bool scalar = state.Masks.TryGetValue(pending.MaskId, out byte value);
                if (!scalar && !state.MaskContents.TryGetValue(pending.MaskId, out _))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
                if (!pending.HasOwnFilter && !HasSource(state, pending, workCounter, cancellationToken))
                {
                    RewriteGroupMaskScope(state, pending, 0, null, -1);
                    continue;
                }
                SvgAffineTransform targetMap = pending.Transform;
                if (pending.Parent is {} viewport) targetMap = SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(targetMap);
                SvgMaskTarget? nativeTarget = null;
                if (!targetMap.IsAxisAligned || pending.Parent is { IsTrivial: false })
                {
                    SvgObjectBoundingClip collector = new(pending.Transform, pending.Parent);
                    for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
                    {
                        workCounter.Consume(cancellationToken);
                        SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
                        if (command.Kind == SvgDrawCommandKind.Geometry) collector.Add(state.Scene.GeometryAt(command.GeometryIndex), state, workCounter, cancellationToken);
                    }
                    nativeTarget = SvgMaskTarget.Create(pending.MaskId, targetMap, collector.SourceBounds, state);
                }
                bool hasExplicitRegion = state.MaskRegions.TryGetValue(pending.MaskId, out SvgClipBox customRegion);
                SvgClipBox fractionRegion = default;
                bool hasFractionRegion = !hasExplicitRegion && state.MaskFractionRegions.TryGetValue(pending.MaskId, out fractionRegion);
                SvgClipBox? maskRegion = nativeTarget is {} native ? native.SceneBounds(workCounter, cancellationToken)
                    : hasExplicitRegion || hasFractionRegion
                        ? ResolveGroupMaskRegion(state, pending, hasExplicitRegion, customRegion, fractionRegion, workCounter, cancellationToken)
                        : MaskRegion(GroupChildrenUnion(state, pending, workCounter, cancellationToken));
                if (!scalar || !targetMap.IsAxisAligned && state.MaskContents.TryGetValue(pending.MaskId, out _))
                {
                    if (!state.MaskContents.TryGetValue(pending.MaskId, out SvgMaskContent content)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
                    SvgAffineTransform map = content.IsObjectBoundingBox || nativeTarget is not null ? targetMap : pending.Transform;
                    int contentIndex = state.Scene.AddMaskContent(SvgMaskContentReplay.PlaceForTarget(pending.MaskId, content, map, GroupChildrenUnion(state, pending, workCounter, cancellationToken), nativeTarget, state, workCounter, cancellationToken));
                    RewriteGroupMaskScope(state, pending, 255, maskRegion, contentIndex);
                    continue;
                }
                if (state.MaskContentBoxes.TryGetValue(pending.MaskId, out SvgClipBox contentBox))
                {
                    SvgClipBox placedContent = targetMap.IsAxisAligned ? targetMap.Apply(contentBox) : contentBox;
                    if (HasUniformSource(state, pending.MaskId) && (!targetMap.IsAxisAligned || !GroupCovered(state, pending, placedContent, workCounter, cancellationToken)))
                    {
                        SvgMaskContent content = SvgMaskContentReplay.ResolveUniformSource(pending.MaskId, targetMap, state, workCounter, cancellationToken);
                        int contentIndex = state.Scene.AddMaskContent(content);
                        RewriteGroupMaskScope(state, pending, 255, maskRegion, contentIndex);
                        continue;
                    }
                    ValidateGroupMaskCoverage(state, pending, placedContent, workCounter, cancellationToken);
                }
                // Bind the authored group's own layer. Equal child/parent boundaries and
                // adjacent empty/sibling ranges must never decide which mask owns a scope.
                RewriteGroupMaskScope(state, pending, value, maskRegion, -1);
            }
            finally { state.DefinitionReplayScene = previous; }
        }

        static bool HasSource(SvgDocumentState state, PendingGroupMask pending, ParserWorkCounter work, CancellationToken token)
        {
            bool source = false, paint = false;
            for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
            {
                work.Consume(token);
                SvgDrawCommand command = state.Scene.CommandAt(index);
                if (command.Kind == SvgDrawCommandKind.BeginGroup && command.RegionKind == SvgLayerRegionKind.Filter) paint = true;
                if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                SvgGeometry geometry = state.Scene.GeometryAt(command.GeometryIndex);
                if (geometry.Kind == SvgGeometryKind.Invalid) continue;
                SvgRecordedPaintValue recorded = state.Scene.GetRecordedPaintValue(geometry.RecordedPaint);
                source |= !recorded.IsEffectAnchor || recorded.ObjectBounds is not null;
                paint |= !recorded.IsEffectAnchor && geometry.Kind switch
                {
                    SvgGeometryKind.Rectangle => geometry.Rectangle.HasFill || geometry.Rectangle.Stroke is not null,
                    SvgGeometryKind.OrthogonalBand => geometry.Rectangle.HasFill,
                    SvgGeometryKind.AxisAlignedLine => geometry.AxisAlignedLine.IsPainted,
                    SvgGeometryKind.PolygonFill => geometry.Polygon.HasFill,
                    SvgGeometryKind.GradientPolygon => geometry.GradientPolygon.HasFill,
                    SvgGeometryKind.RadialPolygon => geometry.RadialPolygon.HasFill,
                    SvgGeometryKind.PatternPolygon => geometry.PatternPolygon.HasFill,
                    SvgGeometryKind.GradientFill => geometry.Gradient.Fill.HasFill || geometry.Gradient.Stroke is not null,
                    SvgGeometryKind.RadialGradient => geometry.Radial.Fill.HasFill || geometry.Radial.Stroke is not null,
                    SvgGeometryKind.PatternFill => geometry.Pattern.Fill.HasFill || geometry.Pattern.Fill.Stroke is not null,
                    SvgGeometryKind.GradientStroke => geometry.GradientStroke.Fill.HasFill || geometry.GradientStroke.HasStroke,
                    _ => true,
                };
                if (source && paint) return true;
            }
            return false;
        }

        static bool GroupCovered(SvgDocumentState state, PendingGroupMask pending, SvgClipBox content, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            bool covers = true;
            for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
                if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                SvgGeometry geometry = state.Scene.GeometryAt(command.GeometryIndex);
                SvgRectangle box = ResourceCoverageBox(geometry, workCounter, cancellationToken);
                covers &= content.Contains(box.X, box.Y, box.X + box.Width, box.Y + box.Height);
            }
            return covers;
        }

        static void ValidateGroupMaskCoverage(SvgDocumentState state, PendingGroupMask pending, SvgClipBox content, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
                if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                SvgGeometry geometry = state.Scene.GeometryAt(command.GeometryIndex);
                SvgRectangle box = ResourceCoverageBox(geometry, workCounter, cancellationToken);
                if (!content.Contains(box.X, box.Y, box.X + box.Width, box.Y + box.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering masked group children is implemented.");
            }
        }

        static void RewriteGroupMaskScope(SvgDocumentState state, PendingGroupMask pending, byte maskAlpha, SvgClipBox? maskRegion, int maskContentIndex)
        {
            SvgDrawCommand scope = state.Scene.CommandAt(pending.FirstCommandIndex);
            if (scope.Kind != SvgDrawCommandKind.BeginGroup) throw new InvalidOperationException("Deferred group mask resolution requires its isolation scope.");
            state.Scene.ReplaceCommand(pending.FirstCommandIndex, new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, scope.GeometryIndex, scope.SourceOpacity, maskRegion, maskContentIndex, maskAlpha));
        }
    }
    /// <summary>Resolves a custom group-mask region into root-user space for scope compositing.</summary>
    /// <remarks>Explicit boxes map through the captured ancestor frame (axis scales and offsets); bounding-box fractions resolve against the masked children union like direct leaves. Glyph rules stay separate; empty groups resolve degenerate regions that composite nothing.</remarks>
    private static SvgClipBox ResolveGroupMaskRegion(SvgDocumentState state, PendingGroupMask pending, bool hasExplicitRegion, SvgClipBox customRegion, SvgClipBox fractionRegion, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        SvgClipBox region;
        if (hasExplicitRegion) region = pending.Transform.Apply(customRegion);
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
            SvgRecordedPaintValue paint = state.Scene.GetRecordedPaintValue(geometry.RecordedPaint);
            if (paint.IsEffectAnchor && paint.ObjectBounds is null) continue;
            SvgRectangle box = RecordedObjectBox(geometry, state, workCounter, cancellationToken);
            if (box.X < left) left = box.X;
            if (box.Y < top) top = box.Y;
            if (box.X + box.Width > right) right = box.X + box.Width;
            if (box.Y + box.Height > bottom) bottom = box.Y + box.Height;
        }
        if (double.IsPositiveInfinity(left)) return new SvgRectangle(0, 0, 0, 0, default, false, null);
        return new SvgRectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, false, null);
    }

    internal static SvgRectangle RecordedObjectBox(SvgGeometry geometry, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgPaintBounds? recorded = state.Scene.GetRecordedPaintValue(geometry.RecordedPaint).ObjectBounds;
        return recorded is {} box
            ? new(box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top, default, false, null)
            : ResourceBounds(geometry, work, token);
    }

    /// <summary>Reuses bounded native glyph extrema without changing the qualified non-glyph bounds routes.</summary>
    internal static SvgRectangle ResourceBounds(SvgGeometry geometry, ParserWorkCounter work, CancellationToken token)
        => geometry.Kind == SvgGeometryKind.GlyphContours
            ? SvgGlyphOutline.Bounds(geometry.Glyph.Contours, SvgAffineTransform.Identity, work, token) : BoundingBox(geometry);

    private static SvgRectangle ResourceCoverageBox(SvgGeometry geometry, ParserWorkCounter work, CancellationToken token)
    {
        if (geometry.Kind != SvgGeometryKind.GlyphContours) return CoverageBox(geometry);
        SvgRectangle box = ResourceBounds(geometry, work, token);
        double inflation = geometry.Glyph.StrokeBoundsInflation;
        return box with { X = box.X - inflation, Y = box.Y - inflation, Width = box.Width + 2 * inflation, Height = box.Height + 2 * inflation };
    }

    internal static SvgRectangle BoundingBox(SvgGeometry geometry)
    {
        return geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => geometry.Rectangle,
            SvgGeometryKind.EmbeddedImage => geometry.Image.Box,
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
