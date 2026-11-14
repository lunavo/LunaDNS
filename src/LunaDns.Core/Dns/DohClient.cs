using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>DoH theo RFC 8484 (POST, application/dns-message). Thử lần lượt từng máy chủ, mỗi máy chủ có timeout riêng.</summary>
public sealed partial class DohClient : IDnsUpstream, IDisposable
{
    public const string MediaType = "application/dns-message";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    private const int MaxResponseBytes = ushort.MaxValue;

    private readonly HttpMessageInvoker _http;
    private readonly IReadOnlyList<DohServer> _servers;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;

    public DohClient(HttpMessageHandler handler, IReadOnlyList<DohServer> servers, TimeSpan? timeout = null, ILogger<DohClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(servers);
        if (servers.Count == 0) throw new ArgumentException("Cần ít nhất một máy chủ DoH.", nameof(servers));
        _http = new HttpMessageInvoker(handler, disposeHandler: true);
        _servers = servers;
        _timeout = timeout ?? DefaultTimeout;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Client thật: không qua proxy hệ thống, nối thẳng tới IP bootstrap của từng máy chủ.</summary>
    public static DohClient Create(IReadOnlyList<DohServer> servers, ILogger<DohClient>? logger = null)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = DefaultTimeout,
            ConnectCallback = (context, ct) => ConnectToBootstrapAsync(servers, context, ct),
        };
        return new DohClient(handler, servers, logger: logger);
    }

    public async Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
    {
        if (!DnsWire.TryParseQuery(query.Span, out var parsed))
            throw new ArgumentException("Truy vấn DNS sai định dạng.", nameof(query));
        var body = query.ToArray();
        body[0] = 0; // RFC 8484 §4.1: ID = 0 để cache HTTP dùng chung được
        body[1] = 0;

        var errors = new List<Exception>();
        foreach (var server in _servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                var response = await SendAsync(server, body, timeout.Token).ConfigureAwait(false);
                if (!DnsWire.IsResponseTo(response, parsed.Question))
                    throw new DohException($"{server.Name} trả response không khớp truy vấn.");
                return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or DohException or IOException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                LogServerFailed(_logger, server.Name, ex);
                errors.Add(ex);
            }
        }
        throw new DohException("Mọi máy chủ DoH đều lỗi.", new AggregateException(errors));
    }

    public void Dispose() => _http.Dispose();

    private async Task<byte[]> SendAsync(DohServer server, byte[] body, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(MediaType);
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new DohException($"{server.Name} trả HTTP {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentType?.MediaType != MediaType)
            throw new DohException($"{server.Name} trả Content-Type lạ.");
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new DohException($"{server.Name} trả response quá lớn.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxResponseBytes)
            throw new DohException($"{server.Name} trả response quá lớn.");
        return bytes;
    }

    private static async ValueTask<Stream> ConnectToBootstrapAsync(
        IReadOnlyList<DohServer> servers, SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var server = servers.FirstOrDefault(s => string.Equals(s.Endpoint.Host, host, StringComparison.OrdinalIgnoreCase))
                     ?? throw new DohException($"Host DoH lạ: {host}.");
        SocketException? last = null;
        foreach (var address in server.BootstrapAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            var connected = false;
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                var stream = new NetworkStream(socket, ownsSocket: true);
                connected = true;
                return stream;
            }
            catch (SocketException ex)
            {
                last = ex;
            }
            finally
            {
                // Lỗi socket, timeout hay huỷ: đóng ngay, không chờ GC.
                if (!connected) socket.Dispose();
            }
        }
        var message = $"Không nối được tới {server.Name}.";
        throw last is null ? new DohException(message) : new DohException(message, last);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DoH {Server} lỗi, chuyển sang máy chủ kế")]
    private static partial void LogServerFailed(ILogger logger, string server, Exception exception);
}
