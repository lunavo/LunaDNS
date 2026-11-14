using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Tests.Dns;

namespace LunaDns.Core.Tests.Probing;

public class ProberTests
{
    private const string Host = "store.steampowered.com";
    private static readonly IPAddress Real = IPAddress.Parse("23.50.60.70");
    private static readonly IPAddress OtherEdge = IPAddress.Parse("118.69.10.20");
    private static readonly IPAddress Fake = IPAddress.Parse("203.0.113.9");
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Forever = TimeSpan.FromSeconds(30);

    private readonly FakeResolver _system = new() { Respond = _ => [Real] };
    private readonly FakeResolver _doh = new() { Respond = _ => [Real] };
    private readonly FakeTlsProbe _tls = new();

    private Prober Create(TimeSpan? checkTimeout = null) => new(_system, _doh, _tls, checkTimeout);

    private void Tls(Func<IPAddress, bool, bool> answer) =>
        _tls.RespondAsync = (address, _, split, _) => Task.FromResult(answer(address, split));

    [Fact]
    public async Task Ok_when_system_dns_agrees_with_doh()
    {
        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.Ok, result.Verdict);
        Assert.Equal(Host, result.Host);
        Assert.Equal(Real, Assert.Single(result.SystemAddresses));
        Assert.Equal(Real, Assert.Single(result.DohAddresses));
        Assert.Equal((Real, Host, false), Assert.Single(_tls.Calls));
    }

    [Fact]
    public async Task Ok_when_system_ip_differs_but_presents_valid_certificate()
    {
        _system.Respond = _ => [OtherEdge];

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.Ok, result.Verdict);
        Assert.Contains((OtherEdge, Host, false), _tls.Calls);
    }

    [Fact]
    public async Task DnsPoisoned_when_system_ip_fails_tls()
    {
        _system.Respond = _ => [Fake];
        Tls((address, _) => address.Equals(Real));

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.DnsPoisoned, result.Verdict);
        Assert.False(result.SystemAddressBlocked);
    }

    [Fact]
    public async Task DnsPoisoned_without_handshake_when_system_ip_is_a_block_address()
    {
        _system.Respond = _ => [IPAddress.Loopback];

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.DnsPoisoned, result.Verdict);
        Assert.True(result.SystemAddressBlocked);
        Assert.DoesNotContain(_tls.Calls, call => call.Address.Equals(IPAddress.Loopback));
    }

    [Fact]
    public async Task DnsPoisoned_when_system_dns_returns_nothing()
    {
        _system.Respond = _ => [];

        Assert.Equal(ProbeVerdict.DnsPoisoned, (await Create().ProbeAsync(Host, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task SniFiltered_when_only_split_handshake_works()
    {
        Tls((_, split) => split);

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.SniFiltered, result.Verdict);
        (IPAddress, string, bool)[] expected = [(Real, Host, false), (Real, Host, true)];
        Assert.Equal(expected, _tls.Calls);
    }

    [Fact]
    public async Task Unreachable_when_both_handshakes_fail()
    {
        Tls((_, _) => false);

        Assert.Equal(ProbeVerdict.Unreachable, (await Create().ProbeAsync(Host, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task Unreachable_without_handshake_when_doh_has_no_address()
    {
        _doh.Respond = _ => [];

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.Unreachable, result.Verdict);
        Assert.Empty(_tls.Calls);
    }

    [Fact]
    public async Task Unreachable_when_doh_fails()
    {
        _doh.Respond = _ => throw new DohException("mọi máy chủ DoH đều lỗi");

        Assert.Equal(ProbeVerdict.Unreachable, (await Create().ProbeAsync(Host, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task Handshake_that_exceeds_check_timeout_counts_as_failure()
    {
        _tls.RespondAsync = async (_, _, _, ct) =>
        {
            await Task.Delay(Forever, ct);
            return true;
        };

        Assert.Equal(ProbeVerdict.Unreachable, (await Create(Short).ProbeAsync(Host, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task System_dns_that_exceeds_check_timeout_counts_as_no_answer()
    {
        _system.Delay = Forever;

        var result = await Create(Short).ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.DnsPoisoned, result.Verdict);
        Assert.Empty(result.SystemAddresses);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        _tls.RespondAsync = async (_, _, _, ct) =>
        {
            await Task.Delay(Forever, ct);
            return true;
        };
        using var cts = new CancellationTokenSource(Short);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().ProbeAsync(Host, cts.Token));
    }

    [Fact]
    public async Task RunAsync_probes_hosts_in_parallel_and_keeps_order()
    {
        string[] hosts = ["a.steampowered.com", "b.steampowered.com", "c.steampowered.com"];
        var entered = 0;
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Mỗi lần bắt tay chờ tới khi cả ba host cùng vào. Nếu Prober chạy tuần tự thì lần đầu sẽ quá hạn và verdict khác Ok.
        _tls.RespondAsync = async (_, _, _, ct) =>
        {
            if (Interlocked.Increment(ref entered) == hosts.Length) allEntered.TrySetResult();
            await allEntered.Task.WaitAsync(ct);
            return true;
        };

        var results = await Create(TimeSpan.FromSeconds(2)).RunAsync(hosts, CancellationToken.None);

        Assert.Equal(hosts, results.Select(r => r.Host));
        Assert.All(results, r => Assert.Equal(ProbeVerdict.Ok, r.Verdict));
    }
}
