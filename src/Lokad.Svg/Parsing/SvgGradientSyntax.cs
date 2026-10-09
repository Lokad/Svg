using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one admitted one-to-eight-stop linear gradient with its vector and referencing scale record.</summary>
/// <remarks>ReferenceScaleX and ReferenceScaleY multiply every scale map applied to the stored endpoints after the local vector definition, so raster evaluation inverts the exact paint map.</remarks>
internal readonly record struct SvgLinearGradient(SvgColor Start, SvgColor End, double X1, double Y1, double X2, double Y2, double Offset1, double Offset2, bool IsEmpty, bool IsUserSpace, SvgGradientSpread Spread, SvgGradientStop[] Stops, double ReferenceScaleX, double ReferenceScaleY, bool HasRotation)
{
    internal SvgLinearPaintMap? SourcePaint { get; init; }
}

/// <summary>Identifies the admitted stop-range extension rule.</summary>
internal enum SvgGradientSpread { Pad, Repeat, Reflect }

/// <summary>Retains one parsed gradient stop with its offset while streaming.</summary>
/// <remarks>Alpha combines byte-encoded color alpha with validated authored stop opacity for shader sampling. Color keeps the existing byte representation for admission and degenerate solid lowering. The float fits the former padding on x64; parser array reservations use the actual runtime size.</remarks>
internal readonly record struct SvgGradientStop(double Offset, SvgColor Color)
{
    internal float Alpha { get; init; } = Color.Alpha * (1f / 255f);
}

/// <summary>Retains one parsed linear-gradient definition with attribute explicitness for href inheritance.</summary>
internal readonly record struct SvgGradientTemplate(double X1, double Y1, double X2, double Y2, bool IsUserSpace, SvgGradientTransform Transform, bool HasTransform, int VectorCount, bool HasUnits, SvgGradientSpread Spread, bool HasSpread, string? Href, bool HasX1, bool HasY1, bool HasX2, bool HasY2);

/// <summary>Retains one admitted one-to-eight-stop radial gradient with a strictly inside focal point, contained focal circle and referencing scale record, with an inverse paint map for general transforms.</summary>
/// <remarks>ReferenceScaleX and ReferenceScaleY multiply every scale map applied to the stored circle after the local definition, so raster evaluation inverts the exact paint map under non-uniform referencing transforms.</remarks>
internal readonly record struct SvgRadialGradient(SvgColor Start, SvgColor End, double CenterX, double CenterY, double Radius, double FocalX, double FocalY, double FocalRadius, double Offset1, double Offset2, bool IsEmpty, bool IsUserSpace, SvgGradientSpread Spread, SvgGradientStop[] Stops, double ReferenceScaleX, double ReferenceScaleY, bool HasRotation, SvgGradientTransform Inverse, bool HasGeneralTransform)
{
    /// <summary>Retains the local shader matrix separately from referencing maps so bitmap translation precedes their float concatenation.</summary>
    internal SvgGradientPaintMatrices? NativePaintMatrices { get; init; }

    /// <summary>Covers the retained paint value and conservative owner/header overhead before a scene object is allocated.</summary>
    internal static long RetainedStorageBytes => 64L + System.Runtime.CompilerServices.Unsafe.SizeOf<SvgRadialGradient>();
}

/// <summary>Retains one parsed radial-gradient definition with attribute explicitness for href inheritance.</summary>
internal readonly record struct SvgRadialTemplate(double CenterX, double CenterY, double Radius, double FocalX, double FocalY, double FocalRadius, bool IsUserSpace, SvgGradientTransform Transform, bool HasTransform, bool HasUnits, SvgGradientSpread Spread, bool HasSpread, string? Href, int VectorCount, bool HasCenterX, bool HasCenterY, bool HasRadius, bool HasFocalX, bool HasFocalY, bool HasFocalRadius);

/// <summary>Retains one linear-gradient href inheritance whose target was missing at registration for document-end resolution.</summary>
internal readonly record struct PendingLinearGradientHref(string Id, string HrefId, SvgGradientTemplate Template, SvgGradientStop[] Stops);

/// <summary>Retains one radial-gradient href inheritance whose target was missing at registration for document-end resolution.</summary>
internal readonly record struct PendingRadialGradientHref(string Id, string HrefId, SvgRadialTemplate Template, SvgGradientStop[] Stops);

/// <summary>Retains one admitted affine gradient transform with finite coordinates.</summary>
/// <remarks>Coefficients follow the SVG matrix(a b c d e f) convention shared with contour transforms: x prime equals A times x plus C times y plus E, and y prime equals B times x plus D times y plus F. Composition applies the inner transform first. Diagonal inputs evaluate bitwise-identically to the former scale-plus-offset form.</remarks>
internal readonly record struct SvgGradientTransform(double A, double B, double C, double D, double E, double F)
{
    internal static SvgGradientTransform Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>Reports whether the columns are orthogonal with a shared length, covering rotation, reflection and uniform scales.</summary>
    /// <remarks>Mirrors the contour uniform-scale check with an orthogonality requirement added: exact equality first with a relative tolerance fallback, so composed rotations admit exactly while skew and non-uniform rotation stay gated. Such maps send circles to circles, which the radial evaluator handles without ellipse math.</remarks>
    internal bool IsSimilarity()
    {
        double first = Math.Sqrt(A * A + B * B);
        double second = Math.Sqrt(C * C + D * D);
        double dot = A * C + B * D;
        if (!double.IsFinite(first) || !double.IsFinite(second) || !double.IsFinite(dot)) return false;
        if (first == second && dot == 0) return true;
        double magnitude = Math.Max(first, second);
        if (magnitude == 0) return false;
        if (Math.Abs(first - second) > 1e-9 * magnitude) return false;
        return Math.Abs(dot) <= 1e-9 * magnitude * magnitude;
    }

    /// <summary>Maps one gradient vector endpoint, rejecting non-finite results.</summary>
    internal (double X, double Y) Apply(double x, double y)
    {
        double mappedX = A * x + C * y + E;
        double mappedY = B * x + D * y + F;
        if (!double.IsFinite(mappedX) || !double.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported gradient transform exceeds the gradient coordinate range.");
        return (mappedX, mappedY);
    }


    /// <summary>Composes two gradient transforms, applying the inner transform first.</summary>
    internal static SvgGradientTransform Compose(SvgGradientTransform outer, SvgGradientTransform inner)
        => new(outer.A * inner.A + outer.C * inner.B, outer.B * inner.A + outer.D * inner.B, outer.A * inner.C + outer.C * inner.D, outer.B * inner.C + outer.D * inner.D, outer.A * inner.E + outer.C * inner.F + outer.E, outer.B * inner.E + outer.D * inner.F + outer.F);

    /// <summary>Inverts this gradient transform when its determinant is nonzero, reporting singular or overflowing maps as missing.</summary>
    internal bool TryInvert(out SvgGradientTransform inverse)
    {
        double determinant = A * D - B * C;
        if (determinant == 0 || !double.IsFinite(determinant)) { inverse = Identity; return false; }
        double a = D / determinant;
        double b = -B / determinant;
        double c = -C / determinant;
        double d = A / determinant;
        double e = (C * F - D * E) / determinant;
        double f = (B * E - A * F) / determinant;
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c) || !double.IsFinite(d) || !double.IsFinite(e) || !double.IsFinite(f)) { inverse = Identity; return false; }
        inverse = new SvgGradientTransform(a, b, c, d, e, f);
        return true;
    }
}

