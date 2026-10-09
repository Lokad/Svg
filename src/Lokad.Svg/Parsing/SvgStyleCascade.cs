namespace Lokad.Svg.Parsing;

/// <summary>Accumulates specified stylesheet declarations by per-property !important priority, specificity and source order.</summary>
internal struct SvgStyleCascade
{
    private const int ImportantBias = 1_000_000;

    private string? _color;
    private string? _fill;
    private string? _fillOpacity;
    private string? _stroke;
    private string? _display;
    private string? _visibility;
    private string? _opacity;
    private string? _fillRule;
    private string? _clipRule;
    private string? _paintOrder;
    private string? _family;
    private string? _weight;
    private string? _fontStyle;
    private string? _fontSize;
    private string? _whiteSpace;
    private string? _letterSpacing;
    private string? _linecap;
    private string? _linejoin;
    private string? _dasharray;
    private string? _dashoffset;
    private string? _width;
    private string? _miterlimit;
    private string? _strokeOpacity;
    private string? _pathLength;
    private bool _hasVisibleOverflow;
    private int _colorRank;
    private int _fillRank;
    private int _fillOpacityRank;
    private int _strokeRank;
    private int _displayRank;
    private int _visibilityRank;
    private int _opacityRank;
    private int _fillRuleRank;
    private int _clipRuleRank;
    private int _paintOrderRank;
    private int _familyRank;
    private int _weightRank;
    private int _fontStyleRank;
    private int _fontSizeRank;
    private int _whiteSpaceRank;
    private int _letterSpacingRank;
    private int _linecapRank;
    private int _linejoinRank;
    private int _dasharrayRank;
    private int _dashoffsetRank;
    private int _widthRank;
    private int _miterlimitRank;
    private int _strokeOpacityRank;
    private int _pathLengthRank;

    public SvgStyleCascade()
    {
        _colorRank = _fillRank = _fillOpacityRank = _strokeRank = -1;
        _displayRank = _visibilityRank = _opacityRank = _fillRuleRank = -1;
        _clipRuleRank = _paintOrderRank = -1;
        _familyRank = _weightRank = _fontStyleRank = _fontSizeRank = _whiteSpaceRank = _letterSpacingRank = -1;
        _linecapRank = _linejoinRank = _dasharrayRank = _dashoffsetRank = _widthRank = _miterlimitRank = _strokeOpacityRank = _pathLengthRank = -1;
    }

    internal void Apply(InlineStyle style, int specificity)
    {
        _hasVisibleOverflow |= style.HasVisibleOverflow;
        Apply(ref _color, ref _colorRank, style.Color, (style.Important & InlineImportantProperties.Color) != 0, specificity);
        Apply(ref _fill, ref _fillRank, style.Fill, (style.Important & InlineImportantProperties.Fill) != 0, specificity);
        Apply(ref _fillOpacity, ref _fillOpacityRank, style.FillOpacity, (style.Important & InlineImportantProperties.FillOpacity) != 0, specificity);
        Apply(ref _stroke, ref _strokeRank, style.Stroke, (style.Important & InlineImportantProperties.Stroke) != 0, specificity);
        Apply(ref _display, ref _displayRank, style.Display, (style.Important & InlineImportantProperties.Display) != 0, specificity);
        Apply(ref _visibility, ref _visibilityRank, style.Visibility, (style.Important & InlineImportantProperties.Visibility) != 0, specificity);
        Apply(ref _opacity, ref _opacityRank, style.Opacity, (style.Important & InlineImportantProperties.Opacity) != 0, specificity);
        Apply(ref _fillRule, ref _fillRuleRank, style.FillRule, (style.Important & InlineImportantProperties.FillRule) != 0, specificity);
        Apply(ref _paintOrder, ref _paintOrderRank, style.PaintOrder, (style.Important & InlineImportantProperties.PaintOrder) != 0, specificity);
        Apply(ref _clipRule, ref _clipRuleRank, style.ClipRule, (style.Important & InlineImportantProperties.ClipRule) != 0, specificity);
        Apply(ref _family, ref _familyRank, style.TextFont.Family, (style.Important & InlineImportantProperties.Family) != 0, specificity);
        Apply(ref _weight, ref _weightRank, style.TextFont.Weight, (style.Important & InlineImportantProperties.Weight) != 0, specificity);
        Apply(ref _fontStyle, ref _fontStyleRank, style.TextFont.Style, (style.Important & InlineImportantProperties.FontStyle) != 0, specificity);
        Apply(ref _fontSize, ref _fontSizeRank, style.TextFont.Size, (style.Important & InlineImportantProperties.FontSize) != 0, specificity);
        Apply(ref _whiteSpace, ref _whiteSpaceRank, style.WhiteSpace, (style.Important & InlineImportantProperties.WhiteSpace) != 0, specificity);
        Apply(ref _letterSpacing, ref _letterSpacingRank, style.LetterSpacing, (style.Important & InlineImportantProperties.LetterSpacing) != 0, specificity);
        Apply(ref _linecap, ref _linecapRank, style.Linecap, (style.Important & InlineImportantProperties.Linecap) != 0, specificity);
        Apply(ref _linejoin, ref _linejoinRank, style.Linejoin, (style.Important & InlineImportantProperties.Linejoin) != 0, specificity);
        Apply(ref _dasharray, ref _dasharrayRank, style.Dasharray, (style.Important & InlineImportantProperties.Dasharray) != 0, specificity);
        Apply(ref _dashoffset, ref _dashoffsetRank, style.Dashoffset, (style.Important & InlineImportantProperties.Dashoffset) != 0, specificity);
        Apply(ref _width, ref _widthRank, style.Width, (style.Important & InlineImportantProperties.Width) != 0, specificity);
        Apply(ref _miterlimit, ref _miterlimitRank, style.Miterlimit, (style.Important & InlineImportantProperties.Miterlimit) != 0, specificity);
        Apply(ref _strokeOpacity, ref _strokeOpacityRank, style.StrokeOpacity, (style.Important & InlineImportantProperties.StrokeOpacity) != 0, specificity);
        Apply(ref _pathLength, ref _pathLengthRank, style.PathLength, (style.Important & InlineImportantProperties.PathLength) != 0, specificity);
    }

