using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

internal sealed class FakeUpstream : IDnsUpstream
{
    private int _calls;

    public Func<byte[], byte[]> Respond { get; set; } = Answer();

    public int Calls => Volatile.Read(ref _calls);

    public Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Respond(query.ToArray()));
    }

    /// <summary>Trả lời đúng tên được hỏi bằng <paramref name="records"/> bản ghi A, ID = 0 như DoH.</summary>
    public static Func<byte[], byte[]> Answer(int records = 1, uint ttl = 300) => query =>
    {
        Assert.True(DnsWire.TryParseQuery(query, out var parsed));
        return TestDnsMessage.Response(parsed.Question.Name, 0, Enumerable.Repeat(ttl, records).ToArray());
    };
}
