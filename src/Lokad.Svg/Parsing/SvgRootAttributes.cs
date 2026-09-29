using System.Globalization;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded supported root attributes before child traversal starts.</summary>
internal readonly record struct SvgRootAttributes(SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, SvgInheritedStrokeStyle InheritedStroke, string? Id, string? Class, byte Opacity, SvgRootLength? Width, SvgRootLength? Height, SvgViewBox? ViewBox, string? FillRule)
{

    internal static SvgRootAttributes Parse(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidateRoot(reader, workCounter, cancellationToken);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline);
        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, true, true, namedColors);
        SvgTextWhitespaceMode rootWhitespace = ValidateXmlSpace(reader.GetAttribute("space", "http://www.w3.org/XML/1998/namespace"));
        ValidateEnableBackground(reader.GetAttribute("enable-background"));
        SvgRootSyntax syntax = SvgRootSyntax.Parse(reader);
        return new SvgRootAttributes(new SvgInheritedPresentation(presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, AxisTransform.Identity), SvgTextInheritance.Resolve(reader, SvgInheritedTextStyle.Default.WithWhitespaceMode(rootWhitespace), inline.TextFont, inline.WhiteSpace, inline.LetterSpacing), new SvgInheritedStrokeStyle(style.Linecap, style.Linejoin, style.Dasharray, style.Dashoffset, style.Width, style.Miterlimit, style.StrokeOpacity, style.Stroke), reader.GetAttribute("id"), reader.GetAttribute("class"), presentation.Opacity, ParseRootStyleLength(inline.GeometryWidth) ?? syntax.Width, ParseRootStyleLength(inline.GeometryHeight) ?? syntax.Height, syntax.ViewBox, style.FillRule);
        // enable-background is a deprecated Illustrator-era hint for filter
        // backdrops; the renderer performs no backdrop compositing, so the value
        // is validated as spec-shaped and then discarded.
        static void ValidateEnableBackground(string? value)
        {
            if (value is null) return;
            string[] tokens = value.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || (tokens[0] != "new" && tokens[0] != "accumulate")) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only new and accumulate root enable-background values are implemented.");
            if (tokens[0] == "accumulate" && tokens.Length != 1) throw new SvgException(SvgErrorCode.MalformedInput, "Accumulate root enable-background values take no viewport rectangle.");
            if (tokens[0] == "new" && tokens.Length != 1 && tokens.Length != 5) throw new SvgException(SvgErrorCode.MalformedInput, "New root enable-background values take an optional four-number viewport rectangle.");
            foreach (string token in tokens.Skip(1))
            {
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double coordinate) || !double.IsFinite(coordinate)) throw new SvgException(SvgErrorCode.MalformedInput, "Root enable-background rectangles must be finite numbers.");
            }
        }
        static SvgRootLength? ParseRootStyleLength(string? value)
        {
            if (value is null) return null;
            ReadOnlySpan<char> token = value.AsSpan().Trim();
            if (token.Equals("auto".AsSpan(), StringComparison.OrdinalIgnoreCase) || token.Equals("inherit".AsSpan(), StringComparison.OrdinalIgnoreCase)) return null;
            try { return SvgGeometrySyntax.ParseRootLength(value); }
            catch (SvgException) { return null; }
        }

        static SvgTextWhitespaceMode ValidateXmlSpace(string? value)
        {
            // Only the two spec-defined values are accepted so anything else fails fast;
            // the mode seeds whitespace inheritance for text descendants.
            if (value is null || value is "default") return SvgTextWhitespaceMode.Default;
            if (value is "preserve") return SvgTextWhitespaceMode.Preserve;
            throw new SvgException(SvgErrorCode.MalformedInput, "Root xml:space values must be default or preserve.");
        }

    }
}

