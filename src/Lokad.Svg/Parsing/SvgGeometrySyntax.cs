using System.Globalization;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded numeric, transform, viewport, and viewBox syntax shared by supported geometry.</summary>
internal static class SvgGeometrySyntax
{
    internal static AxisTransform ParseTransform(string? value)
    {
        if (value is null) return AxisTransform.Identity;
        if (value.Length > SvgSyntaxLimits.MaximumTransformCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Transform values exceed the configured syntax limit.");
        ReadOnlySpan<char> input = value.AsSpan();
        AxisTransform transform = AxisTransform.Identity;
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
            if (nameStart == position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), nonzero scale(), and nonzero axis-aligned matrix() transforms are implemented.");
            ReadOnlySpan<char> name = input[nameStart..position];
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            if (position == input.Length || input[position] != '(') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), nonzero scale(), and nonzero axis-aligned matrix() transforms are implemented.");
            position++;
            int argumentsStart = position;
            while (position < input.Length && input[position] != ')') position++;
            if (position == input.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), nonzero scale(), and nonzero axis-aligned matrix() transforms are implemented.");
            transform = transform.Compose(ParseTransformFunction(name, input[argumentsStart..position]));
            position++;
            hasFunction = true;
        }
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
            double resolved = percent * baseValue.Value / 100;
            if (!double.IsFinite(resolved)) throw new SvgException(SvgErrorCode.LimitExceeded, "Percentage viewport dimensions exceed the admitted numeric range.");
            return resolved;
        }
        return ParseRectangleCoordinate(value);
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
        else if (token.IndexOfAny("%emivc".AsSpan()) >= 0 || (token.Length > 0 && char.IsLetter(token[^1]))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number, px, in, cm, mm, pt, and pc root dimensions are implemented.");
        if (hasSupportedUnit && token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number, px, in, cm, mm, pt, and pc root dimensions are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result <= 0 || !double.IsFinite(result *= multiplier)) throw new SvgException(SvgErrorCode.MalformedInput, "Root dimensions must be finite positive CSS pixel lengths.");
        return result;
    }

    /// <summary>Maps viewBox units into a viewport box under its alignment policy.</summary>
    internal static (double ScaleX, double ScaleY, double OffsetX, double OffsetY) MapViewBox(SvgViewBox viewBox, double viewportWidth, double viewportHeight)
    {
        double scaleX = viewportWidth / viewBox.Width;
        double scaleY = viewportHeight / viewBox.Height;
        if (viewBox.HorizontalAlignment != SvgViewBoxAlignment.None)
        {
            double scale = viewBox.Fit == SvgViewBoxFit.Slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            scaleX = scale;
            scaleY = scale;
        }
        double offsetX = MapAlignmentOffset(viewportWidth - viewBox.Width * scaleX, viewBox.HorizontalAlignment) - viewBox.X * scaleX;
        double offsetY = MapAlignmentOffset(viewportHeight - viewBox.Height * scaleY, viewBox.VerticalAlignment) - viewBox.Y * scaleY;
        return (scaleX, scaleY, offsetX, offsetY);
    }

    private static double MapAlignmentOffset(double remaining, SvgViewBoxAlignment alignment) => alignment switch
    {
        SvgViewBoxAlignment.Min or SvgViewBoxAlignment.None => 0,
        SvgViewBoxAlignment.Mid => remaining / 2,
        SvgViewBoxAlignment.Max => remaining,
        _ => throw new InvalidOperationException("Unknown viewBox alignment.")
    };

    internal static SvgViewBox? ParseViewBox(string? value, string? preserveAspectRatio)
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
            if (valueCount == values.Length || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out values[valueCount])) throw new SvgException(SvgErrorCode.MalformedInput, "viewBox must contain four finite numbers with positive width and height.");
            valueCount++;
        }
        if (valueCount != 4 || !double.IsFinite(values[0]) || !double.IsFinite(values[1]) || !double.IsFinite(values[2]) || !double.IsFinite(values[3]) || values[2] <= 0 || values[3] <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "viewBox must contain four finite numbers with positive width and height.");
        (SvgViewBoxAlignment horizontalAlignment, SvgViewBoxAlignment verticalAlignment, SvgViewBoxFit fit) = ParsePreserveAspectRatio(preserveAspectRatio);
        return new SvgViewBox(values[0], values[1], values[2], values[3], horizontalAlignment, verticalAlignment, fit);
    }

    private static AxisTransform ParseTransformFunction(ReadOnlySpan<char> name, ReadOnlySpan<char> arguments)
    {
        bool isMatrix = name.Equals("matrix", StringComparison.OrdinalIgnoreCase);
        bool isTranslate = name.Equals("translate", StringComparison.OrdinalIgnoreCase);
        if (!isMatrix && !isTranslate && !name.Equals("scale", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), nonzero scale(), and nonzero axis-aligned matrix() transforms are implemented.");
        Span<int> values = stackalloc int[6];
        int valueCount = 0;
        DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, isMatrix ? "matrix() requires six coordinates." : "Transforms require one or two coordinates.");
            values[valueCount++] = ParseInteger(value);
        }
        if (isMatrix)
        {
            if (valueCount != 6) throw new SvgException(SvgErrorCode.MalformedInput, "matrix() requires six coordinates.");
            int scaleX = values[0]; int skewY = values[1]; int skewX = values[2]; int scaleY = values[3]; int offsetX = values[4]; int offsetY = values[5];
            if (skewX != 0 || skewY != 0 || scaleX == 0 || scaleY == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero axis-aligned integer matrix() transforms are implemented.");
            return new AxisTransform(scaleX, scaleY, offsetX, offsetY);
        }
        if (valueCount is < 1 or > 2) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
        int first = values[0]; int second = valueCount == 2 ? values[1] : first;
        if (isTranslate) return new AxisTransform(1, 1, first, valueCount == 2 ? second : 0);
        if (first == 0 || second == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero integer scale() values are implemented.");
        return new AxisTransform(first, second, 0, 0);
    }

    internal static (SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit) ParsePreserveAspectRatio(string? value)
    {
        if (value is null || value.Equals("xMidYMid meet", StringComparison.OrdinalIgnoreCase)) return (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet);
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return (SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        if (value is not null)
        {
            int separator = value.LastIndexOf(' ');
            if (separator <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.");
            SvgViewBoxFit fit = value[(separator + 1)..] switch { "meet" => SvgViewBoxFit.Meet, "slice" => SvgViewBoxFit.Slice, _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.") };
            return value[..separator] switch
            {
                "xMinYMin" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Min, fit), "xMidYMin" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Min, fit), "xMaxYMin" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Min, fit), "xMinYMid" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Mid, fit), "xMidYMid" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, fit), "xMaxYMid" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Mid, fit), "xMinYMax" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Max, fit), "xMidYMax" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Max, fit), "xMaxYMax" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Max, fit),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.")
            };
        }
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.");
    }

    internal ref struct DelimitedTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal DelimitedTokenReader(ReadOnlySpan<char> value) => _value = value;

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
}
