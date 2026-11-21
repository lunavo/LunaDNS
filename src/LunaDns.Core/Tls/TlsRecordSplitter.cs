using System.Buffers.Binary;

namespace LunaDns.Core.Tls;

/// <summary>
/// Tách một TLS record handshake thành hai record tại <c>cut</c> (offset trong record, nằm trong phần thân).
/// Mỗi record mới giữ content type + version của record gốc; RFC 8446 §5.1 cho phép handshake trải nhiều record.
/// </summary>
public static class TlsRecordSplitter
{
    public const int HeaderLength = 5;

    public static IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> record, int cut)
    {
        var span = record.Span;
        if (ClientHelloParser.RecordLength(span) != record.Length)
            throw new ArgumentException("Cần đúng một TLS record handshake hoàn chỉnh.", nameof(record));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cut, HeaderLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cut, record.Length);
        return [Build(span, span[HeaderLength..cut]), Build(span, span[cut..])];
    }

    private static byte[] Build(ReadOnlySpan<byte> original, ReadOnlySpan<byte> body)
    {
        var result = new byte[HeaderLength + body.Length];
        original[..3].CopyTo(result);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(3), (ushort)body.Length);
        body.CopyTo(result.AsSpan(HeaderLength));
        return result;
    }
}
