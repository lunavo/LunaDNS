using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Platform;

/// <summary>
/// Những gì journal còn giữ sau <see cref="ChangeJournal.RollbackAll"/> (mục hoàn tác lỗi, để thử lại): quét dọn không đụng tới
/// (spec F7 mục 3). <see cref="Unknown"/> là các Op có mục mà snapshot không đọc được: không biết mục đó giữ gì nên bỏ qua cả loại.
/// </summary>
public sealed class JournalCoverage
{
    private JournalCoverage(HashSet<string> dns, HashSet<string> pac, HashSet<string> hosts, HashSet<string> unknown)
    {
        DnsMarkers = dns;
        PacUrls = pac;
        HostsLines = hosts;
        Unknown = unknown;
    }

    /// <summary>Marker của định tuyến DNS (dòng đầu file /etc/resolver, Comment của luật NRPT).</summary>
    public IReadOnlySet<string> DnsMarkers { get; }

    /// <summary>URL PAC LunaDNS đã đặt (NewValue).</summary>
    public IReadOnlySet<string> PacUrls { get; }

    /// <summary>Dòng hosts đầy đủ, kèm "# LunaDNS".</summary>
    public IReadOnlySet<string> HostsLines { get; }

    public IReadOnlySet<string> Unknown { get; }

    public bool IsKnown(string op) => !Unknown.Contains(op);

    public static JournalCoverage From(IEnumerable<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        HashSet<string> dns = new(StringComparer.Ordinal), pac = new(StringComparer.Ordinal),
            hosts = new(StringComparer.Ordinal), unknown = new(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            switch (entry.Op)
            {
                case ChangeOps.DnsRoute when Read(entry, SnapshotJsonContext.Default.DnsRouteSnapshot) is { Marker: { } marker }:
                    dns.Add(marker);
                    break;
                case ChangeOps.AutoProxy when Read(entry, SnapshotJsonContext.Default.AutoProxySnapshot) is { NewValue: { } url }:
                    pac.Add(url);
                    break;
                case ChangeOps.HostsEntries when Read(entry, SnapshotJsonContext.Default.HostsSnapshot) is { Lines: { } lines }
                    && lines.All(l => l is not null):
                    hosts.UnionWith(lines);
                    break;
                case ChangeOps.DnsRoute or ChangeOps.AutoProxy or ChangeOps.HostsEntries:
                    unknown.Add(entry.Op);
                    break;
            }
        }
        return new JournalCoverage(dns, pac, hosts, unknown);
    }

    private static T? Read<T>(JournalEntry entry, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (entry.Snapshot.ValueKind != JsonValueKind.Object) return null;
        try
        {
            return entry.Snapshot.Deserialize(typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
