using LunaDns.Platform.Windows.Diagnostics;

namespace LunaDns.App;

internal static partial class PlatformInfo
{
    public static partial bool CanUninstall() =>
        InstallLocation.IsInstalled(Environment.ProcessPath ?? string.Empty, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    public static partial bool HasAdvancedSettings() => true;

    public static partial IPlatformDiagnostics CreateDiagnostics(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new WindowsReportDiagnostics(WindowsDiagnostics.CreateDefault(), services.Paths.JournalFile);
    }
}
