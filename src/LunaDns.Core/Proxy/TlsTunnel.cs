using System.Net.Sockets;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Proxy;

/// <summary>Phần chung của SplitProxy và SniForwarder: đọc TLS record đầu, ghi cắt gói, chuyển byte hai chiều.</summary>
internal static class TlsTunnel
{
    internal const int MaxRecordLength = 5 + 16384;
    internal const int BufferSize = 32 * 1024;

    /// <summary>
    /// Đọc tới khi buffer chứa trọn TLS record đầu tiên (hoặc client đóng sớm). RecordLength = -1 khi dữ liệu
    /// không phải record handshake hoặc khai dài quá cỡ; khi đó Count là số byte đang có.
    /// </summary>
    internal static async Task<(int Count, int RecordLength)> ReadFirstRecordAsync(Stream client, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        count = await FillAsync(client, buffer, count, 5, cancellationToken).ConfigureAwait(false);
        var recordLength = ClientHelloParser.RecordLength(buffer.AsSpan(0, count));
        if (recordLength < 0 || recordLength > MaxRecordLength) return (count, -1);
        count = await FillAsync(client, buffer, count, recordLength, cancellationToken).ConfigureAwait(false);
        return (count, recordLength);
    }

    /// <summary>Ghi record đầu sang máy chủ: cắt gói nếu là record handshake, chuyển nguyên nếu không; byte thừa ghi sau.</summary>
    internal static async Task WriteFirstRecordAsync(Stream upstream, byte[] buffer, int count, int recordLength, SplitMode mode, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (recordLength < 0)
        {
            await upstream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            return;
        }
        var helloLength = Math.Min(count, recordLength);
        await SplitWriter.WriteAsync(upstream, buffer.AsMemory(0, helloLength), mode, delay, cancellationToken).ConfigureAwait(false);
        if (count > helloLength)
            await upstream.WriteAsync(buffer.AsMemory(helloLength, count - helloLength), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Chuyển byte hai chiều tới khi một bên đóng, rồi dừng chiều còn lại. Trả lỗi đã kết thúc tunnel (để log), hoặc null.</summary>
    internal static async Task<Exception?> RelayAsync(Stream client, Stream upstream, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var up = client.CopyToAsync(upstream, linked.Token);
        var down = upstream.CopyToAsync(client, linked.Token);
        await Task.WhenAny(up, down).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(up, down).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            return ex;
        }
    }

    /// <summary>Đọc tới khi buffer có ít nhất <paramref name="needed"/> byte. Client đóng sớm thì trả số byte đang có.</summary>
    private static async Task<int> FillAsync(Stream source, byte[] buffer, int count, int needed, CancellationToken cancellationToken)
    {
        while (count < needed)
        {
            var read = await source.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        return count;
    }
}
