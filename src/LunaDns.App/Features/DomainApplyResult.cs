using LunaDns.Core.Features;

namespace LunaDns.App.Features;

/// <summary>Kết quả của nút Lưu/công tắc nhóm (spec KH8 5.2). Message là câu tiếng Việt khi Success = false; Error là mã lỗi của FeatureSet (nếu có) để ViewModel không phải so khớp câu chữ.</summary>
internal sealed record DomainApplyResult(bool Success, string? Message, FeatureError? Error = null);
