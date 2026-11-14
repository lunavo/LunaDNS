using LunaDns.Core.Domains;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>Xử lý một gói truy vấn của stub, không phụ thuộc UDP hay TCP (spec 4.2).</summary>
public sealed partial class DnsQueryHandler
{
    private readonly IReadOnlyList<string> _domains;
    private readonly IDnsUpstream _upstream;
    private readonly DnsCache _cache;
    private readonly ILogger _logger;

    /// <param name="domains">Domain được trả lời (domain và subdomain). Domain khác nhận REFUSED.</param>
    public DnsQueryHandler(IReadOnlyList<string> domains, IDnsUpstream upstream, DnsCache cache, ILogger<DnsQueryHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(domains);
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(cache);
        _domains = domains;
        _upstream = upstream;
        _cache = cache;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Response để gửi lại client, hoặc null khi phải im lặng (gói ngắn hơn header, hoặc là response lạc vào).</summary>
    public async Task<byte[]?> HandleAsync(ReadOnlyMemory<byte> packet, DnsTransport transport, CancellationToken cancellationToken)
    {
        if (packet.Length < DnsWire.HeaderLength || (packet.Span[2] & 0x80) != 0) return null;
        if (!DnsWire.TryParseQuery(packet.Span, out var query))
            return DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.FormErr);
        if (query.Opcode != 0)
            return DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.NotImp);
        if (!DomainMatcher.Matches(query.Question.Name, _domains))
        {
            LogRefused(_logger, query.Question.Name);
            return DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.Refused);
        }

        if (!_cache.TryGet(query.Question, query.Id, out var response))
        {
            byte[] fresh;
            try
            {
                fresh = await _upstream.QueryAsync(packet, cancellationToken).ConfigureAwait(false);
            }
            catch (DohException ex)
            {
                LogUpstreamFailed(_logger, query.Question.Name, ex);
                return DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.ServFail);
            }
            _cache.Store(query.Question, fresh);
            response = DnsWire.RewriteForClient(fresh, query.Id, elapsedSeconds: 0, DnsCache.MaxTtlSeconds);
        }

        var limit = transport == DnsTransport.Udp ? query.MaxUdpPayload : ushort.MaxValue;
        return response.Length <= limit
            ? response
            : DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.NoError, truncated: true);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Từ chối {Name}: không thuộc danh sách domain game")]
    private static partial void LogRefused(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DoH lỗi khi hỏi {Name}, trả SERVFAIL")]
    private static partial void LogUpstreamFailed(ILogger logger, string name, Exception exception);
}
