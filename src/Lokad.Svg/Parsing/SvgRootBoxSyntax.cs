using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Retains root CSS box insets independently of SVG user coordinates and intrinsic sizing.</summary>
/// <remarks>Immutable values own no storage. All four percentages use the CSS containing width;
/// resolution narrows through CSS float values and its 1/64-pixel layout grid before image projection.</remarks>
internal readonly record struct SvgRootBoxLayout(SvgRootLength Top, SvgRootLength Right, SvgRootLength Bottom, SvgRootLength Left)
{
    internal SvgRootBorder Border { get; init; }
    internal bool HasPadding => Top.Value != 0 || Right.Value != 0 || Bottom.Value != 0 || Left.Value != 0;
    internal bool HasInsets => HasPadding || Border.HasWidth;

    internal static double Resolve(SvgRootLength length, double basis)
        => Math.Floor(SvgCssLength.NormalizeFixed(length.Resolve(basis, basis)) * 64d) / 64;
}

/// <summary>Retains used CSS border widths and source colors independently of SVG strokes.</summary>
/// <remarks>Immutable values own no storage. Widths are CSS used lengths before output density;
/// colors retain their source alpha. Zero-width sides paint nothing and introduce no inset.</remarks>
internal readonly record struct SvgRootBorder(SvgRootBorderSide Top, SvgRootBorderSide Right, SvgRootBorderSide Bottom, SvgRootBorderSide Left)
{
    internal bool HasWidth => Top.Width > 0 || Right.Width > 0 || Bottom.Width > 0 || Left.Width > 0;
    internal bool HasPaint => Top.HasPaint || Right.HasPaint || Bottom.HasPaint || Left.HasPaint;
}

internal readonly record struct SvgRootBorderSide(double Width, SvgSolidPaint Paint)
{
    internal bool HasPaint => Width > 0 && Paint.Alpha > 0;
}

/// <summary>Resolves outer-root layout declarations without adding per-edge fields to every retained text style.</summary>
/// <remarks>Rule sources and winning tokens borrow the caller's budgeted stylesheet store until resolution
/// completes. The result owns no strings. Matching and scanning observe cancellation; use is conversion-thread only.</remarks>
internal static class SvgRootBoxSyntax
{
    internal static bool IsLayoutProperty(ReadOnlySpan<char> property)
        => property.Equals("padding", StringComparison.OrdinalIgnoreCase)
            || property.Equals("padding-top", StringComparison.OrdinalIgnoreCase)
            || property.Equals("padding-right", StringComparison.OrdinalIgnoreCase)
            || property.Equals("padding-bottom", StringComparison.OrdinalIgnoreCase)
            || property.Equals("padding-left", StringComparison.OrdinalIgnoreCase)
            || property.Equals("box-sizing", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-width", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-style", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-color", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-top", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-right", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-bottom", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-left", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-top-width", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-right-width", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-bottom-width", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-left-width", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-top-style", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-right-style", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-bottom-style", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-left-style", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-top-color", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-right-color", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-bottom-color", StringComparison.OrdinalIgnoreCase)
            || property.Equals("border-left-color", StringComparison.OrdinalIgnoreCase);

