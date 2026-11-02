using System.Xml;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgTextAnchorTests
{
    [Fact]
    public void Text_interval_compaction_retains_anchor_only_changes_without_inventing_positioning()
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        SvgInheritedTextStyle font = SvgInheritedTextStyle.Default;
        using (SvgTextPaintOutput text = new(budget, 0, SvgTextPaintStyle.Default, font, CancellationToken.None))
        {
            text.Append("A", 0, 0, 10, CancellationToken.None);
            text.EnterStyleSpan(SvgTextPaintStyle.Default, font.WithAnchor(SvgTextAnchor.End));
            text.Append("A", 0, 1, 10, CancellationToken.None);
            text.ExitPaintSpan();
            text.Append("A", 0, 0, 10, CancellationToken.None);
            using SvgExtractedTextRun run = text.Detach(null, false);
            Assert.Equal(new[] { SvgTextAnchor.Start, SvgTextAnchor.End, SvgTextAnchor.Start }, run.FontSpans.Items.Select(span => span.Font.Anchor));
            Assert.Empty(run.Layout.PositionAdjustments);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("", "", (int)SvgTextAnchor.End)]
    [InlineData("text-anchor:start", "", (int)SvgTextAnchor.Start)]
    [InlineData("text-anchor:middle", "", (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor:initial", "", (int)SvgTextAnchor.Start)]
    [InlineData("text-anchor:inherit", "", (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor:unset", "", (int)SvgTextAnchor.Middle)]
    [InlineData("", "text-anchor:start", (int)SvgTextAnchor.Start)]
    [InlineData("text-anchor:middle", "text-anchor:start", (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor:middle", "text-anchor:start!important", (int)SvgTextAnchor.Start)]
    [InlineData("text-anchor:middle!important", "text-anchor:start!important", (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor:middle!important;text-anchor:start", "", (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor:middle;text-anchor:start", "", (int)SvgTextAnchor.Start)]
    public void Winning_anchor_preserves_cascade_global_keywords_and_inherited_value(string inline, string sheet, int expected)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<g text-anchor='end'/>"));
        Assert.True(reader.Read());
        SvgInheritedTextStyle inherited = SvgInheritedTextStyle.Default.WithAnchor(SvgTextAnchor.Middle);
        SvgInheritedTextStyle resolved = SvgTextInheritance.Resolve(reader, inherited,
            SvgInlineStyleParser.Parse(inline, CancellationToken.None, true), SvgInlineStyleParser.Parse(sheet, CancellationToken.None, true));
        Assert.Equal((SvgTextAnchor)expected, resolved.Anchor);
    }

    [Fact]
    public void Stylesheet_anchor_uses_important_specificity_and_source_order()
    {
        SvgStyleCascade cascade = default;
        cascade.Apply(SvgInlineStyleParser.Parse("text-anchor:end", CancellationToken.None, true), new(0, 1, 0));
        cascade.Apply(SvgInlineStyleParser.Parse("text-anchor:middle", CancellationToken.None, true), new(1, 0, 0));
        cascade.Apply(SvgInlineStyleParser.Parse("text-anchor:start", CancellationToken.None, true), new(0, 1, 0));
        Assert.Equal("middle", cascade.ToInlineStyle().TextAnchor);
        cascade.Apply(SvgInlineStyleParser.Parse("text-anchor:end!important", CancellationToken.None, true), default);
        Assert.Equal("end", cascade.ToInlineStyle().TextAnchor);
        Assert.True((cascade.ToInlineStyle().Important & InlineImportantProperties.TextAnchor) != 0);
    }

    [Theory]
    [MemberData(nameof(SvgCssSelectorTests.ClassSeparators), MemberType = typeof(SvgCssSelectorTests))]
    public void Anchor_token_boundaries_use_css_whitespace(int character, bool whitespace)
    {
        string value = char.ConvertFromUtf32(character) + "middle" + char.ConvertFromUtf32(character);
        if (whitespace) Assert.Equal(SvgTextAnchor.Middle, SvgTextSyntax.ParseAnchor(value, SvgTextAnchor.End));
        else Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextSyntax.ParseAnchor(value, SvgTextAnchor.End)).ErrorCode);
    }

    [Theory]
    [InlineData("center")]
    [InlineData("middle end")]
    [InlineData("'middle'")]
    [InlineData("var(--anchor)")]
    public void Unknown_anchor_values_fail_at_the_shared_boundary(string value)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextSyntax.ParseAnchor(value, SvgTextAnchor.Start)).ErrorCode);

    [Fact]
    public void Anchor_values_keep_the_existing_lexical_limit()
        => Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgTextSyntax.ParseAnchor(new string('a', SvgSyntaxLimits.MaximumValueTokenCharacters + 1), SvgTextAnchor.Start)).ErrorCode);

    [Fact]
    public void Span_anchor_overrides_its_attribute_without_creating_an_absolute_position()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<tspan text-anchor='end' style='text-anchor:middle'/>"));
        Assert.True(reader.Read());
        Assert.True(SvgTextSyntax.TryParseTspanStyle(reader, new(SvgTextWhitespaceMode.Default, 0), default, CancellationToken.None, out SvgTextSpanStyle span));
        Assert.Equal(SvgTextAnchor.Middle, span.Font.Anchor);
        Assert.Null(span.PositionOffset);
        Assert.True(span.PositionLists.IsEmpty);
    }
}
