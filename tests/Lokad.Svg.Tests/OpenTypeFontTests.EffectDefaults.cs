using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> NoEffectReferences()
    {
        foreach (string effect in new[] { "clip-path", "mask" })
        foreach (string scope in new[] { "rect", "path", "group", "use", "pattern", "clip", "mask" })
        foreach (bool forward in new[] { false, true }) yield return [effect, scope, forward];
    }

    [Theory]
    [MemberData(nameof(NoEffectReferences))]
    public async Task None_effect_references_are_inert_with_ancestor_effects_and_replay(string effect, string scope, bool forward)
    {
        byte[] input = Svg(" " + effect + "=' none '"), reference = Svg("");
        byte[] expected = SvgConverter.ConvertToPng(reference, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        byte[] Svg(string attribute)
        {
            string definitions = "<clipPath id='outer'><rect width='6' height='8'/></clipPath>";
            string body;
            switch (scope)
            {
                case "rect": body = "<rect width='8' height='8'" + attribute + "/>"; break;
                case "path": body = "<path d='M0 0H8V8H0Z'" + attribute + "/>"; break;
                case "group": body = "<g" + attribute + "><rect width='8' height='8'/></g>"; break;
                case "use":
                    definitions += "<rect id='s' width='8' height='8'/>";
                    body = "<use href='#s'" + attribute + "/>"; break;
                case "pattern":
                    definitions += "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'" + attribute + "/></pattern>";
                    body = "<rect width='8' height='8' fill='url(#p)'/>"; break;
                case "clip":
                    definitions += "<clipPath id='c'><rect width='8' height='8'" + attribute + "/></clipPath>";
                    body = "<rect width='8' height='8' clip-path='url(#c)'/>"; break;
                default:
                    definitions += "<mask id='m'><rect width='8' height='8' fill='white'" + attribute + "/></mask>";
                    body = "<rect width='8' height='8' mask='url(#m)'/>"; break;
            }
            string drawing = "<g fill='red' clip-path='url(#outer)'>" + body + "</g>";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>"
                + (forward ? drawing + "<defs>" + definitions + "</defs>" : "<defs>" + definitions + "</defs>" + drawing) + "</svg>");
        }
    }
}