    internal static SvgRootBoxLayout Resolve(XmlReader reader, InlineStyle inline,
        BudgetedParserList<CssRule> rules, BudgetedParserStringStore strings, CancellationToken token)
    {
        Slots slots = default;
        string? id = reader.GetAttribute("id"), classes = reader.GetAttribute("class");
        for (int index = 0; index < rules.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            CssRule rule = rules[index];
            if (rule.Style.BoxLayoutDeclarations is {} source && rule.MatchesRoot(id, classes, token))
                Apply(source, rule.Specificity, false, ref slots, strings, token);
        }
        if (inline.BoxLayoutDeclarations is {} declarations)
        {
            try { Apply(declarations, default, true, ref slots, strings, token); }
            catch (SvgException exception) { SvgSourceContext.Attach(reader, exception, "style", declarations); throw; }
        }
        string? sizing = slots[16].Value;
        if (sizing is not null && !Global(sizing) && !sizing.Equals("content-box", StringComparison.OrdinalIgnoreCase)
            && !sizing.Equals("border-box", StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root box-sizing requires content-box or border-box.");
        SvgSolidPaint current = SvgPaint.Default.CurrentPaint;
        if (slots[4].Value is not null || slots[5].Value is not null || slots[6].Value is not null || slots[7].Value is not null
            || slots[8].Value is not null || slots[9].Value is not null || slots[10].Value is not null || slots[11].Value is not null
            || slots[12].Value is not null || slots[13].Value is not null || slots[14].Value is not null || slots[15].Value is not null)
        {
            InlineStyle sheet = SvgRootStyleSheetParser.ResolveRoot(id, classes, rules, token);
            SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, sheet);
            current = SvgPaintResolver.Resolve(null, null, style.Color, SvgPaint.Default, SvgRootParser.CssNamedColors).CurrentPaint;
        }
        return new(Parse(slots[0].Value), Parse(slots[1].Value), Parse(slots[2].Value), Parse(slots[3].Value))
        {
            Border = new(BorderSide(slots[4].Value, slots[8].Value, slots[12].Value, current),
                BorderSide(slots[5].Value, slots[9].Value, slots[13].Value, current),
                BorderSide(slots[6].Value, slots[10].Value, slots[14].Value, current),
                BorderSide(slots[7].Value, slots[11].Value, slots[15].Value, current))
        };

        static SvgRootBorderSide BorderSide(string? width, string? style, string? color, SvgSolidPaint current)
        {
            double usedWidth = width is null || Global(width) || width.Equals("medium", StringComparison.OrdinalIgnoreCase) ? 3
                : width.Equals("thin", StringComparison.OrdinalIgnoreCase) ? 1
                : width.Equals("thick", StringComparison.OrdinalIgnoreCase) ? 5 : Parse(width).Value;
            if (width is not null && width.EndsWith('%'))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Border widths require fixed CSS lengths.");
            if (style is null || Global(style) || style.Equals("none", StringComparison.OrdinalIgnoreCase) || style.Equals("hidden", StringComparison.OrdinalIgnoreCase)) usedWidth = 0;
            else if (!style.Equals("solid", StringComparison.OrdinalIgnoreCase))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid, none and hidden root border styles are implemented.");
            usedWidth = SvgCssLength.NormalizeFixed(usedWidth);
            usedWidth = usedWidth == 0 ? 0 : Math.Max(1, Math.Floor(usedWidth));
            SvgSolidPaint paint = color is null || Global(color) || color.Equals("currentColor", StringComparison.OrdinalIgnoreCase)
                ? current : SvgColorParser.ParsePaint(color, SvgRootParser.CssNamedColors);
            return usedWidth == 0 ? default : new(usedWidth, paint);
        }

        static SvgRootLength Parse(string? value)
        {
            if (value is null) return default;
            ReadOnlySpan<char> text = value.AsSpan();
            if (text.Equals("initial", StringComparison.OrdinalIgnoreCase) || text.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                || text.Equals("unset", StringComparison.OrdinalIgnoreCase)) return default;
            bool percentage = text.EndsWith("%", StringComparison.Ordinal);
            double factor = 1;
            ReadOnlySpan<char> number;
            if (percentage) number = text[..^1];
            else if (!SvgCssLength.TrySplitFixedUnit(text, out number, out factor))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root box insets require fixed CSS lengths or percentages.");
            if (!double.TryParse(number, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double result)
                || !double.IsFinite(result *= factor) || result < 0 || !percentage && result != 0 && number.Length == text.Length)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root box insets require nonnegative finite CSS lengths; nonzero bare numbers are not CSS lengths.");
            return new(result, percentage);
        }

        static void Apply(string source, CssSpecificity specificity, bool inline, ref Slots slots,
            BudgetedParserStringStore strings, CancellationToken token)
        {
            Span<char> scratch = stackalloc char[2 * SvgSyntaxLimits.MaximumValueTokenCharacters + 1];
            Span<Range> ranges = stackalloc Range[4];
            SvgInlineStyleParser.SvgInlineDeclarationReader declarations = new(source.AsSpan(), token);
            while (declarations.TryRead(out ReadOnlySpan<char> raw))
            {
                ReadOnlySpan<char> declaration = SvgCssComments.NormalizeDeclaration(raw, scratch, token);
                int separator = declaration.IndexOf(':');
                if (separator < 0) continue; // The shared declaration parser already validates syntax.
                ReadOnlySpan<char> property = declaration[..separator].Trim(SvgCssWhitespace.Characters);
                if (!IsLayoutProperty(property)) continue;
                bool important = SvgInlineStyleParser.TryStripImportant(declaration[(separator + 1)..].Trim(SvgCssWhitespace.Characters), out ReadOnlySpan<char> value);
                if (value.IsEmpty || value.Contains('!') || SvgCssWhitespace.HasNonAsciiBoundaryWhitespace(value))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root box declarations require bounded CSS values and ordinary !important placement.");
                if (property.Equals("box-sizing", StringComparison.OrdinalIgnoreCase))
                    Set(ref slots[16], value, important, inline, specificity, strings);
                else if (property.Equals("border", StringComparison.OrdinalIgnoreCase)
                    || property.Equals("border-top", StringComparison.OrdinalIgnoreCase) || property.Equals("border-right", StringComparison.OrdinalIgnoreCase)
                    || property.Equals("border-bottom", StringComparison.OrdinalIgnoreCase) || property.Equals("border-left", StringComparison.OrdinalIgnoreCase))
                {
                    int count = Split(value, ranges, token);
                    scoped ReadOnlySpan<char> width = "medium", style = "none", color = "currentColor";
                    bool hasWidth = false, hasStyle = false, hasColor = false;
                    if (Global(value)) count = 0;
                    for (int component = 0; component < count; component++)
                    {
                        ReadOnlySpan<char> part = value[ranges[component]];
                        if (Style(part))
                        {
                            if (hasStyle) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Border shorthand repeats its style.");
                            style = part; hasStyle = true;
                        }
                        else if (part.Equals("thin", StringComparison.OrdinalIgnoreCase) || part.Equals("medium", StringComparison.OrdinalIgnoreCase)
                            || part.Equals("thick", StringComparison.OrdinalIgnoreCase) || char.IsAsciiDigit(part[0]) || part[0] is '.' or '+' or '-')
                        {
                            if (hasWidth) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Border shorthand repeats its width.");
                            width = part; hasWidth = true;
                        }
                        else
                        {
                            if (hasColor || Global(part)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Border shorthand requires one width, style and color.");
                            color = part; hasColor = true;
                        }
                    }
                    if (count > 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Border shorthand requires one width, style and color.");
                    int first = property.Equals("border", StringComparison.OrdinalIgnoreCase) ? 0 : Side(property[7..]);
                    int last = property.Equals("border", StringComparison.OrdinalIgnoreCase) ? 4 : first + 1;
                    for (int edge = first; edge < last; edge++)
                    {
                        Set(ref slots[4 + edge], width, important, inline, specificity, strings);
                        Set(ref slots[8 + edge], style, important, inline, specificity, strings);
                        Set(ref slots[12 + edge], color, important, inline, specificity, strings);
                    }
                }
                else if (property.Equals("padding", StringComparison.OrdinalIgnoreCase) || property.Equals("border-width", StringComparison.OrdinalIgnoreCase)
                    || property.Equals("border-style", StringComparison.OrdinalIgnoreCase) || property.Equals("border-color", StringComparison.OrdinalIgnoreCase))
                {
                    int count = Split(value, ranges, token);
                    int first = property.Equals("padding", StringComparison.OrdinalIgnoreCase) ? 0
                        : property.Equals("border-width", StringComparison.OrdinalIgnoreCase) ? 4
                        : property.Equals("border-style", StringComparison.OrdinalIgnoreCase) ? 8 : 12;
                    for (int edge = 0; edge < 4; edge++)
                    {
                        int component = edge switch { 0 => 0, 1 => count > 1 ? 1 : 0, 2 => count > 2 ? 2 : 0, _ => count > 3 ? 3 : count > 1 ? 1 : 0 };
                        ReadOnlySpan<char> part = value[ranges[component]];
                        if (count > 1 && (part.Equals("initial", StringComparison.OrdinalIgnoreCase)
                            || part.Equals("inherit", StringComparison.OrdinalIgnoreCase) || part.Equals("unset", StringComparison.OrdinalIgnoreCase)))
                            throw new SvgException(SvgErrorCode.UnsupportedFeature, "A box shorthand global keyword cannot be combined with other components.");
                        Set(ref slots[first + edge], part, important, inline, specificity, strings);
                    }
                }
                else
                {
                    bool padding = property.StartsWith("padding-", StringComparison.OrdinalIgnoreCase);
                    int first = padding ? 0 : property.EndsWith("-width", StringComparison.OrdinalIgnoreCase) ? 4
                        : property.EndsWith("-style", StringComparison.OrdinalIgnoreCase) ? 8 : 12;
                    int edge = Side(property[(padding ? 8 : 7)..]);
                    Set(ref slots[first + edge], value, important, inline, specificity, strings);
                }
            }

            static int Side(ReadOnlySpan<char> property) => property.StartsWith("top", StringComparison.OrdinalIgnoreCase) ? 0
                : property.StartsWith("right", StringComparison.OrdinalIgnoreCase) ? 1
                : property.StartsWith("bottom", StringComparison.OrdinalIgnoreCase) ? 2 : 3;

            static int Split(ReadOnlySpan<char> value, Span<Range> ranges, CancellationToken token)
            {
                int count = 0, position = 0;
                while (position < value.Length)
                {
                    token.ThrowIfCancellationRequested();
                    if (count == ranges.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Box shorthand requires at most four components.");
                    int start = position, depth = 0;
                    while (position < value.Length)
                    {
                        char c = value[position];
                        if (c == '(') depth++;
                        else if (c == ')') depth--;
                        else if (depth == 0 && SvgCssWhitespace.Characters.Contains(c)) break;
                        position++;
                    }
                    ranges[count++] = start..position;
                    while (position < value.Length && SvgCssWhitespace.Characters.Contains(value[position])) position++;
                }
                return count;
            }

            static bool Style(ReadOnlySpan<char> value) => value.Equals("none", StringComparison.OrdinalIgnoreCase)
                || value.Equals("hidden", StringComparison.OrdinalIgnoreCase) || value.Equals("solid", StringComparison.OrdinalIgnoreCase)
                || value.Equals("dotted", StringComparison.OrdinalIgnoreCase) || value.Equals("dashed", StringComparison.OrdinalIgnoreCase)
                || value.Equals("double", StringComparison.OrdinalIgnoreCase) || value.Equals("groove", StringComparison.OrdinalIgnoreCase)
                || value.Equals("ridge", StringComparison.OrdinalIgnoreCase) || value.Equals("inset", StringComparison.OrdinalIgnoreCase)
                || value.Equals("outset", StringComparison.OrdinalIgnoreCase);

            static void Set(ref Declaration current, ReadOnlySpan<char> value, bool important, bool inline,
                CssSpecificity specificity, BudgetedParserStringStore strings)
            {
                if (current.Value is not null)
                {
                    if (current.Important != important) { if (current.Important) return; }
                    else if (current.Inline != inline) { if (current.Inline) return; }
                    else
                    {
                        int comparison = specificity.Ids.CompareTo(current.Specificity.Ids);
                        if (comparison == 0) comparison = specificity.Classes.CompareTo(current.Specificity.Classes);
                        if (comparison == 0) comparison = specificity.Elements.CompareTo(current.Specificity.Elements);
                        if (comparison < 0) return;
                    }
                }
                current = new(strings.Copy(value), important, inline, specificity);
            }
        }

    }

    private static bool Global(ReadOnlySpan<char> value) => value.Equals("initial", StringComparison.OrdinalIgnoreCase)
        || value.Equals("inherit", StringComparison.OrdinalIgnoreCase) || value.Equals("unset", StringComparison.OrdinalIgnoreCase);

    private readonly record struct Declaration(string? Value, bool Important, bool Inline, CssSpecificity Specificity);
    [InlineArray(17)]
    private struct Slots { private Declaration _first; }
}
