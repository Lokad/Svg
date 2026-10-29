namespace Lokad.Svg.Parsing;

/// <summary>Borrows one span's validated tokens over its normalized UTF-16 interval.</summary>
internal readonly record struct SvgTextPositionSpan(int Start, int End, SvgTextPositionLists Lists, SvgTextPositionOffset? Scalar);

/// <summary>Advances one ancestor's lists once per normalized scalar, independently of descendant overrides.</summary>
/// <remarks>Parser-thread confined, allocation-free, and borrowed from the owning layout buffer.
/// The caller checks cancellation per step; no cursor owns storage.</remarks>
internal struct SvgTextPositionSpanCursor
{
    private readonly SvgTextPositionSpan _span;
    private int _x, _y, _dx, _dy, _rotate;
    private bool _applied;
    private double? _lastRotation;

    internal SvgTextPositionSpanCursor(SvgTextPositionSpan span) { _span = span; }
    internal readonly int End => _span.End;

    internal SvgTextPositionValue Next()
    {
        if (_span.Scalar is {} scalar)
        {
            if (_applied) return default;
            _applied = true;
            return new(scalar.X, scalar.Y, scalar.Dx, scalar.Dy, null, false);
        }
        double? x = Read(_span.Lists.X, ref _x, true), y = Read(_span.Lists.Y, ref _y, true);
        double? dx = Read(_span.Lists.Dx, ref _dx, true), dy = Read(_span.Lists.Dy, ref _dy, true);
        if (Read(_span.Lists.Rotate, ref _rotate, false) is double angle) _lastRotation = angle;
        return new(x, y, dx, dy, _lastRotation, true);

        static double? Read(string? source, ref int offset, bool pixels)
        {
            SvgGeometrySyntax.DelimitedTokenReader reader = new(source.AsSpan(), SvgNumberListSyntax.Svg, offset);
            bool found = reader.TryReadValue(out ReadOnlySpan<char> value);
            offset = reader.Consumed;
            return found ? SvgTextPositionLists.ReadToken(value, pixels) : null;
        }
    }
}

/// <summary>Distinguishes a missing value from an explicitly specified zero before ancestor merging.</summary>
internal readonly record struct SvgTextPositionValue(double? X, double? Y, double? Dx, double? Dy, double? Rotation, bool ExplicitRelativeAxes);
