using System.Collections.ObjectModel;
using System.Text;
using LunaDns.App.Features;
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Hộp thoại "Tên miền tuỳ chỉnh" (spec KH8 5.2). Chỉ áp dụng khi SaveAsync; Huỷ không đổi gì trên máy.
/// Bộ đếm hoàn tác chạy trên luồng của TimeProvider; <c>post</c> chuyển nhịp đếm về luồng UI (app truyền Dispatcher.UIThread.Post).
/// </summary>
internal sealed class CustomDomainsViewModel : ObservableObject, IDisposable
{
    private const string OffPrefix = "# off: ";
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan UndoTick = TimeSpan.FromSeconds(1);

    private readonly DomainSelection _initial;
    private readonly Func<DomainSelection, Task<DomainApplyResult>> _apply;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ProbeResult>>> _probe;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly IReadOnlyList<string> _enabledCatalog;
    private string _addText = "";
    private IReadOnlyList<string> _addErrors = [];
    private bool _isBusy;
    private string? _errorMessage;
    private (int Index, CustomDomainRowViewModel Row)? _lastDeleted;
    private ITimer? _undoTimer;
    private int _undoGeneration;
    private int _undoLeft;

    public CustomDomainsViewModel(
        DomainSelection initial,
        Func<DomainSelection, Task<DomainApplyResult>> apply,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ProbeResult>>> probe,
        TimeProvider time,
        Action<Action>? post = null)
    {
        _initial = initial ?? throw new ArgumentNullException(nameof(initial));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _post = post ?? (a => a());
        _enabledCatalog = [.. DomainCatalog.Selectable.Where(initial.IsEnabled).SelectMany(DomainCatalog.For)];
        Rows = [.. initial.Custom.Select(c => Track(new CustomDomainRowViewModel(c.Domain, c.Enabled)))];
        Rows.CollectionChanged += (_, _) => RaiseListChanged();
    }

    public ObservableCollection<CustomDomainRowViewModel> Rows { get; }

    public string AddText { get => _addText; set => SetProperty(ref _addText, value); }

    /// <summary>Lỗi của lần Thêm gần nhất, mỗi mục "chữ nhập: câu báo" (spec KH8 4.2).</summary>
    public IReadOnlyList<string> AddErrors { get => _addErrors; private set => SetProperty(ref _addErrors, value); }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public string? UndoText => _lastDeleted is { } d
        ? Strings.Format(nameof(Strings.CustomDeletedFormat), d.Row.Domain)
        : null;

    public string? UndoSecondsText => _lastDeleted is null
        ? null
        : Strings.Format(nameof(Strings.CustomUndoSecondsFormat), _undoLeft);

    /// <summary>Danh sách đang rỗng: hộp thoại hiện màn trống.</summary>
    public bool IsEmpty => Rows.Count == 0;

    public string CountText => Strings.Format(nameof(Strings.CustomCountFormat),
        Rows.Count(r => r.Enabled), Rows.Count, DomainPolicy.MaxCustomDomains);

    public DomainSelection Current => _initial with { Custom = [.. Rows.Select(r => new CustomDomain(r.Domain, r.Enabled))] };

    public bool IsDirty => !Current.SameAs(_initial);

    /// <summary>Thêm mọi mục hợp lệ trong ô nhập; mục lỗi ở lại trong ô (mỗi mục một dòng) kèm câu báo.</summary>
    public void Add()
    {
        var rejected = new List<string>();
        var errors = new List<string>();
        foreach (var item in DomainPolicy.SplitInput(AddText))
        {
            var check = DomainPolicy.Check(item, Domains(), _enabledCatalog);
            if (check.Domain is null) continue;
            if (check.Ok)
            {
                Rows.Add(Track(new CustomDomainRowViewModel(check.Domain, true)));
            }
            else
            {
                rejected.Add(item);
                errors.Add($"{item}: {Explain(check)}");
            }
        }
        AddText = string.Join('\n', rejected);
        AddErrors = errors;
    }

    /// <summary>Nhận chữ đang sửa của dòng (Enter). Sai thì giữ tên cũ, báo ở ErrorMessage và trả false.</summary>
    public bool CommitEdit(CustomDomainRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var others = Rows.Where(r => !ReferenceEquals(r, row)).Select(r => r.Domain).ToList();
        var check = DomainPolicy.Check(row.EditText, others, _enabledCatalog);
        if (!check.Ok)
        {
            ErrorMessage = $"{row.EditText}: {Explain(check)}";
            return false;
        }
        ErrorMessage = null;
        if (!string.Equals(row.Domain, check.Domain, StringComparison.Ordinal))
        {
            row.Domain = check.Domain!;
            row.ProbeText = null;
            row.ProbeTone = null;
            row.ProbeDetail = null;
        }
        row.IsEditing = false;
        return true;
    }

