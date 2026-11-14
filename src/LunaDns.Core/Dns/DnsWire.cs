using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Dns;

/// <summary>Đọc/ghi wire format DNS (RFC 1035) ở mức stub cần: header, question, OPT, TTL.</summary>
public static class DnsWire
{
    public const int HeaderLength = 12;
    public const int MinUdpPayload = 512;
    public const int MaxUdpPayload = 4096;
    internal const ushort TypeOpt = 41;
    public const ushort TypeA = 1;
    public const ushort TypeAaaa = 28;

    private const int FlagResponse = 0x8000;
    private const int FlagTruncated = 0x0200;
    private const int FlagRecursionAvailable = 0x0080;
    private const int KeepOpcodeAndRd = 0x7900;

    public static ushort ReadId(ReadOnlySpan<byte> msg) => BinaryPrimitives.ReadUInt16BigEndian(msg);

    public static DnsRcode ReadRcode(ReadOnlySpan<byte> msg) => (DnsRcode)(msg[3] & 0x0F);

    /// <summary>Đọc truy vấn có đúng một question. Sai định dạng thì trả false.</summary>
    public static bool TryParseQuery(ReadOnlySpan<byte> msg, [NotNullWhen(true)] out DnsQuery? query)
    {
        query = null;
        if (msg.Length < HeaderLength) return false;
        var flags = Read16(msg, 2);
        if ((flags & FlagResponse) != 0) return false;
        if (Read16(msg, 4) != 1 || Read16(msg, 6) != 0 || Read16(msg, 8) != 0) return false;

        var offset = HeaderLength;
        if (!TryReadQuestion(msg, ref offset, out var question)) return false;

        var maxUdp = MinUdpPayload;
        for (var i = Read16(msg, 10); i > 0; i--)
        {
            if (!TrySkipName(msg, ref offset) || offset + 10 > msg.Length) return false;
            var type = Read16(msg, offset);
            var udpSize = Read16(msg, offset + 2); // với OPT, trường CLASS là cỡ UDP tối đa
            offset += 10 + Read16(msg, offset + 8);
            if (offset > msg.Length) return false;
            if (type == TypeOpt) maxUdp = Math.Clamp((int)udpSize, MinUdpPayload, MaxUdpPayload);
        }

        query = new DnsQuery(ReadId(msg), (flags >> 11) & 0xF, question, maxUdp);
        return true;
    }

