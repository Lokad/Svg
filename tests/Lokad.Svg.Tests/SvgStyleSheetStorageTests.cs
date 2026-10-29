using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStyleSheetStorageTests
{
    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(65536)]
    [InlineData(1048576)]
    public void Collected_text_fits_its_two_live_copies_without_growing_at_eof(int length)
    {
        string css = "rect{fill:red}" + new string(' ', length - 14);
        using XmlReader reader = XmlReader.Create(new StringReader("<root><style>" + css + "</style><next/></root>"));
        Assert.True(reader.ReadToFollowing("style"));
        RenderBudget budget = new(4L * length + 256);
        using (OwnedStyleSheetText text = SvgStyleSheet.ReadElementText(reader, budget, CancellationToken.None))
            Assert.Equal(css, text.Value);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(XmlNodeType.EndElement, reader.NodeType);
        Assert.True(reader.Read());
        Assert.Equal("next", reader.LocalName);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(65536)]
    [InlineData(1048576)]
    public void Consuming_collected_text_uses_bounded_storage_and_leaves_the_next_sibling(int length)
    {
        // XmlReader itself materializes CDATA; its input-sized workspace covers that.
        // Measure streamed text here, while retaining a small CDATA boundary control.
        string xml = "<root><style>rect{fill:red}" + new string(' ', length) + "<![CDATA[ /*end*/ ]]></style><next/></root>";
        // Warm the same reader/consumer path before measuring cumulative allocations.
        using (XmlReader warm = XmlReader.Create(new StringReader(xml)))
        {
            Assert.True(warm.ReadToFollowing("style"));
            SvgRootStyleSheetParser.Consume(warm, new(16_384), CancellationToken.None);
        }
        using XmlReader reader = XmlReader.Create(new StringReader(xml));
        Assert.True(reader.ReadToFollowing("style"));
        RenderBudget budget = new(16_384);
        long before = GC.GetAllocatedBytesForCurrentThread();
        SvgRootStyleSheetParser.Consume(reader, budget, CancellationToken.None);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 65_536, $"Discarding {length} characters allocated {allocated} bytes.");
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(XmlNodeType.EndElement, reader.NodeType);
        Assert.True(reader.Read());
        Assert.Equal("next", reader.LocalName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Text_readers_release_storage_on_invalid_children_and_cancellation(bool collect)
    {
        string xml = "<style>" + new string(' ', 8192) + "<child/></style>";
        {
            using XmlReader reader = XmlReader.Create(new StringReader(xml));
            Assert.True(reader.Read());
            RenderBudget budget = new(100_000);
            Assert.Throws<XmlException>(() => Read(CancellationToken.None));
            Assert.Equal(0, budget.Snapshot().CurrentBytes);

            void Read(CancellationToken token)
            {
                if (collect) { using OwnedStyleSheetText text = SvgStyleSheet.ReadElementText(reader, budget, token); }
                else SvgRootStyleSheetParser.Consume(reader, budget, token);
            }
        }
        using XmlReader cancelled = XmlReader.Create(new StringReader(xml));
        Assert.True(cancelled.Read());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelledBudget = new(100_000);
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            if (collect) { using OwnedStyleSheetText text = SvgStyleSheet.ReadElementText(cancelled, cancelledBudget, cancellation.Token); }
            else SvgRootStyleSheetParser.Consume(cancelled, cancelledBudget, cancellation.Token);
        });
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, cancelledBudget.Snapshot().CurrentBytes);
    }
}
