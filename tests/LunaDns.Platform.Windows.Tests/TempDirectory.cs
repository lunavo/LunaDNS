namespace LunaDns.Platform.Windows.Tests;

/// <summary>Thư mục tạm riêng cho mỗi test; xoá cả file ReadOnly khi Dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("lunadns-").FullName;

    public string PathOf(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Path)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Path, recursive: true);
    }
}
