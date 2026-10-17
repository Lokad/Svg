namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

/// <summary>Builds one bounded simple horizontal text run from prepared caller-owned font data.</summary>
internal static class SvgTextRunBuilder
{
    internal static SvgTextRun Create(SvgTextStyle style, string text, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
        => Create(style, text, snapshot, budget, null, cancellationToken);

    /// <summary>Builds one text run while sharing its font-table work with the containing conversion.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, SvgFontSnapshot snapshot, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => Create(style, text, [], snapshot, budget, workCounter, cancellationToken);

    /// <summary>Builds one text run with normalized UTF-16 letter-spacing intervals retained by direct-text extraction.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, SvgFontSnapshot snapshot, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, [], snapshot, budget, workCounter, cancellationToken);

    /// <summary>Builds one text run with normalized letter-spacing intervals and scalar span offsets retained by direct-text extraction.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, SvgFontSnapshot snapshot, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, positionAdjustments, null, snapshot, budget, workCounter, cancellationToken);

    /// <summary>Builds a run using the already resolved output scale for platform glyph advances and SVG fragments.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, positionAdjustments, [], null, deviceMetrics, snapshot, budget, workCounter, cancellationToken);

    /// <summary>Resolves paint during the existing character-to-glyph mapping while preserving one continuous layout and owning any varying glyph-paint array.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, SvgTextGlyphPaint? defaultPaint, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, positionAdjustments, paintSpans, [], defaultPaint, deviceMetrics, snapshot, budget, workCounter, cancellationToken);

    /// <summary>Resolves normalized font intervals from the caller snapshot once, then shares their faces with continuous layout and glyph replay.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextFontSpan> fontSpans, SvgTextGlyphPaint? defaultPaint, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        try { return CreateCore(style, text, letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, defaultPaint, deviceMetrics, snapshot, budget, workCounter, cancellationToken); }
        catch (SvgException exception)
        {
            if (style.SourceContext is {} source) exception.AttachSource(source.Describe(string.Empty, string.Empty));
            throw;
        }
    }

