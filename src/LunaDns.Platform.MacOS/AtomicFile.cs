namespace LunaDns.Platform.MacOS;

/// <summary>Ghi file kiểu tạm-rồi-đổi-tên trong cùng thư mục: không bao giờ để lại file ghi dở khi mất điện.</summary>
internal static class AtomicFile
{
    public const UnixFileMode WorldReadable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static void Write(string path, ReadOnlySpan<byte> content, UnixFileMode mode)
    {
        var tmp = path + ".lunadns.tmp";
        try
        {
            using (var stream = new FileStream(tmp, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = mode,
            }))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.SetUnixFileMode(tmp, mode); // umask có thể đã bớt quyền đọc
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
