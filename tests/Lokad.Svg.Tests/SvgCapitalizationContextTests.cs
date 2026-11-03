using System.Globalization;
using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgCapitalizationContextTests
{
    [Theory]
    [MemberData(nameof(CapitalizationMappings))]
    public void First_lowercase_units_use_pinned_full_titlecase(int scalar, string expected)
    {
        Span<char> output = stackalloc char[3];
        int length = SvgTextCasing.Write(new Rune(scalar), SvgTextTransform.Capitalize, string.Empty, false, true, output);
        Assert.Equal(expected, output[..length].ToString());
    }

    public static IEnumerable<object[]> CapitalizationMappings()
    {
        foreach (string row in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "unicode-capitalization.csv")).Skip(1))
        {
            string[] fields = row.Split(';');
            string value = string.Concat(fields[1].Split(' ').Select(static scalar => char.ConvertFromUtf32(int.Parse(scalar, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            yield return [int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture), value];
        }
    }

    [Theory]
    [MemberData(nameof(WordClassBoundaries))]
    public void Word_classes_keep_pinned_unicode_boundaries(int scalar, int expected)
        => Assert.Equal((SvgTextCasingWordClass)expected, SvgTextCasing.GetWordClass(new Rune(scalar)));

    public static IEnumerable<object[]> WordClassBoundaries()
    {
        var ranges = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "unicode-casing-words.csv")).Skip(1)
            .Select(static row => row.Split(';').Select(static value => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        int[] candidates = ranges.SelectMany(static row => new[] { row[0] - 1, row[0], row[1], row[1] + 1 })
            .Where(Rune.IsValid).Distinct().Order().ToArray();
        foreach (int scalar in candidates)
        {
            int[]? match = ranges.FirstOrDefault(row => scalar >= row[0] && scalar <= row[1]);
            yield return [scalar, match is null ? 0 : match[2]];
        }
    }

    [Theory]
    [InlineData("hello world", "Hello World")]
    [InlineData("hELLo WORLD", "HELLo WORLD")]
    [InlineData("ßeta ﬁnal", "Sseta Final")]
    [InlineData("ǆuro Ǆuro Ǉ Ǌ Ǳ", "ǅuro Ǆuro Ǉ Ǌ Ǳ")]
    [InlineData("123abc _abc abc123def", "123abc _abc Abc123def")]
    [InlineData("a.b a:b a'b", "A.b A:b A'b")]
    [InlineData("a''b a-b", "A''B A-B")]
    [InlineData("1'a a1'b a:1b", "1'A A1'B A:1b")]
    [InlineData("a\u200Db a'\u0301b", "A\u200Db A'\u0301b")]
    [InlineData("\u0345abc e\u0301lan", "\u0345Abc E\u0301lan")]
    [InlineData("中abc カabc اabc", "中Abc カAbc اabc")]
    public void Projection_uses_original_word_context_and_preserves_other_characters(string source, string expected)
    {
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "capitalize")
            { TextTransform = SvgTextTransform.Capitalize };
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextProjection projection = new(style, source, [], [], [], [], [], [], [], budget, CancellationToken.None))
            Assert.Equal(expected, projection.Value);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("foo", "bar", "bar")]
    [InlineData("foo-", "bar", "Bar")]
    [InlineData("foo'", "bar", "bar")]
    [InlineData("foo'", " bar", " Bar")]
    [InlineData("123", "bar", "bar")]
    [InlineData("_", "bar", "bar")]
    [InlineData("foo", "\u0301bar", "\u0301bar")]
    [InlineData("foo''", "bar", "Bar")]
    public void Coverage_keeps_original_context_across_literal_styles_and_font_changes(string prefix, string continuation, string expected)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        coverage.BeginText();
        coverage.ObserveNormalized(new("First", 400, false), prefix, SvgTextTransform.None, string.Empty);
        foreach (Rune scalar in continuation.EnumerateRunes())
            coverage.ObserveNormalized(new("Second", 400, false), scalar.ToString(), SvgTextTransform.Capitalize, string.Empty);
        Assert.Equal(expected.EnumerateRunes().Select(static scalar => (uint)scalar.Value).Distinct().Order(),
            coverage.GetScalars(new("Second", 400, false)).Order());
    }

    [Theory]
    [InlineData(true, false, "b", 'B')]
    [InlineData(false, true, "b", 'b')]
    [InlineData(false, true, " b", 'B')]
    [InlineData(false, false, " b", 'b')]
    public void Path_whitespace_restarts_preserve_original_word_context(bool preserveTrailing, bool hasPrior, string continuation, char expected)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        coverage.BeginText();
        coverage.SetFont(new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Capitalize });
        coverage.Observe("foo ", SvgTextWhitespaceMode.Default);
        coverage.BeginWhitespaceRun(preserveTrailing, hasPrior);
        coverage.Observe(continuation, SvgTextWhitespaceMode.Default);
        uint[] needs = coverage.GetScalars(new("Example", 400, false));
        Assert.Contains((uint)expected, needs);
        Assert.DoesNotContain((uint)(expected == 'B' ? 'b' : 'B'), needs);
    }

    [Fact]
    public void Expansion_lifts_original_positions_and_keeps_noninitial_style_boundaries()
    {
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "capitalize")
            { TextTransform = SvgTextTransform.Capitalize, PositionLists = new("12 132", null, null, null, "5 15") };
        SvgInheritedTextStyle selected = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Capitalize };
        SvgInheritedTextStyle bold = new("Example", 700, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Capitalize };
        SvgTextFontSpan[] fonts = [new(0, 1, selected), new(1, 2, bold)];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgTextProjection projection = new(style, "ßa", [], [], [], fonts, [new(1, true)], [], [], budget, CancellationToken.None);
        Assert.Equal("Ssa", projection.Value);
        Assert.Equal(new[] { 0, 1, 2 }, projection.Positions.ToArray().Select(static position => position.Start));
        Assert.Equal(132, projection.Positions[2].X);
        Assert.Equal(new double?[] { 5, 5, 15 }, projection.Positions.ToArray().Select(static position => position.Rotation));
        Assert.Equal(2, projection.Fonts[1].Start);
        Assert.Equal(2, projection.Nodes[0].Offset);
    }

    [Fact]
    public void Literal_prefix_context_can_keep_the_borrowed_identity_without_allocation()
    {
        string source = "foobar";
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "identity");
        SvgInheritedTextStyle literal = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgInheritedTextStyle capital = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Capitalize };
        RenderBudget budget = new(new HostMemoryBudget(0));
        using SvgTextProjection projection = new(style, source, [], [], [], [new(0, 3, literal), new(3, 6, capital)], [], [], [], budget, CancellationToken.None);
        Assert.Same(source, projection.Value);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Reader_chunks_preserve_supplementary_scalars_and_word_context(int split)
    {
        const string source = "\U00010428xy \U00010429z";
        const string expected = "\U00010400xy \U00010401z";
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        coverage.BeginText();
        coverage.ObserveNormalized(new("Example", 400, false), source.AsSpan(0, split), SvgTextTransform.Capitalize, string.Empty);
        coverage.ObserveNormalized(new("Example", 400, false), source.AsSpan(split), SvgTextTransform.Capitalize, string.Empty);
        Assert.Equal(expected.EnumerateRunes().Select(static scalar => (uint)scalar.Value).Order(),
            coverage.GetScalars(new("Example", 400, false)).Order());
    }

    [Fact]
    public void Logical_text_end_resets_word_context()
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        coverage.BeginText();
        coverage.ObserveNormalized(new("Example", 400, false), "a", SvgTextTransform.Capitalize, string.Empty);
        coverage.EndText(); coverage.BeginText();
        coverage.ObserveNormalized(new("Example", 400, false), "b", SvgTextTransform.Capitalize, string.Empty);
        Assert.Equal(new uint[] { 'A', 'B' }, coverage.GetScalars(new("Example", 400, false)).Order());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unqualified_language_tailoring_is_explicit_at_every_word_position(bool wordStart)
    {
        SvgException error = Assert.Throws<SvgException>(() => Convert(wordStart));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);

        static void Convert(bool first)
        {
            Span<char> output = stackalloc char[3];
            SvgTextCasing.Write(new Rune('i'), SvgTextTransform.Capitalize, "tr", false, first, output);
        }
    }
}
