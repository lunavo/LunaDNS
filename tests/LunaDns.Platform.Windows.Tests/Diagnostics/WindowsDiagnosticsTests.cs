using System.Text;
using LunaDns.Platform.Windows.Diagnostics;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;
using LunaDns.Platform.Windows.Tests.Nrpt;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Tests.Diagnostics;

/// <summary>NRPT giả, khoá HKU\&lt;SID&gt;\Software\LunaDNS.Tests\&lt;guid&gt; và file hosts tạm; không cần Admin.</summary>
public sealed class WindowsDiagnosticsTests : IDisposable
{
    private readonly string _sid = AutoProxyRegistry.CurrentUserSid();
    private readonly string _testKey = $@"Software\LunaDNS.Tests\{Guid.NewGuid():N}";
    private readonly TempDirectory _dir = new();
    private readonly FakeNrptStore _nrpt = new();
    private readonly WindowsDiagnostics _diagnostics;

    public WindowsDiagnosticsTests()
    {
        Registry.Users.CreateSubKey($@"{_sid}\{_testKey}").Dispose();
        _diagnostics = new WindowsDiagnostics(_nrpt, new AutoProxyRegistry(_testKey, () => true), new HostsFileStore(_dir.PathOf("hosts")));
    }

    public void Dispose()
    {
        Registry.Users.DeleteSubKeyTree($@"{_sid}\{_testKey}", throwOnMissingSubKey: false);
        _dir.Dispose();
    }

    private void SetAutoConfig(string value)
    {
        using var key = Registry.Users.OpenSubKey($@"{_sid}\{_testKey}", writable: true)!;
        key.SetValue(AutoProxyRegistry.ValueName, value, RegistryValueKind.String);
    }

    [Fact]
    public void Lists_only_rules_marked_by_lunadns()
    {
        _nrpt.Rules.Add(FakeNrptStore.Tailscale());
        _nrpt.Rules.Add(new NrptRule("{11111111-2222-3333-4444-555555555555}", [".steampowered.com", ".steamcommunity.com"],
            ["127.0.0.1"], NrptDnsRouter.MarkerPrefix + "abc", NrptDnsRouter.DisplayName));

        var lines = _diagnostics.LunaDnsNrptRules();

        var line = Assert.Single(lines);
        Assert.Equal("LunaDNS abc | .steampowered.com, .steamcommunity.com → 127.0.0.1", line);
    }

    [Fact]
    public void No_marked_rules_gives_empty_list()
    {
        _nrpt.Rules.Add(FakeNrptStore.Tailscale());

        Assert.Empty(_diagnostics.LunaDnsNrptRules());
    }

    [Fact]
    public void Nrpt_read_error_becomes_one_line()
    {
        _nrpt.FailList = true;

        var line = Assert.Single(_diagnostics.LunaDnsNrptRules());
        Assert.Equal("(không đọc được: giả lập lỗi đọc NRPT)", line);
    }

    [Fact]
    public void Missing_auto_config_is_empty() =>
        Assert.Equal("(trống)", _diagnostics.AutoConfigLine(_sid));

    [Fact]
    public void Loopback_auto_config_is_shown()
    {
        SetAutoConfig("http://127.0.0.1:18080/proxy.pac");

        Assert.Equal("http://127.0.0.1:18080/proxy.pac", _diagnostics.AutoConfigLine(_sid));
    }

    [Fact]
    public void Foreign_auto_config_is_hidden()
    {
        SetAutoConfig("http://wpad.corp.local/proxy.pac");

        var line = _diagnostics.AutoConfigLine(_sid);

        Assert.DoesNotContain("corp.local", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Returns_only_marked_hosts_lines()
    {
        File.WriteAllText(_dir.PathOf("hosts"),
            "# comment\r\n127.0.0.1 localhost\r\n127.0.0.1 prod-live-front.playbattlegrounds.com # LunaDNS\r\n", Encoding.UTF8);

        var line = Assert.Single(_diagnostics.MarkedHostsLines());
        Assert.Equal("127.0.0.1 prod-live-front.playbattlegrounds.com # LunaDNS", line);
    }

    [Fact]
    public void Missing_hosts_file_becomes_one_line()
    {
        var line = Assert.Single(_diagnostics.MarkedHostsLines());

        Assert.StartsWith("(không đọc được: ", line, StringComparison.Ordinal);
    }
}
