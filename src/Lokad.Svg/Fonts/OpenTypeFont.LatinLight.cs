namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal static partial class OpenTypeFont
{
    private static readonly string[] LatinAlignmentSamples = ["THEZOCQS", "HEZLOCUS", "fijkdbh", "uvxzoesc", "nrxzoesc", "pqgjy"];
    /// <summary>Returns owned Latin light-hinted contours in 26.6 device coordinates, borrowing the unchanged font program.</summary>
    /// <remarks>Metric glyphs and the rendered glyph use the existing charged decoder. All temporary points, topology and returned segments retain their reservations until disposal. No font or native service is acquired here.</remarks>
    internal static OpenTypeGlyphContourStorage GetLatinLightContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, RenderBudget budget, ParserWorkCounter work, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(deviceSize) || deviceSize <= 0 || deviceSize > 32767) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted font size exceeds the native coordinate range.");
        work.Consume(cancellationToken);
        using OpenTypeSimpleGlyphStructureStorage structure = GetGlyphStructure(bytes, glyphIndex, metrics, budget, work, cancellationToken, true);
        if (structure.CompositeHeader is not null)
        {
            using OpenTypeGlyphPointOutline outline = GetGlyphPointOutline(bytes, glyphIndex, metrics, null, budget, work, cancellationToken);
            Span<LatinAlignmentZone> compositeZones = stackalloc LatinAlignmentZone[6];
            int compositeZoneCount = GetLatinAlignmentZones(bytes, metrics, compositeZones, budget, work, cancellationToken);
            LatinLightHinting.Hint(outline.Points, outline.Ends, compositeZones[..compositeZoneCount], metrics.UnitsPerEm, deviceSize, budget, work, cancellationToken);
            using OpenTypeSegmentAccumulator compositeSegments = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, work, cancellationToken);
            BuildQuadraticContours(outline.Points, outline.Ends, compositeSegments, true, true, work, cancellationToken);
            return compositeSegments.Detach();
        }
        if (structure.Value.ContourEndpoints.Length == 0) return new([], [], null, null);
        using OpenTypeGlyphPointStorage points = GetSimpleGlyphPoints(bytes, structure.Value, budget, work, cancellationToken);
        Span<LatinAlignmentZone> zones = stackalloc LatinAlignmentZone[6];
        int zoneCount = GetLatinAlignmentZones(bytes, metrics, zones, budget, work, cancellationToken);
        LatinLightHinting.Hint(points.Points, structure.Value.ContourEndpoints, zones[..zoneCount], metrics.UnitsPerEm, deviceSize, budget, work, cancellationToken);
        using var segments = new OpenTypeSegmentAccumulator(budget, GlyphDecoderBudgetPhase.SimpleSegmentCapacity, GlyphDecoderBudgetPhase.SimpleSegmentExact, GlyphDecoderBudgetPhase.SimpleContourBoundaryCapacity, GlyphDecoderBudgetPhase.SimpleContourBoundaryExact, work, cancellationToken);
        BuildQuadraticContours(points.Points, structure.Value.ContourEndpoints, segments, true, true, work, cancellationToken);
        return segments.Detach();
    }

    /// <summary>Samples at most six Latin alignment zones into caller-owned storage, borrowing one face and charging each decoded glyph.</summary>
    internal static int GetLatinAlignmentZones(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, Span<LatinAlignmentZone> zones, RenderBudget budget, ParserWorkCounter work, CancellationToken cancellationToken)
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
                work.Consume(cancellationToken);
                ushort sampleGlyph = GetGlyphIndex(bytes, scalar, metrics, work, cancellationToken);
                if (sampleGlyph == 0) continue;
                OpenTypeGlyphHeader? header = GetGlyphHeader(bytes, sampleGlyph, metrics, work, cancellationToken);
                if (header is null || header.Value.IsComposite || header.Value.ContourCount == 0) continue;
                using OpenTypeSimpleGlyphStructureStorage sampleStructure = GetSimpleGlyphStructure(bytes, sampleGlyph, metrics, budget, work, cancellationToken);
                using OpenTypeGlyphPointStorage samplePoints = GetSimpleGlyphPoints(bytes, sampleStructure.Value, budget, work, cancellationToken);
                if (!LatinLightHinting.FindAlignment(samplePoints.Points, sampleStructure.Value.ContourEndpoints, top, metrics.UnitsPerEm, work, cancellationToken, out int position, out bool round, out int maximum, out int minimum)) continue;
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

