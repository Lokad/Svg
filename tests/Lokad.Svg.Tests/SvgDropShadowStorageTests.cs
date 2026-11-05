using System.Globalization;
using System.Security.Cryptography;
using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgDropShadowStorageTests
{
    // linearRGB retains a separate high-precision final sampling plane.
    [Theory]
    [InlineData(0, 0, 1, -1, "sRGB", false, 49151, "5393aa61fdda2102a677fcb7e8af028cf7819f683e02b3c150c750d4c7e09824")]
    [InlineData(.03f, .03f, -.25f, .75f, "linearRGB", false, 98303, "d4eb961af263b677676a5c5c610653b9a7922c39de8f0786952890015d74af73")]
    [InlineData(.1f, .1f, 0, 1, "linearRGB", false, 98303, "d5cd0dbd605f2cdc43de77381c2ee8bfd2cb484e5b8a41296f03f75a4cd27236")]
    [InlineData(0, 2, -.375f, .625f, "sRGB", false, 49151, "22d97b1a9878634200bc49484e3eaad79cc1ae07068d7c5048e5b3b2c6d5e822")]
    [InlineData(2, 0, .3f, -.7f, "sRGB", false, 49151, "f992cceeee657ff86d1f37259b7d26bec53724c74f5ef71e5cb187f8528722d7")]
    [InlineData(2, 3, -.5f, .375f, "linearRGB", false, 98303, "78dbceed8badcb6c653efca2e80bdcc5ee060f50661ebddd362004295e8a4e4f")]
    [InlineData(2, 3, -.5f, .375f, "sRGB", false, 65535, "6df0aa44fb7c78b3be51c49087bae6e09749868febd8030e0b7ede18fd72c6a0")]
    [InlineData(2, 3, 1, 0, "linearRGB", true, 114687, "9c33acf3d5911ce30a340b88550c24a6619bcb87db18f8773b7d6aadd3cc25dc")]
    public void Destination_scratch_preserves_shadow_pixels_and_borrowed_input(float horizontal, float vertical,
        float offsetX, float offsetY, string colorSpace, bool previous, long maximumBytes, string expectedRgbaSha256)
    {
        const int size = 64, surfaceBytes = size * size * 4;
        byte[] source = new byte[surfaceBytes];
        for (int y = 17; y < 43; y++)
        for (int x = 19; x < 47; x++)
        {
            int offset = (y * size + x) * 4;
            byte alpha = (byte)(1 + (x * 31 + y * 17) % 255);
            source[offset] = (byte)(alpha / 3); source[offset + 1] = (byte)(alpha / 2);
            source[offset + 2] = (byte)(alpha * 3 / 4); source[offset + 3] = alpha;
        }
        byte[] unchanged = source.ToArray();
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>"
            + "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='64' height='64' color-interpolation-filters='" + colorSpace + "'>"
            + (previous ? "<feOffset dx='0' dy='0'/>" : "")
            + "<feDropShadow stdDeviation='" + Number(horizontal) + " " + Number(vertical)
            + "' dx='" + Number(offsetX) + "' dy='" + Number(offsetY) + "' flood-color='#397db3' flood-opacity='.6'/>"
            + "</filter></defs><rect width='64' height='64' filter='url(#f)'/></svg>";
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(TextEncoding.UTF8.GetBytes(svg), SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterResource resource = (root.FilterResources ?? throw new InvalidDataException("Missing filter resource.")).ResourceAt(0);
            SvgFilterDeviceMap map = new(1, 1)
            {
                Regions = new(SvgAffineTransform.Identity, new(size, size), new(0, 0, size, size))
                { SourceBounds = new(0, 0, size, size) }
            };
            using HostMemoryBudget host = new(2_000_000);
            RenderBudget raster = new(host);
            Verify();
            host.Limit = maximumBytes;
            Verify();
            host.Limit = (previous ? 3 : 2) * surfaceBytes - 1;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgFilterResult denied = SvgFilterPlayback.Apply(source, size, size, resource, map, new(0, 0, size, size), raster, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            host.Limit = maximumBytes;
            Verify();
            using CancellationTokenSource canceled = new(); canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() =>
            {
                using SvgFilterResult denied = SvgFilterPlayback.Apply(source, size, size, resource, map, new(0, 0, size, size), raster, canceled.Token);
            }).CancellationToken);
            Assert.Equal(0, host.Reserved);
            Assert.False(host.Disposed);

            void Verify()
            {
                using (SvgFilterResult result = SvgFilterPlayback.Apply(source, size, size, resource, map, new(0, 0, size, size), raster, CancellationToken.None))
                {
                    string actual = Convert.ToHexStringLower(SHA256.HashData(result.Pixels));
                    Assert.True(expectedRgbaSha256 == actual, actual);
                }
                Assert.Equal(0, host.Reserved);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                Assert.Equal(unchanged, source);
            }
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);

        static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
