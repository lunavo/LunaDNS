namespace LunaDns.Core.Features;

/// <summary>Dịch vụ nền của một tính năng (stub DNS, proxy, listener PUBG). Dispose = dừng.</summary>
public interface IRunningService : IAsyncDisposable
{
    /// <summary>Xong khi dịch vụ dừng; faulted nếu chết vì lỗi. Watchdog chờ task này.</summary>
    Task Completion { get; }
}
