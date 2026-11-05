using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgStaticCssTests
{
    [Theory]
    [InlineData("hover")]
    [InlineData("active")]
    [InlineData("focus")]
    [InlineData("focus-visible")]
    [InlineData("focus-within")]
    [InlineData("HOVER")]
    public void Interaction_components_have_class_specificity_and_never_match_static_elements(string state)
    {
        string selector = "rect.tile:" + state;
        Assert.Equal(new CssSpecificity(0, 2, 1), SvgCssSelectorSyntax.Specificity(selector, CancellationToken.None));
        Assert.False(SvgCssSelectorSyntax.Matches(selector, new("rect", null, "tile", true), CancellationToken.None));
    }

    [Theory]
    [InlineData("rect:root")]
    [InlineData("rect:hovered")]
    [InlineData("rect:hover(1)")]
    [InlineData("rect::hover")]
    [InlineData("rect:focus-within(")]
    public void Static_interaction_admission_keeps_other_or_malformed_pseudo_syntax_explicit(string selector)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgCssSelectorSyntax.Specificity(selector, CancellationToken.None)).ErrorCode);

    [Theory]
    [InlineData("rect:hover")]
    [InlineData("g:active > rect")]
    [InlineData("g:focus-within rect")]
    public void Inactive_rules_do_not_parse_paint_or_font_semantics(string selector)
    {
        byte[] input = Document("<style>rect{fill:red}" + selector
            + "{filter:url(https://example.invalid/unused);font-family:Unused;stroke-linecap:unimplemented;unsupported-paint:1}</style><g><rect width='8' height='8'/></g>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new(255, 0, 0, 255), true, null), Assert.Single(root.Geometries.CharacterizeRectangles()));
        Assert.Empty(Lokad.Svg.Fonts.SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
    }

    [Fact]
    public void A_selector_list_with_a_static_member_still_validates_its_shared_declarations()
    {
        byte[] input = Document("<style>rect:hover,rect{unsupported-paint:1}</style><rect width='8' height='8'/>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Inactive_rule_metadata_retains_no_selector_declarations_or_ancestor_branch()
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (BudgetedParserList<CssRule> rules = new(budget, SvgMemoryCategory.Parser))
        using (BudgetedParserStringStore strings = new(budget))
        {
            SvgStyleSheet.Parse("g:hover rect{unsupported-paint:1}", 1, rules, strings, budget, CancellationToken.None);
            Assert.Equal(1, rules.Count);
            Assert.True(rules[0].IsInactive);
            Assert.Empty(rules[0].Selector);
            Assert.Equal(default, rules[0].Style);
            using SvgCssAncestorContext ancestors = new(rules, budget, CancellationToken.None);
            using StringReader input = new("<g><rect/></g>");
            using XmlReader reader = XmlReader.Create(input);
            reader.MoveToContent(); ancestors.Observe(reader, 0);
            Assert.True(reader.Read()); ancestors.Observe(reader, 0);
            Assert.True(ancestors.Ancestors.IsEmpty);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inactive_rules_still_obey_rule_and_declaration_size_limits(bool oversizedValue)
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (BudgetedParserList<CssRule> rules = new(budget, SvgMemoryCategory.Parser))
        using (BudgetedParserStringStore strings = new(budget))
        {
            string css = oversizedValue ? "rect:hover{unused:" + new string('x', SvgSyntaxLimits.MaximumValueTokenCharacters + 1) + "}"
                : "rect:hover{unused:1}g:focus{unused:1}";
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgStyleSheet.Parse(css, 1, rules, strings, budget, CancellationToken.None)).ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Float_is_inert_in_svg_image_layout()
    {
        byte[] input = Document("<g style='float:right'><rect width='8' height='8' style='float:left;fill:red'/></g>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.False(root.HasUnsupportedChild);
        Assert.Equal(new SvgRectangle(0, 0, 8, 8, new(255, 0, 0, 255), true, null), Assert.Single(root.Geometries.CharacterizeRectangles()));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' id='source'\n  style='unsupported-paint:1'/>", "svg", "source", "style", 2, 3)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'>\n  <rect id='source' style='unsupported-paint:1'/></svg>", "rect", "source", "style", 2, 21)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'>\n  <style id='source'>rect{unsupported-paint:1}</style></svg>", "style", "source", "", 2, 4)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'>\n  <style id='source'>rect:hovered{fill:red}</style></svg>", "style", "source", "", 2, 4)]
    public void Style_admission_errors_retain_the_declaring_source(string input, string element, string id, string attribute, int line, int column)
    {
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(input), SvgRenderOptions.Default, CancellationToken.None));
        SvgSourceDiagnostic source = Assert.IsType<SvgSourceDiagnostic>(error.SourceDiagnostic);
        Assert.Equal(element, source.ElementName);
        Assert.Equal(id, source.ElementId);
        Assert.Equal(attribute, source.AttributeName);
        Assert.Equal(line, source.LineNumber);
        Assert.Equal(column, source.LinePosition);
    }

    private static byte[] Document(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>");
}
