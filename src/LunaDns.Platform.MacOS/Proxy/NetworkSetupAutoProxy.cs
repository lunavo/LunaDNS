using System.Globalization;
using System.Text.RegularExpressions;
using LunaDns.Core.Platform;
using LunaDns.Core.Proxy;
using LunaDns.Platform.MacOS.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Platform.MacOS.Proxy;

/// <summary>
/// Auto-proxy (PAC) qua <c>networksetup</c>, cho từng network service đang bật và gắn với card thật (có Device).
/// Service của VPN (Device rỗng) không bị đụng. Snapshot giữ URL + trạng thái cũ của từng service để hoàn tác đúng.
/// networksetup không có lệnh xoá URL: service vốn chưa có URL thì khi hoàn tác chỉ tắt auto-proxy (URL cũ của LunaDNS nằm lại, đã tắt).
/// </summary>
public sealed partial class NetworkSetupAutoProxy(IProcessRunner runner, ILogger<NetworkSetupAutoProxy>? logger = null)
{
    public const string Tool = "/usr/sbin/networksetup";

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public AutoProxySnapshot Plan(Uri pacUrl, bool allowOverride)
    {
        ArgumentNullException.ThrowIfNull(pacUrl);
        if (!pacUrl.IsAbsoluteUri || pacUrl.Scheme != Uri.UriSchemeHttp || !pacUrl.IsLoopback)
            throw new ArgumentException("PAC của LunaDNS phải là http trên chính máy này.", nameof(pacUrl));

        var services = ListServices();
        if (services.Count == 0)
            throw new ConfiguratorException(ConfiguratorError.Other, "Không tìm thấy card mạng nào đang bật để đặt proxy.");

        List<ServiceAutoProxy> states = [];
        var foreign = false;
        foreach (var service in services)
        {
            var (url, enabled) = GetAutoProxy(service);
            if (enabled && !string.Equals(url, pacUrl.AbsoluteUri, StringComparison.Ordinal)) foreign = true;
            if (IsEnabled(Run("-getwebproxy", service)) || IsEnabled(Run("-getsecurewebproxy", service))) foreign = true;
            states.Add(new ServiceAutoProxy(service, url, enabled));
        }
        if (foreign && !allowOverride)
            throw new ConfiguratorException(ConfiguratorError.ExistingProxyConfig,
                "Máy đang dùng cấu hình proxy riêng (file PAC hoặc proxy thủ công). Bật Proxy SNI sẽ thay cấu hình đó cho tới khi tắt.");
        return new AutoProxySnapshot("", false, null, false, pacUrl.AbsoluteUri, states);
    }

    public void Apply(AutoProxySnapshot snapshot)
    {
        var services = EnsureSnapshot(snapshot);
        foreach (var state in services)
        {
            var (url, enabled) = GetAutoProxy(state.Service);
            if (enabled && string.Equals(url, snapshot.NewValue, StringComparison.Ordinal)) continue;
            Run("-setautoproxyurl", state.Service, snapshot.NewValue);
        }
    }

    /// <summary>Service mà người dùng đã đổi sang URL khác sau khi bật thì giữ nguyên. Service đã biến mất thì bỏ qua.</summary>
    public void Revert(AutoProxySnapshot snapshot)
    {
        var services = EnsureSnapshot(snapshot);
        var present = ListServices(includeDisabled: true).ToHashSet(StringComparer.Ordinal);
        foreach (var state in services)
        {
            if (!present.Contains(state.Service)) continue;
            var (url, _) = GetAutoProxy(state.Service);
            if (!string.Equals(url, snapshot.NewValue, StringComparison.Ordinal))
            {
                LogChangedByOther(_logger, state.Service);
                continue;
            }
            if (state.PreviousUrl is { Length: > 0 } previous) Run("-setautoproxyurl", state.Service, previous);
            Run("-setautoproxystate", state.Service, state.PreviousEnabled ? "on" : "off");
        }
    }

    /// <summary>Service đang bật PAC trỏ về proxy của LunaDNS (http://127.0.0.1:&lt;cổng&gt;/proxy.pac), để báo trạng thái.</summary>
    public IReadOnlyList<string> ServicesUsingLunaDnsPac() =>
        [.. ListServices().Where(s => GetAutoProxy(s) is { Enabled: true, Url: { } u }
            && Uri.TryCreate(u, UriKind.Absolute, out var url) && url.IsLoopback && url.AbsolutePath == "/proxy.pac")];

    /// <summary>
    /// Quét dọn (spec F7): service (kể cả service đang tắt) còn bật PAC đúng địa chỉ LunaDNS ghi
    /// (<c>http://127.0.0.1:&lt;18080–18090&gt;/proxy.pac</c>) mà <paramref name="coveredUrls"/> không giữ thì tắt auto-proxy.
    /// URL cũ của người dùng đã mất cùng journal nên trả về "tắt" (máy đi thẳng). Trả số service đã tắt.
    /// </summary>
    public int SweepOrphans(IReadOnlySet<string> coveredUrls)
    {
        ArgumentNullException.ThrowIfNull(coveredUrls);
        var removed = 0;
        foreach (var service in ListServices(includeDisabled: true))
        {
            var (url, enabled) = GetAutoProxy(service);
            if (!enabled || url is null || !IsLunaDnsPac(url) || coveredUrls.Contains(url)) continue;
            Run("-setautoproxystate", service, "off");
            LogOrphanTurnedOff(_logger, service);
            removed++;
        }
        return removed;
    }

