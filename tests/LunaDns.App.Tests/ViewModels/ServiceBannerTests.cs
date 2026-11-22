using LunaDns.App.Features;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Tests.ViewModels;

/// <summary>Banner dịch vụ nền macOS (mockup 1.4), B4/U3: tài khoản khác đang ngồi trước máy thì không có nút Cài lại.</summary>
public sealed class ServiceBannerTests
{
    [Theory]
    [InlineData(nameof(ControllerAvailability.Local))]
    [InlineData(nameof(ControllerAvailability.DaemonConnecting))]
    [InlineData(nameof(ControllerAvailability.DaemonRunning))]
    public void No_banner_when_nothing_is_wrong(string availability) =>
        Assert.Null(ServiceBanner.From(Enum.Parse<ControllerAvailability>(availability), needsUpdate: false));

    [Fact]
    public void Not_installed_offers_install()
    {
        var banner = ServiceBanner.From(ControllerAvailability.DaemonNotInstalled, needsUpdate: false)!;

        Assert.Equal(Strings.ServiceNotInstalled, banner.Text);
        Assert.Equal(Strings.InstallServiceButton, banner.ButtonText);
    }

    [Fact]
    public void Not_responding_offers_reinstall()
    {
        var banner = ServiceBanner.From(ControllerAvailability.DaemonNotResponding, needsUpdate: false)!;

        Assert.Equal(Strings.ServiceNotResponding, banner.Text);
        Assert.Equal(Strings.ReinstallServiceButton, banner.ButtonText);
    }

    [Fact]
    public void Outdated_daemon_offers_reinstall()
    {
        var banner = ServiceBanner.From(ControllerAvailability.DaemonRunning, needsUpdate: true)!;

        Assert.Equal(Strings.DaemonNeedsUpdate, banner.Text);
        Assert.Equal(Strings.ReinstallServiceButton, banner.ButtonText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Other_console_user_has_no_button_even_when_an_update_was_wanted(bool needsUpdate)
    {
        var banner = ServiceBanner.From(ControllerAvailability.DaemonOtherUser, needsUpdate)!;

        Assert.Equal(Strings.ServiceOtherUser, banner.Text);
        Assert.Null(banner.ButtonText);
    }

    [Fact]
    public void Status_is_polled_slower_while_the_window_is_hidden()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), MainViewModel.PollInterval(windowShown: true));
        Assert.Equal(TimeSpan.FromSeconds(15), MainViewModel.PollInterval(windowShown: false));
    }
}
