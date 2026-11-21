using LunaDns.App.Updates;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Tests;

public sealed class UpdateBannerViewModelTests
{
    private const string Url = "https://github.com/o/r/releases/tag/v0.9.1";

    private readonly FakeShell _shell = new();
    private readonly List<Exception> _errors = [];

    private UpdateBannerViewModel Create() => new(_shell, new Version(0, 9, 0, 0), _errors.Add);

    private static UpdateCheckResult Newer() => new(UpdateStatus.NewerAvailable, new Version(0, 9, 1), Url);

    [Fact]
    public void Hidden_until_shown()
    {
        var banner = Create();

        Assert.False(banner.IsVisible);
        Assert.Equal(string.Empty, banner.Text);
    }

    [Fact]
    public void Newer_version_shows_text_with_both_versions()
    {
        var banner = Create();
        var changed = new List<string?>();
        banner.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        banner.Show(Newer());

        Assert.True(banner.IsVisible);
        Assert.Equal("Đã có LunaDNS 0.9.1 (bạn đang dùng 0.9.0).", banner.Text);
        Assert.Contains(nameof(UpdateBannerViewModel.IsVisible), changed);
        Assert.Contains(nameof(UpdateBannerViewModel.Text), changed);
    }

    [Fact]
    public void Up_to_date_and_failure_do_not_show_or_hide()
    {
        var banner = Create();
        banner.Show(new UpdateCheckResult(UpdateStatus.UpToDate, null, null));
        banner.Show(UpdateCheckResult.Failed);
        Assert.False(banner.IsVisible);

        banner.Show(Newer());
        banner.Show(UpdateCheckResult.Failed);
        Assert.True(banner.IsVisible);
    }

    [Fact]
    public void Open_opens_release_page_and_keeps_banner()
    {
        var banner = Create();
        banner.Show(Newer());

        banner.OpenCommand.Execute(null);

        Assert.Equal([Url], _shell.Urls);
        Assert.True(banner.IsVisible);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Dismiss_hides_and_manual_check_can_show_again()
    {
        var banner = Create();
        banner.Show(Newer());

        banner.DismissCommand.Execute(null);
        Assert.False(banner.IsVisible);

        banner.Show(Newer());
        Assert.True(banner.IsVisible);
    }

    private sealed class FakeShell : ISystemShell
    {
        public List<string> Urls { get; } = [];

        public void OpenUrl(string url) => Urls.Add(url);

        public void StartProcess(string path) { }
    }
}
