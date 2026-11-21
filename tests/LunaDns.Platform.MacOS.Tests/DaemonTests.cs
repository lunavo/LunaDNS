using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Settings;
using LunaDns.Platform.MacOS.Daemon;
using LunaDns.Platform.MacOS.Hosts;
using LunaDns.Platform.MacOS.Processes;
using LunaDns.Platform.MacOS.Proxy;
using LunaDns.Platform.MacOS.Resolver;

namespace LunaDns.Platform.MacOS.Tests;

public sealed class DaemonServerTests : IAsyncLifetime
{
    // Đường dẫn Unix socket tối đa 104 byte: dùng /tmp thay vì thư mục tạm dài của macOS.
    private readonly string _socket = $"/tmp/lunadns-test-{Guid.NewGuid():N}.sock";
    private DaemonServer? _server;
    private bool _allow = true;
    private DaemonRequest? _last;

    public Task InitializeAsync()
    {
        _server = DaemonServer.Start(_socket, (request, _) =>
        {
            _last = request;
            return Task.FromResult(new DaemonResponse(true, Version: "test"));
        }, _ => _allow);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
    }

    [Fact]
    public async Task Round_trip_and_socket_is_connectable_by_everyone()
    {
        var response = await new DaemonClient(_socket).SendAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.SniProxy, true));

        Assert.True(response.Ok);
        Assert.Equal("test", response.Version);
        Assert.Equal(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.SniProxy, true), _last);
        Assert.True((File.GetUnixFileMode(_socket) & UnixFileMode.OtherWrite) != 0);
    }

    [Fact]
    public async Task Unauthorized_uid_gets_refusal_and_handler_never_runs()
    {
        _allow = false;
        var response = await new DaemonClient(_socket).SendAsync(new DaemonRequest(DaemonProtocol.DisableAll));

        Assert.False(response.Ok);
        Assert.Null(_last);
    }

    [Fact]
    public async Task Garbage_and_oversized_requests_are_refused()
    {
        Assert.False((await Raw("not json\n")).Ok);
        Assert.False((await Raw(new string('a', DaemonProtocol.MaxLineBytes + 10))).Ok);
        Assert.Null(_last);
    }

    [Theory]
    [InlineData("""{"command":"set-domains","domains":{"custom":null}}""")]
    [InlineData("""{"command":"set-domains","domains":{"custom":[null]}}""")]
    [InlineData("""{"command":"set-domains","domains":{"enabledGroups":[99]}}""")]
    public async Task Malformed_domain_payloads_are_refused_and_daemon_keeps_serving(string json)
    {
        await using var tmp = new RealDaemon();

        var response = await tmp.Raw(json + "\n");

        Assert.False(response.Ok);
        Assert.False(string.IsNullOrEmpty(response.Message));
        Assert.Equal("1.2.3", (await new DaemonClient(tmp.Socket).SendAsync(new DaemonRequest(DaemonProtocol.Ping))).Version);
    }

    [Fact]
    public async Task Missing_daemon_is_reported_clearly()
    {
        await Assert.ThrowsAsync<DaemonUnavailableException>(() =>
            new DaemonClient("/tmp/lunadns-none.sock").SendAsync(new DaemonRequest(DaemonProtocol.Ping)));
    }

    [Fact]
    public async Task Dispose_removes_socket_file()
    {
        await _server!.DisposeAsync();
        _server = null;
        Assert.False(File.Exists(_socket));
    }

    private Task<DaemonResponse> Raw(string text) => RawTo(_socket, text);

    internal static async Task<DaemonResponse> RawTo(string path, string text)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path));
        // Yêu cầu quá dài: daemon ngừng đọc ở MaxLineBytes, trả lời rồi đóng, nên phần cuối của lần gửi có thể gặp EPIPE.
        var send = socket.SendAsync(Encoding.UTF8.GetBytes(text), SocketFlags.None);
        using var reader = new StreamReader(new NetworkStream(socket));
        var line = await reader.ReadLineAsync();
        try
        {
            await send;
        }
        catch (SocketException) when (text.Length > DaemonProtocol.MaxLineBytes)
        {
            // Đã có câu trả lời; lỗi gửi phần thừa là đúng như mong đợi.
        }
        return System.Text.Json.JsonSerializer.Deserialize<DaemonResponse>(line!, DaemonProtocol.Json)!;
    }
}

