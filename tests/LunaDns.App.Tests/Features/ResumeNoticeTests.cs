using LunaDns.App.Features;
using LunaDns.Core.Features;

namespace LunaDns.App.Tests.Features;

public sealed class ResumeNoticeTests
{
    private static readonly FeatureStatus Off = new(false, false, false, null, 0);
    private static readonly FeatureStatus DnsOn = new(true, false, false, null, 0);
    private static readonly WantedFeatures Both = new(true, true);
    private static readonly WantedFeatures DnsOnly = new(true, false);
    private static readonly WantedFeatures SniOnly = new(false, true);

    private static ResumeState State(WantedFeatures wanted, bool enabled = true, bool restoring = false, FeatureResult? failure = null) =>
        new(enabled, wanted, restoring, failure);

    private static ResumeNotice? From(ResumeState? resume, FeatureStatus? status = null, WantedFeatures? carried = null,
        WantedFeatures? dismissed = null, ControllerAvailability availability = ControllerAvailability.DaemonRunning) =>
        ResumeNotice.From(availability, resume, status ?? Off, carried ?? WantedFeatures.None, dismissed);

    [Fact]
    public void Nothing_missing_or_daemon_away_or_still_restoring_shows_nothing()
    {
        Assert.Null(From(State(WantedFeatures.None)));
        Assert.Null(From(State(DnsOnly), DnsOn));
        Assert.Null(From(State(DnsOnly, restoring: true)));
        Assert.Null(From(State(DnsOnly), availability: ControllerAvailability.DaemonNotResponding));
        Assert.Null(From(null));
    }

    [Fact]
    public void Switch_off_names_both_features_and_the_reason()
    {
        var notice = From(State(Both, enabled: false))!;

        Assert.Equal(Both, notice.Missing);
        var names = Strings.Format(nameof(Strings.ResumeBothFormat), Strings.FeatureTitle(Feature.DnsRouting), Strings.FeatureTitle(Feature.SniProxy));
        Assert.Equal($"{Strings.Format(nameof(Strings.ResumeNoticeFormat), names)} {Strings.ResumeNoticeSwitchOff}", notice.Text);
    }

    [Fact]
    public void Failure_of_a_missing_feature_explains_it_and_proxy_override_asks_to_confirm()
    {
        var proxy = From(State(Both, failure: new FeatureResult(Feature.SniProxy, FeatureOutcome.NeedsProxyOverride)), DnsOn)!;
        Assert.Equal(SniOnly, proxy.Missing);
        Assert.EndsWith(Strings.ResumeNoticeProxy, proxy.Text, StringComparison.Ordinal);

        var busy = new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.DnsPortBusy);
        Assert.EndsWith(ErrorMessages.For(busy)!, From(State(DnsOnly, failure: busy))!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_of_a_feature_that_is_running_again_is_not_mentioned()
    {
        var notice = From(State(Both, failure: new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.DnsPortBusy)),
            DnsOn)!;

        Assert.Equal(Strings.Format(nameof(Strings.ResumeNoticeFormat), Strings.FeatureTitle(Feature.SniProxy)), notice.Text);
    }

    [Fact]
    public void Carried_set_from_before_a_reinstall_counts_even_for_an_old_daemon()
    {
        Assert.Equal(SniOnly, From(null, carried: SniOnly)!.Missing);
        Assert.Equal(Both, From(State(DnsOnly), carried: SniOnly)!.Missing);
    }

    [Fact]
    public void Dismissed_set_stays_hidden_until_the_missing_set_changes()
    {
        Assert.Null(From(State(DnsOnly), dismissed: DnsOnly));
        Assert.NotNull(From(State(Both), dismissed: DnsOnly));
    }
}
