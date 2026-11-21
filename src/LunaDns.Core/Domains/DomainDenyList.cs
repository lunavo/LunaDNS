// src/LunaDns.Core/Domains/DomainDenyList.cs
namespace LunaDns.Core.Domains;

public sealed record DeniedDomain(string Domain, string Reason);

/// <summary>
/// Tên miền LunaDNS không bao giờ định tuyến (spec KH8 4.4). Khớp cả ba chiều: trùng, tên miền con, tên miền cha.
/// Luật DNS luôn gồm tên miền con, nên định tuyến tên cha sẽ kéo theo tên bị cấm.
/// </summary>
public static class DomainDenyList
{
    private const string AntiCheat = "anti-cheat, định tuyến sai có thể bị khoá tài khoản game";
    private const string OwnDoh = "máy chủ DoH của chính LunaDNS, định tuyến sẽ gây vòng lặp";
    private const string Connectivity = "hệ điều hành dùng để kiểm tra mạng và trang đăng nhập Wi-Fi";

    public static IReadOnlyList<DeniedDomain> Domains { get; } =
    [
        new("accounts.pubg.com", AntiCheat),
        new("easyanticheat.net", AntiCheat),
        new("battleye.com", AntiCheat),
        // EA Javelin (eaanticheat.ac.ea.com); cấm phòng xa theo quyết định PO 21/11/2026, nguồn: diễn đàn EA (chưa đọc được nguyên văn).
        new("ac.ea.com", AntiCheat),
        new("cloudflare-dns.com", OwnDoh),
        new("one.one.one.one", OwnDoh),
        new("dns.google", OwnDoh),
        new("msftconnecttest.com", Connectivity),
        new("msftncsi.com", Connectivity),
        new("captive.apple.com", Connectivity),
        new("connectivitycheck.gstatic.com", Connectivity),
    ];

    /// <summary>Nhãn chứa chuỗi này là anti-cheat (spec v1 4.4: <c>*xenuine*</c>).</summary>
    private static readonly string[] LabelFragments = ["xenuine"];

    public static DeniedDomain? Find(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        foreach (var fragment in LabelFragments)
            if (host.Contains(fragment, StringComparison.Ordinal)) return new DeniedDomain(host, AntiCheat);
        foreach (var denied in Domains)
        {
            var d = denied.Domain;
            if (host == d || IsUnder(host, d) || IsUnder(d, host)) return denied;
        }
        return null;
    }

    private static bool IsUnder(string child, string parent) =>
        child.Length > parent.Length && child.EndsWith(parent, StringComparison.Ordinal) && child[child.Length - parent.Length - 1] == '.';
}
