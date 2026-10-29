using Lokad.Svg.Parsing;
using ReadOnlyMemoryStream = Lokad.Svg.Parsing.ReadOnlyMemoryStream;

namespace Lokad.Svg.Tests;

public sealed class ReadOnlyMemoryStreamTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void Reads_at_or_beyond_end_return_eof_without_moving_position(int position)
    {
        byte[] input = [1, 2, 3];
        using ReadOnlyMemoryStream stream = new(input);
        Assert.Equal(position, stream.Seek(position, SeekOrigin.Begin));
        byte[] output = [99, 99];
        Assert.Equal(0, stream.Read(output, 0, output.Length));
        Assert.Equal(0, stream.Read(output.AsSpan()));
        Assert.Equal(0, stream.Read(Span<byte>.Empty));
        Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(position, stream.Position);
        Assert.Equal(new byte[] { 99, 99 }, output);

        Assert.Equal(2, stream.Seek(-1, SeekOrigin.End));
        Assert.Equal(3, stream.ReadByte());
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public void Reads_use_the_borrowed_memory_and_stop_at_its_end()
    {
        byte[] input = [1, 2, 3];
        using ReadOnlyMemoryStream stream = new(input);
        input[1] = 7;
        byte[] output = new byte[5];
        Assert.Equal(3, stream.Read(output.AsSpan()));
        Assert.Equal(new byte[] { 1, 7, 3, 0, 0 }, output);
        Assert.Equal(0, stream.Read(output.AsSpan()));
        Assert.Equal(-1, stream.ReadByte());
    }
}
