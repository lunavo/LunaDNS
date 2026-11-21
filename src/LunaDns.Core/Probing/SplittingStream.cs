using LunaDns.Core.Tls;

namespace LunaDns.Core.Probing;

/// <summary>
/// Bọc một stream: lần ghi đầu tiên (ClientHello do SslStream ghi) được cắt theo <see cref="HelloSplitter"/>,
/// các lần sau chuyển thẳng. Nhờ vậy Prober thử được đúng cách cắt gói mà proxy sẽ dùng.
/// </summary>
internal sealed class SplittingStream : Stream
{
    private readonly Stream _inner;
    private readonly HelloSplit _split;
    private bool _first = true;

    public SplittingStream(Stream inner, HelloSplit split)
    {
        _inner = inner;
        _split = split;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_first)
        {
            _inner.Write(buffer, offset, count);
            return;
        }
        _first = false;
        foreach (var piece in HelloSplitter.Split(buffer.AsMemory(offset, count), _split))
        {
            _inner.Write(piece.Span);
            _inner.Flush();
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_first)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            return;
        }
        _first = false;
        await SplitWriter.WriteAsync(_inner, buffer, _split, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
