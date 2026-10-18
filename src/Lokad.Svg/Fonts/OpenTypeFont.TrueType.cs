namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal enum TrueTypeImpliedPointRounding { Down, None, TowardZero }

internal static partial class OpenTypeFont
{
    /// <summary>Distinguishes program-bearing simple glyphs from previously admitted instruction-free outlines.</summary>
    internal static bool HasTrueTypePrograms(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        ReadOnlySpan<byte> data = bytes.Span;
        int count = ReadUInt16(data, 4);
        for (int i = 0; i < count; i++)
        {
            work.Consume(token);
            int entry = 12 + i * 16;
            if (ReadUInt32(data, entry) is 0x6670676D or 0x70726570 && ReadUInt32(data, entry + 12) != 0) return true;
        }
        using OpenTypeSimpleGlyphStructureStorage structure = GetGlyphStructure(bytes, glyphIndex, metrics, budget, work, token, true);
        if (structure.CompositeHeader is not null) return true;
        if (structure.Value.ContourEndpoints.Length == 0) return false;
        return ReadUInt16(data, structure.Value.GlyphRange.Offset + 10 + structure.Value.ContourEndpoints.Length * 2) != 0;
    }

    /// <summary>Returns owned instruction-fitted contours with the established integer midpoint policy.</summary>
    internal static OpenTypeGlyphContourStorage GetTrueTypeContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, TrueTypeHintTarget target, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        => GetTrueTypeContours(bytes, glyphIndex, metrics, deviceSize, target, TrueTypeImpliedPointRounding.Down, budget, work, token);

    /// <summary>Returns owned, instruction-fitted contours with an explicit rasterizer midpoint policy.</summary>
    /// <remarks>Programs and tables borrow the unchanged supplied face. Decoder, machine and
    /// copied point/CVT storage are bounded, reserved before allocation and released on failure.
    /// The explicit target selects rasterizer queries; this internal stage draws no masks and
    /// supplies owned geometry to the managed coverage stage.</remarks>
    internal static OpenTypeGlyphContourStorage GetTrueTypeContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, TrueTypeHintTarget target, TrueTypeImpliedPointRounding impliedPointRounding, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        => GetTrueTypeContours(bytes, glyphIndex, metrics, deviceSize, target, impliedPointRounding, null, budget, work, token);

