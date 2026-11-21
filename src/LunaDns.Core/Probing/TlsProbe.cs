using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Probing;

/// <summary>
/// Bắt tay TLS thật bằng SslStream trên socket trần (không bao giờ qua proxy hệ thống). Chứng chỉ được kiểm tra
/// theo tên host, nên trang chặn hay máy chủ giả không thể qua. Không gửi dữ liệu ứng dụng nào.
/// </summary>
public sealed partial class TlsProbe : ITlsProbe
{
    private readonly int _port;
    private readonly HelloSplit _split;
    private readonly TimeSpan _delay;
    private readonly RemoteCertificateValidationCallback? _validation;
    private readonly ILogger _logger;

    public TlsProbe(HelloSplit? split = null, ILogger<TlsProbe>? logger = null)
        : this(443, split ?? HelloSplit.Default, null, logger)
    {
    }

    /// <summary>D3: thử đúng tổ hợp cắt gói + độ trễ mà proxy sẽ dùng.</summary>
    public TlsProbe(HelloSplit split, TimeSpan delay, ILogger<TlsProbe>? logger = null)
        : this(443, split, null, logger, delay)
    {
    }

    /// <summary>Cho test: cổng và cách kiểm tra chứng chỉ tuỳ chọn.</summary>
    internal TlsProbe(int port, HelloSplit split, RemoteCertificateValidationCallback? validation, ILogger<TlsProbe>? logger,
        TimeSpan delay = default)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, SplitWriter.MaxDelay);
        _port = port;
        _split = split;
        _delay = delay;
        _validation = validation;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, _port), cancellationToken).ConfigureAwait(false);
            Stream transport = new NetworkStream(socket, ownsSocket: false);
            if (split) transport = new SplittingStream(transport, _split, _delay);
            using var ssl = new SslStream(transport, leaveInnerStreamOpen: false);
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                RemoteCertificateValidationCallback = _validation,
            };
            await ssl.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException)
        {
            LogFailed(_logger, host, split, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bắt tay TLS tới {Host} (cắt gói: {Split}) không thành công")]
    private static partial void LogFailed(ILogger logger, string host, bool split, Exception exception);
}
