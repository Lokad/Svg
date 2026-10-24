using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported presentation values after inline, stylesheet and attribute precedence.</summary>
internal readonly record struct SvgComputedStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule)
{
    /// <summary>Retains the parent's own opacity for an explicit inherit declaration, before any layer or mask composition.</summary>
    internal SvgPaintOpacity? ComputedOpacity { get; init; }
    /// <summary>Retains explicit display inheritance without replacing the authored source declaration.</summary>
    internal SvgDisplay? ComputedDisplay { get; init; }
    internal bool InheritsDisplay => Display == SvgInlineStyleParser.InheritedValue;
    internal SvgComputedStyle ResolveDisplay(SvgDisplay parentDisplay)
        => this with { ComputedDisplay = InheritsDisplay ? parentDisplay : null };
    internal SvgDisplay GetDisplay()
        => ComputedDisplay ?? SvgPresentationResolver.ParseResolvedDisplay(Display);
    internal SvgDisplay GetSourceDisplay()
        => SvgPresentationResolver.ParseSourceDisplay(Display);
    internal bool InheritsOpacity => Opacity == SvgInlineStyleParser.InheritedValue;
    internal SvgComputedStyle ResolveOpacity(SvgPaintOpacity parentOpacity)
        => this with { ComputedOpacity = InheritsOpacity ? parentOpacity : null };
    internal SvgPaintOpacity GetOpacity() => ComputedOpacity ?? SvgColorParser.ParsePaintOpacity(Opacity);

    /// <summary>Gets the cascaded stroke linecap value, falling back to the presentation attribute.</summary>
    internal string? Linecap { get; init; }

    /// <summary>Gets the cascaded stroke linejoin value, falling back to the presentation attribute.</summary>
    internal string? Linejoin { get; init; }

    /// <summary>Gets the cascaded stroke dasharray value, falling back to the presentation attribute.</summary>
    internal string? Dasharray { get; init; }

    /// <summary>Gets the cascaded stroke dashoffset value, falling back to the presentation attribute.</summary>
    internal string? Dashoffset { get; init; }
    internal string? Width { get; init; }
    internal string? Miterlimit { get; init; }
    internal string? StrokeOpacity { get; init; }
    internal string? PathLength { get; init; }
    internal string? ClipRule { get; init; }
    internal SvgPaintOrder? PaintOrder { get; init; }
    internal string? MarkerStart { get; init; }
    internal string? MarkerMid { get; init; }
    internal string? MarkerEnd { get; init; }
    internal string? Filter { get; init; }
    internal string? Overflow { get; init; }
    internal string? VectorEffect { get; init; }
    internal bool NonScalingStroke { get; init; }
    internal SvgComputedStyle ResolveVectorEffect(bool parent)
        => this with { NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(VectorEffect, parent) };
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle, InlineStyle styleSheet)
    {
        ValidateGeometryDimensions(reader, inlineStyle, styleSheet);
        string? filter = Select(reader, "filter", inlineStyle.Filter, (inlineStyle.Important & InlineImportantProperties.Filter) != 0, styleSheet.Filter, (styleSheet.Important & InlineImportantProperties.Filter) != 0);
        if (filter is not null && filter.Trim() != "none" && reader.LocalName is not ("g" or "use" or "rect" or "line" or "path" or "polygon" or "polyline" or "circle" or "ellipse" or "image" or "text"))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filters on this element are not implemented.");
        string? overflow = Select(reader, "overflow", inlineStyle.Overflow, (inlineStyle.Important & InlineImportantProperties.Overflow) != 0, styleSheet.Overflow, (styleSheet.Important & InlineImportantProperties.Overflow) != 0);
        // Attribute overflow keeps its established element-specific consumer
        // (including visible pattern overflow). This gate applies to CSS.
        if ((inlineStyle.Overflow is not null || styleSheet.Overflow is not null)
            && overflow is not null && overflow.AsSpan().Trim().Equals("visible", StringComparison.OrdinalIgnoreCase)
            && reader.LocalName is "svg" or "symbol" or "image" or "pattern" or "foreignObject")
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only hidden viewport overflow is implemented.");
        return new(Select(reader, "color", inlineStyle.Color, (inlineStyle.Important & InlineImportantProperties.Color) != 0, styleSheet.Color, (styleSheet.Important & InlineImportantProperties.Color) != 0), Select(reader, "fill", inlineStyle.Fill, (inlineStyle.Important & InlineImportantProperties.Fill) != 0, styleSheet.Fill, (styleSheet.Important & InlineImportantProperties.Fill) != 0), Select(reader, "fill-opacity", inlineStyle.FillOpacity, (inlineStyle.Important & InlineImportantProperties.FillOpacity) != 0, styleSheet.FillOpacity, (styleSheet.Important & InlineImportantProperties.FillOpacity) != 0), Select(reader, "stroke", inlineStyle.Stroke, (inlineStyle.Important & InlineImportantProperties.Stroke) != 0, styleSheet.Stroke, (styleSheet.Important & InlineImportantProperties.Stroke) != 0), Select(reader, "display", inlineStyle.Display, (inlineStyle.Important & InlineImportantProperties.Display) != 0, styleSheet.Display, (styleSheet.Important & InlineImportantProperties.Display) != 0), Select(reader, "visibility", inlineStyle.Visibility, (inlineStyle.Important & InlineImportantProperties.Visibility) != 0, styleSheet.Visibility, (styleSheet.Important & InlineImportantProperties.Visibility) != 0), Select(reader, "opacity", inlineStyle.Opacity, (inlineStyle.Important & InlineImportantProperties.Opacity) != 0, styleSheet.Opacity, (styleSheet.Important & InlineImportantProperties.Opacity) != 0), Select(reader, "fill-rule", inlineStyle.FillRule, (inlineStyle.Important & InlineImportantProperties.FillRule) != 0, styleSheet.FillRule, (styleSheet.Important & InlineImportantProperties.FillRule) != 0))
        { Overflow = overflow, VectorEffect = reader.GetAttribute("vector-effect"), Filter = filter, MarkerStart = Select(reader, "marker-start", inlineStyle.MarkerStart, (inlineStyle.Important & InlineImportantProperties.MarkerStart) != 0, styleSheet.MarkerStart, (styleSheet.Important & InlineImportantProperties.MarkerStart) != 0), MarkerMid = Select(reader, "marker-mid", inlineStyle.MarkerMid, (inlineStyle.Important & InlineImportantProperties.MarkerMid) != 0, styleSheet.MarkerMid, (styleSheet.Important & InlineImportantProperties.MarkerMid) != 0), MarkerEnd = Select(reader, "marker-end", inlineStyle.MarkerEnd, (inlineStyle.Important & InlineImportantProperties.MarkerEnd) != 0, styleSheet.MarkerEnd, (styleSheet.Important & InlineImportantProperties.MarkerEnd) != 0), PaintOrder = SvgPaintOrderSyntax.Parse(Select(reader, "paint-order", inlineStyle.PaintOrder, (inlineStyle.Important & InlineImportantProperties.PaintOrder) != 0, styleSheet.PaintOrder, (styleSheet.Important & InlineImportantProperties.PaintOrder) != 0)), ClipRule = Select(reader, "clip-rule", inlineStyle.ClipRule, (inlineStyle.Important & InlineImportantProperties.ClipRule) != 0, styleSheet.ClipRule, (styleSheet.Important & InlineImportantProperties.ClipRule) != 0), Linecap = Select(reader, "stroke-linecap", inlineStyle.Linecap, (inlineStyle.Important & InlineImportantProperties.Linecap) != 0, styleSheet.Linecap, (styleSheet.Important & InlineImportantProperties.Linecap) != 0), Linejoin = Select(reader, "stroke-linejoin", inlineStyle.Linejoin, (inlineStyle.Important & InlineImportantProperties.Linejoin) != 0, styleSheet.Linejoin, (styleSheet.Important & InlineImportantProperties.Linejoin) != 0), Dasharray = Select(reader, "stroke-dasharray", inlineStyle.Dasharray, (inlineStyle.Important & InlineImportantProperties.Dasharray) != 0, styleSheet.Dasharray, (styleSheet.Important & InlineImportantProperties.Dasharray) != 0), Dashoffset = Select(reader, "stroke-dashoffset", inlineStyle.Dashoffset, (inlineStyle.Important & InlineImportantProperties.Dashoffset) != 0, styleSheet.Dashoffset, (styleSheet.Important & InlineImportantProperties.Dashoffset) != 0), Width = Select(reader, "stroke-width", inlineStyle.Width, (inlineStyle.Important & InlineImportantProperties.Width) != 0, styleSheet.Width, (styleSheet.Important & InlineImportantProperties.Width) != 0), Miterlimit = Select(reader, "stroke-miterlimit", inlineStyle.Miterlimit, (inlineStyle.Important & InlineImportantProperties.Miterlimit) != 0, styleSheet.Miterlimit, (styleSheet.Important & InlineImportantProperties.Miterlimit) != 0), StrokeOpacity = Select(reader, "stroke-opacity", inlineStyle.StrokeOpacity, (inlineStyle.Important & InlineImportantProperties.StrokeOpacity) != 0, styleSheet.StrokeOpacity, (styleSheet.Important & InlineImportantProperties.StrokeOpacity) != 0), PathLength = reader.GetAttribute("pathLength") };

        // Reject unsupported CSS sizing on applicable geometry after selecting its declarations.
        static void ValidateGeometryDimensions(XmlReader reader, InlineStyle inlineStyle, InlineStyle styleSheet)
        {
            if (reader.LocalName is not ("rect" or "image")) return;
            Reject("width", SvgInlineStyleParser.MergeWithImportance(inlineStyle.GeometryWidth, inlineStyle.Important, InlineImportantProperties.GeometryWidth, styleSheet.GeometryWidth, styleSheet.Important));
            Reject("height", SvgInlineStyleParser.MergeWithImportance(inlineStyle.GeometryHeight, inlineStyle.Important, InlineImportantProperties.GeometryHeight, styleSheet.GeometryHeight, styleSheet.Important));

            void Reject(string property, string? value)
            {
                if (value is null) return;
                SvgException exception = new(SvgErrorCode.UnsupportedFeature, "CSS " + property + " on " + reader.LocalName + " elements is not implemented.");
                SvgSourceContext.Attach(reader, exception, property, value);
                throw exception;
            }
        }
    }

    /// <summary>Reads the presentation attribute only when neither an !important nor a normal inline or stylesheet value wins.</summary>
    private static string? Select(XmlReader reader, string attribute, string? inlineValue, bool inlineImportant, string? styleSheetValue, bool styleSheetImportant)
    {
        string? resolved = inlineImportant && inlineValue is not null ? inlineValue
            : styleSheetImportant && styleSheetValue is not null ? styleSheetValue
            : inlineValue ?? styleSheetValue ?? reader.GetAttribute(attribute);
        // Attribute admission and CSS parsing bound the authored token first.
        if (resolved is not null)
        {
            resolved = SvgCssWhitespace.Trim(resolved);
            if (SvgCssWhitespace.HasNonAsciiBoundaryWhitespace(resolved))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-ASCII whitespace is not a separator for this presentation value.");
        }
        // Resolve color's self-reference as inheritance after the cascade has selected its declaration.
        if (attribute == "color" && resolved is not null && resolved.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) return null;
        if (attribute == "filter" && resolved is not null && resolved.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (attribute.StartsWith("marker-", StringComparison.Ordinal)) return resolved;
        if (attribute is not ("paint-order" or "overflow") && resolved is not null && SvgInlineStyleParser.TryMapPaintKeyword(attribute.AsSpan(), resolved.AsSpan(), out string? mapped))
            return mapped == SvgInlineStyleParser.InheritedValue && SvgInlineStyleParser.IsInheritedPaintProperty(attribute.AsSpan()) ? null : mapped;
        return resolved;
    }
}
