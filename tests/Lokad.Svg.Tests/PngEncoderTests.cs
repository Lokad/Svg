using Lokad.Svg.Encoding;
using System.Buffers.Binary;

namespace Lokad.Svg.Tests;

public sealed class PngEncoderTests
{
    [Fact]
    public void Encodes_a_valid_rgba8_png_with_straight_alpha_pixels()
    {
        byte[] png = PngEncoder.Encode(1, 1, [10, 20, 30, 40], CancellationToken.None);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, 37, 4));
    }

    [Fact]
    public void Cancellation_is_checked_before_scanline_encoding()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PngEncoder.Encode(1, 1, [0, 0, 0, 0], cancellation.Token));
    }

    [Fact]
    public void Encodes_directly_into_caller_destination()
    {
        byte[] owned = PngEncoder.Encode(1, 1, [1, 2, 3, 4], CancellationToken.None);
        byte[] destination = new byte[owned.Length];

        int written = PngEncoder.EncodeInto(1, 1, [1, 2, 3, 4], destination, CancellationToken.None);

        Assert.Equal(owned.Length, written);
        Assert.Equal(owned, destination);
        Assert.Throws<SvgException>(() => PngEncoder.EncodeInto(1, 1, [1, 2, 3, 4], new byte[1], CancellationToken.None));
    }

}
