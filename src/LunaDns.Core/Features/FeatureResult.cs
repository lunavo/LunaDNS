namespace LunaDns.Core.Features;

/// <summary>Kết quả một lệnh bật/tắt. ChangesLeftBehind = còn mục Journal chưa hoàn tác được (sẽ thử lại khi thoát hoặc lần mở sau).</summary>
/// <remarks>
/// Detail: lý do cụ thể đã viết sẵn cho người dùng (câu của <see cref="Platform.ConfiguratorException"/>, hoặc Message của
/// dịch vụ nền trên macOS); null khi không có. Giao diện chỉ dùng nó cho mã <see cref="FeatureError.Other"/>.
/// </remarks>
public sealed record FeatureResult(Feature Feature, FeatureOutcome Outcome, FeatureError Error = FeatureError.None, bool ChangesLeftBehind = false,
    string? Detail = null);
