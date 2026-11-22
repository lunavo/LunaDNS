using System.Text.Json;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Settings;
using LunaDns.Platform.MacOS.Daemon;
using LunaDns.Platform.MacOS.Hosts;
using LunaDns.Platform.MacOS.Proxy;
using LunaDns.Platform.MacOS.Resolver;

namespace LunaDns.Platform.MacOS.Tests;

/// <summary>F1 phía daemon: lệnh tường minh ghi wanted, disable-all lúc hết phiên giữ wanted, set-auto-resume, dạng dây.</summary>
public sealed class DaemonResumeTests : IAsyncLifetime
{
    private readonly TempDirectory _tmp = new();
    private readonly FakeNetworkSetup _ns = new();
    private FeatureSet _features = null!;
    private ChangeJournal _journal = null!;
    private DomainListService _domainList = null!;
    private SettingsHolder _settings = null!;
    private FeatureResumer _resumer = null!;
    private DaemonCommands _commands = null!;

    public Task InitializeAsync()
    {
        File.WriteAllText(_tmp.Combine("hosts"), "127.0.0.1 localhost\n");
        var cfg = new MacNetworkConfigurator(new ResolverDnsRouter(15353, _tmp.Combine("resolver")), new NetworkSetupAutoProxy(_ns),
            new UnixHostsFileStore(_tmp.Combine("hosts")), _ns);
        _journal = new ChangeJournal(new JsonFileJournalStore(_tmp.Combine("journal.json")), SystemChanges.Reverters(cfg));
        _features = new FeatureSet(_journal, new SystemChanges(_journal, cfg), new DaemonCommandsTests.IdleServices());
        _domainList = DaemonHarness.Idle(_features, _tmp.Path);
        _settings = new SettingsHolder(new JsonFileSettingsStore(_tmp.Combine("settings.json")));
        _resumer = new FeatureResumer(_features, () => _settings.Current, _settings.Save, [TimeSpan.Zero]);
        _commands = new DaemonCommands(_features, _journal, "1.2.3", _settings, _domainList, resumer: _resumer);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _features.DisposeAsync();
        _tmp.Dispose();
    }

    private Task<DaemonResponse> Send(DaemonRequest request) => _commands.HandleAsync(request, CancellationToken.None);

    private WantedFeatures Saved() => new JsonFileSettingsStore(_tmp.Combine("settings.json")).Load().Wanted!;

    [Fact]
    public async Task Enable_and_disable_record_wanted_in_settings_json()
    {
        var on = await Send(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting));
        Assert.Equal(new DaemonResume(true, true, false, false), on.Resume);
        await Send(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.SniProxy));
        Assert.Equal(new WantedFeatures(true, true), Saved());

        await Send(new DaemonRequest(DaemonProtocol.Disable, FeatureKeys.SniProxy));
        Assert.Equal(new WantedFeatures(true, false), Saved());
    }

    [Fact]
    public async Task Enable_waiting_for_proxy_override_keeps_wanted()
    {
        _ns.Auto["Luna Wifi"] = ("http://corp/proxy.pac", true);

        var first = await Send(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.SniProxy));

        Assert.Equal("NeedsProxyOverride", first.Outcome);
        Assert.Equal(WantedFeatures.None, Saved());
    }

    [Fact]
    public async Task Disable_all_forgets_but_session_end_keeps_wanted()
    {
        await Send(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting));

        var sessionEnd = await Send(new DaemonRequest(DaemonProtocol.DisableAll, SessionEnd: true));
        Assert.False(sessionEnd.Status!.DnsRouting);
        Assert.Equal(new WantedFeatures(true, false), Saved());

        await Send(new DaemonRequest(DaemonProtocol.DisableAll));
        Assert.Equal(WantedFeatures.None, Saved());
    }

    [Fact]
    public async Task Restart_after_session_end_turns_wanted_back_on()
    {
        await Send(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting));
        await _features.ShutdownAsync(); // SIGTERM: tắt hết, giữ wanted

        await _resumer.RunAsync(rollbackClean: true);

        var status = await Send(new DaemonRequest(DaemonProtocol.Status));
        Assert.True(status.Status!.DnsRouting);
        Assert.Equal(new DaemonResume(true, true, false, false), status.Resume);
    }

    [Fact]
    public async Task Set_auto_resume_saves_the_switch()
    {
        var off = await Send(new DaemonRequest(DaemonProtocol.SetAutoResume, Enabled: false));

        Assert.True(off.Ok);
        Assert.False(off.Resume!.Enabled);
        Assert.False(new JsonFileSettingsStore(_tmp.Combine("settings.json")).Load().AutoResume);
        Assert.False((await Send(new DaemonRequest(DaemonProtocol.SetAutoResume))).Ok);
    }

    [Fact]
    public async Task Run_mode_without_resumer_has_no_resume_and_refuses_the_switch()
    {
        var run = new DaemonCommands(_features, _journal, "run", _settings, _domainList);

        var on = await run.HandleAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting), CancellationToken.None);

        Assert.Null(on.Resume);
        Assert.Equal(WantedFeatures.None, Saved());
        Assert.False((await run.HandleAsync(new DaemonRequest(DaemonProtocol.SetAutoResume, Enabled: false), CancellationToken.None)).Ok);
    }

    [Fact]
    public void Resume_wire_format_is_stable_and_round_trips_failures()
    {
        var state = new ResumeState(false, new WantedFeatures(true, true), true,
            new FeatureResult(Feature.SniProxy, FeatureOutcome.NeedsProxyOverride));
        var wire = DaemonResume.From(state);

        Assert.Equal("""{"enabled":false,"dnsRouting":true,"sniProxy":true,"restoring":true,"failedFeature":"sni-proxy","outcome":"NeedsProxyOverride"}""",
            JsonSerializer.Serialize(wire, DaemonProtocol.Json));
        Assert.Equal(state, wire.ToState());
        Assert.Equal("""{"command":"disable-all","allowProxyOverride":false,"sessionEnd":true}""",
            JsonSerializer.Serialize(new DaemonRequest(DaemonProtocol.DisableAll, SessionEnd: true), DaemonProtocol.Json));

        var failed = new DaemonResume(true, true, false, false, FeatureKeys.DnsRouting, "Failed", "DnsPortBusy").ToState();
        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.DnsPortBusy), failed.Failure);
        Assert.Null(new DaemonResume(true, false, false, false, "lạ").ToState().Failure);
    }
}
