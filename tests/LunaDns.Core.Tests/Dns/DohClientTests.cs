using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DohClientTests
{
    private const string Name = "store.steampowered.com";
    private static readonly byte[] StoreQuery = TestDnsMessage.Query(Name);

    private static byte[] Answer() => TestDnsMessage.Response(Name, 0, 300);

    private static DohClient Client(FakeHttpHandler handler, TimeSpan? timeout = null) =>
        new(handler, DohServer.Defaults, timeout);

    /// <summary>Cloudflare chạy theo <paramref name="cloudflare"/>, Google luôn trả lời đúng.</summary>
    private static FakeHttpHandler Route(Func<CancellationToken, Task<HttpResponseMessage>> cloudflare) =>
        new((request, ct) => request.RequestUri!.Host == DohServer.Cloudflare.Endpoint.Host
            ? cloudflare(ct)
            : Task.FromResult(FakeHttpHandler.DnsMessage(Answer())));

    [Fact]
    public async Task Posts_wire_query_with_zero_id_and_dns_message_headers()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.DnsMessage(Answer())));
        using var client = Client(handler);

        var response = await client.QueryAsync(StoreQuery, CancellationToken.None);

        Assert.Equal(Answer(), response);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(DohServer.Cloudflare.Endpoint, request.Uri);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(DohClient.MediaType, request.ContentType);
        Assert.Contains(DohClient.MediaType, request.Accept, StringComparison.Ordinal);
        Assert.Equal(0, DnsWire.ReadId(request.Body));
        Assert.Equal(StoreQuery[2..], request.Body[2..]);
    }

    [Fact]
    public async Task Falls_back_to_google_on_http_error()
    {
        var handler = Route(_ => Task.FromResult(FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable)));
        using var client = Client(handler);

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(DohServer.Google.Endpoint, handler.Requests[1].Uri);
    }

    [Fact]
    public async Task Falls_back_to_google_on_timeout()
    {
        var handler = Route(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHttpHandler.Status(HttpStatusCode.OK);
        });
        using var client = Client(handler, TimeSpan.FromMilliseconds(200));

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Falls_back_when_response_is_for_another_question()
    {
        var handler = Route(_ => Task.FromResult(FakeHttpHandler.DnsMessage(TestDnsMessage.Response("other.com", 0, 300))));
        using var client = Client(handler);

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Falls_back_on_wrong_content_type()
    {
        var handler = Route(_ => Task.FromResult(FakeHttpHandler.DnsMessage(Answer(), "text/html")));
        using var client = Client(handler);

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Throws_DohException_when_every_server_fails()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Status(HttpStatusCode.InternalServerError)));
        using var client = Client(handler);

        await Assert.ThrowsAsync<DohException>(() => client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_fallback()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.DnsMessage(Answer())));
        using var client = Client(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.QueryAsync(StoreQuery, cts.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Rejects_malformed_query()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.DnsMessage(Answer())));
        using var client = Client(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.QueryAsync(new byte[5], CancellationToken.None));
    }
}
