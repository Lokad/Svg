// Managed adaptation of Skia rounded-rectangle source point construction.
// Copyright 2012 Google Inc.; 2019, 2025 Google LLC.
// See THIRD_PARTY_NOTICES.txt for the BSD license and source revision.
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Appends native float rounded-rectangle source verbs to caller-owned, bounded storage.</summary>
internal static class SvgRoundedRectPath
{
    internal static void Append(BudgetedParserList<SvgAuthoredPathCommand> commands, double x, double y,
        double width, double height, double radiusX, double radiusY)
    {
        float left = (float)x, top = (float)y;
        float right = (float)(x + width), bottom = (float)(y + height);
        float nativeWidth = right - left, nativeHeight = bottom - top;
        // SVG clamps each radius independently before native float shape construction.
        float rx = MathF.Min((float)radiusX, nativeWidth * .5f);
        float ry = MathF.Min((float)radiusY, nativeHeight * .5f);
        if (nativeWidth < rx + rx || nativeHeight < ry + ry)
        {
            float scale = MathF.Min(nativeWidth / (rx + rx), nativeHeight / (ry + ry));
            rx *= scale;
            ry *= scale;
        }
        // SVG rounded rectangles begin at the top tangent; their simplified oval
        // begins at the top midpoint. Preserve this order for stroke construction
        // and hairline paints, where segment overlap can change stored color.
        if (rx <= 0 || ry <= 0)
        {
            if (rx > 0 || ry > 0)
            {
                // A single zero SVG radius selects AddRect's top-left path start.
                // Ordinary drawRect sources retain their independent bottom-left start.
                commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(left, top), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, top), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, bottom), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(left, bottom), default, default));
            }
            else
            {
                commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(left, bottom), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(left, top), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, top), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, bottom), default, default));
            }
        }
        else if (rx >= nativeWidth * .5f && ry >= nativeHeight * .5f)
        {
            float centerX = (float)(((double)left + right) * .5);
            float centerY = (float)(((double)top + bottom) * .5);
            commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(centerX, top), default, default));
            Conic(commands, new(right, top), new(right, centerY));
            Conic(commands, new(right, bottom), new(centerX, bottom));
            Conic(commands, new(left, bottom), new(left, centerY));
            Conic(commands, new(left, top), new(centerX, top));
        }
        else
        {
            commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(left + rx, top), default, default));
            commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right - rx, top), default, default));
            Conic(commands, new(right, top), new(right, top + ry));
            commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, bottom - ry), default, default));
            Conic(commands, new(right, bottom), new(right - rx, bottom));
            commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(left + rx, bottom), default, default));
            Conic(commands, new(left, bottom), new(left, bottom - ry));
            commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(left, top + ry), default, default));
            Conic(commands, new(left, top), new(left + rx, top));
        }
        commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));

        static void Conic(BudgetedParserList<SvgAuthoredPathCommand> target, SvgDevicePoint control, SvgDevicePoint end)
            => target.Add(new(SvgAuthoredPathVerb.Conic, SvgAuthoredArcFlags.None, control, end, new(SvgRationalQuadratic.QuarterCircleWeight, 0)));
    }
}