/// <summary>Parses the bounded linear-gradient definition syntax and lowers gradient-filled rectangles.</summary>
internal static class SvgGradientSyntax
{
    /// <summary>Bounds retained linear-gradient stops; the per-entry storage charge covers exactly this many.</summary>
    internal const int MaximumGradientStops = 8;

    internal static SvgGradientTemplate ParseLinearVector(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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
        SvgGradientTransform gradientTransform = ParseGradientTransform(transformValue, workCounter, cancellationToken);
        string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        return new SvgGradientTemplate(x1, y1, x2, y2, isUserSpace, gradientTransform, transformValue is not null, vectorCount, units is not null, spreadMethod, spread is not null, href, rawX1 is not null, rawY1 is not null, rawX2 is not null, rawY2 is not null);
    }

    internal static SvgRadialTemplate ParseRadialVector(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? units = reader.GetAttribute("gradientUnits");
        bool isUserSpace = units is not null && units.Equals("userSpaceOnUse", StringComparison.Ordinal);
        if (units is not null && !units.Equals("objectBoundingBox", StringComparison.Ordinal) && !isUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only objectBoundingBox and userSpaceOnUse radial gradient units are implemented.");
        string? spread = reader.GetAttribute("spreadMethod");
        SvgGradientSpread spreadMethod = ParseSpreadMethod(spread);
        string? transformValue = reader.GetAttribute("gradientTransform");
        SvgGradientTransform gradientTransform = ParseGradientTransform(transformValue, workCounter, cancellationToken);
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

    private static SvgGradientTransform ParseGradientTransform(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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
            if (nameStart == position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), nonzero scale(), rotation, and finite matrix() gradient transforms are implemented.");
            ReadOnlySpan<char> name = input[nameStart..position];
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            if (position == input.Length || input[position] != '(') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), nonzero scale(), rotation, and finite matrix() gradient transforms are implemented.");
            position++;
            int argumentsStart = position;
            while (position < input.Length && input[position] != ')') position++;
            if (position == input.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), nonzero scale(), rotation, and finite matrix() gradient transforms are implemented.");
            transform = SvgGradientTransform.Compose(transform, ParseGradientTransformFunction(name, input[argumentsStart..position], workCounter, cancellationToken));
            position++;
            hasFunction = true;
        }
    }

    private static SvgGradientTransform ParseGradientTransformFunction(ReadOnlySpan<char> name, ReadOnlySpan<char> arguments, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        bool isMatrix = name.Equals("matrix".AsSpan(), StringComparison.OrdinalIgnoreCase);
        bool isTranslate = name.Equals("translate".AsSpan(), StringComparison.OrdinalIgnoreCase);
        bool isRotate = name.Equals("rotate".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (!isMatrix && !isTranslate && !isRotate && !name.Equals("scale".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), nonzero scale(), rotation, and finite matrix() gradient transforms are implemented.");
        Span<double> values = stackalloc double[6];
        int valueCount = 0;
        SvgGeometrySyntax.DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, isMatrix ? "matrix() requires six coordinates." : "Transforms require one or two coordinates.");
            values[valueCount++] = ParseGradientTransformCoordinate(value, workCounter, cancellationToken);
        }
        if (isMatrix)
        {
            if (valueCount != 6) throw new SvgException(SvgErrorCode.MalformedInput, "matrix() requires six coordinates.");
            return new SvgGradientTransform(values[0], values[1], values[2], values[3], values[4], values[5]);
        }
        if (isRotate)
        {
            if (valueCount is not (1 or 3)) throw new SvgException(SvgErrorCode.MalformedInput, "rotate() requires one angle or an angle with a center.");
            double radians = values[0] * (Math.PI / 180d);
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            SvgGradientTransform rotation = new(cosine, sine, -sine, cosine, 0, 0);
            if (valueCount == 1) return rotation;
            SvgGradientTransform toCenter = new(1, 0, 0, 1, values[1], values[2]);
            SvgGradientTransform fromCenter = new(1, 0, 0, 1, -values[1], -values[2]);
            return SvgGradientTransform.Compose(SvgGradientTransform.Compose(toCenter, rotation), fromCenter);
        }
        if (valueCount is < 1 or > 2) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
        double first = values[0];
        double second = valueCount == 2 ? values[1] : (isTranslate ? 0 : first);
        if (isTranslate) return new SvgGradientTransform(1, 0, 0, 1, first, second);
        if (first == 0 || second == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero scale() gradient transforms are implemented.");
        return new SvgGradientTransform(first, 0, 0, second, 0, 0);
    }

    private static double ParseGradientTransformCoordinate(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient transform coordinates must be finite bounded numbers.");
        return SvgFloatNumber.ReadValidated(token, workCounter, cancellationToken);
    }


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
        return percentage ? (float)result / 100f : result;
    }

    internal static SvgGradientStop ParseStop(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        double offset = ParseStopOffset(reader.GetAttribute("offset"));
        string? styleColor = null;
        string? styleOpacity = null;
        string? styleValue = reader.GetAttribute("style");
        if (styleValue is not null) ParseStopStyle(styleValue, out styleColor, out styleOpacity, workCounter, cancellationToken);
        string? colorValue = styleColor ?? reader.GetAttribute("stop-color");
        SvgColor color = colorValue is null ? new(0, 0, 0, 255) : SvgColorParser.Parse(colorValue, namedColors);
        SvgPaintOpacity stopOpacity = SvgColorParser.ParsePaintOpacity(styleOpacity ?? reader.GetAttribute("stop-opacity"));
        workCounter.Consume(cancellationToken);
        return new SvgGradientStop(offset, new SvgColor(color.Red, color.Green, color.Blue, SvgReferenceResolver.MultiplyAlpha(color.Alpha, stopOpacity.Alpha))) { Alpha = color.Alpha * (1f / 255f) * stopOpacity.Value };
    }

