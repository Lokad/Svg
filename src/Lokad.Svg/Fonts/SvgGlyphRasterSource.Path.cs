// Adapts pinned SkStrikeSpec.cpp and SkFont.cpp.
// Copyright 2019 Google LLC. See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal sealed partial class SvgGlyphRasterSource
{
    /// <summary>Tests the original transformed font axes before mask-strike relaxation.</summary>
    internal bool UsesCanonicalPath(double scaleX, double scaleY)
        => SvgGlyphStrikeTransform.RequiresPath(_fontSize,
            new(_glyphMap.A * scaleX, _glyphMap.B * scaleY, _glyphMap.C * scaleX, _glyphMap.D * scaleY, 0, 0));

    /// <summary>Returns an unhinted canonical-size outline with its unsnapped device placement.</summary>
    /// <remarks>The immutable face stays borrowed. Decoding and commands use the conversion budget;
    /// cancellation releases all scratch. The caller exclusively owns the returned path.</remarks>
    internal SvgAuthoredPath CreateCanonicalPath(double scaleX, double offsetX, double scaleY, double offsetY,
        RenderBudget budget, CancellationToken token)
    {
        const float canonicalSize = SvgGlyphOutline.CanonicalSize;
        SvgPreparedFont font = _font ?? throw new InvalidOperationException("The glyph source is disposed.");
        using OpenTypeGlyphContourStorage contours = SvgGlyphOutline.GetCanonicalContourStorage(font, _glyph.GlyphIndex, budget, token, out float pointScale);
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Geometry, token);
        int first = 0;
        foreach (int end in contours.ContourEnds)
        {
            token.ThrowIfCancellationRequested();
            commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, Point(contours.Segments[first].Start), default, default));
            for (int index = first; index < end; index++)
            {
                token.ThrowIfCancellationRequested();
                OpenTypeQuadraticSegment segment = contours.Segments[index];
                commands.Add(segment.Control == segment.End
                    ? new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, Point(segment.End), default, default)
                    : new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, Point(segment.Control), Point(segment.End), default));
            }
            commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            first = end;
        }
        double recordingScale = _pictureFontScale ?? 1;
        float size = SvgGlyphDeviceMetrics.GetEffectiveFontSize(_fontSize * recordingScale);
        float ratio = size / canonicalSize;
        SvgDevicePoint origin = SvgNativePathStroke.MapPoint(_glyphMap, new(_glyph.X, _glyph.Y));
        SvgAffineTransform map = new((float)(_glyphMap.A * scaleX / recordingScale) * ratio,
            (float)(_glyphMap.B * scaleY / recordingScale) * ratio,
            (float)(_glyphMap.C * scaleX / recordingScale) * ratio,
            (float)(_glyphMap.D * scaleY / recordingScale) * ratio,
            (float)(origin.X * scaleX + offsetX), (float)(origin.Y * scaleY + offsetY));
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        try { return SvgAuthoredPath.Create(owned, map, budget); }
        catch { owned.Dispose(); throw; }

        SvgDevicePoint Point(OpenTypeOutlinePoint point) => new((float)point.X * pointScale, -(float)point.Y * pointScale);
    }
}
