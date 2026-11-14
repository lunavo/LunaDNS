namespace LunaDns.Platform.Windows;

public static class WindowsDns
{
    /// <summary>Xoá cache của DNS Client (tương đương ipconfig /flushdns). Cần quyền Admin; false nếu Windows từ chối.</summary>
    public static bool FlushResolverCache() => NativeMethods.DnsFlushResolverCache();
}
