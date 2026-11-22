using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Logging;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;
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
    public async Task Refusal_is_read_even_when_daemon_closes_before_request_is_sent()
    {
        // Daemon trả lời từ chối rồi đóng mà không đọc yêu cầu; đôi khi việc gửi của client gặp EPIPE (~2% lần trên macOS).
        _allow = false;
        var client = new DaemonClient(_socket);
        for (var i = 0; i < 1000; i++)
        {
            Assert.False((await client.SendAsync(new DaemonRequest(DaemonProtocol.DisableAll))).Ok);
        }
        Assert.Null(_last);
    }

    [Fact]
    public async Task Garbage_and_oversized_requests_are_refused()
    {
        Assert.False((await Raw("not json\n")).Ok);
        Assert.False((await Raw(new string('a', DaemonProtocol.MaxLineBytes + 10))).Ok);
        Assert.Null(_last);
    }

    [Fact]
    public async Task Line_of_100_kilobytes_is_read()
    {
        // Envelope base64 ~88 KB: trước D4 bị cắt ở 65 536 byte và trả "Yêu cầu rỗng hoặc quá dài."
        var request = new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: new string('A', 100_000));
        var response = await Raw(System.Text.Json.JsonSerializer.Serialize(request, DaemonProtocol.Json) + "\n");
        Assert.DoesNotContain("quá dài", response.Message ?? string.Empty, StringComparison.Ordinal);
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
    public async Task Dispose_right_after_a_reply_never_faults_the_accept_loop()
    {
        // Trả lời xong là vòng accept gọi AcceptAsync ngay; Dispose chen vào lúc đó có thể làm accept hoàn tất đồng bộ
        // với OperationAborted (SocketException, không phải OperationCanceledException).
        // Cửa sổ rất hẹp: 20000 vòng (~5 giây) thường bắt được lỗi cũ, không phải lần nào cũng.
        for (var i = 0; i < 20000; i++)
        {
            var path = $"/tmp/lunadns-test-{Guid.NewGuid():N}.sock";
            var server = DaemonServer.Start(path, (_, _) => Task.FromResult(new DaemonResponse(true, Version: "test")), _ => true);
            await new DaemonClient(path).SendAsync(new DaemonRequest(DaemonProtocol.Ping));
            await server.DisposeAsync();
        }
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
    private readonly DomainListService _domainList;

    public RealDaemon()
    {
        File.WriteAllText(_tmp.Combine("hosts"), "127.0.0.1 localhost\n");
        var ns = new FakeNetworkSetup();
        var cfg = new MacNetworkConfigurator(new ResolverDnsRouter(15353, _tmp.Combine("resolver")), new NetworkSetupAutoProxy(ns),
            new UnixHostsFileStore(_tmp.Combine("hosts")), ns);
        var journal = new ChangeJournal(new JsonFileJournalStore(_tmp.Combine("journal.json")), SystemChanges.Reverters(cfg));
        _features = new FeatureSet(journal, new SystemChanges(journal, cfg), new DaemonCommandsTests.IdleServices());
        _domainList = DaemonHarness.Idle(_features, _tmp.Path);
        var commands = new DaemonCommands(_features, journal, "1.2.3", new JsonFileSettingsStore(_tmp.Combine("settings.json")), _domainList);
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
    private DomainListService _domainList = null!;

    public Task InitializeAsync()
    {
        File.WriteAllText(_tmp.Combine("hosts"), "127.0.0.1 localhost\n");
        _cfg = new MacNetworkConfigurator(new ResolverDnsRouter(15353, _tmp.Combine("resolver")), new NetworkSetupAutoProxy(_ns),
            new UnixHostsFileStore(_tmp.Combine("hosts")), _ns);
        _journal = new ChangeJournal(new JsonFileJournalStore(_tmp.Combine("journal.json")), SystemChanges.Reverters(_cfg));
        _features = new FeatureSet(_journal, new SystemChanges(_journal, _cfg), new IdleServices());
        _domainList = DaemonHarness.Idle(_features, _tmp.Path);
        _commands = new DaemonCommands(_features, _journal, "1.2.3", new JsonFileSettingsStore(_tmp.Combine("settings.json")), _domainList);
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
    public async Task Enable_failure_message_carries_the_system_reason()
    {
        _ns.FailNextSetUrl = "** Error: The parameters were not valid.";

        var reply = await Send(DaemonProtocol.Enable, FeatureKeys.SniProxy);

        Assert.False(reply.Ok);
        Assert.Equal("Failed", reply.Outcome);
        Assert.Equal("Other", reply.Error);
        Assert.Contains("networksetup -setautoproxyurl lỗi: ** Error: The parameters were not valid.", reply.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_log_without_reader_is_refused_with_status()
    {
        var reply = await Send(DaemonProtocol.GetLog);

        Assert.False(reply.Ok);
        Assert.NotNull(reply.Status);
        Assert.Null(reply.Log);
    }

    [Fact]
    public async Task Get_log_returns_redacted_tail()
    {
        var logs = Directory.CreateDirectory(_tmp.Combine("logs")).FullName;
        File.WriteAllLines(Path.Combine(logs, RollingFileLoggerProvider.FileName), ["một", "hai của lunav", "ba"]);
        var commands = new DaemonCommands(_features, _journal, "1.2.3", new JsonFileSettingsStore(_tmp.Combine("settings.json")), _domainList,
            log: new DaemonLogReader(logs, () => "lunav", "máy"));

        var reply = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.GetLog, Lines: 2), CancellationToken.None);

        Assert.True(reply.Ok);
        Assert.NotNull(reply.Status);
        Assert.Equal(["hai của <user>", "ba"], reply.Log);
    }

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

    private DaemonCommands CreateCommands(JsonFileSettingsStore store) => new(_features, _journal, "test", store, _domainList);

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

    // Định dạng dây cố định: giao diện và daemon có thể khác bản (daemon cũ còn cài trên máy sau khi cập nhật app).
    private const string RequestWire =
        """{"command":"set-domains","allowProxyOverride":false,"domains":{"enabledGroups":["steam","reddit"],"custom":[{"domain":"example.com","enabled":false}]}}""";

    private const string ResponseWire =
        """{"ok":true,"version":"0.5.0","status":{"dnsRouting":true,"sniProxy":false,"proxyConnections":0,"journalEntries":2},"outcome":"applied"}""";

    [Fact]
    public void Protocol_wire_format_is_stable()
    {
        var request = new DaemonRequest(DaemonProtocol.SetDomains,
            Domains: new DomainSelection { EnabledGroups = [DomainGroup.Steam, DomainGroup.Reddit], Custom = [new CustomDomain("example.com", false)] });
        var response = new DaemonResponse(true, Version: "0.5.0", Status: new DaemonStatus(true, false, null, 0, 2), Outcome: "applied");

        Assert.Equal(RequestWire, JsonSerializer.Serialize(request, DaemonProtocol.Json));
        Assert.Equal(ResponseWire, JsonSerializer.Serialize(response, DaemonProtocol.Json));
        Assert.Equal(request, JsonSerializer.Deserialize<DaemonRequest>(RequestWire, DaemonProtocol.Json));
        Assert.Equal(response, JsonSerializer.Deserialize<DaemonResponse>(ResponseWire, DaemonProtocol.Json));
    }

    [Fact]
    public void Old_daemon_status_without_doh_fields_reads_as_unknown()
    {
        var back = JsonSerializer.Deserialize<DaemonResponse>(ResponseWire, DaemonProtocol.Json)!;

        Assert.Null(back.Status!.DohServer);
        Assert.Null(back.Status.DohFallback);
    }

    [Fact]
    public void Protocol_reads_pascal_case_enums_and_ignores_unknown_fields()
    {
        // Bản cũ đọc enum không phân biệt hoa thường và bỏ qua trường lạ; giữ như vậy để client khác bản không vỡ.
        var back = JsonSerializer.Deserialize<DaemonRequest>(
            """{"command":"set-domains","domains":{"enabledGroups":["Steam"],"custom":[]},"extra":1}""", DaemonProtocol.Json)!;
        Assert.Equal([DomainGroup.Steam], back.Domains!.EnabledGroups);
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

    private static readonly DaemonTuning Quad9MidSni = new(DohProvider.Quad9, SplitMode.MidSni, SplitRecords: true, SplitDelayMs: 10);

    [Fact]
    public async Task Get_tuning_returns_daemon_settings()
    {
        var response = await _commands.HandleAsync(new DaemonRequest(DaemonProtocol.GetTuning), CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal(new DaemonTuning(DohProvider.Cloudflare, SplitMode.FirstByte, false, 0), response.Tuning);
    }

    [Fact]
    public async Task Set_tuning_applies_then_persists()
    {
        using var dir = new TempDirectory();
        var store = new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json"));
        var commands = CreateCommands(store);
        Assert.True((await commands.HandleAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting), CancellationToken.None)).Ok);

        var response = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.SetTuning, Tuning: Quad9MidSni), CancellationToken.None);

        Assert.True(response.Ok);
        Assert.True(response.Status!.DnsRouting);
        var saved = store.Load();
        Assert.Equal(DohProvider.Quad9, saved.EffectiveDoh());
        Assert.Equal(SplitMode.MidSni, saved.SplitMode);
        Assert.True(saved.SplitRecords);
        Assert.Equal(10, saved.SplitDelayMs);
        var read = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.GetTuning), CancellationToken.None);
        Assert.Equal(Quad9MidSni, read.Tuning);
    }

    public static TheoryData<DaemonTuning?> BadTunings => new()
    {
        null,
        new DaemonTuning(DohProvider.Custom, SplitMode.FirstByte, false, 0),
        new DaemonTuning(DohProvider.Quad9, SplitMode.FirstByte, false, 25),
        new DaemonTuning(DohProvider.Quad9, SplitMode.FirstByte, false, -1),
        new DaemonTuning((DohProvider)99, SplitMode.FirstByte, false, 0),
        new DaemonTuning(DohProvider.Quad9, (SplitMode)99, false, 0),
    };

    [Theory]
    [MemberData(nameof(BadTunings))]
    public async Task Set_tuning_refuses_bad_input_without_touching_anything(DaemonTuning? bad)
    {
        using var dir = new TempDirectory();
        var commands = CreateCommands(new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json")));

        var response = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.SetTuning, Tuning: bad), CancellationToken.None);

        Assert.False(response.Ok);
        Assert.False(string.IsNullOrEmpty(response.Message));
        Assert.NotNull(response.Status);
        Assert.False(File.Exists(Path.Combine(dir.Path, "settings.json")));
    }

    [Fact]
    public async Task Set_tuning_reports_unwritable_settings_and_keeps_old_tuning()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "settings.json");
        Directory.CreateDirectory(path);
        var commands = CreateCommands(new JsonFileSettingsStore(path));

        var response = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.SetTuning, Tuning: Quad9MidSni), CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("settings.json", response.Message, StringComparison.Ordinal);
        var read = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.GetTuning), CancellationToken.None);
        Assert.Equal(DohProvider.Cloudflare, read.Tuning!.Doh);
    }

    [Fact]
    public async Task Status_reports_answering_doh_server_only_when_known()
    {
        using var dir = new TempDirectory();
        var services = new IdleServices { Doh = new DohActivity("Google", Fallback: true) };
        await using var features = new FeatureSet(_journal, new SystemChanges(_journal, _cfg), services);
        var domainList = DaemonHarness.Idle(features, dir.Path);
        var commands = new DaemonCommands(features, _journal, "test", new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json")), domainList);

        var off = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.Status), CancellationToken.None);
        Assert.Null(off.Status!.DohServer);
        Assert.Null(off.Status.DohFallback);
        Assert.DoesNotContain("doh", JsonSerializer.Serialize(off, DaemonProtocol.Json), StringComparison.OrdinalIgnoreCase);

        await commands.HandleAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting), CancellationToken.None);
        var on = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.Status), CancellationToken.None);

        Assert.Equal("Google", on.Status!.DohServer);
        Assert.True(on.Status.DohFallback);
        Assert.Contains("\"dohServer\":\"Google\",\"dohFallback\":true", JsonSerializer.Serialize(on, DaemonProtocol.Json), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_tuning_failure_turns_every_running_feature_back_on()
    {
        using var dir = new TempDirectory();
        var services = new FlakyDnsServices();
        await using var features = new FeatureSet(_journal, new SystemChanges(_journal, _cfg), services);
        var domainList = DaemonHarness.Idle(features, dir.Path);
        var commands = new DaemonCommands(features, _journal, "test", new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json")), domainList);
        Assert.True((await commands.HandleAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting), CancellationToken.None)).Ok);
        Assert.True((await commands.HandleAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.SniProxy), CancellationToken.None)).Ok);
        services.FailNextDnsStart = true;

        var response = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.SetTuning, Tuning: Quad9MidSni), CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Contains("đã giữ cấu hình cũ", response.Message, StringComparison.Ordinal);
        Assert.True(response.Status!.DnsRouting);
        Assert.NotNull(response.Status.ProxyPort);
        var read = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.GetTuning), CancellationToken.None);
        Assert.Equal(DohProvider.Cloudflare, read.Tuning!.Doh);
    }

    /// <summary>Stub DNS khởi động lỗi đúng một lần khi FailNextDnsStart bật (giả lập cổng cũ chưa nhả).</summary>
    private sealed class FlakyDnsServices : IFeatureServices
    {
        private readonly IdleServices _idle = new();

        public bool FailNextDnsStart { get; set; }

        public IRunningService StartDnsStub(IReadOnlyList<string> domains)
        {
            if (!FailNextDnsStart) return _idle.StartDnsStub(domains);
            FailNextDnsStart = false;
            throw new DnsStubException("giả lập cổng bận");
        }

        public IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning) =>
            _idle.StartSniProxy(domains, port, tuning);

        public IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning) =>
            _idle.StartPubgForwarder(domains, tuning);
    }

    [Fact]
    public async Task Set_domains_after_set_tuning_keeps_tuning()
    {
        using var dir = new TempDirectory();
        var store = new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json"));
        var commands = CreateCommands(store);
        await commands.HandleAsync(new DaemonRequest(DaemonProtocol.SetTuning, Tuning: Quad9MidSni), CancellationToken.None);

        await commands.HandleAsync(
            new DaemonRequest(DaemonProtocol.SetDomains, Domains: new DomainSelection { EnabledGroups = [DomainGroup.Reddit] }),
            CancellationToken.None);

        var saved = store.Load();
        Assert.Equal(SplitMode.MidSni, saved.SplitMode);
        Assert.Equal([DomainGroup.Reddit], saved.Domains.EnabledGroups);
    }

    private const string TuningWire =
        """{"command":"set-tuning","allowProxyOverride":false,"tuning":{"doh":"quad9","splitMode":"midSni","splitRecords":true,"splitDelayMs":10}}""";

    [Fact]
    public void Protocol_tuning_wire_format_is_stable()
    {
        var request = new DaemonRequest(DaemonProtocol.SetTuning, Tuning: Quad9MidSni);

        Assert.Equal(TuningWire, JsonSerializer.Serialize(request, DaemonProtocol.Json));
        Assert.Equal(request, JsonSerializer.Deserialize<DaemonRequest>(TuningWire, DaemonProtocol.Json));
    }

    internal sealed class IdleServices : IFeatureServices
    {
        /// <summary>Máy DoH mà stub DNS giả báo (F4).</summary>
        public DohActivity? Doh { get; init; }

        public IRunningService StartDnsStub(IReadOnlyList<string> domains) => new Idle { Doh = Doh };

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

        public DohActivity? Doh { get; init; }

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
    public void Console_owner_name_comes_from_stat()
    {
        Assert.Equal("lunav", new ConsoleUser(new StatRunner("lunav\n")).CurrentName());
        Assert.Null(new ConsoleUser(new StatRunner("  \n")).CurrentName());
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
