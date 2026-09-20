using System.Buffers;
using System.Runtime.InteropServices;

namespace Lokad.Svg.Tests;

public sealed class DestinationOverlapTests
{
    [Theory]
    [InlineData(128, 64)]
    [InlineData(0, 512)]
    [InlineData(112, 32)]
    [InlineData(176, 512)]
    [InlineData(144, 8)]
    public async Task Overlap_is_rejected_before_parsing_or_writing(int destinationOffset, int destinationLength)
    {
        // Deliberately malformed input also proves rejection precedes SVG parsing.
        byte[] storage = Enumerable.Repeat((byte)0xA5, 1024).ToArray();
        byte[] original = (byte[])storage.Clone();
        using HiddenArrayMemory first = new(storage);
        using HiddenArrayMemory second = new(storage);
        Assert.False(MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)first.Memory, out _));
        Memory<byte>[] backings = [storage, first.Memory, second.Memory];

        foreach (Memory<byte> inputBacking in backings)
        foreach (Memory<byte> outputBacking in backings)
        {
            ReadOnlyMemory<byte> input = inputBacking.Slice(128, 64);
            Memory<byte> destination = outputBacking.Slice(destinationOffset, destinationLength);

            ArgumentException synchronous = Assert.Throws<ArgumentException>(() => SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
            ArgumentException asynchronous = await Assert.ThrowsAsync<ArgumentException>(() => SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));

            Assert.Equal("destination", synchronous.ParamName);
            Assert.Equal("destination", asynchronous.ParamName);
            Assert.Equal(original, storage);
        }
    }

    [Fact]
    public async Task Disjoint_slices_of_shared_storage_produce_identical_output_and_preserve_input()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'/>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] storage = Enumerable.Repeat((byte)0xA5, 1024).ToArray();
        svg.CopyTo(storage, 128);
        using HiddenArrayMemory first = new(storage);
        using HiddenArrayMemory second = new(storage);
        Memory<byte>[] backings = [storage, first.Memory, second.Memory];

        foreach (Memory<byte> inputBacking in backings)
        foreach (Memory<byte> outputBacking in backings)
        foreach (int destinationOffset in new[] { 128 - expected.Length, 128 + svg.Length })
        {
            ReadOnlyMemory<byte> input = inputBacking.Slice(128, svg.Length);
            Memory<byte> destination = outputBacking.Slice(destinationOffset, expected.Length);

            int synchronous = SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected.Length, synchronous);
            Assert.Equal(expected, destination.ToArray());
            destination.Span.Fill(0xA5);
            int asynchronous = await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None);

            Assert.Equal(expected.Length, asynchronous);
            Assert.Equal(expected, destination.ToArray());
            Assert.Equal(svg, input.ToArray());
        }
    }

    [Fact]
    public void Empty_destination_is_insufficient_not_overlapping()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, svg.AsMemory(1, 0), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.InsufficientDestination, exception.ErrorCode);
    }

    /// <summary>Exposes stable shared test storage without an array segment; never pins or owns its bytes.</summary>
    private sealed class HiddenArrayMemory : MemoryManager<byte>
    {
        private readonly byte[] _storage;

        internal HiddenArrayMemory(byte[] storage) => _storage = storage;

        public override Span<byte> GetSpan() => _storage;
        public override MemoryHandle Pin(int elementIndex) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
