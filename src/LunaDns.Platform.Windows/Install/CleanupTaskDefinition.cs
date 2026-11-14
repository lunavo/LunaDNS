using System.Xml.Linq;

namespace LunaDns.Platform.Windows.Install;

/// <summary>
/// XML của task \LunaDNS\Cleanup (spec 4.7, KH7 K7–K8): SYSTEM chạy "LunaDNS.exe --cleanup" lúc khởi động máy
/// và lúc có người đăng nhập (hive HKU\&lt;SID&gt; chỉ có khi đã đăng nhập, cần cho mục AutoConfigURL).
/// </summary>
public static class CleanupTaskDefinition
{
    public const string TaskName = @"\LunaDNS\Cleanup";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string BuildXml(string exePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(exePath);
        if (!Path.IsPathFullyQualified(exePath)) throw new ArgumentException("Cần đường dẫn tuyệt đối.", nameof(exePath));

        var task = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "Hoàn tác thay đổi mạng LunaDNS còn sót khi máy khởi động hoặc có người đăng nhập.")),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "BootTrigger", new XElement(Ns + "Enabled", "true")),
                new XElement(Ns + "LogonTrigger", new XElement(Ns + "Enabled", "true"))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", "S-1-5-18"),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "StartWhenAvailable", "true"),
                new XElement(Ns + "ExecutionTimeLimit", "PT5M"),
                new XElement(Ns + "Enabled", "true")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", $"\"{exePath}\""),
                    new XElement(Ns + "Arguments", "--cleanup"))));

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine + task;
    }
}