    private static SvgTextRun CreateCore(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextFontSpan> fontSpans, SvgTextGlyphPaint? defaultPaint, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (style.TextLength is not null && style.LengthAdjust == SvgTextLengthAdjust.Spacing && deviceMetrics is {} metrics)
            deviceMetrics = metrics with { UsesTextLengthSpacing = true };
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(snapshot);
        using BudgetedParserList<SvgTextResolvedFontSpan> resolvedFonts = new(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        foreach (SvgTextFontSpan span in fontSpans)
        {
            if (workCounter is null) cancellationToken.ThrowIfCancellationRequested(); else workCounter.Consume(cancellationToken);
            SvgFontFaceRequest request = new(span.Font.Family ?? "serif", span.Font.Weight, span.Font.Italic);
            SvgPreparedFont face = workCounter is null ? snapshot.GetFont(request, style.SourceElementId) : snapshot.GetFont(request, style.SourceElementId, workCounter, cancellationToken);
            resolvedFonts.Add(new(span.Start, span.End, new(face, span.Font.FontSize)));
        }
        SvgPreparedFont font = resolvedFonts.Count != 0 ? resolvedFonts[0].Style.Font
            : workCounter is null ? snapshot.GetFont(style.Font, style.SourceElementId) : snapshot.GetFont(style.Font, style.SourceElementId, workCounter, cancellationToken);
        bool needsFragments = defaultPaint?.Decoration is not null || defaultPaint?.HasStroke == true || defaultPaint?.FillServer is not null;
        foreach (SvgTextPaintSpan span in paintSpans) needsFragments |= span.Paint.Decoration is not null || span.Paint.HasStroke || span.Paint.FillServer is not null;
        double originX = style.X + style.Dx;
        double originY = style.Y + style.Dy;
        if (!double.IsFinite(originX) || !double.IsFinite(originY))
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
        }

        if (style.PositionLists.IsEmpty)
        {
            SvgPositionedGlyphStorage ordinary = SvgGlyphLayout.CreateStorage(font, text, style.FontSize, originX, originY, style.LetterSpacing, style.WordSpacing, needsFragments, letterSpacingSpans, positionAdjustments, paintSpans, resolvedFonts.AsSpan(), style.Anchor, deviceMetrics, budget, style.SourceElementId, workCounter, cancellationToken);
            try { return new SvgTextRun(style, font, ordinary, paintSpans.IsEmpty ? defaultPaint : paintSpans[0].Paint, deviceMetrics)
                { GlyphStretch = AdjustGlyphs(ordinary, font, style, deviceMetrics, workCounter, cancellationToken) }; }
            catch { ordinary.Dispose(); throw; }
        }
        using OwnedParserArray<SvgTextPositionAdjustment> positions = style.PositionLists.Build(text, positionAdjustments, budget, workCounter, cancellationToken);
        SvgPositionedGlyphStorage? glyphs = null;
        try
        {
            glyphs = SvgGlyphLayout.CreateStorage(font, text, style.FontSize, 0, 0, style.LetterSpacing, style.WordSpacing, needsFragments, letterSpacingSpans, positions.Items, paintSpans, resolvedFonts.AsSpan(), style.Anchor, deviceMetrics, budget, style.SourceElementId, workCounter, cancellationToken);
            return new SvgTextRun(style, font, glyphs, paintSpans.IsEmpty ? defaultPaint : paintSpans[0].Paint, deviceMetrics)
            { GlyphStretch = AdjustGlyphs(glyphs, font, style, deviceMetrics, workCounter, cancellationToken) };
        }
        catch { glyphs?.Dispose(); throw; }

        static double AdjustGlyphs(SvgPositionedGlyphStorage storage, SvgPreparedFont font, SvgTextStyle style, SvgGlyphDeviceMetrics? metrics,
            ParserWorkCounter? work, CancellationToken token)
        {
            if (style.Baseline != SvgTextBaseline.Alphabetic)
            {
                SvgGlyphFontStyle previous = default;
                double shift = 0;
                Span<SvgPositionedGlyph> baselineGlyphs = storage.MutableGlyphs;
                for (int index = 0; index < baselineGlyphs.Length; index++)
                {
                    if (work is null) token.ThrowIfCancellationRequested(); else work.Consume(token);
                    SvgGlyphFontStyle current = storage.GlyphFonts.IsEmpty ? new(font, style.FontSize) : storage.GlyphFonts[index];
                    if (index == 0 || current != previous)
                    {
                        shift = GetBaselineShift(current, style.Baseline, metrics, work, token);
                        previous = current;
                    }
                    double y = baselineGlyphs[index].Y + shift;
                    if (!double.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text baseline exceeds the numeric range.");
                    baselineGlyphs[index] = baselineGlyphs[index] with { Y = y };
                }
            }
            if (style.TextLength is not double desired || storage.Glyphs.Length == 0) return 1;
            Span<SvgPositionedGlyph> glyphs = storage.MutableGlyphs;
            double first = glyphs[0].X;
            double natural = storage.NaturalWidth;
            if (natural <= 0) return 1;
            double scale = style.LengthAdjust == SvgTextLengthAdjust.SpacingAndGlyphs ? (float)desired / (float)natural : 1;
            double spacing = style.LengthAdjust == SvgTextLengthAdjust.Spacing && glyphs.Length > 1 ? (desired - natural) / (glyphs.Length - 1) : 0;
            double anchorShift = style.Anchor switch { SvgTextAnchor.Middle => (natural - desired) / 2, SvgTextAnchor.End => natural - desired, _ => 0 };
            Span<SvgTextPaintFragment> fragments = storage.MutablePaintFragments;
            for (int index = 0; index < fragments.Length; index++)
            {
                if (work is null) token.ThrowIfCancellationRequested(); else work.Consume(token);
                SvgTextPaintFragment fragment = fragments[index];
                fragments[index] = fragment with { Width = fragment.Width * scale + spacing * (fragment.End - fragment.Start - 1) };
            }
            for (int index = 0; index < glyphs.Length; index++)
            {
                if (work is null) token.ThrowIfCancellationRequested(); else work.Consume(token);
                SvgPositionedGlyph glyph = glyphs[index];
                double x = first + (glyph.X - first) * scale + index * spacing + anchorShift;
                if (!double.IsFinite(x) || !double.IsFinite(scale)) throw new SvgException(SvgErrorCode.LimitExceeded, "Adjusted text length exceeds the numeric range.");
                glyphs[index] = glyph with { X = x, Advance = glyph.Advance * scale };
            }
            return scale;

            static double GetBaselineShift(SvgGlyphFontStyle fontStyle, SvgTextBaseline baseline, SvgGlyphDeviceMetrics? metrics, ParserWorkCounter? work, CancellationToken token)
            {
                SvgPreparedFont font = fontStyle.Font;
                double outputScale = metrics?.Scale ?? 1;
                float size = (float)(fontStyle.FontSize * outputScale);
                float ascent = (metrics?.Target ?? (OperatingSystem.IsWindows() ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux)) == SvgGlyphMetricTarget.Windows
                    ? size * font.Metrics.LineMetrics.WindowsAscender / font.UnitsPerEm : font.Metrics.LineMetrics.Ascender * (size / font.UnitsPerEm);
                float roundedAscent = MathF.Floor(ascent + .5f);
                float baselineAscent;
                if (baseline == SvgTextBaseline.Middle)
                {
                    int xHeight = font.GetXHeight(work, token);
                    baselineAscent = roundedAscent - (xHeight > 0 ? size * xHeight / font.UnitsPerEm / 2 : size / 4);
                }
                else baselineAscent = roundedAscent * .2f;
                // Blink retains fractional baseline metrics in LayoutUnit before
                // the platform's glyph strike snaps its painted origin.
                double shift = (roundedAscent - MathF.Round(baselineAscent * 64, MidpointRounding.AwayFromZero) / 64) / outputScale;
                if (!double.IsFinite(shift)) throw new SvgException(SvgErrorCode.LimitExceeded, "The text baseline exceeds the device range.");
                return shift;
            }
        }
    }

    /// <summary>Yields each positioned glyph's closed SVG-coordinate contours while retaining at most one decoded consecutive glyph outline.</summary>
    internal static IEnumerable<SvgGlyphContourStorage> GetGlyphContours(SvgTextRun run, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => GetGlyphContours(run, SvgAffineTransform.Identity, budget, workCounter, cancellationToken);

    /// <summary>Applies the admitted parent translation after local layout while reusing the ordinary decoded outline and native strike.</summary>
    internal static IEnumerable<SvgGlyphContourStorage> GetGlyphContours(SvgTextRun run, SvgAffineTransform placement, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        OpenTypeGlyphContourStorage? decoded = null;
        ushort decodedGlyphIndex = 0;
        SvgPreparedFont? decodedFont = null;
        try
        {
            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (SvgPositionedGlyph glyph, SvgAffineTransform glyphMap) = run.GetGlyphPlacement(index, placement);
                SvgGlyphFontStyle glyphFont = run.GetGlyphFont(index);
                if (decoded is null || decodedGlyphIndex != glyph.GlyphIndex || !ReferenceEquals(decodedFont, glyphFont.Font))
                {
                    decoded?.Dispose();
                    decoded = OpenTypeFont.GetGlyphContours(glyphFont.Font.Bytes, glyph.GlyphIndex, glyphFont.Font.Metrics, budget, workCounter, cancellationToken);
                    decodedGlyphIndex = glyph.GlyphIndex;
                    decodedFont = glyphFont.Font;
                }
                yield return SvgGlyphOutline.GetContourStorage(glyphFont.Font, glyph, glyphFont.FontSize, glyphMap, decoded, budget, workCounter, cancellationToken);
            }
        }
        finally { decoded?.Dispose(); }
    }
}

/// <summary>Represents one simple single-font horizontal text run before glyph painting.</summary>
internal sealed class SvgTextRun : IDisposable
{
    private readonly SvgPositionedGlyphStorage _glyphs;
    private readonly SvgTextGlyphPaint? _defaultPaint;
    private readonly SvgGlyphDeviceMetrics? _deviceMetrics;

