using LunaDns.App.Features;
using LunaDns.App.MacOS;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;
using LunaDns.Platform.MacOS.Daemon;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests.MacOS;

public sealed class DaemonFeatureControllerTests
{
    private readonly FakeDaemon _daemon = new();

    private DaemonFeatureController Create(bool installed = true, Version? appVersion = null) =>
        new(_daemon, () => installed, NullLoggerFactory.Instance, appVersion);

    private void DaemonVersion(string version) =>
        _daemon.Reply = r => r.Command == DaemonProtocol.Status
            ? new DaemonResponse(true, Version: version, Status: new DaemonStatus(false, false, null, 0, 0))
            : null;

    [Theory]
    [InlineData("0.5.0+5624b47", true)]
    [InlineData("0.9.0", true)]
    [InlineData("0.10.0+42f087e", false)]
    [InlineData("0.11.0", false)]
    [InlineData("test", false)]
    public async Task Service_is_outdated_when_daemon_is_older_than_the_app(string daemonVersion, bool outdated)
    {
        DaemonVersion(daemonVersion);
        await using var controller = Create(appVersion: new Version(0, 10, 0, 0));

        await controller.RefreshAsync();

        Assert.Equal(outdated, controller.ServiceOutdated);
    }

    [Fact]
    public async Task Service_is_outdated_after_an_unknown_command()
    {
        _daemon.Reply = r => r.Command == DaemonProtocol.GetDomainList
            ? new DaemonResponse(false, DaemonProtocol.UnknownCommandPrefix + " get-domain-list")
            : null;
        await using var controller = Create(appVersion: new Version(0, 10, 0, 0));
        await controller.RefreshAsync();
        Assert.False(controller.ServiceOutdated);

        Assert.Null(await controller.GetDomainListAsync());

        Assert.True(controller.ServiceOutdated);
    }

    [Fact]
    public async Task Unknown_command_flag_clears_when_daemon_version_changes()
    {
        _daemon.Reply = r => r.Command == DaemonProtocol.GetDomainList
            ? new DaemonResponse(false, DaemonProtocol.UnknownCommandPrefix + " get-domain-list")
            : null;
        await using var controller = Create(appVersion: new Version(0, 10, 0, 0));
        await controller.RefreshAsync();
        await controller.GetDomainListAsync();

        DaemonVersion("0.10.0+new");
        await controller.RefreshAsync();

        Assert.False(controller.ServiceOutdated);
    }

    [Fact]
    public async Task Service_is_not_outdated_while_daemon_is_down()
    {
        DaemonVersion("0.5.0");
        await using var controller = Create(appVersion: new Version(0, 10, 0, 0));
        await controller.RefreshAsync();
        _daemon.Reply = null;
        _daemon.Down = true;

        await controller.RefreshAsync();

        Assert.False(controller.ServiceOutdated);
    }

    [Fact]
    public async Task Starts_connecting_without_calling_daemon()
    {
        await using var controller = Create();

        Assert.Equal(ControllerAvailability.DaemonConnecting, controller.Availability);
        Assert.Empty(_daemon.Requests);
    }

    [Fact]
    public async Task Not_installed_until_daemon_answers()
    {
        _daemon.Down = true;
        await using var controller = Create(installed: false);

        await controller.RefreshAsync();

        Assert.Equal(ControllerAvailability.DaemonNotInstalled, controller.Availability);
        Assert.False(controller.Status.DnsRouting);
    }

    [Fact]
    public async Task Installed_but_silent_is_not_responding()
    {
        _daemon.Down = true;
        await using var controller = Create();

        await controller.RefreshAsync();

        Assert.Equal(ControllerAvailability.DaemonNotResponding, controller.Availability);
    }

    [Fact]
    public async Task Refresh_reads_status_from_daemon()
    {
        _daemon.Dns = true;
        await using var controller = Create();

        await controller.RefreshAsync();

        Assert.Equal(ControllerAvailability.DaemonRunning, controller.Availability);
        Assert.True(controller.Status.DnsRouting);
        Assert.Equal(1, controller.JournalEntries);
        Assert.Equal("test", controller.DaemonVersion);
    }

    [Fact]
    public async Task Refresh_reads_answering_doh_server()
    {
        _daemon.Dns = true;
        _daemon.DohServer = "Google";
        _daemon.DohFallback = true;
        await using var controller = Create();

        await controller.RefreshAsync();

        Assert.Equal(new DohActivity("Google", Fallback: true), controller.Status.Doh);
    }

