using System.Net;
using System.Text.Json;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Tests.Platform;

namespace LunaDns.Core.Tests.Journal;

/// <summary>
/// Định dạng journal.json phải giữ nguyên qua các bản: máy người dùng có thể còn journal do bản cũ ghi
/// (serializer reflection, tên thuộc tính PascalCase) khi bản mới (serializer sinh sẵn, để publish trimmed) khởi động.
/// </summary>
public sealed class JournalFormatTests : IDisposable
{
    // Journal do bản 0.5.0 ghi (serializer reflection), một mục cho mỗi loại thao tác.
    private const string OldJournal = """
        [
          {
            "Id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b",
            "Feature": "dns-routing",
            "Op": "dns-route",
            "Snapshot": {
              "Marker": "LunaDNS 0.5.0",
              "Namespaces": [".steampowered.com", ".steamcommunity.com"],
              "NameServer": "127.0.0.1",
              "SkippedNamespaces": []
            },
            "AppliedAt": "2026-11-14T09:00:00+00:00"
          },
          {
            "Id": "7a2b3c4d-5e6f-4a1b-9c2d-3e4f5a6b7c8d",
            "Feature": "sni-proxy",
            "Op": "auto-proxy",
            "Snapshot": {
              "UserSid": "",
              "HadPreviousValue": false,
              "PreviousValue": null,
              "PreviousWasExpandString": false,
              "NewValue": "http://127.0.0.1:18080/proxy.pac",
              "Services": [{ "Service": "Wi-Fi", "PreviousUrl": null, "PreviousEnabled": false }]
            },
            "AppliedAt": "2026-11-14T09:01:00+00:00"
          },
          {
            "Id": "8b3c4d5e-6f7a-4b2c-8d3e-4f5a6b7c8d9e",
            "Feature": "dns-routing",
            "Op": "hosts-entries",
            "Snapshot": { "Lines": ["127.0.0.1 store.steampowered.com"], "LineEnding": "\n", "AddedNewline": true },
            "AppliedAt": "2026-11-14T09:02:00+00:00"
          }
        ]
        """;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-test-").FullName;
    private string JournalPath => Path.Combine(_dir, "journal.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Journal_written_by_old_version_loads_and_rolls_back()
    {
        File.WriteAllText(JournalPath, OldJournal);
        var configurator = new FakeConfigurator();
        var journal = new ChangeJournal(new JsonFileJournalStore(JournalPath), SystemChanges.Reverters(configurator));

        Assert.Equal(3, journal.Entries.Count);
        var result = journal.RollbackAll();

        Assert.Equal(3, result.Reverted);
        Assert.Empty(result.Failed);
        Assert.Equal(
            ["revert:hosts:127.0.0.1 store.steampowered.com", "flush", "revert:proxy:http://127.0.0.1:18080/proxy.pac", "revert:dns:LunaDNS 0.5.0", "flush"],
            configurator.Calls);
    }

    [Fact]
    public void Old_snapshots_keep_every_field()
    {
        var entries = Load(OldJournal);

        var dns = entries[0].Snapshot.Deserialize(SnapshotJsonContext.Default.DnsRouteSnapshot)!;
        Assert.Equal([".steampowered.com", ".steamcommunity.com"], dns.Namespaces);
        Assert.Equal("127.0.0.1", dns.NameServer);
        Assert.Empty(dns.SkippedNamespaces);

        var proxy = entries[1].Snapshot.Deserialize(SnapshotJsonContext.Default.AutoProxySnapshot)!;
        Assert.Equal(new ServiceAutoProxy("Wi-Fi", null, false), Assert.Single(proxy.Services!));
        Assert.Null(proxy.PreviousValue);

        var hosts = entries[2].Snapshot.Deserialize(SnapshotJsonContext.Default.HostsSnapshot)!;
        Assert.Equal("\n", hosts.LineEnding);
        Assert.True(hosts.AddedNewline);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 9, 2, 0, TimeSpan.Zero), entries[2].AppliedAt);
    }

    [Fact]
    public void Journal_file_matches_reflection_serializer_output()
    {
        // Bản mới ghi file mà bản cũ (reflection) cũng đọc được: so với chính đầu ra của serializer reflection.
        var configurator = new FakeConfigurator();
        var store = new JsonFileJournalStore(JournalPath);
        var journal = new ChangeJournal(store, SystemChanges.Reverters(configurator));
        var changes = new SystemChanges(journal, configurator);
        var dns = changes.AddDnsRoute("dns-routing", ["steampowered.com"], IPAddress.Loopback);
        var proxy = changes.SetAutoProxy("sni-proxy", new Uri("http://127.0.0.1:18080/proxy.pac"));
        var hosts = changes.AddHostsEntries("dns-routing", [new HostsEntry("127.0.0.1", "store.steampowered.com")]);

        var written = journal.Entries;
        object[] snapshots = [dns, proxy, hosts];
        for (var i = 0; i < snapshots.Length; i++)
            Assert.Equal(JsonSerializer.Serialize(snapshots[i], snapshots[i].GetType()), written[i].Snapshot.GetRawText());
        var reflection = JsonSerializer.Serialize(written, Indented);
        Assert.Equal(reflection, File.ReadAllText(JournalPath));
    }

    private List<JournalEntry> Load(string json)
    {
        File.WriteAllText(JournalPath, json);
        return [.. new JsonFileJournalStore(JournalPath).Load()];
    }
}
