namespace LunaDns.Core.Settings;

/// <summary>
/// settings.json của daemon macOS, dùng chung giữa Engine (DoH, tuning lúc khởi động dịch vụ) và DaemonCommands
/// (set-domains, set-tuning), để lệnh này không ghi đè trường của lệnh kia. Nạp một lần lúc tạo.
/// </summary>
public sealed class SettingsHolder
{
    private readonly JsonFileSettingsStore _store;
    private volatile AppSettings _current;

    public SettingsHolder(JsonFileSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _current = store.Load();
        DroppedDomains = store.DroppedDomains;
    }

    /// <summary>Đặt tạm (chưa ghi file) để dịch vụ khởi động lại đọc giá trị mới; Save mới ghi.</summary>
    public AppSettings Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Số dòng tên miền bị bỏ lúc nạp (để log).</summary>
    public int DroppedDomains { get; }

    /// <summary>Ghi file rồi mới đổi Current; ghi lỗi thì ném và Current giữ nguyên.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _store.Save(settings);
        _current = settings;
    }
}
