namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal static partial class OpenTypeFont
{
    private static readonly string[] LatinAlignmentSamples = ["THEZOCQS", "HEZLOCUS", "fijkdbh", "uvxzoesc", "nrxzoesc", "pqgjy"];
    /// <summary>Returns owned Latin light-hinted contours in 26.6 device coordinates, borrowing the unchanged font program.</summary>
    /// <remarks>Metric glyphs and the rendered glyph use the existing decoder. All temporary points, topology and returned segments retain their reservations until disposal. No font or native service is acquired here.</remarks>
    internal static OpenTypeGlyphContourStorage GetLatinLightContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, RenderBudget budget, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(deviceSize) || deviceSize <= 0 || deviceSize > 32767) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted font size exceeds the native coordinate range.");
        cancellationToken.ThrowIfCancellationRequested();
        bool nonBase = IsNonBase(bytes, glyphIndex, metrics, cancellationToken);
        using OpenTypeSimpleGlyphStructureStorage structure = GetGlyphStructure(bytes, glyphIndex, metrics, budget, cancellationToken, true);
        // Native light outlines average signed 26.6 implied points toward zero.
        if (structure.CompositeHeader is not null)
        {
            using OpenTypeGlyphPointOutline outline = GetGlyphPointOutline(bytes, glyphIndex, metrics, null, null, OpenTypeCompositeOffsetMath.FontationsLength, budget, cancellationToken);
            SubtractGlyphOrigin(outline.Points, GetHorizontalOrigin(bytes, glyphIndex, metrics, cancellationToken), cancellationToken);
            Span<LatinAlignmentZone> compositeZones = stackalloc LatinAlignmentZone[6];
            int compositeZoneCount = GetLatinAlignmentZones(bytes, metrics, compositeZones, budget, cancellationToken);
            LatinLightHinting.Hint(outline.Points, outline.Ends, compositeZones[..compositeZoneCount], metrics.UnitsPerEm, deviceSize, GetLatinStemWidths(bytes, metrics, budget, cancellationToken), !nonBase, budget, cancellationToken);
            using OpenTypeSegmentAccumulator compositeSegments = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, cancellationToken);
            BuildQuadraticContours(outline.Points, outline.Ends, compositeSegments, true, TrueTypeImpliedPointRounding.TowardZero, cancellationToken);
            return compositeSegments.Detach();
        }
        if (structure.Value.ContourEndpoints.Length == 0) return new([], [], null, null);
        using OpenTypeGlyphPointStorage points = GetSimpleGlyphPoints(bytes, structure.Value, budget, cancellationToken);
        SubtractGlyphOrigin(points.Points, GetHorizontalOrigin(bytes, glyphIndex, metrics, cancellationToken), cancellationToken);
        Span<LatinAlignmentZone> zones = stackalloc LatinAlignmentZone[6];
        int zoneCount = GetLatinAlignmentZones(bytes, metrics, zones, budget, cancellationToken);
        LatinLightHinting.Hint(points.Points, structure.Value.ContourEndpoints, zones[..zoneCount], metrics.UnitsPerEm, deviceSize, GetLatinStemWidths(bytes, metrics, budget, cancellationToken), !nonBase, budget, cancellationToken);
        using var segments = new OpenTypeSegmentAccumulator(budget, GlyphDecoderBudgetPhase.SimpleSegmentCapacity, GlyphDecoderBudgetPhase.SimpleSegmentExact, GlyphDecoderBudgetPhase.SimpleContourBoundaryCapacity, GlyphDecoderBudgetPhase.SimpleContourBoundaryExact, cancellationToken);
        BuildQuadraticContours(points.Points, structure.Value.ContourEndpoints, segments, true, TrueTypeImpliedPointRounding.TowardZero, cancellationToken);
        return segments.Detach();

        static bool IsNonBase(ReadOnlyMemory<byte> bytes, ushort glyph, OpenTypeFontMetrics metrics, CancellationToken token)
        {
            if (glyph == 0 || metrics.Cmap is null) return false;
            // Skrifa 0.43.2 generated Latin style ranges. Classification belongs
            // to the cmap glyph, including aliases, not the requested scalar.
            // It changes alignment only; admitting a mapping does not enable
            // mark shaping or expand the supported text subset.
            ReadOnlySpan<(int First, int Last)> ranges = [(94, 96), (126, 126), (168, 169), (174, 176), (180, 180), (184, 184), (188, 190), (697, 735), (741, 879), (6832, 6846), (7616, 7679), (8215, 8215), (8254, 8254), (42888, 42888), (43002, 43002)];
            foreach ((int first, int last) in ranges)
                for (int scalar = first; scalar <= last; scalar++)
                {
                    token.ThrowIfCancellationRequested();
                    if (GetGlyphIndex(bytes, (uint)scalar, metrics, token) == glyph) return true;
                }
            return false;
        }
    }

    /// <summary>Measures linked widths and the native edge-merging allowance from one bounded standard glyph.</summary>
    private static LatinStemWidths GetLatinStemWidths(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, RenderBudget budget, CancellationToken token)
    {
        foreach (char scalar in "oO0")
        {
            ushort index = GetGlyphIndex(bytes, scalar, metrics, token);
            if (index == 0) continue;
            using OpenTypeGlyphPointOutline sample = GetGlyphPointOutline(bytes, index, metrics, null, null, OpenTypeCompositeOffsetMath.FontationsLength, budget, token);
            return LatinLightHinting.MeasureStemWidths(sample.Points, sample.Ends, metrics.UnitsPerEm, budget, token);
        }
        return new(0, metrics.UnitsPerEm * 50 / 2048 / 5);
    }

    /// <summary>Samples at most six Latin alignment zones into caller-owned storage, borrowing one face and reserving decoded glyph storage.</summary>
    internal static int GetLatinAlignmentZones(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, Span<LatinAlignmentZone> zones, RenderBudget budget, CancellationToken cancellationToken)
    {
        if (zones.Length < LatinAlignmentSamples.Length) throw new ArgumentException("The alignment buffer must hold every Latin zone.", nameof(zones));
        int zoneCount = 0;
        Span<int> flats = stackalloc int[8];
        Span<int> rounds = stackalloc int[8];
        for (int zoneIndex = 0; zoneIndex < LatinAlignmentSamples.Length; zoneIndex++)
        {
            int flatCount = 0, roundCount = 0;
            int ascender = int.MinValue, descender = int.MaxValue;
            bool top = zoneIndex is 0 or 2 or 3;
            foreach (char scalar in LatinAlignmentSamples[zoneIndex])
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort sampleGlyph = GetGlyphIndex(bytes, scalar, metrics, cancellationToken);
                if (sampleGlyph == 0) continue;
                OpenTypeGlyphHeader? header = GetGlyphHeader(bytes, sampleGlyph, metrics, cancellationToken);
                if (header is null || !header.Value.IsComposite && header.Value.ContourCount == 0) continue;
                using OpenTypeGlyphPointOutline sample = GetGlyphPointOutline(bytes, sampleGlyph, metrics, null, null, OpenTypeCompositeOffsetMath.FontationsLength, budget, cancellationToken);
                if (!LatinLightHinting.FindAlignment(sample.Points, sample.Ends, top, metrics.UnitsPerEm, cancellationToken, out int position, out bool round, out int maximum, out int minimum)) continue;
                ascender = Math.Max(ascender, maximum); descender = Math.Min(descender, minimum);
                if (round) rounds[roundCount++] = position;
                else flats[flatCount++] = position;
            }
            if (flatCount + roundCount == 0) continue;
            flats[..flatCount].Sort(); rounds[..roundCount].Sort();
            int reference = flatCount == 0 ? rounds[roundCount / 2] : flats[flatCount / 2];
            int overshoot = roundCount == 0 ? reference : rounds[roundCount / 2];
            if (reference != overshoot && (top ^ (overshoot > reference))) reference = overshoot = (reference + overshoot) / 2;
            zones[zoneCount++] = new(reference, overshoot, ascender, descender, top, zoneIndex == 3);
        }
        return zoneCount;
    }
}

