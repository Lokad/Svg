namespace Lokad.Svg.Parsing;

/// <summary>Accumulates specified stylesheet declarations by per-property specificity and source order.</summary>
internal struct SvgStyleCascade
{
    private string? _color;
    private string? _fill;
    private string? _fillOpacity;
    private string? _stroke;
    private string? _display;
    private string? _visibility;
    private string? _opacity;
    private string? _fillRule;
    private string? _family;
    private string? _weight;
    private string? _fontStyle;
    private string? _fontSize;
    private int _colorSpecificity;
    private int _fillSpecificity;
    private int _fillOpacitySpecificity;
    private int _strokeSpecificity;
    private int _displaySpecificity;
    private int _visibilitySpecificity;
    private int _opacitySpecificity;
    private int _fillRuleSpecificity;
    private int _familySpecificity;
    private int _weightSpecificity;
    private int _fontStyleSpecificity;
    private int _fontSizeSpecificity;

    public SvgStyleCascade()
    {
        _colorSpecificity = _fillSpecificity = _fillOpacitySpecificity = _strokeSpecificity = -1;
        _displaySpecificity = _visibilitySpecificity = _opacitySpecificity = _fillRuleSpecificity = -1;
        _familySpecificity = _weightSpecificity = _fontStyleSpecificity = _fontSizeSpecificity = -1;
    }

    internal void Apply(InlineStyle style, int specificity)
    {
        Apply(ref _color, ref _colorSpecificity, style.Color, specificity);
        Apply(ref _fill, ref _fillSpecificity, style.Fill, specificity);
        Apply(ref _fillOpacity, ref _fillOpacitySpecificity, style.FillOpacity, specificity);
        Apply(ref _stroke, ref _strokeSpecificity, style.Stroke, specificity);
        Apply(ref _display, ref _displaySpecificity, style.Display, specificity);
        Apply(ref _visibility, ref _visibilitySpecificity, style.Visibility, specificity);
        Apply(ref _opacity, ref _opacitySpecificity, style.Opacity, specificity);
        Apply(ref _fillRule, ref _fillRuleSpecificity, style.FillRule, specificity);
        Apply(ref _family, ref _familySpecificity, style.TextFont.Family, specificity);
        Apply(ref _weight, ref _weightSpecificity, style.TextFont.Weight, specificity);
        Apply(ref _fontStyle, ref _fontStyleSpecificity, style.TextFont.Style, specificity);
        Apply(ref _fontSize, ref _fontSizeSpecificity, style.TextFont.Size, specificity);
    }

    internal InlineStyle ToInlineStyle() => new(_color, _fill, _fillOpacity, _stroke, _display, _visibility, _opacity, _fillRule) { TextFont = new SvgTextInlineFontProperties(_family, _weight, _fontStyle, _fontSize) };

    private static void Apply(ref string? value, ref int currentSpecificity, string? candidate, int specificity)
    {
        if (candidate is null || specificity < currentSpecificity) return;
        value = candidate;
        currentSpecificity = specificity;
    }
}
