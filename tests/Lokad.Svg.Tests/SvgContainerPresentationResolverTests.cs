using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgContainerPresentationResolverTests
{
    [Fact]
    public void Resolves_the_typed_container_presentation_from_the_computed_cascade()
    {
        IReadOnlyDictionary<string, SvgColor> namedColors = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = new SvgColor(0, 0, 255, 255)
        };
        SvgComputedStyle style = new("blue", "currentColor", "0.5", "none", "none", "hidden", "0.5", null);

        SvgResolvedContainerPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, true, true, namedColors);

        Assert.Equal(new SvgPaint(SvgColor.Transparent, true, 128, new SvgColor(0, 0, 255, 255), true), presentation.Paint);
        Assert.False(presentation.IsDisplayed);
        Assert.False(presentation.IsVisible);
        Assert.Equal((byte)128, presentation.Opacity);
    }
}
