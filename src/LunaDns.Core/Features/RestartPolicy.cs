namespace LunaDns.Core.Features;

/// <summary>
/// Giới hạn của watchdog: tối đa <c>maxRestarts</c> lần khởi động lại trong một cửa sổ trượt (spec 6: 3 lần trong 1 phút).
/// Không an toàn đa luồng; FeatureSet chỉ gọi khi đang giữ cổng tuần tự của nó.
/// </summary>
public sealed class RestartPolicy
{
    public const int DefaultMaxRestarts = 3;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time;
    private readonly int _maxRestarts;
    private readonly TimeSpan _window;
    private readonly Queue<DateTimeOffset> _restarts = new();

    public RestartPolicy(TimeProvider? time = null, int maxRestarts = DefaultMaxRestarts, TimeSpan? window = null)
    {
        _time = time ?? TimeProvider.System;
        _maxRestarts = maxRestarts;
        _window = window ?? DefaultWindow;
    }

    /// <summary>Ghi một lần khởi động lại nếu còn trong giới hạn. false = đã đủ số lần trong cửa sổ, phải tắt tính năng.</summary>
    public bool TryRecordRestart()
    {
        var now = _time.GetUtcNow();
        while (_restarts.Count > 0 && now - _restarts.Peek() >= _window) _restarts.Dequeue();
        if (_restarts.Count >= _maxRestarts) return false;
        _restarts.Enqueue(now);
        return true;
    }
}
