using System.Security.Cryptography;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Settings;
using LunaDns.Platform.MacOS.Daemon;
using LunaDns.Platform.MacOS.Hosts;
using LunaDns.Platform.MacOS.Resolver;
using LunaDns.Platform.MacOS.Proxy;

namespace LunaDns.Platform.MacOS.Tests;

/// <summary>FeatureSet + SettingsHolder + DomainListService + DaemonCommands dựng như Engine của daemon.</summary>
internal sealed class DaemonHarness : IAsyncDisposable
{
    private readonly TempDirectory _tmp;

    private DaemonHarness(TempDirectory tmp) => _tmp = tmp;

    public FeatureSet Features { get; private set; } = null!;

    public SettingsHolder Settings { get; private set; } = null!;

    public string SettingsPath { get; private set; } = null!;

    public DomainListService DomainList { get; private set; } = null!;

    public DaemonCommands Commands { get; private set; } = null!;

    public static DaemonHarness Create(TempDirectory tmp, IReadOnlyDictionary<string, string> keys)
    {
        var h = new DaemonHarness(tmp);
        File.WriteAllText(tmp.Combine("hosts"), "127.0.0.1 localhost\n");
        var ns = new FakeNetworkSetup();
        var cfg = new MacNetworkConfigurator(new ResolverDnsRouter(15353, tmp.Combine("resolver")), new NetworkSetupAutoProxy(ns),
            new UnixHostsFileStore(tmp.Combine("hosts")), ns);
        var journal = new ChangeJournal(new JsonFileJournalStore(tmp.Combine("journal.json")), SystemChanges.Reverters(cfg));
        h.Features = new FeatureSet(journal, new SystemChanges(journal, cfg), new DaemonCommandsTests.IdleServices());
        h.SettingsPath = tmp.Combine("settings.json");
        h.Settings = new SettingsHolder(new JsonFileSettingsStore(h.SettingsPath));
        h.DomainList = new DomainListService(new DomainListStore(tmp.Combine("domain-list.json"), keys), keys, true,
            () => h.Settings.Current.Domains, (d, ct) => h.Features.ReapplyAsync(d, ct));
        h.Commands = new DaemonCommands(h.Features, journal, "test", h.Settings, h.DomainList);
        return h;
    }

    /// <summary>Service riêng cho test cũ chỉ cần DaemonCommands chạy được: khoá rỗng, lớp 2 không bao giờ có.</summary>
    public static DomainListService Idle(FeatureSet features, string directory) =>
        new(new DomainListStore(Path.Combine(directory, "domain-list.json"), new Dictionary<string, string>()),
            new Dictionary<string, string>(), true, () => new DomainSelection(), (d, ct) => features.ReapplyAsync(d, ct));

    public async ValueTask DisposeAsync()
    {
        await Features.DisposeAsync();
    }
}

public sealed class DaemonDomainListTests : IAsyncLifetime
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly IReadOnlyDictionary<string, string> Keys =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["t"] = DomainListSigning.PublicKey(Key) };

    private readonly TempDirectory _tmp = new();
    private DaemonHarness _h = null!;

    public Task InitializeAsync() { _h = DaemonHarness.Create(_tmp, Keys); return Task.CompletedTask; }

    public async Task DisposeAsync() { await _h.DisposeAsync(); _tmp.Dispose(); }

    private static string Envelope(long serial, params string[] steam) =>
        Convert.ToBase64String(DomainListSigning.Sign(new DomainListSource(new() { ["steam"] = [.. steam] }), serial, DateTimeOffset.UnixEpoch, Key, "t"));

    private Task<DaemonResponse> Send(DaemonRequest r) => _h.Commands.HandleAsync(r, CancellationToken.None);

    [Fact]
    public async Task Set_domain_list_verifies_applies_and_reports_state()
    {
        var set = await Send(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: Envelope(5, "cdn.example.net")));
        Assert.True(set.Ok);
        Assert.Equal("Accepted", set.DomainListResult!.Outcome);

        var get = await Send(new DaemonRequest(DaemonProtocol.GetDomainList));
        Assert.Equal(5, get.DomainList!.Serial);
        Assert.Equal(["cdn.example.net"], get.DomainList.Added["steam"]);
    }

    [Fact]
    public async Task Tampered_envelope_is_rejected_by_the_daemon()
    {
        var bytes = Convert.FromBase64String(Envelope(5, "cdn.example.net"));
        bytes[^5] ^= 1;
        var set = await Send(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: Convert.ToBase64String(bytes)));
        Assert.False(set.Ok);
        Assert.NotEqual("Accepted", set.DomainListResult!.Outcome);
    }

    [Fact]
    public async Task Missing_or_bad_base64_is_refused()
    {
        Assert.False((await Send(new DaemonRequest(DaemonProtocol.SetDomainList))).Ok);
        Assert.False((await Send(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: "%%%"))).Ok);
    }

    [Fact]
    public async Task Switch_is_saved_without_touching_domains_or_tuning()
    {
        var before = _h.Settings.Current;
        var off = await Send(new DaemonRequest(DaemonProtocol.SetDomainListEnabled, Enabled: false));

        Assert.True(off.Ok);
        var saved = new JsonFileSettingsStore(_h.SettingsPath).Load();
        Assert.False(saved.DomainListUpdates);
        Assert.Equal(before.Domains, saved.Domains);
        Assert.Equal(DaemonTuning.From(before), DaemonTuning.From(saved));
        Assert.False((await Send(new DaemonRequest(DaemonProtocol.GetDomainList))).DomainList!.Enabled);
    }

    [Fact]
    public async Task Set_domains_uses_layer_two()
    {
        await Send(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: Envelope(5, "cdn.example.net")));
        await Send(new DaemonRequest(DaemonProtocol.SetDomains, Domains: new DomainSelection()));
        Assert.Contains("cdn.example.net", _h.Features.RoutedDomains);
    }

    [Fact]
    public void Max_line_holds_a_full_size_list() =>
        Assert.True(DaemonProtocol.MaxLineBytes >= 4 * (DomainListVerifier.MaxBytes / 3 + 1) + 1024);
}