/// <summary>One sampled Latin alignment zone in font units.</summary>
internal readonly record struct LatinAlignmentZone(int Reference, int Overshoot, int Ascender, int Descender, bool Top, bool AdjustScale);

/// <summary>Inline standard-glyph measurements in font units; owns no storage.</summary>
internal readonly record struct LatinStemWidths(int MaximumWidth, int EdgeDistanceThreshold);

/// <summary>Applies bounded vertical Latin light fitting to caller-owned decoded points.</summary>
/// <remarks>Adapts Fontations/Skrifa 0.43.2's Latin topology, alignment and interpolation. See THIRD_PARTY_NOTICES.txt. The font program is never modified; all scratch allocations are charged before allocation and disposed on every exit.</remarks>
internal static class LatinLightHinting
{
    internal static bool FindAlignment(ReadOnlySpan<OpenTypeGlyphPoint> points, ReadOnlySpan<ushort> ends, bool top, int unitsPerEm, CancellationToken token, out int position, out bool round, out int maximum, out int minimum)
    {
        position = top ? int.MinValue : int.MaxValue; round = false;
        maximum = int.MinValue; minimum = int.MaxValue;
        int first = 0, chosenFirst = 0, chosenLast = 0, chosen = -1;
        foreach (ushort last in ends)
        {
            for (int i = first; i <= last; i++)
            {
                token.ThrowIfCancellationRequested();
                OpenTypeGlyphPoint point = points[i];
                if (point.X is < short.MinValue or > short.MaxValue || point.Y is < short.MinValue or > short.MaxValue) throw Limit("The alignment outline exceeds the native font-unit range.");
                maximum = Math.Max(maximum, point.Y); minimum = Math.Min(minimum, point.Y);
                if (last - first < 1 || (chosen >= 0 && !(top ? point.Y > position : point.Y < position))) continue;
                chosen = i; position = point.Y; chosenFirst = first; chosenLast = last;
            }
            first = last + 1;
        }
        if (chosen < 0) return false;
        int left = chosen, right = chosen, onLeft = points[chosen].IsOnCurve ? chosen : -1, onRight = onLeft;
        for (int i = Previous(chosen); i != chosen; i = Previous(i))
        {
            token.ThrowIfCancellationRequested();
            int distance = Math.Abs(points[i].Y - position);
            if (distance > 5 && Math.Abs(points[i].X - points[chosen].X) <= 20 * distance) break;
            left = i;
            if (points[i].IsOnCurve) { onLeft = i; if (onRight < 0) onRight = i; }
        }
        for (int i = Next(chosen); i != chosen; i = Next(i))
        {
            token.ThrowIfCancellationRequested();
            int distance = Math.Abs(points[i].Y - position);
            if (distance > 5 && Math.Abs(points[i].X - points[chosen].X) <= 20 * distance) break;
            right = i;
            if (points[i].IsOnCurve) { onRight = i; if (onLeft < 0) onLeft = i; }
        }
        round = !(onLeft >= 0 && onRight >= 0 && Math.Abs(points[onRight].X - points[onLeft].X) > unitsPerEm / 14)
            && (!points[left].IsOnCurve || !points[right].IsOnCurve);
        return true;

        int Previous(int index) => index == chosenFirst ? chosenLast : index - 1;
        int Next(int index) => index == chosenLast ? chosenFirst : index + 1;
    }

