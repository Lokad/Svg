using System.Xml;
using Lokad.Svg.Fonts;
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
    [InlineData("", null)]
    [InlineData("text-anchor='inherit'", null)]
    [InlineData("text-anchor='start'", (int)SvgTextAnchor.Start)]
    [InlineData("text-anchor='middle'", (int)SvgTextAnchor.Middle)]
    [InlineData("text-anchor='end'", (int)SvgTextAnchor.End)]
    public void Child_path_anchor_retains_explicit_overrides_and_inheritance(string attributes, int? expected)
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<textPath href='#p' " + attributes + "/>"));
        reader.Read();
        SvgTextAnchor? actual = SvgTextPathSyntax.Parse(reader, new(long.MaxValue), CancellationToken.None).Anchor;
        Assert.Equal(expected, actual is {} anchor ? (int)anchor : null);
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
    [InlineData("href='#p' text-anchor='center'", SvgErrorCode.UnsupportedFeature)]
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
    [InlineData("M8 48 H56 Z", "")]
    [InlineData("M8 48 H16 M24 48 H56", "")]
    [InlineData("M8 48 H56", "pathLength='0'")]
    public void Unimplemented_contours_and_calibration_release_temporary_commands(string data, string attributes)
    {
        RenderBudget budget = new(long.MaxValue);
        using SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None);
        SvgDefinitionLeafSource source = Capture(data, attributes, state);
        long retained = budget.Snapshot().CurrentBytes;
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgTextPathLayout.ReadBaseline(source, 100, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(retained, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("M0 0 L3 4", 5, 1.5, 2, .6, .8)]
    [InlineData("M8 48 H-16", 24, -4, 48, -1, 0)]
    [InlineData("M0 0 Q5 0 10 0", 10, 5, 0, 1, 0)]
    [InlineData("M0 0 C0 0 10 0 10 0", 10, 5, 0, 1, 0)]
    public void Open_lines_and_polynomial_curves_place_the_advance_midpoint(string data, double length, double x, double y, double a, double b)
    {
        RenderBudget budget = new(long.MaxValue);
        using (SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None))
        {
            SvgDefinitionLeafSource source = Capture(data, "", state);
            long retained = budget.Snapshot().CurrentBytes;
            using (SvgMeasuredTextPath path = SvgTextPathLayout.ReadMeasured(source, 100, budget, new(long.MaxValue), CancellationToken.None))
            {
                Assert.Equal(length, path.Baseline.Length);
                SvgAffineTransform map = path.GlyphMap(length / 2, 0, 0, new(long.MaxValue), CancellationToken.None);
                Assert.Equal(x, map.E, 5); Assert.Equal(y, map.F, 5);
                Assert.Equal(a, map.A, 6); Assert.Equal(b, map.B, 6);
                SvgAffineTransform shifted = path.GlyphMap(length / 2, 2, 3, new(long.MaxValue), CancellationToken.None);
                SvgDevicePoint midpoint = shifted.Apply(2, -3);
                Assert.Equal(x, midpoint.X, 5); Assert.Equal(y, midpoint.Y, 5);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retained_curved_A_placement_matches_independent_browser_character_geometry()
    {
        // Chrome 153.0.8010.52, intended Noto Sans face proven by CSS.getPlatformFontsForNode.
        // Original 128x96 curved-midpoint control; full native measurements are retained beside its references.
        RenderBudget budget = new(long.MaxValue);
        using SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None);
        SvgDefinitionLeafSource source = Capture("M8 72 C24 8 104 8 120 72", "", state);
        using SvgMeasuredTextPath path = SvgTextPathLayout.ReadMeasured(source, 100, budget, new(long.MaxValue), CancellationToken.None);
        SvgAffineTransform map = path.GlyphMap(24 + 12.78125 / 2, 12.78125 / 2, 0, new(long.MaxValue), CancellationToken.None);
        Assert.Equal(17.154211044311523, map.E, 6);
        Assert.InRange(Math.Abs(49.75111770629883 - map.F), 0, .000001);
        Assert.Equal(-50.969764709472656, Math.Atan2(map.B, map.A) * 180 / Math.PI, 4);
        Assert.InRange(path.Baseline.Length, 159.22885, 159.22886);
    }

    [Fact]
    public void Curve_measurement_and_lookup_denials_release_owners_and_allow_retry()
    {
        RenderBudget budget = new(long.MaxValue);
        using SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None);
        SvgDefinitionLeafSource source = Capture("M8 72 C24 8 104 8 120 72", "pathLength='320'", state);
        long retained = budget.Snapshot().CurrentBytes;
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgTextPathLayout.ReadMeasured(source, 100, budget, new(50), CancellationToken.None)).ErrorCode);
        Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        using (SvgMeasuredTextPath path = SvgTextPathLayout.ReadMeasured(source, 100, budget, new(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(path.Baseline.Length / 320, path.Baseline.OffsetScale);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => path.GlyphMap(30, 6, 0, new(1), CancellationToken.None)).ErrorCode);
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => path.GlyphMap(30, 6, 0, new(long.MaxValue), cancelled.Token)).CancellationToken);
            Assert.True(double.IsFinite(path.GlyphMap(30, 6, 0, new(long.MaxValue), CancellationToken.None).E));
        }
        Assert.Equal(retained, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Nonfinite_curve_evaluation_is_categorized_and_releases_measurement_storage()
    {
        RenderBudget budget = new(long.MaxValue);
        using SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None);
        SvgDefinitionLeafSource source = Capture("M0 0 C2e38 0 2.5e38 0 3e38 0", "", state);
        long retained = budget.Snapshot().CurrentBytes;
        using (SvgMeasuredTextPath path = SvgTextPathLayout.ReadMeasured(source, 100, budget, new(long.MaxValue), CancellationToken.None))
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => path.GlyphMap(path.Baseline.Length / 2, 1, 0, new(long.MaxValue), CancellationToken.None)).ErrorCode);
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

    [Theory]
    [InlineData("pathLength='96'", 0.5)]
    [InlineData("pathLength='24'", 2)]
    [InlineData("transform='translate(8 0) scale(2)' pathLength='96'", 1)]
    public void Positive_path_calibration_scales_offsets_in_composed_coordinates(string attributes, double scale)
    {
        RenderBudget budget = new(long.MaxValue);
        using (SvgDocumentState state = new(budget, new(long.MaxValue), CancellationToken.None))
        {
            SvgDefinitionLeafSource source = Capture("M8 48 H56", attributes, state);
            long retained = budget.Snapshot().CurrentBytes;
            Assert.Equal(scale, SvgTextPathLayout.ReadBaseline(source, 100, budget, new(long.MaxValue), CancellationToken.None).OffsetScale);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
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