    /// <summary>Xoá ngay, không hỏi; mời hoàn tác trong 6 giây. Xoá dòng khác thì lần xoá trước thành vĩnh viễn.</summary>
    public void Delete(CustomDomainRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var index = Rows.IndexOf(row);
        if (index < 0) return;
        Rows.RemoveAt(index);
        _undoTimer?.Dispose();
        _lastDeleted = (index, row);
        _undoLeft = (int)UndoWindow.TotalSeconds;
        var generation = ++_undoGeneration;
        _undoTimer = _time.CreateTimer(_ => _post(() => TickUndo(generation)), null, UndoTick, UndoTick);
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(UndoSecondsText));
    }

    public void Undo()
    {
        if (_lastDeleted is not { } d) return;
        Rows.Insert(Math.Min(d.Index, Rows.Count), d.Row);
        ClearUndo();
    }

    /// <summary>Gộp file .txt vào danh sách (không thay thế). Dòng "# off: x" nhập thành dòng tắt. Trả câu tổng kết.</summary>
    public string Import(string fileText)
    {
        ArgumentNullException.ThrowIfNull(fileText);
        int added = 0, skipped = 0;
        foreach (var rawLine in fileText.Split('\n'))
        {
            var line = rawLine.Trim();
            var enabled = !line.StartsWith(OffPrefix, StringComparison.Ordinal);
            if (!enabled) line = line[OffPrefix.Length..].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var check = DomainPolicy.Check(line, Domains(), _enabledCatalog);
            if (check.Ok)
            {
                Rows.Add(Track(new CustomDomainRowViewModel(check.Domain!, enabled)));
                added++;
            }
            else if (check.Domain is not null)
            {
                skipped++;
            }
        }
        return Strings.Format(nameof(Strings.CustomImportReportFormat), added, skipped);
    }

    /// <summary>Mỗi dòng một tên miền; dòng đang tắt có tiền tố "# off: " để Import đọc lại được.</summary>
    public string Export()
    {
        var text = new StringBuilder();
        foreach (var row in Rows) text.Append(row.Enabled ? "" : OffPrefix).Append(row.Domain).Append('\n');
        return text.ToString();
    }

    public async Task ProbeAsync(bool selectedOnly, CancellationToken cancellationToken)
    {
        var rows = Rows.Where(r => !selectedOnly || r.IsSelected).ToList();
        if (rows.Count == 0) return;
        IsBusy = true;
        try
        {
            var results = await _probe([.. rows.Select(r => r.Domain)], cancellationToken);
            foreach (var row in rows)
            {
                var result = results.FirstOrDefault(r => string.Equals(r.Host, row.Domain, StringComparison.Ordinal));
                if (result is null)
                {
                    MarkFailed(row);
                    continue;
                }
                row.ProbeText = DomainProbeText.For(result.Verdict);
                row.ProbeTone = DomainProbeText.ToneFor(result.Verdict);
                row.ProbeDetail = Strings.Format(nameof(Strings.CustomProbeDetailFormat),
                    string.Join(", ", result.SystemAddresses), string.Join(", ", result.DohAddresses));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ranh giới của nút bấm (như AsyncCommand): lỗi mạng hay huỷ đều thành "Không kiểm tra được" (spec KH8 5.3).
            foreach (var row in rows) MarkFailed(row);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Áp danh sách mới. true = đóng hộp thoại; false = giữ mở, lỗi ở ErrorMessage, trên máy vẫn là danh sách cũ.</summary>
    public async Task<bool> SaveAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _apply(Current);
            if (!result.Success) ErrorMessage = result.Message;
            return result.Success;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        _undoTimer?.Dispose();
        _undoTimer = null;
    }

    private List<string> Domains() => [.. Rows.Select(r => r.Domain)];

    private static void MarkFailed(CustomDomainRowViewModel row)
    {
        row.ProbeText = DomainProbeText.Failed;
        row.ProbeTone = Tone.Error;
        row.ProbeDetail = null;
    }

    private CustomDomainRowViewModel Track(CustomDomainRowViewModel row)
    {
        row.Changed += (_, _) => RaiseListChanged();
        return row;
    }

    private void TickUndo(int generation)
    {
        if (generation != _undoGeneration || _lastDeleted is null) return; // nhịp muộn của bộ đếm cũ
        _undoLeft--;
        if (_undoLeft <= 0) ClearUndo();
        else OnPropertyChanged(nameof(UndoSecondsText));
    }

    private void ClearUndo()
    {
        _undoTimer?.Dispose();
        _undoTimer = null;
        _undoGeneration++;
        _lastDeleted = null;
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(UndoSecondsText));
    }

    private void RaiseListChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static string Explain(DomainCheck check) => check.Domain is null ? Strings.DomainRejectSyntax : check.Rejection switch
    {
        DomainRejection.PublicSuffix => Strings.Format(nameof(Strings.DomainRejectPublicSuffixFormat), check.Domain),
        DomainRejection.LocalName => Strings.DomainRejectLocal,
        DomainRejection.Denied => Strings.Format(nameof(Strings.DomainRejectDeniedFormat), check.Detail),
        DomainRejection.Duplicate => Strings.Format(nameof(Strings.DomainRejectDuplicateFormat), check.Detail),
        DomainRejection.Limit => Strings.Format(nameof(Strings.DomainRejectLimitFormat), DomainPolicy.MaxCustomDomains),
        _ => Strings.DomainRejectSyntax,
    };
}
