namespace LunaDns.App.ViewModels;

/// <summary>Hộp thoại cho VM, để VM không tham chiếu Window.</summary>
internal interface IUserPrompts
{
    Task<bool> ConfirmAsync(string title, string message);

    Task InformAsync(string title, string message);

    /// <summary>Cửa sổ chỉ đọc liệt kê tên miền của một nhóm dựng sẵn (mockup 3.1).</summary>
    Task ShowDomainListAsync(string title, IReadOnlyList<string> domains);

    /// <summary>Hộp thoại "Tên miền tuỳ chỉnh". true = đã Lưu và áp thành công.</summary>
    Task<bool> EditCustomDomainsAsync(CustomDomainsViewModel vm);
}
