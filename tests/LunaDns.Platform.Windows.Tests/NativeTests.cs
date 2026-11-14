namespace LunaDns.Platform.Windows.Tests;

[Collection(RealSystemCollection.Name)]
public class NativeTests
{
    [Fact]
    public void WinInet_accepts_settings_changed_notification() =>
        Assert.True(WinInet.NotifySettingsChanged());

    [AdminFact]
    public void Dns_cache_flush_succeeds_as_admin() =>
        Assert.True(WindowsDns.FlushResolverCache());
}