    internal InlineStyle ToInlineStyle()
    {
        InlineImportantProperties important = InlineImportantProperties.None;
        if (_colorRank >= ImportantBias) important |= InlineImportantProperties.Color;
        if (_fillRank >= ImportantBias) important |= InlineImportantProperties.Fill;
        if (_fillOpacityRank >= ImportantBias) important |= InlineImportantProperties.FillOpacity;
        if (_strokeRank >= ImportantBias) important |= InlineImportantProperties.Stroke;
        if (_displayRank >= ImportantBias) important |= InlineImportantProperties.Display;
        if (_visibilityRank >= ImportantBias) important |= InlineImportantProperties.Visibility;
        if (_opacityRank >= ImportantBias) important |= InlineImportantProperties.Opacity;
        if (_fillRuleRank >= ImportantBias) important |= InlineImportantProperties.FillRule;
        if (_paintOrderRank >= ImportantBias) important |= InlineImportantProperties.PaintOrder;
        if (_clipRuleRank >= ImportantBias) important |= InlineImportantProperties.ClipRule;
        if (_familyRank >= ImportantBias) important |= InlineImportantProperties.Family;
        if (_weightRank >= ImportantBias) important |= InlineImportantProperties.Weight;
        if (_fontStyleRank >= ImportantBias) important |= InlineImportantProperties.FontStyle;
        if (_fontSizeRank >= ImportantBias) important |= InlineImportantProperties.FontSize;
        if (_whiteSpaceRank >= ImportantBias) important |= InlineImportantProperties.WhiteSpace;
        if (_letterSpacingRank >= ImportantBias) important |= InlineImportantProperties.LetterSpacing;
        if (_linecapRank >= ImportantBias) important |= InlineImportantProperties.Linecap;
        if (_linejoinRank >= ImportantBias) important |= InlineImportantProperties.Linejoin;
        if (_dasharrayRank >= ImportantBias) important |= InlineImportantProperties.Dasharray;
        if (_dashoffsetRank >= ImportantBias) important |= InlineImportantProperties.Dashoffset;
        if (_widthRank >= ImportantBias) important |= InlineImportantProperties.Width;
        if (_miterlimitRank >= ImportantBias) important |= InlineImportantProperties.Miterlimit;
        if (_strokeOpacityRank >= ImportantBias) important |= InlineImportantProperties.StrokeOpacity;
        if (_pathLengthRank >= ImportantBias) important |= InlineImportantProperties.PathLength;
        return new InlineStyle(_color, _fill, _fillOpacity, _stroke, _display, _visibility, _opacity, _fillRule) { TextFont = new SvgTextInlineFontProperties(_family, _weight, _fontStyle, _fontSize), WhiteSpace = _whiteSpace, LetterSpacing = _letterSpacing, Linecap = _linecap, Linejoin = _linejoin, Dasharray = _dasharray, Dashoffset = _dashoffset, Width = _width, Miterlimit = _miterlimit, StrokeOpacity = _strokeOpacity, PathLength = _pathLength, ClipRule = _clipRule, PaintOrder = _paintOrder, HasVisibleOverflow = _hasVisibleOverflow, Important = important };
    }

    private static void Apply(ref string? value, ref int currentRank, string? candidate, bool candidateImportant, int specificity)
    {
        if (candidate is null) return;
        int rank = specificity + (candidateImportant ? ImportantBias : 0);
        if (rank < currentRank) return;
        value = candidate;
        currentRank = rank;
    }
}
