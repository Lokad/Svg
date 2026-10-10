using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgLeafSubtreeTests
{
    [Fact]
    public void Element_free_leaf_bodies_leave_the_reader_on_their_closing_element()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<svg><path> <!-- empty --> <![CDATA[ignored]]></path><text/></svg>"));
        reader.Read();
        reader.Read();

        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, SvgRenderLimits.Default, 2, new ParserWorkCounter(100), CancellationToken.None);

        Assert.False(subtree.HasElements);
        Assert.Equal(2, subtree.NodeCount);
        Assert.Equal(XmlNodeType.EndElement, reader.NodeType);
        Assert.Equal("path", reader.LocalName);
        Assert.True(reader.Read());
        Assert.Equal("text", reader.LocalName);
    }

    [Theory]
    [InlineData("<path><unknown/></path>", 1, 16, 16)]
    [InlineData("<path><unknown/></path>", 16, 1, 16)]
    [InlineData("<path><unknown a='1' b='2'/></path>", 16, 16, 1)]
    public void Nested_leaf_content_cannot_bypass_node_depth_or_attribute_limits(string input, int nodes, int depth, int attributes)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(input));
        reader.Read();
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, nodes, 16, 100_000, depth, attributes);

        SvgException exception = Assert.Throws<SvgException>(() => SvgStructuralAdmission.ConsumeLeafSubtree(reader, limits, 1, new ParserWorkCounter(100), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Leaf_child_elements_are_counted_and_marked_unsupported_without_descendant_admission()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<path><g><text>ignored</text></g></path>"));
        reader.Read();

        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, SvgRenderLimits.Default, 7, new ParserWorkCounter(100), CancellationToken.None);

        Assert.True(subtree.HasElements);
        Assert.Equal(9, subtree.NodeCount);
        Assert.Equal("path", reader.LocalName);
    }

    [Fact]
    public void Leaf_body_reads_observe_work_limits_and_cancellation()
    {
        const string input = "<path><!-- one --><!-- two --></path>";
        using XmlReader denied = XmlReader.Create(new StringReader(input));
        denied.Read();
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgStructuralAdmission.ConsumeLeafSubtree(denied, SvgRenderLimits.Default, 1, new ParserWorkCounter(1), CancellationToken.None)).ErrorCode);
        using XmlReader cancelled = XmlReader.Create(new StringReader(input));
        cancelled.Read();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgStructuralAdmission.ConsumeLeafSubtree(cancelled, SvgRenderLimits.Default, 1, new ParserWorkCounter(100), cancellation.Token));
    }
}
