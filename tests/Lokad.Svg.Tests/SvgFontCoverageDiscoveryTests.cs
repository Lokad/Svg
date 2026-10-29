using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.Testing;

namespace Lokad.Svg.Tests;

public sealed class SvgFontCoverageDiscoveryTests
{
    [Fact]
    public void Discovery_carries_a_split_scalar_without_retaining_trimmed_whitespace()
    {
        RenderBudget budget = new(long.MaxValue);
        using (SvgFontCoverageDiscovery coverage = new(budget, new(long.MaxValue), CancellationToken.None))
        {
            coverage.BeginText(); coverage.SetFont(SvgInheritedTextStyle.Default);
            coverage.Observe(" \t", SvgTextWhitespaceMode.Default);
            coverage.Observe("\uD835", SvgTextWhitespaceMode.Default);
            coverage.Observe("\uDC00", SvgTextWhitespaceMode.Default);
            coverage.Observe(" \n", SvgTextWhitespaceMode.Default);
            Assert.Equal(new uint[] { 0x1D400 }, coverage.GetScalars(new("serif", 400, false)));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.False(SvgFontCoverageDiscovery.AllowsScalarFallback(0x301));
        Assert.False(SvgFontCoverageDiscovery.AllowsScalarFallback(0x200D));
        Assert.False(SvgFontCoverageDiscovery.AllowsScalarFallback(0x627));
    }

    [Fact]
    [Trait("Layer", "Contracts")]
    public void Materialized_scalar_needs_remain_charged_until_their_coverage_owner_finishes()
    {
        using TestMemoryBudget root = new(1_000_000);
        RenderBudget budget = new(root);
        using (SvgFontCoverageDiscovery coverage = new(budget, new(long.MaxValue), CancellationToken.None))
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
    public void Coverage_storage_and_work_denial_release_the_parser_owner()
    {
        RenderBudget denied = new(1);
        using (SvgFontCoverageDiscovery coverage = new(denied, new(long.MaxValue), CancellationToken.None))
        {
            coverage.BeginText(); coverage.SetFont(SvgInheritedTextStyle.Default);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => coverage.Observe("A", SvgTextWhitespaceMode.Default)).ErrorCode);
        }
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget budget = new(long.MaxValue);
        using (SvgFontCoverageDiscovery coverage = new(budget, new(1), CancellationToken.None))
        {
            coverage.BeginText(); coverage.SetFont(SvgInheritedTextStyle.Default);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => coverage.Observe("AéA", SvgTextWhitespaceMode.Default)).ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
