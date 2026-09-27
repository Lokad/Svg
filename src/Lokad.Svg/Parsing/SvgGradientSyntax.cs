using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one admitted one-to-eight-stop linear gradient with its vector and referencing scale record.</summary>
/// <remarks>ReferenceScaleX and ReferenceScaleY multiply every scale map applied to the stored endpoints after the local vector definition, so raster evaluation inverts the exact paint map.</remarks>
internal readonly record struct SvgLinearGradient(SvgColor Start, SvgColor End, double X1, double Y1, double X2, double Y2, double Offset1, double Offset2, bool IsEmpty, bool IsUserSpace, SvgGradientSpread Spread, SvgGradientStop[] Stops, double ReferenceScaleX, double ReferenceScaleY);

/// <summary>Identifies the admitted stop-range extension rule.</summary>
internal enum SvgGradientSpread { Pad, Repeat, Reflect }

/// <summary>Retains one parsed gradient stop with its offset while streaming.</summary>
internal readonly record struct SvgGradientStop(double Offset, SvgColor Color);

/// <summary>Retains one parsed linear-gradient definition with attribute explicitness for href inheritance.</summary>
internal readonly record struct SvgGradientTemplate(double X1, double Y1, double X2, double Y2, bool IsUserSpace, SvgGradientTransform Transform, bool HasTransform, int VectorCount, bool HasUnits, SvgGradientSpread Spread, bool HasSpread, string? Href, bool HasX1, bool HasY1, bool HasX2, bool HasY2);

/// <summary>Retains one admitted one-to-eight-stop radial gradient with a strictly inside focal point, contained focal circle and referencing scale record.</summary>
/// <remarks>ReferenceScaleX and ReferenceScaleY multiply every scale map applied to the stored circle after the local definition, so raster evaluation inverts the exact paint map under non-uniform referencing transforms.</remarks>
internal readonly record struct SvgRadialGradient(SvgColor Start, SvgColor End, double CenterX, double CenterY, double Radius, double FocalX, double FocalY, double FocalRadius, double Offset1, double Offset2, bool IsEmpty, bool IsUserSpace, SvgGradientSpread Spread, SvgGradientStop[] Stops, double ReferenceScaleX, double ReferenceScaleY);

