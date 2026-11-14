namespace LunaDns.App;

/// <summary>Khác biệt nền tảng mà giao diện cần biết. Phần cài đặt nằm trong Windows/ và MacOS/.</summary>
internal static partial class PlatformInfo
{
    /// <summary>Nút "Gỡ khỏi máy": chỉ bản Windows do Setup cài. macOS gỡ bằng <c>sudo lunadns uninstall</c>.</summary>
    public static partial bool CanUninstall();

    /// <summary>Mục Nâng cao (kiểu tách gói, độ trễ, DoH). macOS ẩn: dịch vụ nền chưa nhận settings từ giao diện.</summary>
    public static partial bool HasAdvancedSettings();

    public static partial IPlatformDiagnostics CreateDiagnostics(AppServices services);
}
