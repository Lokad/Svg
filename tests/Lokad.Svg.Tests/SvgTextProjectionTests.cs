using System.Globalization;
using System.Text;
using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgTextProjectionTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [MemberData(nameof(UnicodeUppercase))]
    public void Pinned_full_uppercase_maps_the_unicode_scalar(int source, string expected)
    {
        Span<char> output = stackalloc char[3];
        int length = SvgTextCasing.Write(new Rune(source), SvgTextTransform.Uppercase, string.Empty, false, false, output);
        Assert.Equal(expected, output[..length].ToString());
    }

    public static IEnumerable<object[]> UnicodeUppercase()
    {
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "unicode-uppercase.csv")).Skip(1))
        {
            string[] fields = line.Split(';');
            string expected = string.Concat(fields[1].Split(' ').Select(static scalar => char.ConvertFromUtf32(int.Parse(scalar, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            yield return [int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture), expected];
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("", "", (int)SvgTextTransform.Uppercase)]
    [InlineData("text-transform:none", "", (int)SvgTextTransform.None)]
    [InlineData("text-transform:initial", "", (int)SvgTextTransform.None)]
    [InlineData("text-transform:inherit", "", (int)SvgTextTransform.Uppercase)]
    [InlineData("text-transform:unset", "", (int)SvgTextTransform.Uppercase)]
    [InlineData("text-transform:capitalize", "", (int)SvgTextTransform.Capitalize)]
    [InlineData("text-transform:none", "text-transform:uppercase!important", (int)SvgTextTransform.Uppercase)]
    [InlineData("text-transform:none!important", "text-transform:uppercase!important", (int)SvgTextTransform.None)]
    public void Case_selection_keeps_cascade_and_global_keyword_semantics(string inline, string sheet, int expected)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<g xml:lang='en-US'/>"));
        Assert.True(reader.Read());
        SvgInheritedTextStyle inherited = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Uppercase };
        SvgInheritedTextStyle selected = SvgTextInheritance.Resolve(reader, inherited,
            SvgInlineStyleParser.Parse(inline, CancellationToken.None, true), SvgInlineStyleParser.Parse(sheet, CancellationToken.None, true));
        Assert.Equal((SvgTextTransform)expected, selected.TextTransform);
        Assert.Equal("en-US", selected.Language);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Expansion_lifts_original_lists_and_all_source_intervals_once()
    {
        SvgTextStyle style = new(new("Example", 400, false), 20, 12, 48, 0, 0, 0, SvgTextAnchor.Start, "projection")
            { TextTransform = SvgTextTransform.Uppercase, PositionLists = new("12 132", "48", null, null, "5 15") };
        SvgInheritedTextStyle upper = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Uppercase };
        SvgInheritedTextStyle literal = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.End);
        SvgTextFontSpan[] fonts = [new(0, 1, upper), new(1, 2, literal)];
        SvgTextPositionAdjustment[] positions = [new(0, null, null, 7, 0), new(1, null, null, 0, 2)];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextProjection projection = new(style, "ßa", [new(0, 1, 3), new(1, 2, 7)], positions,
            [new(0, 2, SvgTextPaintStyle.Default.GlyphPaint)], fonts, [new(1, true)],
            [new(0, 1, null, null), new(1, 2, new("p", 0, false, null), null)], [new(0, 2, 1, 4, true)], budget, CancellationToken.None))
        {
            Assert.Equal("SSa", projection.Value);
            Assert.True(projection.Style.PositionLists.IsEmpty);
            Assert.Equal(0, projection.Style.X);
            Assert.Equal(new[] { 0, 1, 2 }, projection.Positions.ToArray().Select(position => position.Start));
            Assert.Equal(12, projection.Positions[0].X);
            Assert.Equal(7, projection.Positions[0].Dx);
            Assert.Null(projection.Positions[1].X);
            Assert.Equal(0, projection.Positions[1].Dx);
            Assert.Equal(new double?[] { 5, 5, 15 }, projection.Positions.ToArray().Select(position => position.Rotation));
            Assert.Equal(132, projection.Positions[2].X);
            Assert.Equal(2, projection.Positions[2].Dy);
            Assert.Equal(new[] { 2, 3 }, projection.Spacing.ToArray().Select(span => span.End));
            Assert.Equal(3, projection.Paints[0].End);
            Assert.Equal(2, projection.Fonts[1].Start);
            Assert.Equal(2, projection.Nodes[0].Offset);
            Assert.Equal(2, projection.Placements[1].Start);
            Assert.Equal(3, projection.Placements[1].End);
            Assert.Equal(2, projection.Recordings[0].FirstText);
            Assert.Equal(3, projection.Recordings[0].End);
        }
        Assert.Equal(1, fonts[1].Start);
        Assert.Equal(1, positions[1].Start);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Unchanged_uppercase_keeps_the_borrowed_identity_without_material_allocation()
    {
        string source = "AA🙂";
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "identity")
            { TextTransform = SvgTextTransform.Uppercase };
        RenderBudget budget = new(new HostMemoryBudget(0));
        using SvgTextProjection projection = new(style, source, [], [], [], [], [], [], [], budget, CancellationToken.None);
        Assert.Same(source, projection.Value);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Expansion_storage_is_distinct_from_the_original_character_bound()
    {
        string source = new('ß', SvgTextLimits.MaximumCharacters);
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "expansion")
            { TextTransform = SvgTextTransform.Uppercase };
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextProjection projection = new(style, source, [], [], [], [], [], [], [], budget, CancellationToken.None))
            Assert.Equal(2 * source.Length, projection.Value.Length);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Projection_refusal_and_cancellation_release_storage_and_allow_retry()
    {
        RenderBudget generous = new(new HostMemoryBudget(1_000_000));
        Project(generous, CancellationToken.None);
        Assert.Equal(0, generous.Snapshot().CurrentBytes);
        HostMemoryBudget host = new(generous.Snapshot().PeakBytes - 1);
        RenderBudget limited = new(host);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Project(limited, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => Project(generous, cancelled.Token)).CancellationToken);
        host.Limit = generous.Snapshot().PeakBytes;
        Project(limited, CancellationToken.None);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);

        static void Project(RenderBudget budget, CancellationToken token)
        {
            SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "retry")
                { TextTransform = SvgTextTransform.Uppercase, PositionLists = new("12 132", null, null, null, "5") };
            using SvgTextProjection projection = new(style, "ßa", [], [], [], [], [], [], [], budget, token);
            Assert.Equal("SSA", projection.Value);
        }
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Lifting_rejects_an_original_split_scalar_and_releases_material_storage()
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        Assert.Throws<ArgumentException>(() => Project(budget));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static void Project(RenderBudget budget)
        {
            SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "boundary")
                { TextTransform = SvgTextTransform.Uppercase };
            using SvgTextProjection projection = new(style, "ß🙂", [], [], [], [], [new(2, true)], [], [], budget, CancellationToken.None);
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("tr")]
    [InlineData("fr")]
    public void Unqualified_language_tailoring_is_explicit_without_affecting_unconverted_text(string language)
    {
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Convert(language)).ErrorCode);
        Span<char> literal = stackalloc char[3];
        int length = SvgTextCasing.Write(new Rune('i'), SvgTextTransform.None, language, false, false, literal);
        Assert.Equal("i", literal[..length].ToString());

        static void Convert(string language)
        {
            Span<char> output = stackalloc char[3];
            _ = SvgTextCasing.Write(new Rune('i'), SvgTextTransform.Uppercase, language, false, false, output);
        }
    }
}
