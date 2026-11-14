using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>UID của tiến trình ở đầu kia Unix socket, do kernel cung cấp (getpeereid(2)): không giả mạo được từ phía client.</summary>
internal static partial class PeerCredentials
{
    public static uint? EffectiveUid(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        var ok = GetPeerEid((int)socket.SafeHandle.DangerousGetHandle(), out var uid, out _) == 0;
        return ok ? uid : null;
    }

    [LibraryImport("libc", EntryPoint = "getpeereid", SetLastError = true)]
    private static partial int GetPeerEid(int socket, out uint euid, out uint egid);
}
