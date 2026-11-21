using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Probing;

public sealed class TlsProbeTests : IAsyncLifetime
{
    private const string Host = "store.steampowered.com";
    private LoopbackTlsServer _server = null!;

    public Task InitializeAsync()
    {
        _server = new LoopbackTlsServer(Host);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private TlsProbe Trusting(int? port = null) => new(port ?? _server.Port, HelloSplit.Default, _server.Trusts, null);

    [Fact]
    public async Task Intact_handshake_succeeds_with_trusted_certificate() =>
        Assert.True(await Trusting().HandshakeAsync(IPAddress.Loopback, Host, split: false, CancellationToken.None));

    [Fact]
    public async Task Split_handshake_is_reassembled_by_the_server() =>
        Assert.True(await Trusting().HandshakeAsync(IPAddress.Loopback, Host, split: true, CancellationToken.None));

    [Fact]
    public async Task Untrusted_certificate_is_a_failure()
    {
        // Không truyền callback: dùng kiểm tra mặc định của hệ điều hành, chứng chỉ tự ký bị từ chối.
        var probe = new TlsProbe(_server.Port, HelloSplit.Default, null, null);

        Assert.False(await probe.HandshakeAsync(IPAddress.Loopback, Host, split: false, CancellationToken.None));
    }

    [Fact]
    public async Task Refused_connection_is_a_failure()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var deadPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.False(await Trusting(deadPort).HandshakeAsync(IPAddress.Loopback, Host, split: false, CancellationToken.None));
    }

    [Fact]
    public async Task Connection_closed_by_peer_is_a_failure()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            var handshake = Trusting(((IPEndPoint)listener.LocalEndpoint).Port)
                .HandshakeAsync(IPAddress.Loopback, Host, split: false, CancellationToken.None);
            (await accept).Dispose(); // giống thiết bị lọc SNI: nhận ClientHello rồi cắt kết nối

            Assert.False(await handshake);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Cancellation_is_thrown_not_swallowed()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); // nhận kết nối vào hàng chờ nhưng không bao giờ trả lời
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var probe = Trusting(((IPEndPoint)listener.LocalEndpoint).Port);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                probe.HandshakeAsync(IPAddress.Loopback, Host, split: false, cts.Token));
        }
        finally
        {
            listener.Stop();
        }
    }
}
