using LunaDns.App.MacOS;
using LunaDns.Core.Platform;
using LunaDns.Platform.MacOS.Net;
using LunaDns.Platform.MacOS.Processes;

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

    public static partial async Task<IReadOnlyList<string>?> ServiceLogAsync(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Features is DaemonFeatureController daemon ? await daemon.GetServiceLogAsync().ConfigureAwait(false) : null;
    }

    public static partial INetworkIdentity CreateNetworkIdentity() => new MacNetworkIdentity(new ProcessRunner());
}
