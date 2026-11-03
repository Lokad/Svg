using System.Text;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgLanguageCasingTests
{
    public static IEnumerable<object[]> Samples()
    {
        yield return ["tr", (int)SvgTextTransform.Lowercase, "I", "ı"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "İ", "i"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "I\u0307", "i"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "I\u0323\u0307", "i\u0323"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "I\u0301\u0307", "ı\u0301\u0307"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "I\u034F\u0307", "ı\u034F\u0307"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "I\u0307\u0307", "i\u0307"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "I\u0315\u0307", "i\u0315"];
        yield return ["AZ-Latn-AZ", (int)SvgTextTransform.Uppercase, "iı", "İI"];
        yield return ["tr-TR", (int)SvgTextTransform.Capitalize, "istanbul izmir ısparta", "İstanbul İzmir Isparta"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "I\u0307Σ\u0301", "iς\u0301"];
        yield return ["tr", (int)SvgTextTransform.Lowercase, "IΣ\u0307", "ıς\u0307"];
        yield return ["lt", (int)SvgTextTransform.Lowercase, "I\u0301", "i\u0307\u0301"];
        yield return ["lt", (int)SvgTextTransform.Lowercase, "J\u0323\u0301", "j\u0307\u0323\u0301"];
        yield return ["lt", (int)SvgTextTransform.Lowercase, "Į\u0300", "į\u0307\u0300"];
        yield return ["lt", (int)SvgTextTransform.Lowercase, "ÌÍĨ", "i\u0307\u0300i\u0307\u0301i\u0307\u0303"];
        yield return ["lt", (int)SvgTextTransform.Lowercase, "I\u0323a\u0301", "i\u0323a\u0301"];
        yield return ["lt", (int)SvgTextTransform.Uppercase, "i\u0307", "I"];
        yield return ["lt", (int)SvgTextTransform.Uppercase, "j\u0323\u0307", "J\u0323"];
        yield return ["lt", (int)SvgTextTransform.Uppercase, "į\u0307", "Į"];
        yield return ["lt", (int)SvgTextTransform.Uppercase, "i\u0301\u0307", "I\u0301\u0307"];
        yield return ["lt", (int)SvgTextTransform.Uppercase, "i\u034F\u0307", "I\u034F\u0307"];
        yield return ["lt", (int)SvgTextTransform.Capitalize, "i\u0307namai kiti\u0307", "Inamai Kiti\u0307"];
        yield return ["lt", (int)SvgTextTransform.Lowercase, "AΣ", "aς"];
        yield return ["en", (int)SvgTextTransform.Lowercase, "I\u0307", "i\u0307"];
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Projection_uses_original_conditional_language_context(string language, int transform, string source, string expected)
    {
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "language")
            { TextTransform = (SvgTextTransform)transform, Language = language };
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextProjection projection = new(style, source, [], [], [], [], [], [], [], budget, CancellationToken.None))
            Assert.Equal(expected, projection.Value);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Streaming_coverage_uses_the_same_context_across_reader_chunks(string language, int transform, string source, string expected)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        coverage.BeginText();
        for (int offset = 0; offset < source.Length; offset++)
            coverage.ObserveNormalized(new("Example", 400, false), source.AsSpan(offset, 1), (SvgTextTransform)transform, language);
        Assert.Equal(expected.EnumerateRunes().Select(static scalar => (uint)scalar.Value).Distinct().Order(),
            coverage.GetScalars(new("Example", 400, false)).Order());
    }

    [Fact]
    public void Deferred_expansion_stays_in_its_original_font_and_deleted_marks_need_no_glyph()
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        coverage.BeginText();
        coverage.ObserveNormalized(new("Base", 400, false), "I", SvgTextTransform.Lowercase, "lt");
        coverage.ObserveNormalized(new("Mark", 400, false), "\u0301", SvgTextTransform.None, string.Empty);
        Assert.Equal(new uint[] { 'i', 0x307 }, coverage.GetScalars(new("Base", 400, false)).Order());
        Assert.Equal(new uint[] { 0x301 }, coverage.GetScalars(new("Mark", 400, false)));
        coverage.BeginText();
        coverage.ObserveNormalized(new("Turkic", 400, false), "I", SvgTextTransform.Lowercase, "tr");
        coverage.ObserveNormalized(new("Deleted", 400, false), "\u0307", SvgTextTransform.Lowercase, "tr");
        Assert.Equal(new uint[] { 'i' }, coverage.GetScalars(new("Turkic", 400, false)));
        Assert.Empty(coverage.GetScalars(new("Deleted", 400, false)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deletion_drops_empty_spans_without_losing_original_positions_or_node_owners(bool trailing)
    {
        string source = trailing ? "I\u0307" : "I\u0307a";
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "deletion")
            { TextTransform = SvgTextTransform.Lowercase, Language = "tr", PositionLists = new("12 120 132", "48", "0 7 3", null, "5 15 25", new(20)) };
        SvgInheritedTextStyle font = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Lowercase, Language = "tr" };
        SvgTextFontSpan[] fonts = trailing ? [new(0, 1, font), new(1, 2, font)] : [new(0, 1, font), new(1, 2, font), new(2, 3, font)];
        SvgTextNodeBoundary[] nodes = trailing ? [new(1, true) { PreviousElementIdentity = 0, ElementIdentity = 1 }]
            : [new(1, true) { PreviousElementIdentity = 0, ElementIdentity = 1 }, new(2, true) { PreviousElementIdentity = 1, ElementIdentity = 0 }];
        SvgTextPlacementRange[] ranges = trailing ? [] : [new(0, 1, null, null), new(1, 2, new("p", 0, false, null), null), new(2, 3, null, new(100, 20))];
        SvgTextLetterSpacingSpan[] spacing = trailing ? [new(0, 1, 0), new(1, 2, 2)]
            : [new(0, 1, 0), new(1, 2, 2), new(2, 3, 0)];
        SvgTextPaintSpan[] paints = trailing ? [new(0, 1, SvgTextPaintStyle.Default.GlyphPaint), new(1, 2, SvgTextPaintStyle.Default.GlyphPaint)]
            : [new(0, 1, SvgTextPaintStyle.Default.GlyphPaint), new(1, 2, SvgTextPaintStyle.Default.GlyphPaint), new(2, 3, SvgTextPaintStyle.Default.GlyphPaint)];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextProjection projection = new(style, source, spacing, [], paints, fonts, nodes, ranges, [], budget, CancellationToken.None))
        {
            Assert.Equal(trailing ? "i" : "ia", projection.Value);
            Assert.Equal(trailing ? 1 : 2, projection.Spacing.Length);
            Assert.Equal(trailing ? 1 : 2, projection.Paints.Length);
            Assert.Equal(trailing ? 1 : 2, projection.Fonts.Length);
            Assert.Equal(trailing ? 1 : 2, projection.Positions.Length);
            Assert.Equal(5, projection.Positions[0].Rotation);
            if (trailing) Assert.Empty(projection.Nodes.ToArray());
            else
            {
                Assert.Equal(132, projection.Positions[1].X);
                Assert.Equal(10, projection.Positions[1].Dx);
                Assert.Equal(25, projection.Positions[1].Rotation);
                Assert.False(projection.Nodes[0].ChangesElement);
                Assert.Equal(2, projection.Placements.Length);
                Assert.Null(projection.Placements[1].AfterPathOrigin);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recording_owners_keep_the_first_surviving_direct_text_and_skip_children(bool directSuffix)
    {
        string source = directSuffix ? "I\u0307AB" : "I\u0307A";
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "recording")
            { TextTransform = SvgTextTransform.Lowercase, Language = "tr" };
        SvgTextRecordingScope[] scopes = [new(0, 1, 0, 0, false), new(2, 3, 2, 1, false), new(0, source.Length, 1, 4, true)];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgTextProjection projection = new(style, source, [], [], [], [], [], [], scopes, budget, CancellationToken.None);
        Assert.Equal(directSuffix ? 3 : 2, projection.Recordings.Length);
        if (directSuffix) Assert.Equal(2, projection.Recordings[^1].FirstText);
        else Assert.DoesNotContain(projection.Recordings.ToArray(), static scope => scope.IsRoot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Relative_transfer_overflow_refuses_and_releases_projection_storage(bool vertical)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        SvgException error = Assert.Throws<SvgException>(() => Project(budget, vertical));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static void Project(RenderBudget budget, bool vertical)
        {
            SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "overflow")
                { TextTransform = SvgTextTransform.Lowercase, Language = "tr" };
            double dx = vertical ? 0 : double.MaxValue, dy = vertical ? double.MaxValue : 0;
            using SvgTextProjection projection = new(style, "I\u0307a", [], [new(1, dx, dy), new(2, dx, dy)], [], [], [], [], [], budget, CancellationToken.None);
        }
    }
}
