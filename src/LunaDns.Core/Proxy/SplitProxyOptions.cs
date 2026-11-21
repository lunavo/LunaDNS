using LunaDns.Core.Tls;

namespace LunaDns.Core.Proxy;

public sealed record SplitProxyOptions
{
    public const int DefaultFirstPort = 18080;
    public const int DefaultLastPort = 18090;

    /// <summary>Domain được phép CONNECT và có mặt trong PAC. Host khác nhận 403 (spec 4.3).</summary>
    public required IReadOnlyList<string> Domains { get; init; }

    /// <summary>Proxy thử lần lượt từ <see cref="FirstPort"/> tới <see cref="LastPort"/>. Cổng 0 chỉ dùng trong test.</summary>
    public int FirstPort { get; init; } = DefaultFirstPort;

    public int LastPort { get; init; } = DefaultLastPort;

    /// <summary>Cắt ClientHello ở đâu và có tách TLS record không (mục Nâng cao, spec 4.3 và D1).</summary>
    public HelloSplit Split { get; init; } = HelloSplit.Default;

    /// <summary>Chờ giữa hai mảnh ClientHello, 0–20 ms. Mặc định 0: chỉ cần NoDelay và hai lần Write.</summary>
    public TimeSpan SplitDelay { get; init; } = TimeSpan.Zero;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Domains);
        ArgumentNullException.ThrowIfNull(Split);
        if (FirstPort is < 0 or > 65535 || LastPort is < 0 or > 65535 || FirstPort > LastPort)
            throw new ArgumentException($"Khoảng cổng proxy không hợp lệ: {FirstPort}–{LastPort}.");
        if (SplitDelay < TimeSpan.Zero || SplitDelay > SplitWriter.MaxDelay)
            throw new ArgumentException("Độ trễ cắt gói phải nằm trong khoảng 0–20 ms.");
    }
}
