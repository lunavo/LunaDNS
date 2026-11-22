using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Dns;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Proxy;

public sealed class TcpUpstreamConnectorTests : IDisposable
{
    private const string Host = "store.steampowered.com";
    private static readonly byte[] Ping = [42];

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly FakeResolver _resolver = new();
    private readonly int _port;

    public TcpUpstreamConnectorTests()
    {
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Dispose() => _listener.Stop();

    [Fact]
    public async Task Connects_to_resolved_address_with_nagle_off()
    {
        var connector = new TcpUpstreamConnector(_resolver);

        await using var stream = await connector.ConnectAsync(Host, _port, CancellationToken.None);
        using var accepted = await _listener.AcceptTcpClientAsync();
        await stream.WriteAsync(Ping);
        var got = new byte[1];
        await accepted.GetStream().ReadExactlyAsync(got);

        Assert.Equal(Ping, got);
        Assert.True(Assert.IsType<NetworkStream>(stream).Socket.NoDelay);
        Assert.Equal(Host, Assert.Single(_resolver.Hosts));
    }

    [Fact]
    public async Task Falls_back_to_next_address()
    {
        // 127.0.0.2 không có ai nghe (listener chỉ bind 127.0.0.1); Windows mất khoảng 2 giây mới báo từ chối.
        _resolver.Respond = _ => [IPAddress.Parse("127.0.0.2"), IPAddress.Loopback];

        await using var stream = await new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None);

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task Throws_IOException_when_name_has_no_address()
    {
        _resolver.Respond = _ => [];

        await Assert.ThrowsAsync<IOException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));
    }

    [Fact]
    public async Task Throws_IOException_when_nothing_listens()
    {
        _listener.Stop();

        var ex = await Assert.ThrowsAsync<IOException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));

        Assert.IsType<SocketException>(ex.InnerException);
    }

    [Fact]
    public async Task Lets_resolver_failure_through()
    {
        _resolver.Respond = _ => throw new DohException("DoH chết");

        await Assert.ThrowsAsync<DohException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));
    }
}

/// <summary>B9: IP đầu bị chặn ngầm (treo) không được ăn hết ngân sách của bên gọi.</summary>
public sealed class TcpUpstreamConnectorStaggerTests
{
    private static readonly IPAddress Silent = IPAddress.Parse("10.0.0.1");
    private static readonly IPAddress Alive = IPAddress.Parse("10.0.0.2");

    private readonly FakeTimeProvider _time = new();
    private readonly List<IPAddress> _tried = [];

    private TcpUpstreamConnector Connector(Func<IPAddress, CancellationToken, Task<Stream>> connect) =>
        new(new FakeResolver { Respond = _ => [Silent, Alive] }, _time, TcpUpstreamConnector.DefaultAttemptDelay,
            (endPoint, ct) =>
            {
                lock (_tried) _tried.Add(endPoint.Address);
                return connect(endPoint.Address, ct);
            });

    private int Tried
    {
        get { lock (_tried) return _tried.Count; }
    }

    [Fact]
    public async Task Next_address_starts_after_attempt_delay_and_wins()
    {
        var alive = new MemoryStream();
        var connector = Connector(async (address, ct) =>
        {
            if (address.Equals(Silent)) await Task.Delay(Timeout.Infinite, ct);
            return alive;
        });

        var connect = connector.ConnectAsync("cdn.steamstatic.com", 443, CancellationToken.None);
        await Eventually.TrueAsync(() => Tried == 1);
        _time.Advance(TcpUpstreamConnector.DefaultAttemptDelay);

        Assert.Same(alive, await connect);
        Assert.Equal([Silent, Alive], _tried);
    }

    [Fact]
    public async Task Late_winner_socket_is_closed()
    {
        var late = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateStream = new MemoryStream();
        var connector = Connector((address, _) => address.Equals(Silent) ? late.Task : Task.FromResult<Stream>(new MemoryStream()));

        var connect = connector.ConnectAsync("cdn.steamstatic.com", 443, CancellationToken.None);
        await Eventually.TrueAsync(() => Tried == 1);
        _time.Advance(TcpUpstreamConnector.DefaultAttemptDelay);
        await connect;
        late.SetResult(lateStream);

        await Eventually.TrueAsync(() => !lateStream.CanRead);
    }

    [Fact]
    public async Task Caller_budget_still_bounds_the_whole_race()
    {
        var connector = Connector(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        });
        using var budget = new CancellationTokenSource();

        var connect = connector.ConnectAsync("cdn.steamstatic.com", 443, budget.Token);
        _time.Advance(TcpUpstreamConnector.DefaultAttemptDelay);
        await Eventually.TrueAsync(() => Tried == 2);
        await budget.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
    }
}
