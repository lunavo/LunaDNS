using System.Net;
using LunaDns.App;
using LunaDns.App.Features;
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests;

public sealed class CustomDomainsViewModelTests
{
    private readonly FakeTimeProvider _time = new();
    private DomainSelection? _applied;
    private DomainApplyResult _applyResult = new(true, null);

    private CustomDomainsViewModel Create(params string[] domains) => new(
        new DomainSelection { EnabledGroups = [DomainGroup.Steam, DomainGroup.Custom], Custom = [.. domains.Select(d => new CustomDomain(d))] },
        s => { _applied = s; return Task.FromResult(_applyResult); },
        (hosts, _) => Task.FromResult<IReadOnlyList<ProbeResult>>(
            [.. hosts.Select(h => new ProbeResult(h, h.StartsWith("bad", StringComparison.Ordinal) ? ProbeVerdict.Unreachable : ProbeVerdict.SniFiltered, [], [], false))]),
        _time);

    [Fact]
    public void Add_accepts_many_lines_and_keeps_rejected_text()
    {
        var vm = Create();
        vm.AddText = "https://www.reddit.com/r/x\nredd.it, com.vn\nstore.steampowered.com";

        vm.Add();

        Assert.Equal(["reddit.com", "redd.it"], vm.Rows.Select(r => r.Domain));
        Assert.Equal("com.vn\nstore.steampowered.com", vm.AddText);
        Assert.Equal(2, vm.AddErrors.Count);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Edit_revalidates_and_rejects_duplicates()
    {
        var vm = Create("reddit.com", "redd.it");
        var row = vm.Rows[1];
        row.IsEditing = true;

        row.EditText = "reddit.com";
        Assert.False(vm.CommitEdit(row));
        Assert.Equal("redd.it", row.Domain);

        row.EditText = "https://i.redd.it/abc";
        Assert.True(vm.CommitEdit(row));
        Assert.Equal("i.redd.it", row.Domain);
        Assert.False(row.IsEditing);
    }

    [Fact]
    public void Delete_can_be_undone_within_six_seconds()
    {
        var vm = Create("reddit.com", "redd.it");

        vm.Delete(vm.Rows[0]);
        Assert.Equal(["redd.it"], vm.Rows.Select(r => r.Domain));
        Assert.NotNull(vm.UndoText);

        vm.Undo();
        Assert.Equal(["reddit.com", "redd.it"], vm.Rows.Select(r => r.Domain));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Undo_offer_expires_after_six_seconds()
    {
        var vm = Create("reddit.com");
        vm.Delete(vm.Rows[0]);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("còn 4 giây", vm.UndoSecondsText);

        _time.Advance(TimeSpan.FromSeconds(4.1));

        Assert.Null(vm.UndoText);
        vm.Undo();
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Import_merges_and_reports_counts_and_export_round_trips()
    {
        var vm = Create("reddit.com");

        var report = vm.Import("# danh sách\nreddit.com\nredd.it\ncom\n# off: example.org\n");

        Assert.Equal(["reddit.com", "redd.it", "example.org"], vm.Rows.Select(r => r.Domain));
        Assert.False(vm.Rows[2].Enabled);
        Assert.Contains("2", report, StringComparison.Ordinal);   // đã thêm 2
        Assert.Contains("2", report[report.IndexOf(',', StringComparison.Ordinal)..], StringComparison.Ordinal); // bỏ qua 2 (trùng + lỗi)
        Assert.Equal("reddit.com\nredd.it\n# off: example.org\n", vm.Export());
    }

    [Fact]
    public async Task Probe_fills_each_row_with_spec_text()
    {
        var vm = Create("reddit.com", "bad.example.org");

        await vm.ProbeAsync(selectedOnly: false, CancellationToken.None);

        Assert.Equal(DomainProbeText.For(ProbeVerdict.SniFiltered), vm.Rows[0].ProbeText);
        Assert.Equal(DomainProbeText.For(ProbeVerdict.Unreachable), vm.Rows[1].ProbeText);
    }

    [Fact]
    public async Task Save_applies_and_closes_on_success()
    {
        var vm = Create();
        vm.AddText = "reddit.com";
        vm.Add();

        Assert.True(await vm.SaveAsync());
        Assert.Equal([new CustomDomain("reddit.com")], _applied!.Custom);
        Assert.Equal([DomainGroup.Steam, DomainGroup.Custom], _applied.EnabledGroups);
    }

    [Fact]
    public async Task Save_failure_keeps_dialog_open_with_message()
    {
        _applyResult = new DomainApplyResult(false, "lỗi giả lập");
        var vm = Create();
        vm.AddText = "reddit.com";
        vm.Add();

        Assert.False(await vm.SaveAsync());
        Assert.Equal("lỗi giả lập", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Count_text_shows_enabled_over_total_and_limit()
    {
        var vm = Create("reddit.com", "redd.it");
        vm.Rows[1].Enabled = false;

        Assert.Equal("1/2 đang bật · tối đa 200", vm.CountText);
    }
}
