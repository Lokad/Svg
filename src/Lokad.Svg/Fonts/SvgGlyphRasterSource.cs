namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Retains one positioned glyph while borrowing the caller's prepared face for device-size fitting.</summary>
/// <remarks>The scene owns this metadata reservation. The font snapshot remains caller-owned and is never disposed here. Path construction reserves discovery, hinting and copied-command storage with the conversion budget and returns an exclusive owner. Readers and disposal belong to one single-threaded conversion.</remarks>
internal sealed partial class SvgGlyphRasterSource : IDisposable
{
    private SvgPreparedFont? _font;
    private RenderBudget.BudgetReservation? _reservation;
    private readonly SvgPositionedGlyph _glyph;
    private readonly double _fontSize;
    private readonly double? _pictureFontScale;
    private readonly SvgAffineTransform _glyphMap;

    internal bool HasGlyphMap => _glyphMap != SvgAffineTransform.Identity;
    internal bool HasCompleteTrueTypeProfile => (_font ?? throw new InvalidOperationException("The glyph source is disposed.")).Metrics.HasCompleteTrueTypeProfile;
    internal SvgAffineTransform GlyphMap => _glyphMap;

    private SvgGlyphRasterSource(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, SvgAffineTransform glyphMap, double? pictureFontScale, RenderBudget.BudgetReservation reservation)
    {
        _font = font; _glyph = glyph; _fontSize = fontSize; _glyphMap = glyphMap; _pictureFontScale = pictureFontScale; _reservation = reservation;
    }

    /// <summary>Creates conversion-owned placement metadata while borrowing the same prepared face and positioned glyph.</summary>
    /// <remarks>The returned owner reserves bounded scene storage and must be disposed by its single-threaded replay consumer.
    /// No font or outline arrays are copied. The caller observes cancellation during placement.</remarks>
    internal SvgGlyphRasterSource Place(SvgAffineTransform placement, RenderBudget budget)
        => Create(_font ?? throw new InvalidOperationException("The glyph source is disposed."), _glyph, _fontSize,
            placement.ComposeChecked(_glyphMap), _pictureFontScale, budget);

    /// <summary>Measures the native font line box before device fitting, in this source's retained coordinates.</summary>
    /// <remarks>No outline storage is created. The caller observes cancellation during corner mapping.</remarks>
    internal SvgPaintBounds LayoutBounds(SvgAffineTransform placement, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        OpenTypeFontMetrics metrics = font.Metrics;
        float size = (float)_fontSize;
        float ascent = OperatingSystem.IsWindows() ? size * metrics.LineMetrics.WindowsAscender / metrics.UnitsPerEm : metrics.LineMetrics.Ascender * (size / metrics.UnitsPerEm);
        float descent = OperatingSystem.IsWindows() ? size * metrics.LineMetrics.WindowsDescender / metrics.UnitsPerEm : -metrics.LineMetrics.Descender * (size / metrics.UnitsPerEm);
        SvgPaintBounds bounds = new((float)_glyph.X, (float)_glyph.Y - MathF.Floor(ascent + .5f),
            (float)(_glyph.X + _glyph.Advance), (float)_glyph.Y + MathF.Floor(descent + .5f));
        if (OpenTypeFont.GetGlyphHeader(font.Bytes, _glyph.GlyphIndex, metrics, token) is {} header)
        {
            float unit = size / metrics.UnitsPerEm;
            bounds = new(MathF.Min(bounds.Left, (float)_glyph.X + MathF.Floor(header.XMinimum * unit) - 1),
                MathF.Min(bounds.Top, (float)_glyph.Y + MathF.Floor(-header.YMaximum * unit) - 1),
                MathF.Max(bounds.Right, (float)_glyph.X + MathF.Ceiling(header.XMaximum * unit) + 1),
                MathF.Max(bounds.Bottom, (float)_glyph.Y + MathF.Ceiling(-header.YMinimum * unit) + 1));
        }
        return bounds.Map(placement.ComposeChecked(_glyphMap), token);
    }

    /// <summary>Identifies the admitted simple-outline stage without changing existing composite admission.</summary>
    internal bool HasSimpleOutline(CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        OpenTypeGlyphHeader? header = OpenTypeFont.GetGlyphHeader(font.Bytes, _glyph.GlyphIndex, font.Metrics, token);
        return header is OpenTypeGlyphHeader value && !value.IsComposite;
    }

    /// <summary>Admits simple fitting and newly supported composite dependencies, preserving the established route for earlier composites.</summary>
    internal bool UsesDeviceFitting(RenderBudget budget, CancellationToken token)
    {
        if (HasSimpleOutline(token)) return true;
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        return OpenTypeFont.NeedsCompositeDeviceFitting(font.Bytes, _glyph.GlyphIndex, font.Metrics, budget, token);
    }

