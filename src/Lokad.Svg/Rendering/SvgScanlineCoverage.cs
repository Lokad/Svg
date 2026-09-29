using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Answers per-pixel coverage queries for one scanline row across retained device contours.</summary>
/// <remarks>
/// <para>Membership resolves before coverage: row preparation collects signed edge crossings and per-edge touched pixel ranges, sorts both by x, then sweeps per-contour signed winding into interval totals. A pixel evaluates through the shared fan path unless its square touches no contour edge (within an adaptive epsilon) and its interval total is zero; skipped pixels match a fan total of exactly zero under that epsilon discipline, while everything else keeps existing values bitwise. Roles weight nonzero magnitudes, signs weight even-odd magnitudes, and the role-less path accumulates signed winding exactly like the unwound fan sum.</para>
/// <para>Ownership: construction reserves caller-charged scratch storage sized by ScratchByteCount and releases it through Dispose; this type allocates nothing else and holds no budget itself. Cancellation is observed while scanning edges, sorting crossings and sweeping intervals; MightCover is pure. Instances are single-threaded like the raster loops that own them, and queries assume ascending pixel order only for cache efficiency, never for correctness.</para>
/// </remarks>
internal ref struct SvgScanlineCoverage
{
    private readonly ReadOnlySpan<SvgDevicePoint> _points;
    private readonly ReadOnlySpan<int> _ends;
    private readonly ReadOnlySpan<int> _votingSigns;
    private readonly bool _isEvenOdd;
    private readonly bool _hasVotes;
    private readonly int[] _orientations;
    private readonly int _contourCount;
    private readonly int _xStart;
    private readonly int _xEnd;
    private readonly double _epsilon;
    private readonly bool _allFinite;
    private readonly double[] _crossX;
    // Crossing ownership packed with direction: contour + 1 for upward edges, -(contour + 1) for downward edges, so the x sort carries both without extra storage.
    private readonly int[] _crossContour;
    private readonly int[] _touchLo;
    private readonly int[] _touchHi;
    private readonly int[] _winding;
    private readonly double[] _segmentX;
    private readonly int[] _segmentTotal;
    private readonly RenderBudget.BudgetReservation? _reservation;
    private int _crossCount;
    private int _touchCount;
    private int _segmentCount;

    private SvgScanlineCoverage(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, ReadOnlySpan<int> votingSigns, bool isEvenOdd, bool hasVotes, int[] orientations, int xStart, int xEnd, double epsilon, bool allFinite, double[] crossX, int[] crossContour, int[] touchLo, int[] touchHi, int[] winding, double[] segmentX, int[] segmentTotal, RenderBudget.BudgetReservation? reservation)
    {
        _points = points;
        _ends = ends;
        _votingSigns = votingSigns;
        _isEvenOdd = isEvenOdd;
        _hasVotes = hasVotes;
        _orientations = orientations;
        _contourCount = ends.Length;
        _xStart = xStart;
        _xEnd = xEnd;
        _epsilon = epsilon;
        _allFinite = allFinite;
        _crossX = crossX;
        _crossContour = crossContour;
        _touchLo = touchLo;
        _touchHi = touchHi;
        _winding = winding;
        _segmentX = segmentX;
        _segmentTotal = segmentTotal;
        _reservation = reservation;
        _crossCount = 0;
        _touchCount = 0;
        _segmentCount = 0;
    }

    public void Dispose() => _reservation?.Dispose();

    /// <summary>Returns the caller-reserved scratch byte count for one fill with the given point and contour counts.</summary>
    internal static long ScratchByteCount(int pointCount, int contourCount)
    {
        long bytes;
        try { bytes = checked(ParserCollectionMemory.GetArrayByteCount<double>(pointCount) + ParserCollectionMemory.GetArrayByteCount<int>(pointCount) + ParserCollectionMemory.GetArrayByteCount<int>(pointCount) + ParserCollectionMemory.GetArrayByteCount<int>(pointCount) + ParserCollectionMemory.GetArrayByteCount<int>(contourCount) + ParserCollectionMemory.GetArrayByteCount<int>(contourCount) + ParserCollectionMemory.GetArrayByteCount<double>(pointCount + 1) + ParserCollectionMemory.GetArrayByteCount<int>(pointCount + 1)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage storage exceeds the supported accounting range."); }
        return bytes;
    }

    /// <summary>Validates retained contours like the shared fan path, then reserves scanline scratch for one fill.</summary>
    /// <remarks>Validation mirrors the fan coverage guards exactly (partitioned ends, role counts and values) so malformed payloads fail identically; the returned setup cost covers validation, epsilon calibration and plain-mode orientation scoring for the caller work ledger.</remarks>
    internal static SvgScanlineCoverage Prepare(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, ReadOnlySpan<int> nestingSigns, bool isEvenOdd, int[]? signs, int xStart, int xEnd, RenderBudget budget, out long setupWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(budget);
        SvgPolygonCoverage.ValidateContourEnds(points, ends);
        ReadOnlySpan<int> votes = isEvenOdd ? nestingSigns : (signs is int[] roles ? roles : ReadOnlySpan<int>.Empty);
        if (!isEvenOdd && signs is not null && signs.Length != ends.Length) throw new ArgumentOutOfRangeException(nameof(signs));
        if (isEvenOdd && nestingSigns.Length != ends.Length) throw new ArgumentOutOfRangeException(nameof(nestingSigns));
        bool hasVotes = isEvenOdd || signs is not null;
        for (int contour = 0; contour < ends.Length; contour++)
        {
            if ((contour & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (hasVotes && votes[contour] is not (-1 or 0 or 1)) throw new ArgumentOutOfRangeException(isEvenOdd ? nameof(nestingSigns) : nameof(signs));
        }
        double maxAbs = 0;
        bool allFinite = true;
        for (int index = 0; index < points.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            double ax = Math.Abs(points[index].X);
            double ay = Math.Abs(points[index].Y);
            if (!double.IsFinite(ax) || !double.IsFinite(ay)) { allFinite = false; break; }
            if (ax > maxAbs) maxAbs = ax;
            if (ay > maxAbs) maxAbs = ay;
        }
        double epsilon = Math.Max(0.000000001, maxAbs * 0.000000000000001);
        int[] orientations = new int[ends.Length];
        if (!hasVotes)
        {
            int start = 0;
            for (int contour = 0; contour < ends.Length; contour++)
            {
                if ((contour & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                orientations[contour] = ContourOrientation(points, start, ends[contour]);
                start = ends[contour];
            }
        }
        double[] crossX;
        int[] crossContour;
        int[] touchLo;
        int[] touchHi;
        double[] segmentX;
        int[] segmentTotal;
        int[] winding = new int[ends.Length];
        RenderBudget.BudgetReservation? scratch = points.Length == 0 && ends.Length == 0 ? null : budget.Reserve(ScratchByteCount(points.Length, ends.Length), SvgMemoryCategory.Surface);
        try
        {
            crossX = new double[points.Length];
            crossContour = new int[points.Length];
            touchLo = new int[points.Length];
            touchHi = new int[points.Length];
            segmentX = new double[points.Length + 1];
            segmentTotal = new int[points.Length + 1];
            segmentX[0] = double.NegativeInfinity;
            segmentTotal[0] = 0;
        }
        catch { scratch?.Dispose(); throw; }
        setupWork = (long)points.Length + ends.Length;
        return new SvgScanlineCoverage(points, ends, votes, isEvenOdd, hasVotes, orientations, xStart, xEnd, epsilon, allFinite, crossX, crossContour, touchLo, touchHi, winding, segmentX, segmentTotal, scratch);
    }

    /// <summary>Returns the authored orientation sign of one closed contour ring.</summary>
    private static int ContourOrientation(ReadOnlySpan<SvgDevicePoint> points, int start, int end)
    {
        double twice = 0;
        for (int index = start; index < end; index++)
        {
            SvgDevicePoint first = points[index];
            SvgDevicePoint second = points[(index + 1 - start) % (end - start) + start];
            twice += (second.X - first.X) * (second.Y + first.Y);
        }
        return twice > 0 ? 1 : twice < 0 ? -1 : 0;
    }

    /// <summary>Builds sorted crossings and touched intervals for one pixel row, charging setup work.</summary>
    /// <remarks>Every contour edge is visited once per row; crossings sort by x with direction-encoded contour identity so the sweep resolves per-contour signed winding, and touched pixel ranges merge into disjoint intervals. The returned row cost covers the edge scan, winding clearing and a sorting upper bound for the caller ledger.</remarks>
    internal void PrepareRow(int y, out long rowWork, CancellationToken cancellationToken)
    {
        Array.Clear(_winding, 0, _contourCount);
        _crossCount = 0;
        _touchCount = 0;
        _segmentCount = 1;
        _segmentX[0] = double.NegativeInfinity;
        _segmentTotal[0] = 0;
        if (!_allFinite)
        {
            rowWork = _points.Length + _contourCount;
            return;
        }
        double centerY = y + 0.5;
        double bandLo = y - _epsilon;
        double bandHi = y + 1 + _epsilon;
        int start = 0;
        long scanned = 0;
        for (int contour = 0; contour < _contourCount; contour++)
        {
            int end = _ends[contour];
            if (end - start >= 3 && Vote(contour) != 0)
            {
                for (int index = start; index < end; index++)
                {
                    if ((scanned & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    scanned++;
                    SvgDevicePoint first = _points[index];
                    SvgDevicePoint second = _points[index + 1 < end ? index + 1 : start];
                    double rangeLo = double.NaN;
                    double rangeHi = double.NaN;
                    if (first.Y == second.Y)
                    {
                        if (first.Y < bandLo || first.Y > bandHi) rangeLo = double.NaN;
                        else { rangeLo = first.X < second.X ? first.X : second.X; rangeHi = first.X > second.X ? first.X : second.X; }
                    }
                    else
                    {
                        double span = second.Y - first.Y;
                        double enter = (bandLo - first.Y) / span;
                        double leave = (bandHi - first.Y) / span;
                        double cutLo = enter < leave ? enter : leave;
                        double cutHi = enter > leave ? enter : leave;
                        if (cutLo < 0) cutLo = 0;
                        if (cutHi > 1) cutHi = 1;
                        if (cutLo > cutHi) rangeLo = double.NaN;
                        else
                        {
                            double xAtLo = first.X + cutLo * (second.X - first.X);
                            double xAtHi = first.X + cutHi * (second.X - first.X);
                            rangeLo = xAtLo < xAtHi ? xAtLo : xAtHi;
                            rangeHi = xAtLo > xAtHi ? xAtLo : xAtHi;
                        }
                    }
                    if (double.IsFinite(rangeLo) && double.IsFinite(rangeHi))
                    {
                        double clampedLo = Math.Max(_xStart - 1, Math.Min(_xEnd, Math.Ceiling(rangeLo - _epsilon - 1)));
                        double clampedHi = Math.Max(_xStart - 1, Math.Min(_xEnd, Math.Floor(rangeHi + _epsilon)));
                        int loPx = (int)clampedLo;
                        int hiPx = (int)clampedHi;
                        if (loPx <= hiPx)
                        {
                            _touchLo[_touchCount] = loPx;
                            _touchHi[_touchCount] = hiPx;
                            _touchCount++;
                        }
                    }
                    if ((first.Y > centerY) != (second.Y > centerY))
                    {
                        double crossX = first.X + (centerY - first.Y) * (second.X - first.X) / (second.Y - first.Y);
                        _crossX[_crossCount] = crossX;
                        _crossContour[_crossCount] = second.Y > first.Y ? contour + 1 : -(contour + 1);
                        _crossCount++;
                    }
                }
            }
            start = end;
        }
        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(_crossX, _crossContour, 0, _crossCount);
        Array.Sort(_touchLo, _touchHi, 0, _touchCount);
        int merged = 0;
        for (int touch = 0; touch < _touchCount; touch++)
        {
            if ((touch & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (merged > 0 && _touchLo[touch] <= _touchHi[merged - 1] + 1)
            {
                if (_touchHi[touch] > _touchHi[merged - 1]) _touchHi[merged - 1] = _touchHi[touch];
            }
            else
            {
                _touchLo[merged] = _touchLo[touch];
                _touchHi[merged] = _touchHi[touch];
                merged++;
            }
        }
        _touchCount = merged;
        int total = 0;
        int cursor = 0;
        while (cursor < _crossCount)
        {
            if ((cursor & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            double groupX = _crossX[cursor];
            do
            {
                int packed = _crossContour[cursor];
                int hit = (packed > 0 ? packed : -packed) - 1;
                int step = packed > 0 ? 1 : -1;
                int old = _winding[hit];
                int updated = old + step;
                _winding[hit] = updated;
                if (_hasVotes) total += _votingSigns[hit] * (Math.Abs(updated) - Math.Abs(old));
                else total += step;
                cursor++;
            } while (cursor < _crossCount && _crossX[cursor] == groupX);
            _segmentX[_segmentCount] = groupX;
            _segmentTotal[_segmentCount] = total;
            _segmentCount++;
        }
        cancellationToken.ThrowIfCancellationRequested();
        int sortLevels = 0;
        for (int level = _crossCount; level > 0; level >>= 1) sortLevels++;
        rowWork = scanned + _contourCount + (long)_crossCount * sortLevels;
    }

    /// <summary>Reports whether one pixel may carry nonzero coverage on the prepared row.</summary>
    /// <remarks>True covers every pixel the shared fan path could paint: touched squares plus nonzero winding intervals. Skipped pixels match a fan total of exactly zero under the preparation epsilon discipline.</remarks>
    internal bool MightCover(int x)
    {
        if (!_allFinite) return true;
        int lo = 0;
        int hi = _touchCount;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (_touchLo[mid] <= x) lo = mid + 1;
            else hi = mid;
        }
        if (lo > 0 && x <= _touchHi[lo - 1]) return true;
        int start = 0;
        int end = _segmentCount;
        while (start < end)
        {
            int mid = start + ((end - start) >> 1);
            if (_segmentX[mid] <= x) start = mid + 1;
            else end = mid;
        }
        return _segmentTotal[start - 1] != 0;
    }

    /// <summary>Returns the signed winding weight of one contour, or zero when masked out.</summary>
    private int Vote(int contour)
    {
        if (_isEvenOdd) return _votingSigns[contour] == 0 ? 0 : 1;
        if (_hasVotes) return _votingSigns[contour];
        return _orientations[contour];
    }
}