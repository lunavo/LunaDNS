using LunaDns.App.Updates;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Dải báo "có bản mới" (spec 2026-11-15). Hiện khi tự kiểm lúc mở app hay nút "Kiểm tra cập nhật" thấy bản mới;
/// UpToDate/Failed không đổi gì. Tải mở trang Release (URL đã lọc trong UpdateChecker), ✕ ẩn đến lần mở app sau.
/// </summary>
internal sealed class UpdateBannerViewModel : ObservableObject
{
    private readonly ISystemShell _shell;
    private readonly string _current;
    private UpdateCheckResult? _update;

    public UpdateBannerViewModel(ISystemShell shell, Version current, Action<Exception> onError)
    {
        _shell = shell;
        _current = UpdateChecker.Normalize(current).ToString();
        OpenCommand = new AsyncCommand(OpenAsync, onError);
        DismissCommand = new AsyncCommand(DismissAsync, onError);
    }

    public AsyncCommand OpenCommand { get; }

    public AsyncCommand DismissCommand { get; }

    public bool IsVisible => _update is not null;

    public string Text => _update is null ? string.Empty : Strings.Format(nameof(Strings.UpdateBannerFormat), _update.Latest, _current);

    public void Show(UpdateCheckResult result)
    {
        if (result.Status == UpdateStatus.NewerAvailable) Set(result);
    }

    private Task OpenAsync()
    {
        if (_update?.Url is { } url) _shell.OpenUrl(url);
        return Task.CompletedTask;
    }

    private Task DismissAsync()
    {
        Set(null);
        return Task.CompletedTask;
    }

    private void Set(UpdateCheckResult? update)
    {
        _update = update;
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(Text));
    }
}
