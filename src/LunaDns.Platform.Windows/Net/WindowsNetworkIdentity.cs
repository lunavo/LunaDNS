using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using LunaDns.Core.Net;
using LunaDns.Core.Platform;

namespace LunaDns.Platform.Windows.Net;

/// <summary>Mạng hiện tại trên Windows: interface Windows chọn để ra 1.1.1.1 (GetBestInterface), gateway IPv4 của nó, MAC qua SendARP.</summary>
public sealed partial class WindowsNetworkIdentity : INetworkIdentity
{
    private static readonly uint Probe = BinaryPrimitives.ReadUInt32LittleEndian(IPAddress.Parse("1.1.1.1").GetAddressBytes());

    public NetworkSnapshot? Current()
    {
        try
        {
            if (GetBestInterface(Probe, out var index) != 0) return null;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var props = nic.GetIPProperties();
                if (props.GetIPv4Properties()?.Index != (int)index) continue;
                var gateway = props.GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                if (gateway is null) return null;
                var id = NetworkId.From(ReadMac(gateway), gateway.ToString(), nic.NetworkInterfaceType.ToString(), nic.Name);
                return id is null ? null : new NetworkSnapshot(id, $"{nic.Name} · {gateway}");
            }
            return null;
        }
        catch (Exception ex) when (ex is NetworkInformationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    internal static string? FormatMac(ReadOnlySpan<byte> mac) =>
        mac.Length == 6 ? NetworkId.NormalizeMac(string.Join(':', mac.ToArray().Select(b => b.ToString("x2", CultureInfo.InvariantCulture)))) : null;

    private static string? ReadMac(IPAddress gateway)
    {
        var dest = BinaryPrimitives.ReadUInt32LittleEndian(gateway.GetAddressBytes());
        var buffer = new byte[8];
        var length = (uint)buffer.Length;
        return SendARP(dest, 0, buffer, ref length) == 0 && length >= 6 ? FormatMac(buffer.AsSpan(0, 6)) : null;
    }

    [LibraryImport("iphlpapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetBestInterface(uint destAddr, out uint bestIfIndex);

    [LibraryImport("iphlpapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SendARP(uint destIp, uint srcIp, [Out] byte[] macAddr, ref uint physicalAddrLen);
}
