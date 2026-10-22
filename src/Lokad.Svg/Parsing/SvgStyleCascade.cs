namespace Lokad.Svg.Parsing;

/// <summary>Accumulates specified stylesheet declarations by per-property !important priority, specificity and source order.</summary>
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
    private string? _clipRule;
    private string? _paintOrder;
    private string? _markerEnd;
    private string? _filter;
    private CssDeclarationRank _filterRank;
    private string? _markerMid;
    private string? _markerStart;
    private string? _family;
    private string? _weight;
    private string? _fontStyle;
    private string? _fontSize;
    private string? _whiteSpace;
    private string? _letterSpacing;
    private string? _wordSpacing;
    private string? _textDecoration;
    private string? _linecap;
    private string? _linejoin;
    private string? _dasharray;
    private string? _dashoffset;
    private string? _width;
    private string? _miterlimit;
    private string? _strokeOpacity;
    private string? _geometryWidth;
    private string? _geometryHeight;
    private string? _overflow;
    private CssDeclarationRank _overflowRank;
    private CssDeclarationRank _colorRank;
    private CssDeclarationRank _fillRank;
    private CssDeclarationRank _fillOpacityRank;
    private CssDeclarationRank _strokeRank;
    private CssDeclarationRank _displayRank;
    private CssDeclarationRank _visibilityRank;
    private CssDeclarationRank _opacityRank;
    private CssDeclarationRank _fillRuleRank;
    private CssDeclarationRank _clipRuleRank;
    private CssDeclarationRank _paintOrderRank;
    private CssDeclarationRank _markerEndRank;
    private CssDeclarationRank _markerMidRank;
    private CssDeclarationRank _markerStartRank;
    private CssDeclarationRank _familyRank;
    private CssDeclarationRank _weightRank;
    private CssDeclarationRank _fontStyleRank;
    private CssDeclarationRank _fontSizeRank;
    private CssDeclarationRank _whiteSpaceRank;
    private CssDeclarationRank _letterSpacingRank;
    private CssDeclarationRank _wordSpacingRank;
    private CssDeclarationRank _textDecorationRank;
    private CssDeclarationRank _linecapRank;
    private CssDeclarationRank _linejoinRank;
    private CssDeclarationRank _dasharrayRank;
    private CssDeclarationRank _dashoffsetRank;
    private CssDeclarationRank _widthRank;
    private CssDeclarationRank _miterlimitRank;
    private CssDeclarationRank _strokeOpacityRank;
    private CssDeclarationRank _geometryWidthRank;
    private CssDeclarationRank _geometryHeightRank;

    internal void Apply(InlineStyle style, CssSpecificity specificity)
    {
        Apply(ref _overflow, ref _overflowRank, style.Overflow, (style.Important & InlineImportantProperties.Overflow) != 0, specificity);
        Apply(ref _geometryWidth, ref _geometryWidthRank, style.GeometryWidth, (style.Important & InlineImportantProperties.GeometryWidth) != 0, specificity);
        Apply(ref _geometryHeight, ref _geometryHeightRank, style.GeometryHeight, (style.Important & InlineImportantProperties.GeometryHeight) != 0, specificity);
        Apply(ref _filter, ref _filterRank, style.Filter, (style.Important & InlineImportantProperties.Filter) != 0, specificity);
        Apply(ref _markerEnd, ref _markerEndRank, style.MarkerEnd, (style.Important & InlineImportantProperties.MarkerEnd) != 0, specificity);
        Apply(ref _markerMid, ref _markerMidRank, style.MarkerMid, (style.Important & InlineImportantProperties.MarkerMid) != 0, specificity);
        Apply(ref _markerStart, ref _markerStartRank, style.MarkerStart, (style.Important & InlineImportantProperties.MarkerStart) != 0, specificity);
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
        Apply(ref _wordSpacing, ref _wordSpacingRank, style.WordSpacing, (style.Important & InlineImportantProperties.WordSpacing) != 0, specificity);
        Apply(ref _textDecoration, ref _textDecorationRank, style.TextDecoration, (style.Important & InlineImportantProperties.TextDecoration) != 0, specificity);
        Apply(ref _linecap, ref _linecapRank, style.Linecap, (style.Important & InlineImportantProperties.Linecap) != 0, specificity);
        Apply(ref _linejoin, ref _linejoinRank, style.Linejoin, (style.Important & InlineImportantProperties.Linejoin) != 0, specificity);
        Apply(ref _dasharray, ref _dasharrayRank, style.Dasharray, (style.Important & InlineImportantProperties.Dasharray) != 0, specificity);
        Apply(ref _dashoffset, ref _dashoffsetRank, style.Dashoffset, (style.Important & InlineImportantProperties.Dashoffset) != 0, specificity);
        Apply(ref _width, ref _widthRank, style.Width, (style.Important & InlineImportantProperties.Width) != 0, specificity);
        Apply(ref _miterlimit, ref _miterlimitRank, style.Miterlimit, (style.Important & InlineImportantProperties.Miterlimit) != 0, specificity);
        Apply(ref _strokeOpacity, ref _strokeOpacityRank, style.StrokeOpacity, (style.Important & InlineImportantProperties.StrokeOpacity) != 0, specificity);
    }

    internal InlineStyle ToInlineStyle()
    {
        InlineImportantProperties important = InlineImportantProperties.None;
        if (_overflowRank.Important) important |= InlineImportantProperties.Overflow;
        if (_filterRank.Important) important |= InlineImportantProperties.Filter;
        if (_markerEndRank.Important) important |= InlineImportantProperties.MarkerEnd;
        if (_markerMidRank.Important) important |= InlineImportantProperties.MarkerMid;
        if (_markerStartRank.Important) important |= InlineImportantProperties.MarkerStart;
        if (_colorRank.Important) important |= InlineImportantProperties.Color;
        if (_fillRank.Important) important |= InlineImportantProperties.Fill;
        if (_fillOpacityRank.Important) important |= InlineImportantProperties.FillOpacity;
        if (_strokeRank.Important) important |= InlineImportantProperties.Stroke;
        if (_displayRank.Important) important |= InlineImportantProperties.Display;
        if (_visibilityRank.Important) important |= InlineImportantProperties.Visibility;
        if (_opacityRank.Important) important |= InlineImportantProperties.Opacity;
        if (_fillRuleRank.Important) important |= InlineImportantProperties.FillRule;
        if (_paintOrderRank.Important) important |= InlineImportantProperties.PaintOrder;
        if (_clipRuleRank.Important) important |= InlineImportantProperties.ClipRule;
        if (_familyRank.Important) important |= InlineImportantProperties.Family;
        if (_weightRank.Important) important |= InlineImportantProperties.Weight;
        if (_fontStyleRank.Important) important |= InlineImportantProperties.FontStyle;
        if (_fontSizeRank.Important) important |= InlineImportantProperties.FontSize;
        if (_whiteSpaceRank.Important) important |= InlineImportantProperties.WhiteSpace;
        if (_letterSpacingRank.Important) important |= InlineImportantProperties.LetterSpacing;
        if (_wordSpacingRank.Important) important |= InlineImportantProperties.WordSpacing;
        if (_textDecorationRank.Important) important |= InlineImportantProperties.TextDecoration;
        if (_linecapRank.Important) important |= InlineImportantProperties.Linecap;
        if (_linejoinRank.Important) important |= InlineImportantProperties.Linejoin;
        if (_dasharrayRank.Important) important |= InlineImportantProperties.Dasharray;
        if (_dashoffsetRank.Important) important |= InlineImportantProperties.Dashoffset;
        if (_widthRank.Important) important |= InlineImportantProperties.Width;
        if (_miterlimitRank.Important) important |= InlineImportantProperties.Miterlimit;
        if (_strokeOpacityRank.Important) important |= InlineImportantProperties.StrokeOpacity;
        if (_geometryWidthRank.Important) important |= InlineImportantProperties.GeometryWidth;
        if (_geometryHeightRank.Important) important |= InlineImportantProperties.GeometryHeight;
        return new InlineStyle(_color, _fill, _fillOpacity, _stroke, _display, _visibility, _opacity, _fillRule) { TextFont = new SvgTextInlineFontProperties(_family, _weight, _fontStyle, _fontSize), WhiteSpace = _whiteSpace, LetterSpacing = _letterSpacing, WordSpacing = _wordSpacing, TextDecoration = _textDecoration, Linecap = _linecap, Linejoin = _linejoin, Dasharray = _dasharray, Dashoffset = _dashoffset, Width = _width, Miterlimit = _miterlimit, StrokeOpacity = _strokeOpacity, ClipRule = _clipRule, PaintOrder = _paintOrder, Filter = _filter, MarkerEnd = _markerEnd, MarkerMid = _markerMid, MarkerStart = _markerStart, Overflow = _overflow, GeometryWidth = _geometryWidth, GeometryHeight = _geometryHeight, Important = important };
    }

    private static void Apply(ref string? value, ref CssDeclarationRank currentRank, string? candidate, bool candidateImportant, CssSpecificity specificity)
    {
        if (candidate is null) return;
        if (currentRank.Important != candidateImportant)
        {
            if (currentRank.Important) return;
        }
        else
        {
            int comparison = specificity.Ids.CompareTo(currentRank.Specificity.Ids);
            if (comparison == 0) comparison = specificity.Classes.CompareTo(currentRank.Specificity.Classes);
            if (comparison == 0) comparison = specificity.Elements.CompareTo(currentRank.Specificity.Elements);
            if (comparison < 0) return;
        }
        value = candidate;
        currentRank = new(candidateImportant, specificity);
    }

    private readonly record struct CssDeclarationRank(bool Important, CssSpecificity Specificity);
}
