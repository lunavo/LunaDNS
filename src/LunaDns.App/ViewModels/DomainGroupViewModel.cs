using LunaDns.App.Features;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Một dòng của khu "Tên miền" (spec KH8 5.1). Mỗi lần bật/tắt là một lần áp trọn vẹn.
/// Ô tích bind OneWay + ToggleCommand (như ToggleSwitch của FeatureViewModel): ô đã tự lật lúc bấm, Refresh đẩy lại giá trị thật.
/// Dòng trạng thái: trạng thái tạm (Đã giữ lại / Chưa áp được) &gt; kết quả Kiểm tra &gt; chữ mặc định theo mockup.
/// </summary>
internal sealed class DomainGroupViewModel : ObservableObject
{
    /// <summary>Nhóm đang tắt và chưa có kết quả: mockup chỉ vẽ một gạch ngang.</summary>
    public const string NoStatus = "—";

    private readonly Func<DomainSelection> _current;
    private readonly Func<EffectiveCatalog> _catalog;
    private readonly Func<DomainSelection, Task<DomainApplyResult>> _apply;
    private readonly Action<string> _showError;
    private (string Text, Tone Tone)? _probe;
    private (string Text, Tone Tone)? _temporary;
    private bool _busy;

    public DomainGroupViewModel(
        DomainGroup group,
        Func<DomainSelection> current,
        Func<EffectiveCatalog> catalog,
        Func<DomainSelection, Task<DomainApplyResult>> apply,
        Action<string> showError,
        Func<DomainGroupViewModel, Task>? open = null,
        Action<Exception>? onError = null)
    {
        Group = group;
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _showError = showError ?? throw new ArgumentNullException(nameof(showError));
        onError ??= _ => { };
        ToggleCommand = new AsyncCommand(() => SetEnabledAsync(!IsEnabled), onError);
        OpenCommand = new AsyncCommand(() => open?.Invoke(this) ?? Task.CompletedTask, onError);
    }

    public DomainGroup Group { get; }

    public bool IsCustom => Group == DomainGroup.Custom;

    public string Title => Strings.GroupName(Group);

    /// <summary>Nút cuối dòng: "Xem" cho nhóm dựng sẵn, "Sửa…" cho Tuỳ chỉnh.</summary>
    public string OpenText => IsCustom ? Strings.GroupEditButton : Strings.GroupViewButton;

    public AsyncCommand ToggleCommand { get; }

    public AsyncCommand OpenCommand { get; }

    public bool IsEnabled
    {
        get => _current().IsEnabled(Group);
        set => _ = SetEnabledAsync(value);
    }

    public string CountText
    {
        get
        {
            var current = _current();
            if (IsCustom)
                return Strings.Format(nameof(Strings.GroupCustomCountFormat), current.Custom.Count(c => c.Enabled), current.Custom.Count);
            var catalog = _catalog();
            var added = catalog.Added(Group).Count;
            return added == 0
                ? Strings.Format(nameof(Strings.GroupCountFormat), catalog.For(Group).Count)
                : Strings.Format(nameof(Strings.GroupCountWithUpdateFormat), catalog.For(Group).Count, added);
        }
    }

    public string StatusText => _temporary?.Text ?? Fallback().Text;

    /// <summary>null: không vẽ icon trạng thái (gạch ngang, "Chưa có tên miền").</summary>
    public Tone? StatusTone => _temporary?.Tone ?? Fallback().Tone;

    public bool HasStatusIcon => StatusTone is not null;

    /// <summary>Viền đỏ của dòng bị giữ lại (mockup 1.3).</summary>
    public bool IsKeptBack => _temporary is { Tone: Tone.Error };

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_busy || enabled == IsEnabled)
        {
            Refresh();
            return;
        }
        _busy = true;
        try
        {
            var result = await _apply(_current().WithGroup(Group, enabled));
            if (!result.Success && result.Message is { } message)
            {
                if (result.Error == FeatureError.NoDomains)
                    ShowTemporary(Strings.GroupKeptBack, Tone.Error);
                else if (message == Strings.DaemonNeedsUpdate)
                    ShowTemporary(Strings.GroupNotApplied, Tone.Warning);
                _showError(message);
            }
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    /// <summary>Kết quả Kiểm tra của nhóm (null: nhóm không được kiểm). Xoá luôn trạng thái tạm.</summary>
    public void ShowProbe(string? text, Tone? tone)
    {
        _probe = text is null || tone is null ? null : (text, tone.Value);
        _temporary = null;
        RaiseStatus();
    }

    public void ShowTemporary(string text, Tone tone)
    {
        _temporary = (text, tone);
        RaiseStatus();
    }

    public void ClearTemporary()
    {
        if (_temporary is null) return;
        _temporary = null;
        RaiseStatus();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(CountText));
        RaiseStatus();
    }

    private (string Text, Tone? Tone) Fallback()
    {
        var current = _current();
        if (IsCustom && current.Custom.Count == 0) return (Strings.GroupCustomEmpty, null);
        if (!current.IsEnabled(Group)) return (NoStatus, null);
        return _probe is { } probe ? (probe.Text, probe.Tone) : (Strings.GroupNotChecked, Tone.Neutral);
    }

    private void RaiseStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(HasStatusIcon));
        OnPropertyChanged(nameof(IsKeptBack));
    }
}
