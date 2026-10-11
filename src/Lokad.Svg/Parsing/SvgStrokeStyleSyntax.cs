using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Validates the admitted stroke style subset shared by attributes and style declarations.</summary>
internal static class SvgStrokeStyleSyntax
{
    /// <summary>Validates one stroke style attribute value, ignoring namespaces already cleared by the caller.</summary>
    internal static void ValidateAttribute(string localName, string value)
    {
        ValidateAttribute(localName, value, false);
    }

    /// <summary>Validates one stroke style attribute value, admitting round and square caps and round joins on kernel-stroked shapes.</summary>
    internal static void ValidateAttribute(string localName, string value, bool allowNonButt)
    {
        ValidateAttribute(localName, value, allowNonButt, false, false);
    }

    /// <summary>Validates one stroke style attribute value, additionally admitting square caps on band-stroked lines.</summary>
    internal static void ValidateAttribute(string localName, string value, bool allowNonButt, bool allowSquareCap, bool allowClosedLoopCaps)
    {
        ValidateAttribute(localName, value, allowNonButt, allowSquareCap, allowClosedLoopCaps, false, false);
    }

    /// <summary>Validates one stroke style attribute value, additionally admitting round joins on rounded rectangles and bevel joins on admitted shapes.</summary>
    internal static void ValidateAttribute(string localName, string value, bool allowNonButt, bool allowSquareCap, bool allowClosedLoopCaps, bool allowRoundJoin, bool allowBevelJoin)
    {
        if (localName == "stroke-linecap") ValidateLinecap(value.AsSpan(), allowNonButt, allowSquareCap, allowClosedLoopCaps);
        else if (localName == "stroke-linejoin") ValidateLinejoin(value.AsSpan(), allowNonButt || allowRoundJoin, allowBevelJoin);
        else if (localName == "stroke-dasharray") ValidateDasharray(value.AsSpan(), allowNonButt);
        else if (localName == "stroke-dashoffset") ValidateDashoffset(value.AsSpan());
    }

    /// <summary>Validates one stroke style attribute, falling back to the inline style declaration when the attribute carries a spelling the inline style neutralizes. Stylesheet overrides resolve later; lowering re-gates the cascaded value honestly.</summary>
    internal static void ValidateAttribute(XmlReader reader, string localName, string value, bool allowNonButt, bool allowSquareCap, bool allowClosedLoopCaps, bool allowRoundJoin, bool allowBevelJoin, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (SvgInlineStyleParser.IsInheritedPaintProperty(localName.AsSpan())
            && SvgInlineStyleParser.TryMapPaintKeyword(localName.AsSpan(), value.AsSpan(), out _)) return;
        try { ValidateAttribute(localName, value, allowNonButt, allowSquareCap, allowClosedLoopCaps, allowRoundJoin, allowBevelJoin); }
        catch (SvgException)
        {
            if (InlineStrokeDeclaration(reader, localName, workCounter, cancellationToken) is not string inline) throw;
            if (SvgInlineStyleParser.TryMapPaintKeyword(localName.AsSpan(), inline.AsSpan(), out _)) return;
            ValidateAttribute(localName, inline, allowNonButt, allowSquareCap, allowClosedLoopCaps, allowRoundJoin, allowBevelJoin);
        }
    }

    /// <summary>Returns the inline style value for one stroke property when the element declares it, so validation honors inline overrides of rejected attribute spellings.</summary>
    internal static string? InlineStrokeDeclaration(XmlReader reader, string localName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        string? inline = reader.GetAttribute("style");
        if (inline is null) return null;
        InlineStyle parsed = SvgInlineStyleParser.Parse(inline.AsSpan(), workCounter, cancellationToken);
        return localName switch
        {
            "stroke-linecap" => parsed.Linecap,
            "stroke-linejoin" => parsed.Linejoin,
            "stroke-dasharray" => parsed.Dasharray,
            "stroke-dashoffset" => parsed.Dashoffset,
            _ => null,
        };
    }

