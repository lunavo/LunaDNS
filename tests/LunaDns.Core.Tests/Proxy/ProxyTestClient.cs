using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LunaDns.Core.Tests.Proxy;

internal static class ProxyTestClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task<NetworkStream> ConnectAsync(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(IPAddress.Loopback, port);
        return new NetworkStream(socket, ownsSocket: true);
    }

    public static async Task SendAsync(Stream stream, string text) =>
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text));

    /// <summary>Gửi một yêu cầu rồi đọc tới khi proxy đóng kết nối.</summary>
    public static async Task<string> RequestAsync(int port, string head)
    {
        await using var stream = await ConnectAsync(port);
        await SendAsync(stream, head);
        using var timeout = new CancellationTokenSource(Timeout);
        using var all = new MemoryStream();
        await stream.CopyToAsync(all, timeout.Token);
        return Encoding.ASCII.GetString(all.ToArray());
    }

    /// <summary>Đọc từng byte tới hết dòng trống, để không nuốt mất dữ liệu của tunnel phía sau.</summary>
    public static async Task<string> ReadHeadAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var bytes = new List<byte>();
        var one = new byte[1];
        while (!EndsWithBlankLine(bytes))
        {
            if (await stream.ReadAsync(one, timeout.Token) == 0) break;
            bytes.Add(one[0]);
        }
        return Encoding.ASCII.GetString([.. bytes]);
    }

    public static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, timeout.Token);
        return buffer;
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Hết 5 giây mà điều kiện chưa đạt.");
            await Task.Delay(10);
        }
    }

    private static bool EndsWithBlankLine(List<byte> bytes) =>
        bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n';
}