    /// <summary>Reads stop-color and stop-opacity declarations from one stop style attribute, ignoring other properties.</summary>
    /// <remarks>Style declarations outrank presentation attributes per CSS, so callers prefer these values; unknown or malformed declarations stay tolerated like editor cruft while values themselves parse strictly. Charges one work unit per declaration, and observes cancellation.</remarks>
    private static void ParseStopStyle(string value, out string? styleColor, out string? styleOpacity, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        styleColor = null;
        styleOpacity = null;
        foreach (string declaration in value.Split(';'))
        {
            workCounter.Consume(cancellationToken);
            int colon = declaration.IndexOf(':');
            if (colon < 0) continue;
            string name = declaration[..colon].Trim();
            string declared = declaration[(colon + 1)..].Trim();
            if (declared.EndsWith("!important", StringComparison.OrdinalIgnoreCase)) declared = declared[..^10].TrimEnd();
            if (name.Equals("stop-color", StringComparison.OrdinalIgnoreCase)) styleColor = declared;
            else if (name.Equals("stop-opacity", StringComparison.OrdinalIgnoreCase)) styleOpacity = declared;
        }
    }

    internal static SvgGeometry LowerGradientRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Fill), out SvgLinearGradient gradient))
        {
            if (state.RadialGradients.TryGetValue(ParseGradientId(style.Fill), out SvgRadialGradient radial)) return LowerRadialRect(reader, context, style, state, radial, frame);
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        }
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        SvgAffineTransform transform = context.ResolveTransform(reader);
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
        if (gradient.Stops.Length > 1 && gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
        }
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, fillOpacity, painted ? stroke : null));
    }

    /// <summary>Lowers one radial-gradient-filled rectangle with an optional solid stroke.</summary>
    internal static SvgGeometry LowerRadialRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgRadialGradient gradient, SvgViewportFrame? frame)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
        SvgAffineTransform transform = context.ResolveTransform(reader);
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
        return SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(gradient, box, fill, fillOpacity, painted ? stroke : null, state.Budget));
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
            bandStroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, style, bandStrokeShape, elementName, elementName == "polyline");
        }
        SvgAffineTransform transform = context.ResolveTransform(reader);
        bool bindSourceMap = !transform.IsAxisAligned;
        if (!bindSourceMap) gradient = MapUserSpaceRadial(transform, gradient, frame);
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        if (isPath && painted && !gradient.IsEmpty)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasCurve, bool hasArc, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            // Retain authored path contours and repaint their shared fill with the radial circle.
            SvgComputedStyle solidFill = style with { Fill = "black" };
            SvgResolvedPaintableElement curveShape = context.ResolveShape(solidFill, reader, true);
            using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, elementName, true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgPolygonMapping mapping = hasCurve ? SvgPolygonMapping.FlattenedCurves : SvgPolygonMapping.FiniteEdges;
            SvgParsedGeometry filled = curveLowering.LowerDeviceFill(d, commandCount, hasCurve, mapping, curveShape, frame, false, budget);
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            try
            {
                SvgRectangle paintBox;
                if (bindSourceMap)
                {
                    SvgAuthoredPath sourcePath = filled.Geometries[0].AuthoredPath ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Affine radial path paints require retained source commands.");
                    paintBox = gradient.IsUserSpace ? default : SvgPathBounds.Compute(sourcePath.Commands, context.WorkCounter, context.CancellationToken);
                    gradient = BindRadialSourceMap(gradient, paintBox, sourcePath.SceneMap, context.WorkCounter, context.CancellationToken);
                }
                else paintBox = gradient.IsUserSpace ? default : SvgOrthogonalShapeParser.BoxFromFilledGeometryBounds(filled, context.WorkCounter, context.CancellationToken);
                foreach (SvgGeometry existing in filled.Geometries)
                {
                    SvgGeometry repaint = existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, gradient, paintBox, opacity, budget));
                    try { repainted.Add(repaint); }
                    catch { repaint.Dispose(); throw; }
                }
                filled.Dispose();
                return AppendPaintFillStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            }
            catch { foreach (SvgGeometry geometry in repainted.AsSpan()) geometry.Dispose(); filled.Discard(); throw; }
        }
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
        try
        {
            for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
            {
                context.WorkCounter.Consume(context.CancellationToken);
                bands[bandIndex] = SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(gradient, box, bands[bandIndex].Rectangle, opacity, null, state.Budget));
            }
        }
        catch { solid.Discard(); throw; }
        return AppendPaintFillStroke(solid, reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);

        static SvgRectangle BoxFromRadialBands(BudgetedParserList<SvgPoint> points, SvgAffineTransform transform, SvgViewportFrame? frame, SvgPaintableElementContext context)
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
        SvgRadialGradient outlineCircle = default;
        bool paintRadialStroke = SvgOrthogonalShapeParser.TryResolveOutlineRadial(style, state, out outlineCircle);
        SvgLinearGradient outlineGradient = default;
        bool paintStroke = !paintRadialStroke && SvgOrthogonalShapeParser.TryResolveOutlineGradient(style, state, out outlineGradient);
        SvgPatternTile outlineTile = default;
        bool paintPatternStroke = !paintRadialStroke && !paintStroke && SvgOrthogonalShapeParser.TryResolveOutlinePattern(style, state, out outlineTile);
        SvgComputedStyle strokeStyle = (paintStroke || paintRadialStroke || paintPatternStroke) ? style with { Stroke = "#ffffff" } : style;
        SvgStrokeStyleSyntax.SvgAdmittedStroke? bandStroke = null;
        SvgResolvedPaintableElement bandStrokeShape = default;
        if (style.Stroke is not null && !style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            // Solid strokes compose over gradient fills through the admitted outline kernel; gradient strokes lower white and repaint below while other paints stay gated.
            bandStrokeShape = context.ResolveShape(strokeStyle with { Fill = "black" }, reader, true);
            bandStroke = SvgStrokeStyleSyntax.ResolveAdmittedStroke(reader, strokeStyle, bandStrokeShape, elementName, elementName == "polyline");
        }
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Fill), out SvgLinearGradient gradient))
        {
            if (state.RadialGradients.TryGetValue(ParseGradientId(style.Fill), out SvgRadialGradient radial)) return LowerRadialBands(reader, context, style, state, radial, elementName, isPath, maximumGeometryElements, budget, frame);
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        }
        SvgAffineTransform transform = context.ResolveTransform(reader);
        bool bindSourceMap = isPath && !transform.IsAxisAligned;
        if (!bindSourceMap)
        {
            gradient = MapUserSpaceVector(transform, gradient, frame);
            gradient = MirrorBoundingBoxVector(transform, gradient);
        }
        if (paintStroke)
        {
            outlineGradient = MapUserSpaceVector(transform, outlineGradient, frame);
            outlineGradient = MirrorBoundingBoxVector(transform, outlineGradient);
        }
        if (paintRadialStroke) outlineCircle = MapUserSpaceRadial(transform, outlineCircle, frame);
        bool painted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(SvgColorParser.ParseOpacity(style.Opacity), SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        if (isPath && painted && !gradient.IsEmpty)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasCurve, bool hasArc, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
            int commandCount = renderData.CommandCount;

            // Retain authored path contours and repaint their shared fill with the gradient.
            // The gradient url is not a parseable color; resolve a solid placeholder (discarded by the repaint below) for transform and fill state.
            SvgComputedStyle solidFill = strokeStyle with { Fill = "black" };
            SvgResolvedPaintableElement curveShape = context.ResolveShape(solidFill, reader, true);
            using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, elementName, true, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgPolygonMapping mapping = hasCurve ? SvgPolygonMapping.FlattenedCurves : SvgPolygonMapping.FiniteEdges;
            SvgParsedGeometry filled = curveLowering.LowerDeviceFill(d, commandCount, hasCurve, mapping, curveShape, frame, false, budget);
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            try
            {
                if (gradient.Stops.Length > 1 && gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
                SvgRectangle paintBox = gradient.IsUserSpace ? default : SvgOrthogonalShapeParser.BoxFromFilledGeometryBounds(filled, context.WorkCounter, context.CancellationToken);
                if (bindSourceMap && filled.Geometries.Length > 0)
                {
                    SvgAuthoredPath sourcePath = filled.Geometries[0].AuthoredPath ?? throw new InvalidOperationException("Mapped linear fills require source commands.");
                    if (!gradient.IsUserSpace) paintBox = SvgPathBounds.Compute(sourcePath.Commands, context.WorkCounter, context.CancellationToken);
                    gradient = BindLinearSourceMap(gradient, paintBox, sourcePath.SceneMap, state, context.WorkCounter, context.CancellationToken);
                    paintBox = default;
                }
                foreach (SvgGeometry existing in filled.Geometries)
                {
                    SvgGeometry repaint = existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, gradient, paintBox, opacity, budget));
                    try { repainted.Add(repaint); }
                    catch { repaint.Dispose(); throw; }
                }
                filled.Dispose();
                if (!paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
                if (paintPatternStroke) return AppendPaintOutlineStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
                if (paintRadialStroke) return AppendPaintOutlineStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, paintBox);
                return AppendPaintOutlineStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, paintBox);
            }
            catch { foreach (SvgGeometry geometry in repainted.AsSpan()) geometry.Dispose(); filled.Discard(); throw; }
        }
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, true, maximumGeometryElements, budget, context, contourEnds);
        SvgRectangle box = BoxFromGradientBands(points, transform, frame, context);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, isPath, isEvenOdd, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
        if (SvgOrthogonalShapeParser.HasDiagonalContour(points, contourEnds))
        {
            if (gradient.Stops.Length == 1 && !paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            if (paintPatternStroke) if (gradient.Stops.Length == 1) return AppendPaintOutlineStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
            if (paintRadialStroke) if (gradient.Stops.Length == 1) return AppendPaintOutlineStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, box);
            if (gradient.Stops.Length == 1) return AppendPaintOutlineStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, box);
            if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
            {
                if (gradient.Stops.Length > 1 && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
                if (!paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
                if (paintPatternStroke) return AppendPaintOutlineStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
                if (paintRadialStroke) return AppendPaintOutlineStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, box);
                return AppendPaintOutlineStroke(lowering.LowerPolygonFill(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, box);
            }
            if (!paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            if (paintPatternStroke) return AppendPaintOutlineStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
            if (paintRadialStroke) return AppendPaintOutlineStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, box);
            return AppendPaintOutlineStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, box);
        }
        if (gradient.Stops.Length > 1 && gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Stops.Length > 1 && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
            if (!paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStroke(lowering.Lower(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            if (paintPatternStroke) return AppendPaintOutlineStroke(lowering.Lower(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
            if (paintRadialStroke) return AppendPaintOutlineStroke(lowering.Lower(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, box);
            return AppendPaintOutlineStroke(lowering.Lower(SvgPaintResolver.ApplyOpacity(gradient.End, opacity), painted, transform, frame), reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, box);
        }
        SvgParsedGeometry solid = lowering.Lower(default, painted && !gradient.IsEmpty, transform, frame);
        try
        {
            SvgGeometry[] bands = solid.Storage.Items;
            for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
            {
                context.WorkCounter.Consume(context.CancellationToken);
                bands[bandIndex] = SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, bands[bandIndex].Rectangle, opacity, null));
            }
            if (!paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStroke(solid, reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            if (paintPatternStroke) return AppendPaintOutlineStroke(solid, reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
            if (paintRadialStroke) return AppendPaintOutlineStroke(solid, reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, box);
            return AppendPaintOutlineStroke(solid, reader, context, elementName, isPath, points, contourEnds, transform, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, box);
        }
        catch { solid.Discard(); throw; }
    }

    /// <summary>Appends an admitted solid stroke outline to paint-filled bands and outlines, leaving unadmitted strokes to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintFillStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, SvgAffineTransform transform, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape)
    {
        if (stroke is null) return filled;
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasArc, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            return AppendPaintFillStrokePath(filled, d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, stroke, strokeShape);
        }
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, false, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry stroked = strokeLowering.LowerStrokePolyline(points, elementName == "polygon", strokeShape, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, stroke.Value.Color, strokeShape.Presentation.IsPainted, frame, budget);
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
        }
        catch { filled.Discard(); throw; }
    }

    /// <summary>Appends a linear-gradient stroke outline lowered from path data to paint-filled geometry by lowering a white outline and repainting it.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasArc, bool hasCurve, XmlReader reader, SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgLinearGradient outline, SvgRectangle strokeBox)
    {
        if (stroke is null) return filled;
        SvgParsedGeometry white;
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            white = strokeLowering.LowerStrokePath(d, commandCount, hasArc, hasCurve, strokeShape, stroke.Value.Color, strokeShape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, budget);
        }
        catch { filled.Discard(); throw; }
        return RepaintAndConcatOutlines(filled, white, outline, strokeBox, strokeShape, context, budget);
    }

    /// <summary>Appends a pattern stroke outline lowered from path data to paint-filled geometry by lowering a white outline and repainting it.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasArc, bool hasCurve, XmlReader reader, SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgPatternTile outlineTile)
    {
        if (stroke is null) return filled;
        SvgParsedGeometry white;
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            white = strokeLowering.LowerStrokePath(d, commandCount, hasArc, hasCurve, strokeShape, stroke.Value.Color, strokeShape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, budget);
        }
        catch { filled.Discard(); throw; }
        return RepaintAndConcatOutlines(filled, white, outlineTile, strokeShape, context, budget);
    }

    /// <summary>Appends a radial-gradient stroke outline lowered from path data to paint-filled geometry by lowering a white outline and repainting it.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasArc, bool hasCurve, XmlReader reader, SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgRadialGradient outlineCircle, SvgRectangle strokeBox)
    {
        if (stroke is null) return filled;
        SvgParsedGeometry white;
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            white = strokeLowering.LowerStrokePath(d, commandCount, hasArc, hasCurve, strokeShape, stroke.Value.Color, strokeShape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, budget);
        }
        catch { filled.Discard(); throw; }
        return RepaintAndConcatOutlines(filled, white, outlineCircle, strokeBox, strokeShape, context, budget);
    }

    /// <summary>Repaints white stroke outlines with a linear-gradient paint and concatenates them after paint-filled geometry, transferring ownership on every path.</summary>
    private static SvgParsedGeometry RepaintAndConcatOutlines(SvgParsedGeometry filled, SvgParsedGeometry white, SvgLinearGradient outline, SvgRectangle strokeBox, SvgResolvedPaintableElement strokeShape, SvgPaintableElementContext context, RenderBudget budget)
    {
        bool whiteReleased = false;
        try
        {
            SvgRectangle paintBox = strokeBox;
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            byte strokeOpacity = strokeShape.Presentation.Opacity;
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, outline, paintBox, strokeOpacity, budget)));
            int whiteCount = white.ElementCount;
            white.Dispose();
            whiteReleased = true;
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, new SvgParsedGeometry(repainted.Detach(), whiteCount), budget, context.WorkCounter, context.CancellationToken);
        }
        catch
        {
            if (!whiteReleased) white.Discard();
            filled.Discard();
            throw;
        }
    }

    /// <summary>Repaints white stroke outlines with a pattern paint and concatenates them after paint-filled geometry, transferring ownership on every path.</summary>
    private static SvgParsedGeometry RepaintAndConcatOutlines(SvgParsedGeometry filled, SvgParsedGeometry white, SvgPatternTile outlineTile, SvgResolvedPaintableElement strokeShape, SvgPaintableElementContext context, RenderBudget budget)
    {
        bool whiteReleased = false;
        try
        {
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            byte strokeOpacity = strokeShape.Presentation.Opacity;
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, outlineTile, strokeOpacity, budget)));
            int whiteCount = white.ElementCount;
            white.Dispose();
            whiteReleased = true;
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, new SvgParsedGeometry(repainted.Detach(), whiteCount), budget, context.WorkCounter, context.CancellationToken);
        }
        catch
        {
            if (!whiteReleased) white.Discard();
            filled.Discard();
            throw;
        }
    }

    /// <summary>Repaints white stroke outlines with a radial-gradient paint and concatenates them after paint-filled geometry, transferring ownership on every path.</summary>
    private static SvgParsedGeometry RepaintAndConcatOutlines(SvgParsedGeometry filled, SvgParsedGeometry white, SvgRadialGradient outlineCircle, SvgRectangle strokeBox, SvgResolvedPaintableElement strokeShape, SvgPaintableElementContext context, RenderBudget budget)
    {
        bool whiteReleased = false;
        try
        {
            SvgRectangle paintBox = strokeBox;
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            byte strokeOpacity = strokeShape.Presentation.Opacity;
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, outlineCircle, paintBox, strokeOpacity, budget)));
            int whiteCount = white.ElementCount;
            white.Dispose();
            whiteReleased = true;
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, new SvgParsedGeometry(repainted.Detach(), whiteCount), budget, context.WorkCounter, context.CancellationToken);
        }
        catch
        {
            if (!whiteReleased) white.Discard();
            filled.Discard();
            throw;
        }
    }

    /// <summary>Appends an admitted solid stroke outline lowered from path data to paint-filled geometry.</summary>
    internal static SvgParsedGeometry AppendPaintFillStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasArc, bool hasCurve, XmlReader reader, SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape)
    {
        if (stroke is null) return filled;
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, true, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            SvgParsedGeometry stroked = strokeLowering.LowerStrokePath(d, commandCount, hasArc, hasCurve, strokeShape, stroke.Value.Color, strokeShape.Presentation.IsPainted, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, frame, budget);
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, stroked, budget, context.WorkCounter, context.CancellationToken);
        }
        catch { filled.Discard(); throw; }
    }

    /// <summary>Appends a linear-gradient stroke outline to paint-filled bands and outlines by lowering a white outline and repainting it, leaving other paints to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, SvgAffineTransform transform, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgLinearGradient outline, SvgRectangle strokeBox)
    {
        if (stroke is null) return filled;
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasArc, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            return AppendPaintOutlineStrokePath(filled, d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, stroke, strokeShape, outline, strokeBox);
        }
        SvgParsedGeometry white;
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, false, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            white = strokeLowering.LowerStrokePolyline(points, elementName == "polygon", strokeShape, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, stroke.Value.Color, strokeShape.Presentation.IsPainted, frame, budget);
        }
        catch { filled.Discard(); throw; }
        return RepaintAndConcatOutlines(filled, white, outline, strokeBox, strokeShape, context, budget);
    }

    /// <summary>Appends a pattern stroke outline to paint-filled bands and outlines by lowering a white outline and repainting it, leaving other paints to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, SvgAffineTransform transform, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgPatternTile outlineTile)
    {
        if (stroke is null) return filled;
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasArc, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            return AppendPaintOutlineStrokePath(filled, d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, stroke, strokeShape, outlineTile);
        }
        SvgParsedGeometry white;
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, false, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            white = strokeLowering.LowerStrokePolyline(points, elementName == "polygon", strokeShape, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, stroke.Value.Color, strokeShape.Presentation.IsPainted, frame, budget);
        }
        catch { filled.Discard(); throw; }
        return RepaintAndConcatOutlines(filled, white, outlineTile, strokeShape, context, budget);
    }

    /// <summary>Appends a radial-gradient stroke outline to paint-filled bands and outlines by lowering a white outline and repainting it, leaving other paints to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, SvgAffineTransform transform, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgRadialGradient outlineCircle, SvgRectangle strokeBox)
    {
        if (stroke is null) return filled;
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasArc, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.WorkCounter, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            return AppendPaintOutlineStrokePath(filled, d, commandCount, hasArc, hasCurve, reader, context, elementName, frame, maximumGeometryElements, budget, stroke, strokeShape, outlineCircle, strokeBox);
        }
        SvgParsedGeometry white;
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, false, false, maximumGeometryElements, budget, context.WorkCounter, context.CancellationToken);
            white = strokeLowering.LowerStrokePolyline(points, elementName == "polygon", strokeShape, stroke.Value.Width, stroke.Value.MiterLimit, stroke.Value.Cap, stroke.Value.Join, stroke.Value.DashPattern, stroke.Value.DashOffset, stroke.Value.PathLength, stroke.Value.NonScalingStroke, stroke.Value.Color, strokeShape.Presentation.IsPainted, frame, budget);
        }
        catch { filled.Discard(); throw; }
        return RepaintAndConcatOutlines(filled, white, outlineCircle, strokeBox, strokeShape, context, budget);
    }

    /// <summary>Computes the device paint box from retained contour points for bounding-box evaluation.</summary>
    internal static SvgRectangle BoxFromGradientBands(BudgetedParserList<SvgPoint> points, SvgAffineTransform transform, SvgViewportFrame? frame, SvgPaintableElementContext context)
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

    /// <summary>Inverts one axis-aligned scale-plus-offset map, refusing singular scales.</summary>
    internal static bool TryInvertAxisMap(double scaleX, double scaleY, double offsetX, double offsetY, out SvgGradientTransform inverse)
    {
        inverse = SvgGradientTransform.Identity;
        if (scaleX == 0 || scaleY == 0) return false;
        double a = 1 / scaleX;
        double d = 1 / scaleY;
        double e = -offsetX / scaleX;
        double f = -offsetY / scaleY;
        if (!double.IsFinite(a) || !double.IsFinite(d) || !double.IsFinite(e) || !double.IsFinite(f)) return false;
        inverse = new SvgGradientTransform(a, 0, 0, d, e, f);
        return true;
    }

    /// <summary>Shifts one general radial gradient through a translation, composing its inverse paint map.</summary>
    internal static SvgRadialGradient ShiftGeneralRadial(SvgRadialGradient gradient, double offsetX, double offsetY)
    {
        return ScaleGeneralRadial(gradient, 1, 1, offsetX, offsetY);
    }

    /// <summary>Shifts one general radial gradient through an axis-aligned scale plus offset, composing its inverse paint map.</summary>
    internal static SvgRadialGradient ScaleGeneralRadial(SvgRadialGradient gradient, double scaleX, double scaleY, double offsetX, double offsetY)
    {
        if (!TryInvertAxisMap(scaleX, scaleY, offsetX, offsetY, out SvgGradientTransform inverseMap)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular gradient referencing maps are not implemented.");
        SvgGradientPaintMatrices? native = gradient.NativePaintMatrices;
        if (native is {} matrices)
        {
            SvgShaderMatrix map = new((float)scaleX, 0, (float)offsetX, 0, (float)scaleY, (float)offsetY);
            SvgAffineTransform source = matrices.Referencing;
            SvgShaderMatrix combined = SvgShaderMatrix.Concat(map, new((float)source.A, (float)source.C, (float)source.E, (float)source.B, (float)source.D, (float)source.F));
            native = matrices with { Referencing = new(combined.ScaleX, combined.SkewY, combined.SkewX, combined.ScaleY, combined.TranslateX, combined.TranslateY) };
        }
        return gradient with { Inverse = SvgGradientTransform.Compose(gradient.Inverse, inverseMap), NativePaintMatrices = native };
    }

    internal static SvgLinearGradient MapUserSpaceVector(SvgAffineTransform transform, SvgLinearGradient gradient, SvgViewportFrame? frame)
    {
        if (!gradient.IsUserSpace) return gradient;
        if (!transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned gradient transforms are implemented.");
        (double mappedX1, double mappedY1) = transform.ApplyToUserPoint(gradient.X1, gradient.Y1);
        (double mappedX2, double mappedY2) = transform.ApplyToUserPoint(gradient.X2, gradient.Y2);
        double referenceScaleX = transform.A;
        double referenceScaleY = transform.D;
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

    /// <summary>Binds one linear definition to its source object box and composed drawing map.</summary>
    /// <remarks>The parent scene owns the immutable map. Authored endpoints and local matrices remain
    /// separate from placement for native float preparation; singular maps reject before publication.</remarks>
    internal static SvgLinearGradient BindLinearSourceMap(SvgLinearGradient gradient, SvgRectangle sourceBox,
        SvgAffineTransform sceneMap, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        SvgLinearPaintMapData source = gradient.SourcePaint?.Data ?? throw new InvalidOperationException("A linear source map requires definition coordinates.");
        if (!gradient.IsUserSpace)
        {
            if (sourceBox.Width <= 0 || sourceBox.Height <= 0) return gradient with { IsEmpty = true };
            SvgGradientTransform boxMap = new(sourceBox.Width, 0, 0, sourceBox.Height, sourceBox.X, sourceBox.Y);
            source = source with { Local = SvgGradientTransform.Compose(boxMap, source.Local) };
        }
        source = source with { Referencing = sceneMap, IsMapped = true };
        return gradient with { IsUserSpace = true, SourcePaint = state.Scene.RetainLinearPaintMap(source) };
    }

    /// <summary>Binds one radial definition to its source object box and composed drawing map.</summary>
    /// <remarks>Immutable and allocation-free. Source bounds precede affine placement; shader-local and
    /// reference matrices stay distinct for native float concatenation. The inverse supports the existing
    /// focal evaluator. The caller charges this bounded setup; singular maps reject without owned storage.</remarks>
    internal static SvgRadialGradient BindRadialSourceMap(SvgRadialGradient gradient, SvgRectangle sourceBox, SvgAffineTransform sceneMap, ParserWorkCounter work, CancellationToken token)
    {
        const int setupWork = 8;
        for (int phase = 0; phase < setupWork; phase++) work.Consume(token);
        if (!gradient.IsUserSpace && (sourceBox.Width <= 0 || sourceBox.Height <= 0)) return gradient with { IsEmpty = true };
        SvgGradientTransform objectMap = gradient.IsUserSpace ? SvgGradientTransform.Identity
            : new(sourceBox.Width, 0, 0, sourceBox.Height, sourceBox.X, sourceBox.Y);
        SvgGradientTransform reference = new(sceneMap.A, sceneMap.B, sceneMap.C, sceneMap.D, sceneMap.E, sceneMap.F);
        if (!SvgGradientTransform.Compose(reference, objectMap).TryInvert(out SvgGradientTransform inverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular radial paint referencing maps are not implemented.");
        SvgGradientTransform localInverse = gradient.HasGeneralTransform ? gradient.Inverse : SvgGradientTransform.Identity;
        SvgShaderMatrix local = gradient.NativePaintMatrices?.Local ?? new(1, 0, 0, 0, 1, 0);
        SvgShaderMatrix nativeObject = new((float)objectMap.A, 0, (float)objectMap.E, 0, (float)objectMap.D, (float)objectMap.F);
        SvgAffineTransform nativeReference = sceneMap;
        return gradient with { IsUserSpace = true, HasGeneralTransform = true,
            Inverse = SvgGradientTransform.Compose(localInverse, inverse),
            NativePaintMatrices = new(SvgShaderMatrix.Concat(nativeObject, local), nativeReference) };
    }

    /// <summary>Maps one userSpaceOnUse radial circle and its focal point through the referencing transform, recording scales for inverse evaluation.</summary>
    internal static SvgRadialGradient MapUserSpaceRadial(SvgAffineTransform transform, SvgRadialGradient gradient, SvgViewportFrame? frame)
    {
        if (!gradient.IsUserSpace) return gradient;
        if (!transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned gradient transforms are implemented.");
        if (gradient.HasGeneralTransform) return ScaleGeneralRadial(ScaleGeneralRadial(gradient, transform.A, transform.D, transform.E, transform.F), frame?.ScaleX ?? 1, frame?.ScaleY ?? 1, frame?.OffsetX ?? 0, frame?.OffsetY ?? 0);
        (double mappedX, double mappedY) = transform.ApplyToUserPoint(gradient.CenterX, gradient.CenterY);
        (double mappedFocalX, double mappedFocalY) = transform.ApplyToUserPoint(gradient.FocalX, gradient.FocalY);
        double mappedRadius = gradient.Radius * Math.Abs(transform.A);
        double mappedFocalRadius = gradient.FocalRadius * Math.Abs(transform.A);
        double referenceScaleX = transform.A;
        double referenceScaleY = transform.D;
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
    internal static SvgLinearGradient MirrorBoundingBoxVector(SvgAffineTransform transform, SvgLinearGradient gradient)
    {
        if (gradient.IsUserSpace) return gradient;
        if (!transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned gradient transforms are implemented.");
        double x1 = transform.A < 0 ? 1 - gradient.X1 : gradient.X1;
        double x2 = transform.A < 0 ? 1 - gradient.X2 : gradient.X2;
        double y1 = transform.D < 0 ? 1 - gradient.Y1 : gradient.Y1;
        double y2 = transform.D < 0 ? 1 - gradient.Y2 : gradient.Y2;
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
        SvgAffineTransform transform = context.ResolveTransform(reader);
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
        if (probe is null && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width gradient strokes no larger than the rectangle dimensions are implemented.");
        double halfWidth = probe is null ? 0 : probe.Value.Width / 2d;
        int strokeWidth = probe is null ? 0 : checked((int)probe.Value.Width);
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
            if (probe is not null && !frame.Value.Clip.Contains(placement.X - halfWidth, placement.Y - halfWidth, placement.X + placement.Width + halfWidth, placement.Y + placement.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            SvgRectangle shifted = frame.Value.Shift(placement);
            fill = frame.Value.Clip.ClipRectangle(shifted) with { Color = fillColor, HasFill = fillHasFill };
            box = frame.Value.Shift(box);
            top = frame.Value.Clip.ClipRectangle(frame.Value.Shift(top));
            bottom = frame.Value.Clip.ClipRectangle(frame.Value.Shift(bottom));
            left = frame.Value.Clip.ClipRectangle(frame.Value.Shift(left));
            right = frame.Value.Clip.ClipRectangle(frame.Value.Shift(right));
        }
        using BudgetedGeometryBuilder stroked = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        if (hasLinearFill) stroked.Add(LowerLinearFillGeometry(fillGradient, box, fill, fillHasFill, fillOpacity));
        else if (hasRadialFill) stroked.Add(LowerRadialFillGeometry(fillCircle, box, fill, fillHasFill, fillOpacity, state.Budget));
        if (probe is null)
        {
            // Zero width paints no stroke bands; probe nulls only on zero widths here.
            if (!hasLinearFill && !hasRadialFill) stroked.Add(SvgGeometry.FromRectangle(fill));
            return new SvgParsedGeometry(stroked.Detach(), 0);
        }
        SvgRectangle strokeFill = combined ? fill with { HasFill = false } : fill;
        if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Stops.Length > 1 && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient strokes are implemented.");
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
        SvgAffineTransform transform = context.ResolveTransform(reader);
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
        if (probe is null && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width gradient strokes no larger than the rectangle dimensions are implemented.");
        double halfWidth = probe is null ? 0 : probe.Value.Width / 2d;
        int strokeWidth = probe is null ? 0 : checked((int)probe.Value.Width);
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
            if (probe is not null && !frame.Value.Clip.Contains(placement.X - halfWidth, placement.Y - halfWidth, placement.X + placement.Width + halfWidth, placement.Y + placement.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            SvgRectangle shifted = frame.Value.Shift(placement);
            fill = frame.Value.Clip.ClipRectangle(shifted) with { Color = fillColor, HasFill = fillHasFill };
            box = frame.Value.Shift(box);
            top = frame.Value.Clip.ClipRectangle(frame.Value.Shift(top));
            bottom = frame.Value.Clip.ClipRectangle(frame.Value.Shift(bottom));
            left = frame.Value.Clip.ClipRectangle(frame.Value.Shift(left));
            right = frame.Value.Clip.ClipRectangle(frame.Value.Shift(right));
        }
        using BudgetedGeometryBuilder stroked = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        if (hasLinearFill) stroked.Add(LowerLinearFillGeometry(fillGradient, box, fill, fillHasFill, fillOpacity));
        else if (hasRadialFill) stroked.Add(LowerRadialFillGeometry(fillCircle, box, fill, fillHasFill, fillOpacity, state.Budget));
        else stroked.Add(SvgGeometry.FromRectangle(fill));
        if (probe is null)
        {
            // Zero width retains only the fill operation above.
            return new SvgParsedGeometry(stroked.Detach(), 0);
        }
        if (circle.Radius == 0 && !circle.IsEmpty)
        {
            if (circle.Stops.Length > 1 && circle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
        }
        bool paintBands = painted && !circle.IsEmpty;
        stroked.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, top with { HasFill = paintBands }, opacity, null, state.Budget)) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        stroked.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, bottom with { HasFill = paintBands }, opacity, null, state.Budget)) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        stroked.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, left with { HasFill = paintBands }, opacity, null, state.Budget)) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        stroked.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, right with { HasFill = paintBands }, opacity, null, state.Budget)) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        return new SvgParsedGeometry(stroked.Detach(), 0);
    }

    internal static SvgParsedGeometry LowerGradientStrokeLine(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgLineSyntax syntax, SvgDocumentState state, SvgViewportFrame? frame)
    {
        // A zero width paints nothing for any paint, so it returns empty before dash, cap, and paint handling like the solid line kernel.
        if (SvgGeometrySyntax.ParseInteger(style.Width ?? syntax.StrokeWidth ?? "1") == 0)
        {
            using BudgetedGeometryBuilder none = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            return new SvgParsedGeometry(none.Detach(), 0);
        }
        // Round caps without dashes lower through the paint-specific round-cap path below; everything else keeps the band gate.
        if (SvgStrokeStyleSyntax.ParseDasharray(reader.GetAttribute("stroke-dasharray")) is not null || SvgStrokeStyleSyntax.ParseLinecap(reader.GetAttribute("stroke-linecap")) != SvgStrokeCap.Round) SvgStrokeStyleSyntax.RequireButtOrSquareCapJoin(reader, "line", context.WorkCounter, context.CancellationToken);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        if (transform != SvgAffineTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
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
    private static SvgParsedGeometry LowerRoundCapGradientStrokeLine(SvgLineSyntax syntax, int strokeWidth, SvgComputedStyle style, SvgAffineTransform transform, SvgLinearGradient gradient, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        bool degenerate = gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty;
        if (degenerate && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
        (SvgParsedGeometry white, SvgRectangle strokeBox) = SvgLeafParser.LowerWhiteRoundCapLine(syntax, strokeWidth, style, transform, painted, painted && !gradient.IsEmpty, frame, state, context);
        try
        {
            SvgColor solid = SvgPaintResolver.ApplyOpacity(gradient.End, opacity);
            using BudgetedGeometryBuilder repainted = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            foreach (SvgGeometry existing in white.Geometries)
            {
                if (existing.Kind == SvgGeometryKind.OrthogonalBand) repainted.Add(degenerate ? SvgGeometry.FromOrthogonalBand(existing.Rectangle with { Color = solid }) : SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, strokeBox, existing.Rectangle, opacity, null)));
                else if (degenerate) repainted.Add(SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(existing.Polygon.Points, existing.Polygon.ContourEnds, existing.Polygon.Signs, solid, existing.Polygon.HasFill, existing.Polygon.IsEvenOdd, existing.Polygon.Mapping, existing.Polygon.Clip, null, state.Budget)));
                else repainted.Add(existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, gradient, strokeBox, opacity, state.Budget)));
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
        using BudgetedGeometryBuilder runs = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
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
        using BudgetedGeometryBuilder retained = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        retained.Add(geometry);
        return new SvgParsedGeometry(retained.Detach(), 0);
    }

    /// <summary>Lowers one radial-gradient-stroked axis-aligned line into a radial-evaluated stroke band.</summary>
    internal static SvgParsedGeometry LowerRadialStrokeLine(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgLineSyntax syntax, SvgDocumentState state, SvgViewportFrame? frame)
    {
        // A zero width paints nothing for any paint, so it returns empty before dash, cap, and paint handling like the solid line kernel.
        if (SvgGeometrySyntax.ParseInteger(style.Width ?? syntax.StrokeWidth ?? "1") == 0)
        {
            using BudgetedGeometryBuilder none = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            return new SvgParsedGeometry(none.Detach(), 0);
        }
        // Round caps without dashes lower through the paint-specific round-cap path below; everything else keeps the band gate.
        if (SvgStrokeStyleSyntax.ParseDasharray(reader.GetAttribute("stroke-dasharray")) is not null || SvgStrokeStyleSyntax.ParseLinecap(reader.GetAttribute("stroke-linecap")) != SvgStrokeCap.Round) SvgStrokeStyleSyntax.RequireButtOrSquareCapJoin(reader, "line", context.WorkCounter, context.CancellationToken);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        if (transform != SvgAffineTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed horizontal or vertical lines are implemented.");
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
        return RetainSingleBand(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, fill, opacity, null, state.Budget)), state, context);
    }

    /// <summary>Reports whether one radial-gradient line stroke paints fully opaque runs.</summary>
    internal static bool IsOpaqueRadialStroke(SvgRadialGradient circle, byte opacity)
    {
        if (opacity != 255 || circle.Start.Alpha != 255 || circle.End.Alpha != 255) return false;
        foreach (SvgGradientStop stop in circle.Stops) if (stop.Color.Alpha != 255) return false;
        return true;
    }

    /// <summary>Lowers one round-capped radial-gradient line stroke into one radial-evaluated band with two repainted tip discs.</summary>
    private static SvgParsedGeometry LowerRoundCapRadialStrokeLine(SvgLineSyntax syntax, int strokeWidth, SvgComputedStyle style, SvgAffineTransform transform, SvgRadialGradient circle, bool painted, byte opacity, SvgViewportFrame? frame, SvgDocumentState state, SvgPaintableElementContext context)
    {
        bool degenerate = circle.Radius == 0 && !circle.IsEmpty;
        if (degenerate && circle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
        (SvgParsedGeometry white, SvgRectangle strokeBox) = SvgLeafParser.LowerWhiteRoundCapLine(syntax, strokeWidth, style, transform, painted, painted && !circle.IsEmpty, frame, state, context);
        try
        {
            SvgColor solid = SvgPaintResolver.ApplyOpacity(circle.End, opacity);
            using BudgetedGeometryBuilder repainted = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            foreach (SvgGeometry existing in white.Geometries)
            {
                if (existing.Kind == SvgGeometryKind.OrthogonalBand) repainted.Add(degenerate ? SvgGeometry.FromOrthogonalBand(existing.Rectangle with { Color = solid }) : SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, strokeBox, existing.Rectangle, opacity, null, state.Budget)));
                else if (degenerate) repainted.Add(SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(existing.Polygon.Points, existing.Polygon.ContourEnds, existing.Polygon.Signs, solid, existing.Polygon.HasFill, existing.Polygon.IsEvenOdd, existing.Polygon.Mapping, existing.Polygon.Clip, null, state.Budget)));
                else repainted.Add(existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, circle, strokeBox, opacity, state.Budget)));
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
        using BudgetedGeometryBuilder runs = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
        bool degenerate = circle.Radius == 0 && !circle.IsEmpty;
        if (degenerate && circle.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate radial gradient strokes are implemented.");
        for (int runIndex = 0; runIndex < runRects.Count; runIndex++)
        {
            SvgRectangle run = runRects[runIndex];
            if (degenerate) runs.Add(SvgGeometry.FromOrthogonalBand(run with { Color = SvgPaintResolver.ApplyOpacity(circle.End, opacity), HasFill = paintsAny && painted }));
            else runs.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, run, run with { HasFill = paintsAny && painted && !circle.IsEmpty }, opacity, null, state.Budget)));
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
    internal static SvgGeometry LowerRadialFillGeometry(SvgRadialGradient fillCircle, SvgRectangle box, SvgRectangle fill, bool fillHasFill, byte fillOpacity, RenderBudget budget)
    {
        if (fillCircle.Stops.Length == 1) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(fillCircle.End, fillOpacity), HasFill = fillHasFill });
        if (fillCircle.Radius == 0 && !fillCircle.IsEmpty) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(fillCircle.End, fillOpacity), HasFill = fillHasFill });
        return SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(fillCircle, box, fill with { HasFill = fillHasFill }, fillOpacity, null, budget));
    }

    private static SvgStroke? ParseGradientStroke(XmlReader reader, SvgComputedStyle style, SvgPaintableElementContext context, SvgAffineTransform transform, SvgRectangle placement, byte opacity, bool painted)
    {
        if (style.Stroke is null || style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"))) return null;
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
        return percentage ? (float)result / 100f : result;
    }
}
