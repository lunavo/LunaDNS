namespace LunaDns.Platform.Windows;

public static class WinInet
{
    /// <summary>Báo cho WinINet đọc lại cấu hình proxy (INTERNET_OPTION_SETTINGS_CHANGED rồi REFRESH). False nếu Windows từ chối.</summary>
    public static bool NotifySettingsChanged()
    {
        var changed = NativeMethods.InternetSetOption(IntPtr.Zero, NativeMethods.InternetOptionSettingsChanged, IntPtr.Zero, 0);
        var refreshed = NativeMethods.InternetSetOption(IntPtr.Zero, NativeMethods.InternetOptionRefresh, IntPtr.Zero, 0);
        return changed && refreshed;
    }
}
