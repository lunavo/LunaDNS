namespace LunaDns.App;

/// <summary>Exe có nằm trong thư mục chỉ Admin ghi được không (KH7 K7). So đường dẫn đầy đủ, có dấu phân cách cuối.</summary>
internal static class InstallLocation
{
    public static bool IsUnder(string path, string directory)
    {
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bản do Setup cài: dưới Program Files và có unins000.exe (Inno Setup) cạnh exe.</summary>
    public static bool IsInstalled(string exePath, string programFiles) =>
        IsUnder(exePath, programFiles) && File.Exists(Path.Combine(Path.GetDirectoryName(exePath)!, "unins000.exe"));
}