    internal SvgTextRun(SvgTextStyle style, SvgPreparedFont font, SvgPositionedGlyphStorage glyphs)
        : this(style, font, glyphs, null) { }

    internal SvgTextRun(SvgTextStyle style, SvgPreparedFont font, SvgPositionedGlyphStorage glyphs, SvgTextGlyphPaint? defaultPaint)
        : this(style, font, glyphs, defaultPaint, null) { }

    internal SvgTextRun(SvgTextStyle style, SvgPreparedFont font, SvgPositionedGlyphStorage glyphs, SvgTextGlyphPaint? defaultPaint,
        SvgGlyphDeviceMetrics? deviceMetrics)
    {
        Style = style;
        Font = font;
        _glyphs = glyphs;
        _defaultPaint = defaultPaint;
        _deviceMetrics = deviceMetrics;
    }

    internal SvgTextStyle Style { get; }

    internal SvgPreparedFont Font { get; }
    internal double GlyphStretch { get; init; } = 1;

    internal ReadOnlySpan<SvgPositionedGlyph> Glyphs => _glyphs.Glyphs;
    internal ReadOnlySpan<SvgTextPaintFragment> PaintFragments => _glyphs.PaintFragments;
    internal SvgGlyphDeviceMetrics? DeviceMetrics => _deviceMetrics;
    internal SvgGlyphFontStyle GetGlyphFont(int index) => _glyphs.GlyphFonts.IsEmpty ? new(Font, Style.FontSize) : _glyphs.GlyphFonts[index];
    internal SvgTextGlyphPaint GetGlyphPaint(int index, SvgColor fallbackColor) => _glyphs.GlyphPaints.IsEmpty ? _defaultPaint ?? new(fallbackColor, true) : _glyphs.GlyphPaints[index];
    internal double GetGlyphRotation(int index) => _glyphs.GlyphRotations.IsEmpty ? 0 : _glyphs.GlyphRotations[index];

