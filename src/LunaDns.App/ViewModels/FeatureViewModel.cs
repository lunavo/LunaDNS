using System.Globalization;
using LunaDns.Core.Features;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Một thẻ công tắc (spec 4.8, KH6 U6). IsOn chỉ đổi qua Update; ToggleSwitch bind OneWay.
/// Tone: Đang xử lý &gt; Lỗi &gt; Bật &gt; Tắt; dải màu bên trái, icon và dòng trạng thái đổi theo Tone.
/// </summary>
internal sealed class FeatureViewModel : ObservableObject
{
    private bool _isOn;
    private bool _isBusy;
    private bool _isAvailable = true;
    private string _statusText = Strings.StatusOff;
    private string _errorText = string.Empty;

    public FeatureViewModel(Feature feature, Func<FeatureViewModel, Task> toggle, Action<Exception> onError)
    {
        Feature = feature;
        Title = Strings.FeatureTitle(feature);
        Description = Strings.FeatureDescription(feature);
        ToggleCommand = new AsyncCommand(() => toggle(this), onError);
    }

    public Feature Feature { get; }

    /// <summary>Chọn icon của huy hiệu (PathIcon.feature.dns).</summary>
    public bool IsDns => Feature == Feature.DnsRouting;

    public string Title { get; }

    public string Description { get; }

    public AsyncCommand ToggleCommand { get; }

    public bool IsOn => _isOn;

    /// <summary>MainViewModel đặt trong lúc bật/tắt: khoá toggle, hiện thanh tiến trình.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanToggle));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Tone));
        }
    }

    /// <summary>macOS: false khi dịch vụ nền chưa cài hoặc không trả lời (toggle xám, banner giải thích).</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (SetProperty(ref _isAvailable, value)) OnPropertyChanged(nameof(CanToggle));
        }
    }

    public bool CanToggle => !_isBusy && _isAvailable;

    public string StatusText => _isBusy ? Strings.FeatureBusy : _statusText;

    public string ErrorText => _errorText;

    public bool HasError => _errorText.Length > 0;

    public Tone Tone => _isBusy ? Tone.Busy : HasError ? Tone.Error : _isOn ? Tone.Good : Tone.Neutral;

    /// <summary>Lý do ngắn hiện trên thẻ; chuỗi rỗng để xoá.</summary>
    public void SetError(string text)
    {
        if (_errorText == text) return;
        _errorText = text;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Tone));
    }

    /// <summary>Luôn phát IsOn, kể cả khi không đổi: ToggleSwitch đã tự lật lúc bấm, binding OneWay cần được đẩy lại giá trị thật.</summary>
    public void Update(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _isOn = IsDns ? status.DnsRouting : status.SniProxy;
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(Tone));
        UpdateStatusText(status);
    }

    /// <summary>Gọi từ timer: chỉ đổi dòng trạng thái, không đụng IsOn.</summary>
    public void UpdateStatusText(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var text = !_isOn ? Strings.StatusOff
            : Feature == Feature.SniProxy && status.ProxyPort is int port
                ? Strings.Format(nameof(Strings.StatusProxyFormat), port.ToString(CultureInfo.InvariantCulture), status.ProxyConnections)
            : IsDns && status.Doh is { } doh
                ? Strings.Format(doh.Fallback ? nameof(Strings.StatusDnsDohFallbackFormat) : nameof(Strings.StatusDnsDohFormat), doh.Server)
                : Strings.StatusOn;
        if (_statusText == text) return;
        _statusText = text;
        OnPropertyChanged(nameof(StatusText));
    }
}
