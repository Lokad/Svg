namespace Lokad.Svg.Fonts;

using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Runs normalization and selected font features in shared, source-partitioned glyph storage.</summary>
internal static class SvgTextShaping
{
    /// <summary>Lends empty storage when neither normalization nor selected run features require work; otherwise owns glyphs until layout finishes.</summary>
    /// <remarks>Font programs and source styles remain borrowed. Only source partitions selecting
    /// the same physical face/size, paint and spacing may share substitutions or attachment.
    /// Absolute positions and native scaled-element boundaries also stop shaping. Placement follows retained source clusters;
    /// text-path transitions do not add a shaping boundary. All material capacity
    /// is charged to the caller's budget and released on refusal, cancellation or failed validation.
    /// Callers serialize the operation and keep every borrowed font alive through layout.</remarks>
    internal static OwnedParserArray<SvgShapingGlyph> Create(SvgPreparedFont defaultFont, double defaultSize, string text,
        double defaultSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> spacingSpans,
        ReadOnlySpan<SvgTextPositionAdjustment> positions, ReadOnlySpan<SvgTextPaintSpan> paints,
        ReadOnlySpan<SvgTextResolvedFontSpan> fonts, ReadOnlySpan<SvgTextNodeBoundary> nodes,
        SvgGlyphDeviceMetrics? metrics,
        RenderBudget budget, CancellationToken token)
    {
        if (text.Length == 0) return new([], null);
        bool required = false;
        foreach (Rune scalar in text.EnumerateRunes())
        {
            token.ThrowIfCancellationRequested();
            if (SvgCanonicalText.RequiresNormalization(scalar)) { required = true; break; }
        }
        if (!required)
        {
            if (fonts.IsEmpty) required = HasRunFeatures(defaultFont, token);
            else foreach (SvgTextResolvedFontSpan span in fonts)
            {
                token.ThrowIfCancellationRequested();
                if (span.End > span.Start && HasRunFeatures(span.Style.Font, token)) { required = true; break; }
            }
        }
        if (!required) return new([], null);
        using BudgetedParserList<SvgShapingGlyph> output = new(budget, SvgMemoryCategory.Geometry, token);
        int fontIndex = 0, paintIndex = 0, spacingIndex = 0, positionIndex = 0, nodeIndex = 0;
        for (int source = 0; source < text.Length;)
        {
            token.ThrowIfCancellationRequested();
            SvgGlyphFontStyle face = new(defaultFont, defaultSize);
            double spacing = defaultSpacing;
            int end = text.Length;
            if (!fonts.IsEmpty)
            {
                while (fonts[fontIndex].End <= source) fontIndex++;
                face = fonts[fontIndex].Style;
                end = Math.Min(end, fonts[fontIndex].End);
            }
            if (!paints.IsEmpty)
            {
                while (paints[paintIndex].End <= source) paintIndex++;
                end = Math.Min(end, paints[paintIndex].End);
            }
            if (!spacingSpans.IsEmpty)
            {
                while (spacingSpans[spacingIndex].End <= source) spacingIndex++;
                spacing = spacingSpans[spacingIndex].LetterSpacing;
                end = Math.Min(end, spacingSpans[spacingIndex].End);
            }
            while (positionIndex < positions.Length && (positions[positionIndex].Start <= source || !positions[positionIndex].HasAbsolutePosition)) positionIndex++;
            if (positionIndex < positions.Length) end = Math.Min(end, positions[positionIndex].Start);
            while (nodeIndex < nodes.Length && (nodes[nodeIndex].Offset <= source || !nodes[nodeIndex].ChangesElement)) nodeIndex++;
            if (metrics is {} native && (float)face.FontSize * (float)native.Scale != (float)face.FontSize && nodeIndex < nodes.Length)
                end = Math.Min(end, nodes[nodeIndex].Offset);
            int first = output.Count;
            bool normalizeRun = false;
            foreach (Rune scalar in text.AsSpan(source, end - source).EnumerateRunes())
            {
                token.ThrowIfCancellationRequested();
                if (SvgCanonicalText.RequiresNormalization(scalar)) { normalizeRun = true; break; }
            }
            if (normalizeRun) SvgCanonicalText.AppendGlyphs(face.Font, text, source, end, output, token);
            else AppendCmap(face.Font, text, source, end, output, token);
            if (output.Count > SvgTextLimits.MaximumCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Canonical text expansion exceeds the admitted glyph limit.");
            int canonicalCount = OpenTypeFont.ApplyCanonicalSubstitutionsForRun(face.Font.Bytes, face.Font.Metrics, output.AsMutableSpan()[first..], token);
            while (output.Count > first + canonicalCount) { token.ThrowIfCancellationRequested(); output.RemoveLast(); }
            if (spacing == 0 && OpenTypeFont.GetLatinLigatures(face.Font.Bytes, face.Font.Metrics, token) is {} ligatures)
            {
                int count = OpenTypeFont.ApplyLatinLigatures(face.Font.Bytes, ligatures, output.AsMutableSpan()[first..], token);
                while (output.Count > first + count) { token.ThrowIfCancellationRequested(); output.RemoveLast(); }
            }
            Span<SvgShapingGlyph> run = output.AsMutableSpan()[first..];
            SvgCanonicalText.MergeClusters(run, token);
            bool attachesMarks = false;
            foreach (SvgShapingGlyph glyph in run)
            {
                token.ThrowIfCancellationRequested();
                attachesMarks |= !glyph.IsDefaultIgnorable && SvgCanonicalText.GetProperties(glyph.Scalar).IsMark;
            }
            OpenTypeFont.ApplyContextualAdvancesForRun(face.Font.Bytes, face.Font.Metrics, run, token);
            if (attachesMarks && OpenTypeFont.GetMarkPositioning(face.Font.Bytes, face.Font.Metrics, token) is {} marks)
                OpenTypeFont.AttachMarks(face.Font.Bytes, marks, run, token);
            for (int index = 0; index < run.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgShapingGlyph glyph = run[index];
                if (glyph.GlyphIndex != 0)
                {
                    OpenTypeFont.ValidateShapedGlyphPositioning(face.Font.Bytes, glyph, face.Font.Metrics, token);
                }
                if (glyph.IsDefaultIgnorable) { glyph = glyph with { ContextAdvance = 0 }; run[index] = glyph; }
                if (glyph.Attachment.Parent >= 0)
                    // The admitted Latin shaping route zeros mark advances
                    // after GPOS, including advances from matched contexts.
                    run[index] = glyph with { ContextAdvance = 0, Attachment = glyph.Attachment with { Parent = first + glyph.Attachment.Parent } };
            }
            source = end;
        }
        return output.Detach();

        static bool HasRunFeatures(SvgPreparedFont font, CancellationToken token)
            => OpenTypeFont.HasCanonicalSubstitutions(font.Bytes, font.Metrics, token)
                || OpenTypeFont.HasContextualAdvances(font.Bytes, font.Metrics, token);

        static void AppendCmap(SvgPreparedFont font, string text, int start, int end, BudgetedParserList<SvgShapingGlyph> output, CancellationToken token)
        {
            OpenTypeFont.CmapLookup lookup = default;
            bool mappingStarted = false;
            for (int source = start; source < end;)
            {
                token.ThrowIfCancellationRequested();
                Rune scalar = Rune.GetRuneAt(text, source);
                int next = source + scalar.Utf16SequenceLength;
                if (!mappingStarted)
                {
                    lookup = new(font.Bytes, font.Metrics);
                    mappingStarted = true;
                }
                ushort glyph = lookup.GetGlyphIndex((uint)scalar.Value, token);
                output.Add(new(scalar, glyph, source, source, next, output.Count, 0, false, 0, 0, SvgGlyphAttachment.None));
                source = next;
            }
        }
    }
}
