// Adapts Blink's enclosed source text drawing bounds before mapped replay.
// Copyright 2017, 2018 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Shares native drawing bounds and paint references across one run's glyphs and decorations.</summary>
/// <remarks>This parser-local value owns no storage. Fonts remain borrowed and the scene owns retained
/// paint metadata. Measurement and publication charge the conversion's work, memory and cancellation;
/// callers use the value on the parser thread and preserve separate filter-source bounds.</remarks>
internal struct SvgTextPaintPreparation
{
    internal SvgRectangle? SourceBox;
    // The text element shares shader coordinates; drawing enclosures remain fragment-local.
    internal SvgRectangle? ObjectBox;
    internal SvgPaintBounds? FilterVisualBounds;
    internal SvgPaintBounds? ElementVisualBounds;
    private SvgPaintBounds? _drawingBounds;
    private SvgRecordedPaint _lastPaint;
    internal bool NativeDrawing { get; private set; }

    internal static SvgTextPaintPreparation Create(SvgTextRun run, ParserWorkCounter work, CancellationToken token)
    {
        SvgTextPaintPreparation prepared = new() { NativeDrawing = !run.Glyphs.IsEmpty };
        for (int index = 0; prepared.NativeDrawing && index < run.Glyphs.Length; index++)
        {
            work.Consume(token);
            SvgGlyphFontStyle face = run.GetGlyphFont(index);
            prepared.NativeDrawing = SvgTextLayoutBounds.UsesFittedMetrics(face.Font,
                SvgGlyphDeviceMetrics.GetEffectiveFontSize(face.FontSize * (run.DeviceMetrics?.Scale ?? 1)));
        }
        return prepared;
    }

    internal SvgRecordedPaint RecordDrawing(SvgTextRun run, SvgAffineTransform placement, ReadOnlySpan<SvgPlacedTextGlyph> paths,
        SvgDocumentState state, SvgColor fallback, float alpha, SvgRecordedTransform? recording, ParserWorkCounter work, CancellationToken token)
    {
        if (_drawingBounds is null)
        {
            SvgRectangle box = SourceBox ??= SvgTextLayoutBounds.Measure(run, paths, placement, state.TextBoundsMetrics, work, token);
            float inflation = 0;
            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                work.Consume(token);
                if (run.GetGlyphPaint(index, fallback).Stroke?.Resolve() is {} stroke) inflation = MathF.Max(inflation, (float)stroke.Width);
            }
            _drawingBounds = new SvgPaintBounds(MathF.Floor((float)box.X - inflation), MathF.Floor((float)box.Y - inflation),
                MathF.Ceiling((float)(box.X + box.Width) + inflation), MathF.Ceiling((float)(box.Y + box.Height) + inflation)).Map(placement, work, token);
        }
        SvgRecordedPaintValue previous = state.Scene.GetRecordedPaintValue(_lastPaint);
        if (_lastPaint.Identity == 0 || previous.Alpha != alpha || !ReferenceEquals(previous.RecordingTransform, recording))
            _lastPaint = state.Scene.RegisterRecordedPaint(new(state.NewPaintOperationIdentity(), alpha, false, new SvgRecordingScope(1),
                SvgPaintOpacity.Opaque, new SvgRecordingScope(1)) { RecordingTransform = recording, VisualBounds = _drawingBounds });
        return _lastPaint;
    }
}
