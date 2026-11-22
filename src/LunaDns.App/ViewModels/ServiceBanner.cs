using LunaDns.App.Features;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Banner dịch vụ nền macOS (mockup 1.4): chưa cài, không trả lời, bản cũ, hoặc tài khoản khác đang ngồi trước máy.
/// ButtonText null thì không có nút: với tài khoản khác (B4/U3), cài lại sẽ tắt định tuyến của người kia.
/// </summary>
internal sealed record ServiceBanner(string Text, string? ButtonText)
{
    /// <summary>needsUpdate: daemon bản cũ hơn app hoặc không biết một lệnh mới. null khi không có gì cần báo.</summary>
    public static ServiceBanner? From(ControllerAvailability availability, bool needsUpdate) => availability switch
    {
        ControllerAvailability.DaemonOtherUser => new(Strings.ServiceOtherUser, null),
        ControllerAvailability.DaemonNotInstalled => new(Strings.ServiceNotInstalled, needsUpdate ? Strings.ReinstallServiceButton : Strings.InstallServiceButton),
        ControllerAvailability.DaemonNotResponding => new(Strings.ServiceNotResponding, Strings.ReinstallServiceButton),
        _ when needsUpdate => new(Strings.DaemonNeedsUpdate, Strings.ReinstallServiceButton),
        _ => null,
    };
}
