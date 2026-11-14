using System.Net;

namespace LunaDns.Core.Probing;

/// <summary>
/// Một lần bắt tay TLS thử. True khi bắt tay tới <paramref name="address"/> với SNI <paramref name="host"/> thành công
/// và chứng chỉ hợp lệ cho host đó. Lỗi mạng hay chứng chỉ thì false; chỉ ném OperationCanceledException khi token bị huỷ.
/// </summary>
public interface ITlsProbe
{
    Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken);
}
