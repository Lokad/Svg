// Native CSS coordinate conversion adapts Chromium 153.0.8010.52.
// Copyright 2014 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one bounded linear gradient with its vector and referencing scale record.</summary>
/// <remarks>ReferenceScaleX and ReferenceScaleY multiply every scale map applied to the stored endpoints after the local vector definition, so raster evaluation inverts the exact paint map.</remarks>
internal readonly record struct SvgLinearGradient(SvgColor Start, SvgColor End, double X1, double Y1, double X2, double Y2, double Offset1, double Offset2, bool IsEmpty, bool IsUserSpace, SvgGradientSpread Spread, SvgGradientStop[] Stops, double ReferenceScaleX, double ReferenceScaleY, bool HasRotation)
{
    internal SvgLinearPaintMap? SourcePaint { get; init; }
    internal bool RequiresSourceMap => SourcePaint?.Data.Local is {} local && (local.B != 0 || local.C != 0) && !local.IsSimilarity();
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
internal readonly record struct SvgGradientTemplate(SvgLinearGradientCoordinates Coordinates, bool IsUserSpace, SvgGradientTransform Transform,
    bool HasTransform, bool HasUnits, SvgGradientSpread Spread, bool HasSpread, string? Href)
{
    /// <summary>Retains resource display while forward stop and vector references are unresolved.</summary>
    internal bool IsAvailable { get; init; } = true;
    internal SvgViewportLengthBases LengthBases { get; init; }
}

/// <summary>Retains one bounded radial gradient with a strictly inside focal point, contained focal circle and referencing scale record, with an inverse paint map for general transforms.</summary>
/// <remarks>ReferenceScaleX and ReferenceScaleY multiply every scale map applied to the stored circle after the local definition, so raster evaluation inverts the exact paint map under non-uniform referencing transforms.</remarks>
internal readonly record struct SvgRadialGradient(SvgColor Start, SvgColor End, double CenterX, double CenterY, double Radius, double FocalX, double FocalY, double FocalRadius, double Offset1, double Offset2, bool IsEmpty, bool IsUserSpace, SvgGradientSpread Spread, SvgGradientStop[] Stops, double ReferenceScaleX, double ReferenceScaleY, bool HasRotation, SvgGradientTransform Inverse, bool HasGeneralTransform)
{
    /// <summary>Borrows immutable definition coordinates before local or instance placement.</summary>
    internal SvgRadialGradientSource? SourceDefinition { get; init; }
    internal SvgGradientTransform TemplateTransform => SourceDefinition?.Transform ?? SvgGradientTransform.Identity;

    /// <summary>Retains the local shader matrix separately from referencing maps so bitmap translation precedes their float concatenation.</summary>
    internal SvgGradientPaintMatrices? NativePaintMatrices { get; init; }

    /// <summary>Covers the retained paint value and conservative owner/header overhead before a scene object is allocated.</summary>
    internal static long RetainedStorageBytes => 64L + System.Runtime.CompilerServices.Unsafe.SizeOf<SvgRadialGradient>();
}

/// <summary>Retains one parsed radial-gradient definition with attribute explicitness for href inheritance.</summary>
internal readonly record struct SvgRadialTemplate(SvgRadialGradientCoordinates Coordinates, bool IsUserSpace, SvgGradientTransform Transform,
    bool HasTransform, bool HasUnits, SvgGradientSpread Spread, bool HasSpread, string? Href)
{
    /// <summary>Retains resource display while forward stop and vector references are unresolved.</summary>
    internal bool IsAvailable { get; init; } = true;
    internal SvgViewportLengthBases LengthBases { get; init; }
}

/// <summary>Shares unplaced radial coordinates and their authored lengths under scene ownership.</summary>
/// <remarks>Immutable metadata owns no arrays. The scene reserves before construction and retains
/// the allowance through disposal; aliases never invert rounded paint to recover authored lengths.</remarks>
internal sealed record SvgRadialGradientSource(double CenterX, double CenterY, double Radius,
    double FocalX, double FocalY, double FocalRadius, SvgGradientTransform Transform, SvgRadialGradientCoordinates Coordinates)
{
    internal bool HasFocalX => Coordinates.FocalX.IsSpecified;
    internal bool HasFocalY => Coordinates.FocalY.IsSpecified;
    internal bool HasFocalRadius => Coordinates.FocalRadius.IsSpecified;
    internal static long RetainedStorageBytes => 64L + 6 * sizeof(double)
        + System.Runtime.CompilerServices.Unsafe.SizeOf<SvgRadialGradientCoordinates>()
        + System.Runtime.CompilerServices.Unsafe.SizeOf<SvgGradientTransform>();
}

/// <summary>Retains one pending linear template and the next alias waiting on the same target.</summary>
internal readonly record struct PendingLinearGradientHref(string Id, SvgGradientTemplate Template, SvgGradientStop[] Stops)
{
    internal SvgPendingGradientReference? Next { get; init; }
}

/// <summary>Retains one pending radial template and the next alias waiting on the same target.</summary>
internal readonly record struct PendingRadialGradientHref(string Id, SvgRadialTemplate Template, SvgGradientStop[] Stops)
{
    internal SvgPendingGradientReference? Next { get; init; }
}

internal enum SvgGradientKind { Linear, Radial }

/// <summary>Identifies a stable slot in the document's append-only pending template lists.</summary>
internal readonly record struct SvgPendingGradientReference(SvgGradientKind Kind, int Index);

/// <summary>Shares gradient-ID admission with the waiting-list head, including targets not yet defined.</summary>
internal readonly record struct SvgGradientRegistration(bool IsDefined, SvgPendingGradientReference? FirstDependent);

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
    internal const int MaximumGradientStops = 256;

    internal static SvgGradientTemplate ParseLinearVector(XmlReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? units = reader.GetAttribute("gradientUnits");
        bool isUserSpace = units is not null && units.Equals("userSpaceOnUse", StringComparison.Ordinal);
        if (units is not null && !units.Equals("objectBoundingBox", StringComparison.Ordinal) && !isUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only objectBoundingBox and userSpaceOnUse gradient units are implemented.");
        string? spread = reader.GetAttribute("spreadMethod");
        SvgGradientSpread spreadMethod = ParseSpreadMethod(spread);
        SvgLinearGradientCoordinates coordinates = new(SvgGradientLength.Parse(reader.GetAttribute("x1")), SvgGradientLength.Parse(reader.GetAttribute("y1")),
            SvgGradientLength.Parse(reader.GetAttribute("x2")), SvgGradientLength.Parse(reader.GetAttribute("y2")));
        string? transformValue = reader.GetAttribute("gradientTransform");
        SvgGradientTransform gradientTransform = ParseGradientTransform(transformValue, cancellationToken);
        string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        return new(coordinates, isUserSpace, gradientTransform, transformValue is not null, units is not null, spreadMethod, spread is not null, href);
    }

    internal static SvgRadialTemplate ParseRadialVector(XmlReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string? units = reader.GetAttribute("gradientUnits");
        bool isUserSpace = units is not null && units.Equals("userSpaceOnUse", StringComparison.Ordinal);
        if (units is not null && !units.Equals("objectBoundingBox", StringComparison.Ordinal) && !isUserSpace) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only objectBoundingBox and userSpaceOnUse radial gradient units are implemented.");
        string? spread = reader.GetAttribute("spreadMethod");
        SvgGradientSpread spreadMethod = ParseSpreadMethod(spread);
        string? transformValue = reader.GetAttribute("gradientTransform");
        SvgGradientTransform gradientTransform = ParseGradientTransform(transformValue, cancellationToken);
        SvgRadialGradientCoordinates coordinates = new(SvgGradientLength.Parse(reader.GetAttribute("cx")), SvgGradientLength.Parse(reader.GetAttribute("cy")),
            SvgGradientLength.Parse(reader.GetAttribute("r")), SvgGradientLength.Parse(reader.GetAttribute("fx")),
            SvgGradientLength.Parse(reader.GetAttribute("fy")), SvgGradientLength.Parse(reader.GetAttribute("fr")));
        if (coordinates.Radius.Value < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Radial gradient radius cannot be negative.");
        if (coordinates.FocalRadius.Value < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Focal radius cannot be negative.");
        string? href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        return new(coordinates, isUserSpace, gradientTransform, transformValue is not null, units is not null, spreadMethod, spread is not null, href);
    }

    private static SvgGradientTransform ParseGradientTransform(string? value, CancellationToken cancellationToken)
    {
        if (value is null) return SvgGradientTransform.Identity;
        if (value.Length > SvgSyntaxLimits.MaximumTransformCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Transform values exceed the configured syntax limit.");
        ReadOnlySpan<char> input = value.AsSpan();
        SvgGradientTransform transform = SvgGradientTransform.Identity;
        int position = 0;
        while (true)
        {
            while (position < input.Length && SvgCssWhitespace.Is(input[position])) position++;
            if (position == input.Length) return transform;
            int nameStart = position;
            while (position < input.Length && char.IsAsciiLetter(input[position])) position++;
            if (nameStart == position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), nonzero scale(), rotation, and finite matrix() gradient transforms are implemented.");
            ReadOnlySpan<char> name = input[nameStart..position];
            while (position < input.Length && SvgCssWhitespace.Is(input[position])) position++;
            if (position == input.Length || input[position] != '(') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), nonzero scale(), rotation, and finite matrix() gradient transforms are implemented.");
            position++;
            int argumentsStart = position;
            while (position < input.Length && input[position] != ')') position++;
            if (position == input.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), nonzero scale(), rotation, and finite matrix() gradient transforms are implemented.");
            transform = SvgGradientTransform.Compose(transform, ParseGradientTransformFunction(name, input[argumentsStart..position], cancellationToken));
            position = SvgGeometrySyntax.SkipTransformSeparator(input, position + 1);
        }
    }

    private static SvgGradientTransform ParseGradientTransformFunction(ReadOnlySpan<char> name, ReadOnlySpan<char> arguments, CancellationToken cancellationToken)
    {
        bool isMatrix = name.Equals("matrix".AsSpan(), StringComparison.Ordinal);
        bool isTranslate = name.Equals("translate".AsSpan(), StringComparison.Ordinal);
        bool isRotate = name.Equals("rotate".AsSpan(), StringComparison.Ordinal);
        bool isSkewX = name.Equals("skewX".AsSpan(), StringComparison.Ordinal);
        bool isSkewY = name.Equals("skewY".AsSpan(), StringComparison.Ordinal);
        if (!isMatrix && !isTranslate && !isRotate && !isSkewX && !isSkewY && !name.Equals("scale".AsSpan(), StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite translate(), nonzero scale(), rotation, skew and finite matrix() gradient transforms are implemented.");
        Span<double> values = stackalloc double[6];
        int valueCount = 0;
        SvgGeometrySyntax.DelimitedTokenReader tokens = new(arguments, SvgNumberListSyntax.Svg, 0);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, isMatrix ? "matrix() requires six coordinates." : "Transforms require one or two coordinates.");
            values[valueCount++] = ParseGradientTransformCoordinate(value, cancellationToken);
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
        if (isSkewX || isSkewY)
        {
            if (valueCount != 1) throw new SvgException(SvgErrorCode.MalformedInput, "skewX()/skewY() require one angle.");
            SvgAffineTransform map = isSkewX ? SvgAffineTransform.FromSkewXDegrees(values[0]) : SvgAffineTransform.FromSkewYDegrees(values[0]);
            return new(map.A, map.B, map.C, map.D, 0, 0);
        }
        if (valueCount is < 1 or > 2) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
        double first = values[0];
        double second = valueCount == 2 ? values[1] : (isTranslate ? 0 : first);
        if (isTranslate) return new SvgGradientTransform(1, 0, 0, 1, first, second);
        if (first == 0 || second == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero scale() gradient transforms are implemented.");
        return new SvgGradientTransform(first, 0, 0, second, 0, 0);
    }

    private static double ParseGradientTransformCoordinate(ReadOnlySpan<char> value, CancellationToken cancellationToken)
    {
        ReadOnlySpan<char> token = SvgCssWhitespace.Trim(value);
        if (!double.TryParse(token, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient transform coordinates must be finite bounded numbers.");
        return SvgFloatNumber.ReadValidated(token, cancellationToken);
    }


    internal static SvgGradientSpread ParseSpreadMethod(string? value)
    {
        if (value is null || value.Equals("pad", StringComparison.Ordinal)) return SvgGradientSpread.Pad;
        if (value.Equals("repeat", StringComparison.Ordinal)) return SvgGradientSpread.Repeat;
        if (value.Equals("reflect", StringComparison.Ordinal)) return SvgGradientSpread.Reflect;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad, repeat and reflect gradient spread methods are implemented.");
    }

    /// <summary>Validates the admitted default color interpolation shared by attributes and style declarations.</summary>
    internal static void ValidateColorInterpolation(ReadOnlySpan<char> value)
    {
        if (SvgCssWhitespace.Trim(value).Equals("sRGB".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only sRGB color interpolation is implemented.");
    }


    internal static string ParsePaintId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A paint server reference is required.");
        if (!SvgPaintReference.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
        return id;
    }

    internal static string ParseGradientId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A gradient reference is required.");
        if (!SvgPaintReference.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local gradient references are implemented.");
        return id;
    }

    internal static string ParseGradientHrefId(string? value)
    {
        if (value is null || value.Length < 2 || !value.StartsWith("#", StringComparison.Ordinal)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local gradient references are implemented.");
        ReadOnlySpan<char> id = value.AsSpan(1);
        if (id.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local gradient references are implemented.");
        return SvgLocalUrl.ParseFragment(id);
    }

    internal static bool IsGradientFill(string? fill)
        => SvgPaintReference.IsReference(fill);

    internal static double ParseStopOffset(string? value, CancellationToken cancellationToken)
    {
        if (value is null) return 0;
        ReadOnlySpan<char> token = value.AsSpan().Trim(SvgCssWhitespace.Characters);
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient stop values exceed the configured syntax limit.");
        bool percentage = token.EndsWith("%");
        if (percentage) token = token[..^1];
        if (!double.TryParse(token, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.MalformedInput, "Gradient stop offsets must be finite numbers.");
        float native = SvgFloatNumber.ReadValidated(token, cancellationToken);
        // Finite legacy tokens can overflow intermediate float place values,
        // including long leading-zero forms. Keep clamping free of NaN/Infinity.
        if (!float.IsFinite(native)) return percentage ? result / 100d : result;
        return percentage ? native / 100f : native;
    }

    internal static SvgGradientStop ParseStop(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, CancellationToken cancellationToken)
    {
        double offset = ParseStopOffset(reader.GetAttribute("offset"), cancellationToken);
        string? styleColor = null;
        string? styleOpacity = null;
        string? styleValue = reader.GetAttribute("style");
        if (styleValue is not null) ParseStopStyle(styleValue, out styleColor, out styleOpacity, cancellationToken);
        string? colorValue = styleColor ?? reader.GetAttribute("stop-color");
        SvgSolidPaint paint = colorValue is null ? new(new(0, 0, 0, 255), 1) : SvgColorParser.ParsePaint(colorValue, namedColors);
        SvgColor color = paint.Color;
        SvgPaintOpacity stopOpacity = SvgColorParser.ParsePaintOpacity(styleOpacity ?? reader.GetAttribute("stop-opacity"));
        cancellationToken.ThrowIfCancellationRequested();
        return new SvgGradientStop(offset, new SvgColor(color.Red, color.Green, color.Blue, SvgReferenceResolver.MultiplyAlpha(color.Alpha, stopOpacity.Alpha))) { Alpha = paint.Alpha * stopOpacity.Value };

        // Stop styles outrank attributes and resolve each property by importance,
        // then source order. Unrecognized exporter declarations remain ignored.
        static void ParseStopStyle(string value, out string? styleColor, out string? styleOpacity, CancellationToken cancellationToken)
        {
            styleColor = null;
            styleOpacity = null;
            bool colorImportant = false, opacityImportant = false;
            Span<char> commentScratch = stackalloc char[2 * SvgSyntaxLimits.MaximumValueTokenCharacters + 1];
            SvgInlineStyleParser.SvgInlineDeclarationReader declarations = new(value, cancellationToken);
            while (declarations.TryRead(out ReadOnlySpan<char> raw))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<char> declaration = SvgCssComments.NormalizeDeclaration(raw, commentScratch, cancellationToken);
                int colon = declaration.IndexOf(':');
                if (colon < 0) continue;
                ReadOnlySpan<char> name = SvgCssWhitespace.Trim(declaration[..colon]);
                bool color = name.Equals("stop-color", StringComparison.OrdinalIgnoreCase);
                bool opacity = name.Equals("stop-opacity", StringComparison.OrdinalIgnoreCase);
                if (!color && !opacity) continue;
                bool important = SvgInlineStyleParser.TryStripImportant(SvgCssWhitespace.Trim(declaration[(colon + 1)..]), out ReadOnlySpan<char> declared);
                if (color && (!colorImportant || important)) { styleColor = declared.ToString(); colorImportant = important; }
                if (opacity && (!opacityImportant || important)) { styleOpacity = declared.ToString(); opacityImportant = important; }
            }
        }
    }

    internal static SvgGeometry LowerGradientRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Fill), out SvgLinearGradient gradient))
        {
            if (state.RadialGradients.TryGetValue(ParseGradientId(style.Fill), out SvgRadialGradient radial)) return LowerRadialRect(reader, context, style, state, radial, frame);
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        }
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        if (gradient.RequiresSourceMap)
        {
            SvgAffineTransform map = frame is {} parent ? SvgAffineTransform.FromScaleOffset(parent.ScaleX, parent.ScaleY, parent.OffsetX, parent.OffsetY).ComposeChecked(transform) : transform;
            gradient = BindLinearSourceMap(gradient, new(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, false, null), map, state, context.CancellationToken);
        }
        else
        {
            gradient = MapUserSpaceVector(transform, gradient, frame);
            gradient = MirrorBoundingBoxVector(transform, gradient);
        }
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        bool painted = SvgPresentationResolver.IsPainted(style, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = style.GetOpacity().Alpha;
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
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, fillOpacity, painted ? stroke : null));
    }

    /// <summary>Lowers one radial-gradient-filled rectangle with an optional solid stroke.</summary>
    internal static SvgGeometry LowerRadialRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgRadialGradient gradient, SvgViewportFrame? frame)
    {
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        gradient = MapRadialReference(transform, gradient, frame);
        SvgRectangle placement = transform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, true, null));
        bool painted = SvgPresentationResolver.IsPainted(style, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = style.GetOpacity().Alpha;
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

    /// <summary>Lowers a radial path through retained source commands, or a point-list fill through its shared shape box.</summary>
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
        if (!isPath && transform.IsAxisAligned) gradient = MapRadialReference(transform, gradient, frame);
        bool painted = SvgPresentationResolver.IsPainted(style, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(style.GetOpacity().Alpha, SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        if (isPath && painted && !gradient.IsEmpty)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasCurve, bool hasArc, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            // Retain authored path contours and repaint their shared fill with the radial circle.
            SvgComputedStyle solidFill = style with { Fill = "black" };
            SvgResolvedPaintableElement curveShape = context.ResolveShape(solidFill, reader, true);
            using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, elementName, true, isEvenOdd, maximumGeometryElements, budget, context.CancellationToken);
            SvgPolygonMapping mapping = hasCurve ? SvgPolygonMapping.FlattenedCurves : SvgPolygonMapping.FiniteEdges;
            SvgParsedGeometry filled = curveLowering.LowerDeviceFill(d, commandCount, hasCurve, mapping, curveShape, frame, false, budget);
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            try
            {
                // Axis and skewed placement retain the same definition/local/reference stages.
                SvgAuthoredPath sourcePath = filled.Geometries[0].AuthoredPath ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Radial path paints require retained source commands.");
                SvgRectangle paintBox = gradient.IsUserSpace ? default : SvgPathBounds.Compute(sourcePath.Commands, context.CancellationToken);
                gradient = BindRadialSourceMap(gradient, paintBox, sourcePath.SceneMap, context.CancellationToken);
                foreach (SvgGeometry existing in filled.Geometries)
                {
                    SvgGeometry repaint = existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, gradient, paintBox, opacity, budget));
                    try { repainted.Add(repaint); }
                    catch { repaint.Dispose(); throw; }
                }
                filled.Dispose();
                return AppendPaintFillStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            }
            catch { foreach (SvgGeometry geometry in repainted.AsSpan()) geometry.Dispose(); filled.Discard(); throw; }
        }
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, maximumGeometryElements, budget, context, contourEnds);
        SvgRectangle box = BoxFromRadialBands(points, transform, frame, context);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, isPath, isEvenOdd, maximumGeometryElements, budget, context.CancellationToken);
        if (SvgOrthogonalShapeParser.HasDiagonalContour(points, contourEnds))
        {
            return AppendPaintFillStroke(lowering.LowerRadialPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
        }
        SvgParsedGeometry solid = lowering.Lower(default, painted && !gradient.IsEmpty, transform, frame);
        SvgGeometry[] bands = solid.Storage.Items;
        try
        {
            for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                bands[bandIndex] = SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(gradient, box, bands[bandIndex].Rectangle, opacity, null, state.Budget));
            }
        }
        catch { solid.Discard(); throw; }
        return AppendPaintFillStroke(solid, reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);

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
        bool paintPatternStroke = !paintRadialStroke && !paintStroke && SvgOrthogonalShapeParser.TryResolveOutlinePattern(style, state, context.CancellationToken, out outlineTile);
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
        bool bindSourceMap = isPath && (!transform.IsAxisAligned || gradient.RequiresSourceMap);
        if (gradient.RequiresSourceMap && !isPath) throw new SvgException(SvgErrorCode.UnsupportedFeature, "General linear paint maps on point-list geometry are not implemented.");
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
        if (paintRadialStroke) outlineCircle = MapRadialReference(transform, outlineCircle, frame);
        bool painted = SvgPresentationResolver.IsPainted(style, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
        byte opacity = SvgReferenceResolver.MultiplyAlpha(style.GetOpacity().Alpha, SvgColorParser.ParseOpacity(style.FillOpacity));
        SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
        bool isEvenOdd = style.FillRule is not null && style.FillRule.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
        if (isPath && painted && !gradient.IsEmpty)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.CancellationToken);
            string d = renderData.Value;
            (_, bool hasCurve, bool hasArc, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.CancellationToken);
            int commandCount = renderData.CommandCount;

            // Retain authored path contours and repaint their shared fill with the gradient.
            // The gradient url is not a parseable color; resolve a solid placeholder (discarded by the repaint below) for transform and fill state.
            SvgComputedStyle solidFill = strokeStyle with { Fill = "black" };
            SvgResolvedPaintableElement curveShape = context.ResolveShape(solidFill, reader, true);
            using BudgetedParserList<SvgPoint> noPoints = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            using BudgetedParserList<int> noEnds = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            using SvgOrthogonalGeometryLowering curveLowering = new(noPoints, noEnds, elementName, true, isEvenOdd, maximumGeometryElements, budget, context.CancellationToken);
            SvgPolygonMapping mapping = hasCurve ? SvgPolygonMapping.FlattenedCurves : SvgPolygonMapping.FiniteEdges;
            SvgParsedGeometry filled = curveLowering.LowerDeviceFill(d, commandCount, hasCurve, mapping, curveShape, frame, false, budget);
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            try
            {
                SvgRectangle paintBox = gradient.IsUserSpace ? default : SvgOrthogonalShapeParser.BoxFromFilledGeometryBounds(filled, context.CancellationToken);
                if (bindSourceMap && filled.Geometries.Length > 0)
                {
                    SvgAuthoredPath sourcePath = filled.Geometries[0].AuthoredPath ?? throw new InvalidOperationException("Mapped linear fills require source commands.");
                    if (!gradient.IsUserSpace) paintBox = SvgPathBounds.Compute(sourcePath.Commands, context.CancellationToken);
                    gradient = BindLinearSourceMap(gradient, paintBox, sourcePath.SceneMap, state, context.CancellationToken);
                    paintBox = default;
                }
                foreach (SvgGeometry existing in filled.Geometries)
                {
                    SvgGeometry repaint = existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, gradient, paintBox, opacity));
                    try { repainted.Add(repaint); }
                    catch { repaint.Dispose(); throw; }
                }
                filled.Dispose();
                if (!paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
                if (paintPatternStroke) return AppendPaintOutlineStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
                if (paintRadialStroke) return AppendPaintOutlineStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, paintBox);
                return AppendPaintOutlineStrokePath(new SvgParsedGeometry(repainted.Detach(), filled.ElementCount), d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, paintBox);
            }
            catch { foreach (SvgGeometry geometry in repainted.AsSpan()) geometry.Dispose(); filled.Discard(); throw; }
        }
        using BudgetedParserList<int> contourEnds = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalShapeParser.TokenizeBands(reader, elementName, isPath, maximumGeometryElements, budget, context, contourEnds);
        SvgRectangle box = BoxFromGradientBands(points, transform, frame, context);
        using SvgOrthogonalGeometryLowering lowering = new(points, contourEnds, elementName, isPath, isEvenOdd, maximumGeometryElements, budget, context.CancellationToken);
        if (SvgOrthogonalShapeParser.HasDiagonalContour(points, contourEnds))
        {
            if (!paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            if (paintPatternStroke) return AppendPaintOutlineStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
            if (paintRadialStroke) return AppendPaintOutlineStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, box);
            return AppendPaintOutlineStroke(lowering.LowerGradientPolygonFill(gradient, box, painted && !gradient.IsEmpty, opacity, transform, frame, budget), reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, box);
        }
        SvgParsedGeometry solid = lowering.Lower(default, painted && !gradient.IsEmpty, transform, frame);
        try
        {
            SvgGeometry[] bands = solid.Storage.Items;
            for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                bands[bandIndex] = SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, bands[bandIndex].Rectangle, opacity, null));
            }
            if (!paintStroke && !paintRadialStroke && !paintPatternStroke) return AppendPaintFillStroke(solid, reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape);
            if (paintPatternStroke) return AppendPaintOutlineStroke(solid, reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineTile);
            if (paintRadialStroke) return AppendPaintOutlineStroke(solid, reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineCircle, box);
            return AppendPaintOutlineStroke(solid, reader, context, elementName, isPath, points, frame, maximumGeometryElements, budget, bandStroke, bandStrokeShape, outlineGradient, box);
        }
        catch { solid.Discard(); throw; }
    }

    /// <summary>Appends an admitted solid stroke outline to paint-filled bands and outlines, leaving unadmitted strokes to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintFillStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape)
    {
        if (stroke is null) return filled;
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.CancellationToken);
            string d = renderData.Value;
            (_, _, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            return AppendPaintFillStrokePath(filled, d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, stroke, strokeShape);
        }
        SvgParsedGeometry stroked = LowerPaintStrokePolyline(filled, points, context, elementName, frame, maximumGeometryElements, budget, stroke.Value, strokeShape);
        return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, stroked, budget, context.CancellationToken);
    }

    /// <summary>Appends a linear-gradient stroke outline lowered from path data to paint-filled geometry by lowering a white outline and repainting it.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasCurve, SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgLinearGradient outline, SvgRectangle strokeBox)
    {
        if (stroke is null) return filled;
        SvgParsedGeometry white = LowerPaintStrokePath(filled, d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, stroke.Value, strokeShape);
        return RepaintAndConcatOutlines(filled, white, outline, strokeBox, strokeShape, context, budget);
    }

    /// <summary>Appends a pattern stroke outline lowered from path data to paint-filled geometry by lowering a white outline and repainting it.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasCurve, SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgPatternTile outlineTile)
    {
        if (stroke is null) return filled;
        SvgParsedGeometry white = LowerPaintStrokePath(filled, d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, stroke.Value, strokeShape);
        return RepaintAndConcatOutlines(filled, white, outlineTile, strokeShape, context, frame, budget);
    }

    /// <summary>Appends a radial-gradient stroke outline lowered from path data to paint-filled geometry by lowering a white outline and repainting it.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasCurve, SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgRadialGradient outlineCircle, SvgRectangle strokeBox)
    {
        if (stroke is null) return filled;
        SvgParsedGeometry white = LowerPaintStrokePath(filled, d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, stroke.Value, strokeShape);
        return RepaintAndConcatOutlines(filled, white, outlineCircle, strokeBox, strokeShape, context, budget);
    }

    /// <summary>Lowers one source path stroke, leaving fill ownership with the caller on success.</summary>
    /// <remarks>Local lists and the lowerer own scratch under the existing memory budget and cancellation
    /// limits. Failure discards the supplied fill; success transfers the returned stroke to its caller.</remarks>
    private static SvgParsedGeometry LowerPaintStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasCurve,
        SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements,
        RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke stroke, SvgResolvedPaintableElement strokeShape)
    {
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, true, false, maximumGeometryElements, budget, context.CancellationToken);
            return strokeLowering.LowerStrokePath(d, commandCount, hasCurve, strokeShape, stroke.Color, strokeShape.Presentation.IsPainted, stroke.Width, stroke.MiterLimit, stroke.Cap, stroke.Join, stroke.DashPattern, stroke.DashOffset, stroke.PathLength, stroke.NonScalingStroke, frame, budget);
        }
        catch { filled.Discard(); throw; }
    }

    /// <summary>Lowers one source point-list stroke, leaving fill ownership with the caller on success.</summary>
    /// <remarks>Local lists and the lowerer own scratch under the existing memory budget and cancellation
    /// limits. Failure discards the supplied fill; success transfers the returned stroke to its caller.</remarks>
    private static SvgParsedGeometry LowerPaintStrokePolyline(SvgParsedGeometry filled, BudgetedParserList<SvgPoint> points,
        SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements,
        RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke stroke, SvgResolvedPaintableElement strokeShape)
    {
        try
        {
            using BudgetedParserList<SvgPoint> noStrokePoints = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            using BudgetedParserList<int> noStrokeEnds = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            using SvgOrthogonalGeometryLowering strokeLowering = new(noStrokePoints, noStrokeEnds, elementName, false, false, maximumGeometryElements, budget, context.CancellationToken);
            return strokeLowering.LowerStrokePolyline(points, elementName == "polygon", strokeShape, stroke.Width, stroke.MiterLimit, stroke.Cap, stroke.Join, stroke.DashPattern, stroke.DashOffset, stroke.PathLength, stroke.NonScalingStroke, stroke.Color, strokeShape.Presentation.IsPainted, frame, budget);
        }
        catch { filled.Discard(); throw; }
    }

    /// <summary>Repaints white stroke outlines with a linear-gradient paint and concatenates them after paint-filled geometry, transferring ownership on every path.</summary>
    private static SvgParsedGeometry RepaintAndConcatOutlines(SvgParsedGeometry filled, SvgParsedGeometry white, SvgLinearGradient outline, SvgRectangle strokeBox, SvgResolvedPaintableElement strokeShape, SvgPaintableElementContext context, RenderBudget budget)
    {
        bool whiteReleased = false;
        try
        {
            SvgRectangle paintBox = strokeBox;
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            byte strokeOpacity = strokeShape.Presentation.Opacity;
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(existing.TransferAuthoredPathTo(SvgGradientPolygon.RepaintOutline(existing.Polygon, outline, paintBox, strokeOpacity)));
            int whiteCount = white.ElementCount;
            white.Dispose();
            whiteReleased = true;
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, new SvgParsedGeometry(repainted.Detach(), whiteCount), budget, context.CancellationToken);
        }
        catch
        {
            if (!whiteReleased) white.Discard();
            filled.Discard();
            throw;
        }
    }

    /// <summary>Repaints white stroke outlines with a pattern paint and concatenates them after paint-filled geometry, transferring ownership on every path.</summary>
    private static SvgParsedGeometry RepaintAndConcatOutlines(SvgParsedGeometry filled, SvgParsedGeometry white, SvgPatternTile outlineTile, SvgResolvedPaintableElement strokeShape, SvgPaintableElementContext context, SvgViewportFrame? frame, RenderBudget budget)
    {
        bool whiteReleased = false;
        try
        {
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            byte strokeOpacity = strokeShape.Presentation.Opacity;
            SvgAffineTransform placement = SvgViewportFrame.MapGeometry(strokeShape.Transform, frame);
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(existing.TransferAuthoredPathTo(SvgPatternPolygon.RepaintOutline(existing.Polygon, outlineTile, strokeOpacity, placement)));
            int whiteCount = white.ElementCount;
            white.Dispose();
            whiteReleased = true;
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, new SvgParsedGeometry(repainted.Detach(), whiteCount), budget, context.CancellationToken);
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
            using BudgetedGeometryBuilder repainted = new(budget, SvgMemoryCategory.Geometry, context.CancellationToken);
            byte strokeOpacity = strokeShape.Presentation.Opacity;
            foreach (SvgGeometry existing in white.Geometries) repainted.Add(existing.TransferAuthoredPathTo(SvgRadialPolygon.RepaintOutline(existing.Polygon, outlineCircle, paintBox, strokeOpacity, budget)));
            int whiteCount = white.ElementCount;
            white.Dispose();
            whiteReleased = true;
            return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, new SvgParsedGeometry(repainted.Detach(), whiteCount), budget, context.CancellationToken);
        }
        catch
        {
            if (!whiteReleased) white.Discard();
            filled.Discard();
            throw;
        }
    }

    /// <summary>Appends an admitted solid stroke outline lowered from path data to paint-filled geometry.</summary>
    internal static SvgParsedGeometry AppendPaintFillStrokePath(SvgParsedGeometry filled, string d, int commandCount, bool hasCurve, SvgPaintableElementContext context, string elementName, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape)
    {
        if (stroke is null) return filled;
        SvgParsedGeometry stroked = LowerPaintStrokePath(filled, d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, stroke.Value, strokeShape);
        return SvgOrthogonalShapeParser.ConcatParsedGeometries(filled, stroked, budget, context.CancellationToken);
    }

    /// <summary>Appends a linear-gradient stroke outline to paint-filled bands and outlines by lowering a white outline and repainting it, leaving other paints to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgLinearGradient outline, SvgRectangle strokeBox)
    {
        if (stroke is null) return filled;
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.CancellationToken);
            string d = renderData.Value;
            (_, _, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            return AppendPaintOutlineStrokePath(filled, d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, stroke, strokeShape, outline, strokeBox);
        }
        SvgParsedGeometry white = LowerPaintStrokePolyline(filled, points, context, elementName, frame, maximumGeometryElements, budget, stroke.Value, strokeShape);
        return RepaintAndConcatOutlines(filled, white, outline, strokeBox, strokeShape, context, budget);
    }

    /// <summary>Appends a pattern stroke outline to paint-filled bands and outlines by lowering a white outline and repainting it, leaving other paints to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgPatternTile outlineTile)
    {
        if (stroke is null) return filled;
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.CancellationToken);
            string d = renderData.Value;
            (_, _, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            return AppendPaintOutlineStrokePath(filled, d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, stroke, strokeShape, outlineTile);
        }
        SvgParsedGeometry white = LowerPaintStrokePolyline(filled, points, context, elementName, frame, maximumGeometryElements, budget, stroke.Value, strokeShape);
        return RepaintAndConcatOutlines(filled, white, outlineTile, strokeShape, context, frame, budget);
    }

    /// <summary>Appends a radial-gradient stroke outline to paint-filled bands and outlines by lowering a white outline and repainting it, leaving other paints to the shared stroke gates.</summary>
    internal static SvgParsedGeometry AppendPaintOutlineStroke(SvgParsedGeometry filled, XmlReader reader, SvgPaintableElementContext context, string elementName, bool isPath, BudgetedParserList<SvgPoint> points, SvgViewportFrame? frame, int maximumGeometryElements, RenderBudget budget, SvgStrokeStyleSyntax.SvgAdmittedStroke? stroke, SvgResolvedPaintableElement strokeShape, SvgRadialGradient outlineCircle, SvgRectangle strokeBox)
    {
        if (stroke is null) return filled;
        if (isPath)
        {
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumGeometryElements, budget, context.CancellationToken);
            string d = renderData.Value;
            (_, _, bool hasCurve, _) = SvgOrthogonalShapeParser.ScanPathData(d, context.CancellationToken);
            int commandCount = renderData.CommandCount;
            return AppendPaintOutlineStrokePath(filled, d, commandCount, hasCurve, context, elementName, frame, maximumGeometryElements, budget, stroke, strokeShape, outlineCircle, strokeBox);
        }
        SvgParsedGeometry white = LowerPaintStrokePolyline(filled, points, context, elementName, frame, maximumGeometryElements, budget, stroke.Value, strokeShape);
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
        => SvgPaintReference.IsReference(stroke);

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

    /// <summary>Composes an axis reference with a general radial paint, treating collapsed axes as empty paint.</summary>
    internal static SvgRadialGradient ScaleGeneralRadial(SvgRadialGradient gradient, double scaleX, double scaleY, double offsetX, double offsetY)
    {
        if (scaleX == 0 || scaleY == 0) return gradient with { IsEmpty = true };
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
        if (gradient.RequiresSourceMap && gradient.SourcePaint?.Data.IsMapped != true) throw new SvgException(SvgErrorCode.UnsupportedFeature, "General linear paint maps require source-bound fill geometry.");
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
        SvgAffineTransform sceneMap, SvgDocumentState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
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
    /// reference matrices stay distinct for native float concatenation. Retained definition coordinates
    /// replace axis-resolved coordinates before binding so the local transform is applied exactly once.
    /// The inverse supports the existing
    /// focal evaluator. The caller checks cancellation during this bounded setup; singular maps reject without owned storage.</remarks>
    internal static SvgRadialGradient BindRadialSourceMap(SvgRadialGradient gradient, SvgRectangle sourceBox, SvgAffineTransform sceneMap, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!gradient.IsUserSpace && (sourceBox.Width <= 0 || sourceBox.Height <= 0)) return gradient with { IsEmpty = true };
        SvgGradientTransform localInverse = gradient.HasGeneralTransform ? gradient.Inverse : SvgGradientTransform.Identity;
        SvgShaderMatrix local = gradient.NativePaintMatrices?.Local ?? new(1, 0, 0, 0, 1, 0);
        if (gradient.SourceDefinition is {} definition)
        {
            SvgGradientTransform transform = definition.Transform;
            if (!transform.TryInvert(out localInverse))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular radial gradient definition map.");
            local = new((float)transform.A, (float)transform.C, (float)transform.E,
                (float)transform.B, (float)transform.D, (float)transform.F);
            gradient = gradient with { CenterX = definition.CenterX, CenterY = definition.CenterY, Radius = definition.Radius,
                FocalX = definition.FocalX, FocalY = definition.FocalY, FocalRadius = definition.FocalRadius,
                ReferenceScaleX = 1, ReferenceScaleY = 1 };
        }
        SvgGradientTransform objectMap = gradient.IsUserSpace ? SvgGradientTransform.Identity
            : new(sourceBox.Width, 0, 0, sourceBox.Height, sourceBox.X, sourceBox.Y);
        SvgGradientTransform reference = new(sceneMap.A, sceneMap.B, sceneMap.C, sceneMap.D, sceneMap.E, sceneMap.F);
        if (!SvgGradientTransform.Compose(reference, objectMap).TryInvert(out SvgGradientTransform inverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular radial paint referencing maps are not implemented.");
        SvgShaderMatrix nativeObject = new((float)objectMap.A, 0, (float)objectMap.E, 0, (float)objectMap.D, (float)objectMap.F);
        SvgAffineTransform nativeReference = sceneMap;
        return gradient with { IsUserSpace = true, HasGeneralTransform = true,
            Inverse = SvgGradientTransform.Compose(localInverse, inverse),
            NativePaintMatrices = new(SvgShaderMatrix.Concat(nativeObject, local), nativeReference) };
    }

    /// <summary>Maps a radial paint through an axis reference, preserving object-box reflection and user-space scale records.</summary>
    /// <remarks>Reflected paints retain reference matrices so native float preparation keeps
    /// definition coordinates separate from the local paint map. Callers bind general source maps.</remarks>
    internal static SvgRadialGradient MapRadialReference(SvgAffineTransform transform, SvgRadialGradient gradient, SvgViewportFrame? frame)
    {
        if (!gradient.IsUserSpace)
        {
            bool mirrorX = transform.A < 0, mirrorY = transform.D < 0;
            if (!transform.IsAxisAligned || !mirrorX && !mirrorY) return gradient;
            if (!gradient.HasGeneralTransform)
                gradient = gradient with { HasGeneralTransform = true, Inverse = SvgGradientTransform.Identity,
                    NativePaintMatrices = new(new(1, 0, 0, 0, 1, 0), SvgAffineTransform.Identity) };
            return ScaleGeneralRadial(gradient, mirrorX ? -1 : 1, mirrorY ? -1 : 1, mirrorX ? 1 : 0, mirrorY ? 1 : 0);
        }
        if (!transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned gradient transforms are implemented.");
        bool canMapFocalReference = gradient.FocalRadius != 0
            && TryInvertAxisMap(transform.A, transform.D, transform.E, transform.F, out _)
            && (frame is null || TryInvertAxisMap(frame.Value.ScaleX, frame.Value.ScaleY, frame.Value.OffsetX, frame.Value.OffsetY, out _));
        if (!gradient.HasGeneralTransform && (transform.A < 0 || transform.D < 0 || canMapFocalReference))
            gradient = gradient with { HasGeneralTransform = true, Inverse = SvgGradientTransform.Identity,
                NativePaintMatrices = new(new(1, 0, 0, 0, 1, 0), SvgAffineTransform.Identity) };
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
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.CancellationToken);
        if (state.Patterns.TryGetValue(ParseGradientId(style.Stroke), out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern strokes are not implemented.");
        if (!state.Gradients.TryGetValue(ParseGradientId(style.Stroke), out SvgLinearGradient gradient)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
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
                fillCircle = MapRadialReference(transform, resolvedCircle, frame);
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
            painted = SvgPresentationResolver.IsPainted(style, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
            opacity = style.GetOpacity().Alpha;
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
        SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.Width, placement.Height, 255, painted);
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
        using BudgetedGeometryBuilder stroked = new(state.Budget, SvgMemoryCategory.Geometry, context.CancellationToken);
        if (hasLinearFill) stroked.Add(LowerLinearFillGeometry(fillGradient, box, fill, fillHasFill, fillOpacity));
        else if (hasRadialFill) stroked.Add(LowerRadialFillGeometry(fillCircle, box, fill, fillHasFill, fillOpacity, state.Budget));
        if (probe is null)
        {
            // Zero width paints no stroke bands; probe nulls only on zero widths here.
            if (!hasLinearFill && !hasRadialFill) stroked.Add(SvgGeometry.FromRectangle(fill));
            return new SvgParsedGeometry(stroked.Detach(), 0);
        }
        SvgRectangle strokeFill = combined ? fill with { HasFill = false } : fill;
        stroked.Add(SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(strokeFill, top, bottom, left, right, gradient, box, opacity, painted)));
        return new SvgParsedGeometry(stroked.Detach(), 0);
    }

    /// <summary>Lowers one gradient-stroked axis-aligned line into a gradient-evaluated stroke band.</summary>
    /// <summary>Lowers one radial-gradient-stroked rectangle into one solid fill plus four radial-evaluated stroke bands sharing the placement box.</summary>
    internal static SvgParsedGeometry LowerRadialStrokeRect(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state, SvgViewportFrame? frame)
    {
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.CancellationToken);
        if (state.Patterns.TryGetValue(ParseGradientId(style.Stroke), out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern strokes are not implemented.");
        if (!state.RadialGradients.TryGetValue(ParseGradientId(style.Stroke), out SvgRadialGradient circle)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Strokes in scaled viewports are not implemented.");
        SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
        SvgAffineTransform transform = context.ResolveTransform(reader);
        circle = MapRadialReference(transform, circle, frame);
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
                fillCircle = MapRadialReference(transform, resolvedCircle, frame);
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
            painted = SvgPresentationResolver.IsPainted(style, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible);
            opacity = style.GetOpacity().Alpha;
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
        SvgStroke? probe = SvgLeafParser.ParseRectangleStroke(new SvgColor(255, 255, 255, 255), style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.Width, placement.Height, 255, painted);
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
        using BudgetedGeometryBuilder stroked = new(state.Budget, SvgMemoryCategory.Geometry, context.CancellationToken);
        if (hasLinearFill) stroked.Add(LowerLinearFillGeometry(fillGradient, box, fill, fillHasFill, fillOpacity));
        else if (hasRadialFill) stroked.Add(LowerRadialFillGeometry(fillCircle, box, fill, fillHasFill, fillOpacity, state.Budget));
        else stroked.Add(SvgGeometry.FromRectangle(fill));
        if (probe is null)
        {
            // Zero width retains only the fill operation above.
            return new SvgParsedGeometry(stroked.Detach(), 0);
        }
        bool paintBands = painted && !circle.IsEmpty;
        stroked.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, top with { HasFill = paintBands }, opacity, null, state.Budget)) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        stroked.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, bottom with { HasFill = paintBands }, opacity, null, state.Budget)) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        stroked.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, left with { HasFill = paintBands }, opacity, null, state.Budget)) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        stroked.Add(SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(circle, box, right with { HasFill = paintBands }, opacity, null, state.Budget)) with { RecordedPaint = SvgRecordedPaint.SourceStroke });
        return new SvgParsedGeometry(stroked.Detach(), 0);
    }

    /// <summary>Lowers one linear gradient fill rectangle shared by combined fill-and-stroke lowerings, mirroring the fill-only lowering.</summary>
    internal static SvgGeometry LowerLinearFillGeometry(SvgLinearGradient fillGradient, SvgRectangle box, SvgRectangle fill, bool fillHasFill, byte fillOpacity)
    {
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(fillGradient, box, fill with { HasFill = fillHasFill }, fillOpacity, null));
    }

    /// <summary>Lowers one radial gradient fill rectangle shared by combined fill-and-stroke lowerings, mirroring the fill-only lowering.</summary>
    internal static SvgGeometry LowerRadialFillGeometry(SvgRadialGradient fillCircle, SvgRectangle box, SvgRectangle fill, bool fillHasFill, byte fillOpacity, RenderBudget budget)
    {
        return SvgGeometry.FromRadialFill(SvgRadialGeometry.Create(fillCircle, box, fill with { HasFill = fillHasFill }, fillOpacity, null, budget));
    }

    private static SvgStroke? ParseGradientStroke(XmlReader reader, SvgComputedStyle style, SvgPaintableElementContext context, SvgAffineTransform transform, SvgRectangle placement, byte opacity, bool painted)
    {
        if (style.Stroke is null || style.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"))) return null;
        if (IsGradientStrokeUrl(style.Stroke)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient fills with gradient strokes are not implemented.");
        SvgColor strokeColor = SvgPaintResolver.ResolveStrokeColor(style.Stroke, style.Color, context.InheritedPresentation.Paint, context.NamedColors);
        if (style.StrokeOpacity is not null) strokeColor = SvgPaintResolver.ApplyOpacity(strokeColor, SvgColorParser.ParseOpacity(style.StrokeOpacity));
        return SvgLeafParser.ParseRectangleStroke(strokeColor, style.Width ?? reader.GetAttribute("stroke-width"), transform, placement.Width, placement.Height, opacity, painted);
    }

}
