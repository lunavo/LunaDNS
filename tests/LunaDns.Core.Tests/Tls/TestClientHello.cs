using System.Text;

namespace LunaDns.Core.Tests.Tls;

/// <summary>Sinh ClientHello TLS tối giản nhưng đúng cấu trúc, để test parser và splitter.</summary>
internal static class TestClientHello
{
    public static byte[] Build(string? sni, int paddingLength = 0)
    {
        var body = new List<byte>();
        body.AddRange([0x03, 0x03]);                 // client_version
        body.AddRange(new byte[32]);                 // random
        body.Add(32); body.AddRange(new byte[32]);   // session_id
        body.AddRange([0x00, 0x02, 0x13, 0x01]);     // cipher_suites
        body.AddRange([0x01, 0x00]);                 // compression_methods

        var ext = new List<byte>();
        if (sni is not null)
        {
            var name = Encoding.ASCII.GetBytes(sni);
            ext.AddRange([0x00, 0x00]);              // type server_name
            ext.AddRange(U16(name.Length + 5));      // extension length
            ext.AddRange(U16(name.Length + 3));      // server_name_list length
            ext.Add(0x00);                           // name_type host_name
            ext.AddRange(U16(name.Length));
            ext.AddRange(name);
        }
        if (paddingLength > 0)
        {
            ext.AddRange([0x00, 0x15]);              // type padding
            ext.AddRange(U16(paddingLength));
            ext.AddRange(new byte[paddingLength]);
        }
        body.AddRange(U16(ext.Count));
        body.AddRange(ext);

        var hs = new List<byte> { 0x01, (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)body.Count };
        hs.AddRange(body);

        var rec = new List<byte> { 0x16, 0x03, 0x01 };
        rec.AddRange(U16(hs.Count));
        rec.AddRange(hs);
        return [.. rec];
    }

    private static byte[] U16(int v) => [(byte)(v >> 8), (byte)v];
}
