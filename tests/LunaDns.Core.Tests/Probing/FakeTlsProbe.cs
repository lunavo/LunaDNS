using System.Net;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

internal sealed class FakeTlsProbe : ITlsProbe
{
    private readonly List<(IPAddress Address, string Host, bool Split)> _calls = [];

    /// <summary>Mặc định: mọi lần bắt tay đều thành công.</summary>
    public Func<IPAddress, string, bool, CancellationToken, Task<bool>> RespondAsync { get; set; } =
        (_, _, _, _) => Task.FromResult(true);

    public IReadOnlyList<(IPAddress Address, string Host, bool Split)> Calls
    {
        get
        {
            lock (_calls) return [.. _calls];
        }
    }

    public Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken)
    {
        lock (_calls) _calls.Add((address, host, split));
        return RespondAsync(address, host, split, cancellationToken);
    }
}
