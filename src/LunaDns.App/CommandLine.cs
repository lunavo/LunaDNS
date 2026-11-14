namespace LunaDns.App;

internal enum CommandKind
{
    Gui,
    Cleanup,
    Report,
    InstallSystem,
    Uninstall,
    Invalid,
}

/// <summary>LunaDNS.exe | LunaDNS.exe --cleanup | LunaDNS.exe --report (spec 4.5, 4.9) | --install-system | --uninstall (KH7, Setup gọi). Không phân biệt hoa thường.</summary>
internal static class CommandLine
{
    public const string CleanupSwitch = "--cleanup";
    public const string ReportSwitch = "--report";
    public const string InstallSystemSwitch = "--install-system";
    public const string UninstallSwitch = "--uninstall";

    public static CommandKind Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args switch
        {
            [] => CommandKind.Gui,
            [var only] when Is(only, CleanupSwitch) => CommandKind.Cleanup,
            [var only] when Is(only, ReportSwitch) => CommandKind.Report,
            [var only] when Is(only, InstallSystemSwitch) => CommandKind.InstallSystem,
            [var only] when Is(only, UninstallSwitch) => CommandKind.Uninstall,
            _ => CommandKind.Invalid,
        };
    }

    private static bool Is(string arg, string expected) => string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase);
}
