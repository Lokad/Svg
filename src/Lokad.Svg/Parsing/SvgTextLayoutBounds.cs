// Adapts Blink source fragment ink/line rectangles and gfx float rectangle union.
// Copyright 2017 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
using System.Runtime.CompilerServices;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Measures source text paint boxes before stroke fringes and device paint placement.</summary>
/// <remarks>Fonts and runs remain borrowed. A conversion-local metric cache and temporary fitted
/// contours/scans reserve storage before allocation; all visits charge work and observe cancellation.
/// Disposal releases scratch on success or failure. No font or output-dependent cache is shared.</remarks>
internal static class SvgTextLayoutBounds
{
    private readonly record struct MetricKey(SvgPreparedFont Font, ushort Glyph, float Size);

    internal static bool UsesFittedMetrics(SvgPreparedFont font, float size)
        => size is > 0 and <= 32767 && font.Metrics.HasCompleteTrueTypeProfile;

    internal static SvgRectangle Measure(SvgTextRun run, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgAffineTransform placement,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        if (run.Glyphs.IsEmpty) return default;
        using MetricCache cache = MetricCache.Create(budget);
        return Measure(run, paths, placement, cache, work, token);
    }

    internal static SvgRectangle Measure(SvgTextRun run, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgAffineTransform placement,
        MetricCache cache, ParserWorkCounter work, CancellationToken token)
        => cache.Measure(run, paths, placement, work, token);

    /// <summary>Owns fitted metric reuse during one parser state; fonts remain borrowed.</summary>
    /// <remarks>The parser thread owns access and disposal. Values retain no contour or bitmap;
    /// construction and entry growth reserve storage before allocation. Fitting charges the
    /// caller's work/token. Disposal releases all reservations on success or failure.</remarks>
    internal sealed class MetricCache : IDisposable
    {
        private readonly RenderBudget.BudgetReservation _owner;
        private readonly BudgetedParserDictionary<MetricKey, SourceBox> _items;
        private readonly RenderBudget _budget;

        private MetricCache(RenderBudget budget, RenderBudget.BudgetReservation owner)
        {
            _budget = budget; _owner = owner;
            _items = new(budget, SvgMemoryCategory.Parser, 64 + Unsafe.SizeOf<MetricKey>() + Unsafe.SizeOf<SourceBox>());
        }

        internal static MetricCache Create(RenderBudget budget)
        {
            RenderBudget.BudgetReservation owner = budget.Reserve(256, SvgMemoryCategory.Parser);
            try { return new(budget, owner); }
            catch { owner.Dispose(); throw; }
        }

        public void Dispose() { _items.Dispose(); _owner.Dispose(); }

