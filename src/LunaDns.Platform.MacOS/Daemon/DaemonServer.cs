using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Platform.MacOS.Daemon;

public delegate Task<DaemonResponse> DaemonHandler(DaemonRequest request, CancellationToken cancellationToken);

/// <summary>
/// Nghe Unix socket. Socket mở quyền 666 để mọi user kết nối được, nhưng mỗi kết nối bị kiểm UID qua getpeereid và
/// <paramref name="authorize"/>; không qua thì đóng ngay. Thư mục chứa socket (/var/run) chỉ root ghi được nên không ai
/// đặt socket giả được. Mỗi kết nối một yêu cầu, xử lý tuần tự (FeatureSet vốn có khoá riêng).
/// </summary>
public sealed partial class DaemonServer : IAsyncDisposable
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private readonly Socket _listener;
    private readonly DaemonHandler _handler;
    private readonly Func<uint, bool> _authorize;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private DaemonServer(string path, Socket listener, DaemonHandler handler, Func<uint, bool> authorize, ILogger logger)
    {
        SocketPath = path;
        _listener = listener;
        _handler = handler;
        _authorize = authorize;
        _logger = logger;
        _loop = AcceptLoopAsync();
    }

    public string SocketPath { get; }

    public Task Completion => _loop;

    public static DaemonServer Start(string path, DaemonHandler handler, Func<uint, bool> authorize, ILogger<DaemonServer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(authorize);
        if (File.Exists(path)) File.Delete(path); // socket cũ của lần chạy trước (launchd khởi động lại)
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(path));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
                | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            listener.Listen(16);
        }
        catch
        {
            listener.Dispose();
            throw;
        }
        return new DaemonServer(path, listener, handler, authorize, (ILogger?)logger ?? NullLogger.Instance);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            await ServeAsync(client).ConfigureAwait(false);
        }
    }

    private async Task ServeAsync(Socket client)
    {
        using (client)
        {
            var uid = PeerCredentials.EffectiveUid(client);
            if (uid is null || !_authorize(uid.Value))
            {
                LogRejected(_logger, uid);
                await WriteAsync(client, new DaemonResponse(false, "Chỉ người đang dùng máy này (hoặc root) mới điều khiển được LunaDNS."))
                    .ConfigureAwait(false);
                return;
            }

            DaemonResponse response;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(ReadTimeout);
                var line = await ReadLineAsync(client, timeout.Token).ConfigureAwait(false);
                var request = line is null ? null : JsonSerializer.Deserialize(line, DaemonProtocol.RequestInfo);
                response = request is null
                    ? new DaemonResponse(false, "Yêu cầu rỗng hoặc quá dài.")
                    : await _handler(request, _stop.Token).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                response = new DaemonResponse(false, "Yêu cầu không đúng định dạng.");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
            await WriteAsync(client, response).ConfigureAwait(false);
        }
    }

    /// <summary>Đọc tới '\n', tối đa MaxLineBytes. Quá dài hoặc đóng sớm thì null.</summary>
    private static async Task<string?> ReadLineAsync(Socket client, CancellationToken cancellationToken)
    {
        var buffer = new byte[DaemonProtocol.MaxLineBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await client.ReceiveAsync(buffer.AsMemory(length), SocketFlags.None, cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;
            var newline = Array.IndexOf(buffer, (byte)'\n', length, read);
            length += read;
            if (newline >= 0) return Encoding.UTF8.GetString(buffer, 0, newline);
        }
        return null;
    }

    private static async Task WriteAsync(Socket client, DaemonResponse response)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, DaemonProtocol.ResponseInfo) + "\n");
        try
        {
            await client.SendAsync(bytes, SocketFlags.None).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // client đã đi
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Dispose();
        await _loop.ConfigureAwait(false);
        _stop.Dispose();
        try
        {
            File.Delete(SocketPath);
        }
        catch (IOException)
        {
            // không quan trọng: lần khởi động sau xoá
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Từ chối kết nối điều khiển từ UID {Uid}.")]
    private static partial void LogRejected(ILogger logger, uint? uid);
}
