using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgLeafSubtreeTests
{
    [Theory]
    [InlineData("clipPath")]
    [InlineData("mask")]
    [InlineData("pattern")]
    [InlineData("linearGradient")]
    public void Resource_leaf_descriptions_obey_document_structural_limits(string resource)
    {
        string child = resource == "linearGradient" ? "stop" : "rect";
        string attributes = string.Join(' ', Enumerable.Range(0, 9).Select(index => "a" + index + "='v'"));
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><defs><" + resource + " id='r'><" + child + "><title><x " + attributes + "/></title></" + child + "></" + resource + "></defs></svg>");
        foreach ((int nodes, int depth, int attributeLimit) in new[] { (5, 128, 256), (100, 5, 256), (100, 128, 8) })
        {
            SvgRenderLimits limits = new(1_000_000, 100_000, 1024, 1_000_000, 1_000_000, nodes, 1000, depth, attributeLimit);
            Lokad.Svg.Rendering.RenderBudget budget = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Element_free_leaf_bodies_leave_the_reader_on_their_closing_element()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<svg><path> <!-- empty --> <![CDATA[ignored]]></path><text/></svg>"));
        reader.Read();
        reader.Read();

        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, SvgRenderLimits.Default, 2, CancellationToken.None);

        Assert.False(subtree.HasUnsupportedChildren);
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
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, nodes, 16, depth, attributes);

        SvgException exception = Assert.Throws<SvgException>(() => SvgStructuralAdmission.ConsumeLeafSubtree(reader, limits, 1, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Leaf_child_elements_are_counted_and_marked_unsupported_without_descendant_admission()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<path><g><text>ignored</text></g></path>"));
        reader.Read();

        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, SvgRenderLimits.Default, 7, CancellationToken.None);

        Assert.True(subtree.HasUnsupportedChildren);
        Assert.Equal(9, subtree.NodeCount);
        Assert.Equal("path", reader.LocalName);
    }

    [Fact]
    public void Leaf_body_reads_observe_work_limits_and_cancellation()
    {
        const string input = "<path><!-- one --><!-- two --></path>";
        using XmlReader denied = XmlReader.Create(new StringReader(input));
        denied.Read();
        using XmlReader cancelled = XmlReader.Create(new StringReader(input));
        cancelled.Read();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgStructuralAdmission.ConsumeLeafSubtree(cancelled, SvgRenderLimits.Default, 1, cancellation.Token));
    }
}
