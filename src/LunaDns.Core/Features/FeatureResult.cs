namespace LunaDns.Core.Features;

/// <summary>Kết quả một lệnh bật/tắt. ChangesLeftBehind = còn mục Journal chưa hoàn tác được (sẽ thử lại khi thoát hoặc lần mở sau).</summary>
public sealed record FeatureResult(Feature Feature, FeatureOutcome Outcome, FeatureError Error = FeatureError.None, bool ChangesLeftBehind = false);
