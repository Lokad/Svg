using System.Xml;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgTextPathStyleTests
{
    [Theory]
    [InlineData("font-size='28' text-anchor='end'", "", 28, (int)SvgTextAnchor.End)]
    [InlineData("font-size='28' text-anchor='start' style='font-size:24px;text-anchor:end'", "", 24, (int)SvgTextAnchor.End)]
    [InlineData("style='font-size:24px;text-anchor:end'", "font-size:32px!important;text-anchor:middle!important", 32, (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor='center' style='text-anchor:end'", "", 20, (int)SvgTextAnchor.End)]
    [InlineData("text-anchor='start' style='text-anchor:inherit'", "", 20, (int)SvgTextAnchor.Middle)]
    [InlineData("style='text-anchor:initial'", "text-anchor:end", 20, (int)SvgTextAnchor.Start)]
    public void Path_and_span_share_style_cascade_while_placement_keeps_the_computed_anchor(string attributes, string declarations, double size, int anchor)
    {
        SvgTextSpanStyle inherited = Inherited();
        InlineStyle sheet = SvgInlineStyleParser.Parse(declarations, CancellationToken.None, true);
        using XmlReader path = XmlReader.Create(new StringReader("<textPath href='#p' " + attributes + "/>"));
        Assert.True(path.Read());
        SvgTextPathReference reference = SvgTextPathSyntax.Read(path, inherited, sheet, CancellationToken.None, out SvgTextSpanStyle actual);
        using XmlReader span = XmlReader.Create(new StringReader("<tspan " + attributes + "/>"));
        Assert.True(span.Read());
        Assert.True(SvgTextSyntax.TryParseChildStyle(span, inherited, sheet, CancellationToken.None, out SvgTextSpanStyle expected));
        // Font states are immutable reference objects; compare their values, not their identities.
        Assert.Equal(expected with { Font = actual.Font }, actual);
        Assert.Equal((expected.Font.Family, expected.Font.Weight, expected.Font.Italic, expected.Font.FontSize,
                expected.Font.WhitespaceMode, expected.Font.LetterSpacing, expected.Font.WordSpacing,
                expected.Font.Decoration, expected.Font.Anchor, expected.Font.TextTransform, expected.Font.Language),
            (actual.Font.Family, actual.Font.Weight, actual.Font.Italic, actual.Font.FontSize,
                actual.Font.WhitespaceMode, actual.Font.LetterSpacing, actual.Font.WordSpacing,
                actual.Font.Decoration, actual.Font.Anchor, actual.Font.TextTransform, actual.Font.Language));
        Assert.Equal(size, actual.Font.FontSize);
        Assert.Equal((SvgTextAnchor)anchor, actual.Font.Anchor);
        Assert.Equal(actual.Font.Anchor, reference.Anchor);
        Assert.Equal(XmlNodeType.Element, path.NodeType);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("dx")]
    [InlineData("dy")]
    [InlineData("rotate")]
    [InlineData("width")]
    public void Inapplicable_position_attributes_do_not_become_path_positioning_lists(string attribute)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<textPath href='#p' " + attribute + "='not-a-coordinate'/>"));
        Assert.True(reader.Read());
        _ = SvgTextPathSyntax.Read(reader, Inherited(), default, CancellationToken.None, out SvgTextSpanStyle style);
        Assert.True(style.PositionLists.IsEmpty);
        Assert.Null(style.PositionOffset);
    }

    [Theory]
    [InlineData("style='opacity:.5'")]
    [InlineData("style='filter:url(#f)'")]
    [InlineData("style='width:20px'")]
    [InlineData("style='height:20px'")]
    [InlineData("transform='scale(2)'")]
    [InlineData("textLength='20'")]
    public void Child_specific_effects_and_other_path_features_keep_explicit_admission(string attributes)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<textPath href='#p' " + attributes + "/>"));
        Assert.True(reader.Read());
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgTextPathSyntax.Read(reader, Inherited(), default, CancellationToken.None, out _)).ErrorCode);
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void Plain_and_xlink_references_share_the_identifier_boundary(bool xlink, int delta)
    {
        string id = new('p', SvgSyntaxLimits.MaximumIdentifierCharacters + delta);
        string attribute = xlink ? "xmlns:xlink='http://www.w3.org/1999/xlink' xlink:href" : "href";
        using XmlReader reader = XmlReader.Create(new StringReader("<textPath " + attribute + "='#" + id + "'/>"));
        Assert.True(reader.Read());
        if (delta > 0) Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgTextPathSyntax.Parse(reader, CancellationToken.None)).ErrorCode);
        else Assert.Equal(id, SvgTextPathSyntax.Parse(reader, CancellationToken.None).PathId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Styled_paths_discover_only_the_live_family_and_emitted_characters(bool instance)
    {
        const string text = "<text font-family='NeverOpen'><textPath href='#p' font-family='Example' style='text-transform:uppercase;font-size:20px'> a </textPath></text>";
        string body = instance ? "<defs><g id='label'>" + text + "</g></defs><use href='#label'/>" : text;
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='48'><defs><path id='p' d='M0 32H80'/></defs>" + body + "</svg>");
        using SvgFontCoverageDiscovery coverage = SvgFontRequestDiscovery.DiscoverWithCoverage(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgFontCoverageRequest request = Assert.Single(coverage.Requests);
        Assert.Equal("Example", request.Request.Family);
        Assert.Equal(new uint[] { 'A' }, request.Scalars);
    }

    private static SvgTextSpanStyle Inherited()
        => new(SvgTextWhitespaceMode.Default, 0) { Font = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Middle) };
}
