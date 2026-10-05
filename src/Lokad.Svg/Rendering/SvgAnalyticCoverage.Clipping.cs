// Managed adaptation of Skia curve/line clipping and polynomial solvers.
// Copyright 2006, 2009 The Android Open Source Project;
// 2011, 2012 Google Inc.; 2023 Google LLC.
// See THIRD_PARTY_NOTICES.txt for the BSD license and source revision.
namespace Lokad.Svg.Rendering;

/// <summary>Identifies one clipped analytic verb by its control-point count.</summary>
internal enum SvgAnalyticClipKind { Line = 2, Quad = 3, Cubic = 4 }

/// <summary>Carries one native float point before fixed-point edge construction.</summary>
internal readonly record struct SvgAnalyticClipPoint(float X, float Y)
{
    internal (float X, float Y) Coordinates => (X, Y);
}

/// <summary>Preserves a clipped verb's ordered endpoints and controls.</summary>
internal readonly record struct SvgAnalyticClipPiece(SvgAnalyticClipKind Kind, SvgAnalyticClipPoint First, SvgAnalyticClipPoint Second, SvgAnalyticClipPoint Third, SvgAnalyticClipPoint Fourth);

/// <summary>Identifies the integer device clip used to construct analytic edges.</summary>
internal readonly record struct SvgAnalyticClipBounds(int Left, int Top, int Right, int Bottom);