    internal static void Hint(Span<OpenTypeGlyphPoint> source, ReadOnlySpan<ushort> ends, ReadOnlySpan<LatinAlignmentZone> zones, int unitsPerEm, double size, RenderBudget budget, CancellationToken token)
        => Hint(source, ends, zones, unitsPerEm, size, new(0, unitsPerEm * 50 / 2048 / 5), budget, token);

    internal static void Hint(Span<OpenTypeGlyphPoint> source, ReadOnlySpan<ushort> ends, ReadOnlySpan<LatinAlignmentZone> zones, int unitsPerEm, double size, LatinStemWidths stemWidths, RenderBudget budget, CancellationToken token)
        => Hint(source, ends, zones, unitsPerEm, size, stemWidths, true, budget, token);

    internal static void Hint(Span<OpenTypeGlyphPoint> source, ReadOnlySpan<ushort> ends, ReadOnlySpan<LatinAlignmentZone> zones, int unitsPerEm, double size, LatinStemWidths stemWidths, bool alignBlueZones, RenderBudget budget, CancellationToken token)
    {
        if (!double.IsFinite(size) || size <= 0 || size > 32767) throw Limit("The hinted font size exceeds the native coordinate range.");
        token.ThrowIfCancellationRequested();
        int xScale = Divide((int)((float)size * 64), unitsPerEm), yScale = xScale;
        foreach (LatinAlignmentZone zone in zones)
        {
            token.ThrowIfCancellationRequested();
            if (!zone.AdjustScale) continue;
            int scaled = Multiply(zone.Overshoot, yScale), fitted = (scaled + 40) & ~63;
            if (scaled == 0 || scaled == fitted) continue;
            int nextScale = MultiplyDivide(yScale, fitted, scaled);
            int maximum = unitsPerEm;
            foreach (LatinAlignmentZone item in zones) maximum = Math.Max(maximum, Math.Max(item.Ascender, -item.Descender));
            if ((Math.Abs(Multiply(maximum, nextScale - yScale)) & ~127) == 0) yScale = nextScale;
        }
        using RenderBudget.BudgetReservation pointOwner = budget.Reserve(checked(32L + source.Length * 64L), SvgMemoryCategory.Geometry);
        var points = new HintPoint[source.Length];
        int first = 0;
        foreach (ushort last in ends)
        {
            for (int i = first; i <= last; i++)
            {
                token.ThrowIfCancellationRequested();
                OpenTypeGlyphPoint point = source[i];
                if (point.X is < short.MinValue or > short.MaxValue || point.Y is < short.MinValue or > short.MaxValue) throw Limit("The hinted outline exceeds the native font-unit range.");
                int previous = i == first ? last : i - 1, next = i == last ? first : i + 1;
                points[i] = new(point.X, point.Y, Multiply(point.X, xScale), Multiply(point.Y, yScale), previous, next, point.IsOnCurve);
            }
            first = last + 1;
        }
        ComputeDirections(points, ends, unitsPerEm, token);
        for (int i = 0; i < points.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            ref HintPoint point = ref points[i];
            HintPoint previous = points[point.PreviousNonNear], next = points[point.NextNonNear];
            int inX = point.FontX - previous.FontX, inY = point.FontY - previous.FontY;
            int outX = next.FontX - point.FontX, outY = next.FontY - point.FontY;
            point.Weak = point.Weak || !point.OnCurve || (point.In == point.Out && point.In != 0) || (point.In != 0 && point.In == -point.Out)
                || (point.In == 0 && point.Out == 0 && (((inX ^ outX) >= 0 && (inY ^ outY) >= 0) || IsFlat(inX, inY, outX, outY)));
        }
        if (zones.Length != 0 && yScale != 0)
        {
            using BudgetedParserList<HintSegment> segments = new(budget, SvgMemoryCategory.Geometry, token);
            BuildSegments(points, ends, segments, unitsPerEm, token);
            int major = Orientation(points, ends, token) > 0 ? 1 : -1;
            LinkSegments(segments, major, unitsPerEm, stemWidths.MaximumWidth, token);
            using BudgetedParserList<HintEdge> edges = new(budget, SvgMemoryCategory.Geometry, token);
            BuildEdges(segments, edges, yScale, stemWidths.EdgeDistanceThreshold, token);
            FitEdges(edges, alignBlueZones ? zones : [], yScale, unitsPerEm, major, token);
            AlignPoints(points, ends, segments, edges, token);
        }
        for (int i = 0; i < source.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            source[i] = new(points[i].X, points[i].Y, points[i].OnCurve);
        }

        static bool IsFlat(int inX, int inY, int outX, int outY)
        {
            int combined = Hypot(inX + outX, inY + outY);
            return Hypot(inX, inY) + Hypot(outX, outY) - combined < (combined >> 4);
            static int Hypot(int x, int y) { x = Math.Abs(x); y = Math.Abs(y); return x > y ? x + (3 * y >> 3) : y + (3 * x >> 3); }
        }

        static void BuildEdges(BudgetedParserList<HintSegment> segments, BudgetedParserList<HintEdge> edges, int scale, int edgeDistanceThreshold, CancellationToken token)
        {
            // Nearby edges merge only within one fifth of the standard stem,
            // capped at a quarter device pixel. Thin serifs remain separate.
            int threshold = Divide(Math.Min(16, Multiply(edgeDistanceThreshold, scale)), scale), maxDelta = Divide(32, scale);
            for (int i = 0; i < segments.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                HintSegment segment = segments[i];
                if (segment.Delta > maxDelta) continue;
                int match = -1;
                for (int j = 0; j < edges.Count; j++)
                {
                    token.ThrowIfCancellationRequested();
                    if (edges[j].Direction == segment.Direction && Math.Abs(edges[j].FontPosition - segment.Position) < threshold) { match = j; break; }
                }
                if (match < 0) { match = edges.Count; edges.Add(new(segment.Position, Multiply(segment.Position, scale), segment.Direction, segment.Round)); }
                segment.Edge = match; segments[i] = segment;
            }
            // Sort while updating the bounded segment indices; no comparer or index-map allocation.
            for (int i = 1; i < edges.Count; i++)
            for (int j = i; j > 0 && edges[j].FontPosition < edges[j - 1].FontPosition; j--)
            {
                token.ThrowIfCancellationRequested();
                HintEdge temp = edges[j]; edges[j] = edges[j - 1]; edges[j - 1] = temp;
                for (int k = 0; k < segments.Count; k++)
                {
                    token.ThrowIfCancellationRequested();
                    HintSegment segment = segments[k];
                    if (segment.Edge == j) segment.Edge = j - 1;
                    else if (segment.Edge == j - 1) segment.Edge = j;
                    segments[k] = segment;
                }
            }
            for (int i = 0; i < segments.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                HintSegment segment = segments[i];
                if (segment.Edge < 0) continue;
                HintEdge edge = edges[segment.Edge];
                if (segment.Link >= 0 && segments[segment.Link].Edge >= 0) edge.Link = segments[segment.Link].Edge;
                if (segment.Serif >= 0 && segments[segment.Serif].Edge >= 0) edge.Serif = segments[segment.Serif].Edge;
                edges[segment.Edge] = edge;
            }
        }

        static void FitEdges(BudgetedParserList<HintEdge> edges, ReadOnlySpan<LatinAlignmentZone> zones, int scale, int unitsPerEm, int major, CancellationToken token)
        {
            int anchor = -1;
            for (int i = 0; i < edges.Count; i++)
            {
                HintEdge edge = edges[i];
                int best = Math.Min(32, Multiply(unitsPerEm / 40, scale));
                foreach (LatinAlignmentZone zone in zones)
                {
                    token.ThrowIfCancellationRequested();
                    int zoneDistance = Multiply(zone.Reference - zone.Overshoot, scale);
                    if (Math.Abs(zoneDistance) > 48 || !(zone.Top ^ (edge.Direction == major))) continue;
                    int reference = zone.Reference;
                    int distance = Multiply(Math.Abs(edge.FontPosition - reference), scale);
                    int fitted = Round(Multiply(reference, scale));
                    if (edge.Round && distance != 0 && (zone.Top ^ (edge.FontPosition < reference)))
                    {
                        int shootDistance = Multiply(Math.Abs(edge.FontPosition - zone.Overshoot), scale);
                        if (shootDistance < distance)
                        {
                            distance = shootDistance;
                            int delta = Math.Abs(zoneDistance) < 32 ? 0 : Math.Abs(zoneDistance) < 48 ? 32 : 64;
                            fitted -= zoneDistance < 0 ? -delta : delta;
                        }
                    }
                    if (distance < best) { best = distance; edge.Position = fitted; edge.Done = true; }
                }
                edges[i] = edge;
                if (edge.Done && anchor < 0) anchor = i;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                HintEdge edge = edges[i];
                if (!edge.Done || edge.Link < 0 || edges[edge.Link].Done) continue;
                HintEdge linked = edges[edge.Link];
                linked.Position = edge.Position + linked.Original - edge.Original; linked.Done = true; edges[edge.Link] = linked;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                HintEdge edge = edges[i];
                if (edge.Done || edge.Link < 0) continue;
                HintEdge linked = edges[edge.Link];
                int length = linked.Original - edge.Original;
                if (linked.Done) edge.Position = linked.Position - length;
                else
                {
                    int original = anchor < 0 ? edge.Original : edges[anchor].Position + edge.Original - edges[anchor].Original;
                    int center = original + (length >> 1);
                    if (Math.Abs(length) < 96)
                    {
                        int roundedCenter = Round(center), upper = Math.Abs(length) <= 64 ? 32 : 38, lower = Math.Abs(length) <= 64 ? 32 : 26;
                        center = Math.Abs(center - (roundedCenter - upper)) < Math.Abs(center - (roundedCenter + lower)) ? roundedCenter - upper : roundedCenter + lower;
                        edge.Position = center - length / 2;
                    }
                    else if (anchor < 0)
                    {
                        edge.Position = Round(original);
                    }
                    else
                    {
                        int candidate1 = Round(original), candidate2 = Round(original + length) - length;
                        edge.Position = Math.Abs(candidate1 + (length >> 1) - center) < Math.Abs(candidate2 + (length >> 1) - center) ? candidate1 : candidate2;
                    }
                    linked.Position = Math.Abs(length) < 96 && anchor >= 0 ? center + length / 2 : edge.Position + length;
                    linked.Done = true; edges[edge.Link] = linked;
                }
                // Pending edges constrain fitted stems too, unless the bound
                // would collapse the linked stem to a quarter pixel or less.
                if (anchor >= 0 && i > 0 && edge.Position < edges[i - 1].Position
                    && Math.Abs(linked.Position - edges[i - 1].Position) > 16)
                    edge.Position = edges[i - 1].Position;
                edge.Done = true; edges[i] = edge;
                if (anchor < 0) anchor = i;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                HintEdge edge = edges[i];
                if (edge.Done) continue;
                if (edge.Serif >= 0 && Math.Abs(edge.Original - edges[edge.Serif].Original) < 80)
                    edge.Position = edges[edge.Serif].Position + edge.Original - edges[edge.Serif].Original;
                else if (anchor < 0) { edge.Position = Round(edge.Original); anchor = i; }
                else
                {
                    int before = i - 1, after = i + 1;
                    while (before >= 0 && !edges[before].Done) { token.ThrowIfCancellationRequested(); before--; }
                    while (after < edges.Count && !edges[after].Done) { token.ThrowIfCancellationRequested(); after++; }
                    edge.Position = before >= 0 && after < edges.Count && edges[before].Original != edges[after].Original
                        ? edges[before].Position + MultiplyDivide(edge.Original - edges[before].Original, edges[after].Position - edges[before].Position, edges[after].Original - edges[before].Original)
                        : edges[anchor].Position + ((edge.Original - edges[anchor].Original + 16) & ~31);
                }
                // All linked stems are already done. Unlinked serifs retain
                // their offset even when they cross an earlier fitted edge.
                edge.Done = true; edges[i] = edge;
            }
        }

        static void AlignPoints(HintPoint[] points, ReadOnlySpan<ushort> ends, BudgetedParserList<HintSegment> segments, BudgetedParserList<HintEdge> edges, CancellationToken token)
        {
            if (edges.Count == 0) return;
            foreach (HintSegment segment in segments.AsSpan())
            {
                if (segment.Edge < 0) continue;
                int i = segment.First;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    points[i].Y = edges[segment.Edge].Position; points[i].Touched = true;
                    if (i == segment.Last) break;
                    i = points[i].Next;
                }
            }
            for (int i = 0; i < points.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                ref HintPoint point = ref points[i];
                if (point.Touched || point.Weak) continue;
                if (point.FontY <= edges[0].FontPosition) point.Y = edges[0].Position + point.OriginalY - edges[0].Original;
                else if (point.FontY >= edges[edges.Count - 1].FontPosition) point.Y = edges[edges.Count - 1].Position + point.OriginalY - edges[edges.Count - 1].Original;
                else
                {
                    int after = 1;
                    while (edges[after].FontPosition < point.FontY) { token.ThrowIfCancellationRequested(); after++; }
                    HintEdge first = edges[after - 1], last = edges[after];
                    point.Y = first.Position + Multiply(point.FontY - first.FontPosition, Divide(last.Position - first.Position, last.FontPosition - first.FontPosition));
                }
                point.Touched = true;
            }
            int contourFirst = 0;
            foreach (ushort last in ends)
            {
                int firstTouched = contourFirst;
                while (firstTouched <= last && !points[firstTouched].Touched) { token.ThrowIfCancellationRequested(); firstTouched++; }
                if (firstTouched <= last)
                {
                    int before = firstTouched;
                    do
                    {
                        int after = points[before].Next;
                        while (after != firstTouched && !points[after].Touched) { token.ThrowIfCancellationRequested(); after = points[after].Next; }
                        HintPoint first = points[before], second = points[after];
                        if (first.OriginalY > second.OriginalY) (first, second) = (second, first);
                        int scale = second.OriginalY == first.OriginalY ? 0 : Divide(second.Y - first.Y, second.OriginalY - first.OriginalY);
                        for (int i = points[before].Next; i != after; i = points[i].Next)
                        {
                            token.ThrowIfCancellationRequested();
                            ref HintPoint point = ref points[i];
                            point.Y = point.OriginalY <= first.OriginalY ? point.OriginalY + first.Y - first.OriginalY
                                : point.OriginalY >= second.OriginalY ? point.OriginalY + second.Y - second.OriginalY
                                : first.Y + Multiply(point.OriginalY - first.OriginalY, scale);
                        }
                        before = after;
                    } while (before != firstTouched);
                }
                contourFirst = last + 1;
            }
        }

    }

    /// <summary>Measures at most sixteen unscaled linked widths; scratch ownership and cancellation match glyph fitting.</summary>
    internal static LatinStemWidths MeasureStemWidths(ReadOnlySpan<OpenTypeGlyphPoint> source, ReadOnlySpan<ushort> ends, int unitsPerEm, RenderBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using RenderBudget.BudgetReservation owner = budget.Reserve(checked(32L + source.Length * 64L), SvgMemoryCategory.Geometry);
        HintPoint[] points = new HintPoint[source.Length];
        int first = 0;
        foreach (ushort last in ends)
        {
            for (int i = first; i <= last; i++)
            {
                token.ThrowIfCancellationRequested();
                OpenTypeGlyphPoint point = source[i];
                if (point.X is < short.MinValue or > short.MaxValue || point.Y is < short.MinValue or > short.MaxValue) throw Limit("The stem outline exceeds the native font-unit range.");
                points[i] = new(point.X, point.Y, point.X, point.Y, i == first ? last : i - 1, i == last ? first : i + 1, point.IsOnCurve);
            }
            first = last + 1;
        }
        ComputeDirections(points, ends, unitsPerEm, token);
        using BudgetedParserList<HintSegment> segments = new(budget, SvgMemoryCategory.Geometry, token);
        BuildSegments(points, ends, segments, unitsPerEm, token);
        int major = Orientation(points, ends, token) > 0 ? 1 : -1;
        LinkSegments(segments, major, unitsPerEm, 0, token);
        Span<int> widths = stackalloc int[16];
        int count = 0;
        for (int i = 0; i < segments.Count && count < widths.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            int link = segments[i].Link;
            if (link > i && segments[link].Link == i) widths[count++] = Math.Abs(segments[link].Position - segments[i].Position);
        }
        if (count == 0) return default;
        Span<int> measured = widths[..count];
        measured.Sort();
        // Retain the pinned native clustering arithmetic, including its absolute
        // end-index divisor, before selecting the largest surviving width.
        int current = 0, value = measured[0], lastIndex = count - 1;
        for (int index = 1; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (measured[index] - value <= unitsPerEm / 100 && index != lastIndex) continue;
            if (measured[index] - value <= unitsPerEm / 100 && index == lastIndex) index++;
            int sum = 0;
            for (int at = current; at < index; at++) { sum += measured[at]; measured[at] = 0; }
            measured[current] = sum / index;
            if (index < lastIndex) { current = index + 1; value = measured[current]; }
        }
        int maximum = 0;
        foreach (int width in measured) maximum = Math.Max(maximum, width);
        return new(maximum, measured[0] / 5);

    }

    private static long Orientation(HintPoint[] points, ReadOnlySpan<ushort> ends, CancellationToken token)
    {
        long area = 0;
        int first = 0;
        foreach (ushort last in ends)
        {
            HintPoint previous = points[last];
            for (int i = first; i <= last; i++)
            {
                token.ThrowIfCancellationRequested();
                HintPoint point = points[i];
                area = checked(area + (long)(point.FontY - previous.FontY) * (point.FontX + previous.FontX));
                previous = point;
            }
            first = last + 1;
        }
        return area;
    }

    // Near points share directional vectors while contour links remain available
    // for interpolation and outline replay.
    private static void ComputeDirections(HintPoint[] points, ReadOnlySpan<ushort> ends, int unitsPerEm, CancellationToken token)
    {
        int nearLimit = 20 * unitsPerEm / 2048, first = 0;
        foreach (ushort last in ends)
        {
            int start = first, current = start, previous = points[start].Previous;
            while (previous != first)
            {
                token.ThrowIfCancellationRequested();
                if (Math.Abs(points[current].FontX - points[previous].FontX) + Math.Abs(points[current].FontY - points[previous].FontY) >= 2 * nearLimit - 1) break;
                current = previous; previous = points[current].Previous;
            }
            start = current;
            int anchor = start, next = start, dx = 0, dy = 0;
            points[start].PreviousNonNear = points[start].NextNonNear = start;
            do
            {
                token.ThrowIfCancellationRequested();
                current = next; next = points[current].Next;
                dx += points[next].FontX - points[current].FontX; dy += points[next].FontY - points[current].FontY;
                if (Math.Abs(dx) + Math.Abs(dy) < nearLimit)
                {
                    points[next].Weak = true;
                    continue;
                }
                int direction = Direction(dx, dy);
                points[next].In = direction; points[next].PreviousNonNear = anchor;
                points[anchor].Out = direction; points[anchor].NextNonNear = next;
                for (int i = points[anchor].Next; i != next; i = points[i].Next)
                {
                    token.ThrowIfCancellationRequested();
                    points[i].In = points[i].Out = direction;
                }
                anchor = next; points[anchor].NextNonNear = start; points[start].PreviousNonNear = anchor;
                dx = dy = 0;
            } while (next != start);
            first = last + 1;
        }
        for (int i = 0; i < points.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            ref HintPoint point = ref points[i];
            if (point.Weak || point.In != 0 || point.Out != 0) continue;
            HintPoint before = points[point.PreviousNonNear], after = points[point.NextNonNear];
            int inX = point.FontX - before.FontX, inY = point.FontY - before.FontY;
            int outX = after.FontX - point.FontX, outY = after.FontY - point.FontY;
            if ((inX ^ outX) >= 0 && (inY ^ outY) >= 0)
            {
                point.Weak = true;
                points[point.PreviousNonNear].NextNonNear = point.NextNonNear;
                points[point.NextNonNear].PreviousNonNear = point.PreviousNonNear;
            }
        }
    }

    private static void BuildSegments(HintPoint[] points, ReadOnlySpan<ushort> ends, BudgetedParserList<HintSegment> segments, int unitsPerEm, CancellationToken token)
    {
        int first = 0;
        foreach (ushort last in ends)
        {
            int start = first;
            if (Math.Abs(points[first].Out) == 1 && Math.Abs(points[last].Out) == 1)
            {
                do
                {
                    token.ThrowIfCancellationRequested();
                    start = points[start].Previous;
                    if (Math.Abs(points[start].Out) != 1) { start = points[start].Next; break; }
                } while (start != first);
            }
            int index = start;
            do
            {
                token.ThrowIfCancellationRequested();
                int direction = points[index].Out;
                if (Math.Abs(direction) != 1) { index = points[index].Next; continue; }
                if (segments.Count == 1000) throw Limit("The Latin hint segment count exceeds the supported limit.");
                int begin = index, minY = points[index].FontY, maxY = minY, minX = points[index].FontX, maxX = minX;
                int minPoint = index, maxPoint = index, minOn = int.MaxValue, maxOn = int.MinValue;
                if (points[index].OnCurve) minOn = maxOn = points[index].FontX;
                do
                {
                    token.ThrowIfCancellationRequested();
                    index = points[index].Next;
                    HintPoint point = points[index];
                    minY = Math.Min(minY, point.FontY); maxY = Math.Max(maxY, point.FontY);
                    if (point.FontX < minX) { minX = point.FontX; minPoint = index; }
                    if (point.FontX > maxX) { maxX = point.FontX; maxPoint = index; }
                    if (point.OnCurve) { minOn = Math.Min(minOn, point.FontX); maxOn = Math.Max(maxOn, point.FontX); }
                } while (index != start && points[index].Out == direction);
                segments.Add(new(begin, index, direction, (minY + maxY) >> 1, (maxY - minY) >> 1, minX, maxX,
                    !(minOn != int.MaxValue && maxOn - minOn > unitsPerEm / 14) && (!points[minPoint].OnCurve || !points[maxPoint].OnCurve)));
            } while (index != start);
            first = last + 1;
        }
    }

    private static void LinkSegments(BudgetedParserList<HintSegment> segments, int major, int unitsPerEm, int maximumStemWidth, CancellationToken token)
    {
        int threshold = 8 * unitsPerEm / 2048, lengthScore = 6000 * unitsPerEm / 2048;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].Direction != major) continue;
            for (int j = 0; j < segments.Count; j++)
            {
                token.ThrowIfCancellationRequested();
                HintSegment first = segments[i], second = segments[j];
                int length = Math.Min(first.MaxX, second.MaxX) - Math.Max(first.MinX, second.MinX);
                if (first.Direction != -second.Direction || second.Position <= first.Position || length < Math.Max(1, threshold)) continue;
                int distance = second.Position - first.Position;
                int demerit = distance;
                if (maximumStemWidth > 0)
                {
                    int delta = (distance << 10) / maximumStemWidth - 1024;
                    demerit = delta > 10000 ? 32000 : delta > 0 ? delta * delta / 3000 : 0;
                }
                int score = demerit + lengthScore / length;
                if (score < first.Score) { first.Score = score; first.Link = j; segments[i] = first; }
                if (score < second.Score) { second.Score = score; second.Link = i; segments[j] = second; }
            }
        }
        for (int i = 0; i < segments.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            HintSegment segment = segments[i];
            if (segment.Link >= 0 && segments[segment.Link].Link != i) { segment.Serif = segments[segment.Link].Link; segment.Link = -1; segments[i] = segment; }
        }
    }

    private static int Direction(int x, int y) => Math.Abs(x) > 14 * Math.Abs(y) ? Math.Sign(x) : Math.Abs(y) > 14 * Math.Abs(x) ? 2 * Math.Sign(y) : 0;
    private static int Round(int value) => (value + 32) & ~63;
    private static int Multiply(int first, int second) => CheckedSigned((Math.Abs((long)first * second) + 32768) >> 16, (first < 0) ^ (second < 0));
    private static int Divide(int first, int second) => MultiplyDivide(first, 65536, second);
    private static int MultiplyDivide(int first, int second, int divisor)
    {
        if (divisor == 0) throw Limit("Latin hint scaling has a zero denominator.");
        long product = (long)first * second, denominator = Math.Abs((long)divisor);
        return CheckedSigned((Math.Abs(product) + (denominator >> 1)) / denominator, (product < 0) ^ (divisor < 0));
    }
    private static int CheckedSigned(long value, bool negative)
    {
        if (value > int.MaxValue) throw Limit("Latin hint coordinates exceed the native fixed-point range.");
        return negative ? -(int)value : (int)value;
    }
    private static SvgException Limit(string message) => new(SvgErrorCode.LimitExceeded, message);

    private struct HintPoint(int fontX, int fontY, int x, int y, int previous, int next, bool onCurve)
    {
        internal int FontX = fontX, FontY = fontY, X = x, Y = y, OriginalY = y, Previous = previous, Next = next, In, Out;
        internal int PreviousNonNear = previous, NextNonNear = next;
        internal bool OnCurve = onCurve, Weak, Touched;
    }
    private struct HintSegment(int first, int last, int direction, int position, int delta, int minimum, int maximum, bool round)
    {
        internal int First = first, Last = last, Direction = direction, Position = position, Delta = delta, MinX = minimum, MaxX = maximum, Score = 32000, Link = -1, Serif = -1, Edge = -1;
        internal bool Round = round;
    }
    private struct HintEdge(int fontPosition, int original, int direction, bool round)
    {
        internal int FontPosition = fontPosition, Original = original, Position = original, Direction = direction, Link = -1, Serif = -1;
        internal bool Round = round, Done;
    }
}
