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
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(snapshot);
        SvgPreparedFont font = workCounter is null ? snapshot.GetFont(style.Font, style.SourceElementId) : snapshot.GetFont(style.Font, style.SourceElementId, workCounter, cancellationToken);
        double originX = style.X + style.Dx;
        double originY = style.Y + style.Dy;
        if (!double.IsFinite(originX) || !double.IsFinite(originY))
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
        }

        SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(font, text, style.FontSize, originX, originY, style.LetterSpacing, letterSpacingSpans, positionAdjustments, style.Anchor, budget, style.SourceElementId, workCounter, cancellationToken);
        return new SvgTextRun(style, font, glyphs);
    }

    /// <summary>Yields each positioned glyph's closed SVG-coordinate contours while retaining at most one decoded consecutive glyph outline.</summary>
    internal static IEnumerable<SvgGlyphContourStorage> GetGlyphContours(SvgTextRun run, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        OpenTypeGlyphContourStorage? decoded = null;
        ushort decodedGlyphIndex = 0;
        try
        {
            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgPositionedGlyph glyph = run.Glyphs[index];
                if (decoded is null || decodedGlyphIndex != glyph.GlyphIndex)
                {
                    decoded?.Dispose();
                    decoded = OpenTypeFont.GetGlyphContours(run.Font.Bytes, glyph.GlyphIndex, run.Font.Metrics, budget, workCounter, cancellationToken);
                    decodedGlyphIndex = glyph.GlyphIndex;
                }
                yield return SvgGlyphOutline.GetContourStorage(run.Font, glyph, run.Style.FontSize, decoded, budget, workCounter, cancellationToken);
            }
        }
        finally { decoded?.Dispose(); }
    }
}

/// <summary>Represents one simple single-font horizontal text run before glyph painting.</summary>
internal sealed class SvgTextRun : IDisposable
{
    private readonly SvgPositionedGlyphStorage _glyphs;

    internal SvgTextRun(SvgTextStyle style, SvgPreparedFont font, SvgPositionedGlyphStorage glyphs)
    {
        Style = style;
        Font = font;
        _glyphs = glyphs;
    }

    internal SvgTextStyle Style { get; }

    internal SvgPreparedFont Font { get; }

    internal SvgPositionedGlyph[] Glyphs => _glyphs.Glyphs;

    public void Dispose() => _glyphs.Dispose();
}
