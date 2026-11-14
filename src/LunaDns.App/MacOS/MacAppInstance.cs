using System.Net.Sockets;

namespace LunaDns.App.MacOS;

/// <summary>
/// Thay mutex của Windows: khoá flock trên <c>gui.lock</c> (FileShare.None) trong thư mục dữ liệu của user, cùng Unix socket
/// <c>gui.sock</c> để lần mở thứ hai bảo bản đang chạy hiện cửa sổ. Khoá tự nhả khi tiến trình chết, socket cũ bị xoá lúc lấy khoá.
/// </summary>
internal sealed class MacAppInstance : IAppInstance
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly FileStream _lock;
    private readonly Socket _listener;
    private readonly string _socketPath;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    private MacAppInstance(FileStream held, Socket listener, string socketPath)
    {
        _lock = held;
        _listener = listener;
        _socketPath = socketPath;
    }

    public static string LockPath(string directory) => Path.Combine(directory, "gui.lock");

    public static string SocketPath(string directory) => Path.Combine(directory, "gui.sock");

    /// <summary>null nếu đã có bản khác giữ khoá.</summary>
    public static MacAppInstance? TryAcquire(string directory)
    {
        Directory.CreateDirectory(directory);
        FileStream held;
        try
        {
            held = new FileStream(LockPath(directory), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }

        var socketPath = SocketPath(directory);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            if (File.Exists(socketPath)) File.Delete(socketPath);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.SetUnixFileMode(socketPath, OwnerOnly);
            listener.Listen(4);
        }
        catch
        {
            listener.Dispose();
            held.Dispose();
            throw;
        }
        return new MacAppInstance(held, listener, socketPath);
    }

    /// <summary>Báo bản đang chạy hiện cửa sổ. false nếu không có bản nào nghe.</summary>
    public static bool SignalShow(string directory)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath(directory)));
            socket.Send("show\n"u8);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public void ListenForShow(Action onShow)
    {
        ArgumentNullException.ThrowIfNull(onShow);
        _loop ??= AcceptLoopAsync(onShow);
    }

    private async Task AcceptLoopAsync(Action onShow)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            onShow();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // vòng nghe đã dừng
        }
        _stop.Dispose();
        try
        {
            File.Delete(_socketPath);
        }
        catch (IOException)
        {
            // lần lấy khoá sau xoá
        }
        _lock.Dispose();
    }
}
