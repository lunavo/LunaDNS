using LunaDns.Core.Domains;

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
    private string? _editError;

    /// <summary>Domain hoặc Enabled đổi: hộp thoại tính lại số đếm và IsDirty.</summary>
    public event EventHandler? Changed;

    public string Domain
    {
        get => _domain;
        set
        {
            if (!SetProperty(ref _domain, value)) return;
            OnPropertyChanged(nameof(DeleteName));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Tên đọc cho nút xoá chỉ có icon ("Xoá reddit.com").</summary>
    public string DeleteName => Strings.Format(nameof(Strings.CustomDeleteFormat), _domain);

    public bool Enabled { get => _enabled; set { if (SetProperty(ref _enabled, value)) Changed?.Invoke(this, EventArgs.Empty); } }

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public bool IsEditing { get => _isEditing; set { if (SetProperty(ref _isEditing, value) && value) EditText = Domain; } }

    public string EditText
    {
        get => _editText;
        set
        {
            if (SetProperty(ref _editText, value)) OnPropertyChanged(nameof(EditHint));
        }
    }

    /// <summary>Dòng gợi ý dưới ô sửa: tên sẽ lưu sau khi chuẩn hoá (mockup 2.3).</summary>
    public string EditHint => Strings.Format(nameof(Strings.CustomEditHintFormat), DomainPolicy.Normalize(_editText) ?? _editText.Trim());

    /// <summary>Lý do không nhận chữ đang sửa; hiện ngay dưới ô sửa của dòng này (spec KH8 5.2).</summary>
    public string? EditError
    {
        get => _editError;
        set
        {
            if (SetProperty(ref _editError, value)) OnPropertyChanged(nameof(HasEditError));
        }
    }

    public bool HasEditError => _editError is not null;

    /// <summary>Esc: bỏ chữ đang sửa, giữ tên cũ.</summary>
    public void CancelEdit()
    {
        IsEditing = false;
        EditError = null;
    }

    public string? ProbeText { get => _probeText; set => SetProperty(ref _probeText, value); }

    public Tone? ProbeTone { get => _probeTone; set => SetProperty(ref _probeTone, value); }

    public string? ProbeDetail { get => _probeDetail; set => SetProperty(ref _probeDetail, value); }
}
