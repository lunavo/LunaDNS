using System.Security;
using System.Security.Principal;
using LunaDns.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Proxy;

/// <summary>
/// AutoConfigURL (WinINet) của MỘT người dùng, mở qua HKEY_USERS\&lt;SID&gt; chứ không qua HKCU: tác vụ dọn dẹp chạy bằng SYSTEM (KH4b)
/// vẫn hoàn tác đúng hive của người đã bật. Hoàn tác chỉ khi giá trị vẫn là của LunaDNS. Không đụng DefaultConnectionSettings hay WPAD (spec 4.5).
/// </summary>
public sealed partial class AutoProxyRegistry
{
    public const string InternetSettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string ValueName = "AutoConfigURL";
    private const string ProxyEnableName = "ProxyEnable";

    private readonly string _subKey;
    private readonly Func<bool> _notify;
    private readonly ILogger _logger;

    public AutoProxyRegistry(string subKey = InternetSettingsSubKey, Func<bool>? notify = null, ILogger<AutoProxyRegistry>? logger = null)
    {
        _subKey = subKey;
        _notify = notify ?? WinInet.NotifySettingsChanged;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public static string CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value
            ?? throw new ConfiguratorException(ConfiguratorError.Other, "Không đọc được SID của người dùng hiện tại.");
    }

    public AutoProxySnapshot Plan(Uri pacUrl, bool allowOverride, string? userSid = null)
    {
        ArgumentNullException.ThrowIfNull(pacUrl);
        if (!pacUrl.IsAbsoluteUri || pacUrl.Scheme != Uri.UriSchemeHttp || !pacUrl.IsLoopback)
            throw new ArgumentException("Địa chỉ PAC phải có dạng http://127.0.0.1:<cổng>/proxy.pac.", nameof(pacUrl));
        var sid = userSid ?? CurrentUserSid();
        EnsureHiveLoaded(sid);

        using var key = Registry.Users.OpenSubKey(KeyPath(sid));
        var hadValue = key?.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase) == true;
        string? previous = null;
        var expand = false;
        if (hadValue)
        {
            var kind = key!.GetValueKind(ValueName);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                throw new ConfiguratorException(ConfiguratorError.UnexpectedRegistryValue,
                    "Giá trị AutoConfigURL hiện có không phải chuỗi nên LunaDNS không ghi đè.");
            previous = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            expand = kind == RegistryValueKind.ExpandString;
        }
        var manualProxy = key?.GetValue(ProxyEnableName) is int enabled && enabled != 0;
        if (!allowOverride && (!string.IsNullOrEmpty(previous) || manualProxy))
            throw new ConfiguratorException(ConfiguratorError.ExistingProxyConfig,
                "Máy đang dùng cấu hình proxy riêng (file PAC hoặc proxy thủ công). Bật Proxy SNI sẽ thay cấu hình đó cho tới khi tắt.");
        return new AutoProxySnapshot(sid, hadValue, previous, expand, pacUrl.AbsoluteUri);
    }

    public void Apply(AutoProxySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureHiveLoaded(snapshot.UserSid);
        WithRegistryAccess(() =>
        {
            using var key = Registry.Users.CreateSubKey(KeyPath(snapshot.UserSid), writable: true);
            key.SetValue(ValueName, snapshot.NewValue, RegistryValueKind.String);
        });
        Notify();
    }

    public void Revert(AutoProxySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureHiveLoaded(snapshot.UserSid);
        var changed = false;
        WithRegistryAccess(() =>
        {
            using var key = Registry.Users.OpenSubKey(KeyPath(snapshot.UserSid), writable: true);
            var current = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (key is null || !string.Equals(current, snapshot.NewValue, StringComparison.Ordinal))
            {
                LogValueNotOurs(_logger);
                return;
            }
            if (snapshot.HadPreviousValue)
                key.SetValue(ValueName, snapshot.PreviousValue ?? string.Empty,
                    snapshot.PreviousWasExpandString ? RegistryValueKind.ExpandString : RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            changed = true;
        });
        if (changed) Notify();
    }

    /// <summary>Giá trị AutoConfigURL hiện tại (thô, không expand) cho báo cáo chẩn đoán. Không ghi gì.</summary>
    public string? ReadCurrent(string? userSid = null)
    {
        var sid = userSid ?? CurrentUserSid();
        EnsureHiveLoaded(sid);
        using var key = Registry.Users.OpenSubKey(KeyPath(sid));
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
    }

    private string KeyPath(string sid) => $@"{sid}\{_subKey}";

    private void Notify()
    {
        if (!_notify()) LogNotifyFailed(_logger);
    }

    /// <summary>SID lấy từ journal: kiểm tra đúng định dạng (chặn đường dẫn lạ) và hive của người đó đang được nạp.</summary>
    private static void EnsureHiveLoaded(string sid)
    {
        _ = new SecurityIdentifier(sid);
        using var hive = Registry.Users.OpenSubKey(sid);
        if (hive is null)
            throw new ConfiguratorException(ConfiguratorError.UserHiveNotLoaded,
                "Người dùng đã bật Proxy SNI hiện chưa đăng nhập; cấu hình proxy sẽ được khôi phục khi người đó đăng nhập.");
    }

    private static void WithRegistryAccess(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new ConfiguratorException(ConfiguratorError.AccessDenied, "Không có quyền sửa cấu hình proxy của người dùng.", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AutoConfigURL không còn là giá trị LunaDNS đã ghi (đã đổi hoặc đã hoàn tác); giữ nguyên.")]
    private static partial void LogValueNotOurs(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows không nhận thông báo đổi cấu hình proxy (InternetSetOption).")]
    private static partial void LogNotifyFailed(ILogger logger);
}
