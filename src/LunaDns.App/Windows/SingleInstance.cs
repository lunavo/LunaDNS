namespace LunaDns.App;

/// <summary>
/// Mutex Global\LunaDNS (spec 4.5) cùng sự kiện "&lt;tên&gt;.Show": lần mở thứ hai chỉ bật sự kiện để cửa sổ đang chạy hiện lên, rồi thoát.
/// Dùng cờ createdNew chứ không dùng WaitOne(0), vì mutex .NET cho cùng một luồng lấy lại nhiều lần.
/// Có thread affinity: luồng nào gọi TryAcquire thì luồng đó phải Dispose.
/// </summary>
internal sealed class SingleInstance : IAppInstance
{
    public const string DefaultName = @"Global\LunaDNS";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    public static SingleInstance? TryAcquire(string name = DefaultName)
    {
        Mutex mutex;
        bool created;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name, out created);
        }
        catch (UnauthorizedAccessException)
        {
            // Mutex đã có, do phiên khác tạo với DACL chặt hơn: coi như app đang chạy.
            return null;
        }
        if (!created)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName(name)));
    }

    /// <summary>Báo bản đang chạy hiện cửa sổ. Trả false nếu không có bản nào đang chạy.</summary>
    public static bool SignalShow(string name = DefaultName)
    {
        if (!EventWaitHandle.TryOpenExisting(ShowEventName(name), out var handle)) return false;
        using (handle) return handle.Set();
    }

    /// <summary>onShow chạy trên luồng thread pool; bên gọi tự chuyển về luồng UI.</summary>
    public void ListenForShow(Action onShow)
    {
        ArgumentNullException.ThrowIfNull(onShow);
        _wait?.Unregister(null);
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _show.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static string ShowEventName(string name) => name + ".Show";
}
