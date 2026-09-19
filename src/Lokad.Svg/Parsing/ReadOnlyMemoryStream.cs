namespace Lokad.Svg.Parsing;

/// <summary>Exposes caller-owned read-only memory as a non-copying synchronous stream.</summary>
internal sealed class ReadOnlyMemoryStream : Stream
{
    private readonly ReadOnlyMemory<byte> _memory;
    private int _position;

    internal ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) { _memory = memory; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _memory.Length;
    public override long Position { get => _position; set { if (value is < 0 or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(value)); _position = (int)value; } }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) { int available = Math.Min(buffer.Length, _memory.Length - _position); _memory.Span.Slice(_position, available).CopyTo(buffer); _position += available; return available; }
    public override int ReadByte() { if (_position == _memory.Length) return -1; return _memory.Span[_position++]; }
    public override long Seek(long offset, SeekOrigin origin) { long next = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset, SeekOrigin.End => _memory.Length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }; Position = next; return next; }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
