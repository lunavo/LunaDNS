namespace LunaDns.App.Tests;

/// <summary>U2: lệnh mở thư mục log và hiện file báo cáo trong Finder/Explorer.</summary>
public sealed class SystemShellTests
{
    [Fact]
    public void Mac_opens_folder_and_reveals_file_with_open()
    {
        var folder = SystemShell.FolderCommand("/Users/a/Library/Application Support/LunaDNS/logs", mac: true);
        Assert.Equal("/usr/bin/open", folder.FileName);
        Assert.Equal(["/Users/a/Library/Application Support/LunaDNS/logs"], folder.ArgumentList);

        var reveal = SystemShell.RevealCommand("/Users/a/Desktop/LunaDNS-report-1.txt", mac: true);
        Assert.Equal("/usr/bin/open", reveal.FileName);
        Assert.Equal(["-R", "/Users/a/Desktop/LunaDNS-report-1.txt"], reveal.ArgumentList);
    }

    [Fact]
    public void Windows_uses_explorer_and_select()
    {
        var folder = SystemShell.FolderCommand(@"C:\ProgramData\LunaDNS\logs", mac: false);
        Assert.Equal("explorer.exe", folder.FileName);
        Assert.Equal([@"C:\ProgramData\LunaDNS\logs"], folder.ArgumentList);

        // explorer.exe không đọc theo quy tắc trích dẫn thường: cần đúng một chuỗi /select,"đường dẫn".
        var reveal = SystemShell.RevealCommand(@"C:\Users\a\Desktop\LunaDNS report.txt", mac: false);
        Assert.Equal("explorer.exe", reveal.FileName);
        Assert.Equal(@"/select,""C:\Users\a\Desktop\LunaDNS report.txt""", reveal.Arguments);
        Assert.Empty(reveal.ArgumentList);
    }
}
