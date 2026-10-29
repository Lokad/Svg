namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Maps original font points through the independently measured Windows unhinted fixed-point transform.</summary>
/// <remarks>This immutable value owns no storage and is safe for concurrent callers. Matrix normalization precedes per-product 26.6 rounding; implied points are constructed afterward. Arithmetic is bounded independently of the face. The caller checks cancellation for each visited point.</remarks>
internal readonly record struct OpenTypeWindowsGlyphTransform(int XX, int XY, int YX, int YY)
{
    internal static OpenTypeWindowsGlyphTransform Create(SvgGlyphStrikeTransform strike, ushort unitsPerEm)
    {
        if (unitsPerEm == 0) throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped font has no native scale divisor.");
        return new(Coefficient((float)strike.RemainingMap.A), Coefficient(-(float)strike.RemainingMap.C),
            Coefficient(-(float)strike.RemainingMap.B), Coefficient((float)strike.RemainingMap.D));

        int Coefficient(float value)
        {
            float matrix = MathF.Round(strike.DeviceSize * value * 65536, MidpointRounding.AwayFromZero);
            if (!float.IsFinite(matrix) || (double)matrix < int.MinValue || (double)matrix > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped font matrix exceeds its fixed-point bound.");
            long fixedValue = (int)matrix;
            return (int)(Math.Sign(fixedValue) * ((Math.Abs(fixedValue) + unitsPerEm / 2) / unitsPerEm));
        }
    }

    internal OpenTypeGlyphPoint Apply(OpenTypeGlyphPoint point)
        => ApplyFixed(point, (long)point.X * 64, (long)point.Y * 64);

    internal OpenTypeGlyphPoint ApplyFitted(OpenTypeGlyphPoint point)
        => ApplyFixed(point, point.X, point.Y);

    private OpenTypeGlyphPoint ApplyFixed(OpenTypeGlyphPoint point, long x, long y)
    {
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "An original glyph point exceeds its fixed-point bound.");
        long mappedX = Multiply(x, XX) + Multiply(y, XY), mappedY = Multiply(x, YX) + Multiply(y, YY);
        if (mappedX < int.MinValue || mappedX > int.MaxValue || mappedY < int.MinValue || mappedY > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "A mapped glyph point exceeds its signed bound.");
        return new((int)mappedX, (int)mappedY, point.IsOnCurve);

        static long Multiply(long value, int coefficient)
        {
            long product = value * coefficient;
            return Math.Sign(product) * ((Math.Abs(product) + 32768) >> 16);
        }
    }
}

