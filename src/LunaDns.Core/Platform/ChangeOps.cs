namespace LunaDns.Core.Platform;

/// <summary>Tên thao tác ghi trong journal. Không đổi giá trị: journal cũ trên máy khách dùng các chuỗi này.</summary>
public static class ChangeOps
{
    public const string DnsRoute = "dns-route";
    public const string AutoProxy = "auto-proxy";
    public const string HostsEntries = "hosts-entries";
}
