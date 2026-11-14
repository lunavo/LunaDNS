using System.Net;

namespace LunaDns.Core.Dns;

/// <summary>
/// "Tên → danh sách IPv4". Tên không tồn tại hoặc không có bản ghi A thì trả danh sách rỗng.
/// v1 chỉ dùng IPv4: Prober so sánh hai nguồn trên cùng một họ địa chỉ, và proxy nối ra bằng IPv4.
/// </summary>
public interface IHostResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
