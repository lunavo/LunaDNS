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
    /// <summary>Đã cài nhưng socket không trả lời (daemon chết, đang khởi động lại) hoặc trả lời lỗi không kèm trạng thái.</summary>
    DaemonNotResponding,
    /// <summary>
    /// Daemon chạy nhưng từ chối vì tài khoản khác đang ngồi trước máy (Fast User Switching, B4). Không đề nghị cài lại:
    /// cài lại sẽ tắt định tuyến của người kia.
    /// </summary>
    DaemonOtherUser,
}

/// <summary>Vì sao app thoát (F1): người dùng thoát thì xoá wanted, hết phiên (đăng xuất/tắt máy) thì giữ.</summary>
internal enum ShutdownReason
{
    /// <summary>Thoát &amp; khôi phục trên khay/thanh menu, Cmd+Q, Quit trên Dock.</summary>
    UserQuit,
    /// <summary>Đăng xuất, khởi động lại, tắt máy.</summary>
    SessionEnd,
}

/// <summary>Giá trị đặc biệt của <see cref="IFeatureController.ShutdownAsync"/>.</summary>
internal static class ShutdownOutcome
{
    /// <summary>Không biết đã hoàn tác chưa: dịch vụ nền không trả lời (B8). Chỉ bản daemon trả.</summary>
    public const int Unknown = -1;
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

    /// <summary>
    /// F1: công tắc Tự bật lại, wanted, đang tự bật dở, lỗi lần tự bật cuối. null nếu không biết (daemon cũ hoặc không trả lời).
    /// </summary>
    ResumeState? Resume { get; }

    /// <summary>Bản Local: tự bật lại wanted lúc mở app. Bản daemon không làm gì (daemon tự bật lúc khởi động).</summary>
    Task ResumeAsync();

    /// <summary>Lưu công tắc Tự bật lại. null nếu xong, ngược lại là câu báo lỗi cho người dùng.</summary>
    Task<string?> SetAutoResumeAsync(bool enabled);

    /// <summary>
    /// "Thoát &amp; khôi phục", Cmd+Q, đăng xuất. Trả số thay đổi chưa hoàn tác được (0 = sạch).
    /// <see cref="ShutdownReason.UserQuit"/> còn xoá wanted, để lần khởi động sau không tự bật.
    /// Trả số thay đổi còn sót; <see cref="ShutdownOutcome.Unknown"/> khi không biết (dịch vụ nền không trả lời).
    /// </summary>
    Task<int> ShutdownAsync(ShutdownReason reason);
}