    /// <summary>Fits explicit points and applies an optional Windows device matrix before constructing implied curve points.</summary>
    /// <remarks>Shares the supplied-face interpreter and all ownership, work and cancellation contracts above. The immutable post-transform owns no storage; every transformed point charges work before contour lowering.</remarks>
    internal static OpenTypeGlyphContourStorage GetTrueTypeContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, TrueTypeHintTarget target, TrueTypeImpliedPointRounding impliedPointRounding, OpenTypeWindowsGlyphTransform? postTransform, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        if (impliedPointRounding is not TrueTypeImpliedPointRounding.Down and not TrueTypeImpliedPointRounding.None) throw new ArgumentOutOfRangeException(nameof(impliedPointRounding));
        if (!double.IsFinite(deviceSize) || deviceSize <= 0 || deviceSize > 32767) throw new SvgException(SvgErrorCode.LimitExceeded, "The TrueType device size exceeds its coordinate bound.");
        TrueTypeHinting.ValidateTarget(target);
        if (metrics.ForceIntegerHintingSize)
        {
            deviceSize = Math.Floor(deviceSize + .5);
            if (deviceSize == 0) return new([], [], null, null);
        }
        double requestedScale = Math.Floor(deviceSize * 64 * 65536 / metrics.UnitsPerEm + .5);
        if (requestedScale > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "The TrueType device scale exceeds its fixed-point bound.");
        int scale = checked((int)requestedScale);
        double horizontalSize = deviceSize * target.HorizontalRatio;
        double requestedHorizontalScale = Math.Floor(horizontalSize * 64 * 65536 / metrics.UnitsPerEm + .5);
        if (!double.IsFinite(horizontalSize) || horizontalSize > 32767 || requestedHorizontalScale > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The horizontal TrueType device scale exceeds its coordinate bound.");
        int horizontalScale = checked((int)requestedHorizontalScale);
        TrueTypeHintDeviceScale deviceScale = new(scale, horizontalScale);
        using OpenTypeSimpleGlyphStructureStorage structure = GetGlyphStructure(bytes, glyphIndex, metrics, budget, work, token, true);
        if (structure.CompositeHeader is not null)
        {
            (TrueTypeHintProfile compositeProfile, OpenTypeTableRange compositeCvt, OpenTypeTableRange compositeFont, OpenTypeTableRange compositePrep) = ReadHintTables();
            using CompositeTrueTypeHinting fitting = new(bytes, metrics, compositeProfile, compositeCvt, compositeFont, compositePrep, checked((int)Math.Floor(deviceSize + .5)), deviceScale, deviceSize, horizontalSize, target, budget, work, token);
            using OpenTypeGlyphPointOutline outline = GetGlyphPointOutline(bytes, glyphIndex, metrics, fitting, null, (target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) != 0 ? OpenTypeCompositeOffsetMath.DirectWrite : OpenTypeCompositeOffsetMath.NativeLength, budget, work, token);
            SubtractGlyphOrigin(outline.Points, outline.Phantoms?.LeftX ?? 0, work, token);
            MapPoints(outline.Points);
            using OpenTypeSegmentAccumulator compositeSegments = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, work, token);
            BuildQuadraticContours(outline.Points, outline.Ends, compositeSegments, true, impliedPointRounding, work, token);
            OpenTypeGlyphContourStorage result = compositeSegments.Detach();
            result.SmartDropouts = fitting.GlyphSmartDropouts;
            return result;
        }
        if (structure.Value.ContourEndpoints.Length == 0) return new([], [], null, null);
        using OpenTypeGlyphPointStorage decoded = GetSimpleGlyphPoints(bytes, structure.Value, budget, work, token);
        (TrueTypeHintProfile profile, OpenTypeTableRange cvtRange, OpenTypeTableRange fontProgram, OpenTypeTableRange preparation) = ReadHintTables();
        int pointCount = decoded.Points.Length, cvtCount = cvtRange.Length / 2;
        int ppem = checked((int)Math.Floor(deviceSize + .5));
        long scratchBytes = ParserCollectionMemory.GetArrayByteCount<TrueTypeHintPoint>(pointCount + 4) + ParserCollectionMemory.GetArrayByteCount<int>(cvtCount);
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        TrueTypeHintPoint[] points = new TrueTypeHintPoint[pointCount + 4];
        int[] cvt = new int[cvtCount];
        int controlScale = target.ControlScale == TrueTypeHintControlScale.FractionalFontUnits ? (scale >> 6) << 6 : scale;
        for (int i = 0; i < cvt.Length; i++) { work.Consume(token); cvt[i] = ScaleWith(ReadInt16(bytes.Span, cvtRange.Offset + i * 2), controlScale); }
        for (int i = 0; i < pointCount; i++) { work.Consume(token); points[i] = new(ScaleHorizontal(decoded.Points[i].X), Scale(decoded.Points[i].Y), decoded.Points[i].X, decoded.Points[i].Y) { IsOnCurve = decoded.Points[i].IsOnCurve }; }
        OpenTypeGlyphHeader header = ParseGlyphHeader(bytes, structure.Value.GlyphRange) ?? throw InvalidFont("A nonempty glyph has no header.");
        int advanceIndex = Math.Min(glyphIndex, metrics.NumberOfHMetrics - 1);
        int advance = ReadUInt16(bytes.Span, metrics.Hmtx.Offset + advanceIndex * 4);
        int bearingOffset = glyphIndex < metrics.NumberOfHMetrics ? metrics.Hmtx.Offset + glyphIndex * 4 + 2 : metrics.Hmtx.Offset + metrics.NumberOfHMetrics * 4 + (glyphIndex - metrics.NumberOfHMetrics) * 2;
        int origin = header.XMinimum - ReadInt16(bytes.Span, bearingOffset);
        points[pointCount] = new(ScaleHorizontal(origin), 0, origin, 0); points[pointCount + 1] = new(ScaleHorizontal(origin + advance), 0, origin + advance, 0);
        if ((target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) != 0)
        {
            points[pointCount].X = RoundTrueTypeHorizontalMetric(points[pointCount].X, target);
            points[pointCount + 1].X = RoundTrueTypeHorizontalMetric(points[pointCount + 1].X, target);
        }
        // Preserve indices without inventing vertical metrics. Executed access
        // to either vertical phantom point is an explicit unsupported outcome.
        points[pointCount + 2].Unavailable = points[pointCount + 3].Unavailable = true;
        bool smartDropouts;
        using (TrueTypeHinting machine = TrueTypeHinting.Create(profile, cvt, points, structure.Value.ContourEndpoints, ppem, deviceScale, target, budget, work, token))
        {
            machine.Run(bytes.Slice(fontProgram.Offset, fontProgram.Length), TrueTypeHintProgramKind.Font);
            ReadOnlyMemory<byte> prep = bytes.Slice(preparation.Offset, preparation.Length);
            if (target.Preparation == TrueTypeHintPreparation.DefaultThenTarget)
                machine.Prepare(prep, target with { QueryFlags = TrueTypeHintQueryFlags.None });
            machine.Prepare(prep, target);
            int lengthOffset = structure.Value.GlyphRange.Offset + 10 + structure.Value.ContourEndpoints.Length * 2;
            int instructionLength = ReadUInt16(bytes.Span, lengthOffset);
            machine.Run(bytes.Slice(lengthOffset + 2, instructionLength), TrueTypeHintProgramKind.Glyph);
            smartDropouts = machine.GlyphSmartDropouts;
        }
        for (int i = 0; i < pointCount; i++) { work.Consume(token); decoded.Points[i] = new(points[i].X, points[i].Y, points[i].IsOnCurve); }
        MapPoints(decoded.Points);
        using var segments = new OpenTypeSegmentAccumulator(budget, GlyphDecoderBudgetPhase.SimpleSegmentCapacity, GlyphDecoderBudgetPhase.SimpleSegmentExact, GlyphDecoderBudgetPhase.SimpleContourBoundaryCapacity, GlyphDecoderBudgetPhase.SimpleContourBoundaryExact, work, token);
        BuildQuadraticContours(decoded.Points, structure.Value.ContourEndpoints, segments, true, impliedPointRounding, work, token);
        OpenTypeGlyphContourStorage fitted = segments.Detach();
        fitted.SmartDropouts = smartDropouts;
        return fitted;

