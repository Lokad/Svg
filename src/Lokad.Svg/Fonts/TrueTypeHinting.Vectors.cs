namespace Lokad.Svg.Fonts;

/// <remarks>Projection measures distances; freedom determines movement. Preserve their
/// separate fixed-point rounding, including the dual projection of original points.
/// Normalization and movement adapt FreeType 2.13.2 ttinterp.c/ftcalc.c. All state is
/// inline and glyph-local; normalization iterations consume the existing work budget.</remarks>
internal sealed partial class TrueTypeHinting
{
    private HintVector _projection = HintVector.XAxis, _dualProjection = HintVector.XAxis, _freedom = HintVector.XAxis;

    private bool BackwardCompatible => _target.Grid == TrueTypeHintGrid.ClearType && (_instructionControl & 4) == 0;

    private readonly record struct HintVector(int X, int Y)
    {
        internal static HintVector XAxis => new(16384, 0);
        internal static HintVector YAxis => new(0, 16384);

    }

    private int Project(HintVector vector, int x, int y)
    {
        if (_target.UsesWindowsArithmetic)
        {
            // The Windows projector rounds each signed 2.14 product before the
            // sum; combining them first changes diagonal relative distances.
            long horizontal = ((long)x * vector.X + 8192) >> 14;
            long vertical = ((long)y * vector.Y + 8192) >> 14;
            return checked((int)(horizontal + vertical));
        }
        long dot = (long)x * vector.X + (long)y * vector.Y;
        return checked((int)((dot + 8192 + (dot >> 63)) >> 14));
    }

    private double ProjectionRatio => _projection.Y == 0 ? _target.HorizontalRatio : _projection.X == 0 ? 1
        : Length(Project(_projection, ScaleHorizontal(65536), 0), Project(_projection, 0, 65536)) / 65536d;

    private int ProjectionScale => _projection.Y == 0 ? _scale.Horizontal : _projection.X == 0 ? _scale.Vertical
        : Length(Project(_projection, _scale.Horizontal, 0), Project(_projection, 0, _scale.Vertical));

    private static int Length(int x, int y) => checked((int)Math.Round(Math.Sqrt((double)x * x + (double)y * y), MidpointRounding.AwayFromZero));

    private int Coordinate(TrueTypeHintPoint point, bool original) => original
        ? Project(_dualProjection, point.OriginalX, point.OriginalY) : Project(_projection, point.X, point.Y);

    private int ProjectedDistance(TrueTypeHintPoint point, TrueTypeHintPoint reference, bool original) => original
        ? Project(_dualProjection, checked(point.OriginalX - reference.OriginalX), checked(point.OriginalY - reference.OriginalY))
        : Project(_projection, checked(point.X - reference.X), checked(point.Y - reference.Y));

    private int DesignDistance(TrueTypeHintPoint point, TrueTypeHintPoint reference, bool scaled)
    {
        int x = checked(point.UnscaledX - reference.UnscaledX), y = checked(point.UnscaledY - reference.UnscaledY);
        if (_scale.Horizontal == _scale.Vertical)
        {
            int distance = Project(_dualProjection, x, y);
            return scaled ? ScaleOriginal(distance, _scale.Vertical) : distance;
        }
        return Project(_dualProjection, ScaleOriginal(x, _scale.Horizontal), ScaleOriginal(y, _scale.Vertical));
    }

    private int ScaleOriginal(int distance, int scale)
    {
        // DirectWrite rounds a negative exact half toward positive infinity when
        // scaling a design-space distance. Other targets retain FreeType's signed
        // magnitude rounding; endpoint subtraction must still precede scaling.
        return _target.UsesWindowsArithmetic
            ? checked((int)(((long)distance * scale + 32768) >> 16)) : Divide(distance, 65536, scale);
    }

    private void Move(ref TrueTypeHintPoint point, int distance, bool touch)
    {
        int dot = checked((int)(((long)_projection.X * _freedom.X + (long)_projection.Y * _freedom.Y) >> 14));
        // Match the native protection against nearly perpendicular movement spikes.
        if (Math.Abs((long)dot) < 1024) dot = 16384;
        Translate(ref point, DivideDevice(distance, dot, _freedom.X), DivideDevice(distance, dot, _freedom.Y), touch);
    }

    private void MoveAlongFreedom(ref TrueTypeHintPoint point, int distance)
        => Translate(ref point, DivideDevice(distance, 16384, _freedom.X), DivideDevice(distance, 16384, _freedom.Y), true);

