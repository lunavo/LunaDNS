using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Proxy;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Tests.Proxy;

/// <summary>Dùng khoá riêng HKU\&lt;SID&gt;\Software\LunaDNS.Tests\&lt;guid&gt; thay cho Internet Settings thật; không cần Admin.</summary>
public sealed class AutoProxyRegistryTests : IDisposable
{
    private static readonly Uri PacUrl = new("http://127.0.0.1:18080/proxy.pac");
    private readonly string _sid = AutoProxyRegistry.CurrentUserSid();
    private readonly string _testKey = $@"Software\LunaDNS.Tests\{Guid.NewGuid():N}";
    private readonly AutoProxyRegistry _registry;
    private int _notifications;

    public AutoProxyRegistryTests()
    {
        Registry.Users.CreateSubKey($@"{_sid}\{_testKey}").Dispose();
        _registry = new AutoProxyRegistry(_testKey, () =>
        {
            _notifications++;
            return true;
        });
    }

    public void Dispose() => Registry.Users.DeleteSubKeyTree($@"{_sid}\{_testKey}", throwOnMissingSubKey: false);

    private RegistryKey Key() => Registry.Users.OpenSubKey($@"{_sid}\{_testKey}", writable: true)!;

    private object? CurrentValue()
    {
        using var key = Key();
        return key.GetValue(AutoProxyRegistry.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    [Fact]
    public void Apply_then_revert_without_previous_value_removes_value()
    {
        var snapshot = _registry.Plan(PacUrl, allowOverride: false);
        Assert.False(snapshot.HadPreviousValue);

        _registry.Apply(snapshot);
        Assert.Equal(PacUrl.AbsoluteUri, CurrentValue());
        _registry.Revert(snapshot);

        Assert.Null(CurrentValue());
        Assert.Equal(2, _notifications);
    }

    [Fact]
    public void Override_restores_previous_expand_string_exactly()
    {
        using (var key = Key()) key.SetValue(AutoProxyRegistry.ValueName, @"%USERPROFILE%\corp.pac", RegistryValueKind.ExpandString);

        var snapshot = _registry.Plan(PacUrl, allowOverride: true);
        _registry.Apply(snapshot);
        _registry.Revert(snapshot);

        using var after = Key();
        Assert.Equal(RegistryValueKind.ExpandString, after.GetValueKind(AutoProxyRegistry.ValueName));
        Assert.Equal(@"%USERPROFILE%\corp.pac", CurrentValue());
    }

    [Fact]
    public void Plan_refuses_existing_pac_without_override()
    {
        using (var key = Key()) key.SetValue(AutoProxyRegistry.ValueName, "http://corp.example/proxy.pac");

        var error = Assert.Throws<ConfiguratorException>(() => _registry.Plan(PacUrl, allowOverride: false));

        Assert.Equal(ConfiguratorError.ExistingProxyConfig, error.Error);
    }

    [Fact]
    public void Plan_refuses_manual_proxy_without_override()
    {
        using (var key = Key()) key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);

        var error = Assert.Throws<ConfiguratorException>(() => _registry.Plan(PacUrl, allowOverride: false));

        Assert.Equal(ConfiguratorError.ExistingProxyConfig, error.Error);
    }

    [Fact]
    public void Revert_keeps_value_changed_after_apply()
    {
        var snapshot = _registry.Plan(PacUrl, allowOverride: false);
        _registry.Apply(snapshot);
        using (var key = Key()) key.SetValue(AutoProxyRegistry.ValueName, "http://user-choice.example/x.pac");

        _registry.Revert(snapshot);

        Assert.Equal("http://user-choice.example/x.pac", CurrentValue());
        Assert.Equal(1, _notifications);
    }

    [Fact]
    public void Revert_twice_is_noop()
    {
        var snapshot = _registry.Plan(PacUrl, allowOverride: false);
        _registry.Apply(snapshot);

        _registry.Revert(snapshot);
        _registry.Revert(snapshot);

        Assert.Null(CurrentValue());
        Assert.Equal(2, _notifications);
    }

    [Theory]
    [InlineData("http://example.com/proxy.pac")]
    [InlineData("https://127.0.0.1:18080/proxy.pac")]
    public void Plan_rejects_non_loopback_or_non_http_url(string url)
    {
        Assert.Throws<ArgumentException>(() => _registry.Plan(new Uri(url), allowOverride: true));
    }

    [Fact]
    public void Revert_for_user_without_loaded_hive_throws_hive_not_loaded()
    {
        var snapshot = new AutoProxySnapshot("S-1-5-21-1-2-3-999999", false, null, false, PacUrl.AbsoluteUri);

        var error = Assert.Throws<ConfiguratorException>(() => _registry.Revert(snapshot));

        Assert.Equal(ConfiguratorError.UserHiveNotLoaded, error.Error);
    }

    [Fact]
    public void Revert_rejects_malformed_sid()
    {
        var snapshot = new AutoProxySnapshot(@"..\Other", false, null, false, PacUrl.AbsoluteUri);

        Assert.Throws<ArgumentException>(() => _registry.Revert(snapshot));
    }
}
