using LunaDns.Core.Platform;

namespace LunaDns.Platform.MacOS.Hosts;

/// <summary>
/// File hosts trên macOS. <c>/etc</c> là symlink tới <c>/private/etc</c>: ghi vào đích thật để không thay symlink bằng file thường.
/// Giữ nguyên quyền của file cũ (thường 644 root:wheel; chạy bằng root nên owner không đổi).
/// </summary>
public sealed class UnixHostsFileStore(string path = UnixHostsFileStore.DefaultPath)
{
    public const string DefaultPath = "/etc/hosts";

    public string FilePath { get; } = path;

    public byte[] Read() => Guard(() => File.ReadAllBytes(RealPath()));

    public void Write(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Guard(() =>
        {
            var real = RealPath();
            var mode = File.Exists(real) ? File.GetUnixFileMode(real) : AtomicFile.WorldReadable;
            AtomicFile.Write(real, content, mode);
            return true;
        });
    }

    private string RealPath()
    {
        var full = Path.GetFullPath(FilePath);
        var dir = Path.GetDirectoryName(full)!;
        var realDir = new DirectoryInfo(dir).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? dir;
        var file = Path.Combine(realDir, Path.GetFileName(full));
        return new FileInfo(file).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file;
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
                "Không có quyền sửa /etc/hosts. LunaDNS cần chạy bằng quyền quản trị (sudo).", ex);
        }
        catch (IOException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.Other, "Không đọc/ghi được /etc/hosts. Hãy thử lại.", ex);
        }
    }
}