    [Fact]
    public async Task Old_daemon_without_doh_fields_leaves_doh_unknown()
    {
        _daemon.Dns = true;
        await using var controller = Create();

        await controller.RefreshAsync();

        Assert.Null(controller.Status.Doh);
    }

    [Fact]
    public async Task Enable_sends_feature_key_and_updates_status()
    {
        await using var controller = Create();

        var result = await controller.EnableAsync(Feature.DnsRouting);

        Assert.Equal(FeatureOutcome.Enabled, result.Outcome);
        Assert.Equal(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting), _daemon.Requests.Single());
        Assert.True(controller.Status.DnsRouting);
    }

    [Fact]
    public async Task Proxy_override_flag_is_forwarded()
    {
        await using var controller = Create();

        await controller.EnableAsync(Feature.SniProxy, allowProxyOverride: true);

        Assert.True(_daemon.Requests.Single().AllowProxyOverride);
    }

    [Fact]
    public async Task Daemon_errors_map_to_feature_errors()
    {
        _daemon.FailWith = (nameof(FeatureOutcome.Failed), nameof(FeatureError.DnsRouteConflict));
        await using var controller = Create();

        var result = await controller.EnableAsync(Feature.DnsRouting);

        Assert.Equal(FeatureOutcome.Failed, result.Outcome);
        Assert.Equal(FeatureError.DnsRouteConflict, result.Error);
    }

    [Theory]
    [InlineData("NeedsProxyOverride", null, FeatureOutcome.NeedsProxyOverride, FeatureError.None)]
    [InlineData("Failed", "SomethingNew", FeatureOutcome.Failed, FeatureError.Other)]
    [InlineData("Failed", null, FeatureOutcome.Failed, FeatureError.Other)]
    [InlineData("Weird", null, FeatureOutcome.Failed, FeatureError.Other)]
    public void Unknown_values_degrade_to_other(string outcome, string? error, FeatureOutcome expectedOutcome, FeatureError expectedError)
    {
        var result = DaemonFeatureController.ToResult(Feature.SniProxy,
            new DaemonResponse(false, Outcome: outcome, Error: error, Status: new DaemonStatus(false, false, null, 0, 0)));

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public void Daemon_message_is_kept_as_detail()
    {
        var result = DaemonFeatureController.ToResult(Feature.SniProxy, new DaemonResponse(false, "networksetup -setautoproxyurl lỗi: ** Error",
            Outcome: "Failed", Error: "Other", Status: new DaemonStatus(false, false, null, 0, 0)));

        Assert.Equal("networksetup -setautoproxyurl lỗi: ** Error", result.Detail);
        Assert.Equal("networksetup -setautoproxyurl lỗi: ** Error", ErrorMessages.Reason(result));
    }

    [Fact]
    public async Task Service_log_comes_from_get_log()
    {
        _daemon.Reply = r => r.Command == DaemonProtocol.GetLog
            ? new DaemonResponse(true, Status: new DaemonStatus(false, false, null, 0, 0), Log: ["một", "hai"])
            : null;
        await using var controller = Create();

        Assert.Equal(["một", "hai"], await controller.GetServiceLogAsync());
        Assert.Null(_daemon.LastRequest!.Lines);
    }

    [Fact]
    public async Task Old_daemon_service_log_asks_for_reinstall()
    {
        DaemonVersion("0.10.0");
        await using var controller = Create(appVersion: new Version(0, 10, 0, 0));
        await controller.RefreshAsync();
        _daemon.Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}");

        Assert.Equal([Strings.ServiceLogOutdated], await controller.GetServiceLogAsync());
        Assert.True(controller.ServiceOutdated);
    }

    [Fact]
    public async Task Unreachable_or_refusing_daemon_gives_one_explaining_line()
    {
        await using var controller = Create();
        _daemon.Down = true;
        Assert.Equal([Strings.ServiceLogUnavailable], await controller.GetServiceLogAsync());

        _daemon.Down = false;
        _daemon.Reply = _ => new DaemonResponse(false, "không có log", Status: new DaemonStatus(false, false, null, 0, 0));
        var line = Assert.Single(await controller.GetServiceLogAsync());
        Assert.Contains("không có log", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreachable_daemon_gives_service_unavailable()
    {
        _daemon.Down = true;
        await using var controller = Create();

        var result = await controller.EnableAsync(Feature.DnsRouting);

        Assert.Equal(FeatureError.ServiceUnavailable, result.Error);
    }

    [Fact]
    public async Task Rejected_connection_is_not_responding()
    {
        _daemon.Reject = true;
        await using var controller = Create();

        await controller.RefreshAsync();

        Assert.Equal(ControllerAvailability.DaemonNotResponding, controller.Availability);
    }

    [Fact]
    public async Task Shutdown_disables_all_when_something_is_on()
    {
        _daemon.Dns = true;
        await using var controller = Create();
        await controller.RefreshAsync();

        var remaining = await controller.ShutdownAsync(ShutdownReason.UserQuit);

        Assert.Equal(0, remaining);
        Assert.Equal(DaemonProtocol.DisableAll, _daemon.Requests.Last().Command);
        Assert.False(controller.Status.DnsRouting);
    }

    [Fact]
    public async Task Shutdown_with_nothing_on_does_not_call_daemon()
    {
        await using var controller = Create();
        await controller.RefreshAsync();
        _daemon.Requests.Clear();

        Assert.Equal(0, await controller.ShutdownAsync(ShutdownReason.UserQuit));
        Assert.Empty(_daemon.Requests);
    }

    [Fact]
    public async Task Disable_all_failure_reports_remaining_entries()
    {
        _daemon.Dns = true;
        _daemon.DisableAllFails = true;
        await using var controller = Create();

        var result = await controller.DisableAllAsync();

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Apply_domains_sends_set_domains_and_maps_old_daemon_reply()
    {
        _daemon.Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}");
        await using var controller = Create();

        var result = await controller.ApplyDomainsAsync(new DomainSelection());

        Assert.Equal(DaemonProtocol.SetDomains, _daemon.LastRequest!.Command);
        Assert.False(result.Success);
        Assert.Equal(Strings.DaemonNeedsUpdate, result.Message);
    }

    /// <summary>Daemon cũ không biết get-domains: không được coi là "không trả lời" (banner, công tắc xám, thoát bỏ qua disable-all).</summary>
    [Fact]
    public async Task Unknown_command_reply_keeps_daemon_running()
    {
        _daemon.Dns = true;
        await using var controller = Create();
        await controller.RefreshAsync();
        _daemon.Reply = r => r.Command == DaemonProtocol.GetDomains ? new DaemonResponse(false, $"Lệnh lạ: {r.Command}") : null;

        Assert.Null(await controller.GetDomainsAsync());
        Assert.Equal(ControllerAvailability.DaemonRunning, controller.Availability);
        Assert.True(controller.Status.DnsRouting);
    }

    [Fact]
    public async Task Get_domain_list_maps_the_daemon_state_and_old_daemon_gives_null()
    {
        var added = new Dictionary<string, string[]> { ["steam"] = ["cdn.example.net"] };
        var issued = new DateTimeOffset(2026, 11, 21, 14, 2, 0, TimeSpan.Zero);
        _daemon.Reply = r => r.Command == DaemonProtocol.GetDomainList
            ? new DaemonResponse(true, DomainList: new DaemonDomainList(true, 7, issued, null, added))
            : null;
        await using var controller = Create();

        var state = await controller.GetDomainListAsync();

        Assert.True(state!.Enabled);
        Assert.Equal(7, state.Serial);
        Assert.Equal(["cdn.example.net"], state.Added[DomainGroup.Steam]);

        _daemon.Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}");
        Assert.Null(await controller.GetDomainListAsync());
    }

    [Fact]
    public async Task Offer_domain_list_sends_the_exact_bytes_as_base64()
    {
        byte[] file = [1, 2, 3, 250];
        _daemon.Reply = r => r.Command == DaemonProtocol.SetDomainList
            ? new DaemonResponse(true, DomainListResult: new DaemonDomainListResult("Accepted", 3, 0, null, true))
            : null;
        await using var controller = Create();

        var result = await controller.OfferDomainListAsync(file);

        Assert.Equal(Convert.ToBase64String(file), _daemon.LastRequest!.Envelope);
        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Equal(3, result.Added);
    }

    [Fact]
    public async Task Domain_list_calls_report_unavailable_when_the_daemon_is_silent_or_old()
    {
        _daemon.Down = true;
        await using var controller = Create();
        Assert.Equal(DomainListOutcome.Unavailable, (await controller.OfferDomainListAsync([1])).Outcome);
        Assert.Equal(DomainListOutcome.Unavailable, (await controller.SetDomainListEnabledAsync(false)).Outcome);

        _daemon.Down = false;
        _daemon.Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}");
        Assert.Equal(DomainListOutcome.Unavailable, (await controller.OfferDomainListAsync([1])).Outcome);
    }

    [Fact]
    public async Task Apply_domains_passes_selection_and_daemon_message()
    {
        _daemon.Reply = r => r.Command == DaemonProtocol.SetDomains
            ? new DaemonResponse(false, "Tên miền không hợp lệ ở dòng 2.", Status: new DaemonStatus(false, false, null, 0, 0))
            : null;
        await using var controller = Create();
        await controller.RefreshAsync();
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Reddit] };

        var result = await controller.ApplyDomainsAsync(selection);

        Assert.True(_daemon.LastRequest!.Domains!.SameAs(selection));
        Assert.False(result.Success);
        Assert.Equal("Tên miền không hợp lệ ở dòng 2.", result.Message);
        Assert.Equal(ControllerAvailability.DaemonRunning, controller.Availability);
    }

    [Fact]
    public async Task Apply_domains_succeeds_when_daemon_accepts()
    {
        await using var controller = Create();

        var result = await controller.ApplyDomainsAsync(new DomainSelection());

        Assert.True(result.Success);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task Apply_domains_without_daemon_is_service_unavailable()
    {
        _daemon.Down = true;
        await using var controller = Create();

        var result = await controller.ApplyDomainsAsync(new DomainSelection());

        Assert.False(result.Success);
        Assert.Equal(ErrorMessages.For(FeatureError.ServiceUnavailable), result.Message);
    }

    [Fact]
    public async Task Get_domains_returns_daemon_selection_or_null_when_down()
    {
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Steam, DomainGroup.Reddit] };
        _daemon.Reply = r => r.Command == DaemonProtocol.GetDomains ? new DaemonResponse(true, Domains: selection) : null;
        await using var controller = Create();

        Assert.True((await controller.GetDomainsAsync())!.SameAs(selection));
        Assert.Equal(DaemonProtocol.GetDomains, _daemon.LastRequest!.Command);

        _daemon.Down = true;
        Assert.Null(await controller.GetDomainsAsync());
    }

    /// <summary>Daemon giả giữ trạng thái như DaemonCommands: journal có một mục khi có tính năng bật.</summary>
    [Fact]
    public async Task Get_tuning_reads_daemon_and_is_null_for_old_daemon()
    {
        var tuning = new DaemonTuning(DohProvider.Quad9, SplitMode.MidSni, true, 10);
        _daemon.Reply = r => r.Command == DaemonProtocol.GetTuning ? new DaemonResponse(true, Tuning: tuning) : null;
        await using var controller = Create();

        Assert.Equal(new TuningState(DohProvider.Quad9, tuning.ToCandidate()), await controller.GetTuningAsync());

        _daemon.Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}");
        Assert.Null(await controller.GetTuningAsync());
    }

    [Fact]
    public async Task Apply_tuning_sends_set_tuning_and_maps_old_daemon_reply()
    {
        _daemon.Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}");
        await using var controller = Create();
        var candidate = new TuneCandidate(new HelloSplit(SplitMode.MidSni, true), 0);

        var result = await controller.ApplyTuningAsync(DohProvider.Quad9, candidate);

        Assert.Equal(DaemonProtocol.SetTuning, _daemon.LastRequest!.Command);
        Assert.Equal(DaemonTuning.From(DohProvider.Quad9, candidate), _daemon.LastRequest.Tuning);
        Assert.False(result.Success);
        Assert.Equal(Strings.DaemonNeedsUpdateTuning, result.Message);
    }

    [Fact]
    public async Task Apply_tuning_succeeds_or_reports_service_unavailable()
    {
        await using var controller = Create();
        Assert.True((await controller.ApplyTuningAsync(DohProvider.Google, TuneCandidate.Default)).Success);

        _daemon.Down = true;
        var down = await controller.ApplyTuningAsync(DohProvider.Google, TuneCandidate.Default);
        Assert.False(down.Success);
        Assert.Equal(ErrorMessages.For(FeatureError.ServiceUnavailable), down.Message);
    }

    [Fact]
    public async Task Resume_comes_from_the_daemon_reply_and_clears_when_it_stops_answering()
    {
        _daemon.Resume = new DaemonResume(false, true, false, false, FeatureKeys.DnsRouting, "Failed", "DnsPortBusy");
        await using var controller = Create();

        await controller.RefreshAsync();

        Assert.Equal(new ResumeState(false, new WantedFeatures(true, false), false,
            new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.DnsPortBusy)), controller.Resume);
        _daemon.Down = true;
        await controller.RefreshAsync();
        Assert.Null(controller.Resume);
    }

    [Fact]
    public async Task Session_end_shutdown_asks_the_daemon_to_keep_wanted()
    {
        _daemon.Dns = true;
        await using var controller = Create();
        await controller.RefreshAsync();

        await controller.ShutdownAsync(ShutdownReason.SessionEnd);

        Assert.Equal(new DaemonRequest(DaemonProtocol.DisableAll, SessionEnd: true), _daemon.LastRequest);
    }

    [Fact]
    public async Task User_quit_with_nothing_on_still_clears_wanted_left_in_the_daemon()
    {
        _daemon.Resume = new DaemonResume(false, true, false, false);
        await using var controller = Create();
        await controller.RefreshAsync();

        await controller.ShutdownAsync(ShutdownReason.UserQuit);
        Assert.Equal(new DaemonRequest(DaemonProtocol.DisableAll), _daemon.LastRequest);

        _daemon.Resume = new DaemonResume(true, false, false, false);
        await controller.RefreshAsync();
        _daemon.Requests.Clear();
        await controller.ShutdownAsync(ShutdownReason.UserQuit);
        Assert.Empty(_daemon.Requests);
    }

    [Fact]
    public async Task Set_auto_resume_reports_old_daemons_and_errors()
    {
        await using var controller = Create();
        Assert.Null(await controller.SetAutoResumeAsync(false));
        Assert.Equal(new DaemonRequest(DaemonProtocol.SetAutoResume, Enabled: false), _daemon.LastRequest);

        _daemon.Reply = r => new DaemonResponse(false, $"{DaemonProtocol.UnknownCommandPrefix} {r.Command}");
        Assert.Equal(Strings.DaemonNeedsUpdate, await controller.SetAutoResumeAsync(true));

        _daemon.Reply = null;
        _daemon.Down = true;
        Assert.Equal(ErrorMessages.For(FeatureError.ServiceUnavailable), await controller.SetAutoResumeAsync(true));
    }

    private sealed class FakeDaemon : IDaemonTransport
    {
        public bool Down { get; set; }
        public bool Reject { get; set; }
        public bool Dns { get; set; }
        public bool Proxy { get; set; }

        public string? DohServer { get; set; }

        public bool DohFallback { get; set; }
        public bool DisableAllFails { get; set; }
        public DaemonResume? Resume { get; set; }
        public (string Outcome, string Error)? FailWith { get; set; }
        public List<DaemonRequest> Requests { get; } = [];
        /// <summary>Trả lời riêng cho một yêu cầu; null thì xử lý như bình thường.</summary>
        public Func<DaemonRequest, DaemonResponse?>? Reply { get; set; }
        public DaemonRequest? LastRequest => Requests.Count > 0 ? Requests[^1] : null;

        public Task<DaemonResponse> SendAsync(DaemonRequest request, CancellationToken cancellationToken)
        {
            if (Down) throw new DaemonUnavailableException("down");
            if (Reject) return Task.FromResult(new DaemonResponse(false, "Chỉ người đang dùng máy này"));
            Requests.Add(request);
            if (Reply?.Invoke(request) is { } custom) return Task.FromResult(custom);
            DaemonResponse response;
            switch (request.Command)
            {
                case DaemonProtocol.Enable when FailWith is { } fail:
                    response = Ok() with { Ok = false, Outcome = fail.Outcome, Error = fail.Error };
                    break;
                case DaemonProtocol.Enable:
                    Set(request.Feature, true);
                    response = Ok() with { Outcome = nameof(FeatureOutcome.Enabled) };
                    break;
                case DaemonProtocol.Disable:
                    Set(request.Feature, false);
                    response = Ok() with { Outcome = nameof(FeatureOutcome.Disabled) };
                    break;
                case DaemonProtocol.DisableAll when DisableAllFails:
                    response = Ok() with { Ok = false, Message = "còn 1" };
                    break;
                case DaemonProtocol.DisableAll:
                    Dns = Proxy = false;
                    response = Ok();
                    break;
                default:
                    response = Ok();
                    break;
            }
            return Task.FromResult(response);
        }

        private void Set(string? key, bool on)
        {
            if (key == FeatureKeys.DnsRouting) Dns = on;
            else Proxy = on;
        }

        private DaemonResponse Ok() =>
            new(true, Version: "test", Status: new DaemonStatus(Dns, Proxy, Proxy ? 18080 : null, 0, Dns || Proxy ? 1 : 0,
                DohServer, DohServer is null ? null : DohFallback), Resume: Resume);
    }
}