    internal bool HasTrueTypePrograms(RenderBudget budget, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        return OpenTypeFont.HasTrueTypePrograms(font.Bytes, _glyph.GlyphIndex, font.Metrics, budget, token);
    }

    /// <summary>Returns owned device contours for the selected native LCD profile, with rotated outlines retaining their unhinted shape.</summary>
    internal OpenTypeGlyphContourStorage CreateWindowsContours(double scaleX, double offsetX, double scaleY, double offsetY, RenderBudget budget, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        SvgGlyphStrikeTransform strike = HasGlyphMap
            ? CreateMappedStrike(scaleX, scaleY)
            : default;
        OpenTypeWindowsGlyphProfile profile = OpenTypeFont.GetWindowsGlyphProfile(font.Bytes, font.Metrics, HasGlyphMap ? strike.DeviceSize : DeviceFontSize(scaleY), token);
        SvgGlyphLcdMode mode = profile.LcdMode;
        TrueTypeHintTarget target = profile.CreateHintTarget(1);
        // A horizontal baseline retains device fitting under X skew. Rotated
        // baselines keep their qualified unhinted route, except quarter turns.
        bool hasPrograms = HasGlyphMap && HasTrueTypePrograms(budget, token);
        bool usesAxialStrike = (hasPrograms || !profile.GridFit) &&
            (strike.RelaxedMap.B == 0 || strike.RelaxedMap.A == 0 && strike.RelaxedMap.D == 0);
        SvgAffineTransform fittedMap = strike.RemainingMap;
        if (usesAxialStrike)
        {
            double ratio = Math.Sqrt(fittedMap.A * fittedMap.A + fittedMap.B * fittedMap.B);
            if (ratio > 0)
            {
                target = target with { HorizontalRatio = ratio };
                fittedMap = fittedMap with { A = fittedMap.A / ratio, B = fittedMap.B / ratio };
            }
        }
        OpenTypeGlyphContourStorage contours;
        if (!HasGlyphMap)
            contours = profile.GridFit
                ? OpenTypeFont.GetTrueTypeContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, DeviceFontSize(scaleY), target, TrueTypeImpliedPointRounding.None, budget, token)
                : OpenTypeFont.GetWindowsUnfittedContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, DeviceFontSize(scaleY), budget, token);
        else if (!usesAxialStrike)
            contours = OpenTypeFont.GetWindowsUnhintedContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike, hasPrograms, budget, token);
        else
        {
            OpenTypeWindowsGlyphTransform transform = OpenTypeWindowsGlyphTransform.Create(new(1, fittedMap, strike.RelaxedMap), 1);
            contours = profile.GridFit
                ? OpenTypeFont.GetTrueTypeContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, target, TrueTypeImpliedPointRounding.None, transform, budget, token)
                : OpenTypeFont.GetWindowsUnfittedContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, target.HorizontalRatio, transform, budget, token);
        }
        try
        {
            contours.WindowsLcdMode = mode;
            contours.ExtendWindowsLcdSpans = font.Metrics.ExtendWindowsLcdSpans;
            SvgDevicePoint origin = _glyphMap.Apply(_glyph.X, _glyph.Y);
            float originX = SvgGlyphStrikeTransform.RoundXOrigin((float)(origin.X * scaleX + offsetX), HasGlyphMap ? strike.RelaxedMap : SvgAffineTransform.Identity);
            float originY = HasGlyphMap && strike.RelaxedMap.B != 0 ? MathF.Floor((float)(origin.Y * scaleY + offsetY) * 4f + .5f) / 4f
                : MathF.Floor((float)(origin.Y * scaleY + offsetY) + .5f);
            float ratioX = HasGlyphMap ? 1 : (float)(scaleX / scaleY);
            if (!float.IsFinite(originX) || !float.IsFinite(originY) || !float.IsFinite(ratioX)) throw new SvgException(SvgErrorCode.LimitExceeded, "The hinted glyph placement exceeds the device range.");
            // Native Windows LCD coverage quantizes the quarter-pixel strike
            // origin onto its six-column grid before sampling the fitted outline.
            double phase = originX - MathF.Floor(originX);
            double phaseCorrection = Math.Floor(phase * 6 + .5) / 6 - phase;
            double verticalPhase = originY - MathF.Floor(originY);
            int verticalSamples = mode == SvgGlyphLcdMode.Natural ? 1 : 5;
            double verticalCorrection = HasGlyphMap ? Math.Floor(verticalPhase * verticalSamples + .5) / verticalSamples - verticalPhase : 0;
            for (int i = 0; i < contours.Segments.Length; i++)
            {
                OpenTypeQuadraticSegment segment = contours.Segments[i];
                contours.Segments[i] = new(Map(segment.Start), Map(segment.Control), Map(segment.End));
            }
            if (HasGlyphMap || !HasSimpleOutline(token))
            {
                using (contours) return SvgWindowsGlyphCurves.Prepare(contours, budget, token);
            }
            return contours;

            OpenTypeOutlinePoint Map(OpenTypeOutlinePoint point)
            {
                token.ThrowIfCancellationRequested();
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
    internal double DeviceFontSize(double scale)
    {
        if (_pictureFontScale is not {} host) return _fontSize * scale;
        float recordedSize = SvgGlyphDeviceMetrics.GetEffectiveFontSize(_fontSize * host);
        return recordedSize * SvgGlyphStrikeTransform.RelaxCoefficient(scale / host);
    }

    /// <summary>Preserves a recorded font size while relaxing only its remaining device matrix.</summary>
    private SvgGlyphStrikeTransform CreateMappedStrike(double scaleX, double scaleY)
    {
        if (_pictureFontScale is not {} host)
            return SvgGlyphStrikeTransform.Create(_fontSize * scaleY, new(_glyphMap.A * scaleX / scaleY, _glyphMap.B, _glyphMap.C * scaleX / scaleY, _glyphMap.D, 0, 0));
        float recordedSize = SvgGlyphDeviceMetrics.GetEffectiveFontSize(_fontSize * host);
        return SvgGlyphStrikeTransform.Create(recordedSize, new(_glyphMap.A * scaleX / host, _glyphMap.B * scaleY / host,
            _glyphMap.C * scaleX / host, _glyphMap.D * scaleY / host, 0, 0));
    }

    /// <summary>Replays a mapped outline at the native two-axis subpixel strike origin, preserving its tangent transform.</summary>
    /// <remarks>The returned path owns its commands. The font is borrowed; all decoded scratch is released
    /// before return. Curve and point traversal checks cancellation.</remarks>
    internal SvgAuthoredPath CreateMappedPath(double scaleX, double offsetX, double scaleY, double offsetY,
        RenderBudget budget, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        SvgGlyphStrikeTransform strike = CreateMappedStrike(scaleX, scaleY);
        // Skia keeps FreeType light fitting for axis-aligned maps, including
        // quarter turns; other rotations and skew use unhinted contours.
        using OpenTypeGlyphContourStorage contours = strike.HasAxisAlignedOutline
            ? OpenTypeFont.GetLatinLightContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, budget, token)
            : OpenTypeFont.GetUnhintedDeviceContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, strike.DeviceSize, budget, token);
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Geometry, token);
        int first = 0;
        foreach (int end in contours.ContourEnds)
        {
            token.ThrowIfCancellationRequested();
            OpenTypeOutlinePoint origin = contours.Segments[first].Start;
            commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, Map(origin), default, default));
            for (int index = first; index < end; index++)
            {
                token.ThrowIfCancellationRequested();
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
        float originX = SvgGlyphStrikeTransform.RoundXOrigin((float)glyphOrigin.X, strike.RelaxedMap);
        float originY = strike.RelaxedMap.B == 0 ? MathF.Floor((float)glyphOrigin.Y + .5f) : MathF.Floor((float)glyphOrigin.Y * 4f + .5f) / 4f;
        if (!float.IsFinite(originX) || !float.IsFinite(originY)) throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped glyph placement exceeds the device range.");
        map = new(strike.RemainingMap.A, strike.RemainingMap.B, strike.RemainingMap.C, strike.RemainingMap.D, originX, originY);
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        try { return SvgAuthoredPath.Create(owned, map, budget); }
        catch { owned.Dispose(); throw; }

        SvgDevicePoint Map(OpenTypeOutlinePoint point) => new(point.X / 64, -point.Y / 64);
    }

    /// <summary>Returns native-directed device commands after fitting at mapped size; applies the glyph origin once.</summary>
    internal SvgAuthoredPath CreateLatinLightPath(double scaleX, double offsetX, double scaleY, double offsetY, RenderBudget budget, CancellationToken token)
    {
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        double size = DeviceFontSize(scaleY);
        using OpenTypeGlyphContourStorage contours = OpenTypeFont.GetLatinLightContours(font.Bytes, _glyph.GlyphIndex, font.Metrics, size, budget, token);
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Geometry, token);
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
            token.ThrowIfCancellationRequested();
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
