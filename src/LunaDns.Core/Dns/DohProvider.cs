namespace LunaDns.Core.Dns;

/// <summary>Máy chủ DoH chính người dùng chọn (D2). D3 duyệt các giá trị này bằng <c>settings.WithDoh(x)</c>.</summary>
public enum DohProvider
{
    Cloudflare,
    Google,
    Quad9,
    AdGuard,
    Custom,
}

/// <summary>Một lựa chọn đo / thử được: giá trị lưu vào settings cùng máy chủ tương ứng.</summary>
public sealed record DohChoice(DohProvider Provider, DohServer Server);