/// <summary>Retains one parsed radial-gradient definition with attribute explicitness for href inheritance.</summary>
internal readonly record struct SvgRadialTemplate(double CenterX, double CenterY, double Radius, double FocalX, double FocalY, double FocalRadius, bool IsUserSpace, SvgGradientTransform Transform, bool HasTransform, bool HasUnits, SvgGradientSpread Spread, bool HasSpread, string? Href, int VectorCount, bool HasCenterX, bool HasCenterY, bool HasRadius, bool HasFocalX, bool HasFocalY, bool HasFocalRadius);

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
    /// <summary>Bounds retained linear-gradient stops; the per-entry storage charge covers exactly this many.</summary>
    internal const int MaximumGradientStops = 8;

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

    internal static SvgRadialTemplate ParseRadialVector(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? units = reader.GetAttribute("gradientUnits");
        bool isUserSpace = units is not null && units.Equals("userSpaceOnUse", StringComparison.Ordinal);
        if (units is not null && !units.Equals("objectBoundingBox", StringComparison.Ordinal) && !isUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only objectBoundingBox and userSpaceOnUse radial gradient units are implemented.");
        string? spread = reader.GetAttribute("spreadMethod");
        SvgGradientSpread spreadMethod = ParseSpreadMethod(spread);
        string? transformValue = reader.GetAttribute("gradientTransform");
        SvgGradientTransform gradientTransform = ParseGradientTransform(transformValue);
        if (gradientTransform.ScaleX != gradientTransform.ScaleY && !isUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform radial gradient transforms are not implemented.");
        string? rawFocalRadius = reader.GetAttribute("fr");
        string? rawCenterX = reader.GetAttribute("cx");
        string? rawCenterY = reader.GetAttribute("cy");
        string? rawRadius = reader.GetAttribute("r");
        string? rawFocalX = reader.GetAttribute("fx");
        string? rawFocalY = reader.GetAttribute("fy");
        int vectorCount = (rawCenterX is null ? 0 : 1) + (rawCenterY is null ? 0 : 1) + (rawRadius is null ? 0 : 1);
        if (isUserSpace && (ContainsPercentage(rawCenterX) || ContainsPercentage(rawCenterY) || ContainsPercentage(rawRadius) || ContainsPercentage(rawFocalX) || ContainsPercentage(rawFocalY) || ContainsPercentage(rawFocalRadius))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Percentage userSpaceOnUse gradient coordinates are not implemented.");
        double centerX = ParseAxis(rawCenterX ?? (isUserSpace ? "0" : "0.5"));
        double centerY = ParseAxis(rawCenterY ?? (isUserSpace ? "0" : "0.5"));
        double radius = ParseAxis(rawRadius ?? (isUserSpace ? "0" : "0.5"));
        if (radius < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Radial gradient radius cannot be negative.");
        double focalX = rawFocalX is null ? centerX : ParseAxis(rawFocalX);
        double focalY = rawFocalY is null ? centerY : ParseAxis(rawFocalY);
        double focalRadius = rawFocalRadius is null ? 0 : ParseAxis(rawFocalRadius);
        if (focalRadius < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Focal radius cannot be negative.");
        string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        return new SvgRadialTemplate(centerX, centerY, radius, focalX, focalY, focalRadius, isUserSpace, gradientTransform, transformValue is not null, units is not null, spreadMethod, spread is not null, href, vectorCount, rawCenterX is not null, rawCenterY is not null, rawRadius is not null, rawFocalX is not null, rawFocalY is not null, rawFocalRadius is not null);
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
                if (!hasFunction) return transform;
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
        string? colorValue = reader.GetAttribute("stop-color");
        SvgColor color = colorValue is null ? new SvgColor(0, 0, 0, 255) : SvgColorParser.Parse(colorValue, namedColors);
        byte stopOpacity = SvgColorParser.ParseOpacity(reader.GetAttribute("stop-opacity"));
        workCounter.Consume(cancellationToken);
        return new SvgColor(color.Red, color.Green, color.Blue, SvgReferenceResolver.MultiplyAlpha(color.Alpha, stopOpacity));
    }

    internal static SvgGeometry LowerGradientRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Fill), out SvgLinearGradient gradient))
        {
            if (state.RadialGradients.TryGetValue(ParseGradientId(style.Fill), out SvgRadialGradient radial)) return LowerRadialRect(reader, context, style, state, radial, frame);
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        }
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        gradient = MapUserSpaceVector(transform, gradient, frame);
        gradient = MirrorBoundingBoxVector(transform, gradient);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        byte fillOpacity = SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.FillOpacity));
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
        if (gradient.Stops.Length == 1) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, fillOpacity), Stroke = painted ? stroke : null });
        if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
            return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, fillOpacity), Stroke = painted ? stroke : null });
        }
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, fillOpacity, painted ? stroke : null));
    }

    /// <summary>Lowers one radial-gradient-filled rectangle with an optional solid stroke.</summary>
    internal static SvgGeometry LowerRadialRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgRadialGradient gradient, SvgViewportFrame? frame)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        gradient = MapUserSpaceRadial(transform, gradient, frame);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        byte fillOpacity = SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.FillOpacity));
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
        if (gradient.Stops.Length == 1) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, fillOpacity), Stroke = painted ? stroke : null });
        if (gradient.Radius == 0 && !gradient.IsEmpty) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, fillOpacity), Stroke = painted ? stroke : null });
        return SvgGeometry.FromRadialFill(new SvgRadialGeometry(gradient, box, fill, fillOpacity, painted ? stroke : null));
    }

    /// <summary>Lowers one radial-gradient-filled orthogonal path or point-list shape into radial-evaluated rectangle bands sharing the shape box.</summary>
    internal static SvgParsedGeometry LowerRadialBands(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgRadialGradient gradient, string elementName, bool isPath, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame)
    {
        SvgStrokeStyleSyntax.SvgAdmittedStroke? bandStroke = null;
        SvgResolvedPaintableElement bandStrokeShape = default;
        if (style.Stroke is not null && !style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            // Solid strokes compose over radial fills through the admitted outline kernel; paint strokes stay gated below.
            bandStrokeShape = context.ResolveShape(style with { Fill = "black" }, reader, true);
            bandStroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, style, bandStrokeShape, elementName, false);
        }
        AxisTransform transform = context.ResolveTransform(reader);
        gradient = MapUserSpaceRadial(transform, gradient, frame);
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, true, maximumGeometryElements, budget, context, contourEnds);
        SvgRectangle box = BoxFromRadialBands(points, transform, frame, context);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, isPath, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        if (SvgOrthogonalShapeParser.HasDiagonalContour(points, contourEnds))
        {
            if (gradient.Stops.Length == 1 || (gradient.Radius == 0 && !gradient.IsEmpty)) return AppendPaintFillStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            return AppendPaintFillStroke(lowering.LowerRadialPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
        }
        if (gradient.Stops.Length == 1 || (gradient.Radius == 0 && !gradient.IsEmpty)) return AppendPaintFillStroke(lowering.Lower(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
        SvgParsedGeometry solid = lowering.Lower(default, painted && !gradient.IsEmpty, transform, frame);
        SvgGeometry[] bands = solid.Storage.Items;
        for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
        {
            context.WorkCounter.Consume(context.CancellationToken);
            bands[bandIndex] = SvgGeometry.FromRadialFill(new SvgRadialGeometry(gradient, box, bands[bandIndex].Rectangle, opacity, null));
        }
        return AppendPaintFillStroke(solid, reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);

        static SvgRectangle BoxFromRadialBands(BudgetedParserList<SvgPoint> points, AxisTransform transform, SvgViewportFrame? frame, SvgPaintableElementContext context)
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

    /// <summary>Lowers one gradient-filled orthogonal path or point-list shape into gradient-evaluated rectangle bands.</summary>
    internal static SvgParsedGeometry LowerGradientBands(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, string elementName, bool isPath, int maximumGeometryElements, RenderBudget budget, SvgViewportFrame? frame)
    {
        SvgStrokeStyleSyntax.SvgAdmittedStroke? bandStroke = null;
        SvgResolvedPaintableElement bandStrokeShape = default;
        if (style.Stroke is not null && !style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            // Solid strokes compose over gradient fills through the admitted outline kernel; paint strokes stay gated below.
            bandStrokeShape = context.ResolveShape(style with { Fill = "black" }, reader, true);
            bandStroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, style, bandStrokeShape, elementName, false);
        }
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Fill), out SvgLinearGradient gradient))
        {
            if (state.RadialGradients.TryGetValue(ParseGradientId(style.Fill), out SvgRadialGradient radial)) return LowerRadialBands(reader, context, style, state, radial, elementName, isPath, maximumGeometryElements, budget, frame);
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        }
        AxisTransform transform = context.ResolveTransform(reader);
        gradient = MapUserSpaceVector(transform, gradient, frame);
        gradient = MirrorBoundingBoxVector(transform, gradient);
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        if (isPath && painted && !gradient.IsEmpty)
        {
            string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
            (int commandCount, bool hasCurve, bool hasArc, bool hasFractional) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
            if (bandStroke is not null && (hasCurve || hasFractional)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient-filled curved or fractional paths with strokes are not implemented.");
            if (hasCurve || hasFractional)
            {
                // Curved and fractional fills flatten through shared device outlines, repainted with the gradient like stroke outlines.
                // The gradient url is not a parseable color; resolve a solid placeholder (discarded by the repaint below) for transform and fill state.
                SvgComputedStyle solidFill = style with { Fill = "black" };
                SvgResolvedPaintableElement curveShape = context.ResolveShape(solidFill, reader, true);
                using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, elementName, true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                SvgPolygonMapping mapping = hasCurve ? SvgPolygonMapping.FlattenedCurves : SvgPolygonMapping.FiniteEdges;
                SvgParsedGeometry filled = curveLowering.LowerDeviceFill(d, commandCount, hasArc, hasCurve, mapping, curveShape, frame, budget);
                try
                {
                    if (gradient.Stops.Length > 1 && gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
                    SvgRectangle paintBox = gradient.IsUserSpace ? default : SvgOrthogonalShapeParser.BoxFromFilledGeometryBounds(filled, context.CancellationToken);
                    using BudgetedParserList<SvgGeometry> repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                    foreach (SvgGeometry existing in filled.Geometries)
                    {
                        repainted.Add(SvgGradientPolygon.RepaintOutline(existing.Polygon, gradient, paintBox, opacity, budget));
                    }
                    filled.Dispose();
                    return new SvgParsedGeometry(repainted.Detach(), filled.ElementCount);
                }
                catch { filled.Dispose(); throw; }
            }
        }
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, true, maximumGeometryElements, budget, context, contourEnds);
        SvgRectangle box = BoxFromGradientBands(points, transform, frame, context);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, isPath, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        if (SvgOrthogonalShapeParser.HasDiagonalContour(points, contourEnds))
        {
            if (gradient.Stops.Length == 1) return AppendPaintFillStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
            {
                if (gradient.Stops.Length > 1 && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
                return AppendPaintFillStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            }
            return AppendPaintFillStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
        }
        if (gradient.Stops.Length == 1 || (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty))
        {
            if (gradient.Stops.Length > 1 && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
            return AppendPaintFillStroke(lowering.Lower(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
        }
        SvgParsedGeometry solid = lowering.Lower(default, painted && !gradient.IsEmpty, transform, frame);
        SvgGeometry[] bands = solid.Storage.Items;
        for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
        {
            context.WorkCounter.Consume(context.CancellationToken);
            bands[bandIndex] = SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, bands[bandIndex].Rectangle, opacity, null));
        }
        return AppendPaintFillStroke(solid, reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
    }

    /// <summary>Appends an admitted solid stroke outline to paint-filled bands and outlines, leaving unadmitted strokes to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintFillStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, AxisTransform transform, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape)
    {
        if (stroke is null) return filled;
        try
        {
            SvgParsedGeometry stroked;
            if (isPath)
            {
                string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
                (int commandCount, bool hasArc, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
                using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                stroked = strokeLowering.LowerStrokePath(d, commandCount, hasArc, hasCurve, strokeShape, stroke.Value.Color, strokeShape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, budget);
            }
            else
            {
                using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
                using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, false, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
                stroked = strokeLowering.LowerStrokePolyline(points, elementName == "polygon", strokeShape.Transform, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, stroke.Value.Color, strokeShape.Presentation.IsPainted, frame, budget);
            }
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
        }
        catch { filled.Dispose(); throw; }
    }

    /// <summary>Computes the device paint box from retained contour points for bounding-box evaluation.</summary>
    internal static SvgRectangle BoxFromGradientBands(BudgetedParserList<SvgPoint> points, AxisTransform transform, SvgViewportFrame? frame, SvgPaintableElementContext context)
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

    internal static bool IsGradientStrokeUrl(string? stroke)
        => stroke is not null && stroke.StartsWith("url(", StringComparison.Ordinal);

    internal static SvgLinearGradient MapUserSpaceVector(AxisTransform transform, SvgLinearGradient gradient, SvgViewportFrame? frame)
    {
        if (!gradient.IsUserSpace) return gradient;
        (double mappedX1, double mappedY1) = transform.ApplyToUserPoint(gradient.X1, gradient.Y1);
        (double mappedX2, double mappedY2) = transform.ApplyToUserPoint(gradient.X2, gradient.Y2);
        double referenceScaleX = transform.ScaleX;
        double referenceScaleY = transform.ScaleY;
        if (frame is not null)
        {
            mappedX1 = mappedX1 * frame.Value.ScaleX + frame.Value.OffsetX;
            mappedY1 = mappedY1 * frame.Value.ScaleY + frame.Value.OffsetY;
            mappedX2 = mappedX2 * frame.Value.ScaleX + frame.Value.OffsetX;
            mappedY2 = mappedY2 * frame.Value.ScaleY + frame.Value.OffsetY;
            referenceScaleX *= frame.Value.ScaleX;
            referenceScaleY *= frame.Value.ScaleY;
        }
        if (!double.IsFinite(mappedX1) || !double.IsFinite(mappedY1) || !double.IsFinite(mappedX2) || !double.IsFinite(mappedY2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported viewport map exceeds the gradient coordinate range.");
        return gradient with { X1 = mappedX1, Y1 = mappedY1, X2 = mappedX2, Y2 = mappedY2, ReferenceScaleX = gradient.ReferenceScaleX * referenceScaleX, ReferenceScaleY = gradient.ReferenceScaleY * referenceScaleY };
    }

    /// <summary>Maps one userSpaceOnUse radial circle and its focal point through the referencing transform, recording scales for inverse evaluation.</summary>
    internal static SvgRadialGradient MapUserSpaceRadial(AxisTransform transform, SvgRadialGradient gradient, SvgViewportFrame? frame)
    {
        if (!gradient.IsUserSpace) return gradient;
        (double mappedX, double mappedY) = transform.ApplyToUserPoint(gradient.CenterX, gradient.CenterY);
        (double mappedFocalX, double mappedFocalY) = transform.ApplyToUserPoint(gradient.FocalX, gradient.FocalY);
        double mappedRadius = gradient.Radius * Math.Abs(transform.ScaleX);
        double mappedFocalRadius = gradient.FocalRadius * Math.Abs(transform.ScaleX);
        double referenceScaleX = transform.ScaleX;
        double referenceScaleY = transform.ScaleY;
        if (frame is not null)
        {
            mappedX = mappedX * frame.Value.ScaleX + frame.Value.OffsetX;
            mappedY = mappedY * frame.Value.ScaleY + frame.Value.OffsetY;
            mappedFocalX = mappedFocalX * frame.Value.ScaleX + frame.Value.OffsetX;
            mappedFocalY = mappedFocalY * frame.Value.ScaleY + frame.Value.OffsetY;
            mappedRadius *= Math.Abs(frame.Value.ScaleX);
            mappedFocalRadius *= Math.Abs(frame.Value.ScaleX);
            referenceScaleX *= frame.Value.ScaleX;
            referenceScaleY *= frame.Value.ScaleY;
        }
        if (!double.IsFinite(mappedRadius) || !double.IsFinite(mappedFocalRadius) || !double.IsFinite(mappedFocalX) || !double.IsFinite(mappedFocalY) || !double.IsFinite(mappedX) || !double.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported viewport map exceeds the radial gradient coordinate range.");
        return gradient with { CenterX = mappedX, CenterY = mappedY, Radius = mappedRadius, FocalX = mappedFocalX, FocalY = mappedFocalY, FocalRadius = mappedFocalRadius, ReferenceScaleX = gradient.ReferenceScaleX * referenceScaleX, ReferenceScaleY = gradient.ReferenceScaleY * referenceScaleY };
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

    internal static SvgParsedGeometry LowerGradientStrokeRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (style.Stroke is not null && state.RadialGradients.TryGetValue(ParseGradientId(style.Stroke), out _)) return LowerRadialStrokeRect(reader, context, style, state, frame);
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.WorkCounter, context.CancellationToken);
        if (state.Patterns.TryGetValue(ParseGradientId(style.Stroke), out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern strokes are not implemented.");
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Stroke), out SvgLinearGradient gradient)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        gradient = MapUserSpaceVector(transform, gradient, frame);
        SvgLinearGradient fillGradient = default;
        SvgRadialGradient fillCircle = default;
        bool hasLinearFill = false;
        bool hasRadialFill = false;
        if (IsGradientFill(style.Fill))
        {
            string fillId = ParseGradientId(style.Fill);
            if (state.Gradients.TryGetValue(fillId, out SvgLinearGradient resolvedFill))
            {
                fillGradient = MapUserSpaceVector(transform, resolvedFill, frame);
                fillGradient = MirrorBoundingBoxVector(transform, fillGradient);
                hasLinearFill = true;
            }
            else if (state.RadialGradients.TryGetValue(fillId, out SvgRadialGradient resolvedCircle))
            {
                fillCircle = MapUserSpaceRadial(transform, resolvedCircle, frame);
                hasRadialFill = true;
            }
            else if (state.Patterns.TryGetValue(fillId, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills with gradient strokes are not implemented.");
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        }
        bool combined = hasLinearFill || hasRadialFill;
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool painted;
        byte opacity;
        SvgColor fillColor = default;
        bool hasFill;
        if (combined)
        {
            painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
            opacity = SvgColorParser.ParseOpacity(style.Opacity);
            hasFill = painted;
        }
        else
        {
            SvgResolvedPresentation presentation = SvgPresentationResolver.Resolve(style, context.InheritedPresentation.Paint, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible, context.NamedColors);
            painted = presentation.IsPainted;
            opacity = presentation.Opacity;
            fillColor = SvgPaintResolver.ResolveFillColor(presentation.Paint, opacity);
            hasFill = presentation.Paint.HasFill && painted;
        }
        SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, 255, painted);
        if (probe is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width gradient strokes no larger than the rectangle dimensions are implemented.");
        double halfWidth = probe.Value.Width / 2d;
        int strokeWidth = probe.Value.Width;
        SvgRectangle top = new(placement.X - halfWidth, placement.Y - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
        SvgRectangle bottom = new(placement.X - halfWidth, placement.Y + placement.Height - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
        SvgRectangle left = new(placement.X - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        SvgRectangle right = new(placement.X + placement.Width - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        SvgRectangle box = placement;
        byte fillOpacity = combined ? SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.FillOpacity)) : opacity;
        bool fillHasFill = hasFill && !gradient.IsEmpty;
        if (hasLinearFill) fillHasFill = hasFill && !fillGradient.IsEmpty;
        else if (hasRadialFill) fillHasFill = hasFill && !fillCircle.IsEmpty;
        SvgRectangle fill = placement with { Color = fillColor, HasFill = fillHasFill };
        if (frame is not null)
        {
            if (!frame.Value.Clip.Contains(placement.X - halfWidth, placement.Y - halfWidth, placement.X + placement.Width + halfWidth, placement.Y + placement.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            SvgRectangle shifted = frame.Value.Shift(placement);
            fill = frame.Value.Clip.ClipRectangle(shifted) with { Color = fillColor, HasFill = fillHasFill };
            box = frame.Value.Shift(box);
            top = frame.Value.Clip.ClipRectangle(frame.Value.Shift(top));
            bottom = frame.Value.Clip.ClipRectangle(frame.Value.Shift(bottom));
            left = frame.Value.Clip.ClipRectangle(frame.Value.Shift(left));
            right = frame.Value.Clip.ClipRectangle(frame.Value.Shift(right));
        }
        using BudgetedParserList<SvgGeometry> stroked = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        if (hasLinearFill) stroked.Add(LowerLinearFillGeometry(fillGradient, box, fill, fillHasFill, fillOpacity));
        else if (hasRadialFill) stroked.Add(LowerRadialFillGeometry(fillCircle, box, fill, fillHasFill, fillOpacity));
        SvgRectangle strokeFill = combined ? fill with { HasFill = false } : fill;
        if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Stops.Length > 1 && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient strokes are implemented.");
            SvgStroke solidStroke = new(SvgPaintResolver.ApplyOpacity(gradient.End, SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.StrokeOpacity))), strokeWidth);
            stroked.Add(SvgGeometry.FromRectangle(strokeFill with { Stroke = painted ? solidStroke : null }));
            return new SvgParsedGeometry(stroked.Detach(), 0);
        }
        stroked.Add(SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(strokeFill, top, bottom, left, right, gradient, box, opacity, painted)));
        return new SvgParsedGeometry(stroked.Detach(), 0);
    }

    /// <summary>Lowers one gradient-stroked axis-aligned line into a gradient-evaluated stroke band.</summary>
    /// <summary>Lowers one radial-gradient-stroked rectangle into one solid fill plus four radial-evaluated stroke bands sharing the placement box.</summary>
    internal static SvgParsedGeometry LowerRadialStrokeRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.WorkCounter, context.CancellationToken);
        if (state.Patterns.TryGetValue(ParseGradientId(style.Stroke), out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern strokes are not implemented.");
        if (!state.RadialGradients.TryGetValue(ParseGradientId(style.Stroke), out SvgRadialGradient circle)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        AxisTransform transform = context.ResolveTransform(reader);
        circle = MapUserSpaceRadial(transform, circle, frame);
        SvgLinearGradient fillGradient = default;
        SvgRadialGradient fillCircle = default;
        bool hasLinearFill = false;
        bool hasRadialFill = false;
        if (IsGradientFill(style.Fill))
        {
            string fillId = ParseGradientId(style.Fill);
            if (state.Gradients.TryGetValue(fillId, out SvgLinearGradient resolvedFill))
            {
                fillGradient = MapUserSpaceVector(transform, resolvedFill, frame);
                fillGradient = MirrorBoundingBoxVector(transform, fillGradient);
                hasLinearFill = true;
            }
            else if (state.RadialGradients.TryGetValue(fillId, out SvgRadialGradient resolvedCircle))
            {
                fillCircle = MapUserSpaceRadial(transform, resolvedCircle, frame);
                hasRadialFill = true;
            }
            else if (state.Patterns.TryGetValue(fillId, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills with gradient strokes are not implemented.");
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        }
        bool combined = hasLinearFill || hasRadialFill;
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool painted;
        byte opacity;
        SvgColor fillColor = default;
        bool hasFill;
        if (combined)
        {
            painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
            opacity = SvgColorParser.ParseOpacity(style.Opacity);
            hasFill = painted;
        }
        else
        {
            SvgResolvedPresentation presentation = SvgPresentationResolver.Resolve(style, context.InheritedPresentation.Paint, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible, context.NamedColors);
            painted = presentation.IsPainted;
            opacity = presentation.Opacity;
            fillColor = SvgPaintResolver.ResolveFillColor(presentation.Paint, opacity);
            hasFill = presentation.Paint.HasFill && painted;
        }
        SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, 255, painted);
        if (probe is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width gradient strokes no larger than the rectangle dimensions are implemented.");
        double halfWidth = probe.Value.Width / 2d;
        int strokeWidth = probe.Value.Width;
        SvgRectangle top = new(placement.X - halfWidth, placement.Y - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
        SvgRectangle bottom = new(placement.X - halfWidth, placement.Y + placement.Height - halfWidth, placement.Width + strokeWidth, strokeWidth, default, true, null);
        SvgRectangle left = new(placement.X - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        SvgRectangle right = new(placement.X + placement.Width - halfWidth, placement.Y + halfWidth, strokeWidth, placement.Height - strokeWidth, default, true, null);
        SvgRectangle box = placement;
        byte fillOpacity = combined ? SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.FillOpacity)) : opacity;
        bool fillHasFill = hasFill && !circle.IsEmpty;
        if (hasLinearFill) fillHasFill = hasFill && !fillGradient.IsEmpty;
        else if (hasRadialFill) fillHasFill = hasFill && !fillCircle.IsEmpty;
        SvgRectangle fill = placement with { Color = fillColor, HasFill = fillHasFill };
        if (frame is not null)
        {
            if (!frame.Value.Clip.Contains(placement.X - halfWidth, placement.Y - halfWidth, placement.X + placement.Width + halfWidth, placement.Y + placement.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            SvgRectangle shifted = frame.Value.Shift(placement);
            fill = frame.Value.Clip.ClipRectangle(shifted) with { Color = fillColor, HasFill = fillHasFill };
            box = frame.Value.Shift(box);
            top = frame.Value.Clip.ClipRectangle(frame.Value.Shift(top));
            bottom = frame.Value.Clip.ClipRectangle(frame.Value.Shift(bottom));
            left = frame.Value.Clip.ClipRectangle(frame.Value.Shift(left));
            right = frame.Value.Clip.ClipRectangle(frame.Value.Shift(right));
        }
        using BudgetedParserList<SvgGeometry> stroked = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        if (hasLinearFill) stroked.Add(LowerLinearFillGeometry(fillGradient, box, fill, fillHasFill, fillOpacity));
        else if (hasRadialFill) stroked.Add(LowerRadialFillGeometry(fillCircle, box, fill, fillHasFill, fillOpacity));
        else if (circle.Radius != 0 || circle.IsEmpty) stroked.Add(SvgGeometry.FromRectangle(fill));
        SvgRectangle strokeFill = combined ? fill with { HasFill = false } : fill;
        if (circle.Radius == 0 && !circle.IsEmpty)
        {
            if (circle.Stops.Length > 1 && circle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
            SvgStroke solidStroke = new(SvgPaintResolver.ApplyOpacity(circle.End, SvgReferenceResolver.MultiplyAlpha(opacity, SvgColorParser.ParseOpacity(style.StrokeOpacity))), strokeWidth);
            stroked.Add(SvgGeometry.FromRectangle(strokeFill with { Stroke = painted ? solidStroke : null }));
            return new SvgParsedGeometry(stroked.Detach(), 0);
        }
        bool paintBands = painted && !circle.IsEmpty;
        stroked.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(circle, box, top with { HasFill = paintBands }, opacity, null)));
        stroked.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(circle, box, bottom with { HasFill = paintBands }, opacity, null)));
        stroked.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(circle, box, left with { HasFill = paintBands }, opacity, null)));
        stroked.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(circle, box, right with { HasFill = paintBands }, opacity, null)));
        return new SvgParsedGeometry(stroked.Detach(), 0);
    }

    internal static SvgParsedGeometry LowerGradientStrokeLine(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgLineSyntax syntax, SvgDocumentState state, SvgViewportFrame? frame)
    {
        // Round caps without dashes lower through the paint-specific round-cap path below; everything else keeps the band gate.
        if (SvgStrokeStyleSyntax.ParseDasharray(reader.GetAttribute("stroke-dasharray")) is not null || SvgStrokeStyleSyntax.ParseLinecap(reader.GetAttribute("stroke-linecap")) != SvgStrokeCap.Round) SvgStrokeStyleSyntax.RequireButtOrSquareCapJoin(reader, "line", context.WorkCounter, context.CancellationToken);
        AxisTransform transform = context.ResolveTransform(reader);
        if (transform != AxisTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        if ((syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(style.Width ?? syntax.StrokeWidth ?? "1");
        if (strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width line strokes are implemented.");
        if (style.Stroke is not null && state.RadialGradients.TryGetValue(ParseGradientId(style.Stroke), out _)) return LowerRadialStrokeLine(reader, context, style, syntax, state, frame);
        double[]? dashEntries = SvgStrokeStyleSyntax.ParseDasharray(style.Dasharray);
        SvgStrokeCap dashLinecap = SvgStrokeStyleSyntax.ParseLinecap(style.Linecap);
        if (dashLinecap == SvgStrokeCap.Square && dashEntries is null) syntax = syntax.WithSquareCaps(strokeWidth);
        double? pathLength = dashEntries is null ? null : SvgStrokeStyleSyntax.ParsePathLength(style.PathLength ?? reader.GetAttribute("pathLength"));
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Stroke), out SvgLinearGradient gradient)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        gradient = MapUserSpaceVector(transform, gradient, frame);
        if (!gradient.IsUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse gradient strokes on lines are implemented.");
        gradient = MirrorBoundingBoxVector(transform, gradient);
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.StrokeOpacity));
        if (dashLinecap == SvgStrokeCap.Square && dashEntries is not null && !IsOpaqueGradientStroke(gradient, opacity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent square-capped dashed lines are not implemented.");
        if (dashLinecap == SvgStrokeCap.Round && dashEntries is null)
        {
            if (!IsOpaqueGradientStroke(gradient, opacity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent round-capped paint lines are not implemented.");
            return LowerRoundCapGradientStrokeLine(syntax, strokeWidth, style, transform, gradient, painted, opacity, frame, state, context);
        }
        if (dashEntries is not null) return LowerDashedGradientStrokeLine(syntax, strokeWidth, dashLinecap == SvgStrokeCap.Square, dashEntries, SvgStrokeStyleSyntax.ParseDashoffset(style.Dashoffset), pathLength, gradient, painted, opacity, frame, state, context);
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
            return RetainSingleBand(SvgGeometry.FromOrthogonalBand(fill with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, opacity) }), state, context);
        }
        return RetainSingleBand(SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, opacity, null)), state, context);
    }

    /// <summary>Reports whether one linear-gradient line stroke paints fully opaque runs.</summary>
    internal static bool IsOpaqueGradientStroke(SvgLinearGradient gradient, byte opacity)
    {
        if (opacity != 255 || gradient.Start.Alpha != 255 || gradient.End.Alpha != 255) return false;
        foreach (SvgGradientStop stop in gradient.Stops) if (stop.Color.Alpha != 255) return false;
        return true;
    }

    /// <summary>Lowers one round-capped linear-gradient line stroke into one gradient-evaluated band with two repainted tip discs.</summary>
    private static SvgParsedGeometry LowerRoundCapGradientStrokeLine(SvgLineSyntax syntax, int strokeWidth, SvgComputedStyle style, AxisTransform transform, SvgLinearGradient gradient, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        bool degenerate = gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty;
        if (degenerate && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
        (SvgParsedGeometry white, SvgRectangle strokeBox) = SvgLeafParser.LowerWhiteRoundCapLine(syntax, strokeWidth, style, transform, painted, painted && !gradient.IsEmpty, frame, state, context);
        try
        {
            SvgColor solid = SvgPaintResolver.ApplyOpacity(gradient.End, opacity);
            using BudgetedParserList<SvgGeometry> repainted = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            foreach (SvgGeometry existing in white.Geometries)
            {
                if (existing.Kind == SvgGeometryKind.OrthogonalBand) repainted.Add(degenerate ? SvgGeometry.FromOrthogonalBand(existing.Rectangle with { Color = solid }) : SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, strokeBox, existing.Rectangle, opacity, null)));
                else if (degenerate) repainted.Add(SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(existing.Polygon.Points, existing.Polygon.ContourEnds, existing.Polygon.Signs, solid, existing.Polygon.HasFill, existing.Polygon.IsEvenOdd, existing.Polygon.Mapping, existing.Polygon.Clip, state.Budget)));
                else repainted.Add(SvgGradientPolygon.RepaintOutline(existing.Polygon, gradient, strokeBox, opacity, state.Budget));
            }
            white.Dispose();
            return new SvgParsedGeometry(repainted.Detach(), white.ElementCount);
        }
        catch { white.Dispose(); throw; }
    }
    /// <summary>Lowers one dashed linear-gradient line stroke into one gradient-evaluated band per dash run.</summary>
    private static SvgParsedGeometry LowerDashedGradientStrokeLine(SvgLineSyntax syntax, int strokeWidth, bool squareCaps, double[] dashes, double dashOffset, double? pathLength, SvgLinearGradient gradient, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        using BudgetedParserList<SvgRectangle> runRects = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        bool paintsAny = SvgLeafParser.AppendLineDashRunRects(syntax, strokeWidth, squareCaps, dashes, dashOffset, pathLength, frame, state.Budget, context.WorkCounter, context.CancellationToken, runRects);
        using BudgetedParserList<SvgGeometry> runs = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        bool degenerate = gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty;
        if (degenerate && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
        for (int runIndex = 0; runIndex < runRects.Count; runIndex++)
        {
            SvgRectangle run = runRects[runIndex];
            if (degenerate) runs.Add(SvgGeometry.FromOrthogonalBand(run with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, opacity), HasFill = paintsAny && painted }));
            else runs.Add(SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, run, run with { HasFill = paintsAny && painted && !gradient.IsEmpty }, opacity, null)));
        }
        return new SvgParsedGeometry(runs.Detach(), 0);
    }

    private static SvgParsedGeometry RetainSingleBand(SvgGeometry geometry, SvgDocumentState state, SvgPaintableElementContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        using BudgetedParserList<SvgGeometry> retained = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        retained.Add(geometry);
        return new SvgParsedGeometry(retained.Detach(), 0);
    }

    /// <summary>Lowers one radial-gradient-stroked axis-aligned line into a radial-evaluated stroke band.</summary>
    internal static SvgParsedGeometry LowerRadialStrokeLine(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgLineSyntax syntax, SvgDocumentState state, SvgViewportFrame? frame)
    {
        // Round caps without dashes lower through the paint-specific round-cap path below; everything else keeps the band gate.
        if (SvgStrokeStyleSyntax.ParseDasharray(reader.GetAttribute("stroke-dasharray")) is not null || SvgStrokeStyleSyntax.ParseLinecap(reader.GetAttribute("stroke-linecap")) != SvgStrokeCap.Round) SvgStrokeStyleSyntax.RequireButtOrSquareCapJoin(reader, "line", context.WorkCounter, context.CancellationToken);
        AxisTransform transform = context.ResolveTransform(reader);
        if (transform != AxisTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        if ((syntax.X1 != syntax.X2 && syntax.Y1 != syntax.Y2) || (syntax.X1 == syntax.X2 && syntax.Y1 == syntax.Y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
        int strokeWidth = SvgGeometrySyntax.ParseInteger(style.Width ?? syntax.StrokeWidth ?? "1");
        if (strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width line strokes are implemented.");
        double[]? dashEntries = SvgStrokeStyleSyntax.ParseDasharray(style.Dasharray);
        SvgStrokeCap dashLinecap = SvgStrokeStyleSyntax.ParseLinecap(style.Linecap);
        if (dashLinecap == SvgStrokeCap.Square && dashEntries is null) syntax = syntax.WithSquareCaps(strokeWidth);
        double? pathLength = dashEntries is null ? null : SvgStrokeStyleSyntax.ParsePathLength(style.PathLength ?? reader.GetAttribute("pathLength"));
        if (state.Patterns.TryGetValue(ParseGradientId(style.Stroke), out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern strokes are not implemented.");
        if (!state.RadialGradients.TryGetValue(ParseGradientId(style.Stroke), out SvgRadialGradient circle)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        circle = MapUserSpaceRadial(transform, circle, frame);
        if (!circle.IsUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse gradient strokes on lines are implemented.");
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.StrokeOpacity));
        if (dashLinecap == SvgStrokeCap.Square && dashEntries is not null && !IsOpaqueRadialStroke(circle, opacity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent square-capped dashed lines are not implemented.");
        if (dashLinecap == SvgStrokeCap.Round && dashEntries is null)
        {
            if (!IsOpaqueRadialStroke(circle, opacity)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent round-capped paint lines are not implemented.");
            return LowerRoundCapRadialStrokeLine(syntax, strokeWidth, style, transform, circle, painted, opacity, frame, state, context);
        }
        if (dashEntries is not null) return LowerDashedRadialStrokeLine(syntax, strokeWidth, dashLinecap == SvgStrokeCap.Square, dashEntries, SvgStrokeStyleSyntax.ParseDashoffset(style.Dashoffset), pathLength, circle, painted, opacity, frame, state, context);
        SvgRectangle band = new SvgAxisAlignedLine(syntax.X1, syntax.Y1, syntax.X2, syntax.Y2, default, painted, strokeWidth).ToStrokeBand();
        SvgRectangle box = band;
        if (frame is not null)
        {
            band = frame.Value.ClipShifted(band);
            box = frame.Value.Shift(box);
        }
        SvgRectangle fill = band with { HasFill = painted && !circle.IsEmpty };
        if (circle.Radius == 0 && !circle.IsEmpty)
        {
            if (circle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
            return RetainSingleBand(SvgGeometry.FromOrthogonalBand(fill with { Color = SvgPaintResolver.ApplyOpacity(circle.End, opacity) }), state, context);
        }
        return RetainSingleBand(SvgGeometry.FromRadialFill(new SvgRadialGeometry(circle, box, fill, opacity, null)), state, context);
    }

    /// <summary>Reports whether one radial-gradient line stroke paints fully opaque runs.</summary>
    internal static bool IsOpaqueRadialStroke(SvgRadialGradient circle, byte opacity)
    {
        if (opacity != 255 || circle.Start.Alpha != 255 || circle.End.Alpha != 255) return false;
        foreach (SvgGradientStop stop in circle.Stops) if (stop.Color.Alpha != 255) return false;
        return true;
    }

    /// <summary>Lowers one round-capped radial-gradient line stroke into one radial-evaluated band with two repainted tip discs.</summary>
    private static SvgParsedGeometry LowerRoundCapRadialStrokeLine(SvgLineSyntax syntax, int strokeWidth, SvgComputedStyle style, AxisTransform transform, SvgRadialGradient circle, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        bool degenerate = circle.Radius == 0 && !circle.IsEmpty;
        if (degenerate && circle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
        (SvgParsedGeometry white, SvgRectangle strokeBox) = SvgLeafParser.LowerWhiteRoundCapLine(syntax, strokeWidth, style, transform, painted, painted && !circle.IsEmpty, frame, state, context);
        try
        {
            SvgColor solid = SvgPaintResolver.ApplyOpacity(circle.End, opacity);
            using BudgetedParserList<SvgGeometry> repainted = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            foreach (SvgGeometry existing in white.Geometries)
            {
                if (existing.Kind == SvgGeometryKind.OrthogonalBand) repainted.Add(degenerate ? SvgGeometry.FromOrthogonalBand(existing.Rectangle with { Color = solid }) : SvgGeometry.FromRadialFill(new SvgRadialGeometry(circle, strokeBox, existing.Rectangle, opacity, null)));
                else if (degenerate) repainted.Add(SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(existing.Polygon.Points, existing.Polygon.ContourEnds, existing.Polygon.Signs, solid, existing.Polygon.HasFill, existing.Polygon.IsEvenOdd, existing.Polygon.Mapping, existing.Polygon.Clip, state.Budget)));
                else repainted.Add(SvgRadialPolygon.RepaintOutline(existing.Polygon, circle, strokeBox, opacity, state.Budget));
            }
            white.Dispose();
            return new SvgParsedGeometry(repainted.Detach(), white.ElementCount);
        }
        catch { white.Dispose(); throw; }
    }
    /// <summary>Lowers one dashed radial-gradient line stroke into one radial-evaluated band per dash run.</summary>
    private static SvgParsedGeometry LowerDashedRadialStrokeLine(SvgLineSyntax syntax, int strokeWidth, bool squareCaps, double[] dashes, double dashOffset, double? pathLength, SvgRadialGradient circle, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        using BudgetedParserList<SvgRectangle> runRects = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        bool paintsAny = SvgLeafParser.AppendLineDashRunRects(syntax, strokeWidth, squareCaps, dashes, dashOffset, pathLength, frame, state.Budget, context.WorkCounter, context.CancellationToken, runRects);
        using BudgetedParserList<SvgGeometry> runs = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        bool degenerate = circle.Radius == 0 && !circle.IsEmpty;
        if (degenerate && circle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
        for (int runIndex = 0; runIndex < runRects.Count; runIndex++)
        {
            SvgRectangle run = runRects[runIndex];
            if (degenerate) runs.Add(SvgGeometry.FromOrthogonalBand(run with { Color = SvgPaintResolver.ApplyOpacity(circle.End, opacity), HasFill = paintsAny && painted }));
            else runs.Add(SvgGeometry.FromRadialFill(new SvgRadialGeometry(circle, run, run with { HasFill = paintsAny && painted && !circle.IsEmpty }, opacity, null)));
        }
        return new SvgParsedGeometry(runs.Detach(), 0);
    }

    /// <summary>Lowers one linear gradient fill rectangle shared by combined fill-and-stroke lowerings, mirroring the fill-only lowering.</summary>
    internal static SvgGeometry LowerLinearFillGeometry(SvgLinearGradient fillGradient, SvgRectangle box, SvgRectangle fill, bool fillHasFill, byte fillOpacity)
    {
        if (fillGradient.Stops.Length == 1) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(fillGradient.End, fillOpacity), HasFill = fillHasFill });
        if (fillGradient.X1 == fillGradient.X2 && fillGradient.Y1 == fillGradient.Y2 && !fillGradient.IsEmpty)
        {
            if (fillGradient.Stops.Length > 1 && fillGradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
            return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(fillGradient.End, fillOpacity), HasFill = fillHasFill });
        }
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(fillGradient, box, fill with { HasFill = fillHasFill }, fillOpacity, null));
    }

    /// <summary>Lowers one radial gradient fill rectangle shared by combined fill-and-stroke lowerings, mirroring the fill-only lowering.</summary>
    internal static SvgGeometry LowerRadialFillGeometry(SvgRadialGradient fillCircle, SvgRectangle box, SvgRectangle fill, bool fillHasFill, byte fillOpacity)
    {
        if (fillCircle.Stops.Length == 1) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(fillCircle.End, fillOpacity), HasFill = fillHasFill });
        if (fillCircle.Radius == 0 && !fillCircle.IsEmpty) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(fillCircle.End, fillOpacity), HasFill = fillHasFill });
        return SvgGeometry.FromRadialFill(new SvgRadialGeometry(fillCircle, box, fill with { HasFill = fillHasFill }, fillOpacity, null));
    }

    private static SvgStroke? ParseGradientStroke(XmlReader reader, SvgComputedStyle style, SvgPaintableElementContext context, AxisTransform transform, SvgRectangle placement, byte opacity, bool painted)
    {
        if (style.Stroke is null || style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (IsGradientStrokeUrl(style.Stroke)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient fills with gradient strokes are not implemented.");
        SvgColor strokeColor = SvgPaintResolver.ResolveStrokeColor(style.Stroke, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        if (style.StrokeOpacity is not null) strokeColor = SvgPaintResolver.ApplyOpacity(strokeColor, SvgColorParser.ParseOpacity(style.StrokeOpacity));
        return SvgLeafParser.ParseRectangleStroke(strokeColor, style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.X, placement.Y, placement.Width, placement.Height, opacity, painted);
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