/// <summary>Applies bounded vertical Latin light fitting to caller-owned decoded points.</summary>
/// <remarks>Adapts Fontations/Skrifa 0.43.2's Latin topology, alignment and interpolation. See THIRD_PARTY_NOTICES.txt. The font program is never modified; all scratch allocations are charged before allocation and disposed on every exit.</remarks>
internal static class LatinLightHinting
{
    internal static bool FindAlignment(ReadOnlySpan<OpenTypeGlyphPoint> points, ReadOnlySpan<ushort> ends, bool top, int unitsPerEm, ParserWorkCounter work, CancellationToken token, out int position, out bool round, out int maximum, out int minimum)
    {
        position = top ? int.MinValue : int.MaxValue; round = false;
        maximum = int.MinValue; minimum = int.MaxValue;
        int first = 0, chosenFirst = 0, chosenLast = 0, chosen = -1;
        foreach (ushort last in ends)
        {
            for (int i = first; i <= last; i++)
            {
                work.Consume(token);
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
            work.Consume(token);
            int distance = Math.Abs(points[i].Y - position);
            if (distance > 5 && Math.Abs(points[i].X - points[chosen].X) <= 20 * distance) break;
            left = i;
            if (points[i].IsOnCurve) { onLeft = i; if (onRight < 0) onRight = i; }
        }
        for (int i = Next(chosen); i != chosen; i = Next(i))
        {
            work.Consume(token);
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

    internal static void Hint(Span<OpenTypeGlyphPoint> source, ReadOnlySpan<ushort> ends, ReadOnlySpan<LatinAlignmentZone> zones, int unitsPerEm, double size, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        if (!double.IsFinite(size) || size <= 0 || size > 32767) throw Limit("The hinted font size exceeds the native coordinate range.");
        work.Consume(token);
        int xScale = Divide((int)((float)size * 64), unitsPerEm), yScale = xScale;
        foreach (LatinAlignmentZone zone in zones)
        {
            work.Consume(token);
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
                work.Consume(token);
                OpenTypeGlyphPoint point = source[i];
                if (point.X is < short.MinValue or > short.MaxValue || point.Y is < short.MinValue or > short.MaxValue) throw Limit("The hinted outline exceeds the native font-unit range.");
                int previous = i == first ? last : i - 1, next = i == last ? first : i + 1;
                points[i] = new(point.X, point.Y, Multiply(point.X, xScale), Multiply(point.Y, yScale), previous, next, point.IsOnCurve);
                points[i].In = Direction(point.X - source[previous].X, point.Y - source[previous].Y);
                points[i].Out = Direction(source[next].X - point.X, source[next].Y - point.Y);
            }
            first = last + 1;
        }
        for (int i = 0; i < points.Length; i++)
        {
            work.Consume(token);
            ref HintPoint point = ref points[i];
            HintPoint previous = points[point.Previous], next = points[point.Next];
            int inX = point.FontX - previous.FontX, inY = point.FontY - previous.FontY;
            int outX = next.FontX - point.FontX, outY = next.FontY - point.FontY;
            point.Weak = !point.OnCurve || (point.In == point.Out && point.In != 0) || (point.In != 0 && point.In == -point.Out)
                || (point.In == 0 && point.Out == 0 && (((inX ^ outX) >= 0 && (inY ^ outY) >= 0) || IsFlat(inX, inY, outX, outY)));
        }
        if (zones.Length != 0 && yScale != 0)
        {
            using BudgetedParserList<HintSegment> segments = new(budget, SvgMemoryCategory.Geometry, work, token);
            BuildSegments(points, ends, segments, unitsPerEm, work, token);
            int major = Orientation(points, ends, work, token) > 0 ? 1 : -1;
            LinkSegments(segments, major, unitsPerEm, work, token);
            using BudgetedParserList<HintEdge> edges = new(budget, SvgMemoryCategory.Geometry, work, token);
            BuildEdges(segments, edges, yScale, work, token);
            FitEdges(edges, zones, yScale, unitsPerEm, major, work, token);
            AlignPoints(points, ends, segments, edges, work, token);
        }
        for (int i = 0; i < source.Length; i++)
        {
            work.Consume(token);
            source[i] = new(points[i].X, points[i].Y, points[i].OnCurve);
        }

        static bool IsFlat(int inX, int inY, int outX, int outY)
        {
            int combined = Hypot(inX + outX, inY + outY);
            return Hypot(inX, inY) + Hypot(outX, outY) - combined < (combined >> 4);
            static int Hypot(int x, int y) { x = Math.Abs(x); y = Math.Abs(y); return x > y ? x + (3 * y >> 3) : y + (3 * x >> 3); }
        }


        static void BuildSegments(HintPoint[] points, ReadOnlySpan<ushort> ends, BudgetedParserList<HintSegment> segments, int unitsPerEm, ParserWorkCounter work, CancellationToken token)
        {
            int first = 0;
            foreach (ushort last in ends)
            {
                int start = first;
                while (start <= last && (Math.Abs(points[start].Out) != 1 || points[start].In == points[start].Out)) { work.Consume(token); start++; }
                if (start > last) { first = last + 1; continue; }
                int index = start;
                do
                {
                    work.Consume(token);
                    int direction = points[index].Out;
                    if (Math.Abs(direction) != 1) { index = points[index].Next; continue; }
                    if (segments.Count == 1000) throw Limit("The Latin hint segment count exceeds the supported limit.");
                    int begin = index, minY = points[index].FontY, maxY = minY, minX = points[index].FontX, maxX = minX;
                    int minPoint = index, maxPoint = index, minOn = int.MaxValue, maxOn = int.MinValue;
                    if (points[index].OnCurve) minOn = maxOn = points[index].FontX;
                    do
                    {
                        work.Consume(token);
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

        static long Orientation(HintPoint[] points, ReadOnlySpan<ushort> ends, ParserWorkCounter work, CancellationToken token)
        {
            long area = 0;
            int first = 0;
            foreach (ushort last in ends)
            {
                HintPoint previous = points[last];
                for (int i = first; i <= last; i++)
                {
                    work.Consume(token);
                    HintPoint point = points[i];
                    area = checked(area + (long)(point.FontY - previous.FontY) * (point.FontX + previous.FontX));
                    previous = point;
                }
                first = last + 1;
            }
            return area;
        }

        static void LinkSegments(BudgetedParserList<HintSegment> segments, int major, int unitsPerEm, ParserWorkCounter work, CancellationToken token)
        {
            int threshold = 8 * unitsPerEm / 2048, lengthScore = 6000 * unitsPerEm / 2048;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].Direction != major) continue;
                for (int j = 0; j < segments.Count; j++)
                {
                    work.Consume(token);
                    HintSegment first = segments[i], second = segments[j];
                    int length = Math.Min(first.MaxX, second.MaxX) - Math.Max(first.MinX, second.MinX);
                    if (first.Direction != -second.Direction || second.Position <= first.Position || length < Math.Max(1, threshold)) continue;
                    int score = second.Position - first.Position + lengthScore / length;
                    if (score < first.Score) { first.Score = score; first.Link = j; segments[i] = first; }
                    if (score < second.Score) { second.Score = score; second.Link = i; segments[j] = second; }
                }
            }
            for (int i = 0; i < segments.Count; i++)
            {
                work.Consume(token);
                HintSegment segment = segments[i];
                if (segment.Link >= 0 && segments[segment.Link].Link != i) { segment.Serif = segments[segment.Link].Link; segment.Link = -1; segments[i] = segment; }
            }
        }

        static void BuildEdges(BudgetedParserList<HintSegment> segments, BudgetedParserList<HintEdge> edges, int scale, ParserWorkCounter work, CancellationToken token)
        {
            int threshold = Math.Max(1, Divide(16, scale)), maxDelta = Divide(32, scale);
            for (int i = 0; i < segments.Count; i++)
            {
                work.Consume(token);
                HintSegment segment = segments[i];
                if (segment.Delta > maxDelta) continue;
                int match = -1;
                for (int j = 0; j < edges.Count; j++)
                {
                    work.Consume(token);
                    if (edges[j].Direction == segment.Direction && Math.Abs(edges[j].FontPosition - segment.Position) < threshold) { match = j; break; }
                }
                if (match < 0) { match = edges.Count; edges.Add(new(segment.Position, Multiply(segment.Position, scale), segment.Direction, segment.Round)); }
                segment.Edge = match; segments[i] = segment;
            }
            // Sort while updating the bounded segment indices; no comparer or index-map allocation.
            for (int i = 1; i < edges.Count; i++)
            for (int j = i; j > 0 && edges[j].FontPosition < edges[j - 1].FontPosition; j--)
            {
                work.Consume(token);
                HintEdge temp = edges[j]; edges[j] = edges[j - 1]; edges[j - 1] = temp;
                for (int k = 0; k < segments.Count; k++)
                {
                    work.Consume(token);
                    HintSegment segment = segments[k];
                    if (segment.Edge == j) segment.Edge = j - 1;
                    else if (segment.Edge == j - 1) segment.Edge = j;
                    segments[k] = segment;
                }
            }
            for (int i = 0; i < segments.Count; i++)
            {
                work.Consume(token);
                HintSegment segment = segments[i];
                if (segment.Edge < 0) continue;
                HintEdge edge = edges[segment.Edge];
                if (segment.Link >= 0 && segments[segment.Link].Edge >= 0) edge.Link = segments[segment.Link].Edge;
                if (segment.Serif >= 0 && segments[segment.Serif].Edge >= 0) edge.Serif = segments[segment.Serif].Edge;
                edges[segment.Edge] = edge;
            }
        }

        static void FitEdges(BudgetedParserList<HintEdge> edges, ReadOnlySpan<LatinAlignmentZone> zones, int scale, int unitsPerEm, int major, ParserWorkCounter work, CancellationToken token)
        {
            int anchor = -1;
            for (int i = 0; i < edges.Count; i++)
            {
                HintEdge edge = edges[i];
                int best = Math.Min(32, Multiply(unitsPerEm / 40, scale));
                foreach (LatinAlignmentZone zone in zones)
                {
                    work.Consume(token);
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
                work.Consume(token);
                HintEdge edge = edges[i];
                if (!edge.Done || edge.Link < 0 || edges[edge.Link].Done) continue;
                HintEdge linked = edges[edge.Link];
                linked.Position = edge.Position + linked.Original - edge.Original; linked.Done = true; edges[edge.Link] = linked;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                work.Consume(token);
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
                edge.Done = true; edges[i] = edge;
                if (anchor < 0) anchor = i;
            }
            for (int i = 0; i < edges.Count; i++)
            {
                work.Consume(token);
                HintEdge edge = edges[i];
                if (edge.Done) continue;
                if (edge.Serif >= 0 && Math.Abs(edge.Original - edges[edge.Serif].Original) < 80)
                    edge.Position = edges[edge.Serif].Position + edge.Original - edges[edge.Serif].Original;
                else if (anchor < 0) { edge.Position = Round(edge.Original); anchor = i; }
                else
                {
                    int before = i - 1, after = i + 1;
                    while (before >= 0 && !edges[before].Done) { work.Consume(token); before--; }
                    while (after < edges.Count && !edges[after].Done) { work.Consume(token); after++; }
                    edge.Position = before >= 0 && after < edges.Count && edges[before].Original != edges[after].Original
                        ? edges[before].Position + MultiplyDivide(edge.Original - edges[before].Original, edges[after].Position - edges[before].Position, edges[after].Original - edges[before].Original)
                        : edges[anchor].Position + ((edge.Original - edges[anchor].Original + 16) & ~31);
                }
                if (i > 0) edge.Position = Math.Max(edge.Position, edges[i - 1].Position);
                edge.Done = true; edges[i] = edge;
            }
        }

        static void AlignPoints(HintPoint[] points, ReadOnlySpan<ushort> ends, BudgetedParserList<HintSegment> segments, BudgetedParserList<HintEdge> edges, ParserWorkCounter work, CancellationToken token)
        {
            if (edges.Count == 0) return;
            foreach (HintSegment segment in segments.AsSpan())
            {
                if (segment.Edge < 0) continue;
                int i = segment.First;
                while (true)
                {
                    work.Consume(token);
                    points[i].Y = edges[segment.Edge].Position; points[i].Touched = true;
                    if (i == segment.Last) break;
                    i = points[i].Next;
                }
            }
            for (int i = 0; i < points.Length; i++)
            {
                work.Consume(token);
                ref HintPoint point = ref points[i];
                if (point.Touched || point.Weak) continue;
                if (point.FontY <= edges[0].FontPosition) point.Y = edges[0].Position + point.OriginalY - edges[0].Original;
                else if (point.FontY >= edges[edges.Count - 1].FontPosition) point.Y = edges[edges.Count - 1].Position + point.OriginalY - edges[edges.Count - 1].Original;
                else
                {
                    int after = 1;
                    while (edges[after].FontPosition < point.FontY) { work.Consume(token); after++; }
                    HintEdge first = edges[after - 1], last = edges[after];
                    point.Y = first.Position + Multiply(point.FontY - first.FontPosition, Divide(last.Position - first.Position, last.FontPosition - first.FontPosition));
                }
                point.Touched = true;
            }
            int contourFirst = 0;
            foreach (ushort last in ends)
            {
                int firstTouched = contourFirst;
                while (firstTouched <= last && !points[firstTouched].Touched) { work.Consume(token); firstTouched++; }
                if (firstTouched <= last)
                {
                    int before = firstTouched;
                    do
                    {
                        int after = points[before].Next;
                        while (after != firstTouched && !points[after].Touched) { work.Consume(token); after = points[after].Next; }
                        HintPoint first = points[before], second = points[after];
                        if (first.OriginalY > second.OriginalY) (first, second) = (second, first);
                        int scale = second.OriginalY == first.OriginalY ? 0 : Divide(second.Y - first.Y, second.OriginalY - first.OriginalY);
                        for (int i = points[before].Next; i != after; i = points[i].Next)
                        {
                            work.Consume(token);
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
