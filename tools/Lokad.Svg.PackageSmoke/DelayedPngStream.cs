using System.Security.Cryptography;

/// <summary>Owns a digest of successful writes; each write borrows its buffer until its gate or cancellation completes.</summary>
/// <remarks>Single-writer test destination. Callers finish all writes before disposal. No PNG copy is retained; synchronous writes are deliberately unsupported.</remarks>
internal sealed class DelayedPngStream : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _disposed;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal byte[] Digest() => _hash.GetHashAndReset();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Checks cancellation while borrowing the complete buffer through real asynchronous backpressure.</summary>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Entered.TrySetResult();
        await Finish.Task.WaitAsync(cancellationToken);
        _hash.AppendData(buffer.Span);
    }

    /// <summary>Releases only owned digest storage after the caller has finished every write.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _hash.Dispose(); }
        base.Dispose(disposing);
    }
}
