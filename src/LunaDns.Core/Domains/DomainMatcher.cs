namespace LunaDns.Core.Domains;

public static class DomainMatcher
{
    public static string Normalize(string host) =>
        host.Trim().TrimEnd('.').ToLowerInvariant();

    public static bool Matches(string host, IEnumerable<string> domains)
    {
        var h = Normalize(host);
        if (h.Length == 0) return false;
        foreach (var d in domains)
        {
            if (h == d) return true;
            if (h.Length > d.Length && h.EndsWith(d, StringComparison.Ordinal) && h[h.Length - d.Length - 1] == '.')
                return true;
        }
        return false;
    }
}
