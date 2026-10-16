namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal static partial class OpenTypeFont
{
    /// <summary>Fits children and their assembled parents under one supplied face's prepared interpreter state.</summary>
    /// <remarks>Font/preparation programs, CVT and function/storage changes survive calls within a strike. Transformed Windows children own a bounded strike and interpreter; identity children share the parent state. Each glyph owns temporary phantom/point scratch, and the machine releases all borrows before that scratch reservation ends. The context and machine belong to one conversion/thread and never acquire native services.</remarks>
    private sealed class CompositeTrueTypeHinting : IDisposable
    {
        private readonly ReadOnlyMemory<byte> _bytes;
        private readonly OpenTypeFontMetrics _metrics;
        private readonly TrueTypeHintDeviceScale _scale;
        private readonly double _horizontalSize, _deviceSize;
        private readonly TrueTypeHintProfile _profile;
        private readonly OpenTypeTableRange _cvtRange, _fontProgram, _preparation;
        internal double HorizontalRatio => _target.HorizontalRatio;
        private readonly TrueTypeHintTarget _target;
        private readonly RenderBudget _budget;
        private readonly ParserWorkCounter _work;
        private readonly CancellationToken _token;
        private readonly TrueTypeHinting _machine;
        private readonly RenderBudget.BudgetReservation _reservation;

        internal CompositeTrueTypeHinting(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, TrueTypeHintProfile profile, OpenTypeTableRange cvtRange, OpenTypeTableRange fontProgram, OpenTypeTableRange preparation, int ppem, TrueTypeHintDeviceScale scale, double deviceSize, double horizontalSize, TrueTypeHintTarget target, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            _bytes = bytes; _metrics = metrics; _scale = scale; _deviceSize = deviceSize; _horizontalSize = horizontalSize;
            _profile = profile; _cvtRange = cvtRange; _fontProgram = fontProgram; _preparation = preparation; _target = target; _budget = budget; _work = work; _token = token;
            _reservation = budget.Reserve(256, SvgMemoryCategory.Geometry);
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

        /// <summary>Creates a bounded child strike for a transformed Windows component; identity and other profiles share their existing interpreter.</summary>
        internal CompositeTrueTypeHinting? CreateComponent(OpenTypeAffineTransform transform)
        {
            if ((_target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) == 0 || transform == new OpenTypeAffineTransform(1, 0, 0, 1)) return null;
            double x = _horizontalSize * Math.Max(Math.Abs(transform.XX), Math.Abs(transform.YX));
            double y = _deviceSize * Math.Max(Math.Abs(transform.XY), Math.Abs(transform.YY));
            if (x == 0 || y == 0) return null;
            if (!double.IsFinite(x) || !double.IsFinite(y) || x > 32767 || y > 32767)
                throw new SvgException(SvgErrorCode.LimitExceeded, "A component strike exceeds its device bound.");
            int horizontal = checked((int)Math.Floor(x * 64 * 65536 / _metrics.UnitsPerEm + .5));
            int vertical = checked((int)Math.Floor(y * 64 * 65536 / _metrics.UnitsPerEm + .5));
            return new(_bytes, _metrics, _profile, _cvtRange, _fontProgram, _preparation, checked((int)Math.Floor(y + .5)), new(vertical, horizontal), y, x,
                _target with { HorizontalRatio = x / y }, _budget, _work, _token);
        }

        /// <summary>Returns a fitted child's coordinate to native fractional font units before component placement.</summary>
        internal OpenTypeGlyphPoint Unscale(OpenTypeGlyphPoint point)
        {
            int x = Divide(point.X, _scale.Horizontal >> 6), y = Divide(point.Y, _scale.Vertical >> 6);
            return new(x, y, point.IsOnCurve);

            static int Divide(int value, int coefficient)
            {
                if (coefficient == 0 && value == 0) return 0;
                if (coefficient <= 0) throw new SvgException(SvgErrorCode.LimitExceeded, "A component strike has no finite inverse scale.");
                long numerator = (long)value * 65536;
                return checked((int)(Math.Sign(numerator) * ((Math.Abs(numerator) + coefficient / 2) / coefficient)));
            }
        }

        /// <summary>Restores a placed fractional-font coordinate to the parent strike, rounding each matrix product independently.</summary>
        internal OpenTypeGlyphPoint ScalePlaced(OpenTypeGlyphPoint point)
            => new(Scale(point.X, _scale.Horizontal >> 6), Scale(point.Y, _scale.Vertical >> 6), point.IsOnCurve);

        internal int ScaleOffset(OpenTypeCompositeGlyphComponent component, bool horizontal)
        {
            _work.Consume(_token);
            bool windows = (_target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) != 0;
            bool round = (component.Flags & 4) != 0;
            OpenTypeOutlinePoint offset = component.GetOffset(OpenTypeCompositeOffsetMath.NativeLength);
            int value = windows ? horizontal ? component.HorizontalOffset : component.VerticalOffset : checked((int)(horizontal ? offset.X : offset.Y));
            int scaled = horizontal && windows ? ScaleWindowsHorizontalPoint(value, _horizontalSize, _metrics.UnitsPerEm)
                : Scale(value, horizontal ? _scale.Horizontal : _scale.Vertical);
            if (windows && (component.Flags & 2048) != 0)
                scaled = checked((int)Math.Round(scaled * (horizontal ? Math.Max(Math.Abs(component.Transform.XX), Math.Abs(component.Transform.YX)) : Math.Max(Math.Abs(component.Transform.XY), Math.Abs(component.Transform.YY))), MidpointRounding.AwayFromZero));
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
            points[firstPhantom].X = RoundMetric(initial.LeftX);
            points[firstPhantom + 1].X = RoundMetric(initial.RightX);
            points[firstPhantom + 2].Unavailable = points[firstPhantom + 3].Unavailable = true;
            _machine.HintGlyph(_bytes.Slice(instructions.Offset, instructions.Length), points, ends, composite);
            for (int i = 0; i < outline.Length; i++) { _work.Consume(_token); outline[i] = new(points[i].X, points[i].Y, outline[i].IsOnCurve); }
            // The native FreeType v40 policy and instruction-free parents preserve
            // unrounded metrics; Windows parents retain their fitted sixteenth-pixel origin.
            return _target.Version == 40 || composite && instructions.Length == 0 && (_target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) == 0 ? initial : new(points[firstPhantom].X, points[firstPhantom].Y, points[firstPhantom + 1].X, points[firstPhantom + 1].Y);
        }

        private int RoundMetric(int value)
        {
            if ((_target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) == 0) return Round(value);
            long rounded = ((long)value + 2) & ~3L;
            if (rounded < int.MinValue || rounded > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "A glyph metric exceeds its signed coordinate bound.");
            return (int)rounded;
        }

        private static int Round(int value)
        {
            long rounded = ((long)value + 32) & ~63L;
            if (rounded < int.MinValue || rounded > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "A rounded composite offset exceeds its signed bound.");
            return (int)rounded;
        }

        private int ScaleHorizontal(int value) => (_target.QueryFlags & TrueTypeHintQueryFlags.Symmetric) == 0
            ? Scale(value, _scale.Horizontal) : ScaleWindowsHorizontalPoint(value, _horizontalSize, _metrics.UnitsPerEm);
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
