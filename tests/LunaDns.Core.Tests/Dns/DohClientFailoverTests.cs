using System.Net;
using LunaDns.Core.Dns;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Dns;

/// <summary>B3/F4: chạy đua so le giữa các máy DoH, nhớ máy chết trong Cooldown, về máy chính sau đó.</summary>
public sealed class DohClientFailoverTests
{
    private const string Name = "store.steampowered.com";
    private static readonly byte[] StoreQuery = TestDnsMessage.Query(Name);
    private static readonly TimeSpan Hedge = DohClient.DefaultHedgeDelay;

    private readonly FakeTimeProvider _time = new();

    private static byte[] Answer() => TestDnsMessage.Response(Name, 0, 300);

    /// <summary>Máy theo tên: "hang" (treo tới khi bị huỷ), "ok", "fail" (HTTP 503 ngay). Ghi lại thứ tự host được hỏi.</summary>
    private sealed class Network
    {
        public Dictionary<string, string> Behaviour { get; } = new()
        {
            [DohServer.Cloudflare.Endpoint.Host] = "ok",
            [DohServer.Google.Endpoint.Host] = "ok",
            [DohServer.Quad9.Endpoint.Host] = "ok",
        };

        public List<string> Hosts { get; } = [];

        public FakeHttpHandler Handler() => new(async (request, ct) =>
        {
            var host = request.RequestUri!.Host;
            lock (Hosts) Hosts.Add(host);
            switch (Behaviour[host])
            {
                case "hang":
                    await Task.Delay(Timeout.Infinite, ct);
                    throw new InvalidOperationException();
                case "fail":
                    return FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable);
                default:
                    return FakeHttpHandler.DnsMessage(Answer());
            }
        });

        public int Count
        {
            get { lock (Hosts) return Hosts.Count; }
        }

        public string[] Snapshot()
        {
            lock (Hosts) return [.. Hosts];
        }
    }

    private DohClient Client(Network network, IReadOnlyList<DohServer>? servers = null) =>
        new(network.Handler(), servers ?? [DohServer.Cloudflare, DohServer.Google, DohServer.Quad9], time: _time);

    private static string CF => DohServer.Cloudflare.Endpoint.Host;
    private static string Google => DohServer.Google.Endpoint.Host;
    private static string Quad9 => DohServer.Quad9.Endpoint.Host;

    [Fact]
    public async Task Silent_primary_gets_hedged_after_delay_and_is_skipped_next_time()
    {
        var network = new Network { Behaviour = { [CF] = "hang" } };
        using var client = Client(network);

        var first = client.QueryAsync(StoreQuery, CancellationToken.None);
        Assert.Equal([CF], network.Snapshot());
        _time.Advance(Hedge);
        Assert.Equal(Answer(), await first);
        Assert.Equal([CF, Google], network.Snapshot());

        // Cloudflare đang nghỉ: truy vấn kế đi thẳng Google, không chờ.
        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal([CF, Google, Google], network.Snapshot());
        Assert.Equal(new DohActivity("Google", Fallback: true), client.Activity);
    }

    [Fact]
    public async Task Primary_is_tried_first_again_after_cooldown()
    {
        var network = new Network { Behaviour = { [CF] = "hang" } };
        using var client = Client(network);
        var first = client.QueryAsync(StoreQuery, CancellationToken.None);
        _time.Advance(Hedge);
        await first;

        network.Behaviour[CF] = "ok";
        _time.Advance(DohClient.DefaultCooldown - TimeSpan.FromSeconds(1));
        await client.QueryAsync(StoreQuery, CancellationToken.None);
        Assert.Equal(Google, network.Snapshot()[^1]);

        _time.Advance(TimeSpan.FromSeconds(1));
        await client.QueryAsync(StoreQuery, CancellationToken.None);
        Assert.Equal(CF, network.Snapshot()[^1]);
        Assert.Equal(new DohActivity("Cloudflare", Fallback: false), client.Activity);
    }

    [Fact]
    public async Task Fast_failure_moves_on_without_waiting_for_hedge()
    {
        var network = new Network { Behaviour = { [CF] = "fail" } };
        using var client = Client(network);

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal([CF, Google], network.Snapshot());
    }

    [Fact]
    public async Task Server_started_after_the_winner_is_not_marked_down()
    {
        // Cloudflare treo, Google treo: Quad9 thắng. Google mở trước Quad9 nên bị đánh dấu; Cloudflare cũng vậy.
        // Lần sau chỉ Quad9 khoẻ → hỏi Quad9 trước.
        var network = new Network { Behaviour = { [CF] = "hang", [Google] = "hang" } };
        using var client = Client(network);
        var first = client.QueryAsync(StoreQuery, CancellationToken.None);
        _time.Advance(Hedge);
        await Eventually.TrueAsync(() => network.Count == 2);
        _time.Advance(Hedge);
        await first;
        Assert.Equal([CF, Google, Quad9], network.Snapshot());

        await client.QueryAsync(StoreQuery, CancellationToken.None);
        Assert.Equal(Quad9, network.Snapshot()[^1]);
    }

    [Fact]
    public async Task Late_starter_losing_to_primary_stays_healthy()
    {
        // Cloudflare chậm hơn HedgeDelay nhưng vẫn thắng Google (Google treo): Google mở sau máy thắng nên không bị đánh dấu,
        // và lần sau Cloudflare vẫn được hỏi trước.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hosts = new List<string>();
        var handler = new FakeHttpHandler(async (request, ct) =>
        {
            var host = request.RequestUri!.Host;
            lock (hosts) hosts.Add(host);
            if (host == Google || (host == CF && hosts.Count == 1)) await gate.Task.WaitAsync(ct);
            if (host == Google) await Task.Delay(Timeout.Infinite, ct);
            return FakeHttpHandler.DnsMessage(Answer());
        });
        using var client = new DohClient(handler, [DohServer.Cloudflare, DohServer.Google], time: _time);

        var first = client.QueryAsync(StoreQuery, CancellationToken.None);
        _time.Advance(Hedge);
        await Eventually.TrueAsync(() => { lock (hosts) return hosts.Count == 2; });
        gate.SetResult();
        await first;

        await client.QueryAsync(StoreQuery, CancellationToken.None);
        lock (hosts) Assert.Equal([CF, Google, CF], hosts);
        Assert.Equal(new DohActivity("Cloudflare", Fallback: false), client.Activity);
    }

    [Fact]
    public async Task Per_server_timeout_still_applies_when_alone()
    {
        var network = new Network { Behaviour = { [CF] = "hang" } };
        using var client = Client(network, [DohServer.Cloudflare]);

        var query = client.QueryAsync(StoreQuery, CancellationToken.None);
        _time.Advance(DohClient.DefaultTimeout);

        await Assert.ThrowsAsync<DohException>(() => query);
    }

    [Fact]
    public async Task Every_server_down_still_tries_all_of_them()
    {
        var network = new Network { Behaviour = { [CF] = "fail", [Google] = "fail", [Quad9] = "fail" } };
        using var client = Client(network);
        await Assert.ThrowsAsync<DohException>(() => client.QueryAsync(StoreQuery, CancellationToken.None));

        await Assert.ThrowsAsync<DohException>(() => client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(6, network.Count);
    }

    [Fact]
    public void Activity_is_null_before_any_answer()
    {
        using var client = Client(new Network());

        Assert.Null(client.Activity);
    }
}
