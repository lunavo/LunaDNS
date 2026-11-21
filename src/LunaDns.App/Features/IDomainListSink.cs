using LunaDns.Core.Domains;

namespace LunaDns.App.Features;

/// <summary>
/// Phần của controller mà DomainListUpdater cần (D4): hỏi trạng thái và đưa nguyên byte file đã tải cho nơi giữ thật
/// (Windows: DomainListService trong tiến trình; macOS: daemon xác minh lại từ đầu).
/// </summary>
internal interface IDomainListSink
{
    /// <summary>null: dịch vụ nền không trả lời hoặc là bản cũ (ẩn dòng "Tự cập nhật danh sách").</summary>
    Task<DomainListState?> GetDomainListAsync();

    Task<DomainListOfferResult> OfferDomainListAsync(byte[] file);
}
