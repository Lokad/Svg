using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgGroupClipPlacementTests
{
    [Theory]
    [InlineData("translated")]
    [InlineData("scaled")]
    [InlineData("reflected")]
    [InlineData("child-transform")]
    [InlineData("parent-clip")]
    [InlineData("viewport-offset")]
    public void Group_clip_follows_its_reference_space_in_both_definition_orders(string scenario)
    {
        const string clip = "<defs><clipPath id='clip'><rect x='4' y='3' width='17' height='13'/></clipPath></defs>";
        const string path = "<path d='M0,0 L32,2 L32,30 L0,28 Z' fill='#427ac9'/>";
        string translated = "<g transform='translate(.375 .25)' clip-path='url(#clip)'>";
        (string before, string after, SvgClipBox expected) = scenario switch
        {
            "translated" => (translated, "</g>", new SvgClipBox(4.375, 3.25, 17, 13)),
            "scaled" => ("<g transform='matrix(1.25 0 0 .875 5.125 3.375)' clip-path='url(#clip)'>", "</g>", new SvgClipBox(10.125, 6, 21.25, 11.375)),
            "reflected" => ("<g transform='translate(2.625 1.25)'><g transform='matrix(-1 0 0 1 34 0)' clip-path='url(#clip)'>", "</g></g>", new SvgClipBox(15.625, 4.25, 17, 13)),
            "child-transform" => (translated + "<g transform='translate(2.125 .5)'>", "</g></g>", new SvgClipBox(4.375, 3.25, 17, 13)),
            "parent-clip" => ("<defs><clipPath id='parent'><rect x='10' width='26' height='40'/></clipPath></defs><g clip-path='url(#parent)'>" + translated, "</g></g>", new SvgClipBox(10, 3.25, 11.375, 13)),
            "viewport-offset" => ("<svg x='3' y='2' width='40' height='32'>" + translated, "</g></svg>", new SvgClipBox(7.375, 5.25, 17, 13)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        byte[]? first = null;
        foreach (bool forward in new[] { false, true })
        {
            string drawing = before + path + after;
            byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='40'>" + (forward ? drawing + clip : clip + drawing) + "</svg>");
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
            Assert.Equal(expected, Assert.Single(scene.Geometries).Polygon.Clip);
            byte[] png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
            if (first is null) first = png;
            else Assert.Equal(first, png);
        }
    }
}
