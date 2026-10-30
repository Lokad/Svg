namespace Lokad.Svg.Tests;

public sealed class SvgFilterBitmapReplayTests
{
    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Adjacent_filter_bitmaps_restore_each_translucent_pixel_once()
    {
        byte[] pixels = SvgRasterTestHelper.RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='780' height='600'><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='780' height='600'><feFlood flood-color='black' flood-opacity='.5'/></filter></defs><g filter='url(#f)'><rect width='1' height='1' fill='none'/></g></svg>", out int width, out int height);
        Assert.Equal((780, 600), (width, height));
        ReadOnlySpan<byte> expected = [0, 0, 0, 128];
        for (int offset = 0; offset < pixels.Length; offset += 4)
            if (!pixels.AsSpan(offset, 4).SequenceEqual(expected))
                Assert.Fail($"Pixel ({offset / 4 % width}, {offset / 4 / width}) was not restored exactly once.");
    }
}
