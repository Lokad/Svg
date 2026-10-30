// Native CSS coordinate conversion adapts Chromium 153.0.8010.52.
// Copyright 2014 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
using System.Globalization;

namespace Lokad.Svg.Parsing;

internal enum SvgGradientLengthUnit : byte { Unspecified, Number, Percentage }

/// <summary>Retains an authored gradient length until inherited units and the definition viewport are known.</summary>
internal readonly record struct SvgGradientLength(double Value, SvgGradientLengthUnit Unit)
{
    internal bool IsSpecified => Unit != SvgGradientLengthUnit.Unspecified;
    internal SvgGradientLength Inherit(SvgGradientLength parent) => IsSpecified ? this : parent;

    internal double Resolve(bool userSpace, double? basis)
    {
        if (!IsSpecified) throw new InvalidOperationException("Gradient defaults must be applied before length resolution.");
        if (Unit == SvgGradientLengthUnit.Number) return Value;
        if (!userSpace) return (float)Value / 100f;
        if (basis is not {} extent) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Percentage gradient lengths require a known definition viewport.");
        float result = (float)extent * (float)Value / 100f;
        if (!float.IsFinite(result)) throw new SvgException(SvgErrorCode.LimitExceeded, "Percentage gradient lengths exceed the admitted numeric range.");
        return result;
    }

    internal static SvgGradientLength Parse(string? value)
    {
        if (value is null) return default;
        ReadOnlySpan<char> token = value.AsSpan().Trim(SvgCssWhitespace.Characters);
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient coordinate values exceed the configured syntax limit.");
        bool percentage = token.EndsWith("%");
        if (percentage) token = token[..^1];
        if (!double.TryParse(token, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient coordinates must be finite bounded numbers.");
        // The native CSS length fast path retains at most seven fractional digits.
        int decimalAt = token.IndexOf('.');
        if (token[0] != '+' && token.IndexOfAny('e', 'E') < 0 && decimalAt >= 0 && token.Length - decimalAt - 1 >= 7)
        {
            double whole = decimalAt == 0 || decimalAt == 1 && token[0] == '-' ? 0 : Math.Abs(double.Parse(token[..decimalAt], SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture));
            int fraction = 0;
            for (int digit = decimalAt + 1; digit <= decimalAt + 7; digit++) fraction = fraction * 10 + token[digit] - '0';
            result = whole + fraction * Math.BitIncrement(1e-7);
            if (token[0] == '-') result = -result;
        }
        return new(result, percentage ? SvgGradientLengthUnit.Percentage : SvgGradientLengthUnit.Number);
    }
}

/// <summary>Keeps each linear coordinate's presence and units independently through template inheritance.</summary>
internal readonly record struct SvgLinearGradientCoordinates(SvgGradientLength X1, SvgGradientLength Y1, SvgGradientLength X2, SvgGradientLength Y2)
{
    internal bool IsComplete => X1.IsSpecified && Y1.IsSpecified && X2.IsSpecified && Y2.IsSpecified;

    internal SvgLinearGradientCoordinates Inherit(SvgLinearGradientCoordinates parent)
        => new(X1.Inherit(parent.X1), Y1.Inherit(parent.Y1), X2.Inherit(parent.X2), Y2.Inherit(parent.Y2));

    internal SvgLinearGradientCoordinates WithDefaults()
        => new(X1.Inherit(new(0, SvgGradientLengthUnit.Percentage)), Y1.Inherit(new(0, SvgGradientLengthUnit.Percentage)),
            X2.Inherit(new(100, SvgGradientLengthUnit.Percentage)), Y2.Inherit(new(0, SvgGradientLengthUnit.Percentage)));

    internal (double X1, double Y1, double X2, double Y2) Resolve(bool userSpace, SvgViewportLengthBases bases)
    {
        SvgLinearGradientCoordinates coordinates = WithDefaults();
        return (coordinates.X1.Resolve(userSpace, bases.Width), coordinates.Y1.Resolve(userSpace, bases.Height),
            coordinates.X2.Resolve(userSpace, bases.Width), coordinates.Y2.Resolve(userSpace, bases.Height));
    }
}

/// <summary>Keeps radial lengths unplaced; absent focal coordinates follow the effective inherited center.</summary>
internal readonly record struct SvgRadialGradientCoordinates(SvgGradientLength CenterX, SvgGradientLength CenterY, SvgGradientLength Radius,
    SvgGradientLength FocalX, SvgGradientLength FocalY, SvgGradientLength FocalRadius)
{
    internal bool IsComplete => CenterX.IsSpecified && CenterY.IsSpecified && Radius.IsSpecified;

    internal SvgRadialGradientCoordinates Inherit(SvgRadialGradientCoordinates parent)
        => new(CenterX.Inherit(parent.CenterX), CenterY.Inherit(parent.CenterY), Radius.Inherit(parent.Radius),
            FocalX.Inherit(parent.FocalX), FocalY.Inherit(parent.FocalY), FocalRadius.Inherit(parent.FocalRadius));

    internal SvgRadialGradientCoordinates WithDefaults()
        => this with { CenterX = CenterX.Inherit(new(50, SvgGradientLengthUnit.Percentage)),
            CenterY = CenterY.Inherit(new(50, SvgGradientLengthUnit.Percentage)), Radius = Radius.Inherit(new(50, SvgGradientLengthUnit.Percentage)) };

    internal (double CenterX, double CenterY, double Radius, double FocalX, double FocalY, double FocalRadius) Resolve(bool userSpace, SvgViewportLengthBases bases)
    {
        SvgRadialGradientCoordinates coordinates = WithDefaults();
        double x = coordinates.CenterX.Resolve(userSpace, bases.Width);
        double y = coordinates.CenterY.Resolve(userSpace, bases.Height);
        return (x, y, coordinates.Radius.Resolve(userSpace, bases.NormalizedDiagonal),
            FocalX.IsSpecified ? FocalX.Resolve(userSpace, bases.Width) : x,
            FocalY.IsSpecified ? FocalY.Resolve(userSpace, bases.Height) : y,
            FocalRadius.IsSpecified ? FocalRadius.Resolve(userSpace, bases.NormalizedDiagonal) : 0);
    }
}
