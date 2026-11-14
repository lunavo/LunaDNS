using System.Net;
using System.Net.Http.Headers;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

internal sealed record RecordedRequest(Uri Uri, HttpMethod Method, string? ContentType, string Accept, byte[] Body);

internal sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.RequestUri!, request.Method,
            request.Content?.Headers.ContentType?.MediaType, request.Headers.Accept.ToString(), body));
        return await respond(request, cancellationToken);
    }

    public static HttpResponseMessage DnsMessage(byte[] body, string mediaType = DohClient.MediaType)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);
}
