namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Retains one positioned glyph while borrowing the caller's prepared face for device-size fitting.</summary>
/// <remarks>The scene owns this metadata reservation. The font snapshot remains caller-owned and is never disposed here. Path construction charges discovery, hinting and copied commands to the conversion and returns an exclusive owner. Readers and disposal belong to one single-threaded conversion.</remarks>
internal sealed partial class SvgGlyphRasterSource : IDisposable
{
    private SvgPreparedFont? _font;
    private RenderBudget.BudgetReservation? _reservation;
    private readonly SvgPositionedGlyph _glyph;
    private readonly double _fontSize;
    private readonly double? _pictureFontScale;
    private readonly SvgAffineTransform _glyphMap;

    internal bool HasGlyphMap => _glyphMap != SvgAffineTransform.Identity;
    internal SvgAffineTransform GlyphMap => _glyphMap;

    private SvgGlyphRasterSource(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, SvgAffineTransform glyphMap, double? pictureFontScale, RenderBudget.BudgetReservation reservation)
    {
        _font = font; _glyph = glyph; _fontSize = fontSize; _glyphMap = glyphMap; _pictureFontScale = pictureFontScale; _reservation = reservation;
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

    /// <summary>Returns owned device contours for symmetric LCD coverage, with rotated outlines retaining their unhinted shape.</summary>
    internal OpenTypeGlyphContourStorage CreateWindowsContours(double scaleX, double offsetX, double scaleY, double offsetY, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        TrueTypeHintTarget target = new(42, TrueTypeHintQueryFlags.FontVariations | TrueTypeHintQueryFlags.VerticalPhantoms | TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned | TrueTypeHintQueryFlags.Symmetric, TrueTypeHintControlScale.FullPrecision, TrueTypeHintPreparation.Single);
        SvgGlyphStrikeTransform strike = HasGlyphMap
            ? SvgGlyphStrikeTransform.Create(_fontSize * scaleY, new(_glyphMap.A * scaleX / scaleY, _glyphMap.B, _glyphMap.C * scaleX / scaleY, _glyphMap.D, 0, 0))
            : default;
        // A horizontal baseline retains device fitting under X skew. Rotated
        // baselines keep their qualified unhinted route, except quarter turns.
        bool mappedFitting = HasGlyphMap && HasTrueTypePrograms(budget, work, token) &&
            (strike.RelaxedMap.B == 0 || strike.RelaxedMap.A == 0 && strike.RelaxedMap.D == 0);
        SvgAffineTransform fittedMap = strike.RemainingMap;
        if (mappedFitting)
        {
            double ratio = Math.Sqrt(fittedMap.A * fittedMap.A + fittedMap.B * fittedMap.B);
            if (ratio > 0)
            {
                target = target with { HorizontalRatio = ratio };
                fittedMap = fittedMap with { A = fittedMap.A / ratio, B = fittedMap.B / ratio };
            }
        }
        OpenTypeGlyphContourStorage contours = HasGlyphMap
            ? mappedFitting
                ? OpenTypeFont.GetTrueTypeContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, target, TrueTypeImpliedPointRounding.None, OpenTypeWindowsGlyphTransform.Create(new(1, fittedMap, strike.RelaxedMap), 1), budget, work, token)
                : OpenTypeFont.GetWindowsUnhintedContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike, budget, work, token)
            : OpenTypeFont.GetTrueTypeContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, DeviceFontSize(scaleY), target, TrueTypeImpliedPointRounding.None, budget, work, token);
        try
        {
            SvgDevicePoint origin = _glyphMap.Apply(_glyph.X, _glyph.Y);
            float originX = MathF.Floor((float)(origin.X * scaleX + offsetX) * 4f + .5f) / 4f;
            float originY = HasGlyphMap ? MathF.Floor((float)(origin.Y * scaleY + offsetY) * 4f + .5f) / 4f
                : MathF.Floor((float)(origin.Y * scaleY + offsetY) + .5f);
            float ratioX = HasGlyphMap ? 1 : (float)(scaleX / scaleY);
            if (!float.IsFinite(originX) || !float.IsFinite(originY) || !float.IsFinite(ratioX)) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted glyph placement exceeds the device range.");
            // Native symmetric LCD coverage quantizes the quarter-pixel strike
            // origin onto its six-column grid before sampling the fitted outline.
            double phase = originX - MathF.Floor(originX);
            double phaseCorrection = Math.Floor(phase * 6 + .5) / 6 - phase;
            double verticalPhase = originY - MathF.Floor(originY);
            double verticalCorrection = HasGlyphMap ? Math.Floor(verticalPhase * 5 + .5) / 5 - verticalPhase : 0;
            for (int i = 0; i < contours.Segments.Length; i++)
            {
                OpenTypeQuadraticSegment segment = contours.Segments[i];
                contours.Segments[i] = new(Map(segment.Start), Map(segment.Control), Map(segment.End));
            }
            if (HasGlyphMap)
            {
                using (contours) return SvgWindowsGlyphCurves.Prepare(contours, budget, work, token);
            }
            return contours;

            OpenTypeOutlinePoint Map(OpenTypeOutlinePoint point)
            {
                work.Consume(token);
                float px = (float)(point.X / 64), py = -(float)(point.Y / 64);
                float x = originX + px * ratioX, y = originY + py;
                if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted glyph outline exceeds the device range.");
                return new(x + phaseCorrection, y + verticalCorrection);
            }
        }
        catch { contours.Dispose(); throw; }
    }

    internal static SvgGlyphRasterSource Create(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, RenderBudget budget)
        => Create(font, glyph, fontSize, SvgAffineTransform.Identity, null, budget);

    internal static SvgGlyphRasterSource Create(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, SvgAffineTransform glyphMap, RenderBudget budget)
        => Create(font, glyph, fontSize, glyphMap, null, budget);

    internal static SvgGlyphRasterSource Create(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, SvgAffineTransform glyphMap, double? pictureFontScale, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(font);
        RenderBudget.BudgetReservation reservation = budget.Reserve(192, SvgMemoryCategory.Scene);
        try { return new(font, glyph, fontSize, glyphMap, pictureFontScale, reservation); }
        catch { reservation.Dispose(); throw; }
    }

    // SVG picture text records a screen-scaled font and its inverse layout scale.
    // Relax the remaining picture matrix before fitting, while retaining the
    // original placement for subpixel origin selection. Ordinary glyphs keep
    // their qualified device-size route.
    private double DeviceFontSize(double scale)
    {
        if (_pictureFontScale is not {} host) return _fontSize * scale;
        float recordedSize = SvgGlyphDeviceMetrics.GetEffectiveFontSize(_fontSize * host);
        return recordedSize * SvgGlyphStrikeTransform.RelaxCoefficient(scale / host);
    }

    /// <summary>Replays a mapped outline at the native two-axis subpixel strike origin, preserving its tangent transform.</summary>
    /// <remarks>The returned path owns its commands. The font is borrowed; all decoded scratch is released
    /// before return. Curve and point traversal charges work and checks cancellation.</remarks>
    internal SvgAuthoredPath CreateMappedPath(double scaleX, double offsetX, double scaleY, double offsetY,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        SvgAffineTransform normalized = new(_glyphMap.A * scaleX / scaleY, _glyphMap.B, _glyphMap.C * scaleX / scaleY, _glyphMap.D, 0, 0);
        SvgGlyphStrikeTransform strike = SvgGlyphStrikeTransform.Create(_fontSize * scaleY, normalized);
        // Skia keeps FreeType light fitting for axis-aligned maps, including
        // quarter turns; other rotations and skew use unhinted contours.
        bool axisAligned = strike.RelaxedMap.B == 0 && strike.RelaxedMap.C == 0 || strike.RelaxedMap.A == 0 && strike.RelaxedMap.D == 0;
        using OpenTypeGlyphContourStorage contours = axisAligned
            ? OpenTypeFont.GetLatinLightContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, budget, work, token)
            : OpenTypeFont.GetUnhintedDeviceContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, budget, work, token);
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Geometry, work, token);
        int first = 0;
        foreach (int end in contours.ContourEnds)
        {
            work.Consume(token);
            OpenTypeOutlinePoint origin = contours.Segments[first].Start;
            commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, Map(origin), default, default));
            for (int index = first; index < end; index++)
            {
                work.Consume(token);
                OpenTypeQuadraticSegment segment = contours.Segments[index];
                if (index == end - 1 && segment.Control == segment.End && segment.End == origin) continue;
                commands.Add(segment.Control == segment.End
                    ? new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, Map(segment.End), default, default)
                    : new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, Map(segment.Control), Map(segment.End), default));
            }
            commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            first = end;
        }
        SvgAffineTransform map = SvgAffineTransform.FromScaleOffset(scaleX, scaleY, offsetX, offsetY).ComposeChecked(_glyphMap);
        SvgDevicePoint glyphOrigin = map.Apply(_glyph.X, _glyph.Y);
        float originX = MathF.Floor((float)glyphOrigin.X * 4f + .5f) / 4f;
        float originY = MathF.Floor((float)glyphOrigin.Y * 4f + .5f) / 4f;
        if (!float.IsFinite(originX) || !float.IsFinite(originY)) throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped glyph placement exceeds the device range.");
        map = new(strike.RemainingMap.A, strike.RemainingMap.B, strike.RemainingMap.C, strike.RemainingMap.D, originX, originY);
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        try { return SvgAuthoredPath.Create(owned, map, budget); }
        catch { owned.Dispose(); throw; }

        SvgDevicePoint Map(OpenTypeOutlinePoint point) => new(point.X / 64, -point.Y / 64);
    }

    /// <summary>Returns native-directed device commands after fitting at mapped size; applies the glyph origin once.</summary>
    internal SvgAuthoredPath CreateLatinLightPath(double scaleX, double offsetX, double scaleY, double offsetY, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        double size = DeviceFontSize(scaleY);
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
        try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.FromTranslation(originX, originY), budget); }
        catch { owned.Dispose(); throw; }

        SvgDevicePoint Map(OpenTypeOutlinePoint point)
        {
            work.Consume(token);
            // Native glyph masks keep outline coordinates local; integer placement
            // must not change curve subdivision or byte coverage through float rounding.
            float x = (float)(point.X / 64) * ratioX, y = -(float)(point.Y / 64);
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
