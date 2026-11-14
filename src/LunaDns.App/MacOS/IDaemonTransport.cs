using LunaDns.Platform.MacOS.Daemon;

namespace LunaDns.App.MacOS;

/// <summary>Một yêu cầu tới daemon. Tách khỏi DaemonClient để test DaemonFeatureController không cần socket thật.</summary>
internal interface IDaemonTransport
{
    /// <exception cref="DaemonUnavailableException">Daemon chưa chạy hoặc đóng kết nối.</exception>
    Task<DaemonResponse> SendAsync(DaemonRequest request, CancellationToken cancellationToken);
}

internal sealed class SocketDaemonTransport(string socketPath = DaemonProtocol.SocketPath) : IDaemonTransport
{
    private readonly DaemonClient _client = new(socketPath);

    public Task<DaemonResponse> SendAsync(DaemonRequest request, CancellationToken cancellationToken) =>
        _client.SendAsync(request, cancellationToken);
}
