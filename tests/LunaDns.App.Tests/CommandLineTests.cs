namespace LunaDns.App.Tests;

public sealed class CommandLineTests
{
    // Kiểu CommandKind là internal nên tham số theory dùng tên dạng chuỗi (tránh CS0051).
    [Theory]
    [InlineData("", "Gui")]
    [InlineData("--cleanup", "Cleanup")]
    [InlineData("--CLEANUP", "Cleanup")]
    [InlineData("--report", "Report")]
    [InlineData("--install-system", "InstallSystem")]
    [InlineData("--INSTALL-SYSTEM", "InstallSystem")]
    [InlineData("--uninstall", "Uninstall")]
    [InlineData("--install-system x", "Invalid")]
    [InlineData("--bogus", "Invalid")]
    [InlineData("--cleanup --report", "Invalid")]
    public void Parses(string args, string expected) =>
        Assert.Equal(expected, CommandLine.Parse(args.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToString());
}
