namespace LunaDns.App.Tests;

public sealed class InstallLocationTests
{
    [WindowsTheory]
    [InlineData(@"C:\Program Files\LunaDNS\LunaDNS.exe", @"C:\Program Files", true)]
    [InlineData(@"c:\program files\lunadns\LunaDNS.exe", @"C:\Program Files\", true)]
    [InlineData(@"C:\Program Files Evil\LunaDNS.exe", @"C:\Program Files", false)]
    [InlineData(@"C:\Users\a\Downloads\LunaDNS\LunaDNS.exe", @"C:\Program Files", false)]
    [InlineData(@"C:\Program Files\..\Users\a\LunaDNS.exe", @"C:\Program Files", false)]
    public void IsUnder_compares_full_paths_with_separator(string path, string directory, bool expected)
    {
        Assert.Equal(expected, InstallLocation.IsUnder(path, directory));
    }

    [WindowsFact]
    public void IsInstalled_needs_uninstaller_next_to_exe()
    {
        using var temp = new TempDirectory();
        var exe = temp.PathOf(@"LunaDNS\LunaDNS.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");

        Assert.False(InstallLocation.IsInstalled(exe, temp.Path));
        File.WriteAllText(temp.PathOf(@"LunaDNS\unins000.exe"), "");
        Assert.True(InstallLocation.IsInstalled(exe, temp.Path));
    }
}
