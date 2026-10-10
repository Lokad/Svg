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
        private readonly int _scale;
        private readonly TrueTypeHintTarget _target;
        private readonly RenderBudget _budget;
        private readonly ParserWorkCounter _work;
        private readonly CancellationToken _token;
        private readonly TrueTypeHinting _machine;
        private readonly RenderBudget.BudgetReservation _reservation;

        internal CompositeTrueTypeHinting(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, TrueTypeHintProfile profile, OpenTypeTableRange cvtRange, OpenTypeTableRange fontProgram, OpenTypeTableRange preparation, int ppem, int scale, TrueTypeHintTarget target, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            _bytes = bytes; _metrics = metrics; _scale = scale; _target = target; _budget = budget; _work = work; _token = token;
            _reservation = budget.Reserve(192, SvgMemoryCategory.Geometry);
            try
            {
                int count = cvtRange.Length / 2;
                using RenderBudget.BudgetReservation? scratch = count == 0 ? null : budget.Reserve(ParserCollectionMemory.GetArrayByteCount<int>(count), SvgMemoryCategory.Geometry);
                int[] cvt = new int[count];
                int controlScale = target.ControlScale == TrueTypeHintControlScale.FractionalFontUnits ? (scale >> 6) << 6 : scale;
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
            int scaled = Scale(value, _scale);
            // Legacy grayscale rounds both axes; the admitted smooth profiles retain fractional X.
            return round && (!horizontal || _target.Version == 35) ? Round(scaled) : scaled;
        }

        internal bool GlyphSmartDropouts => _machine.GlyphSmartDropouts;

        internal void Fit(ushort glyph, OpenTypeGlyphPoint[] outline, ushort[] ends, OpenTypeTableRange range, OpenTypeTableRange instructions, bool composite)
        {
            _work.Consume(_token);
            if (range.Length == 0) return;
            using RenderBudget.BudgetReservation scratch = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<TrueTypeHintPoint>(outline.Length + 4), SvgMemoryCategory.Geometry);
            TrueTypeHintPoint[] points = new TrueTypeHintPoint[outline.Length + 4];
            for (int i = 0; i < outline.Length; i++)
            {
                _work.Consume(_token);
                OpenTypeGlyphPoint point = outline[i];
                points[i] = composite ? new(point.X, point.Y, point.X, point.Y) : new(Scale(point.X, _scale), Scale(point.Y, _scale), point.X, point.Y);
            }
            OpenTypeGlyphHeader header = ParseGlyphHeader(_bytes, range) ?? throw InvalidFont("A nonempty glyph has no header.");
            int advanceIndex = Math.Min(glyph, _metrics.NumberOfHMetrics - 1);
            int advance = ReadUInt16(_bytes.Span, _metrics.Hmtx.Offset + advanceIndex * 4);
            int bearingOffset = glyph < _metrics.NumberOfHMetrics ? _metrics.Hmtx.Offset + glyph * 4 + 2 : _metrics.Hmtx.Offset + _metrics.NumberOfHMetrics * 4 + (glyph - _metrics.NumberOfHMetrics) * 2;
            int origin = header.XMinimum - ReadInt16(_bytes.Span, bearingOffset);
            int firstPhantom = outline.Length;
            int scaledOrigin = Scale(origin, _scale), scaledAdvance = Scale(origin + advance, _scale);
            points[firstPhantom] = new(scaledOrigin, 0, composite ? scaledOrigin : origin, 0);
            points[firstPhantom + 1] = new(scaledAdvance, 0, composite ? scaledAdvance : origin + advance, 0);
            if (composite)
            {
                points[firstPhantom].X = Round(scaledOrigin);
                points[firstPhantom + 1].X = Round(scaledAdvance);
            }
            points[firstPhantom + 2].Unavailable = points[firstPhantom + 3].Unavailable = true;
            _machine.HintGlyph(_bytes.Slice(instructions.Offset, instructions.Length), points, ends, composite);
            for (int i = 0; i < outline.Length; i++) { _work.Consume(_token); outline[i] = new(points[i].X, points[i].Y, outline[i].IsOnCurve); }
        }

        private static int Round(int value)
        {
            long rounded = ((long)value + 32) & ~63L;
            if (rounded < int.MinValue || rounded > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "A rounded composite offset exceeds its signed bound.");
            return (int)rounded;
        }

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
