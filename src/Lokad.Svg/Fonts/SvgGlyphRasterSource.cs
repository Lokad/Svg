namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Retains one positioned glyph while borrowing the caller's prepared face for device-size fitting.</summary>
/// <remarks>The scene owns this metadata reservation. The font snapshot remains caller-owned and is never disposed here. Path construction charges discovery, hinting and copied commands to the conversion and returns an exclusive owner. Readers and disposal belong to one single-threaded conversion.</remarks>
internal sealed class SvgGlyphRasterSource : IDisposable
{
    private SvgPreparedFont? _font;
    private RenderBudget.BudgetReservation? _reservation;
    private readonly SvgPositionedGlyph _glyph;
    private readonly double _fontSize;

    private SvgGlyphRasterSource(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, RenderBudget.BudgetReservation reservation)
    {
        _font = font; _glyph = glyph; _fontSize = fontSize; _reservation = reservation;
    }

    /// <summary>Identifies the admitted simple-outline stage without changing existing composite admission.</summary>
    internal bool HasSimpleOutline(ParserWorkCounter work, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        OpenTypeGlyphHeader? header = OpenTypeFont.GetGlyphHeader(font.Bytes, _glyph.GlyphIndex, font.Metrics, work, token);
        return header is OpenTypeGlyphHeader value && !value.IsComposite;
    }

    /// <summary>Admits simple fitting and newly supported composite dependencies, preserving the established route for earlier composites.</summary>
    internal bool UsesDeviceFitting(RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        if (HasSimpleOutline(work, token)) return true;
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        return OpenTypeFont.NeedsCompositeDeviceFitting(font.Bytes, _glyph.GlyphIndex, font.Metrics, budget, work, token);
    }

    internal bool HasTrueTypePrograms(RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        return OpenTypeFont.HasTrueTypePrograms(font.Bytes, _glyph.GlyphIndex, font.Metrics, budget, work, token);
    }

    /// <summary>Returns exclusively owned, positioned instruction-fitted contours for symmetric LCD coverage.</summary>
    internal OpenTypeGlyphContourStorage CreateWindowsContours(double scaleX, double offsetX, double scaleY, double offsetY, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        TrueTypeHintTarget target = new(42, TrueTypeHintQueryFlags.FontVariations | TrueTypeHintQueryFlags.VerticalPhantoms | TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned | TrueTypeHintQueryFlags.Symmetric, TrueTypeHintControlScale.FullPrecision, TrueTypeHintPreparation.Single);
        OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, _fontSize * scaleY, target, budget, work, token);
        try
        {
            float originX = MathF.Floor((float)(_glyph.X * scaleX + offsetX) * 4f + .5f) / 4f;
            float originY = MathF.Floor((float)(_glyph.Y * scaleY + offsetY) + .5f);
            float ratioX = (float)(scaleX / scaleY);
            if (!float.IsFinite(originX) || !float.IsFinite(originY) || !float.IsFinite(ratioX)) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted glyph placement exceeds the device range.");
            for (int i = 0; i < contours.Segments.Length; i++)
            {
                OpenTypeQuadraticSegment segment = contours.Segments[i];
                contours.Segments[i] = new(Map(segment.Start), Map(segment.Control), Map(segment.End));
            }
            return contours;

            OpenTypeOutlinePoint Map(OpenTypeOutlinePoint point)
            {
                work.Consume(token);
                float x = originX + (float)(point.X / 64) * ratioX, y = originY - (float)(point.Y / 64);
                if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted glyph outline exceeds the device range.");
                return new(x, y);
            }
        }
        catch { contours.Dispose(); throw; }
    }

    internal static SvgGlyphRasterSource Create(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(font);
        RenderBudget.BudgetReservation reservation = budget.Reserve(128, SvgMemoryCategory.Scene);
        try { return new(font, glyph, fontSize, reservation); }
        catch { reservation.Dispose(); throw; }
    }

    /// <summary>Returns native-directed device commands after fitting at mapped size; applies the glyph origin once.</summary>
    internal SvgAuthoredPath CreateLatinLightPath(double scaleX, double offsetX, double scaleY, double offsetY, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        double size = _fontSize * scaleY;
        using OpenTypeGlyphContourStorage contours = OpenTypeFont.GetLatinLightContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, size, budget, work, token);
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Geometry, work, token);
        float originX = (float)(_glyph.X * scaleX + offsetX), originY = (float)(_glyph.Y * scaleY + offsetY);
        // Horizontal subpixel strikes round X to quarters and snap the baseline Y.
        // SkGlyphPositionRoundingSpec / SkGlyphRunPainter at the pinned revision.
        originX = MathF.Floor(originX * 4f + .5f) / 4f;
        originY = MathF.Floor(originY + .5f);
        float ratioX = (float)(scaleX / scaleY);
        if (!float.IsFinite(originX) || !float.IsFinite(originY) || !float.IsFinite(ratioX)) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted glyph placement exceeds the device range.");
        int first = 0;
        foreach (int end in contours.ContourEnds)
        {
            OpenTypeOutlinePoint origin = contours.Segments[first].Start;
            commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, Map(origin), default, default));
            for (int index = first; index < end; index++)
            {
                OpenTypeQuadraticSegment segment = contours.Segments[index];
                if (index == end - 1 && segment.Control == segment.End && segment.End == origin) continue;
                if (segment.Control == segment.End) commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, Map(segment.End), default, default));
                else commands.Add(new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, Map(segment.Control), Map(segment.End), default));
            }
            commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            first = end;
        }
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
        catch { owned.Dispose(); throw; }

        SvgDevicePoint Map(OpenTypeOutlinePoint point)
        {
            work.Consume(token);
            float x = originX + (float)(point.X / 64) * ratioX, y = originY - (float)(point.Y / 64);
            if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted glyph outline exceeds the device range.");
            return new(x, y);
        }
    }

    public void Dispose()
    {
        _font = null;
        _reservation?.Dispose(); _reservation = null;
    }
}
