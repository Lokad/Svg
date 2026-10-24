using System.Security.Cryptography;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFractionalMaskRegionTests
{
    // Chrome 153.0.8010.52 / Chromium 1681091, recipe 3, 32x32 transparent RGBA.
    // These are actual masked images. Native mask and opacity inputs differ.
    private static readonly string[] NativeHashes =
    [
        "9E027642F04A588C266E82FE3A27A42AA0C5191BE2B51C25452DD5180D04FB8F",
        "DE6222E155E26C2DED5C02FE48EB3106113E1C0E03CA51C0CB5EF5ADDBB223E6",
        "76158AD19722A6AE2E0B7477CFF6096A0A982E6BB54C6E258397A4EA747B9147",
        "15EA96978169B024BFB09CA1B4D7B17606C3BA9CCE47E9EB832FC7A6035AE2DB",
        "8C8924CC6FF925896B2523FCED25432E479CD6985537FB5CC61F181B5651D593",
        "6C9267A1774148354D6A56D43BC9484C5543E917F54F8D8D02FB3802F589C939",
        "768A65FF2EBE7ABFDAD93CB0DD1522408C71DB37FDF40696F6688C04B621EBC2",
        "C56BDEFC01CA94BA9D7B0AD7C30E98A89ED20DD99C6C87090B377D2D971C04AC",
        "F00CF9833E082CB0BF5FE0CABA58B834C2B46644365B3F2651D97AF783F2DFF2",
        "3BE7247AE80D18307D01429E4DD365BA217B64777C8C5B66C6BB2C6D8EFCBAA9",
        "599C7739EFAC29ADB669FD9666A481F9CE494F42947D6F36C49B0E1299385BE6",
        "FB02768782D27506F264807A59DAAF0E4AC6EFFD762CB77BD0539757CD1B59F7",
        "19229E709A485375314575B5B79A275F5E52D7628761C4E562CB819884AF37B7",
        "C26A28636719D4A1398F247C89362584671E6E9D1861DE616792EDEA4EA978F1",
        "5499EFD8DD507235E1F3C2BCF297CCC33CEAFA35EE3AEF08DAAC5D4237713F15",
        "DCE26B1F73ECC3E70EF86DD05471515813D369FBB659B5E25406D7188DCCB8C2",
    ];

    public static IEnumerable<object[]> MaskRegions()
    {
        string[] shapes = ["circle", "curve"];
        string[] alphas = ["0.25", "0.5", "0.75", "1"];
        for (int shape = 0; shape < shapes.Length; shape++)
        for (int alpha = 0; alpha < alphas.Length; alpha++)
        foreach (string consumer in new[] { "group", "use", "symbol" })
        foreach (bool expanded in new[] { false, true })
        foreach (string kind in new[] { "alpha", "luminance" })
        {
            // A symbol retains its own fractional viewport clip when the mask expands.
            int index = shape * 8 + alpha * 2 + (expanded && consumer != "symbol" ? 1 : 0);
            yield return [shapes[shape], alphas[alpha], consumer, expanded, kind, NativeHashes[index]];
        }
    }

    [Theory]
    [MemberData(nameof(MaskRegions))]
    public void Fractional_mask_regions_preserve_native_source_coverage(string shape, string alpha, string consumer, bool expanded, string kind, string expectedHash)
    {
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(Document(shape, alpha, consumer, expanded, kind), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(surface.Pixels)));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("translate(.5 .25)")]
    [InlineData("translate(4 3)")]
    [InlineData("scale(.75 1.1)")]
    public void Uniform_mask_regions_move_with_their_instance(string map)
    {
        const string shape = "<rect x='4' y='4' width='24' height='24' fill='red'/>";
        const string mask = "<defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='16' height='16'><rect x='-64' y='-64' width='128' height='128' fill='white'/></mask></defs>";
        string expected = "<g transform='" + map + "' mask='url(#m)'>" + shape + "</g>";
        string input = "<defs>" + shape.Replace("<rect", "<rect id='q'", StringComparison.Ordinal) + "</defs><use href='#q' transform='" + map + "' mask='url(#m)'/>";
        Assert.Equal(Render(Input(mask + expected)), Render(Input(mask + input)));
        Assert.NotEqual(Render(Input("<g transform='" + map + "'>" + shape + "</g>")), Render(Input(mask + input)));
    }

    [Theory]
    [InlineData("circle", "translate(-8 0)", false, "E6033C61CF9ED4061DE1CAA9AA324D749BEA2F4D4C94674B69652161656EE397")]
    [InlineData("circle", "translate(4 3)", false, "5C585C80832A99503D11D5B5A0DEA9EF17A3368C9A795E97F47694130A2137EC")]
    [InlineData("circle", "scale(.75 1.1)", false, "6DBC95F78CBED4402B97B2161D179294AF4EAFCECB14C178D89931F7E6C9A451")]
    [InlineData("circle", "translate(-8 0)", true, "1001347BA84B60038062E63745ED2B78B7C6368C337A4822DA8B95B7BE13F4C7")]
    [InlineData("circle", "translate(4 3)", true, "25010FD3D38517702AD564223FC3A6A8A3ABC16C374F5036417466E3F07A3046")]
    [InlineData("circle", "scale(.75 1.1)", true, "74A7960E0D838AE83D8DB52E70BD1295028938AA37CAA61677FBBF3644B09456")]
    [InlineData("curve", "translate(-8 0)", false, "E12E364F9E609A8F6C78F25400C0D0421DCBE785255DF6C2A7A06B1E699656B8")]
    [InlineData("curve", "translate(4 3)", false, "C53C6E3C2F4B1FD3D002E843DD33D4558BE5774ED1AD78AF584678FE1FD13240")]
    [InlineData("curve", "scale(.75 1.1)", false, "C274ED7D1103EBECBE2C2F1F1D3467D48602D6D699E585EA42FE4A3AC0885109")]
    [InlineData("curve", "translate(-8 0)", true, "7D3A56133BD882BEBFFD406AFBC627A22516A0341A7D122D66D1FE91713214CC")]
    [InlineData("curve", "translate(4 3)", true, "46E5639FF640CF16F697C476B163582169406A3F85F9EEEF58F79EEA78419A0B")]
    [InlineData("curve", "scale(.75 1.1)", true, "4FEC9E4B20262D3D15747DD370B8EA2EC855E279B9229EEBF391C50C1E06DC9C")]
    public void Copied_masked_leaves_recompute_their_source_paint_clip(string shape, string map, bool expanded, string nativeHash)
    {
        string geometry = shape == "circle" ? "<circle cx='16' cy='16' r='10' fill='red' mask='url(#m)'/>" : "<path d='M4 26Q16 -4 28 26Z' fill='red' mask='url(#m)'/>";
        string coordinates = expanded ? "x='-32' y='-32' width='96' height='96'" : "x='0' y='0' width='32' height='32'";
        string mask = "<defs><mask id='m' maskUnits='userSpaceOnUse' " + coordinates + "><rect " + coordinates + " fill='white' fill-opacity='.25'/></mask></defs>";
        string content = "<g transform='translate(.5 .25)'>" + geometry + "</g>";
        byte[] expected = Input(mask + content + "<g transform='" + map + "'>" + content + "</g>");
        byte[] input = Input(mask + "<g id='q'>" + content + "</g><use href='#q' transform='" + map + "'/>");
        Assert.Equal(Render(expected), Render(input));
        RenderBudget budget = new(1_000_000);
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
        Assert.Equal(nativeHash, Convert.ToHexString(SHA256.HashData(surface.Pixels)));

    }

    [Theory]
    [InlineData("group", "alpha")]
    [InlineData("group", "luminance")]
    [InlineData("use", "alpha")]
    [InlineData("use", "luminance")]
    [InlineData("copy", "alpha")]
    [InlineData("copy", "luminance")]
    public void Mask_rasterization_releases_storage_preserves_borrowers_and_retries(string consumer, string kind)
    {
        byte[] input = Document("curve", "0.25", consumer, false, kind);
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            RenderBudget measured = new(1_000_000);
            byte[] expected;
            long work;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            { expected = surface.Pixels.ToArray(); work = surface.WorkUnits; }
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            RenderBudget denied = new(measured.Snapshot().PeakBytes + 72);
            using (denied.Reserve(73, SvgMemoryCategory.Parser))
            {
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, CancellationToken.None)).ErrorCode);
                Assert.Equal(73, denied.Snapshot().CurrentBytes);
            }
            SvgRenderLimits limits = SvgRenderLimits.Default;
            SvgRenderOptions shortWork = new(null, 1, SvgColor.Transparent,
                new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                    limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)work - 1)));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, shortWork, measured, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, cancellation.Token)).CancellationToken);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    private static byte[] Document(string shape, string alpha, string consumer, bool expanded, string kind)
    {
        string geometry = shape == "circle" ? "<circle cx='16' cy='16' r='10' fill='red'/>" : "<path d='M4 26Q16 -4 28 26Z' fill='red'/>";
        string coordinates = expanded ? "x='-32' y='-32' width='96' height='96'" : "x='0' y='0' width='32' height='32'";
        string resources = "<mask id='m' maskUnits='userSpaceOnUse' " + coordinates + " mask-type='" + kind + "'><rect " + coordinates + " fill='white' fill-opacity='" + alpha + "'/></mask>";
        if (consumer == "copy")
        {
            string masked = geometry.Replace("/>", " mask='url(#m)'/>", StringComparison.Ordinal);
            return Input("<defs>" + resources + "</defs><g id='q'><g transform='translate(.5 .25)'>" + masked + "</g></g><use href='#q' x='4' y='3'/>");
        }
        if (consumer == "use") resources += "<g id='q'>" + geometry + "</g>";
        if (consumer == "symbol") resources += "<symbol id='s' viewBox='0 0 32 32'>" + geometry + "</symbol>";
        string body = consumer == "use" ? "<use href='#q' mask='url(#m)'/>" : consumer == "symbol" ? "<use href='#s' width='32' height='32' mask='url(#m)'/>" : geometry;
        return Input("<defs>" + resources + "</defs><g transform='translate(.5 .25)'" + (consumer == "group" ? " mask='url(#m)'" : "") + ">" + body + "</g>");
    }

    private static byte[] Input(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
    private static byte[] Render(byte[] input) => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
