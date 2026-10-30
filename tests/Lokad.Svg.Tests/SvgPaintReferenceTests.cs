using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgPaintReferenceTests
{
    [Theory]
    [InlineData("url(#g)", "g", "")]
    [InlineData(" URL( '#g' ) none ", "g", "none")]
    [InlineData("url(#g)rgb(10,200,40)", "g", "rgb(10,200,40)")]
    [InlineData("url('#g)') red", "g)", "red")]
    [InlineData("url(#g%29)currentColor", "g)", "currentColor")]
    [InlineData("url(#%2573) rgba(1,2,3,.5)", "%73", "rgba(1,2,3,.5)")]
    public void Separates_paint_url_and_fallback_before_local_fragment_decoding(string value, string id, string fallback)
    {
        Assert.True(SvgPaintReference.TryRead(value, out SvgPaintReference reference));
        Assert.Equal(fallback, reference.Fallback.ToString());
        Assert.True(SvgPaintReference.TryParseId(value, out string actual));
        Assert.Equal(id, actual);
    }

    [Theory]
    [InlineData("url(#g")]
    [InlineData("url('#g) red")]
    [InlineData("url('#g' red)")]
    [InlineData("url(#g(x)) red")]
    [InlineData("url(#) red")]
    [InlineData("url(https://example.org/g.svg#g) red")]
    [InlineData("url(#%GG) red")]
    public void Rejects_invalid_or_nonlocal_paint_references(string value)
        => Assert.False(SvgPaintReference.TryParseId(value, out _));

    [Theory]
    [InlineData("url(#g) red")]
    [InlineData("url(#g)rgb(10,200,40)")]
    [InlineData("url('#g)') none")]
    public void Effect_references_keep_their_whole_value_grammar(string value)
        => Assert.False(SvgLocalUrl.TryParseId(value, out _));

    [Fact]
    public void Preserving_filter_bounds_keeps_resource_metadata_for_nested_span_fallback()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop stop-color='red'/></linearGradient></defs></svg>"u8.ToArray();
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgPaintResourceSelection? resources = preparation.GetPaintResources();
            Assert.NotNull(resources);
            SvgTextPaintStyle parent = SvgTextPaintStyle.ResolveElement(default(SvgComputedStyle) with { Fill = "none" },
                SvgPaint.Default, true, true, null, resources).ResolveResources(resources, true, false);
            SvgTextPaintStyle span = SvgTextPaintStyle.ResolveSpan(default(SvgComputedStyle) with { Fill = "url(#g) blue" }, parent);
            Assert.True(span.GlyphPaint.HasFill);
            Assert.Equal("url(#g) blue", span.GlyphPaint.FillServer);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
