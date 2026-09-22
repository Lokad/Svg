using System.Globalization;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one admitted two-stop linear gradient with its bounding-box vector.</summary>
internal readonly record struct SvgLinearGradient(SvgColor Start, SvgColor End, double X1, double Y1, double X2, double Y2, double Offset1, double Offset2, bool IsEmpty);

/// <summary>Retains one parsed gradient stop with its offset while streaming.</summary>
internal readonly record struct SvgGradientStop(double Offset, SvgColor Color);

/// <summary>Parses the bounded linear-gradient definition syntax and lowers gradient-filled rectangles.</summary>
internal static class SvgGradientSyntax
{
    internal static (double X1, double Y1, double X2, double Y2) ParseLinearVector(XmlReader reader)
    {
        string? units = reader.GetAttribute("gradientUnits");
        if (units is not null && !units.Equals("objectBoundingBox", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only objectBoundingBox gradient units are implemented.");
        string? spread = reader.GetAttribute("spreadMethod");
        if (spread is not null && !spread.Equals("pad", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad gradient spread is implemented.");
        double x1 = ParseAxis(reader.GetAttribute("x1") ?? "0");
        double y1 = ParseAxis(reader.GetAttribute("y1") ?? "0");
        double x2 = ParseAxis(reader.GetAttribute("x2") ?? "1");
        double y2 = ParseAxis(reader.GetAttribute("y2") ?? "0");
        if (x1 == x2 && y1 == y2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate linear gradients are not implemented.");
        return (x1, y1, x2, y2);
    }

    internal static string ParsePaintId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A paint server reference is required.");
        if (value.Length < 7 || !value.StartsWith("url(#", StringComparison.Ordinal) || !value.EndsWith(")", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
        string id = value.Substring(5, value.Length - 6);
        if (id.Length == 0 || id.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
        return id;
    }

    internal static string ParseGradientId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A gradient reference is required.");
        if (value.Length < 7 || !value.StartsWith("url(#", StringComparison.Ordinal) || !value.EndsWith(")", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local gradient references are implemented.");
        string id = value.Substring(5, value.Length - 6);
        if (id.Length == 0 || id.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local gradient references are implemented.");
        return id;
    }

    internal static bool IsGradientFill(string? fill)
        => fill is not null && fill.StartsWith("url(", StringComparison.Ordinal);

    internal static double ParseStopOffset(string? value)
    {
        if (value is null) return 0;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient stop values exceed the configured syntax limit.");
        bool percentage = token.EndsWith("%");
        if (percentage) token = token[..^1].TrimEnd();
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.MalformedInput, "Gradient stop offsets must be finite numbers.");
        return percentage ? result / 100 : result;
    }

    internal static SvgColor ParseStopColor(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        string? opacityValue = reader.GetAttribute("stop-opacity");
        if (opacityValue is not null && !opacityValue.Equals("1", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only full-opacity gradient stops are implemented.");
        string? colorValue = reader.GetAttribute("stop-color");
        SvgColor color = colorValue is null ? new SvgColor(0, 0, 0, 255) : SvgColorParser.Parse(colorValue, namedColors);
        if (color.Alpha != 255) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque gradient stops are implemented.");
        workCounter.Consume(cancellationToken);
        return color;
    }

    internal static SvgGeometry LowerGradientRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (style.FillOpacity is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill opacity with gradient fills is not implemented.");
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Fill), out SvgLinearGradient gradient)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        SvgStroke? stroke = ParseGradientStroke(reader, style, context, transform, placement, opacity, painted);
        SvgRectangle box = placement;
        if (frame is not null)
        {
            if (stroke is not null)
            {
                double halfWidth = stroke.Value.Width / 2d;
                if (!frame.Value.Clip.Contains(placement.X - halfWidth, placement.Y - halfWidth, placement.X + placement.Width + halfWidth, placement.Y + placement.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            }
            placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
            box = frame.Value.Shift(box);
        }
        SvgRectangle fill = placement with { HasFill = painted && !gradient.IsEmpty };
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, opacity, painted ? stroke : null));
    }

    private static SvgStroke? ParseGradientStroke(XmlReader reader, SvgComputedStyle style, SvgPaintableElementContext context, AxisTransform transform, SvgRectangle placement, byte opacity, bool painted)
    {
        if (style.Stroke is null || style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        SvgColor strokeColor = SvgPaintResolver.ResolveStrokeColor(style.Stroke, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        return SvgLeafParser.ParseRectangleStroke(strokeColor, reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, opacity, painted);
    }

    private static double ParseAxis(string value)
    {
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient coordinate values exceed the configured syntax limit.");
        bool percentage = token.EndsWith("%");
        if (percentage) token = token[..^1].TrimEnd();
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient coordinates must be finite bounded numbers.");
        return percentage ? result / 100 : result;
    }
}