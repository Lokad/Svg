using System.Xml;

namespace Lokad.Svg.Parsing;

[Flags]
internal enum SvgTextDecorationLine : byte { None = 0, Underline = 1, Overline = 2, LineThrough = 4 }

/// <summary>Borrows one immutable declaring paint across descendant glyphs; parsed style storage is covered by the aggregate XML allowance.</summary>
internal sealed record SvgTextDecorationStyle(SvgTextDecorationLine Lines, SvgTextGlyphPaint Paint)
{
    internal int UnderlineCount { get; init; } = (Lines & SvgTextDecorationLine.Underline) != 0 ? 1 : 0;
    internal int OverlineCount { get; init; } = (Lines & SvgTextDecorationLine.Overline) != 0 ? 1 : 0;
    internal int LineThroughCount { get; init; } = (Lines & SvgTextDecorationLine.LineThrough) != 0 ? 1 : 0;
}

internal static class SvgTextDecorationSyntax
{
    internal static SvgTextDecorationLine Parse(ReadOnlySpan<char> value)
    {
        SvgTextDecorationLine lines = SvgTextDecorationLine.None;
        bool found = false, none = false, solid = false;
        foreach (Range range in value.SplitAny((ReadOnlySpan<char>)SvgCssWhitespace.Characters))
        {
            ReadOnlySpan<char> token = value[range];
            if (token.IsEmpty) continue;
            if (token.Equals("solid", StringComparison.OrdinalIgnoreCase))
            {
                if (solid) throw new SvgException(SvgErrorCode.MalformedInput, "Text decoration styles cannot repeat.");
                solid = true; found = true; continue;
            }
            if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                if (none) throw new SvgException(SvgErrorCode.MalformedInput, "Text decoration lines cannot repeat.");
                none = true; found = true; continue;
            }
            SvgTextDecorationLine line = token.Equals("underline", StringComparison.OrdinalIgnoreCase) ? SvgTextDecorationLine.Underline
                : token.Equals("overline", StringComparison.OrdinalIgnoreCase) ? SvgTextDecorationLine.Overline
                : token.Equals("line-through", StringComparison.OrdinalIgnoreCase) ? SvgTextDecorationLine.LineThrough
                : throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid underline, overline and line-through text decorations are implemented.");
            if ((lines & line) != 0) throw new SvgException(SvgErrorCode.MalformedInput, "Text decoration lines cannot repeat.");
            lines |= line; found = true;
        }
        if (!found || none && lines != SvgTextDecorationLine.None) throw new SvgException(SvgErrorCode.MalformedInput, "The text decoration declaration is invalid.");
        return lines;
    }

    internal static SvgTextDecorationStyle? Resolve(XmlReader reader, InlineStyle inline, InlineStyle sheet, SvgTextDecorationStyle? inherited, SvgTextGlyphPaint paint)
    {
        string? value = SvgInlineStyleParser.MergeWithImportance(inline.TextDecoration, inline.Important, InlineImportantProperties.TextDecoration, sheet.TextDecoration, sheet.Important)
            ?? reader.GetAttribute("text-decoration");
        return Resolve(value, inherited, paint);
    }

    internal static SvgTextDecorationStyle? Resolve(string? value, SvgTextDecorationStyle? inherited, SvgTextGlyphPaint paint)
    {
        SvgTextDecorationLine lines = value is null ? SvgTextDecorationLine.None : Parse(value);
        return lines == SvgTextDecorationLine.None ? inherited : new(lines | (inherited?.Lines ?? SvgTextDecorationLine.None), paint with { Decoration = null })
        {
            UnderlineCount = (inherited?.UnderlineCount ?? 0) + ((lines & SvgTextDecorationLine.Underline) != 0 ? 1 : 0),
            OverlineCount = (inherited?.OverlineCount ?? 0) + ((lines & SvgTextDecorationLine.Overline) != 0 ? 1 : 0),
            LineThroughCount = (inherited?.LineThroughCount ?? 0) + ((lines & SvgTextDecorationLine.LineThrough) != 0 ? 1 : 0)
        };
    }
}
