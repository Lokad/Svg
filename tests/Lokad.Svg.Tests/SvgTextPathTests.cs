using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgTextPathTests
{
    [Theory]
    [InlineData("href='#p'", 0, false)]
    [InlineData("href='#p' startOffset='-2.5px'", -2.5, false)]
    [InlineData("href='#p' startOffset='25%'", 25, true)]
    [InlineData("href='#p' startOffset='-10%' method='align' spacing='exact'", -10, true)]
    [InlineData("xmlns:xlink='http://www.w3.org/1999/xlink' xlink:href='#p'", 0, false)]
    [InlineData("xmlns:xlink='http://www.w3.org/1999/xlink' href='#p' xlink:href='#other'", 0, false)]
    public void Parses_local_reference_precedence_and_signed_offsets(string attributes, double offset, bool percentage)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<textPath " + attributes + "/>"));
        reader.Read();
        SvgTextPathReference result = SvgTextPathSyntax.Parse(reader, new(long.MaxValue), CancellationToken.None);
        Assert.Equal("p", result.PathId);
        Assert.Equal(offset, result.StartOffset);
        Assert.Equal(percentage, result.IsPercentage);
        Assert.Equal(XmlNodeType.Element, reader.NodeType);
    }

    [Theory]
    [InlineData("", SvgErrorCode.MalformedInput)]
    [InlineData("href='#'", SvgErrorCode.MalformedInput)]
    [InlineData("href='#one two'", SvgErrorCode.MalformedInput)]
    [InlineData("href='https://example.test/p.svg#p'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("href='#p' method='stretch'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("href='#p' spacing='auto'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("href='#p' startOffset='NaN'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("href='#p' transform='scale(2)'", SvgErrorCode.UnsupportedFeature)]
    public void Reports_explicit_reference_and_subset_boundaries(string attributes, SvgErrorCode category)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<textPath " + attributes + "/>"));
        reader.Read();
        Assert.Equal(category, Assert.Throws<SvgException>(() => SvgTextPathSyntax.Parse(reader, new(long.MaxValue), CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("M8 48 H56", "", 8, 48, 48)]
    [InlineData("m8 48 h4 l12 0 H56", "", 8, 48, 48)]
    [InlineData("M0 0 L24 0", "transform='translate(8 48) scale(2)'", 8, 48, 48)]
    [InlineData("M8 48", "", 8, 48, 0)]
    [InlineData("M8 48 H8", "", 8, 48, 0)]
    [InlineData("M8 48 H16 L", "", 8, 48, 8)]
    public void Reuses_authored_commands_maps_and_renderable_prefix(string data, string attributes, double x, double y, double length)
    {
        RenderBudget budget = new(long.MaxValue);
        using (SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None))
        {
            SvgDefinitionLeafSource source = Capture(data, attributes, state);
            long retained = budget.Snapshot().CurrentBytes;
            SvgTextPathBaseline baseline = SvgTextPathLayout.ReadBaseline(source, 100, budget, new(long.MaxValue), CancellationToken.None);
            Assert.Equal(new(x, y, length), baseline);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("M8 48 L12 40", "")]
    [InlineData("M8 48 H4", "")]
    [InlineData("M8 48 Q24 16 56 48", "")]
    [InlineData("M8 48 H56 Z", "")]
    [InlineData("M8 48 H16 M24 48 H56", "")]
    [InlineData("M8 48 H56", "pathLength='1'")]
    public void Unimplemented_tangents_and_calibration_release_temporary_commands(string data, string attributes)
    {
        RenderBudget budget = new(long.MaxValue);
        using SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None);
        SvgDefinitionLeafSource source = Capture(data, attributes, state);
        long retained = budget.Snapshot().CurrentBytes;
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextPathLayout.ReadBaseline(source, 100, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(retained, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Command_work_denial_and_cancellation_release_scratch_and_allow_retry()
    {
        RenderBudget budget = new(long.MaxValue);
        using SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None);
        SvgDefinitionLeafSource source = Capture("M8 48 H16 H24 H56", "", state);
        long retained = budget.Snapshot().CurrentBytes;
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgTextPathLayout.ReadBaseline(source, 1, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgTextPathLayout.ReadBaseline(source, 100, budget, new(1), CancellationToken.None)).ErrorCode);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => SvgTextPathLayout.ReadBaseline(source, 100, budget, new(long.MaxValue), cancelled.Token)).CancellationToken);
        Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        Assert.Equal(48, SvgTextPathLayout.ReadBaseline(source, 100, budget, new(long.MaxValue), CancellationToken.None).Length);
    }

    private static SvgDefinitionLeafSource Capture(string data, string attributes, SvgDocumentState state)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<path d='" + data + "' " + attributes + "/>"));
        reader.Read();
        SvgPaintableElementContext context = new(new(default, true, true, SvgAffineTransform.Identity), SvgInheritedTextStyle.Default,
            SvgInheritedStrokeStyle.Default, default, SvgRootParser.CssNamedColors, new(long.MaxValue), CancellationToken.None, 1, null);
        return SvgDefinitionLeafSource.Capture(reader, context, state);
    }
}
