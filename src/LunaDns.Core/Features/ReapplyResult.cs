namespace LunaDns.Core.Features;

/// <param name="Success">Danh sách mới đang được áp dụng.</param>
/// <param name="PreviousRestored">Khi Success = false: các tính năng đã chạy lại được với danh sách cũ.</param>
public sealed record ReapplyResult(bool Success, bool PreviousRestored, FeatureError Error = FeatureError.None);
