using System.Xml;

namespace Lokad.Svg.Parsing;

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
    /// <summary>Resolves the effective clip for one rendered leaf or use instance from the viewport frame and an optional clip-path attribute.</summary>
    internal static SvgViewportFrame? ResolveLeafClip(XmlReader reader, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? reference = reader.GetAttribute("clip-path");
        if (reference is null) return state.EffectiveFrame;
        if (state.EffectiveFrame is not null && state.EffectiveFrame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
        if (state.ObjectClips.TryGetValue(ParseClipId(reference), out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths are only implemented on rectangles.");
        if (!state.Clips.TryGetValue(ParseClipId(reference), out SvgClipBox box)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
        SvgViewportFrame? parent = state.EffectiveFrame;
        SvgClipBox clip = parent is null ? box : parent.Value.Clip.Intersect(box);
        double offsetX = parent?.OffsetX ?? 0;
        double offsetY = parent?.OffsetY ?? 0;
        double scaleX = parent?.ScaleX ?? 1;
        double scaleY = parent?.ScaleY ?? 1;
        return new SvgViewportFrame(offsetX, offsetY, scaleX, scaleY, parent?.ViewportWidth, parent?.ViewportHeight, clip, reader.Depth);
    }
}