using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Tls;

namespace LunaDns.Core.Tests.Proxy;

internal sealed class FakeConnector : IUpstreamConnector
{
    private int _calls;

    /// <summary>Mặc định trả một <see cref="RecordingStream"/> mới. Ném ở đây để giả lập máy chủ không nối được.</summary>
    public Func<string, int, Stream> Connect { get; set; } = (_, _) => new RecordingStream();

    public int Calls => Volatile.Read(ref _calls);

    public Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Connect(host, port));
    }
}
