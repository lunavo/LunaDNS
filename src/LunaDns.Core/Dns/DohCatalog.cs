namespace LunaDns.Core.Dns;

/// <summary>Các máy DoH dựng sẵn theo thứ tự ưu tiên, và cách dựng chuỗi dự phòng (D2).</summary>
public static class DohCatalog
{
    /// <summary>Máy chính + 2 dự phòng: thời gian chờ xấu nhất vẫn là 3 × <see cref="DohClient.DefaultTimeout"/>.</summary>
    public const int ChainLength = 3;

    public static IReadOnlyList<DohChoice> BuiltIn { get; } =
    [
        new(DohProvider.Cloudflare, DohServer.Cloudflare),
        new(DohProvider.Google, DohServer.Google),
        new(DohProvider.Quad9, DohServer.Quad9),
        new(DohProvider.AdGuard, DohServer.AdGuard),
    ];

    public static DohServer Get(DohProvider provider) =>
        BuiltIn.FirstOrDefault(c => c.Provider == provider)?.Server
        ?? throw new ArgumentOutOfRangeException(nameof(provider), provider, "Chỉ máy dựng sẵn mới lấy từ danh mục.");

    /// <summary>
    /// <paramref name="primary"/> rồi các máy dựng sẵn đầu tiên theo thứ tự danh mục, bỏ máy trùng host
    /// (<see cref="DohClient"/> tìm IP bootstrap theo host, hai máy cùng host sẽ lẫn nhau).
    /// </summary>
    public static IReadOnlyList<DohServer> Chain(DohServer primary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        var chain = new List<DohServer>(ChainLength) { primary };
        foreach (var server in BuiltIn.Select(c => c.Server))
        {
            if (chain.Count == ChainLength) break;
            if (chain.Any(s => string.Equals(s.Endpoint.Host, server.Endpoint.Host, StringComparison.OrdinalIgnoreCase))) continue;
            chain.Add(server);
        }
        return chain;
    }

    /// <summary>Lựa chọn đo / thử được: 4 máy dựng sẵn, thêm Tự nhập nếu đã có IP.</summary>
    public static IReadOnlyList<DohChoice> Choices(CustomDoh? custom) =>
        CustomDohParser.ToServer(custom) is { } server ? [.. BuiltIn, new DohChoice(DohProvider.Custom, server)] : BuiltIn;
}
