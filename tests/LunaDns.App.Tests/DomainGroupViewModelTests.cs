using LunaDns.App.Features;
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;

namespace LunaDns.App.Tests;

public sealed class DomainGroupViewModelTests
{
    [Fact]
    public async Task Toggling_a_group_applies_new_selection()
    {
        DomainSelection? applied = null;
        var selection = new DomainSelection();
        var vm = new DomainGroupViewModel(DomainGroup.Reddit, () => selection,
            s => { applied = s; selection = s; return Task.FromResult(new DomainApplyResult(true, null)); }, _ => { });

        await vm.SetEnabledAsync(true);

        Assert.True(applied!.IsEnabled(DomainGroup.Reddit));
        Assert.True(vm.IsEnabled);
    }

    [Fact]
    public async Task Failed_toggle_reverts_switch_and_reports_message()
    {
        string? shown = null;
        var vm = new DomainGroupViewModel(DomainGroup.Steam, () => new DomainSelection(),
            _ => Task.FromResult(new DomainApplyResult(false, "lỗi giả lập")), m => shown = m);

        await vm.SetEnabledAsync(false);

        Assert.True(vm.IsEnabled);
        Assert.Equal("lỗi giả lập", shown);
    }

    [Fact]
    public void Count_text_for_custom_shows_enabled_over_total()
    {
        var selection = new DomainSelection { Custom = [new("a.example.org"), new("b.example.org", Enabled: false)] };
        var vm = new DomainGroupViewModel(DomainGroup.Custom, () => selection, _ => Task.FromResult(new DomainApplyResult(true, null)), _ => { });

        Assert.Equal("1/2 đang bật", vm.CountText);
    }

    [Fact]
    public void Count_text_for_prebuilt_group_counts_catalog()
    {
        var vm = Create(DomainGroup.Reddit, new DomainSelection());

        Assert.Equal($"{DomainCatalog.For(DomainGroup.Reddit).Count} tên miền", vm.CountText);
        Assert.Equal("Reddit", vm.Title);
        Assert.False(vm.IsCustom);
    }

    [Fact]
    public void Status_before_any_check_follows_mockup()
    {
        var selection = new DomainSelection();

        Assert.Equal("Chưa kiểm tra", Create(DomainGroup.Steam, selection).StatusText);
        Assert.Equal("—", Create(DomainGroup.Reddit, selection).StatusText);
        Assert.Null(Create(DomainGroup.Reddit, selection).StatusTone);
        Assert.Equal("Chưa có tên miền", Create(DomainGroup.Custom, selection).StatusText);
    }

    [Fact]
    public void Probe_result_shows_until_a_temporary_state_replaces_it()
    {
        var vm = Create(DomainGroup.Steam, new DomainSelection());

        vm.ShowProbe("Giúp được (cần Proxy SNI)", Tone.Good);
        Assert.Equal("Giúp được (cần Proxy SNI)", vm.StatusText);
        Assert.Equal(Tone.Good, vm.StatusTone);

        vm.ShowTemporary(Strings.GroupKeptBack, Tone.Error);
        Assert.Equal("Đã giữ lại", vm.StatusText);

        vm.ClearTemporary();
        Assert.Equal("Giúp được (cần Proxy SNI)", vm.StatusText);
    }

    [Fact]
    public async Task Refusing_the_last_group_marks_row_kept_back()
    {
        var vm = new DomainGroupViewModel(DomainGroup.Steam, () => new DomainSelection { EnabledGroups = [DomainGroup.Steam] },
            _ => Task.FromResult(new DomainApplyResult(false, "bất kỳ câu nào", FeatureError.NoDomains)), _ => { });

        await vm.SetEnabledAsync(false);

        Assert.Equal(Strings.GroupKeptBack, vm.StatusText);
        Assert.Equal(Tone.Error, vm.StatusTone);
        Assert.True(vm.IsEnabled);
    }

    [Fact]
    public async Task Old_daemon_marks_row_not_applied()
    {
        var vm = new DomainGroupViewModel(DomainGroup.Reddit, () => new DomainSelection(),
            _ => Task.FromResult(new DomainApplyResult(false, Strings.DaemonNeedsUpdate)), _ => { });

        await vm.SetEnabledAsync(true);

        Assert.Equal(Strings.GroupNotApplied, vm.StatusText);
        Assert.Equal(Tone.Warning, vm.StatusTone);
        Assert.False(vm.IsEnabled);
    }

    [Fact]
    public async Task Open_command_runs_open_callback()
    {
        DomainGroupViewModel? opened = null;
        var vm = new DomainGroupViewModel(DomainGroup.Steam, () => new DomainSelection(),
            _ => Task.FromResult(new DomainApplyResult(true, null)), _ => { },
            open: row => { opened = row; return Task.CompletedTask; });

        vm.OpenCommand.Execute(null);
        await Task.Yield();

        Assert.Same(vm, opened);
        Assert.Equal("Xem", vm.OpenText);
        Assert.Equal("Sửa…", Create(DomainGroup.Custom, new DomainSelection()).OpenText);
    }

    private static DomainGroupViewModel Create(DomainGroup group, DomainSelection selection) =>
        new(group, () => selection, _ => Task.FromResult(new DomainApplyResult(true, null)), _ => { });
}
