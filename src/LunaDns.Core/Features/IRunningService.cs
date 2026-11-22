using LunaDns.Core.Dns;

namespace LunaDns.Core.Features;

/// <summary>Dịch vụ nền của một tính năng (stub DNS, proxy, listener PUBG). Dispose = dừng.</summary>
public interface IRunningService : IAsyncDisposable
{
    /// <summary>Xong khi dịch vụ dừng; faulted nếu chết vì lỗi. Watchdog chờ task này.</summary>
    Task Completion { get; }

    /// <summary>Máy DoH trả lời gần nhất của dịch vụ này (F4); null khi chưa biết hoặc dịch vụ không dùng DoH.</summary>
    DohActivity? Doh => null;
}
