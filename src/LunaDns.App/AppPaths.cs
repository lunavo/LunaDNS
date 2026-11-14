namespace LunaDns.App;

/// <summary>
/// Journal, settings và log nằm trong %ProgramData%\LunaDNS (spec 4.5). Task dọn dẹp chạy bằng SYSTEM (KH7) đọc cùng chỗ này.
/// Mỗi lần chạy, Program đặt ACL chỉ SYSTEM/Admin ghi được (KH7 K9). Báo cáo ghi ra Desktop.
/// </summary>
internal sealed record AppPaths(string DataDirectory, string LogDirectory, string ReportDirectory)
{
    public string JournalFile => Path.Combine(DataDirectory, "journal.json");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static AppPaths Default()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LunaDNS");
        return new AppPaths(data, Path.Combine(data, "logs"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
