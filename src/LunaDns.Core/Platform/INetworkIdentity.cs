using LunaDns.Core.Net;

namespace LunaDns.Core.Platform;

/// <summary>Đọc mạng đang dùng đường mặc định. Không có gateway (mất mạng) thì null. Không ném.</summary>
public interface INetworkIdentity
{
    NetworkSnapshot? Current();
}
