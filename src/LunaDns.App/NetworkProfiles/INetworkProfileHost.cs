using LunaDns.App.Features;
using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.App.NetworkProfiles;

/// <summary>Phần ứng dụng mà <see cref="NetworkProfileCoordinator"/> cần: cài đặt, tính năng đang chạy, Kiểm tra và áp cấu hình.</summary>
internal interface INetworkProfileHost
{
    /// <summary>Công tắc "Nhớ cấu hình theo mạng" đang bật.</summary>
    bool RememberPerNetwork { get; }

    /// <summary>Trạng thái các tính năng đang chạy (định tuyến DNS, proxy SNI).</summary>
    FeatureStatus Status { get; }

    /// <summary>Đang có việc khác chạy: Kiểm tra, Tìm cấu hình, Đo, Lưu Tự nhập, bật/tắt tính năng hoặc áp tên miền.</summary>
    bool IsBusy { get; }

    /// <summary>Tổ hợp DoH và cách chia gói đang chạy, hoặc null nếu chưa đọc được.</summary>
    Task<TuningState?> GetTuningAsync();

    /// <summary>Áp tổ hợp DoH và cách chia gói vào tính năng đang chạy.</summary>
    Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate);

    /// <summary>Kiểm tra nhóm Steam bằng cài đặt hiện tại.</summary>
    Task<IReadOnlyList<ProbeResult>> ProbeSteamAsync(CancellationToken cancellationToken);

    /// <summary>Chạy như <c>MainViewModel.RunBusyAsync(Features, …)</c> để công tắc xám trong lúc áp.</summary>
    Task RunBusyAsync(Func<Task> work);

    /// <summary>Hiện lỗi cho người dùng.</summary>
    void ShowError(string message);
}
