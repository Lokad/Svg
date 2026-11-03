using System.Globalization;
using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgLowercaseContextTests
{
    [Theory]
    [MemberData(nameof(LowercaseMappings))]
    public void Default_lowercase_uses_the_pinned_full_unicode_mapping(int scalar, string expected)
    {
        Span<char> output = stackalloc char[3];
        int length = SvgTextCasing.Write(new Rune(scalar), SvgTextTransform.Lowercase, string.Empty, false, output);
        Assert.Equal(expected, output[..length].ToString());
    }

    public static IEnumerable<object[]> LowercaseMappings()
    {
        foreach (string row in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "unicode-lowercase.csv")).Skip(1))
        {
            string[] fields = row.Split(';');
            string value = string.Concat(fields[1].Split(' ').Select(static scalar => char.ConvertFromUtf32(int.Parse(scalar, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            yield return [int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture), value];
        }
    }

    [Theory]
    [MemberData(nameof(PropertyBoundaries))]
    public void Casing_properties_keep_unicode_boundaries_and_overlap(int scalar, int expected)
        => Assert.Equal((SvgTextCasingProperties)expected, SvgTextCasing.GetProperties(new Rune(scalar)));

    public static IEnumerable<object[]> PropertyBoundaries()
    {
        var ranges = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "unicode-casing-properties.csv")).Skip(1)
            .Select(static row => row.Split(';').Select(static value => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        int[] candidates = ranges.SelectMany(static row => new[] { row[0] - 1, row[0], row[1], row[1] + 1 }).Distinct().Order().ToArray();
        foreach (int scalar in candidates)
        {
            if (!Rune.IsValid(scalar)) continue;
            int expected = 0;
            foreach (int[] row in ranges)
                if (scalar >= row[0] && scalar <= row[1]) { expected = row[2]; break; }
            yield return [scalar, expected];
        }
    }

    [Theory]
    [InlineData("Σ", "σ")]
    [InlineData("ΑΣ", "ας")]
    [InlineData("ΑΣΑ", "ασα")]
    [InlineData("Α'Σ", "α'ς")]
    [InlineData("ΑΣ'A", "ασ'a")]
    [InlineData("ΑΣ'1", "ας'1")]
    [InlineData("1Σ", "1σ")]
    [InlineData("ʰΣ", "ʰσ")]
    [InlineData("ΑʰΣ", "αʰς")]
    [InlineData("ΑΣʰ", "αςʰ")]
    [InlineData("ΑΣʰΑ", "ασʰα")]
    [InlineData("ΑΣ\u0345", "ας\u0345")]
    [InlineData("ΑΣ\u0345Α", "ασ\u0345α")]
    [InlineData("ΣΣ", "σς")]
    public void Projection_uses_original_context_with_possessive_ignorable_sequences(string source, string expected)
    {
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "lowercase")
            { TextTransform = SvgTextTransform.Lowercase };
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextProjection projection = new(style, source, [], [], [], [], [], [], [], budget, CancellationToken.None))
            Assert.Equal(expected, projection.Value);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lowercase_expansion_lifts_positions_and_inherited_child_selection(bool childLiteral)
    {
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "expansion")
            { TextTransform = SvgTextTransform.Lowercase, PositionLists = new("12 132", null, null, null, null) };
        SvgInheritedTextStyle lower = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Lowercase };
        SvgInheritedTextStyle literal = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgTextFontSpan[] fonts = [new(0, 1, lower), new(1, 2, childLiteral ? literal : lower)];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgTextProjection projection = new(style, "İA", [], [], [], fonts, [new(1, true)], [], [], budget, CancellationToken.None);
        Assert.Equal(childLiteral ? "i\u0307A" : "i\u0307a", projection.Value);
        Assert.Equal(2, projection.Positions[1].Start);
        Assert.Equal(132, projection.Positions[1].X);
        Assert.Equal(2, projection.Nodes[0].Offset);
        Assert.Equal(1, fonts[1].Start);
    }

    [Theory]
    [InlineData(true, false, "A", 0x3C2, true)]
    [InlineData(false, true, "A", 0x3C3, false)]
    [InlineData(false, true, " A", 0x3C2, true)]
    [InlineData(false, false, " A", 0x3C3, false)]
    public void Path_whitespace_restarts_preserve_logical_case_context(bool preserveTrailing, bool hasPrior, string continuation, int sigma, bool space)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        SvgInheritedTextStyle lower = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Lowercase };
        coverage.BeginText(); coverage.SetFont(lower);
        coverage.Observe("AΣ ", SvgTextWhitespaceMode.Default);
        coverage.BeginWhitespaceRun(preserveTrailing, hasPrior);
        coverage.Observe(continuation, SvgTextWhitespaceMode.Default);
        uint[] needs = coverage.GetScalars(new("Example", 400, false));
        Assert.Contains((uint)sigma, needs);
        Assert.DoesNotContain((uint)(sigma == 0x3C2 ? 0x3C3 : 0x3C2), needs);
        Assert.Equal(space, needs.Contains(32u));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_sigma_keeps_its_font_and_reads_following_original_characters(bool normalized)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        coverage.BeginText();
        if (normalized)
        {
            coverage.ObserveNormalized(new("First", 400, false), "AΣ", SvgTextTransform.Lowercase, string.Empty);
            coverage.ObserveNormalized(new("Second", 400, false), "Α", SvgTextTransform.None, string.Empty);
        }
        else
        {
            SvgInheritedTextStyle lower = new("First", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
                { TextTransform = SvgTextTransform.Lowercase };
            coverage.SetFont(lower); coverage.Observe("A", SvgTextWhitespaceMode.Default); coverage.Observe("Σ", SvgTextWhitespaceMode.Default);
            coverage.SetFont(new("Second", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start));
            coverage.Observe("Α", SvgTextWhitespaceMode.Default);
        }
        Assert.Equal(new uint[] { 0x61, 0x3C3 }, coverage.GetScalars(new("First", 400, false)).Order());
        Assert.Equal(new uint[] { 0x391 }, coverage.GetScalars(new("Second", 400, false)));
    }

    [Fact]
    public void Text_end_flushes_sigma_before_a_new_logical_text_element()
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        coverage.BeginText();
        coverage.ObserveNormalized(new("First", 400, false), "AΣ", SvgTextTransform.Lowercase, string.Empty);
        coverage.BeginText();
        coverage.ObserveNormalized(new("Second", 400, false), "A", SvgTextTransform.None, string.Empty);
        Assert.Equal(new uint[] { 0x61, 0x3C2 }, coverage.GetScalars(new("First", 400, false)).Order());
    }
}