    private static void ValidateLinecap(ReadOnlySpan<char> value, bool allowNonButt, bool allowSquareCap, bool allowClosedLoopCaps)
    {
        if (value.Trim().Equals("butt".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        if (allowNonButt && value.Trim().Equals("round".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        if ((allowNonButt || allowSquareCap) && value.Trim().Equals("square".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        if (allowClosedLoopCaps && (value.Trim().Equals("round".AsSpan(), StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("square".AsSpan(), StringComparison.OrdinalIgnoreCase))) return;
        if (allowNonButt) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only butt, round and square stroke linecaps are implemented.");
        if (allowSquareCap) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only butt and square stroke linecaps are implemented.");
        if (allowClosedLoopCaps) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only butt, square and round stroke linecaps are implemented.");
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only butt stroke linecaps are implemented.");
    }

    private static void ValidateLinejoin(ReadOnlySpan<char> value, bool allowNonButt, bool allowBevelJoin)
    {
        if (value.Trim().Equals("miter".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        if (allowNonButt && value.Trim().Equals("round".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        if (allowBevelJoin && value.Trim().Equals("bevel".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        if (allowNonButt && allowBevelJoin) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only miter, round and bevel stroke linejoins are implemented.");
        if (allowNonButt) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only miter and round stroke linejoins are implemented.");
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only miter stroke linejoins are implemented.");
    }

    private static void ValidateDashoffset(ReadOnlySpan<char> value)
    {
        _ = ParseDashoffsetValue(value);
    }

    private static void ValidateDasharray(ReadOnlySpan<char> value, bool allowNonButt)
    {
        if (value.Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        if (!allowNonButt) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid (none) stroke dash arrays are implemented.");
        _ = ParseDashEntries(value);
    }

    /// <summary>Bounds retained dash pattern entries; longer lists stay explicitly unsupported.</summary>
    internal const int MaximumDashEntries = 8;

    /// <summary>Parses one dash pattern, returning nothing for an absent or solid pattern.</summary>
    internal static double[]? ParseDasharray(string? value)
    {
        if (value is null) return null;
        if (value.AsSpan().Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) return null;
        return ParseDashEntries(value.AsSpan());
    }

    /// <summary>Parses one dash offset in user units, defaulting to zero like SVG.</summary>
    internal static double ParseDashoffset(string? value)
    {
        if (value is null) return 0;
        return ParseDashoffsetValue(value.AsSpan());
    }

    private static double ParseDashoffsetValue(ReadOnlySpan<char> value)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px dash offsets are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px dash offsets are implemented.");
        if (result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px dash offsets are implemented.");
        return result;
    }

    private static double[] ParseDashEntries(ReadOnlySpan<char> value)
    {
        SvgGeometrySyntax.DelimitedTokenReader tokens = new(value);
        int count = 0;
        while (tokens.TryReadValue(out ReadOnlySpan<char> entry))
        {
            if (count >= MaximumDashEntries) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only dash arrays with at most eight entries are implemented.");
            _ = ParseDashEntry(entry);
            count++;
        }

        if (count == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid (none) stroke dash arrays are implemented.");
        tokens = new(value);
        bool doubled = (count & 1) == 1;
        double[] pattern = new double[doubled ? count * 2 : count];
        int index = 0;
        while (tokens.TryReadValue(out ReadOnlySpan<char> entry)) pattern[index++] = ParseDashEntry(entry);
        if (doubled) Array.Copy(pattern, 0, pattern, count, count);
        return pattern;
    }

    private static double ParseDashEntry(ReadOnlySpan<char> entry)
    {
        ReadOnlySpan<char> token = entry.Trim();
        if (token.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px dash array entries are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px dash array entries are implemented.");
        if (result < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Dash array lengths cannot be negative.");
        if (result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Dash array entries must be finite bounded numbers.");
        return result;
    }

    /// <summary>Rejects paint-server strokes before color resolution, keeping solid colors and currentColor admitted.</summary>
    internal static void RejectPaintServerStroke(string? stroke, string elementName)
    {
        if (stroke is not null && SvgLocalUrl.TryParseId(stroke, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern and gradient strokes on " + elementName + " elements are not implemented.");
    }

    /// <summary>Parses a nonnegative finite stroke width for the shared outline kernel, which maps fractional offsets exactly like fractional geometry. Zero flows to callers as no-stroke; negatives stay denied.</summary>
    internal static double ParseStrokeWidth(string? value, string elementName)
    {
        ReadOnlySpan<char> token = (value ?? "1").AsSpan().Trim();
        if (token.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px " + elementName + " stroke widths are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double strokeWidth) || !double.IsFinite(strokeWidth) || strokeWidth > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px " + elementName + " stroke widths are implemented.");
        if (strokeWidth < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive " + elementName + " stroke widths are implemented.");
        return strokeWidth;
    }

    /// <summary>Reports whether a stroke-width spelling denotes an exact zero without throwing.</summary>
    internal static bool IsZeroStrokeWidth(string? value)
    {
        ReadOnlySpan<char> token = (value ?? "1").AsSpan().Trim();
        if (token.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token.EndsWith("%"))) return false;
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double strokeWidth) || !double.IsFinite(strokeWidth) || strokeWidth > int.MaxValue) return false;
        return strokeWidth == 0;
    }

    /// <summary>Resolves one admitted solid stroke, or nothing when the element carries no stroke paint.</summary>
    internal readonly record struct SvgAdmittedStroke(SvgColor Color, double Width, double MiterLimit, SvgStrokeCap Cap, SvgStrokeJoin Join, double[]? DashPattern, double DashOffset, double? PathLength, bool NonScalingStroke);

    /// <summary>Validates the stroke subset shared by stroked shapes, rejecting paint servers, non-positive widths, small limits and unsupported dash combinations.</summary>
    internal static SvgAdmittedStroke? ResolveAdmittedStroke(XmlReader reader, SvgComputedStyle style, SvgResolvedPaintableElement shape, string elementName, bool allowBevelJoin)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (shape.Presentation.StrokeColor is null) return null;
        if (IsZeroStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"))) return null;
        RejectPaintServerStroke(style.Stroke, elementName);
        double width = ParseStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"), elementName);
        if (width == 0) return null;
        double miterLimit = ParseMiterLimit(style.Miterlimit ?? reader.GetAttribute("stroke-miterlimit"));
        SvgStrokeCap cap = ParseLinecap(style.Linecap);
        SvgStrokeJoin join = ParseLinejoin(style.Linejoin, allowBevelJoin);
        double[]? dashes = ParseDasharray(style.Dasharray);
        double dashOffset = ParseDashoffset(style.Dashoffset);
        if (dashes is not null)
        {
            bool hasPositive = false;
            bool hasZero = false;
            foreach (double entry in dashes)
            {
                if (entry == 0) hasZero = true;
                else hasPositive = true;
            }
            if (!hasPositive) return null;
            if (hasZero && cap != SvgStrokeCap.Butt) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Zero-length dashes with round or square caps are not implemented.");
        }
        bool nonScaling = ParseVectorEffect(reader.GetAttribute("vector-effect"));
        double? pathLength = ParsePathLength(style.PathLength ?? reader.GetAttribute("pathLength"));
        if (pathLength == 0 && dashes is not null)
        {
            if (dashes[0] > 0) dashes = null;
            else return null;
        }
        if (dashes is not null && nonScaling && !shape.Transform.IsUniformScale()) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniformly scaled dashed strokes are not implemented.");
        SvgColor color = SvgPaintResolver.ApplyOpacity(shape.Presentation.StrokeColor.Value, shape.Presentation.Opacity);
        return new SvgAdmittedStroke(color, width, miterLimit, cap, join, dashes, dashOffset, pathLength, nonScaling);
    }

    /// <summary>Parses the vector effect, defaulting to scaling strokes like SVG.</summary>
    internal static bool ParseVectorEffect(string? value)
    {
        if (value is null) return false;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
        if (token.Equals("non-scaling-stroke".AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only scaling and non-scaling strokes are implemented.");
    }

    /// <summary>Parses one path length in user units, returning nothing when absent.</summary>
    internal static double? ParsePathLength(string? value)
    {
        if (value is null) return null;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number path lengths are implemented.");
        if (result < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Path length cannot be negative.");
        if (result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number path lengths are implemented.");
        return result;
    }

    /// <summary>Parses the open-end cap style, defaulting to butt like SVG.</summary>
    internal static SvgStrokeCap ParseLinecap(string? value)
    {
        if (value is null) return SvgStrokeCap.Butt;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Equals("butt".AsSpan(), StringComparison.OrdinalIgnoreCase)) return SvgStrokeCap.Butt;
        if (token.Equals("round".AsSpan(), StringComparison.OrdinalIgnoreCase)) return SvgStrokeCap.Round;
        if (token.Equals("square".AsSpan(), StringComparison.OrdinalIgnoreCase)) return SvgStrokeCap.Square;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only butt, round and square stroke linecaps are implemented.");
    }

    /// <summary>Parses the corner join style, defaulting to miter like SVG.</summary>
    internal static SvgStrokeJoin ParseLinejoin(string? value, bool allowBevel)
    {
        if (value is null) return SvgStrokeJoin.Miter;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Equals("miter".AsSpan(), StringComparison.OrdinalIgnoreCase)) return SvgStrokeJoin.Miter;
        if (token.Equals("round".AsSpan(), StringComparison.OrdinalIgnoreCase)) return SvgStrokeJoin.Round;
        if (allowBevel && token.Equals("bevel".AsSpan(), StringComparison.OrdinalIgnoreCase)) return SvgStrokeJoin.Bevel;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, allowBevel ? "Only miter, round and bevel stroke linejoins are implemented." : "Only miter and round stroke linejoins are implemented.");
    }

    /// <summary>Rejects non-butt caps on dashed strokes and non-miter joins where strokes lower to fixed bands; round and square caps on solid closed loops are spec no-ops and lower as butt.</summary>
    internal static void RequireButtCapJoin(XmlReader reader, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        string? dashAttribute = reader.GetAttribute("stroke-dasharray");
        bool solid = ParseDasharray(dashAttribute is null ? null : InlineStrokeDeclaration(reader, "stroke-dasharray", workCounter, cancellationToken) ?? dashAttribute) is null;
        RequireBandCapJoin(reader, elementName, solid, solid, solid && SvgElementAttributePolicy.HasNonzeroRoundedCorners(reader), solid && SvgElementAttributePolicy.HasNonzeroRoundedCorners(reader), workCounter, cancellationToken);
    }

    /// <summary>Rejects round caps and non-miter joins where strokes lower to fixed bands with square tip extension.</summary>
    internal static void RequireLineCapJoin(XmlReader reader, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        RequireBandCapJoin(reader, elementName, true, true, false, false, workCounter, cancellationToken);
    }

    internal static void RequireButtOrSquareCapJoin(XmlReader reader, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        RequireBandCapJoin(reader, elementName, true, false, false, false, workCounter, cancellationToken);
    }

    private static void RequireBandCapJoin(XmlReader reader, string elementName, bool allowSquare, bool allowRound, bool allowRoundJoin, bool allowBevelJoin, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        string? capAttribute = reader.GetAttribute("stroke-linecap");
        string? cap = capAttribute is null ? null : InlineStrokeDeclaration(reader, "stroke-linecap", workCounter, cancellationToken) ?? capAttribute;
        bool butt = cap is null || cap.AsSpan().Trim().Equals("butt".AsSpan(), StringComparison.OrdinalIgnoreCase);
        bool square = allowSquare && cap is not null && cap.AsSpan().Trim().Equals("square".AsSpan(), StringComparison.OrdinalIgnoreCase);
        bool round = allowRound && cap is not null && cap.AsSpan().Trim().Equals("round".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (!butt && !square && !round) throw new SvgException(SvgErrorCode.UnsupportedFeature, allowRound ? "Only butt, square and round stroke linecaps are implemented on " + elementName + " strokes." : allowSquare ? "Only butt and square stroke linecaps are implemented on " + elementName + " strokes." : "Only butt stroke linecaps are implemented on " + elementName + " strokes.");
        string? joinAttribute = reader.GetAttribute("stroke-linejoin");
        string? join = joinAttribute is null ? null : InlineStrokeDeclaration(reader, "stroke-linejoin", workCounter, cancellationToken) ?? joinAttribute;
        bool roundJoin = allowRoundJoin && join is not null && join.AsSpan().Trim().Equals("round".AsSpan(), StringComparison.OrdinalIgnoreCase);
        bool bevelJoin = allowBevelJoin && join is not null && join.AsSpan().Trim().Equals("bevel".AsSpan(), StringComparison.OrdinalIgnoreCase);        if (join is not null && !join.AsSpan().Trim().Equals("miter".AsSpan(), StringComparison.OrdinalIgnoreCase) && !roundJoin && !bevelJoin) throw new SvgException(SvgErrorCode.UnsupportedFeature, allowBevelJoin ? "Only miter, round and bevel stroke linejoins are implemented on " + elementName + " strokes." : allowRoundJoin ? "Only miter and round stroke linejoins are implemented on " + elementName + " strokes." : "Only miter stroke linejoins are implemented on " + elementName + " strokes.");
    }

    /// <summary>Parses a miter limit of at least one, defaulting to four like SVG.</summary>
    internal static double ParseMiterLimit(string? value)
    {
        string token = value ?? "4";
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke miter limit values exceed the configured syntax limit.");
        if (!double.TryParse(token.AsSpan().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double miterLimit) || !double.IsFinite(miterLimit) || miterLimit < 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only stroke miter limits of at least one are implemented.");
        return miterLimit;
    }
}
