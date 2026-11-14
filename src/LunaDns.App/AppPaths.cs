namespace LunaDns.App;

/// <summary>
/// Windows: journal, settings và log nằm trong %ProgramData%\LunaDNS (spec 4.5). Task dọn dẹp chạy bằng SYSTEM (KH7) đọc cùng chỗ này.
/// Mỗi lần chạy, Program đặt ACL chỉ SYSTEM/Admin ghi được (KH7 K9).
/// macOS: giao diện chạy quyền user nên chỉ giữ settings, log và khoá một-bản-chạy ở ~/Library/Application Support/LunaDNS;
/// journal thuộc dịch vụ nền (/Library/Application Support/LunaDNS, root 700). Báo cáo ghi ra Desktop ở cả hai.
/// </summary>
internal sealed record AppPaths(string DataDirectory, string LogDirectory, string ReportDirectory)
{
    public string JournalFile => Path.Combine(DataDirectory, "journal.json");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static AppPaths Default()
    {
#if MACOS
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = Path.Combine(home, "Library", "Application Support", "LunaDNS");
        return new AppPaths(data, Path.Combine(data, "logs"), Path.Combine(home, "Desktop"));
#else
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LunaDNS");
        return new AppPaths(data, Path.Combine(data, "logs"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
#endif
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
