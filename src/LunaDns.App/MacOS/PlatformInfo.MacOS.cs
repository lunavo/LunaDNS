using LunaDns.App.MacOS;

namespace LunaDns.App;

internal static partial class PlatformInfo
{
    public static partial bool CanUninstall() => false;

    public static partial bool HasAdvancedSettings() => false;

    public static partial IPlatformDiagnostics CreateDiagnostics(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Program.CreateMacDiagnostics(services.Features as DaemonFeatureController);
    }
}
