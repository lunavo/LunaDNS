namespace LunaDns.App.Features;

/// <summary>Kết quả nút "Cài dịch vụ nền" (macOS). Cancelled = người dùng bấm Huỷ ở hộp thoại mật khẩu.</summary>
internal sealed record ServiceInstallResult(bool Success, bool Cancelled, string Output);