internal static partial class OpenTypeFont
{
    /// <summary>Returns owned ordinary Windows contours for a font-selected unfitted range, preserving native size and initial-point scaling.</summary>
    /// <remarks>Shares bounded component assembly, metric inheritance and contour ownership with the other device loaders. Material scanner state uses the existing interpreter; its fitted points are discarded. Programs without scanner effects remain unnecessary. Outline structure, cancellation and cleanup remain enforced. Borrows immutable font bytes and retains no storage beyond the returned exclusive owner.</remarks>
    internal static OpenTypeGlyphContourStorage GetWindowsUnfittedContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, RenderBudget budget, CancellationToken token)
        => GetWindowsUnfittedContours(bytes, glyphIndex, metrics, deviceSize, 1, null, budget, token);

    /// <summary>Applies an explicit horizontal strike ratio and optional native point transform before implied-point construction, sharing the unfitted ownership and budget contract.</summary>
    internal static OpenTypeGlyphContourStorage GetWindowsUnfittedContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, double horizontalRatio, OpenTypeWindowsGlyphTransform? postTransform, RenderBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(deviceSize) || deviceSize <= 0 || deviceSize > 32767 || !double.IsFinite(horizontalRatio) || horizontalRatio <= 0)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The unfitted Windows font size exceeds its device bound.");
        if (metrics.ForceIntegerHintingSize)
        {
            deviceSize = Math.Floor(deviceSize + .5);
            if (deviceSize == 0) return new([], [], null, null);
        }
        (bool smart, bool simple, bool excludeStubs) = SelectScanner();
        OpenTypeGlyphContourStorage result = GetUnhintedDeviceContours(bytes, glyphIndex, metrics, OpenTypeUnhintedDeviceScale.CreateWindows(deviceSize, deviceSize * horizontalRatio, metrics.UnitsPerEm), OpenTypeCompositeOffsetMath.DirectWrite, TrueTypeImpliedPointRounding.None, postTransform, budget, token);
        result.SmartDropouts = smart; result.SimpleDropouts = simple; result.ExcludeDropoutStubs = excludeStubs;
        return result;

        (bool Smart, bool Simple, bool ExcludeStubs) SelectScanner()
        {
            bool hasPrograms = false, required = false;
            ReadOnlySpan<byte> data = bytes.Span;
            int count = ReadUInt16(data, 4);
            RequireRange(data, 12, count * 16, "scanner table directory");
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                int at = 12 + index * 16;
                if (ReadUInt32(data, at) is not 0x6670676D and not 0x70726570) continue;
                uint offset = ReadUInt32(data, at + 8), length = ReadUInt32(data, at + 12);
                if (offset > int.MaxValue || length > int.MaxValue) throw InvalidFont("A Windows scanner program exceeds the input range.");
                RequireRange(data, (int)offset, (int)length, "scanner program");
                required |= NeedsScanner(bytes.Slice((int)offset, (int)length));
            }
            if (!required)
            {
                using OpenTypeGlyphRecursionTracker active = new(MaximumCompositeGlyphDepth + 1, budget, token);
                required = Visit(glyphIndex, 0, active);
            }
            if (required)
            {
                TrueTypeHintTarget target = new OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode.Symmetric, false).CreateHintTarget(horizontalRatio);
                using OpenTypeGlyphContourStorage scan = GetTrueTypeContours(bytes, glyphIndex, metrics, deviceSize, target, TrueTypeImpliedPointRounding.None, budget, token);
                return (scan.SmartDropouts, scan.SimpleDropouts, scan.ExcludeDropoutStubs);
            }
            bool enabled = Math.Floor(deviceSize + .5) <= 32;
            return (enabled && hasPrograms, enabled && !hasPrograms, false);

            bool Visit(ushort glyph, int depth, OpenTypeGlyphRecursionTracker active)
            {
                token.ThrowIfCancellationRequested();
                if (depth > MaximumCompositeGlyphDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "The scanner glyph nesting depth exceeds the supported limit.");
                if (!active.TryEnter(glyph)) throw InvalidFont("The scanner glyph graph contains a cycle.");
                try
                {
                    using OpenTypeSimpleGlyphStructureStorage structure = GetGlyphStructure(bytes, glyph, metrics, budget, token, true);
                    OpenTypeTableRange range = structure.Value.GlyphRange;
                    if (structure.CompositeHeader is null)
                    {
                        if (range.Length <= 10) return false;
                        int at = range.Offset + 10 + structure.Value.ContourEndpoints.Length * 2;
                        return NeedsScanner(bytes.Slice(at + 2, ReadUInt16(bytes.Span, at)));
                    }
                    using OpenTypeCompositeGlyphComponentStorage components = GetBudgetedCompositeGlyphComponents(bytes, glyph, metrics, budget, token);
                    if (NeedsScanner(bytes.Slice(components.Instructions.Offset, components.Instructions.Length))) return true;
                    foreach (OpenTypeCompositeGlyphComponent component in components.Components)
                        if (Visit(component.GlyphIndex, depth + 1, active)) return true;
                    return false;
                }
                finally { active.Exit(glyph); }
            }

            bool NeedsScanner(ReadOnlyMemory<byte> program)
            {
                hasPrograms |= program.Length != 0;
                ReadOnlySpan<byte> code = program.Span;
                for (int at = 0; at < code.Length;)
                {
                    token.ThrowIfCancellationRequested();
                    byte opcode = code[at++];
                    // Relative jumps can enter pushed data; conservatively use
                    // the interpreter instead of assuming lexical reachability.
                    if (opcode is 0x85 or 0x8D or 0x1C or 0x78 or 0x79) return true;
                    int skip = opcode is >= 0xB0 and <= 0xB7 ? opcode - 0xB0 + 1 : opcode is >= 0xB8 and <= 0xBF ? (opcode - 0xB8 + 1) * 2 : 0;
                    if (opcode is 0x40 or 0x41)
                    {
                        if (at == code.Length) return false;
                        token.ThrowIfCancellationRequested();
                        skip = code[at++] * (opcode == 0x40 ? 1 : 2);
                    }
                    // An unused truncated operand does not make scanner state
                    // necessary. Required programs retain full VM validation.
                    if (skip > code.Length - at) return false;
                    token.ThrowIfCancellationRequested();
                    at += skip;
                }
                return false;
            }
        }
    }

    /// <summary>Returns owned Windows unhinted contours after fixed-point mapping and before strike placement.</summary>
    /// <remarks>Borrows the unchanged supplied face and reuses bounded component assembly. Point and segment owners reserve before allocation and release on success, denial and cancellation. Each decoded or mapped point and lowered contour observes cancellation. No instructions, native services or host fonts are acquired; calls with separate conversion owners may run concurrently.</remarks>
    internal static OpenTypeGlyphContourStorage GetWindowsUnhintedContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, SvgGlyphStrikeTransform strike, bool hasPrograms, RenderBudget budget, CancellationToken token)
    {
        OpenTypeWindowsGlyphTransform transform = OpenTypeWindowsGlyphTransform.Create(strike, metrics.UnitsPerEm);
        using OpenTypeGlyphPointOutline outline = GetGlyphPointOutline(bytes, glyphIndex, metrics, null, null, OpenTypeCompositeOffsetMath.DirectWrite, budget, token);
        // Native mapping rounds the stored points and metric origin separately.
        // Rebasing before those products changes valid nonzero-origin outlines.
        OpenTypeGlyphPoint origin = transform.Apply(new(GetHorizontalOrigin(bytes, glyphIndex, metrics, token), 0, true));
        for (int index = 0; index < outline.Points.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            OpenTypeGlyphPoint point = transform.Apply(outline.Points[index]);
            long x = (long)point.X - origin.X, y = (long)point.Y - origin.Y;
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "A mapped glyph origin exceeds its signed bound.");
            outline.Points[index] = new((int)x, (int)y, point.IsOnCurve);
        }
        using OpenTypeSegmentAccumulator segments = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, token);
        BuildQuadraticContours(outline.Points, outline.Ends, segments, true, TrueTypeImpliedPointRounding.None, token);
        OpenTypeGlyphContourStorage result = segments.Detach();
        result.SmartDropouts = hasPrograms;
        result.SimpleDropouts = !hasPrograms && MathF.Floor(strike.DeviceSize + .5f) <= 32;
        return result;
    }
}
