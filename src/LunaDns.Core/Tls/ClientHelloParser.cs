using System.Text;

namespace LunaDns.Core.Tls;

public static class ClientHelloParser
{
    private const byte HandshakeRecord = 0x16;
    private const byte ClientHelloType = 0x01;

    public static int RecordLength(ReadOnlySpan<byte> data) =>
        data.Length >= 5 && data[0] == HandshakeRecord ? 5 + ((data[3] << 8) | data[4]) : -1;

    public static bool TryFindSni(ReadOnlySpan<byte> data, out SniLocation sni)
    {
        sni = default;
        if (data.Length < 9 || data[0] != HandshakeRecord || data[5] != ClientHelloType) return false;

        var p = 5 + 4 + 2 + 32;                                   // header record + handshake + version + random
        if (!Skip(data, ref p, 1)) return false;                  // session_id
        if (!Skip(data, ref p, 2)) return false;                  // cipher_suites
        if (!Skip(data, ref p, 1)) return false;                  // compression_methods
        if (!ReadU16(data, p, out var extLen)) return false;
        p += 2;
        var extEnd = p + extLen;

        while (p + 4 <= extEnd)
        {
            if (!ReadU16(data, p, out var type) || !ReadU16(data, p + 2, out var len)) return false;
            var body = p + 4;
            if (type == 0)
            {
                // server_name_list(2) + name_type(1) + name_length(2) + name
                if (body + 5 > data.Length || data[body + 2] != 0) return false;
                if (!ReadU16(data, body + 3, out var nameLen)) return false;
                var offset = body + 5;
                if (nameLen == 0 || offset + nameLen > data.Length) return false;
                sni = new SniLocation(offset, nameLen, Encoding.ASCII.GetString(data.Slice(offset, nameLen)));
                return true;
            }
            p = body + len;
        }
        return false;
    }

    // Bỏ qua trường vector: đọc độ dài kích thước prefixBytes (1 hoặc 2) rồi nhảy qua nội dung.
    private static bool Skip(ReadOnlySpan<byte> d, ref int p, int prefixBytes)
    {
        if (p + prefixBytes > d.Length) return false;
        var len = prefixBytes == 1 ? d[p] : (d[p] << 8) | d[p + 1];
        p += prefixBytes + len;
        return p <= d.Length;
    }

    private static bool ReadU16(ReadOnlySpan<byte> d, int p, out int value)
    {
        value = 0;
        if (p < 0 || p + 2 > d.Length) return false;
        value = (d[p] << 8) | d[p + 1];
        return true;
    }
}