        void MapPoints(Span<OpenTypeGlyphPoint> points)
        {
            if (postTransform is not OpenTypeWindowsGlyphTransform transform) return;
            for (int index = 0; index < points.Length; index++)
            {
                work.Consume(token);
                points[index] = transform.ApplyFitted(points[index]);
            }
        }

        int Scale(int value) => ScaleWith(value, scale);
        int ScaleHorizontal(int value)
        {
            if ((target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) == 0) return ScaleWith(value, horizontalScale);
            return ScaleWindowsHorizontalPoint(value, horizontalSize, metrics.UnitsPerEm);
        }
        int ScaleWith(int value, int factor)
        {
            long product = (long)value * factor;
            long scaled = (Math.Abs(product) + 32768) >> 16;
            if (scaled > int.MaxValue + (product < 0 ? 1L : 0L)) throw new SvgException(SvgErrorCode.LimitExceeded, "A TrueType device coordinate exceeds its signed bound.");
            return checked((int)(product < 0 ? -scaled : scaled));
        }
        (TrueTypeHintProfile, OpenTypeTableRange, OpenTypeTableRange, OpenTypeTableRange) ReadHintTables()
        {
            OpenTypeTableRange maxp = default, cvt = default, fpgm = default, prep = default;
            int seen = 0;
            ReadOnlySpan<byte> data = bytes.Span; RequireRange(data, 0, 12, "SFNT header");
            int count = ReadUInt16(data, 4); if (count is < 1 or > MaximumTableRecords) throw InvalidFont("The hint table directory count is invalid.");
            RequireRange(data, 12, count * 16, "hint table directory");
            for (int i = 0; i < count; i++)
            {
                work.Consume(token); int offset = 12 + i * 16; uint tag = ReadUInt32(data, offset);
                if (tag is not 0x6D617870 and not 0x63767420 and not 0x6670676D and not 0x70726570) continue;
                int bit = tag switch { 0x6D617870 => 1, 0x63767420 => 2, 0x6670676D => 4, _ => 8 };
                if ((seen & bit) != 0) throw InvalidFont("A TrueType hint table is duplicated.");
                seen |= bit;
                uint start = ReadUInt32(data, offset + 8), length = ReadUInt32(data, offset + 12);
                if (start > int.MaxValue || length > int.MaxValue) throw InvalidFont("A TrueType hint table exceeds the input range.");
                RequireRange(data, (int)start, (int)length, "hint table"); OpenTypeTableRange range = new((int)start, (int)length);
                switch (tag) { case 0x6D617870: maxp = range; break; case 0x63767420: cvt = range; break; case 0x6670676D: fpgm = range; break; case 0x70726570: prep = range; break; }
            }
            RequireTableLength(maxp, 32, "TrueType maxp");
            if (ReadUInt32(data, maxp.Offset) != 0x10000) throw InvalidFont("TrueType hinting requires maxp version 1.0.");
            if (ReadUInt16(data, maxp.Offset + 14) is < 1 or > 2) throw InvalidFont("The TrueType zone count is invalid.");
            if (ReadUInt16(data, maxp.Offset + 22) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "TrueType instruction definitions are not implemented.");
            if ((cvt.Length & 1) != 0) throw InvalidFont("A TrueType control-value table has an odd byte length.");
            if (cvt.Length > 8192 || fpgm.Length > ushort.MaxValue || prep.Length > ushort.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "A TrueType hint table exceeds its supported byte bound.");
            return (new(ReadUInt16(data, maxp.Offset + 24), ReadUInt16(data, maxp.Offset + 18), ReadUInt16(data, maxp.Offset + 20), ReadUInt16(data, maxp.Offset + 16)), cvt, fpgm, prep);
        }
    }

    private static int RoundTrueTypeHorizontalMetric(int value, TrueTypeHintTarget target)
    {
        int grid = (target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) != 0 ? 4 : 64;
        long rounded = ((long)value + grid / 2) & -(long)grid;
        if (rounded < int.MinValue || rounded > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "A glyph metric exceeds its signed coordinate bound.");
        return (int)rounded;
    }

    /// <summary>Retains native initial horizontal point precision separately from the fixed-point interpreter scale.</summary>
    private static int ScaleWindowsHorizontalPoint(int value, double size, ushort unitsPerEm)
    {
        float scaled = value * ((float)size / unitsPerEm) * 64;
        double rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "A horizontal glyph point exceeds its fixed-point bound.");
        return (int)rounded;
    }
}
