using System.Net.NetworkInformation;
using LunaDns.Core.Net;
using LunaDns.Core.Platform;

namespace LunaDns.App.NetworkProfiles;

/// <summary>
/// Gộp NetworkAddressChanged và poll 60 giây thành một sự kiện mỗi lần đổi mạng thật: chờ yên 3 giây rồi đọc INetworkIdentity,
/// chỉ phát khi NetworkId khác lần trước. Changed chạy trên luồng timer; người nghe tự chuyển về luồng UI.
/// </summary>
internal sealed class NetworkWatcher(INetworkIdentity identity, TimeProvider time) : IDisposable
{
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private ITimer? _settle;
    private ITimer? _poll;
    private bool _disposed;

    public NetworkSnapshot? Current { get; private set; }

    public event Action<NetworkSnapshot?>? Changed;

    public void Start()
    {
        Current = identity.Current();
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        _poll = time.CreateTimer(_ => Poke(), null, PollInterval, PollInterval);
    }

    internal void Poke()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _settle ??= time.CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _settle.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnAddressChanged(object? sender, EventArgs e) => Poke();

    private void Check()
    {
        var now = identity.Current();
        lock (_gate)
        {
            if (_disposed || now?.Id == Current?.Id) return;
            Current = now;
        }
        Changed?.Invoke(now);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _settle?.Dispose();
        _poll?.Dispose();
    }
}
