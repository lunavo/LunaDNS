using System.Collections.ObjectModel;
using System.Text;
using LunaDns.App.Features;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
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
    private readonly EffectiveCatalog _catalog;
    private readonly Func<DomainSelection, Task<DomainApplyResult>> _apply;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ProbeResult>>> _probe;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly IReadOnlyList<string> _enabledCatalog;
    private string _addText = "";
    private IReadOnlyList<string> _addErrors = [];
    private bool _isBusy;
    private string? _errorMessage;
    private string? _reportText;
    private Tone _reportTone;
    private (int Index, CustomDomainRowViewModel Row)? _lastDeleted;
    private ITimer? _undoTimer;
    private int _undoGeneration;
    private int _undoLeft;
    private bool _saving;

    public CustomDomainsViewModel(
        DomainSelection initial,
        EffectiveCatalog catalog,
        Func<DomainSelection, Task<DomainApplyResult>> apply,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ProbeResult>>> probe,
        TimeProvider time,
        Action<Action>? post = null)
    {
        _initial = initial ?? throw new ArgumentNullException(nameof(initial));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _post = post ?? (a => a());
        _enabledCatalog = [.. DomainCatalog.Selectable.Where(initial.IsEnabled).SelectMany(catalog.For)];
        Rows = [.. initial.Custom.Select(c => Track(new CustomDomainRowViewModel(c.Domain, c.Enabled)))];
        Rows.CollectionChanged += (_, _) => RaiseListChanged();
    }

    public ObservableCollection<CustomDomainRowViewModel> Rows { get; }

    public string AddText
    {
        get => _addText;
        set
        {
            if (SetProperty(ref _addText, value)) OnPropertyChanged(nameof(CanAdd));
        }
    }

    /// <summary>Nút Thêm mờ khi ô rỗng hoặc đang bận.</summary>
    public bool CanAdd => !_isBusy && !string.IsNullOrWhiteSpace(_addText);

    /// <summary>Lỗi của lần Thêm gần nhất, mỗi mục "chữ nhập: câu báo" (spec KH8 4.2).</summary>
    public IReadOnlyList<string> AddErrors
    {
        get => _addErrors;
        private set
        {
            if (SetProperty(ref _addErrors, value)) OnPropertyChanged(nameof(HasAddErrors));
        }
    }

    public bool HasAddErrors => _addErrors.Count > 0;

    /// <summary>Đang kiểm tra hoặc đang áp: hộp thoại khoá mọi nút, ô nhập và nút đóng.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanAdd));
            OnPropertyChanged(nameof(CanProbe));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(IsSaving));
            OnPropertyChanged(nameof(ApplyingDetail));
        }
    }

    public bool IsIdle => !_isBusy;

    /// <summary>Đang chạy SaveAsync (khác với đang kiểm tra): hiện khung "Đang áp dụng…".</summary>
    public bool IsSaving => _isBusy && _saving;

    /// <summary>Lỗi của lần Lưu gần nhất (câu của DomainApplyResult).</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(SaveErrorText));
            OnPropertyChanged(nameof(HasSaveError));
        }
    }

    /// <summary>Nội dung khung "Chưa lưu được": câu lỗi nối với lời nhắc máy vẫn chạy danh sách cũ (mockup 2.5).</summary>
    public string? SaveErrorText => _errorMessage is null ? null : $"{_errorMessage} {Strings.CustomSaveFailedSuffix}";

    public bool HasSaveError => _errorMessage is not null;

    /// <summary>Câu tổng kết của lần Thêm hoặc Nhập .txt gần nhất.</summary>
    public string? ReportText
    {
        get => _reportText;
        private set
        {
            if (SetProperty(ref _reportText, value)) OnPropertyChanged(nameof(HasReport));
        }
    }

    public bool HasReport => _reportText is not null;

    public Tone ReportTone { get => _reportTone; private set => SetProperty(ref _reportTone, value); }

    public string ApplyingDetail => Strings.Format(nameof(Strings.CustomApplyingDetailFormat), FeatureDomains.Routed(Current, _catalog).Count);

    public bool CanProbe => !_isBusy && Rows.Count > 0;

    public bool CanSave => !_isBusy && IsDirty;

    public string ProbeSelectedText
    {
        get
        {
            var selected = Rows.Count(r => r.IsSelected);
            return selected == 0 ? Strings.CustomProbeSelected : Strings.Format(nameof(Strings.CustomProbeSelectedFormat), selected);
        }
    }

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
        var added = 0;
        foreach (var item in DomainPolicy.SplitInput(AddText))
        {
            var check = DomainPolicy.Check(item, Domains(), _enabledCatalog);
            if (check.Domain is null) continue;
            if (check.Ok)
            {
                Rows.Add(Track(new CustomDomainRowViewModel(check.Domain, true)));
                added++;
            }
            else
            {
                rejected.Add(item);
                errors.Add($"{item}: {Explain(check)}");
            }
        }
        AddText = string.Join('\n', rejected);
        AddErrors = errors;
        ReportText = rejected.Count == 0
            ? Strings.Format(nameof(Strings.CustomAddedAllFormat), added)
            : Strings.Format(nameof(Strings.CustomAddedFormat), added, rejected.Count);
        ReportTone = added > 0 ? Tone.Good : Tone.Neutral;
    }

    /// <summary>Nhận chữ đang sửa của dòng (Enter). Sai thì giữ tên cũ, báo ở EditError của chính dòng đó và trả false.</summary>
    public bool CommitEdit(CustomDomainRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var others = Rows.Where(r => !ReferenceEquals(r, row)).Select(r => r.Domain).ToList();
        var check = DomainPolicy.Check(row.EditText, others, _enabledCatalog);
        if (!check.Ok)
        {
            row.EditError = $"{row.EditText}: {Explain(check)}";
            return false;
        }
        row.EditError = null;
        if (!string.Equals(row.Domain, check.Domain, StringComparison.Ordinal))
        {
            row.Domain = check.Domain!;
            row.AlsoInGroup = AlsoInGroup(row.Domain);
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
        ReportText = Strings.Format(nameof(Strings.CustomImportReportFormat), added, skipped);
        ReportTone = Tone.Neutral;
        return ReportText;
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
        if (IsBusy) return false; // nút Lưu mờ khi bận; chặn cả Enter/bấm đúp gọi chồng
        _saving = true;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _apply(Current);
            if (!result.Success) ErrorMessage = result.Message ?? Strings.UnexpectedError;
            return result.Success;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ranh giới của nút Lưu (như AsyncCommand): exception thành câu báo trong hộp thoại, không làm sập giao diện.
            ErrorMessage = Strings.UnexpectedError;
            return false;
        }
        finally
        {
            _saving = false;
            IsBusy = false;
            OnPropertyChanged(nameof(IsSaving));
        }
    }

    /// <summary>Nhập/Xuất .txt lỗi đọc ghi: báo ở dòng tổng kết.</summary>
    public void ShowFileError(string detail)
    {
        ReportText = Strings.Format(nameof(Strings.CustomFileFailedFormat), detail);
        ReportTone = Tone.Error;
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

    /// <summary>Nhóm dựng sẵn đang bật đầu tiên đã có tên miền này (lớp 1 ∪ lớp 2).</summary>
    private string? AlsoInGroup(string domain) =>
        DomainCatalog.Selectable.Where(_initial.IsEnabled).Where(g => DomainMatcher.Matches(domain, _catalog.For(g)))
            .Select(Strings.GroupName).FirstOrDefault();

    private CustomDomainRowViewModel Track(CustomDomainRowViewModel row)
    {
        row.AlsoInGroup = AlsoInGroup(row.Domain);
        row.Changed += (_, _) => RaiseListChanged();
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CustomDomainRowViewModel.IsSelected)) OnPropertyChanged(nameof(ProbeSelectedText));
        };
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
        OnPropertyChanged(nameof(CanProbe));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ProbeSelectedText));
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
