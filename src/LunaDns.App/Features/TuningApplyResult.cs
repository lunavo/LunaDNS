using LunaDns.Core.Features;

namespace LunaDns.App.Features;

/// <summary>Kết quả áp cấu hình D3. Message là câu cho người dùng khi Success = false.</summary>
internal sealed record TuningApplyResult(bool Success, string? Message, FeatureError? Error = null);
