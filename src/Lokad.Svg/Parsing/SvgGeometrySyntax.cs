using System.Globalization;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgViewportCoordinateKind { Oval, Rectangle }

/// <summary>Parses the bounded numeric, transform, viewport, and viewBox syntax shared by supported geometry.</summary>
internal static class SvgGeometrySyntax
{
    internal static SvgAffineTransform ParseTransform(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (value is null) return SvgAffineTransform.Identity;
        workCounter.ConsumeBytes(value.Length, cancellationToken);
        if (value.Length > SvgSyntaxLimits.MaximumTransformCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Transform values exceed the configured syntax limit.");
        ReadOnlySpan<char> input = value.AsSpan();
        SvgAffineTransform transform = SvgAffineTransform.Identity;
        int position = 0;
        bool fullMatrix = false;
        while (true)
        {
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            if (position == input.Length) return transform;

            int nameStart = position;
            while (position < input.Length && char.IsAsciiLetter(input[position])) position++;
            if (nameStart == position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A transform function name is required.");
            ReadOnlySpan<char> name = input[nameStart..position];
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            if (position == input.Length || input[position] != '(') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Transform arguments must start with '('.");
            position++;
            int argumentsStart = position;
            while (position < input.Length && input[position] != ')') position++;
            if (position == input.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Transform arguments must end with ')'.");
            SvgAffineTransform function = ParseTransformFunction(name, input[argumentsStart..position], workCounter, cancellationToken);
            // Local scale/translate lists use native float axis storage until a
            // matrix, rotation or skew promotes the list to double precision.
            // Enclosing groups still compose their finished local maps in double.
            fullMatrix |= !(name.Equals("translate", StringComparison.Ordinal) || name.Equals("scale", StringComparison.Ordinal));
            transform = fullMatrix ? transform.ComposeChecked(function) : ComposeAxis(transform, function);
            position = SkipTransformSeparator(input, position + 1);
        }

        static SvgAffineTransform ComposeAxis(SvgAffineTransform outer, SvgAffineTransform inner)
        {
            // Preserve the range gate before narrowing, then validate the rounded
            // result too. Each native multiply and add rounds separately.
            outer.ComposeChecked(inner);
            return SvgAffineTransform.FromScaleOffset((float)outer.A * (float)inner.A, (float)outer.D * (float)inner.D,
                (float)outer.A * (float)inner.E + (float)outer.E, (float)outer.D * (float)inner.F + (float)outer.F).ComposeChecked(SvgAffineTransform.Identity);
        }
    }

    /// <summary>Consumes an optional comma between functions, rejecting a trailing separator.</summary>
    internal static int SkipTransformSeparator(ReadOnlySpan<char> input, int position)
    {
        while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
        if (position < input.Length && input[position] == ',')
        {
            do { position++; } while (position < input.Length && char.IsWhiteSpace(input[position]));
            if (position == input.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A transform function is required after a comma.");
        }
        return position;
    }

    internal static double ParseRectangleCoordinate(string value)
    {
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle coordinate values exceed the configured syntax limit.");
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px rectangle coordinates are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle coordinates must be finite bounded numbers.");
        return result;
    }

    /// <summary>Resolves an explicit bare-number or percentage viewport dimension against its base.</summary>
    internal static double ParseViewportDimension(string value, double? baseValue)
    {
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Viewport dimension values exceed the configured syntax limit.");
        if (token.EndsWith("%"))
        {
            if (baseValue is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Percentage viewport dimensions require known parent dimensions.");
            ReadOnlySpan<char> number = token[..^1].TrimEnd();
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) || !double.IsFinite(percent)) throw new SvgException(SvgErrorCode.MalformedInput, "Percentage viewport dimensions must be finite numbers.");
            double resolved = MultiplyDivide(percent, baseValue.Value, 100);
            if (!double.IsFinite(resolved)) throw new SvgException(SvgErrorCode.LimitExceeded, "Percentage viewport dimensions exceed the admitted numeric range.");
            return resolved;
        }
        return ParseRectangleCoordinate(value);
    }

    /// <summary>Computes a finite length times a finite factor over a positive divisor, retaining ordinary rounding while avoiding intermediate range loss.</summary>
    internal static double MultiplyDivide(double length, double factor, double divisor)
    {
        double product = length * factor;
        if (double.IsNormal(product) || length == 0 || factor == 0) return product / divisor;
        int lengthExponent = Math.ILogB(length);
        int factorExponent = Math.ILogB(factor);
        int divisorExponent = Math.ILogB(divisor);
        double fraction = Math.ScaleB(length, -lengthExponent) * (Math.ScaleB(factor, -factorExponent)
            / Math.ScaleB(divisor, -divisorExponent));
        return Math.ScaleB(fraction, lengthExponent + factorExponent - divisorExponent);
    }
    /// <summary>Parses one finite direct-text coordinate without the rectangle fast path's quarter-pixel restriction.</summary>
    internal static double ParseTextCoordinate(string value)
    {
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Text coordinate values exceed the configured syntax limit.");
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px text coordinates are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text coordinates must be finite numbers.");
        return result;
    }

    /// <summary>Resolves an absolute font size in CSS pixels without admitting physical units for text positions.</summary>
    internal static double ParseFontSize(string value)
    {
        ReadOnlySpan<char> token = SvgCssWhitespace.Trim(value.AsSpan());
        if (SvgCssWhitespace.HasNonAsciiBoundaryWhitespace(token)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-ASCII whitespace is not a font size separator.");
        // Keep the existing finite bare/px validation and its unsupported-size outcomes.
        if (token.Length == 0 || token.EndsWith("px", StringComparison.OrdinalIgnoreCase) || !char.IsLetter(token[^1]) && token[^1] != '%') return ParseTextCoordinate(value);
        return ParseLength(value) ?? throw new InvalidOperationException("Font size parsing returned no value.");
    }

    internal static int ParseInteger(string value) => ParseInteger(value.AsSpan());

    internal static int ParseInteger(ReadOnlySpan<char> value)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Integer coordinate values exceed the configured syntax limit.");
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px integer rectangle coordinates are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result != Math.Truncate(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer rectangle coordinates are implemented.");
        return checked((int)result);
    }

    internal static SvgRootLength? ParseRootLength(string? value)
    {
        if (value is null) return null;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Root length values exceed the configured syntax limit.");
        if (token.EndsWith("%", StringComparison.Ordinal))
        {
            ReadOnlySpan<char> number = token[..^1].TrimEnd();
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) || !double.IsFinite(percent) || percent <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "Root dimensions must be finite positive lengths.");
            return new SvgRootLength(percent, true);
        }
        return new SvgRootLength(ParseLength(value) ?? throw new InvalidOperationException("Root length parsing returned no value."), false);
    }

    internal static double? ParseLength(string? value)
    {
        if (value is null) return null;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Root length values exceed the configured syntax limit.");
        double multiplier = 1;
        bool hasSupportedUnit = false;
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); hasSupportedUnit = true; }
        else if (token.EndsWith("in", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 96; hasSupportedUnit = true; }
        else if (token.EndsWith("cm", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 96d / 2.54d; hasSupportedUnit = true; }
        else if (token.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 96d / 25.4d; hasSupportedUnit = true; }
        else if (token.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 96d / 72d; hasSupportedUnit = true; }
        else if (token.EndsWith("pc", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 16; hasSupportedUnit = true; }
        else if (token.IndexOfAny("%mivc".AsSpan()) >= 0 || (token.Length > 0 && char.IsLetter(token[^1]))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number, px, in, cm, mm, pt, pc, and percent root dimensions are implemented.");
        if (hasSupportedUnit && token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number, px, in, cm, mm, pt, pc, and percent root dimensions are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result <= 0 || !double.IsFinite(result *= multiplier)) throw new SvgException(SvgErrorCode.MalformedInput, "Root dimensions must be finite positive CSS pixel lengths.");
        return result;
    }

    /// <summary>Maps viewBox units into a viewport box under its alignment policy.</summary>
    internal static (double ScaleX, double ScaleY, double OffsetX, double OffsetY) MapViewBox(SvgViewBox viewBox, double viewportWidth, double viewportHeight)
    {
        double x = (float)viewBox.X;
        double y = (float)viewBox.Y;
        double width = (float)viewBox.Width;
        double height = (float)viewBox.Height;
        double physicalWidth = (float)viewportWidth;
        double physicalHeight = (float)viewportHeight;
        if (viewBox.HorizontalAlignment == SvgViewBoxAlignment.None)
        {
            double scaleX = physicalWidth / width;
            double scaleY = physicalHeight / height;
            return (scaleX, scaleY, 0d + -x * scaleX + -y * 0d, 0d + -x * 0d + -y * scaleY);
        }
        double logicalRatio = width / height;
        double physicalRatio = physicalWidth / physicalHeight;
        bool useHeight = logicalRatio < physicalRatio && viewBox.Fit == SvgViewBoxFit.Meet
            || logicalRatio >= physicalRatio && viewBox.Fit == SvgViewBoxFit.Slice;
        double scale = useHeight ? physicalHeight / height : physicalWidth / width;
        double translateX = -x;
        double translateY = -y;
        if (useHeight) translateX -= MapAlignmentOffset(width - physicalWidth * height / physicalHeight, viewBox.HorizontalAlignment);
        else translateY -= MapAlignmentOffset(height - physicalHeight * width / physicalWidth, viewBox.VerticalAlignment);
        return (scale, scale, 0d + translateX * scale + translateY * 0d, 0d + translateX * 0d + translateY * scale);
    }

    private static double MapAlignmentOffset(double remaining, SvgViewBoxAlignment alignment) => alignment switch
    {
        SvgViewBoxAlignment.Min or SvgViewBoxAlignment.None => 0,
        SvgViewBoxAlignment.Mid => remaining / 2,
        SvgViewBoxAlignment.Max => remaining,
        _ => throw new InvalidOperationException("Unknown viewBox alignment.")
    };

    internal static SvgViewBox? ParseViewBox(string? value, string? preserveAspectRatio, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (value is not null && value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "viewBox values exceed the configured syntax limit.");
        if (preserveAspectRatio is not null && preserveAspectRatio.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "preserveAspectRatio values exceed the configured syntax limit.");
        if (value is null)
        {
            if (preserveAspectRatio is not null) ParsePreserveAspectRatio(preserveAspectRatio);
            return null;
        }
        Span<double> values = stackalloc double[4];
        int valueCount = 0;
        DelimitedTokenReader tokens = new(value);
        while (tokens.TryReadValue(out ReadOnlySpan<char> token))
        {
            if (valueCount == values.Length || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double validated) || !double.IsFinite(validated)) throw new SvgException(SvgErrorCode.MalformedInput, "viewBox must contain four finite numbers with positive width and height.");
            float native = SvgFloatNumber.ReadValidated(token, workCounter, cancellationToken);
            if (!float.IsFinite(native) || valueCount >= 2 && native == 0 && validated > 0) throw new SvgException(SvgErrorCode.LimitExceeded, "viewBox values exceed the admitted float range.");
            values[valueCount++] = native;
        }
        if (valueCount != 4 || !double.IsFinite(values[0]) || !double.IsFinite(values[1]) || !double.IsFinite(values[2]) || !double.IsFinite(values[3]) || values[2] <= 0 || values[3] <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "viewBox must contain four finite numbers with positive width and height.");
        (SvgViewBoxAlignment horizontalAlignment, SvgViewBoxAlignment verticalAlignment, SvgViewBoxFit fit) = ParsePreserveAspectRatio(preserveAspectRatio);
        return new SvgViewBox(values[0], values[1], values[2], values[3], horizontalAlignment, verticalAlignment, fit);
    }

    private static SvgAffineTransform ParseTransformFunction(ReadOnlySpan<char> name, ReadOnlySpan<char> arguments, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        bool isMatrix = name.Equals("matrix", StringComparison.Ordinal);
        bool isTranslate = name.Equals("translate", StringComparison.Ordinal);
        bool isRotate = name.Equals("rotate", StringComparison.Ordinal);
        bool isSkewX = name.Equals("skewX", StringComparison.Ordinal);
        bool isSkewY = name.Equals("skewY", StringComparison.Ordinal);
        if (!isMatrix && !isTranslate && !name.Equals("scale", StringComparison.Ordinal) && !isRotate && !isSkewX && !isSkewY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The transform function is not supported.");
        if (isTranslate) return ParseTranslate(arguments, workCounter, cancellationToken);
        if (isRotate) return ParseRotate(arguments, workCounter, cancellationToken);
        if (isSkewX || isSkewY) return ParseSkew(arguments, isSkewY, workCounter, cancellationToken);
        Span<double> values = stackalloc double[6];
        int valueCount = 0;
        DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, isMatrix ? "matrix() requires six coordinates." : "Transforms require one or two coordinates.");
            values[valueCount++] = ParseTransformCoordinate(value, workCounter, cancellationToken);
        }
        if (isMatrix)
        {
            if (valueCount != 6) throw new SvgException(SvgErrorCode.MalformedInput, "matrix() requires six coordinates.");
            double scaleX = values[0]; double skewY = values[1]; double skewX = values[2]; double scaleY = values[3]; double offsetX = values[4]; double offsetY = values[5];
            if (scaleX * scaleY - skewY * skewX == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonsingular finite matrix() transforms are implemented.");
            if (skewX == 0 && skewY == 0)
            {
                if (scaleX == 0 || scaleY == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Matrix scale factors must be nonzero.");
                return SvgAffineTransform.FromScaleOffset(scaleX, scaleY, offsetX, offsetY);
            }
            return SvgAffineTransform.FromMatrix(scaleX, skewY, skewX, scaleY, offsetX, offsetY);
        }
        if (valueCount is < 1 or > 2) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
        double first = values[0]; double second = valueCount == 2 ? values[1] : first;
        if (first == 0 || second == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero finite scale() values are implemented.");
        return SvgAffineTransform.FromScaleOffset(first, second, 0, 0);
    }

    private static SvgAffineTransform ParseRotate(ReadOnlySpan<char> arguments, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        Span<double> values = stackalloc double[3];
        int valueCount = 0;
        DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, "rotate() requires one angle or one angle with a center point.");
            values[valueCount++] = ParseTransformCoordinate(value, workCounter, cancellationToken);
        }
        if (valueCount != 1 && valueCount != 3) throw new SvgException(SvgErrorCode.MalformedInput, "rotate() requires one angle or one angle with a center point.");
        return valueCount == 1 ? SvgAffineTransform.FromRotationDegrees(values[0]) : SvgAffineTransform.FromRotationDegrees(values[0], values[1], values[2]);
    }

    private static SvgAffineTransform ParseSkew(ReadOnlySpan<char> arguments, bool skewY, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        Span<double> values = stackalloc double[1];
        int valueCount = 0;
        DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Skew transforms require one angle.");
            values[valueCount++] = ParseTransformCoordinate(value, workCounter, cancellationToken);
        }
        if (valueCount != 1) throw new SvgException(SvgErrorCode.MalformedInput, "Skew transforms require one angle.");
        double slope = Math.Tan(values[0] * (Math.PI / 180d));
        if (!double.IsFinite(slope)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Skew transform slopes must be finite.");
        return skewY ? SvgAffineTransform.FromMatrix(1, slope, 0, 1, 0, 0) : SvgAffineTransform.FromMatrix(1, 0, slope, 1, 0, 0);
    }

    private static SvgAffineTransform ParseTranslate(ReadOnlySpan<char> arguments, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        Span<double> values = stackalloc double[2];
        int valueCount = 0;
        DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
            values[valueCount++] = ParseTransformCoordinate(value, workCounter, cancellationToken);
        }
        if (valueCount is < 1 or > 2) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
        return SvgAffineTransform.FromScaleOffset(1, 1, values[0], valueCount == 2 ? values[1] : 0);
    }

    private static double ParseTransformCoordinate(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Transform coordinate values exceed the configured syntax limit.");
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px transform coordinates are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only finite transform coordinates are implemented.");
        return SvgFloatNumber.ReadValidated(token, workCounter, cancellationToken);
    }

    internal static (SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit) ParsePreserveAspectRatio(string? value)
    {
        if (value is null || value.Equals("xMidYMid meet", StringComparison.Ordinal)) return (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet);
        value = value.Trim();
        int separator = value.IndexOfAny([' ', '\t', '\r', '\n']);
        string alignment = separator < 0 ? value : value[..separator];
        SvgViewBoxFit fit = separator < 0 ? SvgViewBoxFit.Meet : value[(separator + 1)..].Trim() switch
        {
            "meet" => SvgViewBoxFit.Meet,
            "slice" => SvgViewBoxFit.Slice,
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.")
        };
        if (alignment.Equals("none", StringComparison.Ordinal)) return (SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        return alignment switch
        {
            "xMinYMin" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Min, fit), "xMidYMin" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Min, fit), "xMaxYMin" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Min, fit), "xMinYMid" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Mid, fit), "xMidYMid" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, fit), "xMaxYMid" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Mid, fit), "xMinYMax" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Max, fit), "xMidYMax" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Max, fit), "xMaxYMax" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Max, fit),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.")
        };
    }

    internal ref struct DelimitedTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal DelimitedTokenReader(ReadOnlySpan<char> value) => _value = value;
        internal int Consumed => _position;

        internal bool TryReadValue(out ReadOnlySpan<char> value)
        {
            while (_position < _value.Length && IsSeparator(_value[_position])) _position++;
            if (_position == _value.Length) { value = default; return false; }
            int start = _position;
            while (_position < _value.Length && !IsSeparator(_value[_position])) _position++;
            value = _value[start.._position];
            return true;
        }

        private static bool IsSeparator(char value) => value is ',' or ' ' or '\t' or '\r' or '\n';
    }

    internal static double ParseViewportCoordinate(string value, double? basis, SvgViewportCoordinateKind kind)
    {
        string noun = kind == SvgViewportCoordinateKind.Oval ? "oval" : "rectangle";
        string label = kind == SvgViewportCoordinateKind.Oval ? "Oval" : "Rectangle";
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (!token.EndsWith("%", StringComparison.Ordinal)) return ParseRectangleCoordinate(value);
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, $"{label} coordinate values exceed the configured syntax limit.");
        if (basis is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, $"Percentage {noun} coordinates require a known user-space viewport.");
        if (!double.TryParse(token[..^1].TrimEnd(), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) || !double.IsFinite(percent))
            throw new SvgException(SvgErrorCode.MalformedInput, $"Percentage {noun} coordinates must be finite numbers.");
        // Native SVG lengths narrow the percentage and viewport before resolving in user space.
        float resolved = (float)percent * (float)basis.Value / 100f;
        if (!float.IsFinite(resolved) || resolved < (double)int.MinValue || resolved > (double)int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, $"Percentage {noun} coordinates exceed the admitted numeric range.");
        return resolved;
    }
}
