using System.Security.Cryptography;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAffineFloodTests
{
    // Chrome 153.0.8010.52, Chromium 1681091, recipe 3, 32x32 transparent RGBA.
    // Each vector describes the actual filtered SVG, including native region
    // rounding where an unfiltered rectangle has different pixels.
    private static readonly string[] NativeHashes =
    [
        "590D140F13D7C4FAC61D0745E2ECE4E5AFC4CE50AF80F4A8470F5FCF00495101",
        "3BB38B37C6E0CE3EDD553EB489AC36475B7FDFD23F9B22F84E5024FB35CEEAA4",
        "1A536B274506AA64511F6FB9117682D66A9C4EF667085FF92830E2B6A4F64A04",
        "03D33F47CADAE67FEE1FF6BFC96395F302752ED73562B6F6487531A8E44A4F8D",
        "C701213791F89BECC47A2FB88DB2272AE62D7D0CD8BF23FA0F0B812511E3BA37",
        "BA0E47EF606BD2F983661655A6CB57494A9E69114FB83A12A9115B0E382B8081",
        "878FF10C277094E0668DB4714439295FB00DFD2CE8C93458D5CC42284206F285",
        "AAC9E475C509E514C3A04A4292BFC0C3F568BBE69C5F733EB097C23F32C7BA1C",
        "57272C11E6AD1F58251C79362139570E988E56BD59D13C542E04985F58770FD5",
        "5CF7C80610ABF3BACAFF068973E52F6C4D8D0B200CCDBAC3EC575851CC8777F8",
        "BF2D41621A2B70350383B7576B16708AFA63B59BCB71DB1E32B2994AD9AB7749",
        "22E975EE4374B7C2E73BF5D3B496C11D19D1058A6BB2AC5B0B310A75E9D6BD3F",
        "4D048224A6055ECCE5C71BE23838B449DD31C6DF79B7349A2AE763670DC31D9F",
        "1DCEE5459DFF1DFEF97E58387803CCF62D8DB808C70B61B4E551485CFD072CF1",
        "9FDE0099717120485A50526ABFE47AAA1B3061B8775E9575F520228729C4F994",
        "42CFF8840691505B87CEE92229D7C2BAAE4407B815C57F4BBDA92D736D1BD6AC",
        "708C6F2615C1B7638DA7A468FF144522F5954F40AAC4758C2911D9BA9A31FA4F",
        "FB524EAC86A7D9BCA7D53990264B5F501BE741DA7238CEA97A024F5F14ABD4BA",
        "445A3139CC6EF7B094B64B00BE9058045B8E74AC76147C1D0FA28A1B4A7438E1",
        "19F566C517E2421C333725866797617947C051F5849873DCB47F17966D5C87EC",
        "D3007375570164DCB9EAB772E5B9624D6E41ACECAB5C53815F8AE145902943AA",
        "087F6F39EB6ADBD04833415EA2F319E5CC2455DF96AB8D8D83F1CCD0112CB273",
        "8330C3CB9E957F5A3FFEAD1CFC9E0FFA9AC39B00EDDF6541CF17716173A38868",
        "B794CAA5DDB66DA7663B89CA4B95A9360735D95C91170C49CB03BB1278AE85E0",
        "D66450C7CDCB64C87C656E26FE07A7BBA053019280315616B23021A53AB132F7",
        "B2CEB82CC5253E4CFE25D12531975216A7A3720B15CFFC67F36AD9655E76C4E0",
        "35D9F99D9EF14203056426E1080E596244B0669E72C32DA81BA9D13A20E33EA9",
        "9AFCE170E6F7BA6DE48F5F7AB791E15C4F05BFEDE0DC3D6F7EB443DCB9E3114E",
        "1453D3D8672E47C3C03DD9AECEB810E29203C3431553BFF62E13E46B4E3D0D41",
        "84C13D6EA128B65B7815A76EB586BDFABD5BF51B00FCD6B9965A78188D6649B0",
        "4DCDDEE501E2D65C7FBD6E24938FB51D5B984B05524C223CC086F6AD671D9C3C",
        "36CC9DEAF68AC1C80B10F410C0D110C52DDB4C586A05DD0EC08C2385EA1865B6",
        "47D5CD32C2EB8F72E94DC7AD3469E5DC6ED157B7E850A9968C87D26605AB89F0",
        "5667B061B2E77F84A4CD7F72ABA820EEA9DB958E3DA77A390C8B7C8F150496A4",
        "8AAAA679E878A65A02644A4CF0D3E55A9DE3D1345BB9A1AA87E23B2F3B818BA1",
        "6288A5F085409818E03C2F78854077191E37C6318759233920D438F724CDC5C5",
        "AC95734C668B33E8B355326B5A57ED2402937AEEECE22B13EE5D06EC3F0E5AFF",
        "DA0365DF57F75338CD2C875B778509A379D590AFE31A107BCE22207F9554B869",
        "FCB4FE6480EF3DDC4062FB76D0D18A7731901A2BFC89F927241C1F5FAABDFB3A",
        "D95FF5F02AA64AAF807C2FB432865FD4895B72568F8C6A5C22D8261F0E869AF1",
        "8BB224BDC600D425DDB71983B04FDB05F5CA6F962CD27F562E53CE2DC35F7526",
        "6BE526B1C3DC66CF5C4549B1CEC3A5F428177FE2EF22E82836D0881CAC33475E",
        "56A36AD62B9A2E828C012393BA6E34100F46C3370EE5D610C39E05109BEE94E2",
        "DBD118C90149B6DCBA05AFE44CE01FB65C749A543F634F48C6AF7DD65745B0BE",
        "ABA96606CF31B240AF771E926DD701357F5F7479FBF444E1A2500B8E5FA4B51A",
        "590D140F13D7C4FAC61D0745E2ECE4E5AFC4CE50AF80F4A8470F5FCF00495101",
        "3BB38B37C6E0CE3EDD553EB489AC36475B7FDFD23F9B22F84E5024FB35CEEAA4",
        "1A536B274506AA64511F6FB9117682D66A9C4EF667085FF92830E2B6A4F64A04",
        "771A1F5B57FE8E268A1A5DF78E2D74925F4BB425DCC9B4DE9D10642C061E1A97",
        "FE46A333297C10BF7DE1C9FFDDEA72585A30179AE3EFC858F9103662C9840FBA",
        "ADDE43F4D3B22C49724BB5C2924B70713E01996B1561C554737A59BB5A6F766A",
        "E74C67D37F4EB67942871725EFFF507751691591703410099423EE407560A49F",
        "6D5C80474AB3B58461000B6BA3CD24285DD3020517FD1A80E19BA6BC5488AA65",
        "6188A9E53CA0FB55111464A33D3E81B2CF1BC47AA864B20651201B3F0632B32B",
    ];

    public static IEnumerable<object[]> NativeRegions()
    {
        string[] maps = ["", "rotate(15 16 16)", "skewX(12)", "matrix(1 .2 .1 1 0 0)", "scale(1.25 .75) rotate(15 16 16)", "rotate(90 16 16)"];
        string[] regions = ["x='0' y='0' width='32' height='32'", "x='2' y='3' width='24' height='23'", "x='2.25' y='3.5' width='24.5' height='23.25'"];
        (string Color, string Opacity)[] colors = [("blue", "1"), ("#4f83bc", ".43"), ("rgba(187,63,21,.6)", ".7")];
        for (int map = 0; map < maps.Length; map++)
        for (int region = 0; region < regions.Length; region++)
        for (int color = 0; color < colors.Length; color++)
        foreach (string source in map is 0 or 5 ? new[] { "rect" } : new[] { "rect", "group", "use", "zero-symbol" })
        foreach (bool previous in new[] { false, true })
            yield return [maps[map], regions[region], colors[color].Color, colors[color].Opacity, source, previous, NativeHashes[(map * 3 + region) * 3 + color]];
    }

    public static IEnumerable<object[]> BoundaryRegions()
    {
        string[] maps = ["translate(32 0) scale(-1 1) rotate(15 16 16)", "rotate(-35 16 16)", "matrix(-.8 .35 .2 1.15 24 -2)", "skewX(75)", "matrix(.01 .002 .003 .01 12 12)", "rotate(89.999 16 16)", "translate(.125 -.75) rotate(15 16 16)", "translate(16777216 0) rotate(15) translate(-16777216 0)", "matrix(1 1 1 1.00001 0 0)"];
        string[] regions = ["x='0' y='0' width='32' height='32'", "x='2' y='3' width='24' height='23'", "x='2.25' y='3.5' width='24.5' height='23.25'"];
        string[] hashes =
        [
            "82312D96AB52554EDC1B4BA52EE970331C3C1B4BF989B0F3228786ACFBA5E8BF",
            "3AF06DB4390301B420CBADAB1B7B3649A19901E2509C2075E238ABDC7E6763A7",
            "386B0647DE7EFAEAA58D6ED752F118565C0B60BCD61D6D08A6DBC2EB42F9D8C6",
            "92358866C683656560AD2BCBD7178CE9B20B1E6CA32F07053ECA7C9FE2C7A139",
            "583CBD05C10FC2BC6E45AAAC08A2CAD30522291C056CDF5A44C640E16745DAF4",
            "ACB30384E9F616A9ED33854BF884FB9A0BF098CADB97F8FA1DA992EE05FB87AA",
            "1F140FBEC6E4218779E4F4B0C701890E1AA24901C5384D368DC289A594125F16",
            "CD5956A3C67413D86ED53C8C1C092BBC58F29D677266A0EDCD85F5A123084AB1",
            "0EC0E6F03859DC328A8A19251135C1C7ACA544F2E32F63CAC7F4E2B247FBC929",
            "01EB90DC4D03656EEF7EB3947B496A77EF2815B5284DA2FB05877B8B0BE19824",
            "4C022F1EB7DE1DBBE5C49012414528FDF09DA10078A400BB20DAD393B517AEF9",
            "6D2434767B12855E9069287CD5147B3B08AB2CA86266A667B3101AE7D229E72D",
            "A378A2E34F1169141EE6F3868A82C38B56122CDF75AE2FF1A947A529A344D947",
            "A378A2E34F1169141EE6F3868A82C38B56122CDF75AE2FF1A947A529A344D947",
            "A378A2E34F1169141EE6F3868A82C38B56122CDF75AE2FF1A947A529A344D947",
            "590D140F13D7C4FAC61D0745E2ECE4E5AFC4CE50AF80F4A8470F5FCF00495101",
            "B88D4C4BA3A853CE7F89C28733794CD4C3D11D5CD7A405F73013FC6BDB330129",
            "C7D310AA04005E77AA791D2499808FDA653ED934F7CACEDD8CCB40BB7E6FDA83",
            "EAEA2378FE66A1911C0FDBAA5A24F3B51BF1D8F14B3D5D93C0255434A319BF11",
            "99EFE819940CB7825A1FEF06BD91E2E84CDCC01FFB0C05D6DB33DE4BC445F73B",
            "02EBC665FE895D012095694838B725096F5E7A4BB792062EDF406A88799432AD",
            "AD7FACB2586FC6E966C004D7D1D16B024F5805FF7CB47C7A85DABD8B48892CA7",
            "AD7FACB2586FC6E966C004D7D1D16B024F5805FF7CB47C7A85DABD8B48892CA7",
            "AD7FACB2586FC6E966C004D7D1D16B024F5805FF7CB47C7A85DABD8B48892CA7",
            "AD7FACB2586FC6E966C004D7D1D16B024F5805FF7CB47C7A85DABD8B48892CA7",
            "FCE955A90E001D9EEA77F21BE35ADACBEFCE8C08AA0DE5BD8F3E4273F751A580",
            "C8143FB95CC82E4C882DD6BEF86C9F37F4B66009F0EEFAF7E969F191F630AF5F",
        ];
        for (int map = 0; map < maps.Length; map++)
        for (int region = 0; region < regions.Length; region++)
        foreach (string source in new[] { "rect", "group", "use", "zero-symbol" })
        foreach (bool previous in new[] { false, true })
            yield return [maps[map], regions[region], "blue", "1", source, previous, hashes[map * 3 + region]];
    }

    [Theory]
    [MemberData(nameof(NativeRegions))]
    [MemberData(nameof(BoundaryRegions))]
    public void Terminal_floods_preserve_native_mapped_region_coverage(string map, string region, string color, string opacity, string source, bool previous, string expectedHash)
    {
        byte[] input = Input(map, region, color, opacity, source, previous);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(surface.Pixels)));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("zero-symbol")]
    public void Affine_flood_restoration_releases_on_memory_refusal_cancellation_and_retry(string source)
    {
        byte[] input = Input("rotate(15 16 16)", "x='2.25' y='3.5' width='24.5' height='23.25'", "#4f83bc", ".43", source, true);
        AssertRestorationLimits(input);
    }

    [Fact]
    public void Deferred_offset_restoration_preserves_pixels_and_owner_lifetimes()
    {
        string svg = System.Text.Encoding.UTF8.GetString(Input("rotate(15 16 16)", "x='0' y='0' width='32' height='32'", "blue", "1", "group", false));
        svg = svg.Replace("<feFlood flood-color='blue' flood-opacity='1'/>", "<feOffset in='SourceGraphic' dx='.3' dy='.7'/>", StringComparison.Ordinal);
        AssertRestorationLimits(System.Text.Encoding.UTF8.GetBytes(svg));
    }

    private static void AssertRestorationLimits(byte[] input)
    {
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            byte[] expected;
            RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            { expected = surface.Pixels.ToArray(); }
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
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

    [Theory]
    [InlineData("rotate(15 16 16)", "blue", "1", "pattern", "42CFF8840691505B87CEE92229D7C2BAAE4407B815C57F4BBDA92D736D1BD6AC")]
    [InlineData("rotate(15 16 16)", "blue", "1", "mask", "B01C56808D64913C0769BA0F2B4E1D79D26C085E9C6422FEA236A44079BDB8A0")]
    [InlineData("rotate(15 16 16)", "#4f83bc", ".43", "pattern", "708C6F2615C1B7638DA7A468FF144522F5954F40AAC4758C2911D9BA9A31FA4F")]
    [InlineData("rotate(15 16 16)", "#4f83bc", ".43", "mask", "F9C66CA4BD5AE8BD61BAE5FFF4A89FDC517B8A8ED0FA02EBEF0983DC6CF96887")]
    [InlineData("skewX(12)", "blue", "1", "pattern", "D66450C7CDCB64C87C656E26FE07A7BBA053019280315616B23021A53AB132F7")]
    [InlineData("skewX(12)", "blue", "1", "mask", "65AC38DE2732D1BE50CA3C3F3AFB7EBA3755E033BA295C39A51032125925738B")]
    [InlineData("skewX(12)", "#4f83bc", ".43", "pattern", "B2CEB82CC5253E4CFE25D12531975216A7A3720B15CFFC67F36AD9655E76C4E0")]
    [InlineData("skewX(12)", "#4f83bc", ".43", "mask", "3427FD28661B1B23F73F0B0F60E62BC4C292CE1AD1E87297E4BF853755826D18")]
    [InlineData("scale(1.25 .75) rotate(15 16 16)", "blue", "1", "pattern", "56A36AD62B9A2E828C012393BA6E34100F46C3370EE5D610C39E05109BEE94E2")]
    [InlineData("scale(1.25 .75) rotate(15 16 16)", "blue", "1", "mask", "1924C3D98E7911A5726A7AD22E5E6252254881C295281084A3314985140AFDD1")]
    [InlineData("scale(1.25 .75) rotate(15 16 16)", "#4f83bc", ".43", "pattern", "DBD118C90149B6DCBA05AFE44CE01FB65C749A543F634F48C6AF7DD65745B0BE")]
    [InlineData("scale(1.25 .75) rotate(15 16 16)", "#4f83bc", ".43", "mask", "34F23DB874DECDD41E63F2303F6C91FA0A4E7AA5F69231927B78F9A0D5C0C67F")]
    public void Terminal_floods_preserve_native_pattern_and_mask_consumers(string map, string color, string opacity, string context, string expectedHash)
    {
        string svg = System.Text.Encoding.UTF8.GetString(Input(map, "x='2.25' y='3.5' width='24.5' height='23.25'", color, opacity, "rect", true));
        int start = svg.IndexOf("</defs>", StringComparison.Ordinal) + "</defs>".Length;
        string body = svg[start..^"</svg>".Length];
        string content = context switch
        {
            "pattern" => "<defs><pattern id='consumer' patternUnits='userSpaceOnUse' width='32' height='32'>" + body
                + "</pattern></defs><rect width='32' height='32' fill='url(#consumer)'/>",
            "mask" => "<defs><mask id='consumer' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + body
                + "</mask></defs><rect width='32' height='32' fill='red' mask='url(#consumer)'/>",
            _ => throw new ArgumentException("Unknown resource consumer.", nameof(context))
        };
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg[..start] + content + "</svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(surface.Pixels)));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string map, string region, string color, string opacity, string source, bool previous)
    {
        string node = source switch
        {
            "rect" => "<rect x='4' y='4' width='8' height='8' fill='red' filter='url(#f)'/>",
            "group" => "<g filter='url(#f)'><circle cx='8' cy='8' r='4' fill='red'/></g>",
            "use" => "<use href='#empty' filter='url(#f)'/>",
            "zero-symbol" => "<use href='#s' width='0' height='32' filter='url(#f)'/>",
            _ => throw new ArgumentException("Unknown source form.", nameof(source))
        };
        string graph = (previous ? "<feOffset dx='1'/>" : "") + "<feFlood flood-color='" + color + "' flood-opacity='" + opacity + "'/>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><filter id='f' filterUnits='userSpaceOnUse' " + region + ">" + graph
            + "</filter><g id='empty'/><symbol id='s' viewBox='0 0 32 32'><rect width='32' height='32' fill='red'/></symbol></defs><g transform='" + map + "'>" + node + "</g></svg>");
    }
}
