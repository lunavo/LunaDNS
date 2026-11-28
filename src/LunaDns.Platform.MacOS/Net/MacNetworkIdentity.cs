using System.ComponentModel;
using System.Text.RegularExpressions;
using LunaDns.Core.Net;
using LunaDns.Core.Platform;
using LunaDns.Platform.MacOS.Processes;

namespace LunaDns.Platform.MacOS.Net;

/// <summary>Mạng hiện tại trên macOS: <c>route -n get default</c> cho gateway, <c>arp -n</c> cho MAC. Chạy được với quyền người dùng.</summary>
public sealed partial class MacNetworkIdentity(IProcessRunner runner) : INetworkIdentity
{
    public NetworkSnapshot? Current()
    {
        try
        {
            var route = runner.Run("/sbin/route", ["-n", "get", "default"]);
            if (route.ExitCode != 0 || ParseRoute(route.Output) is not { } parsed) return null;
            var (gateway, iface) = parsed;
            string? mac = null;
            try
            {
                var arp = runner.Run("/usr/sbin/arp", ["-n", gateway]);
                if (arp.ExitCode == 0) mac = ParseArp(arp.Output);
            }
            catch (Exception ex) when (IsProcessFailure(ex))
            {
                // Không có MAC thì dùng id yếu.
            }
            var id = NetworkId.From(mac, gateway, iface, iface);
            return id is null ? null : new NetworkSnapshot(id, $"{iface} · {gateway}");
        }
        catch (Exception ex) when (IsProcessFailure(ex))
        {
            return null;
        }
    }

    // Process.Start ném Win32Exception (không thấy/không đủ quyền), InvalidOperationException, IOException,
    // UnauthorizedAccessException hoặc PlatformNotSupportedException; Current() không được ném.
    private static bool IsProcessFailure(Exception ex) =>
        ex is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException or PlatformNotSupportedException;

    internal static (string Gateway, string Interface)? ParseRoute(string output)
    {
        var gateway = RouteGateway().Match(output);
        var iface = RouteInterface().Match(output);
        return gateway.Success && iface.Success ? (gateway.Groups[1].Value, iface.Groups[1].Value) : null;
    }

    internal static string? ParseArp(string output)
    {
        var m = ArpMac().Match(output);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^\s*gateway:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex RouteGateway();

    [GeneratedRegex(@"^\s*interface:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex RouteInterface();

    [GeneratedRegex(@"\sat\s+([0-9A-Fa-f]{1,2}(?::[0-9A-Fa-f]{1,2}){5})\s")]
    private static partial Regex ArpMac();
}
