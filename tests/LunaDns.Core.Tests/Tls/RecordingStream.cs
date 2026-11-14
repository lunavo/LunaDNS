namespace LunaDns.Core.Tests.Tls;

/// <summary>Stream giả: ghi lại từng lần Write thành một mảng riêng; Read treo tới khi Dispose hoặc token huỷ.</summary>
internal sealed class RecordingStream : Stream
{
    private readonly TaskCompletionSource<int> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<byte[]> _writes = [];

    public IReadOnlyList<byte[]> Writes
    {
        get
        {
            lock (_writes) return [.. _writes];
        }
    }

    public int TotalBytes
    {
        get
        {
            lock (_writes) return _writes.Sum(w => w.Length);
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        new(_closed.Task.WaitAsync(cancellationToken));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        lock (_writes) _writes.Add(buffer.AsSpan(offset, count).ToArray());
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        lock (_writes) _writes.Add(buffer.ToArray());
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        _closed.TrySetResult(0);
        base.Dispose(disposing);
    }
}
