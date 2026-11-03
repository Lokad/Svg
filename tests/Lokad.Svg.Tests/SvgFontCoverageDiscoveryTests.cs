using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.Testing;

namespace Lokad.Svg.Tests;

public sealed class SvgFontCoverageDiscoveryTests
{
    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Discovery_carries_a_split_scalar_without_retaining_trimmed_whitespace()
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None))
        {
            coverage.BeginText(); coverage.SetFont(SvgInheritedTextStyle.Default);
            coverage.Observe(" \t", SvgTextWhitespaceMode.Default);
            coverage.Observe("\uD835", SvgTextWhitespaceMode.Default);
            coverage.Observe("\uDC00", SvgTextWhitespaceMode.Default);
            coverage.Observe(" \n", SvgTextWhitespaceMode.Default);
            Assert.Equal(new uint[] { 0x1D400 }, coverage.GetScalars(new("serif", 400, false)));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public void Coverage_requests_emitted_scalars_in_streaming_and_normalized_paths(bool streaming)
    {
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None);
        SvgInheritedTextStyle style = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start)
            { TextTransform = SvgTextTransform.Uppercase };
        if (streaming)
        {
            coverage.BeginText(); coverage.SetFont(style);
            coverage.Observe("ßa\uD801", SvgTextWhitespaceMode.Default);
            coverage.Observe("\uDC28", SvgTextWhitespaceMode.Default);
        }
        else coverage.ObserveNormalized(new("Example", 400, false), "ßa\U00010428", style.TextTransform, style.Language);
        Assert.Equal(new uint[] { 0x41, 0x53, 0x10400 }, coverage.GetScalars(new("Example", 400, false)).Order());
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Materialized_scalar_needs_remain_charged_until_their_coverage_owner_finishes()
    {
        using TestMemoryBudget root = new(1_000_000);
        RenderBudget budget = new(root);
        using (SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None))
        {
            SvgFontFaceRequest request = new("Example", 400, false);
            string text = new(Enumerable.Range(33, 94).Select(static value => (char)value).ToArray());
            coverage.ObserveNormalized(request, text);
            long collecting = root.Reserved;
            uint[] scalars = coverage.GetScalars(request);
            Assert.Equal(94, scalars.Length);
            Assert.True(root.Reserved >= collecting + scalars.Length * sizeof(uint), "Returned scalar storage must stay admitted while its acquisition borrower is active.");
        }
        Assert.Equal(0, root.Reserved);
        Assert.False(root.Disposed);
    }

    [Fact]
    public void Coverage_storage_denial_releases_the_parser_owner()
    {
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(1));
        using (SvgFontCoverageDiscovery coverage = new(denied, CancellationToken.None))
        {
            coverage.BeginText(); coverage.SetFont(SvgInheritedTextStyle.Default);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => coverage.Observe("A", SvgTextWhitespaceMode.Default)).ErrorCode);
        }
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgFontCoverageDiscovery coverage = new(budget, CancellationToken.None))
        {
            coverage.BeginText(); coverage.SetFont(SvgInheritedTextStyle.Default);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
