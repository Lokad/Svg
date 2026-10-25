using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgContainerPresentationResolverTests
{
    [Fact]
    public void Shared_presentation_visibility_resolution_observes_both_inherited_states()
    {
        Assert.False(SvgPresentationResolver.IsPainted(new(null, null, null, null, "inline", "visible", null, null), false, true));
        Assert.False(SvgPresentationResolver.IsPainted(new(null, null, null, null, "inline", null, null, null), true, false));
        Assert.True(SvgPresentationResolver.IsPainted(new(null, null, null, null, "inline", "visible", null, null), true, false));
    }

    [Fact]
    public void Common_presentation_resolution_retains_inherited_paint_and_visibility()
    {
        SvgPaint inheritedPaint = new(new(new SvgColor(1, 2, 3, 255), 1), true, new SvgPaintOpacity(128, 128 / 255f), new(new SvgColor(4, 5, 6, 255), 1), true);
        SvgComputedStyle style = new(null, null, null, null, "none", "visible", "0.5", null);

        SvgResolvedPresentation presentation = SvgPresentationResolver.Resolve(style, inheritedPaint, true, false, new Dictionary<string, SvgColor>());

        Assert.Equal(inheritedPaint, presentation.Paint);
        Assert.False(presentation.IsDisplayed);
        Assert.True(presentation.IsVisible);
        Assert.False(presentation.IsPainted);
        Assert.Equal((byte)128, presentation.Opacity);
    }

    [Fact]
    public void Resolves_the_typed_container_presentation_from_the_computed_cascade()
    {
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = new SvgColor(0, 0, 255, 255)
        };
        SvgComputedStyle style = new("blue", "currentColor", "0.5", "none", "none", "hidden", "0.5", null);

        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, null, true, true, namedColors);

        Assert.Equal(new SvgPaint(new(SvgColor.Transparent, 0), true, new SvgPaintOpacity(128, 0.5f), new(new SvgColor(0, 0, 255, 255), 1), true), presentation.Paint);
        Assert.False(presentation.IsDisplayed);
        Assert.False(presentation.IsVisible);
        Assert.Equal((byte)128, presentation.Opacity);
    }

    [Fact]
    public void Accepts_painted_stroke_for_container_inheritance()
    {
        SvgComputedStyle style = new(null, null, null, "red", null, null, null, null);

        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, null, true, true, new Dictionary<string, SvgColor>());

        Assert.True(presentation.IsPainted);
    }
}
