namespace LunaDns.Core.Dns;

/// <summary>
/// Lấy IP bootstrap cho URL tự nhập qua một <see cref="IHostResolver"/> DoH (chuỗi dựng sẵn), không bao giờ qua DNS hệ thống.
/// IP sai hay cũ chỉ làm lỗi kết nối: TLS vẫn kiểm chứng chỉ theo host của URL.
/// </summary>
public static class CustomDohBootstrap
{
    public static async Task<CustomDoh> ResolveAsync(CustomDoh custom, IHostResolver resolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custom);
        ArgumentNullException.ThrowIfNull(resolver);
        if (!CustomDohParser.TryParseEndpoint(custom.Endpoint, out var endpoint))
            throw new ArgumentException("URL DoH tự nhập không hợp lệ.", nameof(custom));
        if ((custom.ManualAddresses?.Count ?? 0) > 0 || CustomDohParser.LiteralAddress(endpoint) is not null)
            return custom;

        var host = endpoint.DnsSafeHost;
        var addresses = await resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0) throw new DohException($"Không tìm thấy IP cho {host}.");
        return custom with { ResolvedAddresses = [.. addresses.Select(a => a.ToString())] };
    }
}
