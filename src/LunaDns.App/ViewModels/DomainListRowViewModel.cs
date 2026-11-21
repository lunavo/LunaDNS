using LunaDns.App.DomainList;
using LunaDns.Core.Domains;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Dòng "Tự cập nhật danh sách tên miền" ở khu Tên miền (spec D4 "Giao diện"). Windows và macOS.
/// Trạng thái lấy từ nơi giữ thật (state) cộng lượt kiểm trong bộ nhớ (lastCheck). Kết quả "Cập nhật ngay" hiện 6 giây.
/// </summary>
internal sealed class DomainListRowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ResultDuration = TimeSpan.FromSeconds(6);

    private readonly Func<Task<DomainListState?>> _state;
    private readonly Func<bool, Task<DomainListOfferResult>> _setEnabled;
    private readonly Func<CancellationToken, Task<DomainListCheck>> _checkNow;
    private readonly Func<DomainListCheck?> _lastCheck;
    private readonly Func<bool> _isChecking;
    private readonly Func<int> _reapplyFailures;
    private readonly Func<bool> _busyElsewhere;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private DomainListState? _current;
    private string? _result;
    private ITimer? _resultTimer;

    public DomainListRowViewModel(Func<Task<DomainListState?>> state, Func<bool, Task<DomainListOfferResult>> setEnabled,
        Func<CancellationToken, Task<DomainListCheck>> checkNow, Func<DomainListCheck?> lastCheck, Func<bool> isChecking,
        Func<int> reapplyFailures, Func<bool> busyElsewhere, TimeProvider time, Action<Action>? post = null, Action<Exception>? onError = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
        _checkNow = checkNow ?? throw new ArgumentNullException(nameof(checkNow));
        _lastCheck = lastCheck ?? throw new ArgumentNullException(nameof(lastCheck));
        _isChecking = isChecking ?? throw new ArgumentNullException(nameof(isChecking));
        _reapplyFailures = reapplyFailures ?? throw new ArgumentNullException(nameof(reapplyFailures));
        _busyElsewhere = busyElsewhere ?? throw new ArgumentNullException(nameof(busyElsewhere));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _post = post ?? (a => a());
        onError ??= _ => { };
        // Cùng điều kiện với Cập nhật ngay: không bật/tắt khi một lượt áp tên miền khác đang chạy.
        ToggleCommand = new AsyncCommand(ToggleAsync, onError, () => !_isChecking() && !_busyElsewhere());
        CheckNowCommand = new AsyncCommand(CheckNowAsync, onError, () => IsEnabled && !_isChecking() && !_busyElsewhere());
    }

    public event EventHandler? CatalogChanged;

    public void Dispose() => _resultTimer?.Dispose();

    public AsyncCommand ToggleCommand { get; }

    public AsyncCommand CheckNowCommand { get; }

    public bool IsVisible => _current is not null;

    public bool IsEnabled => _current?.Enabled ?? false;

    public string? ResultText => _result;

    /// <summary>Chạy lại tính năng lỗi 2 lượt liên tiếp: dòng đỏ, chờ 24 giờ (spec D4 "Áp dụng bản mới").</summary>
    private bool IsReapplyStuck => _reapplyFailures() >= 2;

    public Tone? StatusTone => IsEnabled && IsReapplyStuck ? Tone.Error : null;

    public string StatusText
    {
        get
        {
            if (_current is not { } s) return string.Empty;
            if (!s.Enabled) return Strings.DomainListOff;
            if (_isChecking()) return Strings.DomainListChecking;
            if (IsReapplyStuck && s.Issued is { } stuck) return Strings.Format(nameof(Strings.DomainListReapplyFailedFormat), Local(stuck));
            if (_lastCheck() is { } last && Failed(last) && (s.CheckedAt is null || last.At > s.CheckedAt))
                return s.Issued is { } kept
                    ? Strings.Format(nameof(Strings.DomainListFailedKeepFormat), Local(last.At), Local(kept))
                    : Strings.Format(nameof(Strings.DomainListFailedBuiltinFormat), Local(last.At));
            if (s.Issued is { } issued && s.CheckedAt is { } checkedAt)
                return Strings.Format(nameof(Strings.DomainListOkFormat), Local(issued), Local(checkedAt));
            return Strings.DomainListNone;
        }
    }

    public async Task RefreshAsync()
    {
        var state = await _state().ConfigureAwait(true);
        _post(() =>
        {
            _current = state;
            RaiseAll();
        });
    }

    /// <summary>Gọi trên luồng UI khi lượt tải nền xong (DomainListUpdater.Changed).</summary>
    public async Task OnBackgroundCheckAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        if (_lastCheck()?.Offer?.Outcome == DomainListOutcome.Accepted) CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ToggleAsync()
    {
        if (_current is null) return;
        var result = await _setEnabled(!_current.Enabled).ConfigureAwait(true);
        if (!result.Success) ShowResult(Describe(result));
        await RefreshAsync().ConfigureAwait(true);
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task CheckNowAsync()
    {
        var check = await _checkNow(CancellationToken.None).ConfigureAwait(true);
        ShowResult(check.Kind == DomainListCheckKind.Offered ? Describe(check.Offer!) : Strings.DomainListDownloadFailed);
        await RefreshAsync().ConfigureAwait(true);
        if (check.Offer?.Outcome == DomainListOutcome.Accepted) CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _time.LocalTimeZone);

    private static bool Failed(DomainListCheck check) =>
        check.Kind is DomainListCheckKind.NetworkError or DomainListCheckKind.TooLarge || check.Offer?.Success == false;

    private static string Describe(DomainListOfferResult result) => result.Outcome switch
    {
        DomainListOutcome.Accepted => Strings.Format(nameof(Strings.DomainListAddedFormat), result.Added),
        DomainListOutcome.NotNewer => Strings.DomainListUpToDate,
        DomainListOutcome.UnknownFormat => Strings.DomainListNeedsNewerApp,
        DomainListOutcome.Malformed or DomainListOutcome.UnknownKey or DomainListOutcome.BadSignature or DomainListOutcome.Downgrade
            => Strings.DomainListBadSignature,
        DomainListOutcome.ReapplyFailed => ErrorMessages.For(result.Error),
        DomainListOutcome.SaveFailed => Strings.DomainListSaveFailed,
        _ => Strings.DomainListDownloadFailed,
    };

    private void ShowResult(string text)
    {
        _resultTimer?.Dispose();
        _result = text;
        OnPropertyChanged(nameof(ResultText));
        _resultTimer = _time.CreateTimer(_ => _post(() =>
        {
            _result = null;
            OnPropertyChanged(nameof(ResultText));
        }), null, ResultDuration, Timeout.InfiniteTimeSpan);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTone));
        ToggleCommand.RaiseCanExecuteChanged();
        CheckNowCommand.RaiseCanExecuteChanged();
    }
}
