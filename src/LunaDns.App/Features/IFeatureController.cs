using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Probing;

namespace LunaDns.App.Features;

/// <summary>Ai đang giữ các tính năng: chính giao diện (Windows) hay dịch vụ nền (macOS), và dịch vụ nền có trả lời không.</summary>
internal enum ControllerAvailability
{
    /// <summary>Giao diện tự chạy FeatureSet (Windows, quyền Admin sau UAC).</summary>
    Local,
    /// <summary>Chưa hỏi được daemon lần nào (vừa mở app): công tắc xám, chưa hiện banner.</summary>
    DaemonConnecting,
    DaemonRunning,
    /// <summary>Chưa có plist của launchd: giao diện đề nghị cài.</summary>
    DaemonNotInstalled,
    /// <summary>Đã cài nhưng socket không trả lời (daemon chết, đang khởi động lại, hoặc bị từ chối).</summary>
    DaemonNotResponding,
}

/// <summary>
/// Lớp giữa MainViewModel và nơi thật sự bật/tắt tính năng. Windows: <see cref="LocalFeatureController"/> bọc FeatureSet.
/// macOS: DaemonFeatureController gửi lệnh qua Unix socket tới daemon chạy bằng root.
/// <see cref="Status"/> là ảnh chụp gần nhất, đọc được trên luồng UI mà không chặn; <see cref="RefreshAsync"/> cập nhật nó.
/// </summary>
internal interface IFeatureController : IDomainListSink, IAsyncDisposable
{
    /// <summary>Watchdog đã tắt một tính năng. Bản daemon không phát (không phân biệt được với "lunadns off" từ Terminal).</summary>
    event EventHandler<FeatureStoppedEventArgs>? FeatureStopped;

    ControllerAvailability Availability { get; }

    FeatureStatus Status { get; }

    /// <summary>Kết quả hoàn tác journal còn sót lúc mở app. Bản daemon luôn rỗng: daemon tự hoàn tác khi khởi động.</summary>
    RollbackResult StartupRollback { get; }

    /// <summary>Số mục journal còn lại (cho báo cáo). null nếu không biết.</summary>
    int? JournalEntries { get; }

    /// <summary>macOS: dịch vụ nền đang chạy là bản cũ hơn giao diện, cần cài lại. Bản Windows luôn false.</summary>
    bool ServiceOutdated { get; }

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<FeatureResult> EnableAsync(Feature feature, bool allowProxyOverride = false);

    Task<FeatureResult> DisableAsync(Feature feature);

    Task<DisableAllResult> DisableAllAsync();

    /// <summary>Lựa chọn tên miền nơi giữ thật đang dùng. null nếu không hỏi được (dịch vụ nền không trả lời).</summary>
    Task<DomainSelection?> GetDomainsAsync();

    /// <summary>Áp danh sách mới cho DNS/SNI đang chạy. Bản daemon tự lưu; bản Local để AppServices lưu settings.json.</summary>
    Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection);

    /// <summary>Máy DoH và tổ hợp cắt gói nơi giữ thật đang dùng (D3). null nếu không hỏi được (daemon cũ hoặc không trả lời).</summary>
    Task<TuningState?> GetTuningAsync();

    /// <summary>
    /// Áp cấu hình D3 cho tính năng đang chạy. Bản daemon tự kiểm, áp, lưu và tự quay lại khi lỗi (bỏ qua restore).
    /// Bản Local chạy lại FeatureSet, vì AppServices đã lưu settings mới trước khi gọi; lỗi thì gọi restore (đặt lại settings cũ)
    /// rồi bật lại đúng các tính năng đã chạy.
    /// </summary>
    Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate, Action? restore = null);

    /// <summary>
    /// Công tắc "Tự cập nhật danh sách" (D4). Bản daemon tự lưu vào settings.json của nó; bản Local để AppServices lưu sau khi áp.
    /// </summary>
    Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled);

    /// <summary>"Thoát &amp; khôi phục" và đăng xuất. Trả số thay đổi chưa hoàn tác được (0 = sạch).</summary>
    Task<int> ShutdownAsync();
}