internal ref partial struct SvgAnalyticCoverage
{
    /// <summary>Constructs monotonic clipped verbs in caller-owned scratch, preserving path winding.</summary>
    /// <remarks>No heap allocation or retained ownership occurs. The caller provides at least MaximumPieces slots and must account any subsequent edge storage before construction. Each clip replaces the previous pieces; work accumulates within the supplied limit and is charged before numerical operations, including empty/rejected pieces and root-search iterations. Cancellation preserves the supplied token. Each instance and its scratch have one caller and are single-threaded.</remarks>
    internal ref struct Clipper
    {
        // At most nine X/Y-monotonic cubics, each emitting two projection lines
        // and one curve. This bound covers both projections without relying on
        // Skia's tighter 18-verb / 54-point packed buffer bound.
        internal const int MaximumPieces = 27;
        private readonly SvgAnalyticClipBounds _clip;
        private readonly bool _cullRight;
        private readonly Span<SvgAnalyticClipPiece> _pieces;
        private readonly long _maximumWork;
        private readonly CancellationToken _token;
        internal int Count { get; private set; }
        internal long WorkUnits { get; private set; }

        internal Clipper(SvgAnalyticClipBounds clip, bool cullRight, Span<SvgAnalyticClipPiece> pieces, long maximumWork, CancellationToken cancellationToken)
        {
            if (clip.Left > clip.Right || clip.Top > clip.Bottom) throw new ArgumentOutOfRangeException(nameof(clip));
            if (pieces.Length < MaximumPieces) throw new ArgumentOutOfRangeException(nameof(pieces));
            if (maximumWork < 0) throw new ArgumentOutOfRangeException(nameof(maximumWork));
            _clip = clip; _cullRight = cullRight; _pieces = pieces;
            _maximumWork = maximumWork; _token = cancellationToken;
            Count = 0; WorkUnits = 0;
        }

        private void Charge(int units)
        {
            _token.ThrowIfCancellationRequested();
            if (units > _maximumWork - WorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Analytic edge clipping exceeds the configured work limit.");
            WorkUnits += units;
        }

        private static void Validate(SvgAnalyticClipPoint point)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
        }

        private void Append(SvgAnalyticClipPiece piece)
        {
            Charge((int)piece.Kind);
            if (Count == MaximumPieces) throw new InvalidOperationException("Analytic clipping exceeded its proven piece bound.");
            _pieces[Count++] = piece;
        }

        private void Vertical(float x, float y0, float y1, bool reverse)
        {
            if (reverse) (y0, y1) = (y1, y0);
            Append(new(SvgAnalyticClipKind.Line, new(x, y0), new(x, y1), default, default));
        }

        /// <summary>Clips one line vertically and projects its outside horizontal pieces to clip boundaries.</summary>
        internal void ClipLine(SvgAnalyticClipPoint first, SvgAnalyticClipPoint second)
        {
            Count = 0; Charge(2); Validate(first); Validate(second);
            if (_clip.Left == _clip.Right || _clip.Top == _clip.Bottom) return;
            int low = first.Y < second.Y ? 0 : 1;
            int high = 1 - low;
            Span<SvgAnalyticClipPoint> input = stackalloc SvgAnalyticClipPoint[2] { first, second };
            if (input[high].Y <= _clip.Top || input[low].Y >= _clip.Bottom) return;
            Span<SvgAnalyticClipPoint> points = stackalloc SvgAnalyticClipPoint[2] { first, second };
            if (input[low].Y < _clip.Top) points[low] = new(IntersectHorizontal(ref this, first, second, _clip.Top), _clip.Top);
            if (input[high].Y > _clip.Bottom) points[high] = new(IntersectHorizontal(ref this, first, second, _clip.Bottom), _clip.Bottom);
            low = first.X < second.X ? 0 : 1; high = 1 - low;
            bool reverse = low != 0;
            if (points[high].X <= _clip.Left)
            {
                Append(new(SvgAnalyticClipKind.Line, new(_clip.Left, points[0].Y), new(_clip.Left, points[1].Y), default, default));
                return;
            }
            if (points[low].X >= _clip.Right)
            {
                if (!_cullRight) Append(new(SvgAnalyticClipKind.Line, new(_clip.Right, points[0].Y), new(_clip.Right, points[1].Y), default, default));
                return;
            }
            Span<SvgAnalyticClipPoint> result = stackalloc SvgAnalyticClipPoint[4];
            int count = 0;
            if (points[low].X < _clip.Left)
            {
                result[count++] = new(_clip.Left, points[low].Y);
                result[count++] = new(_clip.Left, IntersectVertical(ref this, points[0], points[1], _clip.Left));
            }
            else result[count++] = points[low];
            if (points[high].X > _clip.Right)
            {
                result[count++] = new(_clip.Right, IntersectVertical(ref this, points[0], points[1], _clip.Right));
                result[count++] = new(_clip.Right, points[high].Y);
            }
            else result[count++] = points[high];
            for (int index = 0; index < count - 1; index++)
            {
                int at = reverse ? count - 1 - index : index;
                Append(new(SvgAnalyticClipKind.Line, result[at], result[at + (reverse ? -1 : 1)], default, default));
            }

            static float IntersectHorizontal(ref Clipper clipper, SvgAnalyticClipPoint first, SvgAnalyticClipPoint second, float y)
            {
                clipper.Charge(4);
                if (MathF.Abs(second.Y - first.Y) <= 1f / 4096) return (float)(0.5 * ((double)first.X + second.X));
                double x = first.X + ((double)y - first.Y) * ((double)second.X - first.X) / ((double)second.Y - first.Y);
                return (float)Math.Clamp(x, Math.Min(first.X, second.X), Math.Max(first.X, second.X));
            }

            static float IntersectVertical(ref Clipper clipper, SvgAnalyticClipPoint first, SvgAnalyticClipPoint second, float x)
            {
                clipper.Charge(4);
                float y = MathF.Abs(second.X - first.X) <= 1f / 4096 ? (float)(0.5 * ((double)first.Y + second.Y))
                    : (float)(first.Y + ((double)x - first.X) * ((double)second.Y - first.Y) / ((double)second.X - first.X));
                return Math.Clamp(y, Math.Min(first.Y, second.Y), Math.Max(first.Y, second.Y));
            }
        }

        /// <summary>Splits a cubic at Y then X extrema and clips each monotonic piece, preserving native projection order.</summary>
        internal void ClipCubic(SvgAnalyticClipPoint p0, SvgAnalyticClipPoint p1, SvgAnalyticClipPoint p2, SvgAnalyticClipPoint p3)
        {
            Count = 0; Charge(4); Validate(p0); Validate(p1); Validate(p2); Validate(p3);
            if (_clip.Left == _clip.Right || _clip.Top == _clip.Bottom) return;
            float top = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
            float bottom = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
            if (bottom <= _clip.Top || top >= _clip.Bottom) return;
            float left = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
            float right = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
            const int reliableLimit = 1 << 22;
            if (left < -reliableLimit || top < -reliableLimit || right > reliableLimit || bottom > reliableLimit) { ClipLine(p0, p3); return; }
            Span<SvgAnalyticClipPoint> yPieces = stackalloc SvgAnalyticClipPoint[10];
            Span<SvgAnalyticClipPoint> xPieces = stackalloc SvgAnalyticClipPoint[10];
            int ys = ChopExtrema(ref this, p0, p1, p2, p3, false, yPieces);
            for (int y = 0; y < ys; y++)
            {
                int at = y * 3;
                int xs = ChopExtrema(ref this, yPieces[at], yPieces[at + 1], yPieces[at + 2], yPieces[at + 3], true, xPieces);
                for (int x = 0; x < xs; x++) ClipMonoCubic(ref this, xPieces.Slice(x * 3, 4));
            }

            static int ChopExtrema(ref Clipper clipper, SvgAnalyticClipPoint p0, SvgAnalyticClipPoint p1, SvgAnalyticClipPoint p2, SvgAnalyticClipPoint p3, bool xAxis, scoped Span<SvgAnalyticClipPoint> output)
            {
                clipper.Charge(16);
                Span<float> roots = stackalloc float[2];
                int count = xAxis ? CubicExtremaY(p0.X, p1.X, p2.X, p3.X, roots) : CubicExtremaY(p0.Y, p1.Y, p2.Y, p3.Y, roots);
                if (count == 0) { output[0] = p0; output[1] = p1; output[2] = p2; output[3] = p3; return 1; }
                Span<float> chopped = stackalloc float[20];
                if (count == 1) ChopCubicSingle(p0.Coordinates, p1.Coordinates, p2.Coordinates, p3.Coordinates, roots[0], chopped);
                else ChopCubicDouble(p0.Coordinates, p1.Coordinates, p2.Coordinates, p3.Coordinates, roots[0], roots[1], chopped);
                int axis = xAxis ? 0 : 1;
                chopped[4 + axis] = chopped[6 + axis]; chopped[8 + axis] = chopped[6 + axis];
                if (count == 2) { chopped[10 + axis] = chopped[12 + axis]; chopped[14 + axis] = chopped[12 + axis]; }
                for (int i = 0; i < count * 3 + 4; i++) output[i] = new(chopped[i * 2], chopped[i * 2 + 1]);
                return count + 1;
            }

            static void ClipMonoCubic(ref Clipper clipper, scoped ReadOnlySpan<SvgAnalyticClipPoint> source)
            {
                clipper.Charge(8);
                Span<SvgAnalyticClipPoint> points = stackalloc SvgAnalyticClipPoint[4];
                bool reverse = source[0].Y > source[3].Y;
                for (int i = 0; i < 4; i++) points[i] = source[reverse ? 3 - i : i];
                if (points[3].Y <= clipper._clip.Top || points[0].Y >= clipper._clip.Bottom) return;
                Span<SvgAnalyticClipPoint> chopped = stackalloc SvgAnalyticClipPoint[7];
                if (points[0].Y < clipper._clip.Top)
                {
                    ChopAtAxis(ref clipper, points, clipper._clip.Top, false, chopped);
                    if (chopped[3].Y < clipper._clip.Top && chopped[4].Y < clipper._clip.Top && chopped[5].Y < clipper._clip.Top) ChopAtAxis(ref clipper, chopped.Slice(3, 4), clipper._clip.Top, false, chopped);
                    points[0] = chopped[3] with { Y = clipper._clip.Top };
                    points[1] = chopped[4] with { Y = Math.Max(chopped[4].Y, clipper._clip.Top) };
                    points[2] = chopped[5];
                }
                if (points[3].Y > clipper._clip.Bottom)
                {
                    ChopAtAxis(ref clipper, points, clipper._clip.Bottom, false, chopped);
                    points[1] = chopped[1]; points[2] = chopped[2] with { Y = Math.Min(chopped[2].Y, clipper._clip.Bottom) };
                    points[3] = chopped[3] with { Y = clipper._clip.Bottom };
                }
                if (points[0].X > points[3].X)
                {
                    (points[0], points[3]) = (points[3], points[0]); (points[1], points[2]) = (points[2], points[1]); reverse = !reverse;
                }
                if (points[3].X <= clipper._clip.Left) { clipper.Vertical(clipper._clip.Left, points[0].Y, points[3].Y, reverse); return; }
                if (points[0].X >= clipper._clip.Right) { if (!clipper._cullRight) clipper.Vertical(clipper._clip.Right, points[0].Y, points[3].Y, reverse); return; }
                if (points[0].X < clipper._clip.Left)
                {
                    ChopAtAxis(ref clipper, points, clipper._clip.Left, true, chopped);
                    clipper.Vertical(clipper._clip.Left, chopped[0].Y, chopped[3].Y, reverse);
                    points[0] = chopped[3] with { X = clipper._clip.Left }; points[1] = chopped[4] with { X = Math.Max(chopped[4].X, clipper._clip.Left) }; points[2] = chopped[5];
                }
                if (points[3].X > clipper._clip.Right)
                {
                    ChopAtAxis(ref clipper, points, clipper._clip.Right, true, chopped);
                    chopped[3] = chopped[3] with { X = clipper._clip.Right }; chopped[2] = chopped[2] with { X = Math.Min(chopped[2].X, clipper._clip.Right) };
                    clipper.AppendCurve(chopped.Slice(0, 4), reverse);
                    clipper.Vertical(clipper._clip.Right, chopped[3].Y, chopped[6].Y, reverse);
                }
                else clipper.AppendCurve(points, reverse);

                static void ChopAtAxis(ref Clipper clipper, scoped ReadOnlySpan<SvgAnalyticClipPoint> points, float target, bool xAxis, scoped Span<SvgAnalyticClipPoint> output)
                {
                    clipper.Charge(16);
                    double a = xAxis ? points[0].X : points[0].Y;
                    double b = xAxis ? points[1].X : points[1].Y;
                    double c = xAxis ? points[2].X : points[2].Y;
                    double d = xAxis ? points[3].X : points[3].Y;
                    double A = -a + 3 * b - 3 * c + d, B = 3 * a - 6 * b + 3 * c, C = -3 * a + 3 * b, D = a - target;
                    if (FirstIntersection(ref clipper, A, B, C, D, out double t))
                    {
                        // Retain double De Casteljau arithmetic until each final point
                        // narrows to float, unlike the float extrema chop.
                        Span<double> xy = stackalloc double[14];
                        for (int axis = 0; axis < 2; axis++)
                        {
                            double p0 = axis == 0 ? points[0].X : points[0].Y, p1 = axis == 0 ? points[1].X : points[1].Y;
                            double p2 = axis == 0 ? points[2].X : points[2].Y, p3 = axis == 0 ? points[3].X : points[3].Y;
                            double ab = p0 + (p1 - p0) * t, bc = p1 + (p2 - p1) * t, cd = p2 + (p3 - p2) * t;
                            double abc = ab + (bc - ab) * t, bcd = bc + (cd - bc) * t;
                            xy[axis] = p0; xy[2 + axis] = ab; xy[4 + axis] = abc; xy[6 + axis] = abc + (bcd - abc) * t;
                            xy[8 + axis] = bcd; xy[10 + axis] = cd; xy[12 + axis] = p3;
                        }
                        for (int i = 0; i < 7; i++) output[i] = new((float)xy[i * 2], (float)xy[i * 2 + 1]);
                        return;
                    }
                    float v0 = (float)a, v1 = (float)b, v2 = (float)c, v3 = (float)d;
                    float fa = v3 + 3 * (v1 - v2) - v0, fb = 3 * (v2 - v1 - v1 + v0), fc = 3 * (v1 - v0), goal = target - v0;
                    float bestT = 0.5f, current = 0.5f, step = 0.25f, closest = float.MaxValue;
                    float previous;
                    do
                    {
                        clipper.Charge(4);
                        float location = ((fa * current + fb) * current + fc) * current, distance = MathF.Abs(location - goal);
                        if (closest > distance) { closest = distance; bestT = current; }
                        previous = current; current += location < goal ? step : -step; step *= 0.5f;
                    } while (closest > 0.25f && previous != current);
                    Span<float> chopped = stackalloc float[14];
                    ChopCubicSingle(points[0].Coordinates, points[1].Coordinates, points[2].Coordinates, points[3].Coordinates, bestT, chopped);
                    for (int i = 0; i < 7; i++) output[i] = new(chopped[i * 2], chopped[i * 2 + 1]);

                    static bool FirstIntersection(ref Clipper clipper, double a, double b, double c, double d, out double solution)
                    {
                        clipper.Charge(16);
                        Span<double> roots = stackalloc double[3];
                        int count = CubicRoots(a, b, c, d, roots);
                        Span<double> valid = stackalloc double[3];
                        int found = 0;
                        for (int i = 0; i < count; i++)
                        {
                            double t = roots[i];
                            if (t <= 1.00005 && (t >= 1 || NearEqual(t, 1)))
                            {
                                if ((found < 1 || !NearEqual(valid[0], 1)) && (found < 2 || !NearEqual(valid[1], 1))) valid[found++] = 1;
                            }
                            else if (t >= -0.00005 && (t <= 0 || NearZero(t)))
                            {
                                if ((found < 1 || !NearZero(valid[0])) && (found < 2 || !NearZero(valid[1]))) valid[found++] = 0;
                            }
                            else if (t > 0 && t < 1) valid[found++] = t;
                        }
                        for (int i = 0; i < found; i++) if (Math.Abs(Evaluate(a, b, c, d, valid[i])) < 1e-5) { solution = valid[i]; return true; }
                        if (found == 0) { solution = 0; return false; }
                        // Native fallback searches extrema-separated regions. Retain its
                        // ordering and 1000-iteration cap even though these pieces are monotonic.
                        Span<double> extrema = stackalloc double[2];
                        int rawExtrema = QuadraticRoots(3 * a, 2 * b, c, extrema), extremaCount = 0;
                        for (int i = 0; i < rawExtrema; i++) if (extrema[i] >= 0 && extrema[i] <= 1) extrema[extremaCount++] = extrema[i];
                        Span<double> regions = stackalloc double[4]; regions.Clear(); regions[3] = 1;
                        int start = 2 - extremaCount;
                        if (extremaCount == 1) regions[start + 1] = extrema[0];
                        if (extremaCount == 2) { regions[start + 1] = Math.Min(extrema[0], extrema[1]); regions[start + 2] = Math.Max(extrema[0], extrema[1]); }
                        found = 0;
                        for (; start < 3; start++)
                        {
                            long searchWork = clipper.WorkUnits;
                            double root;
                            try { root = Search(a, b, c, d, regions[start], regions[start + 1], ref searchWork, clipper._maximumWork, clipper._token); }
                            finally { clipper.WorkUnits = searchWork; }
                            if (root >= 0 && (found < 1 || Math.Abs(valid[0] - root) >= 1e-8) && (found < 2 || Math.Abs(valid[1] - root) >= 1e-8)) valid[found++] = root;
                        }
                        for (int i = 0; i < found; i++) if (Math.Abs(Evaluate(a, b, c, d, valid[i])) < 1e-5) { solution = valid[i]; return true; }
                        solution = 0; return false;

                        static double Search(double a, double b, double c, double d, double start, double stop, ref long work, long maximumWork, CancellationToken token)
                        {
                            token.ThrowIfCancellationRequested();
                            if (2 > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Analytic clip root search exceeds the configured work limit.");
                            work += 2;
                            double left = Evaluate(a, b, c, d, start);
                            if (Math.Abs(left) < 1e-8) return start;
                            double right = Evaluate(a, b, c, d, stop);
                            if (!double.IsFinite(left) || !double.IsFinite(right) || (left > 0 && right > 0) || (left < 0 && right < 0)) return -1;
                            for (int i = 0; i < 1000; i++)
                            {
                                token.ThrowIfCancellationRequested();
                                if (4 > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Analytic clip root search exceeds the configured work limit.");
                                work += 4;
                                double step = (start + stop) / 2, current = Evaluate(a, b, c, d, step);
                                if (Math.Abs(current) < 1e-8) return step;
                                if ((current < 0 && left < 0) || (current > 0 && left > 0)) start = step;
                                else stop = step;
                            }
                            return -1;
                        }

                        static int CubicRoots(double A, double B, double C, double D, Span<double> roots)
                        {
                            if (NearZero(B) ? NearZero(A) : Math.Abs(A / B) < 1e-7) return QuadraticRoots(B, C, D, roots);
                            if (NearZero(D))
                            {
                                int count = QuadraticRoots(A, B, C, roots);
                                for (int i = 0; i < count; i++) if (NearZero(roots[i])) return count;
                                roots[count++] = 0; return count;
                            }
                            if (NearZero(A + B + C + D))
                            {
                                int count = QuadraticRoots(A, A + B, -D, roots);
                                for (int i = 0; i < count; i++) if (NearEqual(roots[i], 1)) return count;
                                roots[count++] = 1; return count;
                            }
                            double inv = 1 / A, a = B * inv, b = C * inv, c = D * inv, a2 = a * a;
                            double q = (a2 - b * 3) / 9, r = (2 * a2 * a - 9 * a * b + 27 * c) / 54;
                            double r2 = r * r, q3 = q * q * q, delta = r2 - q3;
                            if (!double.IsFinite(delta)) return 0;
                            double third = a / 3;
                            int found = 0;
                            if (delta < 0)
                            {
                                double theta = Math.Acos(Math.Clamp(r / Math.Sqrt(q3), -1, 1)), factor = -2 * Math.Sqrt(q);
                                roots[found++] = factor * Math.Cos(theta / 3) - third;
                                double second = factor * Math.Cos((theta + 2 * Math.PI) / 3) - third;
                                if (!EqualRoots(roots[0], second)) roots[found++] = second;
                                double last = factor * Math.Cos((theta - 2 * Math.PI) / 3) - third;
                                if (!EqualRoots(roots[0], last) && (found == 1 || !EqualRoots(roots[1], last))) roots[found++] = last;
                            }
                            else
                            {
                                double root = Math.Cbrt(Math.Abs(r) + Math.Sqrt(delta));
                                if (r > 0) root = -root;
                                if (!NearZero(root)) root += q / root;
                                roots[found++] = root - third;
                                if (!NearZero(r2) && NearEqual(r2, q3)) { double second = -root / 2 - third; if (!EqualRoots(roots[0], second)) roots[found++] = second; }
                            }
                            return found;

                            static bool EqualRoots(double a, double b) => NearZero(a) ? NearZero(b) : NearEqual(a, b);
                        }
                    }
                }
            }
        }

        private void AppendCurve(scoped ReadOnlySpan<SvgAnalyticClipPoint> points, bool reverse)
        {
            SvgAnalyticClipKind kind = points.Length == 4 ? SvgAnalyticClipKind.Cubic : SvgAnalyticClipKind.Quad;
            if (reverse) Append(new(kind, points[^1], points[^2], points[^3], points.Length == 4 ? points[0] : default));
            else Append(new(kind, points[0], points[1], points[2], points.Length == 4 ? points[3] : default));
        }

        /// <summary>Splits a quadratic at Y then X extrema and clips the monotonic pieces.</summary>
        internal void ClipQuad(SvgAnalyticClipPoint p0, SvgAnalyticClipPoint p1, SvgAnalyticClipPoint p2)
        {
            Count = 0; Charge(3); Validate(p0); Validate(p1); Validate(p2);
            if (_clip.Left == _clip.Right || _clip.Top == _clip.Bottom) return;
            if (Math.Max(Math.Max(p0.Y, p1.Y), p2.Y) <= _clip.Top || Math.Min(Math.Min(p0.Y, p1.Y), p2.Y) >= _clip.Bottom) return;
            Span<SvgAnalyticClipPoint> yPieces = stackalloc SvgAnalyticClipPoint[5];
            Span<SvgAnalyticClipPoint> xPieces = stackalloc SvgAnalyticClipPoint[5];
            int ys = ChopQuadExtrema(ref this, p0, p1, p2, false, yPieces);
            for (int y = 0; y < ys; y++)
            {
                int at = y * 2;
                int xs = ChopQuadExtrema(ref this, yPieces[at], yPieces[at + 1], yPieces[at + 2], true, xPieces);
                for (int x = 0; x < xs; x++) ClipMonoQuad(ref this, xPieces.Slice(x * 2, 3));
            }

            static int ChopQuadExtrema(ref Clipper clipper, SvgAnalyticClipPoint p0, SvgAnalyticClipPoint p1, SvgAnalyticClipPoint p2, bool xAxis, scoped Span<SvgAnalyticClipPoint> output)
            {
                clipper.Charge(8);
                float a = xAxis ? p0.X : p0.Y, b = xAxis ? p1.X : p1.Y, c = xAxis ? p2.X : p2.Y;
                if (!QuadMonotonic(a, b, c))
                {
                    if (CubicUnitDivide(a - b, a - b - b + c, out float t))
                    {
                        ChopQuad(p0, p1, p2, t, output);
                        output[1] = xAxis ? output[1] with { X = output[2].X } : output[1] with { Y = output[2].Y };
                        output[3] = xAxis ? output[3] with { X = output[2].X } : output[3] with { Y = output[2].Y };
                        return 2;
                    }
                    b = MathF.Abs(a - b) < MathF.Abs(b - c) ? a : c;
                }
                output[0] = p0; output[1] = xAxis ? p1 with { X = b } : p1 with { Y = b }; output[2] = p2;
                return 1;
            }

            static void ClipMonoQuad(ref Clipper clipper, scoped ReadOnlySpan<SvgAnalyticClipPoint> source)
            {
                clipper.Charge(8);
                Span<SvgAnalyticClipPoint> points = stackalloc SvgAnalyticClipPoint[3];
                bool reverse = source[0].Y > source[2].Y;
                for (int i = 0; i < 3; i++) points[i] = source[reverse ? 2 - i : i];
                if (points[2].Y <= clipper._clip.Top || points[0].Y >= clipper._clip.Bottom) return;
                Span<SvgAnalyticClipPoint> chopped = stackalloc SvgAnalyticClipPoint[5];
                if (points[0].Y < clipper._clip.Top)
                {
                    clipper.Charge(8);
                    if (QuadIntersection(points, clipper._clip.Top, false, out float t))
                    {
                        ChopQuad(points[0], points[1], points[2], t, chopped);
                        points[0] = chopped[2] with { Y = clipper._clip.Top }; points[1] = chopped[3] with { Y = Math.Max(chopped[3].Y, clipper._clip.Top) };
                    }
                    else for (int i = 0; i < 3; i++) points[i] = points[i] with { Y = Math.Max(points[i].Y, clipper._clip.Top) };
                }
                if (points[2].Y > clipper._clip.Bottom)
                {
                    clipper.Charge(8);
                    if (QuadIntersection(points, clipper._clip.Bottom, false, out float t))
                    {
                        ChopQuad(points[0], points[1], points[2], t, chopped);
                        points[1] = chopped[1] with { Y = Math.Min(chopped[1].Y, clipper._clip.Bottom) }; points[2] = chopped[2] with { Y = clipper._clip.Bottom };
                    }
                    else for (int i = 0; i < 3; i++) points[i] = points[i] with { Y = Math.Min(points[i].Y, clipper._clip.Bottom) };
                }
                if (points[0].X > points[2].X) { (points[0], points[2]) = (points[2], points[0]); reverse = !reverse; }
                if (points[2].X <= clipper._clip.Left) { clipper.Vertical(clipper._clip.Left, points[0].Y, points[2].Y, reverse); return; }
                if (points[0].X >= clipper._clip.Right) { if (!clipper._cullRight) clipper.Vertical(clipper._clip.Right, points[0].Y, points[2].Y, reverse); return; }
                if (points[0].X < clipper._clip.Left)
                {
                    clipper.Charge(8);
                    if (QuadIntersection(points, clipper._clip.Left, true, out float t))
                    {
                        ChopQuad(points[0], points[1], points[2], t, chopped);
                        clipper.Vertical(clipper._clip.Left, chopped[0].Y, chopped[2].Y, reverse);
                        points[0] = chopped[2] with { X = clipper._clip.Left }; points[1] = chopped[3] with { X = Math.Max(chopped[3].X, clipper._clip.Left) };
                    }
                    else { clipper.Vertical(clipper._clip.Left, points[0].Y, points[2].Y, reverse); return; }
                }
                if (points[2].X > clipper._clip.Right)
                {
                    clipper.Charge(8);
                    if (QuadIntersection(points, clipper._clip.Right, true, out float t))
                    {
                        ChopQuad(points[0], points[1], points[2], t, chopped);
                        chopped[1] = chopped[1] with { X = Math.Min(chopped[1].X, clipper._clip.Right) }; chopped[2] = chopped[2] with { X = clipper._clip.Right };
                        clipper.AppendCurve(chopped.Slice(0, 3), reverse); clipper.Vertical(clipper._clip.Right, chopped[2].Y, chopped[4].Y, reverse); return;
                    }
                    points[1] = points[1] with { X = Math.Min(points[1].X, clipper._clip.Right) }; points[2] = points[2] with { X = Math.Min(points[2].X, clipper._clip.Right) };
                }
                clipper.AppendCurve(points, reverse);

                static bool QuadIntersection(scoped ReadOnlySpan<SvgAnalyticClipPoint> points, float target, bool xAxis, out float t)
                {
                    float a = xAxis ? points[0].X : points[0].Y, b = xAxis ? points[1].X : points[1].Y, c = xAxis ? points[2].X : points[2].Y;
                    // Solve the unit quadratic using the native coefficient order.
                    // coefficients are exactly A=a-2b+c, B=2(b-a), C=a-target.
                    float A = a - b - b + c, B = 2 * (b - a), C = a - target;
                    Span<float> roots = stackalloc float[2];
                    int count = UnitQuadRoots(A, B, C, roots);
                    t = count > 0 ? roots[0] : 0;
                    return count > 0;

                    static int UnitQuadRoots(float A, float B, float C, Span<float> roots)
                    {
                        if (A == 0) return CubicUnitDivide(-C, B, out roots[0]) ? 1 : 0;
                        double discriminant = (double)B * B - 4.0 * A * C;
                        if (discriminant < 0) return 0;
                        float r = (float)Math.Sqrt(discriminant);
                        if (!float.IsFinite(r)) return 0;
                        float q = B < 0 ? -(B - r) / 2 : -(B + r) / 2;
                        int count = 0;
                        if (CubicUnitDivide(q, A, out float first)) roots[count++] = first;
                        if (CubicUnitDivide(C, q, out float second)) roots[count++] = second;
                        if (count == 2) { if (roots[0] > roots[1]) (roots[0], roots[1]) = (roots[1], roots[0]); else if (roots[0] == roots[1]) count--; }
                        return count;
                    }
                }
            }
        }

        private static void ChopQuad(SvgAnalyticClipPoint a, SvgAnalyticClipPoint b, SvgAnalyticClipPoint c, float t, scoped Span<SvgAnalyticClipPoint> output)
        {
            var ab = CubicMix(a.Coordinates, b.Coordinates, t); var bc = CubicMix(b.Coordinates, c.Coordinates, t);
            var middle = CubicMix(ab, bc, t);
            output[0] = a; output[1] = new(ab.X, ab.Y); output[2] = new(middle.X, middle.Y); output[3] = new(bc.X, bc.Y); output[4] = c;
        }

        private static bool NearZero(double value) => value == 0 || Math.Abs(value) < 1.1920928955078125e-7;
        private static bool NearEqual(double a, double b)
        {
            double ma = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(a) & 0x7ff0000000000000L);
            double mb = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(b) & 0x7ff0000000000000L);
            double magnitude = Math.Max(Math.Max(ma, 2.2250738585072014e-308), mb);
            return a == b || Math.Abs(b - a) < magnitude * (2.220446049250313e-16 * 17);
        }

        private static int QuadraticRoots(double A, double B, double C, Span<double> roots)
        {
            if (A == 0 || Math.Abs(B / A) >= 1.0e16)
            {
                roots[0] = 0;
                if (NearZero(B)) return NearZero(C) ? 1 : 0;
                roots[0] = -C / B; return double.IsFinite(roots[0]) ? 1 : 0;
            }
            double b = -0.5 * B, b2 = b * b, ac = A * C, discriminant = b2 - ac;
            if (3 * Math.Abs(discriminant) < b2 + ac) discriminant = (b2 - ac) + (Math.FusedMultiplyAdd(b, b, -b2) - Math.FusedMultiplyAdd(A, C, -ac));
            if (!double.IsFinite(discriminant) || discriminant < 0) return 0;
            double r0, r1;
            if (discriminant == 0) r0 = r1 = b / A;
            else { double d = Math.Sqrt(discriminant), r = b > 0 ? b + d : b - d; r0 = r / A; r1 = C / r; }
            int count = 0;
            if (double.IsFinite(r0)) roots[count++] = NearZero(r0) ? 0 : r0;
            if (double.IsFinite(r1)) roots[count++] = NearZero(r1) ? 0 : r1;
            if (count == 2 && NearEqual(roots[0], roots[1])) count--;
            return count;
        }

        private static double Evaluate(double a, double b, double c, double d, double t)
            => Math.FusedMultiplyAdd(t, Math.FusedMultiplyAdd(t, Math.FusedMultiplyAdd(t, a, b), c), d);

    }
}
