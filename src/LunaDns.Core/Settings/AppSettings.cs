using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Settings;

/// <summary>Lựa chọn của người dùng (mục Nâng cao, ô "Kiểm tra cả PUBG", đã đọc cảnh báo PUBG). Bất biến; đổi bằng <c>with</c>.</summary>
public sealed record AppSettings
{
    public const int MaxSplitDelayMs = 20;

    public SplitMode SplitMode { get; init; } = SplitMode.FirstByte;

    public int SplitDelayMs { get; init; }

    /// <summary>Tách ClientHello thành hai TLS record (D1). Mặc định tắt.</summary>
    public bool SplitRecords { get; init; }

    /// <summary>Trường cũ (trước D2). Chỉ dùng khi <see cref="Doh"/> là <c>null</c>; <see cref="WithDoh"/> giữ nó đồng bộ cho bản app cũ.</summary>
    public bool PreferGoogleDoh { get; init; }

    /// <summary>Máy DoH chính (D2). <c>null</c>: settings.json cũ, suy từ <see cref="PreferGoogleDoh"/>.</summary>
    public DohProvider? Doh { get; init; }

    /// <summary>URL tự nhập, dùng khi <see cref="Doh"/> là <see cref="DohProvider.Custom"/>.</summary>
    public CustomDoh? CustomDoh { get; init; }

    public bool CheckPubg { get; init; }

    public bool PubgWarningAccepted { get; init; }

    public DomainSelection Domains { get; init; } = new();

    /// <summary>Đã bấm Xem/Sửa hoặc bật/tắt nhóm lần đầu: khu Tên miền thôi hiện huy hiệu "MỚI" (spec KH8 5.1).</summary>
    public bool DomainsIntroSeen { get; init; }

    /// <summary>D4: công tắc "Tự cập nhật danh sách tên miền". Tắt = ngừng tải và bỏ lớp 2 khỏi định tuyến.</summary>
    public bool DomainListUpdates { get; init; } = true;

    /// <summary>F1: công tắc "Tự bật lại". Mặc định bật; tắt thì vẫn nhớ <see cref="Wanted"/> để giao diện đề nghị bật lại.</summary>
    public bool AutoResume { get; init; } = true;

    /// <summary>F1: tính năng người dùng để bật lần cuối. Chỉ FeatureResumer đổi trường này.</summary>
    public WantedFeatures? Wanted { get; init; } = WantedFeatures.None;

    /// <summary>Hàm chứ không phải thuộc tính, để không bị ghi ra settings.json.</summary>
    public DohProvider EffectiveDoh() =>
        Doh is { } doh && Enum.IsDefined(doh)
            ? doh // giá trị lạ do sửa tay settings.json (ví dụ "Doh": 7) thì coi như chưa chọn
            : PreferGoogleDoh ? DohProvider.Google : DohProvider.Cloudflare;

    public AppSettings WithDoh(DohProvider provider) =>
        this with { Doh = provider, PreferGoogleDoh = provider == DohProvider.Google };

    /// <summary>Chuỗi DoH thật: máy chính + 2 dự phòng (D2). Tự nhập chưa dùng được thì về chuỗi Cloudflare.</summary>
    public IReadOnlyList<DohServer> DohServers() =>
        DohCatalog.Chain(EffectiveDoh() == DohProvider.Custom
            ? CustomDohParser.ToServer(CustomDoh) ?? DohServer.Cloudflare
            : DohCatalog.Get(EffectiveDoh()));

    /// <summary>Chuỗi chỉ gồm máy dựng sẵn, để phân giải host của URL tự nhập mà không phụ thuộc chính nó.</summary>
    public IReadOnlyList<DohServer> BootstrapDohServers() =>
        DohCatalog.Chain(EffectiveDoh() == DohProvider.Custom ? DohServer.Cloudflare : DohCatalog.Get(EffectiveDoh()));

    public IReadOnlyList<DohChoice> DohChoices() => DohCatalog.Choices(CustomDoh);

    public FeatureTuning Tuning() =>
        new(new HelloSplit(SplitMode, SplitRecords), TimeSpan.FromMilliseconds(Math.Clamp(SplitDelayMs, 0, MaxSplitDelayMs)));

    /// <summary>Tổ hợp cắt gói đang lưu (D3). Hàm, không phải thuộc tính, để không ra JSON.</summary>
    public TuneCandidate CurrentCandidate() =>
        new(new HelloSplit(SplitMode, SplitRecords), Math.Clamp(SplitDelayMs, 0, MaxSplitDelayMs));

    /// <summary>Áp kết quả D3: máy DoH qua WithDoh (giữ PreferGoogleDoh cho bản cũ), rồi điểm cắt, tách record, độ trễ.</summary>
    public AppSettings WithTuning(DohProvider doh, TuneCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return WithDoh(doh) with
        {
            SplitMode = candidate.Split.Mode,
            SplitRecords = candidate.Split.Records,
            SplitDelayMs = Math.Clamp(candidate.DelayMs, 0, MaxSplitDelayMs),
        };
    }
}
