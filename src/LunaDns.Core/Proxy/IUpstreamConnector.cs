namespace LunaDns.Core.Proxy;

/// <summary>Mở kết nối tới máy chủ thật cho một yêu cầu CONNECT. Không nối được thì ném; người gọi đổi thành 502.</summary>
public interface IUpstreamConnector
{
    Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken);
}