    /// <summary>Response chỉ gồm header + question của truy vấn (bỏ OPT), dùng cho REFUSED/SERVFAIL/FORMERR/NOTIMP/TC.</summary>
    public static byte[] BuildHeaderOnlyResponse(ReadOnlySpan<byte> query, DnsRcode rcode, bool truncated = false)
    {
        if (query.Length < HeaderLength) throw new ArgumentException("Gói ngắn hơn header DNS.", nameof(query));
        var questionEnd = HeaderLength;
        var hasQuestion = Read16(query, 4) == 1 && TryReadQuestion(query, ref questionEnd, out _);
        if (!hasQuestion) questionEnd = HeaderLength;

        var response = query[..questionEnd].ToArray();
        var flags = (Read16(query, 2) & KeepOpcodeAndRd) | FlagResponse | FlagRecursionAvailable
                    | (truncated ? FlagTruncated : 0) | (int)rcode;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), (ushort)flags);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), (ushort)(hasQuestion ? 1 : 0));
        response.AsSpan(6, 6).Clear();
        return response;
    }

    /// <summary>TTL nhỏ nhất (giây) của các bản ghi, bỏ qua OPT. Không có bản ghi hoặc sai định dạng thì null: không cache.</summary>
    public static uint? GetMinTtl(ReadOnlySpan<byte> response)
    {
        var offsets = new List<int>();
        if (!TryGetTtlOffsets(response, offsets) || offsets.Count == 0) return null;
        var min = uint.MaxValue;
        foreach (var at in offsets) min = Math.Min(min, BinaryPrimitives.ReadUInt32BigEndian(response[at..]));
        return min;
    }

    /// <summary>Bản sao response mang ID của client; mỗi TTL = max(0, min(ttl, maxTtlSeconds) − elapsedSeconds).</summary>
    public static byte[] RewriteForClient(ReadOnlySpan<byte> response, ushort id, uint elapsedSeconds, uint maxTtlSeconds = uint.MaxValue)
    {
        if (response.Length < HeaderLength) throw new ArgumentException("Gói ngắn hơn header DNS.", nameof(response));
        var copy = response.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(copy, id);
        var offsets = new List<int>();
        if (!TryGetTtlOffsets(copy, offsets)) return copy;
        foreach (var at in offsets)
        {
            var ttl = Math.Min(BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan(at)), maxTtlSeconds);
            BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(at), ttl > elapsedSeconds ? ttl - elapsedSeconds : 0);
        }
        return copy;
    }

    /// <summary>Đúng là response (QR=1) cho đúng một question này.</summary>
    public static bool IsResponseTo(ReadOnlySpan<byte> response, DnsQuestion question)
    {
        if (response.Length < HeaderLength || (Read16(response, 2) & FlagResponse) == 0 || Read16(response, 4) != 1) return false;
        var offset = HeaderLength;
        return TryReadQuestion(response, ref offset, out var echoed) && echoed == question;
    }

    /// <summary>Dựng truy vấn một question, ID = 0, RD = 1, class IN. Dùng cho resolver nội bộ (Prober, proxy).</summary>
    public static byte[] BuildQuery(string name, ushort type)
    {
        ArgumentNullException.ThrowIfNull(name);
        var host = DomainMatcher.Normalize(name);
        if (host.Length is 0 or > 253)
            throw new ArgumentException($"Tên miền không hợp lệ: '{name}'", nameof(name));

        var msg = new byte[HeaderLength + host.Length + 2 + 4];
        msg[2] = 0x01; // RD
        msg[5] = 1;    // QDCOUNT
        var pos = HeaderLength;
        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > 63)
                throw new ArgumentException($"Tên miền không hợp lệ: '{name}'", nameof(name));
            msg[pos++] = (byte)label.Length;
            foreach (var c in label)
            {
                if (c <= 0x20 || c >= 0x7F)
                    throw new ArgumentException($"Tên miền không hợp lệ: '{name}'", nameof(name));
                msg[pos++] = (byte)c;
            }
        }
        msg[pos++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(pos), type);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(pos + 2), 1);
        return msg;
    }

    /// <summary>Địa chỉ trong các bản ghi A/AAAA (class IN) của phần answer. Gói sai định dạng thì trả rỗng.</summary>
    public static IReadOnlyList<IPAddress> ReadAddresses(ReadOnlySpan<byte> response)
    {
        if (response.Length < HeaderLength) return [];
        var offset = HeaderLength;
        for (var i = Read16(response, 4); i > 0; i--)
        {
            if (!TrySkipName(response, ref offset) || offset + 4 > response.Length) return [];
            offset += 4;
        }

        var result = new List<IPAddress>();
        for (var i = Read16(response, 6); i > 0; i--)
        {
            if (!TrySkipName(response, ref offset) || offset + 10 > response.Length) return [];
            var type = Read16(response, offset);
            var cls = Read16(response, offset + 2);
            int length = Read16(response, offset + 8);
            offset += 10;
            if (offset + length > response.Length) return [];
            if (cls == 1 && ((type == TypeA && length == 4) || (type == TypeAaaa && length == 16)))
                result.Add(new IPAddress(response.Slice(offset, length)));
            offset += length;
        }
        return result;
    }

    internal static bool TryGetTtlOffsets(ReadOnlySpan<byte> msg, List<int> offsets)
    {
        if (msg.Length < HeaderLength) return false;
        var offset = HeaderLength;
        for (var i = Read16(msg, 4); i > 0; i--)
        {
            if (!TrySkipName(msg, ref offset) || offset + 4 > msg.Length) return false;
            offset += 4;
        }
        var records = Read16(msg, 6) + Read16(msg, 8) + Read16(msg, 10);
        for (var i = 0; i < records; i++)
        {
            if (!TrySkipName(msg, ref offset) || offset + 10 > msg.Length) return false;
            if (Read16(msg, offset) != TypeOpt) offsets.Add(offset + 4);
            offset += 10 + Read16(msg, offset + 8);
            if (offset > msg.Length) return false;
        }
        return true;
    }

    internal static bool TryReadQuestion(ReadOnlySpan<byte> msg, ref int offset, [NotNullWhen(true)] out DnsQuestion? question)
    {
        question = null;
        var name = new StringBuilder();
        var pos = offset;
        while (true)
        {
            if (pos >= msg.Length) return false;
            int length = msg[pos++];
            if (length == 0) break;
            // > 63 gồm cả con trỏ nén (0xC0): question không được nén.
            if (length > 63 || pos + length > msg.Length) return false;
            if (name.Length > 0) name.Append('.');
            foreach (var b in msg.Slice(pos, length))
            {
                // Chặn dấu chấm và ký tự điều khiển trong label, để "a.steampowered" + "com" không giả được tên game.
                if (b <= 0x20 || b >= 0x7F || b == (byte)'.') return false;
                name.Append((char)b);
            }
            pos += length;
            if (name.Length > 253) return false;
        }
        if (pos + 4 > msg.Length) return false;
        question = new DnsQuestion(DomainMatcher.Normalize(name.ToString()), Read16(msg, pos), Read16(msg, pos + 2));
        offset = pos + 4;
        return true;
    }

    /// <summary>Bỏ qua một tên (label hoặc con trỏ nén). Sai định dạng thì false, offset giữ nguyên.</summary>
    internal static bool TrySkipName(ReadOnlySpan<byte> msg, ref int offset)
    {
        var pos = offset;
        while (pos < msg.Length)
        {
            int length = msg[pos];
            if ((length & 0xC0) == 0xC0)
            {
                if (pos + 2 > msg.Length) return false;
                offset = pos + 2;
                return true;
            }
            if (length > 63) return false;
            pos += 1 + length;
            if (length == 0)
            {
                offset = pos;
                return true;
            }
        }
        return false;
    }

    private static ushort Read16(ReadOnlySpan<byte> msg, int at) => BinaryPrimitives.ReadUInt16BigEndian(msg[at..]);
}
