using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDescriptiveTextStorageTests
{
    [Theory]
    [InlineData(1, 64, 64)]
    [InlineData(100, 2, 64)]
    [InlineData(100, 64, 1)]
    public void Ignored_descendants_still_obey_node_depth_and_attribute_limits(int nodes, int depth, int attributes)
    {
        RenderBudget budget = new(100_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using StringReader input = new("<text xmlns='http://www.w3.org/2000/svg'>A<metadata a='1' b='2'><g/></metadata>A</text>");
            SvgException error = Assert.Throws<SvgException>(() => Read(input, budget, new(100_000), nodes, depth, attributes, CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using StringReader retry = new("<text xmlns='http://www.w3.org/2000/svg'>A<metadata><g/></metadata>A</text>");
            Assert.Equal("AA", Read(retry, budget, new(100_000), 100, 64, 64, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Work_and_memory_denials_release_text_owners_and_allow_retry()
    {
        string document = "<text xmlns='http://www.w3.org/2000/svg'>A<metadata>" + string.Concat(Enumerable.Repeat("<g/>", 128)) + "</metadata>A</text>";
        RenderBudget budget = new(100_000);
        ParserWorkCounter measured = new(100_000);
        using (StringReader input = new(document)) Assert.Equal("AA", Read(input, budget, measured, 200, 64, 64, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(budget.Snapshot().PeakBytes - 1);
        using (StringReader input = new(document))
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Read(input, denied, new(100_000), 200, 64, 64, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using (StringReader input = new(document))
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Read(input, budget, new(measured.Used - 1), 200, 64, 64, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (StringReader retry = new(document)) Assert.Equal("AA", Read(retry, budget, new(100_000), 200, 64, 64, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancellation_while_consuming_ignored_text_releases_storage_without_disposing_the_borrower()
    {
        string document = "<text xmlns='http://www.w3.org/2000/svg'>A<metadata>" + new string('x', 16_384) + "</metadata>A</text>";
        RenderBudget budget = new(100_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        using (CancellationTokenSource cancellation = new())
        {
            using CancelOnReadReader input = new(document, cancellation);
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => Read(input, budget, new(100_000), 100, 64, 64, cancellation.Token)).CancellationToken);
            Assert.InRange(input.CancellationPosition, 8192, document.Length - 1);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using StringReader retry = new(document);
            Assert.Equal("AA", Read(retry, budget, new(100_000), 100, 64, 64, CancellationToken.None));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<metadata><style>text{fill:red}</style></metadata>")]
    [InlineData("<metadata><animate/></metadata>")]
    [InlineData("<title><script/></title>")]
    [InlineData("<rect width='2' height='2'/>")]
    [InlineData("<title xmlns='urn:other'>ignored</title>")]
    public void Descriptions_do_not_admit_unsupported_markup_or_ordinary_drawing_children(string child)
    {
        RenderBudget budget = new(100_000);
        using StringReader input = new("<text xmlns='http://www.w3.org/2000/svg'>A" + child + "A</text>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Read(input, budget, new(100_000), 100, 64, 64, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void An_unclosed_description_preserves_xml_failure_and_releases_partial_text()
    {
        RenderBudget budget = new(100_000);
        using StringReader input = new("<text xmlns='http://www.w3.org/2000/svg'>A<metadata><g></metadata></text>");
        Assert.Throws<XmlException>(() => Read(input, budget, new(100_000), 100, 64, 64, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static string Read(TextReader input, RenderBudget budget, ParserWorkCounter work, int nodes, int depth, int attributes, CancellationToken token)
    {
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        using SvgDocumentState state = new(budget, work, token);
        using BudgetedParserList<SvgExtractedTextRun> runs = new(budget, SvgMemoryCategory.Parser, work, token);
        try
        {
            SvgRenderedLeafParser.ReadDirectTextContent(reader, state, SvgTextWhitespaceMode.Default, 0, SvgTextPaintStyle.Default,
                SvgInheritedTextStyle.Default, 2, nodes, depth, attributes, budget, work, token, runs, out int characters, out _);
            Assert.Equal(2, characters);
            return string.Concat(runs.AsSpan().ToArray().Select(run => run.Layout.Value));
        }
        finally { foreach (SvgExtractedTextRun run in runs.AsSpan()) run.Dispose(); }
    }

    /// <summary>Single-reader fixture that borrows its cancellation source and cancels after the first two XML buffers; disposal owns only the text cursor.</summary>
    private sealed class CancelOnReadReader(string text, CancellationTokenSource cancellation) : StringReader(text)
    {
        internal int CharactersRead { get; private set; }
        internal int CancellationPosition { get; private set; }

        /// <summary>Reads caller-owned buffer space, then cancels the borrowed source during ignored text; no budget or cross-thread access is introduced.</summary>
        public override int Read(char[] buffer, int index, int count)
        {
            int read = base.Read(buffer, index, count);
            CharactersRead += read;
            if (CharactersRead >= 8192 && !cancellation.IsCancellationRequested)
            {
                CancellationPosition = CharactersRead;
                cancellation.Cancel();
            }
            return read;
        }
    }
}
