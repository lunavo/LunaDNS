using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace LunaDns.Core.Tests.Dns;

/// <summary>Dựng gói DNS mẫu cho test. Tên ở answer/authority nén bằng con trỏ 0xC00C về question.</summary>
internal static class TestDnsMessage
{
    public const ushort TypeA = 1;
    public const ushort TypeAaaa = 28;

    public static byte[] Query(string name, ushort type = TypeA, ushort id = 0x1234, int? ednsUdpSize = null, ushort flags = 0x0100)
    {
        var m = new List<byte>();
        Header(m, id, flags, qd: 1, an: 0, ns: 0, ar: ednsUdpSize is null ? 0 : 1);
        Question(m, name, type);
        if (ednsUdpSize is int size) Opt(m, (ushort)size);
        return [.. m];
    }

    /// <summary>Response NOERROR, mỗi TTL một bản ghi A.</summary>
    public static byte[] Response(string name, ushort id, params uint[] ttls) => Build(name, id, withOpt: false, ttls);

    /// <summary>Như <see cref="Response"/> nhưng có thêm OPT (11 byte) ở cuối, như DoH thật hay trả.</summary>
    public static byte[] ResponseWithOpt(string name, ushort id, params uint[] ttls) => Build(name, id, withOpt: true, ttls);

    /// <summary>NXDOMAIN kèm SOA ở authority, giống DoH trả cho tên không tồn tại.</summary>
    public static byte[] NxDomain(string name, ushort id, uint soaTtl)
    {
        var m = new List<byte>();
        Header(m, id, 0x8183, qd: 1, an: 0, ns: 1, ar: 0);
        Question(m, name, TypeA);
        m.Add(0xC0);
        m.Add(0x0C);
        U16(m, 6);
        U16(m, 1);
        U32(m, soaTtl);
        U16(m, 22);
        m.Add(0); // MNAME = root
        m.Add(0); // RNAME = root
        for (var i = 0; i < 5; i++) U32(m, 60);
        return [.. m];
    }

    /// <summary>Response NOERROR, mỗi địa chỉ một bản ghi A hoặc AAAA, TTL 300.</summary>
    public static byte[] ResponseWithAddresses(string name, ushort id, params IPAddress[] addresses)
    {
        var m = new List<byte>();
        Header(m, id, 0x8180, qd: 1, an: addresses.Length, ns: 0, ar: 0);
        Question(m, name, TypeA);
        foreach (var address in addresses)
        {
            var bytes = address.GetAddressBytes();
            m.Add(0xC0);
            m.Add(0x0C);
            U16(m, bytes.Length == 4 ? TypeA : TypeAaaa);
            U16(m, 1);
            U32(m, 300);
            U16(m, (ushort)bytes.Length);
            m.AddRange(bytes);
        }
        return [.. m];
    }

    public static ushort U16(byte[] msg, int at) => BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(at));

    private static byte[] Build(string name, ushort id, bool withOpt, uint[] ttls)
    {
        var m = new List<byte>();
        Header(m, id, 0x8180, qd: 1, an: ttls.Length, ns: 0, ar: withOpt ? 1 : 0);
        Question(m, name, TypeA);
        foreach (var ttl in ttls)
        {
            m.Add(0xC0);
            m.Add(0x0C);
            U16(m, TypeA);
            U16(m, 1);
            U32(m, ttl);
            U16(m, 4);
            U32(m, 0x5DB8D822); // 93.184.216.34
        }
        if (withOpt) Opt(m, 1232);
        return [.. m];
    }

    private static void Header(List<byte> m, ushort id, ushort flags, int qd, int an, int ns, int ar)
    {
        U16(m, id);
        U16(m, flags);
        U16(m, (ushort)qd);
        U16(m, (ushort)an);
        U16(m, (ushort)ns);
        U16(m, (ushort)ar);
    }

    private static void Question(List<byte> m, string name, ushort type)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            m.Add((byte)label.Length);
            m.AddRange(Encoding.ASCII.GetBytes(label));
        }
        m.Add(0);
        U16(m, type);
        U16(m, 1);
    }

    private static void Opt(List<byte> m, ushort udpSize)
    {
        m.Add(0);
        U16(m, 41);
        U16(m, udpSize);
        U32(m, 0);
        U16(m, 0);
    }

    private static void U16(List<byte> m, ushort v)
    {
        m.Add((byte)(v >> 8));
        m.Add((byte)v);
    }

    private static void U32(List<byte> m, uint v)
    {
        U16(m, (ushort)(v >> 16));
        U16(m, (ushort)v);
    }
}
