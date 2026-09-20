namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

/// <summary>Builds one bounded simple horizontal text run from prepared caller-owned font data.</summary>
internal static class SvgTextRunBuilder
{
    internal static SvgTextRun Create(SvgTextStyle style, string text, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(snapshot);
        SvgPreparedFont font = snapshot.GetFont(style.Font);
        double originX = style.X + style.Dx;
        double originY = style.Y + style.Dy;
        if (!double.IsFinite(originX) || !double.IsFinite(originY))
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
        }

        SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(font, text, style.FontSize, originX, originY, style.Anchor, budget, cancellationToken);
        return new SvgTextRun(style, font, glyphs);
    }

    /// <summary>Yields each positioned glyph's closed SVG-coordinate contours without retaining whole-run contour storage.</summary>
    internal static IEnumerable<SvgGlyphQuadraticContour[]> GetGlyphContours(SvgTextRun run, RenderBudget budget, CancellationToken cancellationToken)
    {
        for (int index = 0; index < run.Glyphs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return SvgGlyphOutline.GetContours(run.Font, run.Glyphs[index], run.Style.FontSize, budget, cancellationToken);
        }
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
