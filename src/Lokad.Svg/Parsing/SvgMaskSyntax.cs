using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded mask definition syntax and applies retained mask values to lowered geometry.</summary>
internal static class SvgMaskSyntax
{
    internal static bool ParseMaskType(string? value)
    {
        if (value is null || value.Equals("luminance", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Equals("alpha", StringComparison.OrdinalIgnoreCase)) return true;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only luminance and alpha mask types are implemented.");
    }

    internal static string ParseMaskId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A mask reference is required.");
        if (value.Length < 7 || !value.StartsWith("url(#", StringComparison.Ordinal) || !value.EndsWith(")", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local mask references are implemented.");
        string id = value.Substring(5, value.Length - 6);
        if (id.Length == 0 || id.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local mask references are implemented.");
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
        return ApplyMask(geometry, value, MaskRegion(BoundingBox(geometry)));
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
        SvgClipBox region = MaskRegion(new SvgRectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, false, null));
        for (int index = 0; index < geometries.Length; index++) geometries[index] = ApplyMask(geometries[index], value, region);
    }

    internal static byte? ResolveUseMask(XmlReader reader, SvgDocumentState state)
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
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on this geometry are not implemented.")
        };
    }

        internal static SvgGeometry ApplyMask(SvgGeometry geometry, byte value, SvgClipBox region)
    {
        return geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => SvgGeometry.FromRectangle(StampRectangle(ClipStrokedRectangle(geometry.Rectangle, region), value)),
            SvgGeometryKind.AxisAlignedLine => StampLine(region.ClipLine(geometry.AxisAlignedLine), value),
            SvgGeometryKind.OrthogonalBand => SvgGeometry.FromOrthogonalBand(StampRectangle(region.ClipRectangle(geometry.Rectangle), value)),
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