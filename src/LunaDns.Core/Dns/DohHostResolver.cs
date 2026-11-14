using System.Net;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Dns;

/// <summary>Phân giải qua DoH (hoặc bất kỳ <see cref="IDnsUpstream"/> nào), không đụng DNS hệ thống.</summary>
public sealed class DohHostResolver : IHostResolver
{
    private readonly IDnsUpstream _upstream;
    private readonly DnsCache? _cache;

    public DohHostResolver(IDnsUpstream upstream, DnsCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        _upstream = upstream;
        _cache = cache;
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        var query = DnsWire.BuildQuery(host, DnsWire.TypeA);
        var question = new DnsQuestion(DomainMatcher.Normalize(host), DnsWire.TypeA, 1);
        if (_cache is not null && _cache.TryGet(question, 0, out var cached))
            return DnsWire.ReadAddresses(cached);

        var response = await _upstream.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        if (!DnsWire.IsResponseTo(response, question))
            throw new DohException($"Máy chủ DoH trả lời sai câu hỏi cho {question.Name}.");
        var rcode = DnsWire.ReadRcode(response);
        if (rcode is not (DnsRcode.NoError or DnsRcode.NxDomain))
            throw new DohException($"Máy chủ DoH trả mã lỗi {rcode} cho {question.Name}.");

        _cache?.Store(question, response);
        return DnsWire.ReadAddresses(response);
    }
}
