using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgTransparentGroupStorageTests
{
    public enum Scope { Opacity, ConstantMask, FilterOpacity }

    [Theory]
    [InlineData(Scope.Opacity)]
    [InlineData(Scope.ConstantMask)]
    [InlineData(Scope.FilterOpacity)]
    public void Transparent_scopes_need_no_child_surface_and_preserve_the_borrowed_scene(Scope scope)
    {
        byte[] input = Input(scope), unchanged = input.ToArray();
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            Assert.False(root.HasUnsupportedChild);
            long retained = parser.Snapshot().CurrentBytes;
            const long oneSurface = 64 * 64 * 4;
            const long maximumRasterBytes = 2 * oneSurface - 1;
            using HostMemoryBudget host = new(maximumRasterBytes);
            RenderBudget raster = new(host);
            CheckPixels();
            Assert.InRange(raster.Snapshot().PeakBytes, oneSurface, maximumRasterBytes);
            host.Limit = oneSurface - 1;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            host.Limit = maximumRasterBytes;
            CheckPixels();
            Assert.False(host.Disposed);
            Assert.Equal(retained, parser.Snapshot().CurrentBytes);

            void CheckPixels()
            {
                using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None))
                {
                    for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        ReadOnlySpan<byte> pixel = surface.Pixels.AsSpan((y * 64 + x) * 4, 4);
                        Assert.Equal(0, pixel[0]); Assert.Equal(0, pixel[2]);
                        Assert.Equal(x >= 56 && y >= 56 ? (byte)255 : (byte)0, pixel[1]);
                        Assert.Equal(x >= 56 && y >= 56 ? (byte)255 : (byte)0, pixel[3]);
                    }
                }
                Assert.Equal(0, host.Reserved);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
            }
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);

        static byte[] Input(Scope scope)
        {
            string attributes = scope switch
            {
                Scope.Opacity => "opacity='0'",
                Scope.ConstantMask => "mask='url(#m)'",
                Scope.FilterOpacity => "filter='url(#f)' opacity='0'",
                _ => throw new ArgumentOutOfRangeException(nameof(scope))
            };
            return TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>"
                + "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='64' height='64'><rect width='64' height='64' fill='black'/></mask>"
                + "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='64' height='64'><feFlood flood-color='red'/></filter></defs>"
                + "<g " + attributes + "><rect width='48' height='48' fill='red'/><g><rect x='16' y='8' width='40' height='48' fill='blue'/></g>"
                + "<rect width='32' height='32' fill='none' filter='url(#f)'/></g>"
                + "<path d='M56 56H64V64H56Z' fill='#00ff00'/>"
                + "</svg>");
        }
    }
}
