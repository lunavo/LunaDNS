using System.Net;
using System.Net.Sockets;

namespace LunaDns.Core.Tests.Proxy;

/// <summary>Máy chủ TCP loopback trả lại đúng byte nhận được, đóng vai máy chủ thật sau proxy.</summary>
internal sealed class EchoOrigin : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public EchoOrigin()
    {
        _listener.Start();
        _loop = AcceptLoopAsync();
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public Stream Connect()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        socket.Connect(IPAddress.Loopback, Port);
        return new NetworkStream(socket, ownsSocket: true);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            _ = EchoAsync(client);
        }
    }

    private async Task EchoAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                await stream.CopyToAsync(stream, _stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                // Proxy đóng kết nối hoặc test kết thúc: hết việc của kết nối này.
                return;
            }
        }
    }
}
