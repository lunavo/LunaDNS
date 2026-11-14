using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>Hỏi DNS của hệ điều hành, tức là đúng thứ game và trình duyệt đang thấy (có thể đã bị đầu độc).</summary>
public sealed partial class SystemHostResolver : IHostResolver
{
    private readonly ILogger _logger;

    public SystemHostResolver(ILogger<SystemHostResolver>? logger = null) =>
        _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            // Viết đủ System.Net.Dns: trong namespace này "Dns" là LunaDns.Core.Dns.
            return await System.Net.Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            // NXDOMAIN, SERVFAIL hay DNS không trả lời: với Prober đều là "DNS hệ thống không cho IP dùng được".
            LogFailed(_logger, host, ex);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DNS hệ thống không phân giải được {Host}")]
    private static partial void LogFailed(ILogger logger, string host, Exception exception);
}
