using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public sealed class CustomDohBootstrapTests
{
    private sealed class FakeResolver(Func<string, IReadOnlyList<IPAddress>> answer) : IHostResolver
    {
        public List<string> Hosts { get; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Hosts.Add(host);
            return Task.FromResult(answer(host));
        }
    }

    [Fact]
    public async Task Manual_addresses_skip_the_resolver()
    {
        var resolver = new FakeResolver(_ => throw new InvalidOperationException());
        var custom = new CustomDoh { Endpoint = "https://dns.example.net/q", ManualAddresses = ["1.2.3.4"] };

        Assert.Same(custom, await CustomDohBootstrap.ResolveAsync(custom, resolver, CancellationToken.None));
        Assert.Empty(resolver.Hosts);
    }

    [Fact]
    public async Task Ip_literal_host_skips_the_resolver()
    {
        var resolver = new FakeResolver(_ => throw new InvalidOperationException());
        var custom = new CustomDoh { Endpoint = "https://9.9.9.9/dns-query" };

        Assert.Same(custom, await CustomDohBootstrap.ResolveAsync(custom, resolver, CancellationToken.None));
    }

    [Fact]
    public async Task Resolved_addresses_replace_old_ones()
    {
        var resolver = new FakeResolver(_ => [IPAddress.Parse("5.6.7.8"), IPAddress.Parse("5.6.7.9")]);
        var custom = new CustomDoh { Endpoint = "https://dns.example.net:8443/q", ResolvedAddresses = ["1.1.1.1"] };

        var resolved = await CustomDohBootstrap.ResolveAsync(custom, resolver, CancellationToken.None);

        Assert.Equal(["5.6.7.8", "5.6.7.9"], resolved.ResolvedAddresses);
        Assert.Equal(["dns.example.net"], resolver.Hosts);
        Assert.Equal(custom.Endpoint, resolved.Endpoint);
    }

    [Fact]
    public async Task Empty_answer_throws_doh_exception()
    {
        var custom = new CustomDoh { Endpoint = "https://dns.example.net/q" };

        await Assert.ThrowsAsync<DohException>(() => CustomDohBootstrap.ResolveAsync(custom, new FakeResolver(_ => []), CancellationToken.None));
    }

    [Fact]
    public async Task Bad_url_throws_argument_exception()
    {
        var custom = new CustomDoh { Endpoint = "http://dns.example.net/q" };

        await Assert.ThrowsAsync<ArgumentException>(() => CustomDohBootstrap.ResolveAsync(custom, new FakeResolver(_ => []), CancellationToken.None));
    }
    [Fact]
    public void Failed_resolve_of_a_new_url_keeps_the_old_settings()
    {
        var old = new CustomDoh { Endpoint = "https://old.example.net/q", ResolvedAddresses = ["1.2.3.4"] };

        var draft = CustomDohBootstrap.Draft(old, "https://new.example.net/q", []);

        Assert.Empty(draft.ResolvedAddresses);
        Assert.Null(CustomDohBootstrap.AfterFailedResolve(old, draft));
    }

    [Fact]
    public void Failed_resolve_of_the_same_url_saves_the_draft_with_old_addresses()
    {
        var old = new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] };

        var draft = CustomDohBootstrap.Draft(old, "https://dns.example.net/q", []);

        Assert.Equal(["1.2.3.4"], draft.ResolvedAddresses);
        Assert.Same(draft, CustomDohBootstrap.AfterFailedResolve(old, draft));
    }

    [Fact]
    public void Failed_resolve_with_no_old_settings_saves_nothing()
    {
        var draft = CustomDohBootstrap.Draft(null, "https://new.example.net/q", []);

        Assert.Null(CustomDohBootstrap.AfterFailedResolve(null, draft));
    }
}
