using LunaDns.Core.Platform;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Hosts;

/// <summary>
/// Đọc/ghi file hosts thật. Ghi ra file tạm cùng thư mục rồi File.Replace, nên mất điện giữa chừng không để lại file dở.
/// ReplaceFile giữ DACL của file cũ; thuộc tính (ReadOnly…) được gỡ tạm rồi đặt lại (spec 9).
/// </summary>
public sealed class HostsFileStore
{
    public HostsFileStore(string path) => FilePath = path;

    public string FilePath { get; }

    /// <summary>Thư mục hosts theo DataBasePath của Tcpip (mặc định %SystemRoot%\System32\drivers\etc).</summary>
    public static string DefaultPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
        var directory = key?.GetValue("DataBasePath") as string;
        if (string.IsNullOrWhiteSpace(directory)) directory = Path.Combine(Environment.SystemDirectory, "drivers", "etc");
        return Path.Combine(directory, "hosts");
    }

    public byte[] Read() => Guard(() => File.ReadAllBytes(FilePath));

    public void Write(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Guard(() =>
        {
            WriteCore(content);
            return true;
        });
    }

    private void WriteCore(byte[] content)
    {
        var tmp = FilePath + ".lunadns.tmp";
        try
        {
            var attributes = File.GetAttributes(FilePath);
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(FilePath, attributes & ~FileAttributes.ReadOnly);
            try
            {
                File.Replace(tmp, FilePath, destinationBackupFileName: null);
            }
            finally
            {
                File.SetAttributes(FilePath, attributes);
            }
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.AccessDenied,
                "Không có quyền sửa file hosts. LunaDNS cần chạy bằng quyền Admin, hoặc phần mềm diệt virus đang khoá file này.", ex);
        }
        catch (IOException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.Other,
                "Không đọc/ghi được file hosts (có thể đang bị chương trình khác giữ). Hãy thử lại.", ex);
        }
    }
}