        internal SvgRectangle Measure(SvgTextRun run, ReadOnlySpan<SvgPlacedTextGlyph> paths, SvgAffineTransform placement,
            ParserWorkCounter work, CancellationToken token)
        {
            if (run.Glyphs.IsEmpty) return default;
            RenderBudget budget = _budget;
            BudgetedParserDictionary<MetricKey, SourceBox> metrics = _items;
            float scale = (float)(run.DeviceMetrics?.Scale ?? 1), inverseScale = 1 / scale;
            if (!paths.IsEmpty && !placement.TryInvert(out _))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular text paint placement is not implemented.");
            SourceBox result = default;
            if (!paths.IsEmpty)
            {
                for (int index = 0; index < run.Glyphs.Length; index++)
                {
                    work.Consume(token);
                    SvgPlacedTextGlyph path = paths[index];
                    if (!path.Visible) continue;
                    SvgAffineTransform map = path.SourceMap;
                    map = map with { E = map.E * scale, F = map.F * scale };
                    float inlineWidth = (float)(run.Glyphs[index].Advance * scale);
                    result = result.Union(Fragment(index, index + 1, inlineWidth, default).Map(map, work, token).Scale(inverseScale, inverseScale));
                }
            }
            else if (!run.PaintFragments.IsEmpty)
            {
                foreach (SvgTextPaintFragment fragment in run.PaintFragments)
                {
                    work.Consume(token);
                    SvgPositionedGlyph first = run.Glyphs[Math.Min(fragment.Start, run.Glyphs.Length - 1)];
                    SvgDevicePoint origin = fragment.SourceOrigin ?? new((float)first.X * scale, (float)first.Y * scale);
                    float inlineWidth = fragment.SourceWidth ?? MathF.Ceiling((float)(fragment.Width * scale) * 64) / 64;
                    SourceBox box = Fragment(fragment.Start, fragment.End, inlineWidth, origin);
                    double angle = run.GetGlyphRotation(fragment.Start);
                    if (angle != 0)
                    {
                        SvgAffineTransform map = SvgAffineTransform.FromTranslation(origin.X, origin.Y)
                            .ComposeChecked(SvgAffineTransform.FromRotationDegrees(angle))
                            .ComposeChecked(SvgAffineTransform.FromTranslation(-origin.X, -origin.Y));
                        box = box.Map(map, work, token);
                    }
                    result = result.Union(box.Scale(inverseScale, inverseScale));
                }
            }
            else
            {
                for (int index = 0; index < run.Glyphs.Length; index++)
                {
                    work.Consume(token);
                    SvgPositionedGlyph glyph = run.Glyphs[index];
                    float inlineWidth = MathF.Ceiling((float)(glyph.Advance * scale) * 64) / 64;
                    result = result.Union(Fragment(index, index + 1, inlineWidth, new((float)glyph.X * scale, (float)glyph.Y * scale)).Scale(inverseScale, inverseScale));
                }
            }
            return new(result.X, result.Y, result.Width, result.Height, default, false, null);

            SourceBox Fragment(int start, int end, float width, SvgDevicePoint origin)
            {
                SvgGlyphFontStyle primary = run.GetGlyphFont(Math.Min(start, run.Glyphs.Length - 1));
                float size = SvgGlyphDeviceMetrics.GetEffectiveFontSize(primary.FontSize * scale);
                OpenTypeFontMetrics font = primary.Font.Metrics;
                float ascent = OperatingSystem.IsWindows() ? size * font.LineMetrics.WindowsAscender / font.UnitsPerEm : font.LineMetrics.Ascender * (size / font.UnitsPerEm);
                float descent = OperatingSystem.IsWindows() ? size * font.LineMetrics.WindowsDescender / font.UnitsPerEm : -font.LineMetrics.Descender * (size / font.UnitsPerEm);
                float above = MathF.Floor(ascent + .5f), below = MathF.Floor(descent + .5f);
                float x = (float)origin.X, y = (float)origin.Y - above;
                SourceBox layout = new(x, y, width, above + below), ink = default;
                SvgPositionedGlyph first = run.Glyphs[Math.Min(start, run.Glyphs.Length - 1)];
                for (int index = start; index < end; index++)
                {
                    work.Consume(token);
                    SvgPositionedGlyph glyph = run.Glyphs[index];
                    SvgGlyphFontStyle face = run.GetGlyphFont(index);
                    float deviceSize = SvgGlyphDeviceMetrics.GetEffectiveFontSize(face.FontSize * scale);
                    MetricKey key = new(face.Font, glyph.GlyphIndex, deviceSize);
                    if (!metrics.TryGetValue(key, out SourceBox bounds))
                    {
                        bounds = Ink(face.Font, glyph.GlyphIndex, deviceSize, budget, work, token);
                        metrics.TryAdd(key, bounds);
                    }
                    float relativeX = run.GlyphStretch == 0 ? 0 : (float)((glyph.X - first.X) * scale / run.GlyphStretch);
                    ink = ink.Union(bounds.Offset(relativeX, (float)((glyph.Y - first.Y) * scale)));
                }
                // Join scaled ink and the source fragment rectangle before character
                // transforms and component-wise inverse font scaling.
                ink = ink.Offset(0, above).Scale((float)run.GlyphStretch, 1).Offset(x, y);
                return ink.Union(layout);
            }

            static SourceBox Ink(SvgPreparedFont font, ushort glyph, float size, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
            {
                work.Consume(token);
                if (size == 0) return default;
                // Preserve the established generic-font and oversized-strike routes.
                if (!UsesFittedMetrics(font, size))
                {
                    if (OpenTypeFont.GetGlyphHeader(font.Bytes, glyph, font.Metrics, work, token) is not {} header) return default;
                    float unit = size / font.UnitsPerEm;
                    return new(header.XMinimum * unit, -header.YMaximum * unit,
                        (header.XMaximum - header.XMinimum) * unit, (header.YMaximum - header.YMinimum) * unit);
                }
                using SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(font, new(glyph, 0, 0, 0), size, budget);
                if (OperatingSystem.IsLinux())
                {
                    using SvgAuthoredPath path = source.CreateLatinLightPath(1, 0, 1, 0, budget, work, token);
                    if (path.Commands.IsEmpty) return default;
                    SvgRectangle bounds = SvgPathBounds.Compute(path.Commands, SvgAffineTransform.Identity, work, token);
                    float left = (float)Math.Floor(bounds.X), top = (float)Math.Floor(bounds.Y);
                    return new(left, top, (float)Math.Ceiling(bounds.X + bounds.Width) - left, (float)Math.Ceiling(bounds.Y + bounds.Height) - top);
                }
                using OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(1, 0, 1, 0, budget, work, token);
                if (contours.Segments.Length == 0) return default;
                double minimumX = double.PositiveInfinity, minimumY = double.PositiveInfinity;
                double maximumX = double.NegativeInfinity, maximumY = double.NegativeInfinity;
                foreach (OpenTypeQuadraticSegment segment in contours.Segments)
                {
                    work.Consume(token);
                    Include(segment.Start); Include(segment.Control); Include(segment.End);
                }
                int leftEdge = Edge(Math.Floor(minimumX - 1d / 3)), rightEdge = Edge(Math.Ceiling(maximumX + 1d / 3 + (contours.ExtendWindowsLcdSpans ? 1d / 6 : 0)));
                int topEdge = Edge(Math.Floor(minimumY)), bottomEdge = Edge(Math.Ceiling(maximumY));
                if (contours.Segments.Length > int.MaxValue / 2) throw new SvgException(SvgErrorCode.LimitExceeded, "Text metric crossing storage exceeds its bound.");
                int crossingCount = contours.Segments.Length * 2;
                using RenderBudget.BudgetReservation crossingOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphLcdCoverage.Crossing>(crossingCount), SvgMemoryCategory.Temporary);
                SvgGlyphLcdCoverage.Crossing[] crossings = new SvgGlyphLcdCoverage.Crossing[crossingCount];
                SvgGlyphLcdCoverage.DropoutBounds limits = SvgGlyphLcdCoverage.PrepareBounds(contours.Segments, contours.SmartDropouts,
                    contours.SimpleDropouts, contours.ExcludeDropoutStubs, contours.WindowsLcdMode, work, token) with { ExtendRight = contours.ExtendWindowsLcdSpans };
                using SvgGlyphLcdCoverage.ScanStorage? scans = SvgGlyphLcdCoverage.ScanStorage.TryCreate(contours.Segments, contours.WindowsLcdMode, budget, work, token);
                Span<int> counts = stackalloc int[10];
                while (leftEdge < rightEdge && !HasInk(leftEdge, leftEdge + 1, topEdge, bottomEdge, scans, contours.Segments, crossings, counts, limits, work, token)) leftEdge++;
                while (rightEdge > leftEdge && !HasInk(rightEdge - 1, rightEdge, topEdge, bottomEdge, scans, contours.Segments, crossings, counts, limits, work, token)) rightEdge--;
                if (leftEdge == rightEdge) return default;
                while (topEdge < bottomEdge && !HasInk(leftEdge, rightEdge, topEdge, topEdge + 1, scans, contours.Segments, crossings, counts, limits, work, token)) topEdge++;
                while (bottomEdge > topEdge && !HasInk(leftEdge, rightEdge, bottomEdge - 1, bottomEdge, scans, contours.Segments, crossings, counts, limits, work, token)) bottomEdge--;
                return new(leftEdge, topEdge, rightEdge - leftEdge, bottomEdge - topEdge);

                void Include(OpenTypeOutlinePoint point)
                { minimumX = Math.Min(minimumX, point.X); minimumY = Math.Min(minimumY, point.Y); maximumX = Math.Max(maximumX, point.X); maximumY = Math.Max(maximumY, point.Y); }

                static int Edge(double value) => double.IsFinite(value) && value >= int.MinValue && value <= int.MaxValue
                    ? (int)value : throw new SvgException(SvgErrorCode.LimitExceeded, "Text metric bounds exceed their signed range.");
            }

            static bool HasInk(int left, int right, int top, int bottom, SvgGlyphLcdCoverage.ScanStorage? storage,
                ReadOnlySpan<OpenTypeQuadraticSegment> segments, Span<SvgGlyphLcdCoverage.Crossing> scratch, Span<int> counts,
                SvgGlyphLcdCoverage.DropoutBounds bounds, ParserWorkCounter work, CancellationToken token)
            {
                for (int y = top; y < bottom; y++)
                {
                    work.Consume(token);
                    SvgGlyphLcdCoverage.ScanRow scans = storage is null ? default : storage.PrepareRow(segments, y, counts, work, token);
                    for (int x = left; x < right; x++)
                    {
                        SvgGlyphRgbCoverage coverage = scans.IsPrepared ? SvgGlyphLcdCoverage.GetCoverage(scans, x, scratch, bounds, work, token)
                            : SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, bounds, work, token);
                        if (coverage.Red != 0 || coverage.Green != 0 || coverage.Blue != 0) return true;
                    }
                }
                return false;
            }
        }

    }

    // Scale native rectangle components, rather than double endpoint differences.
    private readonly record struct SourceBox(float X, float Y, float Width, float Height)
    {
        private bool IsEmpty => Width <= 0 || Height <= 0;
        internal SourceBox Union(SourceBox other)
        {
            if (IsEmpty) return other;
            if (other.IsEmpty) return this;
            float x = MathF.Min(X, other.X), y = MathF.Min(Y, other.Y);
            float right = MathF.Max(X + Width, other.X + other.Width), bottom = MathF.Max(Y + Height, other.Y + other.Height);
            float width = right - x, height = bottom - y;
            // Native rectangle union expands one scalar step when subtraction
            // rounds its exclusive edge inward.
            if (x + width < right && width < float.MaxValue) width = MathF.BitIncrement(width);
            if (y + height < bottom && height < float.MaxValue) height = MathF.BitIncrement(height);
            return new(x, y, width, height);
        }
        internal SourceBox Offset(float x, float y) => IsEmpty ? this : new(X + x, Y + y, Width, Height);
        internal SourceBox Scale(float x, float y) => IsEmpty ? this : new(X * x, Y * y, Width * x, Height * y);
        internal SourceBox Map(SvgAffineTransform map, ParserWorkCounter work, CancellationToken token)
        {
            if (IsEmpty) return this;
            float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                work.Consume(token);
                SvgDevicePoint point = map.Apply((corner & 1) == 0 ? X : X + Width, (corner & 2) == 0 ? Y : Y + Height);
                float x = (float)point.X, y = (float)point.Y;
                if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Text layout bounds exceed the numeric range.");
                left = MathF.Min(left, x); top = MathF.Min(top, y); right = MathF.Max(right, x); bottom = MathF.Max(bottom, y);
            }
            return new(left, top, right - left, bottom - top);
        }
    }
}