    /// <summary>Đúng dạng SplitProxy.PacUrl trong dải cổng mặc định; PAC loopback của phần mềm khác không khớp.</summary>
    internal static bool IsLunaDnsPac(string url)
    {
        const string Prefix = "http://127.0.0.1:";
        const string Suffix = "/proxy.pac";
        if (!url.StartsWith(Prefix, StringComparison.Ordinal) || !url.EndsWith(Suffix, StringComparison.Ordinal)) return false;
        var port = url[Prefix.Length..^Suffix.Length];
        return int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n is >= SplitProxyOptions.DefaultFirstPort and <= SplitProxyOptions.DefaultLastPort
            && string.Equals(port, n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    internal IReadOnlyList<string> ListServices(bool includeDisabled = false) =>
        ParseServiceOrder(Run("-listnetworkserviceorder"), includeDisabled);

    /// <summary>
    /// Đọc <c>networksetup -listnetworkserviceorder</c>: mỗi service là "(n) Tên" hoặc "(*) Tên" (đã tắt),
    /// dòng sau là "(Hardware Port: ..., Device: enX)". Device rỗng là VPN: bỏ.
    /// </summary>
    internal static IReadOnlyList<string> ParseServiceOrder(string output, bool includeDisabled)
    {
        ArgumentNullException.ThrowIfNull(output);
        List<string> services = [];
        string? pending = null;
        var pendingDisabled = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var header = ServiceHeader().Match(line);
            if (header.Success)
            {
                pending = header.Groups[2].Value;
                pendingDisabled = header.Groups[1].Value == "*";
                continue;
            }
            var port = HardwarePort().Match(line);
            if (port.Success && pending is not null)
            {
                if (port.Groups[1].Value.Trim().Length > 0 && (includeDisabled || !pendingDisabled)) services.Add(pending);
                pending = null;
            }
        }
        return services;
    }

    /// <summary>Đọc "URL: ...\nEnabled: Yes|No". "(null)" nghĩa là chưa từng đặt.</summary>
    internal static (string? Url, bool Enabled) ParseAutoProxy(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string? url = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("URL:", StringComparison.Ordinal))
            {
                var value = line[4..].Trim();
                url = value is "" or "(null)" ? null : value;
            }
        }
        return (url, IsEnabled(output));
    }

    private (string? Url, bool Enabled) GetAutoProxy(string service) => ParseAutoProxy(Run("-getautoproxyurl", service));

    private static bool IsEnabled(string output) =>
        output.Split('\n').Any(l => l.Trim().Equals("Enabled: Yes", StringComparison.Ordinal));

    private string Run(params string[] arguments)
    {
        var result = runner.Run(Tool, arguments);
        // networksetup có khi trả mã 0 nhưng in "** Error"; coi cả hai là lỗi.
        if (result.ExitCode == 0 && !result.Output.Contains("** Error", StringComparison.Ordinal)) return result.Output;
        var denied = result.Output.Contains("requires admin", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("root", StringComparison.OrdinalIgnoreCase);
        throw new ConfiguratorException(denied ? ConfiguratorError.AccessDenied : ConfiguratorError.Other,
            denied
                ? "Không có quyền đổi proxy hệ thống. LunaDNS cần chạy bằng quyền quản trị (sudo)."
                : $"networksetup {arguments[0]} lỗi: {result.Output.Trim()}");
    }

    private static IReadOnlyList<ServiceAutoProxy> EnsureSnapshot(AutoProxySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Services is null)
            throw new ArgumentException("Snapshot proxy không phải của bản macOS.", nameof(snapshot));
        if (!Uri.TryCreate(snapshot.NewValue, UriKind.Absolute, out var url) || !url.IsLoopback || url.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException("Snapshot proxy không trỏ về máy này.", nameof(snapshot));
        return snapshot.Services;
    }

    [GeneratedRegex(@"^\((\d+|\*)\)\s+(.+)$")]
    private static partial Regex ServiceHeader();

    [GeneratedRegex(@"^\(Hardware Port: .*, Device: (.*)\)$")]
    private static partial Regex HardwarePort();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auto-proxy của service {Service} đã bị đổi sau khi LunaDNS bật; giữ nguyên.")]
    private static partial void LogChangedByOther(ILogger logger, string service);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quét dọn: service {Service} còn bật PAC của LunaDNS không có trong journal; đã tắt auto-proxy.")]
    private static partial void LogOrphanTurnedOff(ILogger logger, string service);
}
