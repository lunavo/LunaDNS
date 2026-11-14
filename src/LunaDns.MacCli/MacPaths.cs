namespace LunaDns.MacCli;

/// <summary>
/// Thư mục dữ liệu macOS, tương đương %ProgramData%\LunaDNS (spec 4.6): root sở hữu, quyền 700.
/// Thư mục có sẵn mà nhóm/người khác ghi được thì không tin: đổi tên thành <c>LunaDNS.untrusted-&lt;ts&gt;</c> rồi tạo mới.
/// </summary>
internal sealed class MacPaths(string root = MacPaths.DefaultRoot)
{
    public const string DefaultRoot = "/Library/Application Support/LunaDNS";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OthersWrite = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    public string Root { get; } = root;

    public string JournalFile => Path.Combine(Root, "journal.json");

    public string LockFile => Path.Combine(Root, "lunadns.lock");

    public string LogDirectory => Path.Combine(Root, "logs");

    /// <summary>Trả đường dẫn thư mục bị cách ly, nếu có.</summary>
    public string? EnsureCreated(DateTimeOffset now)
    {
        string? quarantined = null;
        if (Directory.Exists(Root) && (File.GetUnixFileMode(Root) & OthersWrite) != 0)
        {
            quarantined = $"{Root}.untrusted-{now:yyyyMMddHHmmss}";
            Directory.Move(Root, quarantined);
        }
        Directory.CreateDirectory(Root, OwnerOnly);
        File.SetUnixFileMode(Root, OwnerOnly);
        Directory.CreateDirectory(LogDirectory, OwnerOnly);
        return quarantined;
    }
}
