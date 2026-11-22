using LunaDns.App.Updates;
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;

namespace LunaDns.App.Tests;

public sealed class MaintenanceTests
{
    private const string Url = "https://github.com/o/r/releases/tag/v0.9.1";
    private const string Uninstaller = @"C:\Program Files\LunaDNS\unins000.exe";

    private readonly FakePrompts _prompts = new();
    private readonly FakeShell _shell = new();

    private Maintenance Create() => new(_prompts, _shell);

    private static Func<CancellationToken, Task<UpdateCheckResult>> Returns(UpdateCheckResult result) => _ => Task.FromResult(result);

    [Theory]
    [InlineData(0, 9, 0, 0, "Đang dùng LunaDNS 0.9.0")]
    [InlineData(1, 2, -1, -1, "Đang dùng LunaDNS 1.2.0")]
    public void Version_text_shows_three_part_version(int major, int minor, int build, int revision, string expected)
    {
        var version = build < 0 ? new Version(major, minor) : new Version(major, minor, build, revision);

        Assert.Equal(expected, Maintenance.VersionText(version));
    }

    [Fact]
    public async Task Newer_version_confirmed_opens_release_page()
    {
        _prompts.Answer = true;
        var result = new UpdateCheckResult(UpdateStatus.NewerAvailable, new Version(0, 9, 1), Url);

        await Create().CheckForUpdateAsync(Returns(result), new Version(0, 9, 0), CancellationToken.None);

        Assert.Equal([Url], _shell.Urls);
        Assert.Contains("0.9.1", _prompts.Confirms.Single());
        Assert.Contains("0.9.0", _prompts.Confirms.Single());
    }

    [Fact]
    public async Task Newer_version_declined_opens_nothing()
    {
        _prompts.Answer = false;
        var result = new UpdateCheckResult(UpdateStatus.NewerAvailable, new Version(0, 9, 1), Url);

        await Create().CheckForUpdateAsync(Returns(result), new Version(0, 9, 0), CancellationToken.None);

        Assert.Empty(_shell.Urls);
    }

    [Fact]
    public async Task Up_to_date_informs()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpToDate, new Version(0, 9, 0), Url);

        await Create().CheckForUpdateAsync(Returns(result), new Version(0, 9, 0), CancellationToken.None);

        Assert.Equal([Strings.UpToDateTitle], _prompts.InformTitles);
        Assert.Empty(_shell.Urls);
    }

    [Fact]
    public async Task Failure_informs()
    {
        await Create().CheckForUpdateAsync(Returns(UpdateCheckResult.Failed), new Version(0, 9, 0), CancellationToken.None);

        Assert.Equal([Strings.UpdateCheckFailed], _prompts.InformMessages);
    }

    [Fact]
    public async Task Manual_check_returns_result()
    {
        _prompts.Answer = false;
        var result = new UpdateCheckResult(UpdateStatus.NewerAvailable, new Version(0, 9, 1), Url);

        Assert.Same(result, await Create().CheckForUpdateAsync(Returns(result), new Version(0, 9, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Silent_check_returns_newer_without_prompting()
    {
        var result = new UpdateCheckResult(UpdateStatus.NewerAvailable, new Version(0, 9, 1), Url);

        Assert.Same(result, await Maintenance.CheckSilentlyAsync(Returns(result), CancellationToken.None));
        Assert.Empty(_prompts.Confirms);
        Assert.Empty(_prompts.InformTitles);
        Assert.Empty(_shell.Urls);
    }

    [Fact]
    public async Task Silent_check_ignores_up_to_date_and_failure()
    {
        Assert.Null(await Maintenance.CheckSilentlyAsync(Returns(new UpdateCheckResult(UpdateStatus.UpToDate, null, null)), CancellationToken.None));
        Assert.Null(await Maintenance.CheckSilentlyAsync(Returns(UpdateCheckResult.Failed), CancellationToken.None));
        Assert.Empty(_prompts.InformTitles);
    }

    [Fact]
    public async Task Silent_check_swallows_exceptions()
    {
        Assert.Null(await Maintenance.CheckSilentlyAsync(_ => throw new HttpRequestException("x"), CancellationToken.None));
        Assert.Empty(_prompts.InformTitles);
    }

    [Fact]
    public async Task Silent_check_lets_caller_cancellation_through()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Maintenance.CheckSilentlyAsync(ct => Task.FromCanceled<UpdateCheckResult>(ct), cts.Token));
    }

    [Fact]
    public async Task Uninstall_confirmed_starts_uninstaller()
    {
        _prompts.Answer = true;

        Assert.True(await Create().UninstallAsync(Uninstaller));
        Assert.Equal([Uninstaller], _shell.Processes);
    }

    [Fact]
    public async Task Uninstall_declined_does_nothing()
    {
        _prompts.Answer = false;

        Assert.False(await Create().UninstallAsync(Uninstaller));
        Assert.Empty(_shell.Processes);
    }

    private sealed class FakePrompts : IUserPrompts
    {
        public bool Answer { get; set; }

        public List<string> Confirms { get; } = [];

        public List<string> InformTitles { get; } = [];

        public List<string> InformMessages { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message)
        {
            Confirms.Add(message);
            return Task.FromResult(Answer);
        }

        public Task InformAsync(string title, string message)
        {
            InformTitles.Add(title);
            InformMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task ShowDomainListAsync(string title, IReadOnlyList<CatalogEntry> entries, DateTimeOffset? issued) => Task.CompletedTask;

        public Task<bool> EditCustomDomainsAsync(CustomDomainsViewModel vm) => Task.FromResult(false);
    }

    private sealed class FakeShell : ISystemShell
    {
        public List<string> Urls { get; } = [];

        public List<string> Processes { get; } = [];

        public void OpenUrl(string url) => Urls.Add(url);

        public void StartProcess(string path) => Processes.Add(path);

        public void OpenFolder(string path) => Processes.Add("folder:" + path);

        public void Reveal(string file) => Processes.Add("reveal:" + file);
    }
}
