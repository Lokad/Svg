using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded mask definition syntax and applies retained mask values to lowered geometry.</summary>
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

    /// <summary>Stamps one lowered geometry with a mask value inside its mask region.</summary>
    internal static SvgGeometry ApplyMaskedLeaf(XmlReader reader, SvgDocumentState state, SvgGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("mask");
        if (reference is null) return geometry;
        byte value = ResolveMaskValue(reference, state);
        SvgRectangle fallback = BoundingBox(geometry);
        ValidateMaskCoverage(reference, state, fallback);
        return ApplyMask(geometry, value, ResolveRegion(reader, state, fallback));
    }

    internal static void ApplyMaskToBands(XmlReader reader, SvgDocumentState state, SvgGeometry[] geometries)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(geometries);
        string? reference = reader.GetAttribute("mask");
        if (reference is null || geometries.Length == 0) return;
        byte value = ResolveMaskValue(reference, state);
        double left = double.PositiveInfinity;
        double top = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double bottom = double.NegativeInfinity;
        foreach (SvgGeometry geometry in geometries)
        {
            SvgRectangle box = BoundingBox(geometry);
            left = Math.Min(left, box.X);
            top = Math.Min(top, box.Y);
            right = Math.Max(right, box.X + box.Width);
            bottom = Math.Max(bottom, box.Y + box.Height);
        }
        SvgRectangle fallback = new SvgRectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, false, null);
        ValidateMaskCoverage(reference, state, fallback);
        SvgClipBox region = ResolveRegion(reader, state, fallback);
        for (int index = 0; index < geometries.Length; index++) geometries[index] = ApplyMask(geometries[index], value, region);
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
        return reference is not null && state.MaskRegions.TryGetValue(ParseMaskId(reference), out _);
    }

    /// <summary>Resolves the explicit mask region or falls back to the default bounding-box region.</summary>
    internal static SvgClipBox ResolveRegion(XmlReader reader, SvgDocumentState state, SvgRectangle fallback)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("mask");
        if (reference is not null && state.MaskRegions.TryGetValue(ParseMaskId(reference), out SvgClipBox custom))
        {
            if (state.EffectiveFrame is not null && !state.EffectiveFrame.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions inside viewports are not implemented.");
            return custom;
        }
        return MaskRegion(fallback);
    }
    internal static byte? ResolveOptionalMask(XmlReader reader, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("mask");
        if (reference is null) return null;
        return ResolveMaskValue(reference, state);
    }

    private static byte ResolveMaskValue(string reference, SvgDocumentState state)
    {
        if (!state.Masks.TryGetValue(ParseMaskId(reference), out byte value)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
        return value;
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
            SvgGeometryKind.GradientStroke => geometry.GradientStroke.OuterBox(),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on this geometry are not implemented.")
        };
    }

        private static SvgGeometry ApplyPatternMask(SvgPatternGeometry pattern, byte value, SvgClipBox region)
    {
        SvgPatternGeometry stamped = pattern.Stamped(value);
        if (stamped.Stroke is not null)
        {
            double halfWidth = stamped.Stroke.Value.Width / 2d;
            SvgRectangle fill = stamped.Fill;
            if (!region.Contains(fill.X - halfWidth, fill.Y - halfWidth, fill.X + fill.Width + halfWidth, fill.Y + fill.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroked pattern fills crossing a mask region boundary are not implemented.");
        }
        return SvgGeometry.FromPatternFill(stamped.ClipTo(region));
    }

    private static SvgGeometry ApplyGradientMask(SvgGradientGeometry gradient, byte value, SvgClipBox region)
    {
        SvgGradientGeometry stamped = gradient.Stamped(value);
        if (stamped.Stroke is not null)
        {
            double halfWidth = stamped.Stroke.Value.Width / 2d;
            SvgRectangle fill = stamped.Fill;
            if (!region.Contains(fill.X - halfWidth, fill.Y - halfWidth, fill.X + fill.Width + halfWidth, fill.Y + fill.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroked gradient fills crossing a mask region boundary are not implemented.");
        }
        return SvgGeometry.FromGradientFill(stamped.ClipTo(region));
    }

    private static SvgGeometry ApplyGradientStrokeMask(SvgGradientStrokeGeometry stroke, byte value, SvgClipBox region)
        => SvgGeometry.FromGradientStroke(stroke.Stamped(value).ClipTo(region));

    internal static SvgGeometry ApplyMask(SvgGeometry geometry, byte value, SvgClipBox region)
    {
        return geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => SvgGeometry.FromRectangle(StampRectangle(ClipStrokedRectangle(geometry.Rectangle, region), value)),
            SvgGeometryKind.AxisAlignedLine => StampLine(region.ClipLine(geometry.AxisAlignedLine), value),
            SvgGeometryKind.OrthogonalBand => SvgGeometry.FromOrthogonalBand(StampRectangle(region.ClipRectangle(geometry.Rectangle), value)),
            SvgGeometryKind.PatternFill => ApplyPatternMask(geometry.Pattern, value, region),
            SvgGeometryKind.GradientFill => ApplyGradientMask(geometry.Gradient, value, region),
            SvgGeometryKind.GradientStroke => ApplyGradientStrokeMask(geometry.GradientStroke, value, region),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on this geometry are not implemented.")
        };
    }

    private static SvgRectangle ClipStrokedRectangle(SvgRectangle rectangle, SvgClipBox region)
    {
        if (rectangle.Stroke is null) return region.ClipRectangle(rectangle);
        double halfWidth = rectangle.Stroke.Value.Width / 2d;
        if (!region.Contains(rectangle.X - halfWidth, rectangle.Y - halfWidth, rectangle.X + rectangle.Width + halfWidth, rectangle.Y + rectangle.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a mask region boundary are not implemented.");
        return rectangle;
    }

    private static SvgRectangle StampRectangle(SvgRectangle rectangle, byte value)
    {
        SvgColor color = rectangle.Color;
        SvgColor stamped = new(color.Red, color.Green, color.Blue, SvgReferenceResolver.MultiplyAlpha(color.Alpha, value));
        SvgStroke? stroke = rectangle.Stroke;
        SvgStroke? stampedStroke = stroke is null ? null : new SvgStroke(new SvgColor(stroke.Value.Color.Red, stroke.Value.Color.Green, stroke.Value.Color.Blue, SvgReferenceResolver.MultiplyAlpha(stroke.Value.Color.Alpha, value)), stroke.Value.Width);
        return rectangle with { Color = stamped, Stroke = stampedStroke };
    }

    private static SvgGeometry StampLine(SvgGeometry geometry, byte value)
    {
        if (geometry.Kind == SvgGeometryKind.AxisAlignedLine)
        {
            SvgAxisAlignedLine line = geometry.AxisAlignedLine;
            SvgColor color = line.Color;
            return SvgGeometry.FromAxisAlignedLine(line with { Color = new SvgColor(color.Red, color.Green, color.Blue, SvgReferenceResolver.MultiplyAlpha(color.Alpha, value)) });
        }
        SvgRectangle band = geometry.Rectangle;
        return SvgGeometry.FromOrthogonalBand(StampRectangle(band, value));
    }
}