    /// <summary>Composes character-local rotation with the existing glyph and element placement.</summary>
    internal (SvgPositionedGlyph Position, SvgAffineTransform Map) GetGlyphPlacement(int index, SvgAffineTransform placement)
        => GetPlacement(index, placement, true);

    internal (SvgPositionedGlyph Position, SvgAffineTransform Map) GetDecorationPlacement(int index, SvgAffineTransform placement)
        => GetPlacement(index, placement, false);

    private (SvgPositionedGlyph Position, SvgAffineTransform Map) GetPlacement(int index, SvgAffineTransform placement, bool stretch)
    {
        SvgPositionedGlyph glyph = Glyphs[index];
        double angle = GetGlyphRotation(index);
        double glyphStretch = stretch ? GlyphStretch : 1;
        if (angle == 0 && glyphStretch == 1) return placement.IsTranslation ? (glyph.Translate(placement), SvgAffineTransform.Identity) : (glyph, placement);
        SvgAffineTransform map = SvgAffineTransform.FromTranslation(glyph.X, glyph.Y)
            .ComposeChecked(SvgAffineTransform.FromRotationDegrees(angle)).ComposeChecked(SvgAffineTransform.FromScale(glyphStretch, 1));
        if (angle != 0 && _deviceMetrics is { } metrics)
        {
            SvgGlyphFontStyle font = GetGlyphFont(index);
            map = metrics.PlaceTextPathPaint(map, 0, 0, font.FontSize, font.Font.Metrics);
        }
        return (glyph with { X = 0, Y = 0 }, placement.ComposeChecked(map));
    }

    public void Dispose() => _glyphs.Dispose();
}