/// <summary>DaemonServer nối với DaemonCommands thật, để thử dữ liệu JSON thô đi hết đường tới FeatureSet.</summary>
internal sealed class RealDaemon : IAsyncDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly FeatureSet _features;
    private readonly DaemonServer _server;

    public RealDaemon()
    {
        File.WriteAllText(_tmp.Combine("hosts"), "127.0.0.1 localhost\n");
        var ns = new FakeNetworkSetup();
        var cfg = new MacNetworkConfigurator(new ResolverDnsRouter(15353, _tmp.Combine("resolver")), new NetworkSetupAutoProxy(ns),
            new UnixHostsFileStore(_tmp.Combine("hosts")), ns);
        var journal = new ChangeJournal(new JsonFileJournalStore(_tmp.Combine("journal.json")), SystemChanges.Reverters(cfg));
        _features = new FeatureSet(journal, new SystemChanges(journal, cfg), new DaemonCommandsTests.IdleServices());
        var commands = new DaemonCommands(_features, journal, "1.2.3", new JsonFileSettingsStore(_tmp.Combine("settings.json")));
        _server = DaemonServer.Start(Socket, commands.HandleAsync, _ => true);
    }

    public string Socket { get; } = $"/tmp/lunadns-test-{Guid.NewGuid():N}.sock";

    public Task<DaemonResponse> Raw(string text) => DaemonServerTests.RawTo(Socket, text);

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _features.DisposeAsync();
        _tmp.Dispose();
    }
}

public sealed class DaemonCommandsTests : IAsyncLifetime
{
    private readonly TempDirectory _tmp = new();
    private readonly FakeNetworkSetup _ns = new();
    private FeatureSet _features = null!;
    private ChangeJournal _journal = null!;
    private DaemonCommands _commands = null!;
    private MacNetworkConfigurator _cfg = null!;

    public Task InitializeAsync()
    {
        File.WriteAllText(_tmp.Combine("hosts"), "127.0.0.1 localhost\n");
        _cfg = new MacNetworkConfigurator(new ResolverDnsRouter(15353, _tmp.Combine("resolver")), new NetworkSetupAutoProxy(_ns),
            new UnixHostsFileStore(_tmp.Combine("hosts")), _ns);
        _journal = new ChangeJournal(new JsonFileJournalStore(_tmp.Combine("journal.json")), SystemChanges.Reverters(_cfg));
        _features = new FeatureSet(_journal, new SystemChanges(_journal, _cfg), new IdleServices());
        _commands = new DaemonCommands(_features, _journal, "1.2.3", new JsonFileSettingsStore(_tmp.Combine("settings.json")));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _features.DisposeAsync();
        _tmp.Dispose();
    }

    private Task<DaemonResponse> Send(string command, string? feature = null, bool allow = false) =>
        _commands.HandleAsync(new DaemonRequest(command, feature, allow), CancellationToken.None);

    [Fact]
    public async Task Enable_dns_then_disable_all_leaves_system_clean()
    {
        var on = await Send(DaemonProtocol.Enable, FeatureKeys.DnsRouting);
        Assert.True(on.Ok);
        Assert.Equal("Enabled", on.Outcome);
        Assert.True(on.Status!.DnsRouting);
        Assert.Equal(1, on.Status.JournalEntries);
        Assert.NotEmpty(_cfg.Dns.ListOurs());

        var off = await Send(DaemonProtocol.DisableAll);
        Assert.True(off.Ok);
        Assert.False(off.Status!.DnsRouting);
        Assert.Equal(0, off.Status.JournalEntries);
        Assert.Empty(_cfg.Dns.ListOurs());
    }

    [Fact]
    public async Task Existing_pac_asks_for_override_then_succeeds_with_it()
    {
        _ns.Auto["Luna Wifi"] = ("http://corp/proxy.pac", true);

        var first = await Send(DaemonProtocol.Enable, FeatureKeys.SniProxy);
        Assert.False(first.Ok);
        Assert.Equal("NeedsProxyOverride", first.Outcome);
        Assert.Contains("--allow-proxy-override", first.Message, StringComparison.Ordinal);

        var second = await Send(DaemonProtocol.Enable, FeatureKeys.SniProxy, allow: true);
        Assert.True(second.Ok);
        Assert.Equal(18080, second.Status!.ProxyPort);
    }

    [Fact]
    public async Task Pubg_and_unknown_inputs_are_refused()
    {
        Assert.Contains("PUBG", (await Send(DaemonProtocol.Enable, FeatureKeys.PubgMode)).Message, StringComparison.Ordinal);
        Assert.False((await Send(DaemonProtocol.Enable, "x")).Ok);
        Assert.False((await Send("rm -rf")).Ok);
        Assert.Equal("1.2.3", (await Send(DaemonProtocol.Ping)).Version);
    }

    private DaemonCommands CreateCommands(JsonFileSettingsStore store) => new(_features, _journal, "test", store);

    [Fact]
    public async Task Set_domains_validates_applies_and_persists()
    {
        using var dir = new TempDirectory();
        var store = new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json"));
        var commands = CreateCommands(store);
        var wanted = new DomainSelection { EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom], Custom = [new("example.org")] };

