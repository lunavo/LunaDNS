using System.Net;
using LunaDns.App;
using LunaDns.App.Features;
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests;

public sealed class CustomDomainsViewModelTests
{
    private readonly FakeTimeProvider _time = new();
    private DomainSelection? _applied;
    private DomainApplyResult _applyResult = new(true, null);

    private EffectiveCatalog _catalog = EffectiveCatalog.Builtin;

    private CustomDomainsViewModel Create(params string[] domains) => new(
        new DomainSelection { EnabledGroups = [DomainGroup.Steam, DomainGroup.Custom], Custom = [.. domains.Select(d => new CustomDomain(d))] },
        _catalog,
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

    [Fact]
    public void Edit_error_is_shown_on_the_row_not_as_save_error()
    {
        var vm = Create("reddit.com", "redd.it");
        var row = vm.Rows[1];
        row.IsEditing = true;
        row.EditText = "reddit.com";

        Assert.False(vm.CommitEdit(row));

        Assert.StartsWith("reddit.com: ", row.EditError, StringComparison.Ordinal);
        Assert.Null(vm.ErrorMessage);
        Assert.True(row.IsEditing);

        row.EditText = "i.redd.it";
        Assert.True(vm.CommitEdit(row));
        Assert.Null(row.EditError);
    }

    [Fact]
    public void Edit_hint_shows_normalized_name()
    {
        var row = Create("reddit.com").Rows[0];
        row.IsEditing = true;

        row.EditText = "https://News.Example.org/tin-moi";

        Assert.Equal("Enter để nhận · Esc để huỷ · sẽ lưu thành news.example.org", row.EditHint);
    }

    [Fact]
    public void Cancel_edit_restores_row()
    {
        var vm = Create("reddit.com");
        var row = vm.Rows[0];
        row.IsEditing = true;
        row.EditText = "com";
        vm.CommitEdit(row);

        row.CancelEdit();

        Assert.False(row.IsEditing);
        Assert.Null(row.EditError);
        Assert.Equal("reddit.com", row.Domain);
    }

    [Fact]
    public void Add_reports_added_and_left_over_lines()
    {
        var vm = Create();
        vm.AddText = "reddit.com\nredd.it\ncom.vn";

        vm.Add();

        Assert.Equal("Đã thêm 2 tên miền. 1 dòng còn lại trong ô để bạn sửa.", vm.ReportText);

        vm.AddText = "example.org";
        vm.Add();
        Assert.Equal("Đã thêm 1 tên miền.", vm.ReportText);
    }

    [Fact]
    public void Import_report_is_shown()
    {
        var vm = Create();

        var report = vm.Import("reddit.com\n");

        Assert.Equal(report, vm.ReportText);
    }

    [Fact]
    public void Can_add_only_with_text_and_when_idle()
    {
        var vm = Create();
        Assert.False(vm.CanAdd);

        vm.AddText = "  ";
        Assert.False(vm.CanAdd);

        vm.AddText = "reddit.com";
        Assert.True(vm.CanAdd);
    }

    [Fact]
    public void Probe_selected_text_counts_selection()
    {
        var vm = Create("reddit.com", "redd.it");
        Assert.Equal("Kiểm tra đã chọn", vm.ProbeSelectedText);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Rows[0].IsSelected = true;
        vm.Rows[1].IsSelected = true;

        Assert.Equal("Kiểm tra đã chọn (2)", vm.ProbeSelectedText);
        Assert.Contains(nameof(CustomDomainsViewModel.ProbeSelectedText), changed);
    }

    [Fact]
    public void Can_save_only_when_dirty()
    {
        var vm = Create("reddit.com");
        Assert.False(vm.CanSave);

        vm.Rows[0].Enabled = false;

        Assert.True(vm.CanSave);
    }

    [Fact]
    public async Task Save_exception_becomes_message_not_crash()
    {
        var vm = new CustomDomainsViewModel(new DomainSelection(), EffectiveCatalog.Builtin,
            _ => throw new InvalidOperationException("hỏng"),
            (_, _) => Task.FromResult<IReadOnlyList<ProbeResult>>([]), _time);
        vm.AddText = "reddit.com";
        vm.Add();

        Assert.False(await vm.SaveAsync());

        Assert.Equal(Strings.UnexpectedError, vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Save_does_not_reenter_while_busy()
    {
        var gate = new TaskCompletionSource<DomainApplyResult>();
        var calls = 0;
        var vm = new CustomDomainsViewModel(new DomainSelection(), EffectiveCatalog.Builtin,
            _ => { calls++; return gate.Task; },
            (_, _) => Task.FromResult<IReadOnlyList<ProbeResult>>([]), _time);
        vm.AddText = "reddit.com";
        vm.Add();

        var first = vm.SaveAsync();
        Assert.False(await vm.SaveAsync());
        gate.SetResult(new DomainApplyResult(true, null));

        Assert.True(await first);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Save_error_text_appends_suffix_and_applying_detail_counts_domains()
    {
        var vm = Create("reddit.com");

        Assert.Null(vm.SaveErrorText);
        Assert.Equal(
            $"Tắt rồi bật lại Định tuyến DNS và Proxy SNI với {FeatureDomains.Routed(vm.Current).Count} tên miền. Lỗi thì LunaDNS tự quay về danh sách cũ.",
            vm.ApplyingDetail);
    }

    [Fact]
    public async Task Save_error_text_after_failure()
    {
        _applyResult = new DomainApplyResult(false, "Cổng bận.");
        var vm = Create("reddit.com");
        vm.Rows[0].Enabled = false;

        await vm.SaveAsync();

        Assert.Equal("Cổng bận. Máy vẫn đang chạy theo danh sách cũ; sửa xong bấm Lưu lại.", vm.SaveErrorText);
    }

    [Fact]
    public void Custom_row_also_in_an_enabled_group_is_flagged_and_kept()
    {
        _catalog = EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>> { [DomainGroup.Steam] = ["cdn.example.net"] }, null);

        var vm = Create("cdn.example.net", "other.example.org");

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(Strings.GroupSteam, vm.Rows[0].AlsoInGroup);
        Assert.Null(vm.Rows[1].AlsoInGroup);
    }

    [Fact]
    public void Adding_a_domain_from_the_layer_two_list_is_a_duplicate()
    {
        _catalog = EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>> { [DomainGroup.Steam] = ["cdn.example.net"] }, null);
        var vm = Create();
        vm.AddText = "cdn.example.net";

        vm.Add();

        Assert.Empty(vm.Rows);
        Assert.Single(vm.AddErrors);
    }

    [Fact]
    public void Committing_an_unchanged_row_also_in_a_group_is_not_a_duplicate()
    {
        _catalog = EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>> { [DomainGroup.Steam] = ["cdn.example.net"] }, null);
        var vm = Create("cdn.example.net");
        var row = vm.Rows[0];
        row.IsEditing = true;
        row.EditText = "cdn.example.net";

        Assert.True(vm.CommitEdit(row));
        Assert.Null(row.EditError);
        Assert.False(row.IsEditing);
    }
}
