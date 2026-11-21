using LunaDns.Core.Domains;

namespace LunaDns.App.ViewModels;

/// <summary>Hộp thoại cho VM, để VM không tham chiếu Window.</summary>
internal interface IUserPrompts
{
    Task<bool> ConfirmAsync(string title, string message);

    Task InformAsync(string title, string message);

    /// <summary>Cửa sổ chỉ đọc liệt kê tên miền của một nhóm dựng sẵn kèm nguồn: có sẵn hay bản cập nhật (mockup 3.1, spec D4).</summary>
    Task ShowDomainListAsync(string title, IReadOnlyList<CatalogEntry> entries, DateTimeOffset? issued);

    /// <summary>Hộp thoại "Tên miền tuỳ chỉnh". true = đã Lưu và áp thành công.</summary>
    Task<bool> EditCustomDomainsAsync(CustomDomainsViewModel vm);
}
