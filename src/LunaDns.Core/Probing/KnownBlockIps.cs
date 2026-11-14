using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace LunaDns.Core.Probing;

/// <summary>
/// Địa chỉ mà một máy chủ game công khai không bao giờ có. DNS hệ thống trả một địa chỉ như vậy cho domain game
/// thì chắc chắn câu trả lời đã bị sửa. IP trang chặn riêng của từng nhà mạng chưa có số liệu thực địa nên chưa
/// liệt kê; Prober vẫn bắt được chúng nhờ bước kiểm tra chứng chỉ.
/// </summary>
public static class KnownBlockIps
{
    private static readonly (uint Network, int PrefixLength)[] Ranges =
    [
        (0x00000000, 8),  // 0.0.0.0/8
        (0x0A000000, 8),  // 10.0.0.0/8
        (0x64400000, 10), // 100.64.0.0/10 (CGNAT)
        (0x7F000000, 8),  // 127.0.0.0/8
        (0xA9FE0000, 16), // 169.254.0.0/16
        (0xAC100000, 12), // 172.16.0.0/12
        (0xC0A80000, 16), // 192.168.0.0/16
        (0xE0000000, 3),  // 224.0.0.0/3 (multicast và dành riêng)
    ];

    public static bool IsBlockAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6Any);

        Span<byte> bytes = stackalloc byte[4];
        ip.TryWriteBytes(bytes, out _);
        var value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        foreach (var (network, prefixLength) in Ranges)
        {
            if ((value & (uint.MaxValue << (32 - prefixLength))) == network) return true;
        }
        return false;
    }
}
