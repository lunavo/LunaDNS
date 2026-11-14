using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.Platform.Windows.Tests.Nrpt;

internal sealed class FakeNrptStore : INrptStore
{
    public List<NrptRule> Rules { get; } = [];
    public int AddCalls { get; private set; }
    public bool FailAdd { get; set; }
    public bool FailList { get; set; }

    public IReadOnlyList<NrptRule> List() =>
        FailList ? throw new ConfiguratorException(ConfiguratorError.NrptFailed, "giả lập lỗi đọc NRPT") : [.. Rules];

    public void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName)
    {
        AddCalls++;
        if (FailAdd) throw new ConfiguratorException(ConfiguratorError.NrptFailed, "giả lập lỗi");
        Rules.Add(new NrptRule("{" + Guid.NewGuid() + "}", [.. namespaces], [nameServer], comment, displayName));
    }

    public void Remove(string name) => Rules.RemoveAll(r => r.Name == name);

    /// <summary>Giống luật Tailscale đo trên máy PO: GUID cố định, không Comment/DisplayName.</summary>
    public static NrptRule Tailscale(params string[] extraNamespaces) => new(
        "{5abe529b-675b-4486-8459-25a634dacc23}",
        [".100.100.in-addr.arpa", ".tail1234.ts.net", .. extraNamespaces],
        ["100.100.100.100", "fd7a:115c:a1e0::53"],
        null,
        null);
}
