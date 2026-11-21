using LunaDns.App.Features;
using LunaDns.App.MacOS;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Platform.MacOS.Daemon;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests.MacOS;

public sealed class DaemonFeatureControllerTests
{
    private readonly FakeDaemon _daemon = new();

    private DaemonFeatureController Create(bool installed = true) =>
        new(_daemon, () => installed, NullLoggerFactory.Instance);

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

        var remaining = await controller.ShutdownAsync();

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

        Assert.Equal(0, await controller.ShutdownAsync());
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

    [Fact]
    public async Task Apply_domains_passes_selection_and_daemon_message()
    {
        _daemon.Reply = _ => new DaemonResponse(false, "Tên miền không hợp lệ ở dòng 2.");
        await using var controller = Create();
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Reddit] };

        var result = await controller.ApplyDomainsAsync(selection);

        Assert.True(_daemon.LastRequest!.Domains!.SameAs(selection));
        Assert.False(result.Success);
        Assert.Equal("Tên miền không hợp lệ ở dòng 2.", result.Message);
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
    private sealed class FakeDaemon : IDaemonTransport
    {
        public bool Down { get; set; }
        public bool Reject { get; set; }
        public bool Dns { get; set; }
        public bool Proxy { get; set; }
        public bool DisableAllFails { get; set; }
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
            new(true, Version: "test", Status: new DaemonStatus(Dns, Proxy, Proxy ? 18080 : null, 0, Dns || Proxy ? 1 : 0));
    }
}
