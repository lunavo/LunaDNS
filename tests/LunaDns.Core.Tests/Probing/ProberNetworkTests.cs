using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using Xunit.Abstractions;

namespace LunaDns.Core.Tests.Probing;

public class ProberNetworkTests(ITestOutputHelper output)
{
    [NetworkFact]
    public async Task Classifies_real_hosts_on_this_network()
    {
        using var doh = DohClient.Create(DohServer.Defaults);
        var prober = new Prober(new SystemHostResolver(), new DohHostResolver(doh), new TlsProbe());

        var results = await prober.RunAsync(ProbeTargets.For(includePubg: true), CancellationToken.None);

        foreach (var r in results)
        {
            output.WriteLine($"{r.Host}: {r.Verdict} | DNS hệ thống: {string.Join(", ", r.SystemAddresses)} | DoH: {string.Join(", ", r.DohAddresses)}");
        }
        output.WriteLine($"Đề xuất: {ProbeAdvisor.Recommend(results)}");
        Assert.Equal(5, results.Count);
        // Chỉ đòi DoH trả được IP cho 4 host Steam. Verdict phụ thuộc nhà mạng nên chỉ in ra, không assert.
        Assert.All(results.Take(4), r => Assert.NotEmpty(r.DohAddresses));
    }
}