        var response = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.SetDomains, Domains: wanted), CancellationToken.None);

        Assert.True(response.Ok);
        Assert.True(store.Load().Domains.SameAs(wanted));
        var read = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.GetDomains), CancellationToken.None);
        Assert.True(read.Domains!.SameAs(wanted));
    }

    [Theory]
    [InlineData("com.vn")]
    [InlineData("Example.org")]      // chưa chuẩn hoá: daemon không tự sửa dữ liệu từ app
    [InlineData("../etc/passwd")]
    public async Task Set_domains_rejects_invalid_entries_without_touching_anything(string bad)
    {
        using var dir = new TempDirectory();
        var store = new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json"));
        var commands = CreateCommands(store);

        var response = await commands.HandleAsync(
            new DaemonRequest(DaemonProtocol.SetDomains, Domains: new DomainSelection { Custom = [new("ok.example.org"), new(bad)] }),
            CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("dòng 2", response.Message, StringComparison.Ordinal);
        Assert.NotNull(response.Status); // kèm trạng thái: giao diện không coi là bị từ chối quyền
        Assert.False(File.Exists(Path.Combine(dir.Path, "settings.json")));
    }

    /// <summary>Lỗi dữ liệu (thiếu danh sách, sai dạng, quá 200 dòng) vẫn kèm Status như lỗi ReapplyAsync.</summary>
    [Fact]
    public async Task Set_domains_validation_failures_carry_status()
    {
        using var dir = new TempDirectory();
        var commands = CreateCommands(new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json")));
        DomainSelection?[] cases =
        [
            null,
            new DomainSelection { Custom = null! },
            new DomainSelection { Custom = [.. Enumerable.Range(0, DomainPolicy.MaxCustomDomains + 1).Select(i => new CustomDomain($"d{i}.example.org"))] },
        ];

        foreach (var wanted in cases)
        {
            var response = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.SetDomains, Domains: wanted), CancellationToken.None);

            Assert.False(response.Ok);
            Assert.False(string.IsNullOrEmpty(response.Message));
            Assert.NotNull(response.Status);
        }
    }

    [Fact]
    public void Protocol_round_trips_domain_selection_as_json()
    {
        var request = new DaemonRequest(DaemonProtocol.SetDomains, Domains: new DomainSelection { EnabledGroups = [DomainGroup.Reddit] });
        var json = JsonSerializer.Serialize(request, DaemonProtocol.Json);

        Assert.Contains("\"reddit\"", json, StringComparison.OrdinalIgnoreCase);
        var back = JsonSerializer.Deserialize<DaemonRequest>(json, DaemonProtocol.Json)!;
        Assert.Equal([DomainGroup.Reddit], back.Domains!.EnabledGroups);
    }

    [Fact]
    public async Task Set_domains_reports_unwritable_settings_and_keeps_old_selection()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "settings.json");
        Directory.CreateDirectory(path); // settings.json là thư mục: File.Move ghi đè sẽ lỗi IOException
        var commands = CreateCommands(new JsonFileSettingsStore(path));

        var response = await commands.HandleAsync(
            new DaemonRequest(DaemonProtocol.SetDomains, Domains: new DomainSelection { EnabledGroups = [DomainGroup.Reddit] }),
            CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("settings.json", response.Message, StringComparison.Ordinal);
        var read = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.GetDomains), CancellationToken.None);
        Assert.True(read.Domains!.SameAs(new DomainSelection()));
    }

    internal sealed class IdleServices : IFeatureServices
    {
        public IRunningService StartDnsStub(IReadOnlyList<string> domains) => new Idle();

        public IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning) => new Idle();

        public IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning) => new Idle();
    }

    private sealed class Idle : IRunningProxy
    {
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _done.Task;

        public Uri PacUrl => new("http://127.0.0.1:18080/proxy.pac");

        public int Port => 18080;

        public int ActiveConnections => 0;

        public ValueTask DisposeAsync()
        {
            _done.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}

public class ConsoleUserTests
{
    [Fact]
    public void Root_and_console_owner_are_allowed_others_not()
    {
        var user = new ConsoleUser(new StatRunner("501\n"));
        Assert.True(user.IsAllowed(0));
        Assert.True(user.IsAllowed(501));
        Assert.False(user.IsAllowed(502));
    }

    [Fact]
    public void Unknown_console_owner_allows_only_root()
    {
        var user = new ConsoleUser(new StatRunner("garbage"));
        Assert.True(user.IsAllowed(0));
        Assert.False(user.IsAllowed(501));
    }

    private sealed class StatRunner(string output) : IProcessRunner
    {
        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments) => new(0, output);
    }
}
