using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public sealed class DohCatalogTests
{
    private static string Names(IEnumerable<DohServer> servers) => string.Join(",", servers.Select(s => s.Name));

    [Fact]
    public void Built_in_order_is_cloudflare_google_quad9_adguard()
    {
        Assert.Equal([DohProvider.Cloudflare, DohProvider.Google, DohProvider.Quad9, DohProvider.AdGuard],
            DohCatalog.BuiltIn.Select(c => c.Provider));
        Assert.Equal(new Uri("https://dns.quad9.net/dns-query"), DohServer.Quad9.Endpoint);
        Assert.Equal(new Uri("https://unfiltered.adguard-dns.com/dns-query"), DohServer.AdGuard.Endpoint);
    }

    [Theory]
    [InlineData(DohProvider.Cloudflare, "Cloudflare,Google,Quad9")]
    [InlineData(DohProvider.Google, "Google,Cloudflare,Quad9")]
    [InlineData(DohProvider.Quad9, "Quad9,Cloudflare,Google")]
    [InlineData(DohProvider.AdGuard, "AdGuard,Cloudflare,Google")]
    public void Chain_is_primary_then_first_two_other_built_ins(DohProvider provider, string expected) =>
        Assert.Equal(expected, Names(DohCatalog.Chain(DohCatalog.Get(provider))));

    [Fact]
    public void Chain_for_custom_puts_it_first()
    {
        var custom = CustomDohParser.ToServer(new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] })!;

        Assert.Equal("Tự nhập,Cloudflare,Google", Names(DohCatalog.Chain(custom)));
    }

    [Fact]
    public void Chain_drops_built_in_with_same_host_as_custom()
    {
        var custom = CustomDohParser.ToServer(new CustomDoh { Endpoint = "https://CLOUDFLARE-DNS.com/dns-query", ManualAddresses = ["1.1.1.1"] })!;

        Assert.Equal("Tự nhập,Google,Quad9", Names(DohCatalog.Chain(custom)));
    }

    [Fact]
    public void Get_rejects_custom() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DohCatalog.Get(DohProvider.Custom));

    [Fact]
    public void Choices_add_custom_only_when_it_has_an_address()
    {
        Assert.Equal(4, DohCatalog.Choices(null).Count);
        Assert.Equal(4, DohCatalog.Choices(new CustomDoh { Endpoint = "https://dns.example.net/q" }).Count);

        var choices = DohCatalog.Choices(new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] });
        Assert.Equal(DohProvider.Custom, choices[^1].Provider);
        Assert.Equal(5, choices.Count);
    }
}
