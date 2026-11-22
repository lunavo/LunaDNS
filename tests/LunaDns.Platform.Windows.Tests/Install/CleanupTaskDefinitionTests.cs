using System.Xml.Linq;
using LunaDns.Platform.Windows.Install;

namespace LunaDns.Platform.Windows.Tests.Install;

public sealed class CleanupTaskDefinitionTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const string Exe = @"C:\Program Files\LunaDNS\LunaDNS.exe";

    private static XElement Root() => XDocument.Parse(CleanupTaskDefinition.BuildXml(Exe)).Root!;

    [WindowsFact]
    public void Declares_utf16_for_schtasks()
    {
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", CleanupTaskDefinition.BuildXml(Exe));
    }

    [WindowsFact]
    public void Runs_as_system()
    {
        var principal = Root().Element(Ns + "Principals")!.Element(Ns + "Principal")!;
        Assert.Equal("S-1-5-18", principal.Element(Ns + "UserId")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")!.Value);
    }

    [WindowsFact]
    public void Triggers_at_boot_and_logon()
    {
        var triggers = Root().Element(Ns + "Triggers")!.Elements().Select(e => e.Name.LocalName);
        Assert.Equal(["BootTrigger", "LogonTrigger"], triggers);
    }

    [WindowsFact]
    public void Executes_quoted_exe_with_cleanup()
    {
        var exec = Root().Element(Ns + "Actions")!.Element(Ns + "Exec")!;
        Assert.Equal($"\"{Exe}\"", exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--cleanup", exec.Element(Ns + "Arguments")!.Value);
    }

    [WindowsFact]
    public void Runs_on_battery_and_is_time_limited()
    {
        var settings = Root().Element(Ns + "Settings")!;
        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
        Assert.Equal("PT5M", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
        Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
    }

    [Fact]
    public void Rejects_relative_path()
    {
        Assert.Throws<ArgumentException>(() => CleanupTaskDefinition.BuildXml("LunaDNS.exe"));
    }
}
