using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one admitted two-stop linear gradient with its bounding-box vector.</summary>
internal readonly record struct SvgLinearGradient(SvgColor Start, SvgColor End, double X1, double Y1, double X2, double Y2, double Offset1, double Offset2, bool IsEmpty, bool IsUserSpace, SvgGradientSpread Spread);

/// <summary>Identifies the admitted stop-range extension rule.</summary>
internal enum SvgGradientSpread { Pad, Repeat, Reflect }

/// <summary>Retains one parsed gradient stop with its offset while streaming.</summary>
internal readonly record struct SvgGradientStop(double Offset, SvgColor Color);

/// <summary>Retains one parsed linear-gradient definition with attribute explicitness for href inheritance.</summary>
internal readonly record struct SvgGradientTemplate(double X1, double Y1, double X2, double Y2, bool IsUserSpace, SvgGradientTransform Transform, bool HasTransform, int VectorCount, bool HasUnits, SvgGradientSpread Spread, bool HasSpread, string? Href, bool HasX1, bool HasY1, bool HasX2, bool HasY2);

/// <summary>Retains one admitted axis-aligned gradient transform with finite coordinates.</summary>
internal readonly record struct SvgGradientTransform(double ScaleX, double ScaleY, double OffsetX, double OffsetY)
{
    internal static SvgGradientTransform Identity { get; } = new(1, 1, 0, 0);

    /// <summary>Maps one gradient vector endpoint, rejecting non-finite results.</summary>
    internal (double X, double Y) Apply(double x, double y)
    {
        double mappedX = ScaleX * x + OffsetX;
        double mappedY = ScaleY * y + OffsetY;
        if (!double.IsFinite(mappedX) || !double.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported gradient transform exceeds the gradient coordinate range.");
        return (mappedX, mappedY);
    }
}

/// <summary>Parses the bounded linear-gradient definition syntax and lowers gradient-filled rectangles.</summary>
internal static class SvgGradientSyntax
{
    internal static SvgGradientTemplate ParseLinearVector(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? units = reader.GetAttribute("gradientUnits");
        bool isUserSpace = units is not null && units.Equals("userSpaceOnUse", StringComparison.Ordinal);
        if (units is not null && !units.Equals("objectBoundingBox", StringComparison.Ordinal) && !isUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only objectBoundingBox and userSpaceOnUse gradient units are implemented.");
        string? spread = reader.GetAttribute("spreadMethod");
        SvgGradientSpread spreadMethod = ParseSpreadMethod(spread);
        string? rawX1 = reader.GetAttribute("x1");
        string? rawY1 = reader.GetAttribute("y1");
        string? rawX2 = reader.GetAttribute("x2");
        string? rawY2 = reader.GetAttribute("y2");
        int vectorCount = (rawX1 is null ? 0 : 1) + (rawY1 is null ? 0 : 1) + (rawX2 is null ? 0 : 1) + (rawY2 is null ? 0 : 1);
        if (isUserSpace && (ContainsPercentage(rawX1) || ContainsPercentage(rawY1) || ContainsPercentage(rawX2) || ContainsPercentage(rawY2))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Percentage userSpaceOnUse gradient coordinates are not implemented.");
        double x1 = ParseAxis(rawX1 ?? "0");
        double y1 = ParseAxis(rawY1 ?? "0");
        double x2 = ParseAxis(rawX2 ?? (isUserSpace ? "0" : "1"));
        double y2 = ParseAxis(rawY2 ?? "0");
        string? transformValue = reader.GetAttribute("gradientTransform");
        SvgGradientTransform gradientTransform = ParseGradientTransform(transformValue);
        string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        return new SvgGradientTemplate(x1, y1, x2, y2, isUserSpace, gradientTransform, transformValue is not null, vectorCount, units is not null, spreadMethod, spread is not null, href, rawX1 is not null, rawY1 is not null, rawX2 is not null, rawY2 is not null);
    }

    private static SvgGradientTransform ParseGradientTransform(string? value)
    {
        if (value is null) return SvgGradientTransform.Identity;
        if (value.Length > SvgSyntaxLimits.MaximumTransformCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Transform values exceed the configured syntax limit.");
        ReadOnlySpan<char> input = value.AsSpan();
        SvgGradientTransform transform = SvgGradientTransform.Identity;
        int position = 0;
        bool hasFunction = false;
        while (true)
        {
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            if (position == input.Length)
            {
                if (!hasFunction) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), positive scale(), and positive axis-aligned matrix() gradient transforms are implemented.");
                return transform;
            }
            int nameStart = position;
            while (position < input.Length && char.IsAsciiLetter(input[position])) position++;
            if (nameStart == position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), positive scale(), and positive axis-aligned matrix() gradient transforms are implemented.");
            ReadOnlySpan<char> name = input[nameStart..position];
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            if (position == input.Length || input[position] != '(') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), positive scale(), and positive axis-aligned matrix() gradient transforms are implemented.");
            position++;
            int argumentsStart = position;
            while (position < input.Length && input[position] != ')') position++;
            if (position == input.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), positive scale(), and positive axis-aligned matrix() gradient transforms are implemented.");
            transform = ComposeGradientTransform(transform, ParseGradientTransformFunction(name, input[argumentsStart..position]));
            position++;
            hasFunction = true;
        }
    }

    private static SvgGradientTransform ParseGradientTransformFunction(ReadOnlySpan<char> name, ReadOnlySpan<char> arguments)
    {
        bool isMatrix = name.Equals("matrix".AsSpan(), StringComparison.OrdinalIgnoreCase);
        bool isTranslate = name.Equals("translate".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (!isMatrix && !isTranslate && !name.Equals("scale".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), positive scale(), and positive axis-aligned matrix() gradient transforms are implemented.");
        Span<double> values = stackalloc double[6];
        int valueCount = 0;
        SvgGeometrySyntax.DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, isMatrix ? "matrix() requires six coordinates." : "Transforms require one or two coordinates.");
            values[valueCount++] = ParseGradientTransformCoordinate(value);
        }
        if (isMatrix)
        {
            if (valueCount != 6) throw new SvgException(SvgErrorCode.MalformedInput, "matrix() requires six coordinates.");
            if (values[1] != 0 || values[2] != 0 || values[0] <= 0 || values[3] <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive axis-aligned matrix() gradient transforms are implemented.");
            return new SvgGradientTransform(values[0], values[3], values[4], values[5]);
        }
        if (valueCount is < 1 or > 2) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
        double first = values[0];
        double second = valueCount == 2 ? values[1] : (isTranslate ? 0 : first);
        if (isTranslate) return new SvgGradientTransform(1, 1, first, second);
        if (first <= 0 || second <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive scale() gradient transforms are implemented.");
        return new SvgGradientTransform(first, second, 0, 0);
    }

    private static double ParseGradientTransformCoordinate(ReadOnlySpan<char> value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient transform coordinates must be finite bounded numbers.");
        return result;
    }

    private static SvgGradientTransform ComposeGradientTransform(SvgGradientTransform outer, SvgGradientTransform inner)
        => new(outer.ScaleX * inner.ScaleX, outer.ScaleY * inner.ScaleY, outer.ScaleX * inner.OffsetX + outer.OffsetX, outer.ScaleY * inner.OffsetY + outer.OffsetY);

    internal static SvgGradientSpread ParseSpreadMethod(string? value)
    {
        if (value is null || value.Equals("pad", StringComparison.OrdinalIgnoreCase)) return SvgGradientSpread.Pad;
        if (value.Equals("repeat", StringComparison.OrdinalIgnoreCase)) return SvgGradientSpread.Repeat;
        if (value.Equals("reflect", StringComparison.OrdinalIgnoreCase)) return SvgGradientSpread.Reflect;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad, repeat and reflect gradient spread methods are implemented.");
    }

    /// <summary>Validates the admitted default color interpolation shared by attributes and style declarations.</summary>
    internal static void ValidateColorInterpolation(ReadOnlySpan<char> value)
    {
        if (value.Trim().Equals("sRGB".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only sRGB color interpolation is implemented.");
    }

    private static bool ContainsPercentage(string? value) => value is not null && value.AsSpan().Trim().EndsWith("%");

    internal static string ParsePaintId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A paint server reference is required.");
        if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
        return id;
    }

    internal static string ParseGradientId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A gradient reference is required.");
        if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local gradient references are implemented.");
        return id;
    }

    internal static string ParseGradientHrefId(string? value)
    {
        if (value is null || value.Length < 2 || !value.StartsWith("#", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local gradient references are implemented.");
        string id = value.Substring(1);
        if (id.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local gradient references are implemented.");
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
        gradient = MapUserSpaceVector(transform, gradient, frame);
        gradient = MirrorBoundingBoxVector(transform, gradient);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        SvgStroke? stroke = ParseGradientStroke(reader, style, context, transform, placement, opacity, painted);
        if (stroke is not null && frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
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
        if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
            return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, opacity), Stroke = painted ? stroke : null });
        }
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, opacity, painted ? stroke : null));
    }

    /// <summary>Lowers one gradient-filled orthogonal path or point-list shape into gradient-evaluated rectangle bands.</summary>
    internal static SvgParsedGeometry LowerGradientBands(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, string elementName, bool isPath, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame)
    {
        if (style.FillOpacity is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill opacity with gradient fills is not implemented.");
        if (style.Stroke is not null && !style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient-filled paths with strokes are not implemented.");
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Fill), out SvgLinearGradient gradient)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        AxisTransform transform = context.ResolveTransform(reader);
        gradient = MapUserSpaceVector(transform, gradient, frame);
        gradient = MirrorBoundingBoxVector(transform, gradient);
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, maximumGeometryElements, budget, context, contourEnds);
        SvgRectangle box = BoxFromGradientBands(points, transform, frame, context);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, isPath, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
            return lowering.Lower(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame);
        }
        SvgParsedGeometry solid = lowering.Lower(default, painted && !gradient.IsEmpty, transform, frame);
        SvgGeometry[] bands = solid.Storage.Items;
        for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
        {
            context.WorkCounter.Consume(context.CancellationToken);
            bands[bandIndex] = SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, bands[bandIndex].Rectangle, opacity, null));
        }
        return solid;
        static SvgRectangle BoxFromGradientBands(BudgetedParserList<SvgPoint> points, AxisTransform transform, SvgViewportFrame? frame, SvgPaintableElementContext context)
        {
            int minimumX = points[0].X;
            int minimumY = points[0].Y;
            int maximumX = points[0].X;
            int maximumY = points[0].Y;
            for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
            {
                if ((pointIndex & 4095) == 0) context.CancellationToken.ThrowIfCancellationRequested();
                SvgPoint point = points[pointIndex];
                if (point.X < minimumX) minimumX = point.X;
                if (point.Y < minimumY) minimumY = point.Y;
                if (point.X > maximumX) maximumX = point.X;
                if (point.Y > maximumY) maximumY = point.Y;
            }
            SvgRectangle box = transform.Apply(new SvgRectangle(minimumX, minimumY, (double)maximumX - minimumX, (double)maximumY - minimumY, default, true, null));
            return frame is null ? box : frame.Value.Shift(box);
        }
    }

    internal static bool IsGradientStrokeUrl(string? stroke)
        => stroke is not null && stroke.StartsWith("url(", StringComparison.Ordinal);

    internal static SvgLinearGradient MapUserSpaceVector(AxisTransform transform, SvgLinearGradient gradient, SvgViewportFrame? frame)
    {
        if (!gradient.IsUserSpace) return gradient;
        if (frame is not null && (frame.Value.OffsetX != 0 || frame.Value.OffsetY != 0 || frame.Value.ViewportWidth is not null || frame.Value.ViewportHeight is not null)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "userSpaceOnUse gradients inside transformed viewports are not implemented.");
        (double mappedX1, double mappedY1) = transform.ApplyToUserPoint(gradient.X1, gradient.Y1);
        (double mappedX2, double mappedY2) = transform.ApplyToUserPoint(gradient.X2, gradient.Y2);
        return gradient with { X1 = mappedX1, Y1 = mappedY1, X2 = mappedX2, Y2 = mappedY2 };
    }

    /// <summary>Mirrors one objectBoundingBox gradient vector when the referencing transform reflects its axis.</summary>
    internal static SvgLinearGradient MirrorBoundingBoxVector(AxisTransform transform, SvgLinearGradient gradient)
    {
        if (gradient.IsUserSpace) return gradient;
        double x1 = transform.ScaleX < 0 ? 1 - gradient.X1 : gradient.X1;
        double x2 = transform.ScaleX < 0 ? 1 - gradient.X2 : gradient.X2;
        double y1 = transform.ScaleY < 0 ? 1 - gradient.Y1 : gradient.Y1;
        double y2 = transform.ScaleY < 0 ? 1 - gradient.Y2 : gradient.Y2;
        return gradient with { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
    }

    internal static SvgGeometry LowerGradientStrokeRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (state.Patterns.TryGetValue(ParseGradientId(style.Stroke), out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern strokes are not implemented.");
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Stroke), out SvgLinearGradient gradient)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        gradient = MapUserSpaceVector(transform, gradient, frame);
        if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate linear gradient strokes are not implemented.");
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        SvgResolvedPresentation presentation = SvgPresentationResolver.Resolve(style, context.InheritedPresentation.Paint, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible, context.NamedColors);
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool painted = presentation.IsPainted;
        byte opacity = presentation.Opacity;
        SvgColor fillColor = SvgPaintResolver.ResolveFillColor(presentation.Paint, opacity);
        bool hasFill = presentation.Paint.HasFill && painted;
        SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, 255, painted);
        if (probe is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width gradient strokes no larger than the rectangle dimensions are implemented.");
        double halfWidth = probe.Value.Width / 2d;
        int strokeWidth = probe.Value.Width;
        SvgRectangle top = new(placement.X - halfWidth, placement.Y - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
        SvgRectangle bottom = new(placement.X - halfWidth, placement.Y + placement.Height - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
        SvgRectangle left = new(placement.X - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        SvgRectangle right = new(placement.X + placement.Width - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        SvgRectangle box = placement;
        SvgRectangle fill = placement with { Color = fillColor, HasFill = hasFill && !gradient.IsEmpty };
        if (frame is not null)
        {
            if (!frame.Value.Clip.Contains(placement.X - halfWidth, placement.Y - halfWidth, placement.X + placement.Width + halfWidth, placement.Y + placement.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            SvgRectangle shifted = frame.Value.Shift(placement);
            fill = frame.Value.Clip.ClipRectangle(shifted) with { Color = fillColor, HasFill = hasFill && !gradient.IsEmpty };
            box = frame.Value.Shift(box);
            top = frame.Value.Clip.ClipRectangle(frame.Value.Shift(top));
            bottom = frame.Value.Clip.ClipRectangle(frame.Value.Shift(bottom));
            left = frame.Value.Clip.ClipRectangle(frame.Value.Shift(left));
            right = frame.Value.Clip.ClipRectangle(frame.Value.Shift(right));
        }
        return SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(fill, top, bottom, left, right, gradient, box, opacity, painted));
    }

    /// <summary>Lowers one gradient-stroked axis-aligned line into a gradient-evaluated stroke band.</summary>
    internal static SvgGeometry LowerGradientStrokeLine(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgLineSyntax syntax, SvgDocumentState state, SvgViewportFrame? frame)
    {
        AxisTransform transform = context.ResolveTransform(reader);
        if (transform != AxisTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        if ((syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(syntax.StrokeWidth ?? "1");
        if (strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width line strokes are implemented.");
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Stroke), out SvgLinearGradient gradient)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        gradient = MapUserSpaceVector(transform, gradient, frame);
        if (!gradient.IsUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse gradient strokes on lines are implemented.");
        gradient = MirrorBoundingBoxVector(transform, gradient);
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        SvgRectangle band = new SvgAxisAlignedLine(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, default, painted, strokeWidth).ToStrokeBand();
        SvgRectangle box = band;
        if (frame is not null)
        {
            band = frame.Value.ClipShifted(band);
            box = frame.Value.Shift(box);
        }
        SvgRectangle fill = band with { HasFill = painted && !gradient.IsEmpty };
        if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
            return SvgGeometry.FromOrthogonalBand(fill with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, opacity) });
        }
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, opacity, null));
    }

    private static SvgStroke? ParseGradientStroke(XmlReader reader, SvgComputedStyle style, SvgPaintableElementContext context, AxisTransform transform, SvgRectangle placement, byte opacity, bool painted)
    {
        if (style.Stroke is null || style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (IsGradientStrokeUrl(style.Stroke)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient fills with gradient strokes are not implemented.");
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
