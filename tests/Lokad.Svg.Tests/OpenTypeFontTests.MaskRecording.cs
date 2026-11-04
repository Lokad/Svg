using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Affine_mask_replay_restores_following_text_font_context_and_borrowed_memory()
    {
        const string following = "<text x='55.375' y='42.625' font-family='Example' font-size='200'>A A</text>";
        const string mask = "<defs><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='64'><g><text x='8' y='40' font-family='Example' font-size='200'>A</text></g></mask></defs>";
        const string target = "<g transform='matrix(.92 .18 -.12 1.03 8.5 -4.25)'><rect width='32' height='64' fill='blue' mask='url(#m)'/></g>";
        byte[] input = Document(mask + target + following);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleAAndSpaceFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RenderBudget budget = new(new HostMemoryBudget(4_000_000));
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot control = SvgRootParser.Parse(Document(following), SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
            using (SvgConverter.RasterizedSurface expected = SvgConverter.Rasterize(control, options, budget, CancellationToken.None))
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
            using (SvgConverter.RasterizedSurface actual = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
            {
                Assert.Contains(expected.Pixels, value => value != 0);
                for (int y = 0; y < 64; y++)
                {
                    int offset = (y * 96 + 48) * 4;
                    Assert.True(expected.Pixels.AsSpan(offset, 48 * 4).SequenceEqual(actual.Pixels.AsSpan(offset, 48 * 4)), $"Following text row {y} differs.");
                }
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static byte[] Document(string body)
            => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='64'>" + body + "</svg>");
    }
}
