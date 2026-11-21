using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Settings;

/// <summary>Lựa chọn của người dùng (mục Nâng cao, ô "Kiểm tra cả PUBG", đã đọc cảnh báo PUBG). Bất biến; đổi bằng <c>with</c>.</summary>
public sealed record AppSettings
{
    public const int MaxSplitDelayMs = 20;

    public SplitMode SplitMode { get; init; } = SplitMode.FirstByte;

    public int SplitDelayMs { get; init; }

    public bool PreferGoogleDoh { get; init; }

    public bool CheckPubg { get; init; }

    public bool PubgWarningAccepted { get; init; }

    public DomainSelection Domains { get; init; } = new();

    /// <summary>Đã bấm Xem/Sửa hoặc bật/tắt nhóm lần đầu: khu Tên miền thôi hiện huy hiệu "MỚI" (spec KH8 5.1).</summary>
    public bool DomainsIntroSeen { get; init; }

    /// <summary>Thứ tự máy chủ DoH: Cloudflare trước (spec 4.2), hoặc Google trước nếu người dùng chọn.</summary>
    public IReadOnlyList<DohServer> DohServers() =>
        PreferGoogleDoh ? [DohServer.Google, DohServer.Cloudflare] : DohServer.Defaults;

    public FeatureTuning Tuning() =>
        new(new HelloSplit(SplitMode), TimeSpan.FromMilliseconds(Math.Clamp(SplitDelayMs, 0, MaxSplitDelayMs)));
}
