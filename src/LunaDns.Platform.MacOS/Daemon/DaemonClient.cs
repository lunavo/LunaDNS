using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>Gửi một yêu cầu tới daemon. Daemon chưa cài hoặc chưa chạy thì ném DaemonUnavailableException.</summary>
public sealed class DaemonClient(string socketPath = DaemonProtocol.SocketPath)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<DaemonResponse> SendAsync(DaemonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new DaemonUnavailableException("Dịch vụ nền LunaDNS chưa chạy. Cài bằng: sudo lunadns install", ex);
        }

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, DaemonProtocol.RequestInfo) + "\n");
        SocketException? sendError = null;
        try
        {
            await socket.SendAsync(payload, SocketFlags.None, timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            // Daemon từ chối UID ngay khi nhận kết nối: ghi câu trả lời rồi đóng, không đọc yêu cầu. Nếu nó đóng trước khi
            // ta gửi xong thì gửi gặp EPIPE, nhưng câu trả lời đã nằm sẵn trong bộ đệm nhận, nên vẫn đọc tiếp.
            sendError = ex;
        }

        // Đọc thẳng từ socket: sau EPIPE .NET coi socket là đã ngắt nên NetworkStream không nhận nó nữa.
        string? line;
        try
        {
            line = await ReadLineAsync(socket, timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException) when (sendError is not null)
        {
            line = null;
        }
        if (line is null)
        {
            const string closed = "Dịch vụ nền LunaDNS đóng kết nối mà không trả lời.";
            throw sendError is null ? new DaemonUnavailableException(closed) : new DaemonUnavailableException(closed, sendError);
        }
        return JsonSerializer.Deserialize(line, DaemonProtocol.ResponseInfo)
            ?? throw new DaemonUnavailableException("Dịch vụ nền LunaDNS trả lời rỗng.");
    }

    /// <summary>Đọc tới '\n' hoặc tới khi daemon đóng. Đóng mà chưa có byte nào thì null.</summary>
    private static async Task<string?> ReadLineAsync(Socket socket, CancellationToken cancellationToken)
    {
        using var received = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            received.Write(buffer, 0, newline >= 0 ? newline : read);
            if (newline >= 0) break;
        }
        return received.Length == 0 ? null : Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length);
    }
}

public sealed class DaemonUnavailableException : Exception
{
    public DaemonUnavailableException()
    {
    }

    public DaemonUnavailableException(string message) : base(message)
    {
    }

    public DaemonUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
