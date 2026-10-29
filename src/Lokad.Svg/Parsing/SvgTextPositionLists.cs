using System.Globalization;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Borrows bounded authored coordinate tokens until normalized text is available.</summary>
/// <remarks>XML storage belongs to the parser allowance. Material list/glyph arrays
/// are allocated under the run's budget; validation retains no arrays or owners.</remarks>
internal readonly record struct SvgTextPositionLists(string? X, string? Y, string? Dx, string? Dy, string? Rotate)
{
    internal int CharacterOffset { get; init; }
    /// <summary>Preserves authored Y boundaries while suppressing their coordinate effect on a horizontal text path.</summary>
    internal bool IgnoreY { get; init; }
    internal bool IsEmpty => X is null && Y is null && Dx is null && Dy is null && Rotate is null;

    internal static double ReadFirst(string? value, out bool multiple)
        => ReadFirst(value, true, out multiple);

    internal static double ReadFirst(string? value, bool allowPixels, out bool multiple)
    {
        multiple = false;
        if (value is null) return 0;
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Text coordinate lists exceed the configured syntax limit.");
        SvgGeometrySyntax.DelimitedTokenReader reader = new(value.AsSpan(), SvgNumberListSyntax.Svg, 0);
        if (!reader.TryReadValue(out ReadOnlySpan<char> first))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text coordinate lists must contain finite numbers.");
        double result = ReadToken(first, allowPixels);
        while (reader.TryReadValue(out ReadOnlySpan<char> token)) { _ = ReadToken(token, allowPixels); multiple = true; }
        return result;
    }

    internal static double ReadToken(ReadOnlySpan<char> token)
        => ReadToken(token, true);

    internal static double ReadToken(ReadOnlySpan<char> token, bool allowPixels)
    {
        if (allowPixels && token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2];
        if (!double.TryParse(token, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text coordinate lists must contain finite numbers.");
        return value;
    }

    /// <summary>Builds root positions over normalized scalar boundaries, merging existing descendant adjustments.</summary>
    internal OwnedParserArray<SvgTextPositionAdjustment> Build(string text, ReadOnlySpan<SvgTextPositionAdjustment> descendants,
        RenderBudget budget, CancellationToken token)
    {
        using BudgetedParserList<SvgTextPositionAdjustment> positions = new(budget, SvgMemoryCategory.Parser, token);
        SvgGeometrySyntax.DelimitedTokenReader xs = new(X.AsSpan(), SvgNumberListSyntax.Svg, 0), ys = new(Y.AsSpan(), SvgNumberListSyntax.Svg, 0);
        SvgGeometrySyntax.DelimitedTokenReader dxs = new(Dx.AsSpan(), SvgNumberListSyntax.Svg, 0), dys = new(Dy.AsSpan(), SvgNumberListSyntax.Svg, 0);
        SvgGeometrySyntax.DelimitedTokenReader rotations = new(Rotate.AsSpan(), SvgNumberListSyntax.Svg, 0);
        double? lastRotation = null;
        for (int skipped = 0; skipped < CharacterOffset; skipped++)
        {
            token.ThrowIfCancellationRequested();
            _ = xs.TryReadValue(out _); _ = ys.TryReadValue(out _);
            _ = dxs.TryReadValue(out _); _ = dys.TryReadValue(out _);
            if (rotations.TryReadValue(out ReadOnlySpan<char> previous)) lastRotation = ReadToken(previous, false);
        }
        int sourceOffset = 0, descendantIndex = 0;
        foreach (System.Text.Rune scalar in text.EnumerateRunes())
        {
            token.ThrowIfCancellationRequested();
            double? x = xs.TryReadValue(out ReadOnlySpan<char> xv) ? ReadToken(xv) : null;
            double? y = ys.TryReadValue(out ReadOnlySpan<char> yv) ? ReadToken(yv) : null;
            bool hasDx = dxs.TryReadValue(out ReadOnlySpan<char> dxv), hasDy = dys.TryReadValue(out ReadOnlySpan<char> dyv);
            double dx = hasDx ? ReadToken(dxv) : 0;
            double dy = hasDy ? ReadToken(dyv) : 0;
            if (rotations.TryReadValue(out ReadOnlySpan<char> rotation)) lastRotation = ReadToken(rotation, false);
            double? angle = lastRotation;
            if (descendantIndex < descendants.Length && descendants[descendantIndex].Start == sourceOffset)
            {
                SvgTextPositionAdjustment child = descendants[descendantIndex++];
                x = child.X ?? x; y = child.Y ?? y;
                dx = child.OverridesDx ? child.Dx : dx + child.Dx;
                dy = child.OverridesDy ? child.Dy : dy + child.Dy;
                angle = child.Rotation ?? angle;
                hasDx |= child.OverridesDx; hasDy |= child.OverridesDy;
            }
            // Authored zero entries partition SVG inline items without breaking shaping.
            if (x is not null || y is not null || hasDx || hasDy || dx != 0 || dy != 0 || angle is not null)
                positions.Add(new(sourceOffset, x, y, dx, dy) { Rotation = angle, IgnoreY = this.IgnoreY });
            sourceOffset += scalar.Utf16SequenceLength;
        }
        return positions.Detach();
    }

}
