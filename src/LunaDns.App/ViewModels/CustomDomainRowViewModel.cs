namespace LunaDns.App.ViewModels;

/// <summary>Một dòng trong hộp thoại "Tên miền tuỳ chỉnh". Kết quả kiểm tra chỉ sống trong phiên (spec KH8 5.3).</summary>
internal sealed class CustomDomainRowViewModel(string domain, bool enabled) : ObservableObject
{
    private string _domain = domain;
    private bool _enabled = enabled;
    private bool _isSelected;
    private bool _isEditing;
    private string _editText = domain;
    private string? _probeText;
    private Tone? _probeTone;
    private string? _probeDetail;

    /// <summary>Domain hoặc Enabled đổi: hộp thoại tính lại số đếm và IsDirty.</summary>
    public event EventHandler? Changed;

    public string Domain { get => _domain; set { if (SetProperty(ref _domain, value)) Changed?.Invoke(this, EventArgs.Empty); } }

    public bool Enabled { get => _enabled; set { if (SetProperty(ref _enabled, value)) Changed?.Invoke(this, EventArgs.Empty); } }

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public bool IsEditing { get => _isEditing; set { if (SetProperty(ref _isEditing, value) && value) EditText = Domain; } }

    public string EditText { get => _editText; set => SetProperty(ref _editText, value); }

    public string? ProbeText { get => _probeText; set => SetProperty(ref _probeText, value); }

    public Tone? ProbeTone { get => _probeTone; set => SetProperty(ref _probeTone, value); }

    public string? ProbeDetail { get => _probeDetail; set => SetProperty(ref _probeDetail, value); }
}
