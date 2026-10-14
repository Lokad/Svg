using System.Globalization;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Borrows bounded authored coordinate tokens until normalized text is available.</summary>
/// <remarks>XML storage belongs to the parser allowance. Material list/glyph arrays
/// are allocated under the run's budget; validation retains no arrays or owners.</remarks>
internal readonly record struct SvgTextPositionLists(string? X, string? Y, string? Dx, string? Dy, string? Rotate)
{
    internal bool IsEmpty => X is null && Y is null && Dx is null && Dy is null && Rotate is null;

    internal static double ReadFirst(string? value, out bool multiple)
        => ReadFirst(value, true, out multiple);

    internal static double ReadFirst(string? value, bool allowPixels, out bool multiple)
    {
        multiple = false;
        if (value is null) return 0;
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Text coordinate lists exceed the configured syntax limit.");
        SvgGeometrySyntax.DelimitedTokenReader reader = new(value.AsSpan());
        if (!reader.TryReadValue(out ReadOnlySpan<char> first))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text coordinate lists must contain finite numbers.");
        double result = ReadToken(first, allowPixels);
        while (reader.TryReadValue(out ReadOnlySpan<char> token)) { _ = ReadToken(token, allowPixels); multiple = true; }
        return result;
    }

    internal static double ReadToken(ReadOnlySpan<char> token)
        => ReadToken(token, true);

    private static double ReadToken(ReadOnlySpan<char> token, bool allowPixels)
    {
        if (allowPixels && token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2];
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text coordinate lists must contain finite numbers.");
        return value;
    }

    /// <summary>Builds root positions over normalized scalar boundaries, merging existing descendant adjustments.</summary>
    internal OwnedParserArray<SvgTextPositionAdjustment> Build(string text, ReadOnlySpan<SvgTextPositionAdjustment> descendants,
        RenderBudget budget, ParserWorkCounter? work, CancellationToken token)
    {
        using BudgetedParserList<SvgTextPositionAdjustment> positions = new(budget, SvgMemoryCategory.Parser, work, token);
        SvgGeometrySyntax.DelimitedTokenReader xs = new(X.AsSpan()), ys = new(Y.AsSpan());
        SvgGeometrySyntax.DelimitedTokenReader dxs = new(Dx.AsSpan()), dys = new(Dy.AsSpan());
        int sourceOffset = 0, descendantIndex = 0;
        foreach (System.Text.Rune scalar in text.EnumerateRunes())
        {
            if (work is null) token.ThrowIfCancellationRequested(); else work.Consume(token);
            double? x = xs.TryReadValue(out ReadOnlySpan<char> xv) ? ReadToken(xv) : null;
            double? y = ys.TryReadValue(out ReadOnlySpan<char> yv) ? ReadToken(yv) : null;
            double dx = dxs.TryReadValue(out ReadOnlySpan<char> dxv) ? ReadToken(dxv) : 0;
            double dy = dys.TryReadValue(out ReadOnlySpan<char> dyv) ? ReadToken(dyv) : 0;
            if (descendantIndex < descendants.Length && descendants[descendantIndex].Start == sourceOffset)
            {
                SvgTextPositionAdjustment child = descendants[descendantIndex++];
                x = child.X ?? x; y = child.Y ?? y;
                dx += child.Dx; dy += child.Dy;
            }
            // Retain a character boundary under rotation, including zero offsets,
            // so optional ligatures cannot merge differently oriented characters.
            if (x is not null || y is not null || dx != 0 || dy != 0 || Rotate is not null)
                positions.Add(new(sourceOffset, x, y, dx, dy));
            sourceOffset += scalar.Utf16SequenceLength;
        }
        return positions.Detach();
    }

    /// <summary>Retains authored rotations, with the final value applying to remaining characters.</summary>
    internal OwnedParserArray<double> ReadRotations(RenderBudget budget, ParserWorkCounter? work, CancellationToken token)
    {
        using BudgetedParserList<double> rotations = new(budget, SvgMemoryCategory.Parser, work, token);
        SvgGeometrySyntax.DelimitedTokenReader reader = new(Rotate.AsSpan());
        while (reader.TryReadValue(out ReadOnlySpan<char> value)) rotations.Add(ReadToken(value, false));
        return rotations.Detach();
    }
}
