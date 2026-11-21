namespace LunaDns.Core.Dns;

/// <summary>
/// URL DoH tự nhập (D2). IP lưu dạng chuỗi cho gọn JSON; <see cref="CustomDohParser.ToServer"/> parse lúc dựng máy chủ.
/// <see cref="ResolvedAddresses"/> lấy qua DoH dựng sẵn lúc bấm Lưu hoặc Đo, không bao giờ qua DNS hệ thống.
/// </summary>
public sealed record CustomDoh
{
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Ô "IP (tuỳ chọn)". Có thì dùng, không hỏi DoH.</summary>
    public IReadOnlyList<string> ManualAddresses { get; init; } = [];

    public IReadOnlyList<string> ResolvedAddresses { get; init; } = [];
}
