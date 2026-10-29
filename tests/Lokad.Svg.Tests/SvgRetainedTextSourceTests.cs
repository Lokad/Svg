using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRetainedTextSourceTests
{
    [Fact]
    public void Subtree_readers_preserve_namespaces_chunks_and_parent_position()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (StringReader input = new("<text xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' xml:space='preserve'> A<![CDATA[ <B> ]]><tspan x='3'>C</tspan><textPath xlink:href='#p'>D</textPath> E</text>"))
        using (XmlReader live = XmlReader.Create(input))
        {
            live.MoveToContent();
            SvgPaintableElementContext context = Context(CancellationToken.None);
            SvgRetainedTextSource source = SvgRetainedTextSource.Capture(live, context, state, SvgRenderLimits.Default);
            Assert.Equal(XmlNodeType.EndElement, live.NodeType);
            using SvgRetainedTextSource.Reader reader = source.OpenReader(4, CancellationToken.None);
            Assert.True(reader.Read()); Assert.Equal(4, reader.Depth);
            Assert.Equal("preserve", reader.GetAttribute("space", "http://www.w3.org/XML/1998/namespace"));
            using (XmlReader subtree = reader.ReadSubtree())
            {
                Assert.True(subtree.Read()); Assert.Equal(0, subtree.Depth);
                Assert.True(subtree.Read()); Assert.Equal(" A", ReadChunks(subtree));
                Assert.True(subtree.Read()); Assert.Equal(XmlNodeType.CDATA, subtree.NodeType); Assert.Equal(" <B> ", ReadChunks(subtree));
                Assert.True(subtree.Read()); Assert.Equal("tspan", subtree.LocalName);
                using (XmlReader span = subtree.ReadSubtree())
                {
                    Assert.True(span.Read()); Assert.Equal(0, span.Depth); Assert.Equal("3", span.GetAttribute("x"));
                    Assert.True(span.Read()); Assert.Equal("C", ReadChunks(span));
                }
                Assert.Equal(XmlNodeType.EndElement, subtree.NodeType); Assert.Equal("tspan", subtree.LocalName);
                Assert.True(subtree.Read()); Assert.Equal("textPath", subtree.LocalName);
                Assert.Equal("#p", subtree.GetAttribute("href", "http://www.w3.org/1999/xlink"));
                Assert.True(subtree.MoveToAttribute("href", "http://www.w3.org/1999/xlink"));
                Assert.Equal("xlink:href", subtree.Name); Assert.True(subtree.MoveToElement());
                // Disposing before the end still leaves the parent at its closing token.
            }
            Assert.Equal(XmlNodeType.EndElement, reader.NodeType); Assert.Equal("text", reader.LocalName);
            Assert.Equal(4, reader.Depth); Assert.False(reader.Read()); Assert.True(reader.EOF);
            using SvgRetainedTextSource.Reader independent = source.OpenReader(0, CancellationToken.None);
            Assert.True(independent.Read()); Assert.Equal("text", independent.LocalName);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static string ReadChunks(XmlReader reader)
        {
            char[] chunk = new char[2];
            System.Text.StringBuilder value = new();
            int count;
            while ((count = reader.ReadValueChunk(chunk, 0, chunk.Length)) != 0) value.Append(chunk, 0, count);
            return value.ToString();
        }
    }

    [Theory]
    [InlineData(1L, false)]
    [InlineData(1_000_000L, true)]
    public void Capture_releases_storage_on_denial_or_cancellation(long maximumBytes, bool cancel)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(maximumBytes));
        using CancellationTokenSource cancellation = new();
        if (cancel) cancellation.Cancel();
        try
        {
            using SvgDocumentState state = new(budget, cancellation.Token);
            using StringReader input = new("<text xmlns='http://www.w3.org/2000/svg'>A<tspan>B</tspan></text>");
            using XmlReader live = XmlReader.Create(input); live.MoveToContent();
            SvgRetainedTextSource.Capture(live, Context(cancellation.Token), state, SvgRenderLimits.Default);
            Assert.Fail("Expected bounded capture to fail.");
        }
        catch (SvgException exception) when (!cancel) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        catch (OperationCanceledException exception) when (cancel) { Assert.Equal(cancellation.Token, exception.CancellationToken); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Material_capture_failure_releases_arrays_and_strings_after_partial_growth()
    {
        string svg = "<text xmlns='http://www.w3.org/2000/svg'><tspan>" + new string('A', 8192) + "</tspan><tspan dx='3'>" + new string('B', 8192) + "</tspan></text>";
        RenderBudget accepted = new RenderBudget(new HostMemoryBudget(1_000_000));         Capture(accepted);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(accepted.Snapshot().PeakBytes * 3 / 4));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Capture(denied)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Capture(accepted); Assert.Equal(0, accepted.Snapshot().CurrentBytes);

        void Capture(RenderBudget budget)
        {
            using SvgDocumentState state = new(budget, CancellationToken.None);
            using StringReader input = new(svg);
            using XmlReader live = XmlReader.Create(input); live.MoveToContent();
            SvgRetainedTextSource.Capture(live, Context(CancellationToken.None), state, SvgRenderLimits.Default);
        }
    }

    [Fact]
    public void Canceled_replay_does_not_mutate_source_or_prevent_an_independent_retry()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));         using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (StringReader input = new("<text xmlns='http://www.w3.org/2000/svg'>ABC</text>"))
        using (XmlReader live = XmlReader.Create(input))
        {
            live.MoveToContent();
            SvgRetainedTextSource source = SvgRetainedTextSource.Capture(live, Context(CancellationToken.None), state, SvgRenderLimits.Default);
            using CancellationTokenSource cancellation = new();
            using (SvgRetainedTextSource.Reader reader = source.OpenReader(0, cancellation.Token))
            {
                Assert.True(reader.Read()); Assert.True(reader.Read());
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => reader.ReadValueChunk(new char[1], 0, 1)).CancellationToken);
            }
            using SvgRetainedTextSource.Reader retry = source.OpenReader(0, CancellationToken.None);
            Assert.True(retry.Read()); Assert.True(retry.Read()); Assert.Equal("ABC", retry.Value);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgPaintableElementContext Context(CancellationToken token)
        => new(new(SvgPaint.Default, true, true, SvgAffineTransform.Identity), SvgInheritedTextStyle.Default,
            SvgInheritedStrokeStyle.Default, default, SvgRootParser.CssNamedColors, token, 1, null);
}
