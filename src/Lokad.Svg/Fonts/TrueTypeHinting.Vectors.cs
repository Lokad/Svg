namespace Lokad.Svg.Fonts;

/// <remarks>Projection measures distances; freedom determines movement. Preserve their
/// separate fixed-point rounding, including the dual projection of original points.
/// Normalization and movement adapt FreeType 2.13.2 ttinterp.c/ftcalc.c. All state is
/// inline and glyph-local; normalization iterations consume the existing work budget.</remarks>
internal sealed partial class TrueTypeHinting
{
    private HintVector _projection = HintVector.XAxis, _dualProjection = HintVector.XAxis, _freedom = HintVector.XAxis;

    private readonly record struct HintVector(int X, int Y)
    {
        internal static HintVector XAxis => new(16384, 0);
        internal static HintVector YAxis => new(0, 16384);

        internal int Project(int x, int y)
        {
            long dot = (long)x * X + (long)y * Y;
            return checked((int)((dot + 8192 + (dot >> 63)) >> 14));
        }
    }

    private double ProjectionRatio => _projection.Y == 0 ? _target.HorizontalRatio : _projection.X == 0 ? 1
        : Length(_projection.Project(ScaleHorizontal(65536), 0), _projection.Project(0, 65536)) / 65536d;

    private int ProjectionScale => _projection.Y == 0 ? _scale.Horizontal : _projection.X == 0 ? _scale.Vertical
        : Length(_projection.Project(_scale.Horizontal, 0), _projection.Project(0, _scale.Vertical));

    private static int Length(int x, int y) => checked((int)Math.Round(Math.Sqrt((double)x * x + (double)y * y), MidpointRounding.AwayFromZero));

    private int Coordinate(TrueTypeHintPoint point, bool original) => original
        ? _dualProjection.Project(point.OriginalX, point.OriginalY) : _projection.Project(point.X, point.Y);

    private int ProjectedDistance(TrueTypeHintPoint point, TrueTypeHintPoint reference, bool original) => original
        ? _dualProjection.Project(checked(point.OriginalX - reference.OriginalX), checked(point.OriginalY - reference.OriginalY))
        : _projection.Project(checked(point.X - reference.X), checked(point.Y - reference.Y));

    private int DesignDistance(TrueTypeHintPoint point, TrueTypeHintPoint reference, bool scaled)
    {
        int x = checked(point.UnscaledX - reference.UnscaledX), y = checked(point.UnscaledY - reference.UnscaledY);
        if (_scale.Horizontal == _scale.Vertical)
        {
            int distance = _dualProjection.Project(x, y);
            return scaled ? ScaleOriginal(distance, _scale.Vertical) : distance;
        }
        return _dualProjection.Project(ScaleOriginal(x, _scale.Horizontal), ScaleOriginal(y, _scale.Vertical));
    }

    private int ScaleOriginal(int distance, int scale)
    {
        // DirectWrite rounds a negative exact half toward positive infinity when
        // scaling a design-space distance. Other targets retain FreeType's signed
        // magnitude rounding; endpoint subtraction must still precede scaling.
        return (_target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) != 0
            ? checked((int)(((long)distance * scale + 32768) >> 16)) : Divide(distance, 65536, scale);
    }

    private void Move(ref TrueTypeHintPoint point, int distance, bool touch)
    {
        int dot = checked((int)(((long)_projection.X * _freedom.X + (long)_projection.Y * _freedom.Y) >> 14));
        // Match the native protection against nearly perpendicular movement spikes.
        if (Math.Abs((long)dot) < 1024) dot = 16384;
        Translate(ref point, Divide(distance, dot, _freedom.X), Divide(distance, dot, _freedom.Y), touch);
    }

    private void MoveAlongFreedom(ref TrueTypeHintPoint point, int distance)
        => Translate(ref point, Divide(distance, 16384, _freedom.X), Divide(distance, 16384, _freedom.Y), true);

    private void Translate(ref TrueTypeHintPoint point, int x, int y, bool touch)
    {
        point.X = checked(point.X + x); point.Y = checked(point.Y + y);
        if (touch) { point.TouchedX |= _freedom.X != 0; point.TouchedY |= _freedom.Y != 0; }
    }

    private void SetLineVector(byte opcode)
    {
        TrueTypeHintPoint second = Point(_zp2, Pop()), first = Point(_zp1, Pop());
        int x = checked(first.X - second.X), y = checked(first.Y - second.Y);
        HintVector vector;
        if (x == 0 && y == 0) vector = HintVector.XAxis;
        else
        {
            if ((opcode & 1) != 0) (x, y) = (checked(-y), x);
            vector = Normalize(x, y);
        }
        if (opcode < 8) _projection = _dualProjection = vector;
        else _freedom = vector;

        HintVector Normalize(int vx, int vy)
        {
            if (vx == 0) return new(0, vy < 0 ? -16384 : 16384);
            if (vy == 0) return new(vx < 0 ? -16384 : 16384, 0);
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
}
