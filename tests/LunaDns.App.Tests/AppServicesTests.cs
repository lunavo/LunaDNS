using LunaDns.App.Features;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class AppServicesTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Leftover_journal_is_rolled_back_on_start()
    {
        _app.SeedJournal();

        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

        Assert.Equal(1, services.StartupRollback.Reverted);
        Assert.True(services.StartupRollback.Success);
        Assert.Empty(((LunaDns.App.Features.LocalFeatureController)services.Features).Journal.Entries);
        Assert.Equal(1, _app.Configurator.Reverts);
    }

    [Fact]
    public async Task Setting_settings_saves_file()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

        services.Settings = services.Settings with { CheckPubg = true, PubgWarningAccepted = true };

        var saved = new JsonFileSettingsStore(_app.Paths.SettingsFile).Load();
        Assert.True(saved.CheckPubg);
        Assert.True(saved.PubgWarningAccepted);
    }

    [Fact]
    public async Task Apply_domains_saves_settings_when_local()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom], Custom = [new("example.org")] };

        var result = await services.ApplyDomainsAsync(selection);

        Assert.True(result.Success);
        Assert.True(services.Settings.Domains.SameAs(selection));
        Assert.True(new JsonFileSettingsStore(services.Paths.SettingsFile).Load().Domains.SameAs(selection));
        Assert.True((await services.GetDomainsAsync())!.SameAs(selection));
    }

    /// <summary>Brief dùng DnsRouting; ở đây dùng SniProxy (cổng cao) để test không cần cổng 53 trên máy chạy test.</summary>
    [Fact]
    public async Task Apply_domains_failure_keeps_old_settings()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);
        var enabled = await services.Features.EnableAsync(Feature.SniProxy, allowProxyOverride: true);
        Assert.Equal(FeatureOutcome.Enabled, enabled.Outcome);
        var before = services.Settings.Domains;

        var result = await services.ApplyDomainsAsync(new DomainSelection { EnabledGroups = [] });

        Assert.False(result.Success);
        Assert.Equal(ErrorMessages.For(FeatureError.NoDomains), result.Message);
        Assert.True(services.Settings.Domains.SameAs(before));
        Assert.True(new JsonFileSettingsStore(services.Paths.SettingsFile).Load().Domains.SameAs(before));
    }

    [Fact]
    public async Task Apply_domains_mentions_changes_left_behind_when_old_list_not_restored()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);
        await services.Features.EnableAsync(Feature.SniProxy, allowProxyOverride: true);
        _app.Configurator.FailRevert = true;

        var result = await services.ApplyDomainsAsync(new DomainSelection { EnabledGroups = [DomainGroup.Reddit] });
        _app.Configurator.FailRevert = false;

        Assert.False(result.Success);
        Assert.EndsWith(Strings.ChangesLeftBehind, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_uses_controller_selection_and_falls_back_to_settings()
    {
        var daemonSide = new DomainSelection { EnabledGroups = [DomainGroup.Reddit] };
        var controller = new SelectionOnlyController { Selection = daemonSide };
        await using var services = new AppServices(_app.Paths, _ => controller, NullLoggerFactory.Instance);

        Assert.True((await services.ProbeSelectionAsync()).SameAs(daemonSide));

        controller.Selection = null;
        Assert.True((await services.ProbeSelectionAsync()).SameAs(services.Settings.Domains));
    }

    [Fact]
    public async Task Apply_tuning_locally_saves_settings_and_reports_current_tuning()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);
        var candidate = new TuneCandidate(new HelloSplit(SplitMode.MidSni, true), 10);

        var result = await services.ApplyTuningAsync(DohProvider.Quad9, candidate);

        Assert.True(result.Success);
        Assert.Equal(DohProvider.Quad9, services.Settings.EffectiveDoh());
        Assert.Equal(candidate, services.Settings.CurrentCandidate());
        Assert.Equal(new TuningState(DohProvider.Quad9, candidate), await services.Features.GetTuningAsync());
    }

    /// <summary>Thay settings.json bằng thư mục cùng tên: lần ghi sau ném IOException/UnauthorizedAccessException.</summary>
    private static void MakeSettingsUnwritable(AppServices services)
    {
        File.Delete(services.Paths.SettingsFile);
        Directory.CreateDirectory(services.Paths.SettingsFile);
    }

    [Fact]
    public async Task Apply_tuning_locally_reports_unwritable_settings_without_changing_anything()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);
        services.Settings = services.Settings; // tạo file để thay bằng thư mục
        MakeSettingsUnwritable(services);
        var before = services.Settings;

        var result = await services.ApplyTuningAsync(DohProvider.Quad9, new TuneCandidate(new HelloSplit(SplitMode.MidSni, true), 10));

        Assert.False(result.Success);
        Assert.Contains("settings.json", result.Message, StringComparison.Ordinal);
        Assert.Same(before, services.Settings);
    }

    [Fact]
    public async Task Apply_tuning_through_daemon_keeps_success_when_app_copy_cannot_be_saved()
    {
        var controller = new SelectionOnlyController { TuningResult = new TuningApplyResult(true, null) };
        await using var services = new AppServices(_app.Paths, _ => controller, NullLoggerFactory.Instance);
        services.Settings = services.Settings;
        MakeSettingsUnwritable(services);
        var candidate = new TuneCandidate(new HelloSplit(SplitMode.MidSni, true), 10);

        var result = await services.ApplyTuningAsync(DohProvider.Quad9, candidate);

        Assert.True(result.Success);
        Assert.Equal(DohProvider.Quad9, services.Settings.EffectiveDoh());
        Assert.Equal(candidate, services.Settings.CurrentCandidate());
    }

    [Fact]
    public void Tune_choices_leave_out_custom_doh_unless_local()
    {
        var custom = new CustomDoh { Endpoint = "https://doh.example/dns-query", ManualAddresses = ["192.0.2.1"] };

        Assert.Contains(AppServices.TuneChoices(ControllerAvailability.Local, custom), c => c.Provider == DohProvider.Custom);
        Assert.DoesNotContain(AppServices.TuneChoices(ControllerAvailability.DaemonRunning, custom), c => c.Provider == DohProvider.Custom);
    }

    /// <summary>Chỉ trả lựa chọn tên miền; các lệnh khác không dùng tới trong test này.</summary>
    private sealed class SelectionOnlyController : IFeatureController
    {
        public DomainSelection? Selection { get; set; }

        public event EventHandler<FeatureStoppedEventArgs>? FeatureStopped { add { } remove { } }

        public ControllerAvailability Availability => ControllerAvailability.DaemonRunning;

        public FeatureStatus Status { get; } = new(false, false, false, null, 0);

        public LunaDns.Core.Journal.RollbackResult StartupRollback { get; } = new(0, []);

        public int? JournalEntries => 0;

        public Task<DomainSelection?> GetDomainsAsync() => Task.FromResult(Selection);

        public Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection) => throw new NotSupportedException();

        public Task<TuningState?> GetTuningAsync() => Task.FromResult<TuningState?>(null);

        public TuningApplyResult? TuningResult { get; set; }

        public Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate, Action? restore = null) =>
            TuningResult is { } result ? Task.FromResult(result) : throw new NotSupportedException();

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FeatureResult> EnableAsync(Feature feature, bool allowProxyOverride = false) => throw new NotSupportedException();

        public Task<FeatureResult> DisableAsync(Feature feature) => throw new NotSupportedException();

        public Task<DisableAllResult> DisableAllAsync() => throw new NotSupportedException();

        public Task<int> ShutdownAsync() => Task.FromResult(0);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
