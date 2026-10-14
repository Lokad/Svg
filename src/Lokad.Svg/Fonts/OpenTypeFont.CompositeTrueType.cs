namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal static partial class OpenTypeFont
{
    /// <summary>Fits children and their assembled parents under one supplied face's prepared interpreter state.</summary>
    /// <remarks>Font/preparation programs, CVT and function/storage changes survive component calls. Each glyph owns temporary phantom/point scratch, and the machine releases all borrows before that scratch reservation ends. The context and machine belong to one conversion/thread and never acquire native services.</remarks>
    private sealed class CompositeTrueTypeHinting : IDisposable
    {
        private readonly ReadOnlyMemory<byte> _bytes;
        private readonly OpenTypeFontMetrics _metrics;
        private readonly TrueTypeHintDeviceScale _scale;
        internal double HorizontalRatio => _target.HorizontalRatio;
        private readonly TrueTypeHintTarget _target;
        private readonly RenderBudget _budget;
        private readonly ParserWorkCounter _work;
        private readonly CancellationToken _token;
        private readonly TrueTypeHinting _machine;
        private readonly RenderBudget.BudgetReservation _reservation;

        internal CompositeTrueTypeHinting(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, TrueTypeHintProfile profile, OpenTypeTableRange cvtRange, OpenTypeTableRange fontProgram, OpenTypeTableRange preparation, int ppem, TrueTypeHintDeviceScale scale, TrueTypeHintTarget target, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            _bytes = bytes; _metrics = metrics; _scale = scale; _target = target; _budget = budget; _work = work; _token = token;
            _reservation = budget.Reserve(192, SvgMemoryCategory.Geometry);
            try
            {
                int count = cvtRange.Length / 2;
                using RenderBudget.BudgetReservation? scratch = count == 0 ? null : budget.Reserve(ParserCollectionMemory.GetArrayByteCount<int>(count), SvgMemoryCategory.Geometry);
                int[] cvt = new int[count];
                int controlScale = target.ControlScale == TrueTypeHintControlScale.FractionalFontUnits ? (scale.Vertical >> 6) << 6 : scale.Vertical;
                for (int i = 0; i < count; i++) { work.Consume(token); cvt[i] = Scale(ReadInt16(bytes.Span, cvtRange.Offset + i * 2), controlScale); }
                _machine = TrueTypeHinting.Create(profile, cvt, [], [], ppem, scale, target, budget, work, token);
                try
                {
                    _machine.Run(bytes.Slice(fontProgram.Offset, fontProgram.Length), TrueTypeHintProgramKind.Font);
                    ReadOnlyMemory<byte> prep = bytes.Slice(preparation.Offset, preparation.Length);
                    if (target.Preparation == TrueTypeHintPreparation.DefaultThenTarget) _machine.Prepare(prep, target with { QueryFlags = TrueTypeHintQueryFlags.None });
                    _machine.Prepare(prep, target);
                }
                catch { _machine.Dispose(); throw; }
            }
            catch { _reservation.Dispose(); throw; }
        }

        internal int ScaleOffset(int value, bool round, bool horizontal)
        {
            _work.Consume(_token);
            int scaled = horizontal ? ScaleHorizontal(value) : Scale(value, _scale.Vertical);
            // ROUND_XY_TO_GRID uses sixteenth-pixel X placement in the
            // Windows subpixel profile, separately from whole-pixel Y fitting.
            if (round && horizontal && (_target.QueryFlags & (TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned)) == (TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned))
            {
                long rounded = ((long)scaled + 2) & ~3L;
                if (rounded < int.MinValue || rounded > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "A rounded composite offset exceeds its signed bound.");
                return (int)rounded;
            }
            // Legacy grayscale rounds both axes; the admitted smooth profiles retain fractional X.
            return round && (!horizontal || _target.Version == 35) ? Round(scaled) : scaled;
        }

        internal bool GlyphSmartDropouts => _machine.GlyphSmartDropouts;

        /// <summary>Fits borrowed points in place and returns horizontal phantom coordinates for metric inheritance. Selected device metrics are copied without component placement; scratch is bounded and released on every exit.</summary>
        internal OpenTypeHorizontalPhantoms Fit(ushort glyph, OpenTypeGlyphPoint[] outline, ushort[] ends, OpenTypeTableRange range, OpenTypeTableRange instructions, bool composite, OpenTypeHorizontalPhantoms? selectedMetrics)
        {
            _work.Consume(_token);
            // An instruction-free parent keeps its selected child's completed metric state.
            if (selectedMetrics is {} selected && instructions.Length == 0) return selected;
            using RenderBudget.BudgetReservation scratch = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<TrueTypeHintPoint>(outline.Length + 4), SvgMemoryCategory.Geometry);
            TrueTypeHintPoint[] points = new TrueTypeHintPoint[outline.Length + 4];
            for (int i = 0; i < outline.Length; i++)
            {
                _work.Consume(_token);
                OpenTypeGlyphPoint point = outline[i];
                points[i] = composite ? new(point.X, point.Y, point.X, point.Y) : new(ScaleHorizontal(point.X), Scale(point.Y, _scale.Vertical), point.X, point.Y);
            }
            OpenTypeGlyphHeader? header = ParseGlyphHeader(_bytes, range);
            int advanceIndex = Math.Min(glyph, _metrics.NumberOfHMetrics - 1);
            int advance = ReadUInt16(_bytes.Span, _metrics.Hmtx.Offset + advanceIndex * 4);
            int bearingOffset = glyph < _metrics.NumberOfHMetrics ? _metrics.Hmtx.Offset + glyph * 4 + 2 : _metrics.Hmtx.Offset + _metrics.NumberOfHMetrics * 4 + (glyph - _metrics.NumberOfHMetrics) * 2;
            int origin = (header?.XMinimum ?? 0) - ReadInt16(_bytes.Span, bearingOffset);
            int firstPhantom = outline.Length;
            int scaledOrigin = ScaleHorizontal(origin), scaledAdvance = ScaleHorizontal(origin + advance);
            OpenTypeHorizontalPhantoms initial = selectedMetrics ?? new(scaledOrigin, 0, scaledAdvance, 0);
            points[firstPhantom] = new(initial.LeftX, initial.LeftY, composite ? initial.LeftX : origin, composite ? initial.LeftY : 0);
            points[firstPhantom + 1] = new(initial.RightX, initial.RightY, composite ? initial.RightX : origin + advance, composite ? initial.RightY : 0);
            points[firstPhantom].X = Round(initial.LeftX);
            points[firstPhantom + 1].X = Round(initial.RightX);
            points[firstPhantom + 2].Unavailable = points[firstPhantom + 3].Unavailable = true;
            _machine.HintGlyph(_bytes.Slice(instructions.Offset, instructions.Length), points, ends, composite);
            for (int i = 0; i < outline.Length; i++) { _work.Consume(_token); outline[i] = new(points[i].X, points[i].Y, outline[i].IsOnCurve); }
            // The native v40 compatibility policy preserves the unrounded metrics.
            return _target.Version == 40 ? initial : new(points[firstPhantom].X, points[firstPhantom].Y, points[firstPhantom + 1].X, points[firstPhantom + 1].Y);
        }

        private static int Round(int value)
        {
            long rounded = ((long)value + 32) & ~63L;
            if (rounded < int.MinValue || rounded > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "A rounded composite offset exceeds its signed bound.");
            return (int)rounded;
        }

        private int ScaleHorizontal(int value) => Scale(value, _scale.Horizontal);
        private static int Scale(int value, int factor)
        {
            long product = (long)value * factor;
            long scaled = (Math.Abs(product) + 32768) >> 16;
            if (scaled > int.MaxValue + (product < 0 ? 1L : 0L)) throw new SvgException(SvgErrorCode.LimitExceeded, "A composite device coordinate exceeds its signed bound.");
            return checked((int)(product < 0 ? -scaled : scaled));
        }

        public void Dispose() { _machine.Dispose(); _reservation.Dispose(); }
    }
}
