using LunaDns.Platform.MacOS.Processes;
using LunaDns.Platform.MacOS.Proxy;

namespace LunaDns.Platform.MacOS.Tests;

/// <summary>networksetup giả: giữ URL/trạng thái auto-proxy và proxy thủ công của từng service, ghi lại mọi lệnh đổi.</summary>
internal sealed class FakeNetworkSetup : IProcessRunner
{
    public const string ServiceOrder = """
        An asterisk (*) denotes that a network service is disabled.
        (1) Luna Ethernet
        (Hardware Port: AX88179A, Device: en7)

        (2) Luna Wifi
        (Hardware Port: Wi-Fi, Device: en0)

        (*) Old USB
        (Hardware Port: USB 10/100 LAN, Device: en5)

        (3) Surfshark. WireGuard
        (Hardware Port: com.surfshark.vpnclient.macos.direct, Device: )

        """;

    public Dictionary<string, (string? Url, bool Enabled)> Auto { get; } = new(StringComparer.Ordinal)
    {
        ["Luna Ethernet"] = (null, false),
        ["Luna Wifi"] = ("http://127.0.0.1:8099/canopi.pac", false),
        ["Old USB"] = (null, false),
        ["Surfshark. WireGuard"] = (null, false),
    };

    public HashSet<string> ManualProxy { get; } = new(StringComparer.Ordinal);

    public List<string> Changes { get; } = [];

    public List<string> Calls { get; } = [];

    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
    {
        Calls.Add(fileName + " " + string.Join(' ', arguments));
        if (fileName != NetworkSetupAutoProxy.Tool) return new ProcessResult(0, "");
        var cmd = arguments[0];
        switch (cmd)
        {
            case "-listnetworkserviceorder":
                return new ProcessResult(0, ServiceOrder);
            case "-getautoproxyurl":
                var (url, enabled) = Auto[arguments[1]];
                return new ProcessResult(0, $"URL: {url ?? "(null)"}\nEnabled: {(enabled ? "Yes" : "No")}\n");
            case "-getwebproxy":
            case "-getsecurewebproxy":
                return new ProcessResult(0, $"Enabled: {(ManualProxy.Contains(arguments[1]) ? "Yes" : "No")}\nServer: 127.0.0.1\nPort: 9090\n");
            case "-setautoproxyurl":
                Auto[arguments[1]] = (arguments[2], true);
                Changes.Add($"url {arguments[1]} {arguments[2]}");
                return new ProcessResult(0, "");
            case "-setautoproxystate":
                Auto[arguments[1]] = (Auto[arguments[1]].Url, arguments[2] == "on");
                Changes.Add($"state {arguments[1]} {arguments[2]}");
                return new ProcessResult(0, "");
            default:
                return new ProcessResult(4, "** Error: unknown");
        }
    }
}
