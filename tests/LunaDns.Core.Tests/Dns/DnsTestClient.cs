using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace LunaDns.Core.Tests.Dns;

internal static class DnsTestClient
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public static async Task<byte[]> UdpAsync(IPEndPoint server, byte[] query)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Connect(server);
        await udp.SendAsync(query);
        using var cts = new CancellationTokenSource(Limit);
        return (await udp.ReceiveAsync(cts.Token)).Buffer;
    }

    public static async Task<NetworkStream> ConnectTcpAsync(IPEndPoint server)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(server);
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>Gửi một truy vấn có tiền tố độ dài 2 byte (RFC 1035 §4.2.2) và đọc một response.</summary>
    public static async Task<byte[]> TcpAsync(NetworkStream stream, byte[] query)
    {
        using var cts = new CancellationTokenSource(Limit);
        var framed = new byte[2 + query.Length];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Length);
        query.CopyTo(framed, 2);
        await stream.WriteAsync(framed, cts.Token);

        var prefix = new byte[2];
        await stream.ReadExactlyAsync(prefix, cts.Token);
        var response = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
        await stream.ReadExactlyAsync(response, cts.Token);
        return response;
    }
}
