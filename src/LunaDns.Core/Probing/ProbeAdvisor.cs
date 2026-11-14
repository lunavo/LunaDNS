namespace LunaDns.Core.Probing;

public static class ProbeAdvisor
{
    /// <summary>Gộp verdict của các host thành biện pháp cần bật (cột "Biện pháp đề xuất" ở spec 4.1).</summary>
    public static ProbeRemedies Recommend(IEnumerable<ProbeResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var remedies = ProbeRemedies.None;
        foreach (var result in results)
        {
            remedies |= result.Verdict switch
            {
                ProbeVerdict.DnsPoisoned => ProbeRemedies.DnsRouting,
                ProbeVerdict.SniFiltered => ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy,
                _ => ProbeRemedies.None,
            };
        }
        return remedies;
    }
}
