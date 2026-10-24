namespace Lokad.Svg.Tests;

public sealed class SvgContainedClipIsolationTests
{
    public static IEnumerable<object[]> ContainedMaskPaints()
    {
        foreach (string paint in new[] { "linear", "radial", "pattern" })
        foreach (string alpha in new[] { "1", ".75", ".5" })
        foreach (string maskType in new[] { "alpha", "luminance" })
        foreach (string opacity in new[] { "1", ".5" })
        foreach (string pose in new[] { "", "translate(.5 .25) scale(.875 1.125)" })
        foreach (double scale in new[] { 1.0, 1.5 })
            yield return [paint, alpha, maskType, opacity, pose, scale];
    }

    [Theory]
    [MemberData(nameof(ContainedMaskPaints))]
    public void A_clip_containing_the_bitmap_preserves_masked_shader_origins(string paint, string alpha, string maskType, string opacity, string pose, double scale)
    {
        string resource = paint switch
        {
            "linear" => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='20'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>"
        };
        foreach (bool use in new[] { false, true })
        {
            string leaf = "<path d='M4 4H24V24H4Z' fill='red' stroke='url(#p)' stroke-width='.25' stroke-opacity='.75'/>";
            string definition = use ? "<g id='source'>" + leaf + "</g>" : "";
            if (use) leaf = "<use href='#source'/>";
            string resources = resource + definition + "<clipPath id='c'><rect width='32' height='32'/></clipPath><mask id='m' mask-type='" + maskType + "' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='" + alpha + "'/></mask>";
            string body = "<g opacity='" + opacity + "' mask='url(#m)'><g transform='" + pose + "'>" + leaf + "</g></g>";
            // Independent native captures establish that these complete-bitmap
            // clips leave both the image and the child mask's shader grid unchanged.
            Assert.Equal(Render(body), Render("<g clip-path='url(#c)'>" + body + "</g>"));

            byte[] Render(string content) => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + resources + "</defs>" + content + "</svg>"), new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None).ToArray();
        }
    }
}
