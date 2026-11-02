using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Retains root CSS padding independently of SVG user coordinates and intrinsic sizing.</summary>
/// <remarks>Immutable values own no storage. All four percentages use the CSS containing width;
/// resolution narrows through CSS float values and its 1/64-pixel layout grid before image projection.</remarks>
internal readonly record struct SvgRootBoxLayout(SvgRootLength Top, SvgRootLength Right, SvgRootLength Bottom, SvgRootLength Left)
{
    internal bool HasPadding => Top.Value != 0 || Right.Value != 0 || Bottom.Value != 0 || Left.Value != 0;

    internal static double Resolve(SvgRootLength length, double basis)
        => Math.Floor(SvgCssLength.NormalizeFixed(length.Resolve(basis, basis)) * 64d) / 64;
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
            || property.Equals("padding-left", StringComparison.OrdinalIgnoreCase);

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
        return new(Parse(slots[0].Value), Parse(slots[1].Value), Parse(slots[2].Value), Parse(slots[3].Value));

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
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root padding requires fixed CSS lengths or percentages.");
            if (!double.TryParse(number, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double result)
                || !double.IsFinite(result *= factor) || result < 0 || !percentage && result != 0 && number.Length == text.Length)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root padding requires nonnegative finite CSS lengths; nonzero bare numbers are not CSS lengths.");
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
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root padding requires bounded CSS values and ordinary !important placement.");
                if (property.Equals("padding", StringComparison.OrdinalIgnoreCase))
                {
                    int count = 0, position = 0;
                    while (position < value.Length)
                    {
                        token.ThrowIfCancellationRequested();
                        if (count == 4) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Padding shorthand requires one to four lengths.");
                        int start = position;
                        while (position < value.Length && !SvgCssWhitespace.Characters.Contains(value[position])) position++;
                        ranges[count++] = start..position;
                        while (position < value.Length && SvgCssWhitespace.Characters.Contains(value[position])) position++;
                    }
                    for (int edge = 0; edge < 4; edge++)
                    {
                        int component = edge switch { 0 => 0, 1 => count > 1 ? 1 : 0, 2 => count > 2 ? 2 : 0, _ => count > 3 ? 3 : count > 1 ? 1 : 0 };
                        ReadOnlySpan<char> part = value[ranges[component]];
                        if (count > 1 && (part.Equals("initial", StringComparison.OrdinalIgnoreCase)
                            || part.Equals("inherit", StringComparison.OrdinalIgnoreCase) || part.Equals("unset", StringComparison.OrdinalIgnoreCase)))
                            throw new SvgException(SvgErrorCode.UnsupportedFeature, "A padding global keyword cannot be combined with lengths.");
                        Set(ref slots[edge], part, important, inline, specificity, strings);
                    }
                }
                else
                {
                    int edge = property.Equals("padding-top", StringComparison.OrdinalIgnoreCase) ? 0
                        : property.Equals("padding-right", StringComparison.OrdinalIgnoreCase) ? 1
                        : property.Equals("padding-bottom", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
                    Set(ref slots[edge], value, important, inline, specificity, strings);
                }
            }

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

    private readonly record struct Declaration(string? Value, bool Important, bool Inline, CssSpecificity Specificity);
    [InlineArray(4)]
    private struct Slots { private Declaration _first; }
}
