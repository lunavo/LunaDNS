using LunaDns.Core.Features;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Nhớ lần watchdog dừng tính năng gần nhất để gửi trong status (F5). Record được gọi từ luồng watchdog, Latest từ luồng
/// xử lý socket, nên ghi/đọc qua khoá.
/// </summary>
internal sealed class WatchdogStops
{
    private readonly object _lock = new();
    private DaemonStopped? _latest;

    public DaemonStopped? Latest
    {
        get
        {
            lock (_lock) return _latest;
        }
    }

    public void Record(FeatureStoppedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        lock (_lock)
        {
            _latest = new DaemonStopped((_latest?.Seq ?? 0) + 1, FeatureKeys.For(e.Feature), e.ChangesLeftBehind);
        }
    }
}
