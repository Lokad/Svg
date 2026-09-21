using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgStructuralVocabularyTests
{
    [Theory]
    [InlineData("g", true, true, false, false)]
    [InlineData("a", true, false, false, false)]
    [InlineData("defs", false, false, true, true)]
    [InlineData("title", false, false, true, false)]
    [InlineData("desc", false, false, true, false)]
    [InlineData("metadata", false, false, true, false)]
    [InlineData("svg", false, false, false, false)]
    [InlineData("text", false, false, false, false)]
    [InlineData("tspan", false, false, false, false)]
    [InlineData("rect", false, false, false, false)]
    [InlineData("line", false, false, false, false)]
    [InlineData("use", false, false, false, false)]
    [InlineData("path", false, false, false, false)]
    [InlineData("polygon", false, false, false, false)]
    [InlineData("polyline", false, false, false, false)]
    [InlineData("style", false, false, false, false)]
    [InlineData("symbol", false, false, false, false)]
    [InlineData("script", false, false, false, false)]
    [InlineData("foreignObject", false, false, false, false)]
    [InlineData("G", false, false, false, false)]
    [InlineData("A", false, false, false, false)]
    [InlineData("DEFS", false, false, false, false)]
    [InlineData("", false, false, false, false)]
    [InlineData("g ", false, false, false, false)]
    [InlineData("metadata ", false, false, false, false)]
    public void Classifies_structural_container_names(string localName, bool groupOrLink, bool group, bool inert, bool definitions)
    {
        Assert.Equal(groupOrLink, SvgStructuralVocabulary.IsGroupOrLinkContainer(localName));
        Assert.Equal(group, SvgStructuralVocabulary.IsGroupContainer(localName));
        Assert.Equal(inert, SvgStructuralVocabulary.IsInertContainer(localName));
        Assert.Equal(definitions, SvgStructuralVocabulary.IsDefinitionsContainer(localName));
    }
}