    private int DivideDevice(int value, int denominator, int multiplier)
    {
        if (!_target.UsesWindowsArithmetic) return Divide(value, denominator, multiplier);
        if (denominator == 0) throw Invalid("TrueType arithmetic divides by zero.");
        long product = (long)value * multiplier, magnitude = Math.Abs(product), divisor = Math.Abs((long)denominator);
        long result = (magnitude + divisor / 2) / divisor;
        if ((product < 0) != (denominator < 0))
        {
            if (2 * (magnitude % divisor) == divisor) result--;
            result = -result;
        }
        return checked((int)result);
    }

    private void Translate(ref TrueTypeHintPoint point, int x, int y, bool touch)
    {
        // Subpixel-positioned ClearType retains the linear right side bearing.
        // Compare the borrowed slot itself; a twilight point with the same index
        // is independent and must remain movable.
        if (_target.Grid == TrueTypeHintGrid.ClearType && (_target.QueryFlags & TrueTypeHintQueryFlags.SubpixelPositioned) != 0 &&
            _glyph.Length >= 4 && System.Runtime.CompilerServices.Unsafe.AreSame(ref point, ref _glyph[^3])) return;
        point.X = checked(point.X + x); point.Y = checked(point.Y + y);
        if (touch) { point.TouchedX |= _freedom.X != 0; point.TouchedY |= _freedom.Y != 0; }
    }

    private void SetLineVector(byte opcode)
    {
        TrueTypeHintPoint second = Point(_zp2, Pop()), first = Point(_zp1, Pop());
        int x = checked(first.X - second.X), y = checked(first.Y - second.Y);
        bool perpendicular = (opcode & 1) != 0;
        if (opcode >= 0x86)
        {
            int originalX = checked(first.OriginalX - second.OriginalX), originalY = checked(first.OriginalY - second.OriginalY);
            // Native SDPVTL clears the perpendicular flag for both vectors when
            // the original points coincide, even if hinting separated them.
            if (originalX == 0 && originalY == 0) perpendicular = false;
            _dualProjection = FromLine(originalX, originalY, perpendicular);
            _projection = FromLine(x, y, perpendicular);
        }
        else
        {
            HintVector vector = FromLine(x, y, perpendicular);
            if (opcode < 8) _projection = _dualProjection = vector;
            else _freedom = vector;
        }

        HintVector FromLine(int dx, int dy, bool rotate)
        {
            if (dx == 0 && dy == 0) return HintVector.XAxis;
            if (rotate) (dx, dy) = (checked(-dy), dx);
            return NormalizeVector(dx, dy);
        }
    }

    private HintVector NormalizeVector(int vx, int vy)
    {
        if (vx == 0) return new(0, vy < 0 ? -16384 : 16384);
        if (vy == 0) return new(vx < 0 ? -16384 : 16384, 0);
        if (_target.UsesWindowsArithmetic)
        {
            _work.Consume(_token);
            double magnitude = Math.Sqrt((double)vx * vx + (double)vy * vy);
            return new(checked((int)Math.Round(vx * 16384d / magnitude, MidpointRounding.AwayFromZero)),
                checked((int)Math.Round(vy * 16384d / magnitude, MidpointRounding.AwayFromZero)));
        }
        uint ax = (uint)Math.Abs((long)vx), ay = (uint)Math.Abs((long)vy);
        uint length = ax > ay ? ax + (ay >> 1) : ay + (ax >> 1);
        int shift = System.Numerics.BitOperations.LeadingZeroCount(length);
        shift -= 15 + (length >= (0xAAAAAAAAu >> shift) ? 1 : 0);
        if (shift > 0) { ax <<= shift; ay <<= shift; length = ax > ay ? ax + (ay >> 1) : ay + (ax >> 1); }
        else { ax >>= -shift; ay >>= -shift; length >>= -shift; }
        int reciprocal = 65536 - (int)length, correction;
        uint ux, uy;
        do
        {
            _work.Consume(_token);
            // These modular products are the native 32-bit Newton iteration.
            unchecked
            {
                ux = (uint)((int)ax + ((int)ax * reciprocal >> 16));
                uy = (uint)((int)ay + ((int)ay * reciprocal >> 16));
                correction = -(int)(ux * ux + uy * uy) / 512;
                correction = correction * ((65536 + reciprocal) >> 8) / 65536;
                reciprocal += correction;
            }
        } while (correction > 0);
        return new((vx < 0 ? -(int)ux : (int)ux) / 4, (vy < 0 ? -(int)uy : (int)uy) / 4);
    }
}
