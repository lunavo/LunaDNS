# LunaDNS — Kế hoạch 5: `FeatureSet` + App Avalonia (giao diện, khay, `--cleanup`, `--report`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nối các khối của KH2–KH4 thành app dùng được. `FeatureSet` bật/tắt ba tính năng (Định tuyến DNS, Proxy SNI, Chế độ PUBG) qua Journal, có watchdog. App `LunaDNS.exe` gồm giao diện Avalonia tiếng Việt, biểu tượng khay, và hai lệnh `--cleanup`, `--report`.

**Architecture:** Mọi logic có thể test nằm trong `LunaDns.Core`:
- `Features/` có `FeatureSet` (dịch vụ nền → `SystemChanges` → Journal; hoàn tác theo tính năng; watchdog 3 lần/phút) và `ProbeSummary` (kết quả cho màn hình chính).
- `Proxy/SniForwarder` là listener 443 của PUBG. Nó dùng chung `TlsTunnel` với `SplitProxy`.
- `Settings/`, `Diagnostics/` (báo cáo + che thông tin cá nhân), `Logging/` (log file xoay vòng).

`Platform.Windows` thêm `WindowsDiagnostics` (chỉ đọc). Project mới `LunaDns.App` (`net8.0-windows`, Avalonia 11.3.23) chỉ ráp các phần lại: composition root, mutex `Global\LunaDNS`, CLI, ViewModel và XAML.

**Tech Stack:** .NET 8 SDK 8.0.428, C# 12, Avalonia 11.3.23 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`), `Microsoft.Extensions.Logging` 8.0.1, System.Text.Json source generator, xUnit 2.5.3, `Microsoft.Extensions.TimeProvider.Testing` (đã có trong Core.Tests).

**Spec:** `docs/superpowers/specs/2026-11-13-lunadns-design.md` (mục 4.4, 4.5, 4.8, 4.9, 5, 6). **Kế hoạch trước:** `docs/superpowers/plans/2026-11-13-lunadns-04-platform-windows.md` (đọc mục "Quyết định" ở cuối). **Spike:** `docs/spikes/2026-11-spike-results.md` (ghim Avalonia 11.3.*, CM của Steam không đọc PAC). **Lộ trình:** bảng ở đầu `docs/superpowers/plans/2026-11-13-lunadns-01-core-foundation.md`.

## Sự thật đã có (13/11/2026)

- Trạng thái nền: Core **268 passed + 4 skipped**, Platform **29 passed + 4 skipped** (33/33 khi chạy Admin + mạng, KH4 T7). Chưa commit KH2–KH4.
- Spike: kết nối CM của Steam (đăng nhập) không đọc PAC, chỉ chịu tác động của NRPT. Proxy SNI chỉ có tác dụng với Store/Community (CEF).
- KH4 T7: `Restart-Service Tailscale` không xoá luật NRPT của LunaDNS, nên KH5 không cần watchdog cho NRPT. GUID luật Tailscale đổi sau mỗi lần restart, vì vậy chỉ nhận diện luật theo `Comment`.
- Mạng PO: DNS hệ thống trả `127.0.0.1` cho `store.steampowered.com`; `store`/`community` bị `SniFiltered`, cắt `FirstByte` qua được (KH3 T10).
- Avalonia 12 không build được với SDK 8.0.428 (CS9057). Bản Avalonia 11.3.23 trimmed nặng 34,4 MB.
- `JsonFileJournalStore.Load()` trả `[]` khi chưa có file và đổi tên file hỏng thành `.corrupt-<ts>`. `ChangeJournal.Rollback(feature)` so `Feature` theo ordinal.

## Global Constraints

- Target: `net8.0` cho Core, `net8.0-windows` cho `Platform.Windows`, `LunaDns.App` và test của hai project đó. `Directory.Build.props` giữ nguyên (Nullable, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `LangVersion 12`).
- Quy tắc phụ thuộc (spec 3): `App → Core + Platform.Windows`, `Platform.Windows → Core`. `Core` không tham chiếu `Microsoft.Win32`, P/Invoke hay package riêng của Windows.
- **Package mới, chỉ ở `src/LunaDns.App`:** `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` cùng **11.3.23**, và `Microsoft.Extensions.Logging` **8.0.1**. Không thêm package nào khác (không Serilog, không CommunityToolkit, không Avalonia.Headless). Test project mới dùng đúng phiên bản package của `LunaDns.Platform.Windows.Tests`.
- **Ghi Journal trước, đổi hệ thống sau** (spec 4.5): mọi thay đổi hệ thống đi qua `SystemChanges`. Khi tắt tính năng: **hoàn tác Journal trước, dừng dịch vụ sau**, để hệ thống không bao giờ trỏ vào stub hay proxy đã chết.
- Không bao giờ sửa hay xoá luật NRPT không có `Comment` bắt đầu bằng `"LunaDNS "`. Không có thao tác nào nằm ngoài Journal.
- Không có `catch { }` rỗng (spec 6). `catch (Exception)` chỉ đặt ở ranh giới (một lệnh bật/tắt, một kết nối, một nút bấm), và luôn có log hoặc thông báo. Log dùng `[LoggerMessage]`. Log không chứa giá trị `AutoConfigURL` cũ hay tên host nằm ngoài `GameDomains`.
- Chuỗi hiện cho người dùng nằm trong `src/LunaDns.App/Strings.resx`, tiếng Việt có dấu. Tên định danh trong code viết tiếng Anh. Message của exception trong Core vẫn viết tiếng Việt như KH2–KH4, nhưng giao diện không hiện chúng; giao diện đổi mã lỗi thành chuỗi qua `ErrorMessages`.
- Code trong App chạy trên luồng UI thì **không** dùng `ConfigureAwait(false)`. Core giữ `ConfigureAwait(false)` như hiện tại.
- Hằng mảng truyền làm đối số thì đưa lên thành `static readonly` (CA1861). Đối số `expected` của `Assert.Equal` không viết bằng collection expression khi không suy ra được kiểu.
- Test không chạm cấu hình thật của Windows. Test của App dùng thư mục tạm, khoá registry `HKU\<SID>\Software\LunaDNS.Tests\<guid>` và tên mutex `Global\LunaDNS.Test.<guid>`.
- Lệnh kiểm tra (PowerShell):
  `$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"; dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests; dotnet test tests/LunaDns.Platform.Windows.Tests; dotnet test tests/LunaDns.App.Tests`
  Đích sau KH5: Core **356 passed + 4 skipped**, Platform **37 passed + 4 skipped**, App **29 passed**.
- **Không commit** (giữ như KH2–KH4): không `git add`, `git commit`, `git switch`, không tạo nhánh. Phiên chính review bằng `git diff`/`git status`. Chỉ sửa đúng các file mà task nêu.
- Không chép mã từ SteamAccessFix (D4).

## Quyết định thiết kế mới trong KH5 (khác hoặc thêm so với spec). **PO đã duyệt cả 16 ngày 13/11/2026.** Quyết định 3 và 14 xem lại sau kết quả T8.

1. **Định tuyến DNS và Proxy SNI chỉ dùng nhóm Steam** (`FeatureDomains.Routed = GameDomains.For(Steam)`). Nhóm `Other` (Naraka, Delta Force) không có trên giao diện và Prober không kiểm tra, nên bật cho nhóm này là trái tiêu chí 1 "chỉ can thiệp khi cần". Nhóm PUBG đi riêng qua Chế độ PUBG.
2. **Watchdog mở lại proxy trên đúng cổng cũ.** Nhờ vậy `AutoConfigURL` không phải ghi lại, và Journal không phát sinh mục mới. Không mở lại được cổng cũ thì tính là một lần khởi động lại thất bại. Thất bại 3 lần trong 1 phút thì tắt tính năng qua Journal và báo người dùng (spec 6). Không làm watchdog cho NRPT (đã đo ở KH4 T7).
3. **Chế độ PUBG chỉ thêm một dòng hosts:** `127.0.0.1 prod-live-front.playbattlegrounds.com # LunaDNS`, là host đã có trong `ProbeTargets.Pubg`. Muốn thêm host lobby/entry khác thì PO phải đo bằng PUBG thật rồi bổ sung vào `FeatureDomains.PubgHosts`. Ngoại lệ ở spec 4.4 ("chuyển hướng anti-cheat nếu probe thấy bị chặn") **chưa làm**, vì Prober không kiểm tra domain anti-cheat. v1 không bao giờ chuyển hướng `*xenuine*` hay `accounts.pubg.com`.
4. **Listener PUBG (`SniForwarder`) phân giải máy chủ thật qua DoH**, không qua DNS hệ thống. Lý do: file hosts đã trỏ host PUBG về chính `127.0.0.1`. Listener dùng lại `ProxyStartError`/`SplitProxyException` để báo lỗi khi mở cổng 443.
5. **"Áp dụng đề xuất" chỉ bật biện pháp cho Steam** (theo `ProbeAdvisor`). Với PUBG, app chỉ hiện gợi ý, vì Chế độ PUBG cần người dùng đọc cảnh báo. Cảnh báo hiện ở lần bật đầu tiên. Sau khi người dùng đồng ý, app lưu `PubgWarningAccepted` vào `settings.json` và không hỏi lại.
6. **Tắt một tính năng mà hoàn tác lỗi thì dịch vụ vẫn chạy.** Tính năng vẫn ở trạng thái bật để mạng không hỏng, mục Journal được giữ, và người dùng bấm tắt lại được. "Thoát & khôi phục" và đăng xuất thì luôn dừng mọi dịch vụ. Mục còn lỗi khi đó sẽ được xử lý ở lần mở app sau hoặc khi chạy `--cleanup`.
7. **Dùng `Strings.resx` (bản trung tính, tiếng Việt) và `[assembly: NeutralResourcesLanguage("vi")]` thay cho `Strings.vi.resx`.** File có hậu tố `.vi` sẽ bị biên dịch thành satellite assembly. Trên Windows tiếng Anh, `ResourceManager` khi đó không tìm thấy chuỗi nào. Sau này muốn dịch thì thêm `Strings.en.resx`.
8. **Log tự viết (`RollingFileLoggerProvider`, khoảng 120 dòng), không thêm Serilog.** File `lunadns.log` tối đa 1 MB, giữ 5 file (spec 6). App chỉ thêm `Microsoft.Extensions.Logging` 8.0.1 để có `LoggerFactory`.
9. **Báo cáo chẩn đoán che nhiều hơn spec 4.9:**
   - `AutoConfigURL` chỉ được ghi ra khi là URL loopback (của LunaDNS). Giá trị khác, ví dụ PAC của công ty, chỉ ghi là "có giá trị khác".
   - Che SID (snapshot trong Journal có SID của người dùng).
   - Che IP CGNAT `100.64/10` (Tailscale), `169.254/16`, và IPv6 ULA/link-local.
10. **`--cleanup` không dọn luật NRPT "mồ côi"** (KH4 quyết định 11), vì đó là thao tác nằm ngoài Journal. Báo cáo chẩn đoán vẫn liệt kê các luật này. Khi app đang chạy (mutex bị giữ), `--cleanup` thoát với mã 0 và không làm gì.
11. **Mục Cài đặt ở KH5 chỉ có "Chẩn đoán"; mục Nâng cao đủ ba lựa chọn của spec 4.8, áp dụng ở lần bật kế tiếp.** "Kiểm tra cập nhật" thuộc KH6, "Gỡ khỏi máy" thuộc KH4b.
12. **Chưa có ACL cho `%ProgramData%\LunaDNS`** (việc của KH4b). KH5 tạo thư mục này với ACL mặc định của Windows. ⇒ **Không phát bản nào, kể cả beta cho người quen, trước khi KH4b xong.** KH6 phải chặn bước phát hành nếu KH4b chưa xong.
13. **ViewModel không có unit test.** Logic đã nằm ở `FeatureSet`, `ProbeSummary` và `ErrorMessages`, đều có test. Giao diện được kiểm tay ở T8, không thêm Avalonia.Headless.
14. **Khi đăng xuất hoặc tắt máy, app hoàn tác trong `ShutdownRequested` của Avalonia, chờ tối đa 4 giây.** Chưa chắc Avalonia 11.3 phát sự kiện này khi nhận `WM_QUERYENDSESSION`; T8 bước 6 sẽ đo. Nếu sự kiện không được phát thì lần mở app sau hoặc `--cleanup` vẫn hoàn tác, nhưng từ lúc đăng nhập lại cho tới khi đó Steam sẽ không phân giải được tên miền.
15. **Nút "Chẩn đoán" dùng kết quả "Kiểm tra" gần nhất.** `--report` tự chạy kiểm tra, giới hạn 20 giây. Đọc Journal để ghi báo cáo có thể đổi tên file journal hỏng thành `.corrupt-<ts>`, giống `JsonFileJournalStore`.
16. **`settings.json` dùng JSON source generator** để không vướng cảnh báo khi trim. Journal vẫn serialize bằng reflection (KH1); KH6 kiểm tra cảnh báo trim của phần này lúc publish.

## Gợi ý chia việc cho sub-agent

Dự kiến **4 agent, đều `model: "sonnet"`, chạy tuần tự**. Task 8 do PO chạy trong PowerShell Admin.

| Agent | Task | Lý do gộp |
|---|---|---|
| A | 1, 2 | `TlsTunnel` + `SniForwarder`, hợp đồng `Features`, `RestartPolicy`, settings, `CoreFeatureServices` |
| B | 3, 4 | `FeatureSet` + watchdog (rủi ro cao nhất), chẩn đoán, log, `ProbeSummary` |
| C | 5, 6 | `WindowsDiagnostics`, phần không giao diện của App cùng `LunaDns.App.Tests` |
| D | 7 | Avalonia: Program, App, MainWindow, ViewModel, khay |

Sau mỗi agent, phiên chính tự review bằng `git diff` và chạy lại lệnh kiểm tra. Sonnet trượt verify 2 lần thì giao lại cho Opus đúng một lần.

## Cấu trúc file của kế hoạch 5

```
LunaDns.sln                                        # (sửa, T6) thêm App + App.Tests
src/LunaDns.Core/
├── Proxy/
│   ├── TlsTunnel.cs                               # T1  phần chung: đọc record đầu, ghi cắt gói, relay
│   ├── SplitProxy.cs                              # T1  (sửa) dùng TlsTunnel
│   ├── SniForwarderOptions.cs                     # T1
│   └── SniForwarder.cs                            # T1  listener 127.0.0.1:443 của PUBG
├── Features/
│   ├── Feature.cs                                 # T2  enum + FeatureKeys (tên feature trong journal)
│   ├── FeatureDomains.cs                          # T2
│   ├── FeatureTuning.cs                           # T2
│   ├── IRunningService.cs, IRunningProxy.cs       # T2
│   ├── IFeatureServices.cs                        # T2
│   ├── CoreFeatureServices.cs                     # T2  dựng stub/proxy/forwarder thật
│   ├── RestartPolicy.cs                           # T2  3 lần / 1 phút
│   ├── FeatureError.cs, FeatureOutcome.cs         # T3
│   ├── FeatureResult.cs, FeatureStatus.cs         # T3
│   ├── DisableAllResult.cs                        # T3
│   ├── FeatureStoppedEventArgs.cs                 # T3
│   ├── FeatureSet.cs                              # T3  bật/tắt + watchdog
│   └── ProbeSummary.cs                            # T4  (+ ProbeGroupSummary)
├── Settings/
│   ├── AppSettings.cs                             # T2
│   ├── SettingsJsonContext.cs                     # T2
│   └── JsonFileSettingsStore.cs                   # T2
├── Diagnostics/
│   ├── Redactor.cs                                # T4
│   ├── DiagnosticInput.cs                         # T4
│   └── DiagnosticReport.cs                        # T4
└── Logging/RollingFileLoggerProvider.cs           # T4
src/LunaDns.Platform.Windows/
├── Proxy/AutoProxyRegistry.cs                     # T5  (sửa) thêm ReadCurrent
└── Diagnostics/WindowsDiagnostics.cs              # T5
src/LunaDns.App/
├── LunaDns.App.csproj                             # T6 (Library) → T7 (WinExe + Avalonia)
├── Strings.resx, Strings.cs                       # T6
├── ErrorMessages.cs, CommandLine.cs, AppPaths.cs  # T6
├── SingleInstance.cs                              # T6  mutex Global\LunaDNS + sự kiện "đưa cửa sổ lên"
├── ProbeRunner.cs, AppServices.cs                 # T6  composition root
├── CleanupCommand.cs, ReportWriter.cs             # T6
├── app.manifest, Assets/lunadns.ico               # T7
├── Program.cs, App.axaml(.cs)                     # T7
├── Views/MainWindow.axaml(.cs), Views/ConfirmDialog.cs          # T7
├── Assets/make-icon.ps1                          # T7  sinh lunadns.ico (PowerShell 5.1)
└── ViewModels/ObservableObject.cs, AsyncCommand.cs, IUserPrompts.cs, FeatureViewModel.cs, MainViewModel.cs   # T7
tests/LunaDns.Core.Tests/
├── Eventually.cs                                  # T1  chờ điều kiện tối đa 5 s
├── Proxy/SniForwarderTests.cs                     # T1
├── Features/…Tests.cs, Fake*.cs                   # T2, T3, T4
├── Settings/…Tests.cs                             # T2
├── Diagnostics/…Tests.cs                          # T4
├── Logging/RollingFileLoggerProviderTests.cs      # T4
└── Platform/FakeConfigurator.cs                   # T3  (sửa) thêm ExistingProxy, FailRevert, PlanDnsError
tests/LunaDns.Platform.Windows.Tests/
├── Nrpt/FakeNrptStore.cs                          # T5  (sửa) thêm FailList
└── Diagnostics/WindowsDiagnosticsTests.cs         # T5
tests/LunaDns.App.Tests/                           # T6  TestApp, FakeNetworkConfigurator, EmptyNrptStore + 7 file test
```

---

### Task 1: `TlsTunnel` dùng chung và `SniForwarder` (listener 443 của PUBG)

**Files:**
- Create: `src/LunaDns.Core/Proxy/TlsTunnel.cs`, `src/LunaDns.Core/Proxy/SniForwarderOptions.cs`, `src/LunaDns.Core/Proxy/SniForwarder.cs`
- Modify: `src/LunaDns.Core/Proxy/SplitProxy.cs` (bỏ `MaxRecordLength`, `BufferSize`, `FillAsync`; `ForwardFirstRecordAsync` và `RelayAsync` gọi `TlsTunnel`)
- Test: `tests/LunaDns.Core.Tests/Eventually.cs`, `tests/LunaDns.Core.Tests/Proxy/SniForwarderTests.cs`

**Interfaces:**
- Consumes: `ClientHelloParser.RecordLength/TryFindSni`, `SplitWriter.WriteAsync`, `SplitWriter.MaxDelay`, `DomainMatcher.Matches`, `IUpstreamConnector`, `SplitProxyException(ProxyStartError, string, Exception)`, `SplitProxy.TlsPort`. Test dùng `FakeConnector`, `EchoOrigin`, `RecordingStream`, `TestClientHello.Build`.
- Produces:
  - `internal static class TlsTunnel`: `MaxRecordLength`, `BufferSize`, `Task<(int Count, int RecordLength)> ReadFirstRecordAsync(Stream, byte[], int count, CancellationToken)`, `Task WriteFirstRecordAsync(Stream upstream, byte[], int count, int recordLength, SplitMode, TimeSpan delay, CancellationToken)`, `Task<Exception?> RelayAsync(Stream client, Stream upstream, CancellationToken)`
  - `public sealed record SniForwarderOptions { required IReadOnlyList<string> Domains; SplitMode SplitMode = FirstByte; TimeSpan SplitDelay = 0 }`
  - `public sealed partial class SniForwarder : IAsyncDisposable`: `static readonly IPEndPoint DefaultEndPoint` (127.0.0.1:443), `static SniForwarder Start(IPEndPoint, SniForwarderOptions, IUpstreamConnector, ILogger<SniForwarder>? = null)`, `IPEndPoint LocalEndPoint`, `int ActiveConnections`, `Task Completion`. Mở cổng lỗi thì ném `SplitProxyException` với `PortInUse`/`AccessDenied`/`Other`.
  - `internal static class Eventually { static Task TrueAsync(Func<bool> condition) }` (test)

- [ ] **Step 1: Viết helper `Eventually` và test `SniForwarder`**

`tests/LunaDns.Core.Tests/Eventually.cs`:
```csharp
namespace LunaDns.Core.Tests;

/// <summary>Chờ điều kiện do luồng nền đặt, tối đa 5 giây.</summary>
internal static class Eventually
{
    public static async Task TrueAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Điều kiện không thành sau 5 giây.");
            await Task.Delay(10);
        }
    }
}
```

`tests/LunaDns.Core.Tests/Proxy/SniForwarderTests.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Tls;

namespace LunaDns.Core.Tests.Proxy;

public sealed class SniForwarderTests : IAsyncLifetime
{
    private const string Host = "prod-live-front.playbattlegrounds.com";
    private static readonly byte[] Hello = TestClientHello.Build(Host);
    private static readonly IPEndPoint AnyLoopbackPort = new(IPAddress.Loopback, 0);

    private readonly FakeConnector _connector = new();
    private SniForwarder _forwarder = null!;

    public Task InitializeAsync()
    {
        _forwarder = Start(AnyLoopbackPort);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _forwarder.DisposeAsync();

    private SniForwarder Start(IPEndPoint endPoint) =>
        SniForwarder.Start(endPoint, new SniForwarderOptions { Domains = GameDomains.For(DomainGroup.Pubg) }, _connector);

    private async Task<NetworkStream> ConnectAsync()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(_forwarder.LocalEndPoint);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static async Task<bool> ClosedByServerAsync(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            return await stream.ReadAsync(new byte[1], timeout.Token) == 0;
        }
        catch (IOException)
        {
            // RST cũng là đóng.
            return true;
        }
    }

    [Fact]
    public async Task Forwards_split_hello_then_payload_for_listed_sni()
    {
        var upstream = new RecordingStream();
        string? connectedHost = null;
        var connectedPort = 0;
        _connector.Connect = (host, port) =>
        {
            connectedHost = host;
            connectedPort = port;
            return upstream;
        };

        await using var client = await ConnectAsync();
        await client.WriteAsync(Hello);
        await client.WriteAsync("ping"u8.ToArray());
        await Eventually.TrueAsync(() => upstream.TotalBytes == Hello.Length + 4);

        Assert.Equal(Host, connectedHost);
        Assert.Equal(443, connectedPort);
        Assert.Equal(1, upstream.Writes[0].Length);
        Assert.Equal(Hello.Concat("ping"u8.ToArray()).ToArray(), upstream.Writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task Relays_both_directions()
    {
        await using var origin = new EchoOrigin();
        _connector.Connect = (_, _) => origin.Connect();

        await using var client = await ConnectAsync();
        await client.WriteAsync(Hello);
        var echoed = new byte[Hello.Length];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ReadExactlyAsync(echoed, timeout.Token);

        Assert.Equal(Hello, echoed);
    }

    [Fact]
    public async Task Closes_when_sni_is_not_listed()
    {
        await using var client = await ConnectAsync();
        await client.WriteAsync(TestClientHello.Build("store.steampowered.com"));

        Assert.True(await ClosedByServerAsync(client));
        Assert.Equal(0, _connector.Calls);
    }

    [Fact]
    public async Task Closes_when_hello_has_no_sni()
    {
        await using var client = await ConnectAsync();
        await client.WriteAsync(TestClientHello.Build(null));

        Assert.True(await ClosedByServerAsync(client));
        Assert.Equal(0, _connector.Calls);
    }

    [Fact]
    public async Task Closes_on_non_tls_bytes()
    {
        await using var client = await ConnectAsync();
        await client.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n"));

        Assert.True(await ClosedByServerAsync(client));
        Assert.Equal(0, _connector.Calls);
    }

    [Fact]
    public void Second_bind_on_same_port_reports_port_in_use()
    {
        var ex = Assert.Throws<SplitProxyException>(() => Start(_forwarder.LocalEndPoint));

        Assert.True(ex.Error is ProxyStartError.PortInUse or ProxyStartError.AccessDenied, ex.Error.ToString());
    }

    [Fact]
    public async Task Upstream_failure_closes_connection_and_keeps_listening()
    {
        _connector.Connect = (_, _) => throw new IOException("máy chủ không trả lời");
        await using (var first = await ConnectAsync())
        {
            await first.WriteAsync(Hello);
            Assert.True(await ClosedByServerAsync(first));
        }

        var upstream = new RecordingStream();
        _connector.Connect = (_, _) => upstream;
        await using var second = await ConnectAsync();
        await second.WriteAsync(Hello);
        await Eventually.TrueAsync(() => upstream.TotalBytes == Hello.Length);
    }

    [Fact]
    public async Task Dispose_completes_and_is_idempotent()
    {
        await _forwarder.DisposeAsync();

        Assert.True(_forwarder.Completion.IsCompleted);
        await _forwarder.DisposeAsync();
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận fail biên dịch**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SniForwarderTests"`
Expected: FAIL, lỗi CS0246 `SniForwarder`/`SniForwarderOptions` không tồn tại.

- [ ] **Step 3: Viết `TlsTunnel`**

`src/LunaDns.Core/Proxy/TlsTunnel.cs`:
```csharp
using System.Net.Sockets;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Proxy;

/// <summary>Phần chung của SplitProxy và SniForwarder: đọc TLS record đầu, ghi cắt gói, chuyển byte hai chiều.</summary>
internal static class TlsTunnel
{
    internal const int MaxRecordLength = 5 + 16384;
    internal const int BufferSize = 32 * 1024;

    /// <summary>
    /// Đọc tới khi buffer chứa trọn TLS record đầu tiên (hoặc client đóng sớm). RecordLength = -1 khi dữ liệu
    /// không phải record handshake hoặc khai dài quá cỡ; khi đó Count là số byte đang có.
    /// </summary>
    internal static async Task<(int Count, int RecordLength)> ReadFirstRecordAsync(Stream client, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        count = await FillAsync(client, buffer, count, 5, cancellationToken).ConfigureAwait(false);
        var recordLength = ClientHelloParser.RecordLength(buffer.AsSpan(0, count));
        if (recordLength < 0 || recordLength > MaxRecordLength) return (count, -1);
        count = await FillAsync(client, buffer, count, recordLength, cancellationToken).ConfigureAwait(false);
        return (count, recordLength);
    }

    /// <summary>Ghi record đầu sang máy chủ: cắt gói nếu là record handshake, chuyển nguyên nếu không; byte thừa ghi sau.</summary>
    internal static async Task WriteFirstRecordAsync(Stream upstream, byte[] buffer, int count, int recordLength, SplitMode mode, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (recordLength < 0)
        {
            await upstream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            return;
        }
        var helloLength = Math.Min(count, recordLength);
        await SplitWriter.WriteAsync(upstream, buffer.AsMemory(0, helloLength), mode, delay, cancellationToken).ConfigureAwait(false);
        if (count > helloLength)
            await upstream.WriteAsync(buffer.AsMemory(helloLength, count - helloLength), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Chuyển byte hai chiều tới khi một bên đóng, rồi dừng chiều còn lại. Trả lỗi đã kết thúc tunnel (để log), hoặc null.</summary>
    internal static async Task<Exception?> RelayAsync(Stream client, Stream upstream, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var up = client.CopyToAsync(upstream, linked.Token);
        var down = upstream.CopyToAsync(client, linked.Token);
        await Task.WhenAny(up, down).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(up, down).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            return ex;
        }
    }

    /// <summary>Đọc tới khi buffer có ít nhất <paramref name="needed"/> byte. Client đóng sớm thì trả số byte đang có.</summary>
    private static async Task<int> FillAsync(Stream source, byte[] buffer, int count, int needed, CancellationToken cancellationToken)
    {
        while (count < needed)
        {
            var read = await source.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        return count;
    }
}
```

- [ ] **Step 4: Sửa `SplitProxy` dùng `TlsTunnel`**

Trong `src/LunaDns.Core/Proxy/SplitProxy.cs`:
1. Xoá hai dòng `private const int MaxRecordLength = 5 + 16384;` và `private const int BufferSize = 32 * 1024;`.
2. Trong `ServeAsync`, đổi `var buffer = new byte[BufferSize];` thành `var buffer = new byte[TlsTunnel.BufferSize];`.
3. Thay toàn bộ ba method `ForwardFirstRecordAsync`, `FillAsync`, `RelayAsync` (từ dòng `/// <summary>` ngay trên `ForwardFirstRecordAsync` tới hết `RelayAsync`) bằng:
```csharp
    /// <summary>
    /// Đọc trọn TLS record đầu tiên của client rồi ghi cắt gói sang máy chủ. Dữ liệu không phải record handshake,
    /// hoặc record khai dài quá cỡ, thì chuyển nguyên.
    /// </summary>
    private async Task ForwardFirstRecordAsync(Stream client, Stream upstream, byte[] buffer, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(FirstBytesTimeout);
        var (filled, recordLength) = await TlsTunnel.ReadFirstRecordAsync(client, buffer, count, timeout.Token).ConfigureAwait(false);
        await TlsTunnel.WriteFirstRecordAsync(upstream, buffer, filled, recordLength, _options.SplitMode, _options.SplitDelay, _stop.Token).ConfigureAwait(false);
    }

    /// <summary>Chuyển byte hai chiều tới khi một bên đóng, rồi dừng chiều còn lại.</summary>
    private async Task RelayAsync(Stream client, Stream upstream)
    {
        var ended = await TlsTunnel.RelayAsync(client, upstream, _stop.Token).ConfigureAwait(false);
        if (ended is not null) LogRelayEnded(_logger, ended);
    }
```
4. Nếu `using LunaDns.Core.Tls;` không còn dùng tới (`SplitWriter` đã chuyển sang `TlsTunnel`) thì xoá, tránh IDE0005. Giữ `using System.Net.Sockets;` vì `Socket` vẫn được dùng.

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplitProxy"`
Expected: toàn bộ test SplitProxy cũ vẫn PASS (refactor không đổi hành vi).

- [ ] **Step 5: Viết `SniForwarderOptions` và `SniForwarder`**

`src/LunaDns.Core/Proxy/SniForwarderOptions.cs`:
```csharp
using LunaDns.Core.Tls;

namespace LunaDns.Core.Proxy;

/// <summary>Cấu hình listener 127.0.0.1:443 của Chế độ PUBG (spec 4.4).</summary>
public sealed record SniForwarderOptions
{
    /// <summary>Chỉ chuyển tiếp ClientHello có SNI thuộc các domain này; SNI khác hoặc không có SNI thì đóng.</summary>
    public required IReadOnlyList<string> Domains { get; init; }

    public SplitMode SplitMode { get; init; } = SplitMode.FirstByte;

    public TimeSpan SplitDelay { get; init; } = TimeSpan.Zero;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Domains);
        if (SplitDelay < TimeSpan.Zero || SplitDelay > SplitWriter.MaxDelay)
            throw new ArgumentException("Độ trễ cắt gói phải nằm trong khoảng 0–20 ms.");
    }
}
```

`src/LunaDns.Core/Proxy/SniForwarder.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Domains;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Proxy;

/// <summary>
/// Listener TLS trong suốt của Chế độ PUBG (spec 4.4). File hosts trỏ host PUBG về 127.0.0.1; listener đọc SNI của
/// ClientHello, nối tới máy chủ thật qua <see cref="IUpstreamConnector"/> (resolver DoH, không qua DNS hệ thống vì hosts
/// trỏ về chính nó), ghi ClientHello cắt gói rồi chuyển byte hai chiều. Không có SNI hoặc SNI ngoài danh sách thì đóng.
/// Không giải mã TLS.
/// </summary>
public sealed partial class SniForwarder : IAsyncDisposable
{
    public static readonly IPEndPoint DefaultEndPoint = new(IPAddress.Loopback, SplitProxy.TlsPort);

    private const int MaxConnections = 256;
    private static readonly TimeSpan FirstBytesTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly SniForwarderOptions _options;
    private readonly IUpstreamConnector _connector;
    private readonly ILogger _logger;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(MaxConnections);
    private Task _loop = Task.CompletedTask;
    private int _active;

    private SniForwarder(Socket listener, SniForwarderOptions options, IUpstreamConnector connector, ILogger logger)
    {
        _listener = listener;
        _options = options;
        _connector = connector;
        _logger = logger;
        LocalEndPoint = (IPEndPoint)listener.LocalEndPoint!;
    }

    public IPEndPoint LocalEndPoint { get; }

    public int ActiveConnections => Volatile.Read(ref _active);

    /// <summary>Xong khi vòng nghe dừng; faulted nếu socket lỗi bất ngờ. Watchdog của FeatureSet chờ task này.</summary>
    public Task Completion => _loop;

    public static SniForwarder Start(IPEndPoint endPoint, SniForwarderOptions options, IUpstreamConnector connector, ILogger<SniForwarder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connector);
        options.Validate();

        var listener = Bind(endPoint);
        SniForwarder forwarder;
        try
        {
            listener.Listen(MaxConnections);
            forwarder = new SniForwarder(listener, options, connector, (ILogger?)logger ?? NullLogger.Instance);
        }
        catch
        {
            listener.Dispose();
            throw;
        }
        forwarder._loop = Task.Run(forwarder.AcceptLoopAsync);
        return forwarder;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Dispose();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Vòng nghe đã chết từ trước (Completion faulted); Dispose không ném lại.
            LogLoopFaulted(_logger, ex);
        }
        // Cố ý không Dispose _stop và _slots: kết nối đang xử lý dở vẫn đọc token và trả slot.
    }

    private static Socket Bind(IPEndPoint endPoint)
    {
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        try
        {
            socket.Bind(endPoint);
            return socket;
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            var error = ex.SocketErrorCode switch
            {
                SocketError.AddressAlreadyInUse => ProxyStartError.PortInUse,
                SocketError.AccessDenied => ProxyStartError.AccessDenied,
                _ => ProxyStartError.Other,
            };
            throw new SplitProxyException(error, $"Không mở được cổng {endPoint} cho Chế độ PUBG.", ex);
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            if (!_slots.Wait(0))
            {
                LogBusy(_logger);
                client.Dispose();
                continue;
            }
            client.NoDelay = true;
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(Socket client)
    {
        Interlocked.Increment(ref _active);
        var stream = new NetworkStream(client, ownsSocket: true);
        try
        {
            var buffer = new byte[TlsTunnel.BufferSize];
            int count;
            int recordLength;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                timeout.CancelAfter(FirstBytesTimeout);
                (count, recordLength) = await TlsTunnel.ReadFirstRecordAsync(stream, buffer, 0, timeout.Token).ConfigureAwait(false);
            }
            if (recordLength < 0 || count < recordLength
                || !ClientHelloParser.TryFindSni(buffer.AsSpan(0, recordLength), out var sni)
                || !DomainMatcher.Matches(sni.Host, _options.Domains))
            {
                // Không ghi SNI: có thể là host ngoài GameDomains (log nằm trong báo cáo chẩn đoán).
                LogRefused(_logger);
                return;
            }
            await ForwardAsync(stream, sni.Host, buffer, count, recordLength).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Ranh giới của một kết nối: lỗi ở đây chỉ đóng kết nối này.
            if (ex is not OperationCanceledException && !_stop.IsCancellationRequested) LogConnectionFailed(_logger, ex);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            Interlocked.Decrement(ref _active);
            _slots.Release();
        }
    }

    private async Task ForwardAsync(NetworkStream client, string host, byte[] buffer, int count, int recordLength)
    {
        Stream upstream;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            connectTimeout.CancelAfter(ConnectTimeout);
            upstream = await _connector.ConnectAsync(host, SplitProxy.TlsPort, connectTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            LogUpstreamFailed(_logger, host, ex);
            return;
        }

        try
        {
            await TlsTunnel.WriteFirstRecordAsync(upstream, buffer, count, recordLength, _options.SplitMode, _options.SplitDelay, _stop.Token).ConfigureAwait(false);
            var ended = await TlsTunnel.RelayAsync(client, upstream, _stop.Token).ConfigureAwait(false);
            if (ended is not null) LogRelayEnded(_logger, ended);
        }
        finally
        {
            await upstream.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quá 256 kết nối tới listener PUBG, đóng kết nối mới")]
    private static partial void LogBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Đóng kết nối tới listener PUBG: không phải ClientHello, không có SNI hoặc SNI ngoài danh sách")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listener PUBG không nối được tới {Host}")]
    private static partial void LogUpstreamFailed(ILogger logger, string host, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kết nối tới listener PUBG lỗi")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tunnel PUBG đã đóng")]
    private static partial void LogRelayEnded(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vòng nghe của listener PUBG đã dừng vì lỗi")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
```

- [ ] **Step 6: Chạy test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: build 0 cảnh báo; **276 passed + 4 skipped** (268 + 8).

- [ ] **Step 7: Tự kiểm, không commit**

Run: `git status --short src/LunaDns.Core/Proxy tests/LunaDns.Core.Tests`
Expected: `M SplitProxy.cs`; `??` cho `TlsTunnel.cs`, `SniForwarder*.cs`, `Eventually.cs`, `SniForwarderTests.cs`. Không `git add`.

---

### Task 2: Hợp đồng `Features`, `RestartPolicy`, settings, `CoreFeatureServices`

**Files:**
- Create: `src/LunaDns.Core/Features/Feature.cs`, `FeatureDomains.cs`, `FeatureTuning.cs`, `IRunningService.cs`, `IRunningProxy.cs`, `IFeatureServices.cs`, `CoreFeatureServices.cs`, `RestartPolicy.cs`
- Create: `src/LunaDns.Core/Settings/AppSettings.cs`, `SettingsJsonContext.cs`, `JsonFileSettingsStore.cs`
- Test: `tests/LunaDns.Core.Tests/Features/FeatureKeysTests.cs`, `FeatureDomainsTests.cs`, `RestartPolicyTests.cs`, `CoreFeatureServicesTests.cs`; `tests/LunaDns.Core.Tests/Settings/AppSettingsTests.cs`, `JsonFileSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `GameDomains.For`, `DomainGroup`, `ProbeTargets.Pubg`, `HostsEntry`, `DohClient.Create(IReadOnlyList<DohServer>, ILogger<DohClient>?)`, `DohServer.Cloudflare/Google/Defaults`, `DnsQueryHandler(IReadOnlyList<string>, IDnsUpstream, DnsCache, ILogger?)`, `DohStubServer.Start(IPEndPoint, DnsQueryHandler, ILogger?)` + `DefaultEndPoint`, `SplitProxy.Start(SplitProxyOptions, IUpstreamConnector, ILogger?)`, `SplitProxyOptions.DefaultFirstPort/DefaultLastPort`, `TcpUpstreamConnector(IHostResolver)`, `DohHostResolver(IDnsUpstream, DnsCache?)`, `SniForwarder` (T1), `SplitMode`.
- Produces:
  - `public enum Feature { DnsRouting, SniProxy, PubgMode }`; `public static class FeatureKeys { const string DnsRouting = "dns-routing", SniProxy = "sni-proxy", PubgMode = "pubg-mode"; static string For(Feature) }`
  - `public static class FeatureDomains { IReadOnlyList<string> Routed; IReadOnlyList<string> Pubg; IReadOnlyList<HostsEntry> PubgHosts }`
  - `public sealed record FeatureTuning(SplitMode SplitMode, TimeSpan SplitDelay) { static FeatureTuning Default }`
  - `public interface IRunningService : IAsyncDisposable { Task Completion { get; } }`
  - `public interface IRunningProxy : IRunningService { Uri PacUrl; int Port; int ActiveConnections }`
  - `public interface IFeatureServices { IRunningService StartDnsStub(IReadOnlyList<string> domains); IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning); IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning); }`
  - `public sealed class CoreFeatureServices(Func<IReadOnlyList<DohServer>> dohServers, ILoggerFactory? loggerFactory = null, IPEndPoint? dnsEndPoint = null, IPEndPoint? pubgEndPoint = null) : IFeatureServices`
  - `public sealed class RestartPolicy(TimeProvider? time = null, int maxRestarts = 3, TimeSpan? window = null)` với `bool TryRecordRestart()`
  - `public sealed record AppSettings { SplitMode SplitMode; int SplitDelayMs; bool PreferGoogleDoh; bool CheckPubg; bool PubgWarningAccepted; IReadOnlyList<DohServer> DohServers(); FeatureTuning Tuning(); const int MaxSplitDelayMs = 20 }`
  - `public sealed class JsonFileSettingsStore(string path) { AppSettings Load(); void Save(AppSettings) }`

- [ ] **Step 1: Viết test**

`tests/LunaDns.Core.Tests/Features/FeatureKeysTests.cs`:
```csharp
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureKeysTests
{
    // Giá trị nằm trong journal.json trên máy người dùng: đổi tên là mất khả năng hoàn tác mục cũ.
    [Theory]
    [InlineData(Feature.DnsRouting, "dns-routing")]
    [InlineData(Feature.SniProxy, "sni-proxy")]
    [InlineData(Feature.PubgMode, "pubg-mode")]
    public void Keys_are_stable(Feature feature, string expected) => Assert.Equal(expected, FeatureKeys.For(feature));
}
```

`tests/LunaDns.Core.Tests/Features/FeatureDomainsTests.cs`:
```csharp
using LunaDns.Core.Domains;
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureDomainsTests
{
    [Fact]
    public void Routed_is_the_steam_group() =>
        Assert.Equal(GameDomains.For(DomainGroup.Steam), FeatureDomains.Routed);

    [Fact]
    public void Pubg_hosts_are_loopback_pubg_hosts_without_anticheat()
    {
        Assert.NotEmpty(FeatureDomains.PubgHosts);
        foreach (var entry in FeatureDomains.PubgHosts)
        {
            Assert.Equal("127.0.0.1", entry.Address);
            Assert.True(DomainMatcher.Matches(entry.Host, FeatureDomains.Pubg), entry.Host);
            Assert.DoesNotContain("xenuine", entry.Host, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual("accounts.pubg.com", entry.Host);
        }
    }
}
```

`tests/LunaDns.Core.Tests/Features/RestartPolicyTests.cs`:
```csharp
using LunaDns.Core.Features;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Features;

public sealed class RestartPolicyTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Allows_three_restarts_then_refuses()
    {
        var policy = new RestartPolicy(_time);

        Assert.True(policy.TryRecordRestart());
        Assert.True(policy.TryRecordRestart());
        Assert.True(policy.TryRecordRestart());
        Assert.False(policy.TryRecordRestart());
    }

    [Fact]
    public void Allows_again_after_the_window()
    {
        var policy = new RestartPolicy(_time);
        for (var i = 0; i < 3; i++) policy.TryRecordRestart();

        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(policy.TryRecordRestart());
    }

    [Fact]
    public void Window_slides_with_the_oldest_restart()
    {
        var policy = new RestartPolicy(_time);
        Assert.True(policy.TryRecordRestart());
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(policy.TryRecordRestart());
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.True(policy.TryRecordRestart());
        Assert.False(policy.TryRecordRestart());

        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.True(policy.TryRecordRestart());
    }
}
```

`tests/LunaDns.Core.Tests/Features/CoreFeatureServicesTests.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

public sealed class CoreFeatureServicesTests
{
    private static readonly IPEndPoint AnyLoopbackPort = new(IPAddress.Loopback, 0);

    private static CoreFeatureServices Create() =>
        new(() => DohServer.Defaults, dnsEndPoint: AnyLoopbackPort, pubgEndPoint: AnyLoopbackPort);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Dns_stub_runs_until_disposed()
    {
        var stub = Create().StartDnsStub(FeatureDomains.Routed);
        Assert.False(stub.Completion.IsCompleted);

        await stub.DisposeAsync();

        await stub.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Sni_proxy_on_fixed_port_uses_exactly_that_port()
    {
        var port = FreePort();

        await using var proxy = Create().StartSniProxy(FeatureDomains.Routed, port, FeatureTuning.Default);

        Assert.Equal(port, proxy.Port);
        Assert.Equal(new Uri($"http://127.0.0.1:{port}/proxy.pac"), proxy.PacUrl);
        Assert.Equal(0, proxy.ActiveConnections);
    }

    [Fact]
    public async Task Sni_proxy_without_port_binds_in_default_range()
    {
        await using var proxy = Create().StartSniProxy(FeatureDomains.Routed, null, FeatureTuning.Default);

        Assert.InRange(proxy.Port, 18080, 18090);
    }

    [Fact]
    public async Task Pubg_forwarder_runs_until_disposed()
    {
        var forwarder = Create().StartPubgForwarder(FeatureDomains.Pubg, FeatureTuning.Default);
        Assert.False(forwarder.Completion.IsCompleted);

        await forwarder.DisposeAsync();

        await forwarder.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
```

`tests/LunaDns.Core.Tests/Settings/AppSettingsTests.cs`:
```csharp
using LunaDns.Core.Features;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Settings;

public sealed class AppSettingsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var settings = new AppSettings();

        Assert.Equal(SplitMode.FirstByte, settings.SplitMode);
        Assert.Equal(0, settings.SplitDelayMs);
        Assert.False(settings.PreferGoogleDoh);
        Assert.False(settings.CheckPubg);
        Assert.False(settings.PubgWarningAccepted);
        Assert.Equal(FeatureTuning.Default, settings.Tuning());
    }

    [Theory]
    [InlineData(false, "Cloudflare,Google")]
    [InlineData(true, "Google,Cloudflare")]
    public void Doh_order_follows_preference(bool preferGoogle, string expected)
    {
        var settings = new AppSettings { PreferGoogleDoh = preferGoogle };

        Assert.Equal(expected, string.Join(",", settings.DohServers().Select(s => s.Name)));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(7, 7)]
    [InlineData(50, 20)]
    public void Tuning_clamps_delay_to_0_20_ms(int input, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), new AppSettings { SplitDelayMs = input }.Tuning().SplitDelay);
}
```

`tests/LunaDns.Core.Tests/Settings/JsonFileSettingsStoreTests.cs`:
```csharp
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Settings;

public sealed class JsonFileSettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-settings-").FullName;

    private string PathOf(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults() =>
        Assert.Equal(new AppSettings(), new JsonFileSettingsStore(PathOf("settings.json")).Load());

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonFileSettingsStore(PathOf("sub/settings.json"));
        var settings = new AppSettings { SplitMode = SplitMode.MidSni, SplitDelayMs = 5, PreferGoogleDoh = true, CheckPubg = true, PubgWarningAccepted = true };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
        Assert.False(File.Exists(PathOf("sub/settings.json.tmp")));
    }

    [Fact]
    public void Corrupt_file_is_quarantined_and_defaults_used()
    {
        File.WriteAllText(PathOf("settings.json"), "{ không phải json");

        var loaded = new JsonFileSettingsStore(PathOf("settings.json")).Load();

        Assert.Equal(new AppSettings(), loaded);
        Assert.Single(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
        Assert.False(File.Exists(PathOf("settings.json")));
    }

    [Fact]
    public void Enums_are_written_as_names()
    {
        new JsonFileSettingsStore(PathOf("settings.json")).Save(new AppSettings { SplitMode = SplitMode.MidSni });

        Assert.Contains("\"MidSni\"", File.ReadAllText(PathOf("settings.json")), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận fail biên dịch**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~Features|FullyQualifiedName~Settings"`
Expected: FAIL, lỗi CS0246 (namespace `LunaDns.Core.Features`/`Settings` chưa có).

- [ ] **Step 3: Viết các kiểu của `Features`**

`src/LunaDns.Core/Features/Feature.cs`:
```csharp
namespace LunaDns.Core.Features;

/// <summary>Ba công tắc trên giao diện (spec 4.8).</summary>
public enum Feature
{
    DnsRouting,
    SniProxy,
    PubgMode,
}

/// <summary>Tên tính năng ghi trong journal.json. Không đổi giá trị: mục cũ trên máy người dùng phải hoàn tác được.</summary>
public static class FeatureKeys
{
    public const string DnsRouting = "dns-routing";
    public const string SniProxy = "sni-proxy";
    public const string PubgMode = "pubg-mode";

    public static string For(Feature feature) => feature switch
    {
        Feature.DnsRouting => DnsRouting,
        Feature.SniProxy => SniProxy,
        Feature.PubgMode => PubgMode,
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };
}
```

`src/LunaDns.Core/Features/FeatureDomains.cs`:
```csharp
using LunaDns.Core.Domains;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Features;

/// <summary>Domain mà mỗi tính năng tác động (KH5 quyết định 1 và 3).</summary>
public static class FeatureDomains
{
    /// <summary>Định tuyến DNS (stub + NRPT) và Proxy SNI (PAC, CONNECT): chỉ nhóm Steam.</summary>
    public static IReadOnlyList<string> Routed { get; } = GameDomains.For(DomainGroup.Steam);

    /// <summary>SNI mà listener của Chế độ PUBG chấp nhận.</summary>
    public static IReadOnlyList<string> Pubg { get; } = GameDomains.For(DomainGroup.Pubg);

    /// <summary>Dòng hosts của Chế độ PUBG. Không bao giờ có *xenuine* hay accounts.pubg.com (spec 4.4).</summary>
    public static IReadOnlyList<HostsEntry> PubgHosts { get; } = [.. ProbeTargets.Pubg.Select(host => new HostsEntry("127.0.0.1", host))];
}
```

`src/LunaDns.Core/Features/FeatureTuning.cs`:
```csharp
using LunaDns.Core.Tls;

namespace LunaDns.Core.Features;

/// <summary>Tuỳ chọn cắt gói từ mục Nâng cao (spec 4.3, 4.8). Áp dụng khi dịch vụ được mở (bật hoặc watchdog mở lại).</summary>
public sealed record FeatureTuning(SplitMode SplitMode, TimeSpan SplitDelay)
{
    public static FeatureTuning Default { get; } = new(SplitMode.FirstByte, TimeSpan.Zero);
}
```

`src/LunaDns.Core/Features/IRunningService.cs`:
```csharp
namespace LunaDns.Core.Features;

/// <summary>Dịch vụ nền của một tính năng (stub DNS, proxy, listener PUBG). Dispose = dừng.</summary>
public interface IRunningService : IAsyncDisposable
{
    /// <summary>Xong khi dịch vụ dừng; faulted nếu chết vì lỗi. Watchdog chờ task này.</summary>
    Task Completion { get; }
}
```

`src/LunaDns.Core/Features/IRunningProxy.cs`:
```csharp
namespace LunaDns.Core.Features;

public interface IRunningProxy : IRunningService
{
    Uri PacUrl { get; }

    int Port { get; }

    int ActiveConnections { get; }
}
```

`src/LunaDns.Core/Features/IFeatureServices.cs`:
```csharp
namespace LunaDns.Core.Features;

/// <summary>Mở dịch vụ nền cho FeatureSet. Bản thật là <see cref="CoreFeatureServices"/>; test dùng bản giả.</summary>
public interface IFeatureServices
{
    /// <summary>Stub DoH trên 127.0.0.1:53 (spec 4.2). Ném DnsStubException nếu không mở được cổng.</summary>
    IRunningService StartDnsStub(IReadOnlyList<string> domains);

    /// <summary>
    /// SplitProxy (spec 4.3). <paramref name="port"/> = null: cổng trống đầu tiên trong 18080–18090; có giá trị: chỉ đúng
    /// cổng đó (watchdog mở lại mà giữ nguyên AutoConfigURL). Ném SplitProxyException.
    /// </summary>
    IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning);

    /// <summary>Listener 127.0.0.1:443 của Chế độ PUBG (spec 4.4). Ném SplitProxyException.</summary>
    IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning);
}
```

`src/LunaDns.Core/Features/CoreFeatureServices.cs`:
```csharp
using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Features;

/// <summary>
/// Dựng dịch vụ thật. Mỗi dịch vụ có DohClient riêng, tạo theo <paramref name="dohServers"/> lúc mở (lựa chọn DoH ở
/// mục Nâng cao có hiệu lực từ lần bật kế tiếp) và huỷ cùng dịch vụ. Proxy và listener PUBG phân giải máy chủ qua DoH,
/// không qua DNS hệ thống (KH3 quyết định 5, KH5 quyết định 4). Endpoint chỉ đổi trong test.
/// </summary>
public sealed class CoreFeatureServices(
    Func<IReadOnlyList<DohServer>> dohServers,
    ILoggerFactory? loggerFactory = null,
    IPEndPoint? dnsEndPoint = null,
    IPEndPoint? pubgEndPoint = null) : IFeatureServices
{
    private readonly ILoggerFactory _loggers = loggerFactory ?? NullLoggerFactory.Instance;

    public IRunningService StartDnsStub(IReadOnlyList<string> domains)
    {
        var doh = CreateDoh();
        try
        {
            var handler = new DnsQueryHandler(domains, doh, new DnsCache(), _loggers.CreateLogger<DnsQueryHandler>());
            var stub = DohStubServer.Start(dnsEndPoint ?? DohStubServer.DefaultEndPoint, handler, _loggers.CreateLogger<DohStubServer>());
            return new RunningService(stub.Completion, stub.DisposeAsync, doh);
        }
        catch
        {
            doh.Dispose();
            throw;
        }
    }

    public IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var doh = CreateDoh();
        try
        {
            var options = new SplitProxyOptions
            {
                Domains = domains,
                FirstPort = port ?? SplitProxyOptions.DefaultFirstPort,
                LastPort = port ?? SplitProxyOptions.DefaultLastPort,
                SplitMode = tuning.SplitMode,
                SplitDelay = tuning.SplitDelay,
            };
            var proxy = SplitProxy.Start(options, Connector(doh), _loggers.CreateLogger<SplitProxy>());
            return new RunningProxy(proxy, doh);
        }
        catch
        {
            doh.Dispose();
            throw;
        }
    }

    public IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var doh = CreateDoh();
        try
        {
            var options = new SniForwarderOptions { Domains = domains, SplitMode = tuning.SplitMode, SplitDelay = tuning.SplitDelay };
            var forwarder = SniForwarder.Start(pubgEndPoint ?? SniForwarder.DefaultEndPoint, options, Connector(doh), _loggers.CreateLogger<SniForwarder>());
            return new RunningService(forwarder.Completion, forwarder.DisposeAsync, doh);
        }
        catch
        {
            doh.Dispose();
            throw;
        }
    }

    private DohClient CreateDoh() => DohClient.Create(dohServers(), _loggers.CreateLogger<DohClient>());

    private static TcpUpstreamConnector Connector(DohClient doh) => new(new DohHostResolver(doh, new DnsCache()));

    private sealed class RunningService(Task completion, Func<ValueTask> stop, DohClient doh) : IRunningService
    {
        public Task Completion => completion;

        public async ValueTask DisposeAsync()
        {
            await stop().ConfigureAwait(false);
            doh.Dispose();
        }
    }

    private sealed class RunningProxy(SplitProxy proxy, DohClient doh) : IRunningProxy
    {
        public Task Completion => proxy.Completion;

        public Uri PacUrl => proxy.PacUrl;

        public int Port => proxy.Port;

        public int ActiveConnections => proxy.ActiveConnections;

        public async ValueTask DisposeAsync()
        {
            await proxy.DisposeAsync().ConfigureAwait(false);
            doh.Dispose();
        }
    }
}
```

`src/LunaDns.Core/Features/RestartPolicy.cs`:
```csharp
namespace LunaDns.Core.Features;

/// <summary>
/// Giới hạn của watchdog: tối đa <c>maxRestarts</c> lần khởi động lại trong một cửa sổ trượt (spec 6: 3 lần trong 1 phút).
/// Không an toàn đa luồng; FeatureSet chỉ gọi khi đang giữ cổng tuần tự của nó.
/// </summary>
public sealed class RestartPolicy
{
    public const int DefaultMaxRestarts = 3;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time;
    private readonly int _maxRestarts;
    private readonly TimeSpan _window;
    private readonly Queue<DateTimeOffset> _restarts = new();

    public RestartPolicy(TimeProvider? time = null, int maxRestarts = DefaultMaxRestarts, TimeSpan? window = null)
    {
        _time = time ?? TimeProvider.System;
        _maxRestarts = maxRestarts;
        _window = window ?? DefaultWindow;
    }

    /// <summary>Ghi một lần khởi động lại nếu còn trong giới hạn. false = đã đủ số lần trong cửa sổ, phải tắt tính năng.</summary>
    public bool TryRecordRestart()
    {
        var now = _time.GetUtcNow();
        while (_restarts.Count > 0 && now - _restarts.Peek() >= _window) _restarts.Dequeue();
        if (_restarts.Count >= _maxRestarts) return false;
        _restarts.Enqueue(now);
        return true;
    }
}
```

- [ ] **Step 4: Viết `Settings`**

`src/LunaDns.Core/Settings/AppSettings.cs`:
```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Settings;

/// <summary>Lựa chọn của người dùng (mục Nâng cao, ô "Kiểm tra cả PUBG", đã đọc cảnh báo PUBG). Bất biến; đổi bằng <c>with</c>.</summary>
public sealed record AppSettings
{
    public const int MaxSplitDelayMs = 20;

    public SplitMode SplitMode { get; init; } = SplitMode.FirstByte;

    public int SplitDelayMs { get; init; }

    public bool PreferGoogleDoh { get; init; }

    public bool CheckPubg { get; init; }

    public bool PubgWarningAccepted { get; init; }

    /// <summary>Thứ tự máy chủ DoH: Cloudflare trước (spec 4.2), hoặc Google trước nếu người dùng chọn.</summary>
    public IReadOnlyList<DohServer> DohServers() =>
        PreferGoogleDoh ? [DohServer.Google, DohServer.Cloudflare] : DohServer.Defaults;

    public FeatureTuning Tuning() =>
        new(SplitMode, TimeSpan.FromMilliseconds(Math.Clamp(SplitDelayMs, 0, MaxSplitDelayMs)));
}
```

`src/LunaDns.Core/Settings/SettingsJsonContext.cs`:
```csharp
using System.Text.Json.Serialization;

namespace LunaDns.Core.Settings;

/// <summary>Serializer sinh sẵn, để bản publish trimmed (KH6) không cần reflection cho settings.json.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
```

`src/LunaDns.Core/Settings/JsonFileSettingsStore.cs`:
```csharp
using System.Globalization;
using System.Text.Json;

namespace LunaDns.Core.Settings;

/// <summary>Lưu <see cref="AppSettings"/> ra file: ghi file tạm rồi thay. File hỏng thì cất sang bên cạnh và dùng mặc định.</summary>
public sealed class JsonFileSettingsStore(string path)
{
    public AppSettings Load()
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Cùng cách với JsonFileJournalStore: giữ file hỏng để chẩn đoán, không chặn app.
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(temp, path, overwrite: true);
    }
}
```

- [ ] **Step 5: Chạy test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: build 0 cảnh báo; **298 passed + 4 skipped** (276 + 22). `Sni_proxy_without_port_binds_in_default_range` cần một cổng trống trong 18080–18090. Nếu test này fail với `SplitProxyException PortInUse`, chạy `Get-NetTCPConnection -LocalPort 18080,18081 -State Listen` để xem app nào đang giữ cổng, rồi ghi vào báo cáo. Không sửa test.

- [ ] **Step 6: Tự kiểm, không commit**

Run: `git status --short src/LunaDns.Core tests/LunaDns.Core.Tests`
Expected: chỉ thêm các file của Task 1–2. Không `git add`.

---

### Task 3: `FeatureSet`: bật/tắt qua Journal, watchdog

**Files:**
- Create: `src/LunaDns.Core/Features/FeatureError.cs`, `FeatureOutcome.cs`, `FeatureResult.cs`, `FeatureStatus.cs`, `DisableAllResult.cs`, `FeatureStoppedEventArgs.cs`, `FeatureSet.cs`
- Modify: `tests/LunaDns.Core.Tests/Platform/FakeConfigurator.cs` (thêm `ExistingProxy`, `FailRevert`, `PlanDnsError`)
- Test: `tests/LunaDns.Core.Tests/Features/FakeFeatureServices.cs`, `FeatureSetTests.cs`, `FeatureSetWatchdogTests.cs`

**Interfaces:**
- Consumes: Task 2 (`Feature`, `FeatureKeys`, `FeatureDomains`, `FeatureTuning`, `IFeatureServices`, `IRunningService`, `IRunningProxy`, `RestartPolicy`); `ChangeJournal` (`Entries`, `Record`, `Rollback(string)`, `RollbackAll()`); `SystemChanges` (`AddDnsRoute`, `SetAutoProxy`, `AddHostsEntries`, `Reverters`); `ConfiguratorException.Error`; `DnsStubException.Error`; `SplitProxyException.Error`; `RollbackResult.Success/Failed`. Test dùng `InMemoryJournalStore`, `FakeConfigurator`, `Eventually`, `FakeTimeProvider`.
- Produces:
  - `public enum FeatureError { None, DnsPortBusy, ProxyPortsBusy, PubgPortBusy, ListenerFailed, AccessDenied, DnsRouteConflict, ExistingProxyConfig, UserHiveNotLoaded, UnexpectedProxyValue, HostsUnsupportedEncoding, NrptFailed, RollbackFailed, Other }`
  - `public enum FeatureOutcome { Enabled, Disabled, Unchanged, NeedsProxyOverride, Failed }`
  - `public sealed record FeatureResult(Feature Feature, FeatureOutcome Outcome, FeatureError Error = FeatureError.None, bool ChangesLeftBehind = false)`
  - `public sealed record FeatureStatus(bool DnsRouting, bool SniProxy, bool PubgMode, int? ProxyPort, int ProxyConnections)`
  - `public sealed record DisableAllResult(IReadOnlyList<FeatureResult> Features, int RemainingEntries) { bool Success }`
  - `public sealed class FeatureStoppedEventArgs(Feature feature, bool changesLeftBehind) : EventArgs`
  - `public sealed partial class FeatureSet(ChangeJournal journal, SystemChanges changes, IFeatureServices services, Func<FeatureTuning>? tuning = null, TimeProvider? time = null, ILogger<FeatureSet>? logger = null) : IAsyncDisposable` với `event EventHandler<FeatureStoppedEventArgs>? FeatureStopped`, `bool IsEnabled(Feature)`, `FeatureStatus GetStatus()`, `Task<FeatureResult> EnableAsync(Feature, bool allowProxyOverride = false, CancellationToken = default)`, `Task<FeatureResult> DisableAsync(Feature)`, `Task<DisableAllResult> DisableAllAsync()`, `Task<RollbackResult> ShutdownAsync()`

- [ ] **Step 1: Mở rộng `FakeConfigurator`**

Trong `tests/LunaDns.Core.Tests/Platform/FakeConfigurator.cs`:
1. Ngay sau dòng `public bool FailApply { get; set; }`, thêm:
```csharp
    public bool FailRevert { get; set; }

    /// <summary>Giả lập máy có sẵn PAC/proxy thủ công: PlanAutoProxy ném ExistingProxyConfig khi chưa allowOverride.</summary>
    public bool ExistingProxy { get; set; }

    public ConfiguratorError? PlanDnsError { get; set; }
```
2. Trong `PlanDnsRoute`, ngay sau `Calls.Add("plan:dns");`, thêm:
```csharp
        if (PlanDnsError is { } error) throw new ConfiguratorException(error, "giả lập lỗi plan");
```
3. Trong `PlanAutoProxy`, ngay sau `Calls.Add("plan:proxy");`, thêm:
```csharp
        if (ExistingProxy && !allowOverride)
            throw new ConfiguratorException(ConfiguratorError.ExistingProxyConfig, "giả lập proxy có sẵn");
```
4. Đổi ba method Revert thành:
```csharp
    public void RevertDnsRoute(DnsRouteSnapshot snapshot) => Revert($"revert:dns:{snapshot.Marker}");
```
```csharp
    public void RevertAutoProxy(AutoProxySnapshot snapshot) => Revert($"revert:proxy:{snapshot.NewValue}");
```
```csharp
    public void RevertHostsEntries(HostsSnapshot snapshot) => Revert($"revert:hosts:{snapshot.Lines[0]}");
```
5. Thêm method riêng, đặt ngay dưới `Apply(string call)`:
```csharp
    private void Revert(string call)
    {
        Calls.Add(call);
        if (FailRevert) throw new ConfiguratorException(ConfiguratorError.AccessDenied, "giả lập lỗi hoàn tác");
    }
```

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SystemChanges"`
Expected: PASS (giá trị mặc định giữ nguyên hành vi cũ).

- [ ] **Step 2: Viết dịch vụ giả**

`tests/LunaDns.Core.Tests/Features/FakeFeatureServices.cs`:
```csharp
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

internal class FakeRunningService : IRunningService
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _completion.Task;

    public bool Disposed { get; private set; }

    /// <summary>Chạy trong DisposeAsync, để test ghi thứ tự "hoàn tác trước, dừng sau".</summary>
    public Action? OnDispose { get; set; }

    /// <summary>Giả lập socket chết giữa chừng: Completion faulted.</summary>
    public void Crash() => _completion.TrySetException(new IOException("giả lập socket chết"));

    public ValueTask DisposeAsync()
    {
        if (!Disposed) OnDispose?.Invoke();
        Disposed = true;
        _completion.TrySetResult();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRunningProxy(int port) : FakeRunningService, IRunningProxy
{
    public Uri PacUrl { get; } = new($"http://127.0.0.1:{port}/proxy.pac");

    public int Port { get; } = port;

    public int ActiveConnections { get; set; }
}

internal sealed class FakeFeatureServices : IFeatureServices
{
    public const int DefaultProxyPort = 18080;

    private readonly object _gate = new();
    private readonly List<(Feature Feature, FakeRunningService Service)> _started = [];
    private readonly List<int?> _proxyPorts = [];
    private readonly Dictionary<Feature, Exception> _failStart = [];

    public IReadOnlyList<string>? LastDomains { get; private set; }

    public FeatureTuning? LastTuning { get; private set; }

    public IReadOnlyList<int?> ProxyPorts
    {
        get { lock (_gate) return [.. _proxyPorts]; }
    }

    /// <summary>Từ giờ mọi lần mở dịch vụ của <paramref name="feature"/> đều ném <paramref name="error"/>.</summary>
    public void FailStart(Feature feature, Exception error)
    {
        lock (_gate) _failStart[feature] = error;
    }

    public int Starts(Feature feature)
    {
        lock (_gate) return _started.Count(s => s.Feature == feature);
    }

    public IReadOnlyList<FakeRunningService> All(Feature feature)
    {
        lock (_gate) return [.. _started.Where(s => s.Feature == feature).Select(s => s.Service)];
    }

    public FakeRunningService Latest(Feature feature) => All(feature)[^1];

    public IRunningService StartDnsStub(IReadOnlyList<string> domains) =>
        Add(Feature.DnsRouting, domains, null, new FakeRunningService());

    public IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning)
    {
        lock (_gate) _proxyPorts.Add(port);
        return (FakeRunningProxy)Add(Feature.SniProxy, domains, tuning, new FakeRunningProxy(port ?? DefaultProxyPort));
    }

    public IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning) =>
        Add(Feature.PubgMode, domains, tuning, new FakeRunningService());

    private FakeRunningService Add(Feature feature, IReadOnlyList<string> domains, FeatureTuning? tuning, FakeRunningService service)
    {
        lock (_gate)
        {
            if (_failStart.TryGetValue(feature, out var error)) throw error;
            LastDomains = domains;
            LastTuning = tuning;
            _started.Add((feature, service));
            return service;
        }
    }
}
```

- [ ] **Step 3: Viết test bật/tắt**

`tests/LunaDns.Core.Tests/Features/FeatureSetTests.cs`:
```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Journal;
using LunaDns.Core.Tests.Platform;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureSetTests : IAsyncLifetime
{
    private const string PacUrl = "http://127.0.0.1:18080/proxy.pac";
    private static readonly string[] DnsEnableCalls = ["plan:dns", "apply:dns", "flush"];
    private static readonly string[] HostsEnableCalls = ["plan:hosts", "apply:hosts", "flush"];
    private static readonly Feature[] AllFeatures = [Feature.DnsRouting, Feature.SniProxy, Feature.PubgMode];
    private static readonly string[] DisableDnsCalls = ["revert:dns:LunaDNS test", "flush", "dispose"];
    private static readonly string[] LifoReverts =
    [
        "revert:hosts:127.0.0.1 prod-live-front.playbattlegrounds.com",
        "revert:proxy:" + PacUrl,
        "revert:dns:LunaDNS test",
    ];

    private readonly FakeConfigurator _configurator = new();
    private readonly FakeFeatureServices _services = new();
    private readonly ChangeJournal _journal;
    private readonly FeatureSet _features;

    public FeatureSetTests()
    {
        _journal = new ChangeJournal(new InMemoryJournalStore(), SystemChanges.Reverters(_configurator));
        _features = Create();
    }

    private FeatureSet Create(Func<FeatureTuning>? tuning = null) =>
        new(_journal, new SystemChanges(_journal, _configurator), _services, tuning);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _features.DisposeAsync();

    [Fact]
    public async Task Enable_dns_routing_starts_stub_then_records_and_applies_route()
    {
        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Enabled), result);
        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
        Assert.Equal(FeatureDomains.Routed, _services.LastDomains);
        Assert.Equal(FeatureKeys.DnsRouting, Assert.Single(_journal.Entries).Feature);
        Assert.Equal(DnsEnableCalls, _configurator.Calls);
    }

    [Fact]
    public async Task Enable_twice_is_unchanged()
    {
        await _features.EnableAsync(Feature.DnsRouting);

        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(FeatureOutcome.Unchanged, result.Outcome);
        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Stub_start_failure_writes_nothing()
    {
        _services.FailStart(Feature.DnsRouting, new DnsStubException(DnsStubError.PortInUse, "bận", new IOException()));

        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.DnsPortBusy), result);
        Assert.Empty(_journal.Entries);
        Assert.Empty(_configurator.Calls);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Apply_failure_rolls_back_own_entries_and_stops_service()
    {
        _configurator.FailApply = true;

        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.Other), result);
        Assert.Empty(_journal.Entries);
        Assert.Contains("revert:dns:LunaDNS test", _configurator.Calls);
        Assert.True(_services.Latest(Feature.DnsRouting).Disposed);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Dns_route_conflict_maps_to_its_error()
    {
        _configurator.PlanDnsError = ConfiguratorError.DnsRouteConflict;

        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(FeatureError.DnsRouteConflict, result.Error);
        Assert.Empty(_journal.Entries);
        Assert.True(_services.Latest(Feature.DnsRouting).Disposed);
    }

    [Fact]
    public async Task Existing_proxy_asks_for_override_then_enables_with_it()
    {
        _configurator.ExistingProxy = true;

        var first = await _features.EnableAsync(Feature.SniProxy);

        Assert.Equal(FeatureOutcome.NeedsProxyOverride, first.Outcome);
        Assert.Empty(_journal.Entries);
        Assert.True(_services.Latest(Feature.SniProxy).Disposed);
        Assert.False(_features.IsEnabled(Feature.SniProxy));

        var second = await _features.EnableAsync(Feature.SniProxy, allowProxyOverride: true);

        Assert.Equal(FeatureOutcome.Enabled, second.Outcome);
        var entry = Assert.Single(_journal.Entries);
        Assert.Equal(ChangeOps.AutoProxy, entry.Op);
        Assert.Equal(PacUrl, entry.Snapshot.GetProperty("NewValue").GetString());
    }

    [Fact]
    public async Task Proxy_port_busy_maps_to_its_error()
    {
        _services.FailStart(Feature.SniProxy, new SplitProxyException(ProxyStartError.PortInUse, "hết cổng", new IOException()));

        var result = await _features.EnableAsync(Feature.SniProxy);

        Assert.Equal(FeatureError.ProxyPortsBusy, result.Error);
    }

    [Fact]
    public async Task Pubg_port_busy_maps_to_its_error()
    {
        _services.FailStart(Feature.PubgMode, new SplitProxyException(ProxyStartError.AccessDenied, "443 bận", new IOException()));

        var result = await _features.EnableAsync(Feature.PubgMode);

        Assert.Equal(FeatureError.PubgPortBusy, result.Error);
    }

    [Fact]
    public async Task Tuning_comes_from_the_callback_at_start()
    {
        var tuning = new FeatureTuning(SplitMode.MidSni, TimeSpan.FromMilliseconds(5));
        await using var features = Create(() => tuning);

        await features.EnableAsync(Feature.SniProxy);

        Assert.Equal(tuning, _services.LastTuning);
    }

    [Fact]
    public async Task Pubg_mode_adds_pubg_hosts_and_starts_forwarder()
    {
        var result = await _features.EnableAsync(Feature.PubgMode);

        Assert.Equal(FeatureOutcome.Enabled, result.Outcome);
        Assert.Equal(FeatureDomains.Pubg, _services.LastDomains);
        Assert.Equal(HostsEnableCalls, _configurator.Calls);
        Assert.Equal(FeatureKeys.PubgMode, Assert.Single(_journal.Entries).Feature);
    }

    [Fact]
    public async Task Disable_reverts_first_then_stops_service()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _services.Latest(Feature.DnsRouting).OnDispose = () => _configurator.Calls.Add("dispose");

        var result = await _features.DisableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Disabled), result);
        Assert.Equal(DisableDnsCalls, _configurator.Calls.Skip(DnsEnableCalls.Length));
        Assert.Empty(_journal.Entries);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Disable_with_failed_revert_keeps_service_and_entry()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _configurator.FailRevert = true;

        var result = await _features.DisableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.RollbackFailed, ChangesLeftBehind: true), result);
        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.False(_services.Latest(Feature.DnsRouting).Disposed);
        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Disable_when_off_is_unchanged()
    {
        var result = await _features.DisableAsync(Feature.SniProxy);

        Assert.Equal(FeatureOutcome.Unchanged, result.Outcome);
        Assert.Empty(_configurator.Calls);
    }

    [Fact]
    public async Task Disable_all_reverts_lifo_and_stops_everything()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        await _features.EnableAsync(Feature.SniProxy);
        await _features.EnableAsync(Feature.PubgMode);

        var result = await _features.DisableAllAsync();

        Assert.True(result.Success);
        Assert.Equal(LifoReverts, _configurator.Calls.Where(c => c.StartsWith("revert:", StringComparison.Ordinal)));
        Assert.All(AllFeatures, f => Assert.True(_services.Latest(f).Disposed));
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Disable_all_retries_entries_left_by_a_previous_run()
    {
        _journal.Record(FeatureKeys.DnsRouting, ChangeOps.DnsRoute, new DnsRouteSnapshot("LunaDNS old", [".steampowered.com"], "127.0.0.1", []));

        var result = await _features.DisableAllAsync();

        Assert.True(result.Success);
        Assert.Contains("revert:dns:LunaDNS old", _configurator.Calls);
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Shutdown_stops_services_even_when_revert_fails()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _configurator.FailRevert = true;

        var result = await _features.ShutdownAsync();

        Assert.False(result.Success);
        Assert.True(_services.Latest(Feature.DnsRouting).Disposed);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Status_reports_proxy_port_and_connections()
    {
        await _features.EnableAsync(Feature.SniProxy);
        ((FakeRunningProxy)_services.Latest(Feature.SniProxy)).ActiveConnections = 3;

        Assert.Equal(new FeatureStatus(false, true, false, FakeFeatureServices.DefaultProxyPort, 3), _features.GetStatus());
    }
}
```

- [ ] **Step 4: Viết test watchdog**

`tests/LunaDns.Core.Tests/Features/FeatureSetWatchdogTests.cs`:
```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Tests.Journal;
using LunaDns.Core.Tests.Platform;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureSetWatchdogTests : IAsyncLifetime
{
    private static readonly int?[] SamePortTwice = [null, FakeFeatureServices.DefaultProxyPort];

    private readonly FakeTimeProvider _time = new();
    private readonly FakeConfigurator _configurator = new();
    private readonly FakeFeatureServices _services = new();
    private readonly ChangeJournal _journal;
    private readonly FeatureSet _features;

    public FeatureSetWatchdogTests()
    {
        _journal = new ChangeJournal(new InMemoryJournalStore(), SystemChanges.Reverters(_configurator));
        _features = new FeatureSet(_journal, new SystemChanges(_journal, _configurator), _services, time: _time);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _features.DisposeAsync();

    private TaskCompletionSource<FeatureStoppedEventArgs> ObserveStopped()
    {
        var stopped = new TaskCompletionSource<FeatureStoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        _features.FeatureStopped += (_, e) => stopped.TrySetResult(e);
        return stopped;
    }

    private async Task CrashAndWaitForRestartAsync(Feature feature)
    {
        var before = _services.Starts(feature);
        _services.Latest(feature).Crash();
        await Eventually.TrueAsync(() => _services.Starts(feature) == before + 1);
    }

    [Fact]
    public async Task Crashed_service_is_restarted_without_new_journal_entries()
    {
        await _features.EnableAsync(Feature.DnsRouting);

        await CrashAndWaitForRestartAsync(Feature.DnsRouting);

        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.True(_services.All(Feature.DnsRouting)[0].Disposed);
        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Proxy_restart_reuses_the_same_port()
    {
        await _features.EnableAsync(Feature.SniProxy);

        await CrashAndWaitForRestartAsync(Feature.SniProxy);

        Assert.Equal(SamePortTwice, _services.ProxyPorts);
    }

    [Fact]
    public async Task Gives_up_after_three_restarts_in_a_minute()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        for (var i = 0; i < 3; i++) await CrashAndWaitForRestartAsync(Feature.DnsRouting);
        var stopped = ObserveStopped();

        _services.Latest(Feature.DnsRouting).Crash();
        var args = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Feature.DnsRouting, args.Feature);
        Assert.False(args.ChangesLeftBehind);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
        Assert.Empty(_journal.Entries);
        Assert.Contains("revert:dns:LunaDNS test", _configurator.Calls);
        Assert.Equal(4, _services.Starts(Feature.DnsRouting));
    }

    [Fact]
    public async Task Restart_budget_refills_after_a_minute()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        for (var i = 0; i < 3; i++) await CrashAndWaitForRestartAsync(Feature.DnsRouting);

        _time.Advance(TimeSpan.FromSeconds(61));
        await CrashAndWaitForRestartAsync(Feature.DnsRouting);

        Assert.True(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Failed_restarts_count_towards_the_limit()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _services.FailStart(Feature.DnsRouting, new DnsStubException(DnsStubError.PortInUse, "bận", new IOException()));
        var stopped = ObserveStopped();

        _services.Latest(Feature.DnsRouting).Crash();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(_features.IsEnabled(Feature.DnsRouting));
        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Disable_does_not_trigger_a_restart()
    {
        await _features.EnableAsync(Feature.DnsRouting);

        await _features.DisableAsync(Feature.DnsRouting);
        await Task.Delay(200);

        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
    }
}
```

- [ ] **Step 5: Chạy test, xác nhận fail biên dịch**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~FeatureSet"`
Expected: FAIL, lỗi CS0246 `FeatureSet`/`FeatureResult`/… không tồn tại.

- [ ] **Step 6: Viết các kiểu kết quả**

`src/LunaDns.Core/Features/FeatureError.cs`:
```csharp
namespace LunaDns.Core.Features;

/// <summary>Mã lỗi để giao diện chọn thông báo tiếng Việt (ErrorMessages trong App).</summary>
public enum FeatureError
{
    None,
    /// <summary>127.0.0.1:53 đang bị app khác giữ (PortInUse hoặc AccessDenied, ghi chú KH2).</summary>
    DnsPortBusy,
    /// <summary>Không còn cổng trống trong 18080–18090.</summary>
    ProxyPortsBusy,
    /// <summary>127.0.0.1:443 đang bị app khác giữ.</summary>
    PubgPortBusy,
    ListenerFailed,
    AccessDenied,
    DnsRouteConflict,
    /// <summary>Chỉ xuất hiện khi hoàn tác cũng lỗi; bình thường trả Outcome NeedsProxyOverride.</summary>
    ExistingProxyConfig,
    UserHiveNotLoaded,
    UnexpectedProxyValue,
    HostsUnsupportedEncoding,
    NrptFailed,
    RollbackFailed,
    Other,
}
```

`src/LunaDns.Core/Features/FeatureOutcome.cs`:
```csharp
namespace LunaDns.Core.Features;

public enum FeatureOutcome
{
    Enabled,
    Disabled,
    Unchanged,
    /// <summary>Máy đã có PAC/proxy riêng: hỏi người dùng rồi gọi lại EnableAsync với allowProxyOverride (KH4 quyết định 4).</summary>
    NeedsProxyOverride,
    Failed,
}
```

`src/LunaDns.Core/Features/FeatureResult.cs`:
```csharp
namespace LunaDns.Core.Features;

/// <summary>Kết quả một lệnh bật/tắt. ChangesLeftBehind = còn mục Journal chưa hoàn tác được (sẽ thử lại khi thoát hoặc lần mở sau).</summary>
public sealed record FeatureResult(Feature Feature, FeatureOutcome Outcome, FeatureError Error = FeatureError.None, bool ChangesLeftBehind = false);
```

`src/LunaDns.Core/Features/FeatureStatus.cs`:
```csharp
namespace LunaDns.Core.Features;

/// <summary>Khu vực trạng thái (spec 4.8): đang bật gì, cổng proxy, số kết nối.</summary>
public sealed record FeatureStatus(bool DnsRouting, bool SniProxy, bool PubgMode, int? ProxyPort, int ProxyConnections);
```

`src/LunaDns.Core/Features/DisableAllResult.cs`:
```csharp
namespace LunaDns.Core.Features;

public sealed record DisableAllResult(IReadOnlyList<FeatureResult> Features, int RemainingEntries)
{
    public bool Success => RemainingEntries == 0;
}
```

`src/LunaDns.Core/Features/FeatureStoppedEventArgs.cs`:
```csharp
namespace LunaDns.Core.Features;

/// <summary>Watchdog đã bỏ cuộc và tắt tính năng (spec 6).</summary>
public sealed class FeatureStoppedEventArgs(Feature feature, bool changesLeftBehind) : EventArgs
{
    public Feature Feature { get; } = feature;

    public bool ChangesLeftBehind { get; } = changesLeftBehind;
}
```

- [ ] **Step 7: Viết `FeatureSet`**

`src/LunaDns.Core/Features/FeatureSet.cs`:
```csharp
using System.Collections.Concurrent;
using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Features;

/// <summary>
/// Bật/tắt từng tính năng (spec 5, 6).
/// Bật: mở dịch vụ nền (stub, proxy, listener) → SystemChanges (ghi Journal rồi đổi hệ thống). Lỗi giữa chừng thì
/// hoàn tác phần của chính tính năng đó. Dịch vụ không mở được thì chưa ghi gì.
/// Tắt: hoàn tác Journal trước, dừng dịch vụ sau, để hệ thống không trỏ vào dịch vụ đã chết.
/// Watchdog: dịch vụ chết thì mở lại (proxy giữ đúng cổng), tối đa 3 lần/phút; quá thì tắt tính năng và báo FeatureStopped.
/// Mọi lệnh và watchdog chạy tuần tự qua một cổng.
/// </summary>
public sealed partial class FeatureSet : IAsyncDisposable
{
    private static readonly Feature[] ShutdownOrder = [Feature.PubgMode, Feature.SniProxy, Feature.DnsRouting];

    private readonly ChangeJournal _journal;
    private readonly SystemChanges _changes;
    private readonly IFeatureServices _services;
    private readonly Func<FeatureTuning> _tuning;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<Feature, Running> _running = new();

    public FeatureSet(
        ChangeJournal journal,
        SystemChanges changes,
        IFeatureServices services,
        Func<FeatureTuning>? tuning = null,
        TimeProvider? time = null,
        ILogger<FeatureSet>? logger = null)
    {
        _journal = journal;
        _changes = changes;
        _services = services;
        _tuning = tuning ?? (() => FeatureTuning.Default);
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Watchdog đã tắt một tính năng. Gọi từ luồng nền; giao diện tự chuyển về luồng UI.</summary>
    public event EventHandler<FeatureStoppedEventArgs>? FeatureStopped;

    public bool IsEnabled(Feature feature) => _running.ContainsKey(feature);

    public FeatureStatus GetStatus()
    {
        var proxy = _running.TryGetValue(Feature.SniProxy, out var running) ? running.Service as IRunningProxy : null;
        return new FeatureStatus(IsEnabled(Feature.DnsRouting), proxy is not null, IsEnabled(Feature.PubgMode), proxy?.Port, proxy?.ActiveConnections ?? 0);
    }

    public async Task<FeatureResult> EnableAsync(Feature feature, bool allowProxyOverride = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_running.ContainsKey(feature)) return new(feature, FeatureOutcome.Unchanged);

            IRunningService service;
            try
            {
                service = StartService(feature, port: null);
            }
            catch (Exception ex) when (ex is DnsStubException or SplitProxyException)
            {
                LogStartFailed(_logger, feature, ex);
                return new(feature, FeatureOutcome.Failed, StartError(feature, ex));
            }

            try
            {
                ApplySystem(feature, service, allowProxyOverride);
            }
            catch (Exception ex)
            {
                // Ranh giới của lệnh bật: hoàn tác phần vừa làm của chính tính năng này rồi báo lỗi (spec 6).
                var leftBehind = !_journal.Rollback(FeatureKeys.For(feature)).Success;
                await service.DisposeAsync().ConfigureAwait(false);
                if (ex is ConfiguratorException { Error: ConfiguratorError.ExistingProxyConfig } && !leftBehind)
                    return new(feature, FeatureOutcome.NeedsProxyOverride);
                LogApplyFailed(_logger, feature, ex);
                return new(feature, FeatureOutcome.Failed, SystemError(ex), leftBehind);
            }

            var started = new Running(service, new RestartPolicy(_time));
            _running[feature] = started;
            Watch(feature, started);
            LogEnabled(_logger, feature);
            return new(feature, FeatureOutcome.Enabled);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Tắt một tính năng. Hoàn tác lỗi thì dịch vụ vẫn chạy (mạng không hỏng) và mục Journal được giữ (KH5 quyết định 6).</summary>
    public async Task<FeatureResult> DisableAsync(Feature feature)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await DisableCoreAsync(feature).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Nút "Tắt hết & khôi phục": tắt PUBG, Proxy, DNS, rồi hoàn tác mọi mục Journal còn lại.</summary>
    public async Task<DisableAllResult> DisableAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var results = new List<FeatureResult>();
            foreach (var feature in ShutdownOrder) results.Add(await DisableCoreAsync(feature).ConfigureAwait(false));
            if (_running.IsEmpty && _journal.Entries.Count > 0) _journal.RollbackAll();
            return new DisableAllResult(results, _journal.Entries.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Thoát app hoặc đăng xuất: hoàn tác toàn bộ Journal rồi dừng mọi dịch vụ, kể cả khi hoàn tác lỗi.
    /// Mục lỗi được giữ cho lần mở sau hoặc --cleanup (spec 4.5).
    /// </summary>
    public async Task<RollbackResult> ShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var result = _journal.RollbackAll();
            foreach (var feature in ShutdownOrder)
            {
                if (!_running.TryRemove(feature, out var running)) continue;
                running.Stopping = true;
                await running.Service.DisposeAsync().ConfigureAwait(false);
            }
            if (!result.Success) LogShutdownLeftovers(_logger, result.Failed.Count);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    private async Task<FeatureResult> DisableCoreAsync(Feature feature)
    {
        var key = FeatureKeys.For(feature);
        if (!_running.TryGetValue(feature, out var running))
        {
            if (!_journal.Entries.Any(e => string.Equals(e.Feature, key, StringComparison.Ordinal)))
                return new(feature, FeatureOutcome.Unchanged);
            // Mục còn sót từ lần hoàn tác lỗi trước hoặc lần chạy trước: thử lại.
            return _journal.Rollback(key).Success
                ? new FeatureResult(feature, FeatureOutcome.Disabled)
                : new FeatureResult(feature, FeatureOutcome.Failed, FeatureError.RollbackFailed, ChangesLeftBehind: true);
        }

        var result = _journal.Rollback(key);
        if (!result.Success)
        {
            LogRollbackFailed(_logger, feature, result.Failed.Count);
            return new(feature, FeatureOutcome.Failed, FeatureError.RollbackFailed, ChangesLeftBehind: true);
        }
        running.Stopping = true;
        _running.TryRemove(feature, out _);
        await running.Service.DisposeAsync().ConfigureAwait(false);
        LogDisabled(_logger, feature);
        return new(feature, FeatureOutcome.Disabled);
    }

    private IRunningService StartService(Feature feature, int? port)
    {
        var tuning = _tuning();
        return feature switch
        {
            Feature.DnsRouting => _services.StartDnsStub(FeatureDomains.Routed),
            Feature.SniProxy => _services.StartSniProxy(FeatureDomains.Routed, port, tuning),
            Feature.PubgMode => _services.StartPubgForwarder(FeatureDomains.Pubg, tuning),
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
        };
    }

    private void ApplySystem(Feature feature, IRunningService service, bool allowProxyOverride)
    {
        var key = FeatureKeys.For(feature);
        switch (feature)
        {
            case Feature.DnsRouting:
                _changes.AddDnsRoute(key, FeatureDomains.Routed, IPAddress.Loopback);
                break;
            case Feature.SniProxy:
                _changes.SetAutoProxy(key, ((IRunningProxy)service).PacUrl, allowProxyOverride);
                break;
            case Feature.PubgMode:
                _changes.AddHostsEntries(key, FeatureDomains.PubgHosts);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
        }
    }

    private void Watch(Feature feature, Running running) => _ = WatchAsync(feature, running);

    private async Task WatchAsync(Feature feature, Running running)
    {
        try
        {
            await running.Service.Completion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Completion faulted = dịch vụ chết vì lỗi socket; xử lý như dừng bất ngờ ở dưới.
            LogServiceFaulted(_logger, feature, ex);
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (running.Stopping || !_running.TryGetValue(feature, out var current) || !ReferenceEquals(current, running)) return;

            // Giải phóng cổng và DohClient của dịch vụ đã chết trước khi mở lại trên cùng cổng.
            await running.Service.DisposeAsync().ConfigureAwait(false);
            var port = (running.Service as IRunningProxy)?.Port;
            while (running.Policy.TryRecordRestart())
            {
                try
                {
                    var restarted = new Running(StartService(feature, port), running.Policy);
                    _running[feature] = restarted;
                    Watch(feature, restarted);
                    LogRestarted(_logger, feature);
                    return;
                }
                catch (Exception ex) when (ex is DnsStubException or SplitProxyException)
                {
                    LogRestartFailed(_logger, feature, ex);
                }
            }

            _running.TryRemove(feature, out _);
            var result = _journal.Rollback(FeatureKeys.For(feature));
            LogGaveUp(_logger, feature);
            FeatureStopped?.Invoke(this, new FeatureStoppedEventArgs(feature, !result.Success));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static FeatureError StartError(Feature feature, Exception ex) => (feature, ex) switch
    {
        (Feature.DnsRouting, DnsStubException { Error: not DnsStubError.Other }) => FeatureError.DnsPortBusy,
        (Feature.SniProxy, SplitProxyException { Error: not ProxyStartError.Other }) => FeatureError.ProxyPortsBusy,
        (Feature.PubgMode, SplitProxyException { Error: not ProxyStartError.Other }) => FeatureError.PubgPortBusy,
        _ => FeatureError.ListenerFailed,
    };

    private static FeatureError SystemError(Exception ex) => ex is not ConfiguratorException configurator
        ? FeatureError.Other
        : configurator.Error switch
        {
            ConfiguratorError.AccessDenied => FeatureError.AccessDenied,
            ConfiguratorError.DnsRouteConflict => FeatureError.DnsRouteConflict,
            ConfiguratorError.ExistingProxyConfig => FeatureError.ExistingProxyConfig,
            ConfiguratorError.UserHiveNotLoaded => FeatureError.UserHiveNotLoaded,
            ConfiguratorError.UnexpectedRegistryValue => FeatureError.UnexpectedProxyValue,
            ConfiguratorError.HostsUnsupportedEncoding => FeatureError.HostsUnsupportedEncoding,
            ConfiguratorError.NrptFailed => FeatureError.NrptFailed,
            _ => FeatureError.Other,
        };

    private sealed class Running(IRunningService service, RestartPolicy policy)
    {
        public IRunningService Service { get; } = service;

        public RestartPolicy Policy { get; } = policy;

        /// <summary>Đặt khi tắt có chủ đích, để watchdog không mở lại.</summary>
        public bool Stopping { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã bật {Feature}")]
    private static partial void LogEnabled(ILogger logger, Feature feature);

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã tắt {Feature}")]
    private static partial void LogDisabled(ILogger logger, Feature feature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không mở được dịch vụ của {Feature}")]
    private static partial void LogStartFailed(ILogger logger, Feature feature, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không áp dụng được {Feature}, đã hoàn tác phần vừa làm")]
    private static partial void LogApplyFailed(ILogger logger, Feature feature, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hoàn tác {Feature} còn {Count} mục lỗi, giữ dịch vụ chạy")]
    private static partial void LogRollbackFailed(ILogger logger, Feature feature, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Thoát: còn {Count} mục Journal chưa hoàn tác, để lần mở sau hoặc --cleanup")]
    private static partial void LogShutdownLeftovers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dịch vụ của {Feature} dừng vì lỗi")]
    private static partial void LogServiceFaulted(ILogger logger, Feature feature, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Watchdog đã mở lại dịch vụ của {Feature}")]
    private static partial void LogRestarted(ILogger logger, Feature feature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Watchdog không mở lại được dịch vụ của {Feature}")]
    private static partial void LogRestartFailed(ILogger logger, Feature feature, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dịch vụ của {Feature} chết quá 3 lần trong 1 phút, đã tắt tính năng")]
    private static partial void LogGaveUp(ILogger logger, Feature feature);
}
```

- [ ] **Step 8: Chạy test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: build 0 cảnh báo; **321 passed + 4 skipped** (298 + 17 + 6). Chạy riêng `--filter "FullyQualifiedName~Watchdog"` thêm 5 lần liên tiếp và ghi lại nếu có lần nào fail. Test watchdog phụ thuộc thời gian chạy của luồng nền, nên một lần fail lẻ cũng phải báo về.

- [ ] **Step 9: Tự kiểm, không commit**

Run: `git status --short src/LunaDns.Core/Features tests/LunaDns.Core.Tests`
Expected: `M FakeConfigurator.cs`, các file mới của Task 3. Không `git add`.

---

### Task 4: Báo cáo chẩn đoán, log xoay vòng, `ProbeSummary`

**Files:**
- Create: `src/LunaDns.Core/Diagnostics/Redactor.cs`, `DiagnosticInput.cs`, `DiagnosticReport.cs`; `src/LunaDns.Core/Logging/RollingFileLoggerProvider.cs`; `src/LunaDns.Core/Features/ProbeSummary.cs`
- Test: `tests/LunaDns.Core.Tests/Diagnostics/RedactorTests.cs`, `DiagnosticReportTests.cs`; `tests/LunaDns.Core.Tests/Logging/RollingFileLoggerProviderTests.cs`; `tests/LunaDns.Core.Tests/Features/ProbeSummaryTests.cs`

**Interfaces:**
- Consumes: `ProbeResult`, `ProbeVerdict`, `ProbeRemedies`, `ProbeAdvisor.Recommend`, `ProbeTargets.Pubg`, `JournalEntry`, `DomainGroup`, `FeatureStatus` (T3).
- Produces:
  - `public sealed partial class Redactor(string userName, string machineName) { string Redact(string text) }`
  - `public sealed record DiagnosticInput(string AppVersion, string WindowsVersion, DateTimeOffset GeneratedAt, IReadOnlyList<ProbeResult>? Probe, IReadOnlyList<JournalEntry> Journal, IReadOnlyList<string> NrptRules, string AutoConfig, IReadOnlyList<string> HostsLines, IReadOnlyList<string> LogTail)`
  - `public static class DiagnosticReport { const int LogLines = 200; string Build(DiagnosticInput, Redactor); string DescribeAutoConfig(string? value) }`
  - `public sealed class RollingFileLoggerProvider(string directory, LogLevel minLevel = Information, long maxFileBytes = 1 MB, TimeProvider? time = null) : ILoggerProvider` với `const string FileName = "lunadns.log"`, `const int MaxFiles = 5`, `static IReadOnlyList<string> ReadTail(string directory, int count)`
  - `public sealed record ProbeGroupSummary(DomainGroup Group, IReadOnlyList<ProbeResult> Results) { ProbeVerdict Worst }`
  - `public sealed record ProbeSummary(IReadOnlyList<ProbeGroupSummary> Groups, DateTimeOffset CheckedAt) { static ProbeSummary Create(IReadOnlyList<ProbeResult>, DateTimeOffset); ProbeRemedies PendingSteamRemedies(FeatureStatus); bool SuggestPubgMode(FeatureStatus); bool AnyUnreachable }`

- [ ] **Step 1: Viết test**

`tests/LunaDns.Core.Tests/Diagnostics/RedactorTests.cs`:
```csharp
using LunaDns.Core.Diagnostics;

namespace LunaDns.Core.Tests.Diagnostics;

public sealed class RedactorTests
{
    private static readonly Redactor Default = new("lunav", "DESKTOP-42");

    [Fact]
    public void Replaces_user_and_machine_case_insensitively() =>
        Assert.Equal(@"C:\Users\<user>\Desktop trên <host>", Default.Redact(@"C:\Users\LunaV\Desktop trên desktop-42"));

    [Fact]
    public void Leaves_longer_words_alone() =>
        Assert.Equal("annual pcs", new Redactor("ann", "pc").Redact("annual pcs"));

    [Fact]
    public void Masks_user_sid() =>
        Assert.Equal(@"HKU\<sid>\Software", Default.Redact(@"HKU\S-1-5-21-111-222-333-1001\Software"));

    [Theory]
    [InlineData("192.168.1.23", "192.168.x.x")]
    [InlineData("10.1.2.3", "10.1.x.x")]
    [InlineData("172.20.0.5", "172.20.x.x")]
    [InlineData("100.101.102.103", "100.101.x.x")]
    public void Masks_lan_ipv4(string input, string expected) =>
        Assert.Equal($"ip {expected}.", Default.Redact($"ip {input}."));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("23.45.67.89")]
    [InlineData("172.32.0.1")]
    public void Keeps_public_and_loopback_ipv4(string input) =>
        Assert.Equal(input, Default.Redact(input));

    [Fact]
    public void Masks_ipv6_lan() =>
        Assert.Equal("<ipv6-lan> và <ipv6-lan>", Default.Redact("fd7a:115c:a1e0::53 và fe80::1"));

    [Fact]
    public void Keeps_version_numbers() =>
        Assert.Equal("Windows 10.0.19045.0", Default.Redact("Windows 10.0.19045.0"));

    [Fact]
    public void Blank_names_are_ignored() =>
        Assert.Equal("abc", new Redactor("", " ").Redact("abc"));
}
```

`tests/LunaDns.Core.Tests/Diagnostics/DiagnosticReportTests.cs`:
```csharp
using System.Net;
using System.Text.Json;
using LunaDns.Core.Diagnostics;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Diagnostics;

public sealed class DiagnosticReportTests
{
    private static readonly string[] Sections =
    [
        "== Kết quả kiểm tra ==", "== Journal (", "== Luật NRPT của LunaDNS ==", "== AutoConfigURL ==",
        "== Dòng hosts có # LunaDNS ==", "== 200 dòng log cuối ==",
    ];

    private static readonly Redactor Redactor = new("lunav", "DESKTOP-42");

    private static DiagnosticInput Input(
        IReadOnlyList<ProbeResult>? probe = null,
        IReadOnlyList<JournalEntry>? journal = null,
        IReadOnlyList<string>? logTail = null) =>
        new("0.5.0", "Windows 10 Pro", new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero), probe,
            journal ?? [], ["{abc} | LunaDNS 1 | .steampowered.com → 127.0.0.1"], "(trống)",
            ["127.0.0.1 prod-live-front.playbattlegrounds.com # LunaDNS"], logTail ?? []);

    [Fact]
    public void Lists_every_section_in_order()
    {
        var report = DiagnosticReport.Build(Input(), Redactor);

        var positions = Sections.Select(s => report.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.StartsWith("LunaDNS — báo cáo chẩn đoán", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_probe_says_not_run() =>
        Assert.Contains("(chưa chạy)", DiagnosticReport.Build(Input(), Redactor), StringComparison.Ordinal);

    [Fact]
    public void Probe_line_shows_verdict_and_both_address_lists()
    {
        var result = new ProbeResult("store.steampowered.com", ProbeVerdict.DnsPoisoned, [IPAddress.Loopback], [IPAddress.Parse("23.1.2.3")], true);

        var report = DiagnosticReport.Build(Input(probe: [result]), Redactor);

        Assert.Contains("store.steampowered.com: DnsPoisoned | DNS hệ thống: 127.0.0.1 (IP chặn) | DoH: 23.1.2.3", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Journal_snapshot_sid_is_masked()
    {
        var snapshot = JsonSerializer.SerializeToElement(new AutoProxySnapshot("S-1-5-21-1-2-3-1001", false, null, false, "http://127.0.0.1:18080/proxy.pac"));
        var entry = new JournalEntry(Guid.NewGuid(), "sni-proxy", ChangeOps.AutoProxy, snapshot, DateTimeOffset.UnixEpoch);

        var report = DiagnosticReport.Build(Input(journal: [entry]), Redactor);

        Assert.Contains("== Journal (1 mục) ==", report, StringComparison.Ordinal);
        Assert.Contains("<sid>", report, StringComparison.Ordinal);
        Assert.DoesNotContain("S-1-5-21-1-2-3-1001", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "(trống)")]
    [InlineData("http://127.0.0.1:18080/proxy.pac", "http://127.0.0.1:18080/proxy.pac")]
    [InlineData("http://wpad.corp.local/proxy.pac", "(có giá trị khác của người dùng, không ghi vào báo cáo)")]
    public void Auto_config_is_shown_only_when_it_is_ours(string? value, string expected) =>
        Assert.Equal(expected, DiagnosticReport.DescribeAutoConfig(value));

    [Fact]
    public void Keeps_only_last_200_log_lines()
    {
        var lines = Enumerable.Range(0, 250).Select(i => $"log {i}").ToList();

        var report = DiagnosticReport.Build(Input(logTail: lines), Redactor);

        Assert.Contains("log 249", report, StringComparison.Ordinal);
        Assert.Contains("log 50" + Environment.NewLine, report, StringComparison.Ordinal);
        Assert.DoesNotContain("log 49" + Environment.NewLine, report, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_lines_are_redacted()
    {
        var report = DiagnosticReport.Build(Input(logTail: [@"mở C:\Users\lunav\a.txt từ 192.168.1.9"]), Redactor);

        Assert.Contains(@"mở C:\Users\<user>\a.txt từ 192.168.x.x", report, StringComparison.Ordinal);
    }
}
```

`tests/LunaDns.Core.Tests/Logging/RollingFileLoggerProviderTests.cs`:
```csharp
using LunaDns.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Logging;

public sealed class RollingFileLoggerProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-logs-").FullName;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private RollingFileLoggerProvider Create(long maxFileBytes = RollingFileLoggerProvider.DefaultMaxFileBytes) =>
        new(_dir, LogLevel.Information, maxFileBytes, _time);

    // Gọi ILogger.Log trực tiếp để không phụ thuộc analyzer của các extension LogInformation/LogDebug.
    private static void Write(ILogger logger, LogLevel level, string message, Exception? exception = null) =>
        logger.Log(level, default, message, exception, (s, _) => s);

    private string ReadCurrent() => File.ReadAllText(Path.Combine(_dir, RollingFileLoggerProvider.FileName));

    [Fact]
    public void Writes_time_level_category_and_message()
    {
        using (var provider = Create()) Write(provider.CreateLogger("Test"), LogLevel.Information, "xin chào");

        Assert.Equal("2026-11-13 08:00:00.000 INFO Test: xin chào" + Environment.NewLine, ReadCurrent());
    }

    [Fact]
    public void Skips_levels_below_minimum()
    {
        using (var provider = Create())
        {
            var logger = provider.CreateLogger("Test");
            Write(logger, LogLevel.Debug, "ẩn");
            Write(logger, LogLevel.Warning, "hiện");
        }

        var text = ReadCurrent();
        Assert.DoesNotContain("ẩn", text, StringComparison.Ordinal);
        Assert.Contains("WARN Test: hiện", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_exception_indented_on_following_lines()
    {
        using (var provider = Create()) Write(provider.CreateLogger("Test"), LogLevel.Error, "hỏng", new InvalidOperationException("lý do"));

        Assert.Contains(Environment.NewLine + "    System.InvalidOperationException: lý do", ReadCurrent(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rotates_and_keeps_five_files()
    {
        using (var provider = Create(maxFileBytes: 100))
        {
            var logger = provider.CreateLogger("T");
            for (var i = 0; i < 50; i++) Write(logger, LogLevel.Information, $"dòng {i}");
        }

        Assert.Equal(5, Directory.GetFiles(_dir, "lunadns*.log").Length);
        Assert.True(File.Exists(Path.Combine(_dir, "lunadns.4.log")));
        Assert.False(File.Exists(Path.Combine(_dir, "lunadns.5.log")));
    }

    [Fact]
    public void Read_tail_returns_last_lines_across_files_in_order()
    {
        using (var provider = Create(maxFileBytes: 100))
        {
            var logger = provider.CreateLogger("T");
            for (var i = 0; i < 30; i++) Write(logger, LogLevel.Information, $"dòng {i}");
        }

        var tail = RollingFileLoggerProvider.ReadTail(_dir, 5);

        Assert.Equal(5, tail.Count);
        for (var i = 0; i < 5; i++) Assert.EndsWith($"dòng {25 + i}", tail[i], StringComparison.Ordinal);
    }

    [Fact]
    public void Read_tail_of_empty_directory_is_empty() =>
        Assert.Empty(RollingFileLoggerProvider.ReadTail(_dir, 200));
}
```

`tests/LunaDns.Core.Tests/Features/ProbeSummaryTests.cs`:
```csharp
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Features;

public sealed class ProbeSummaryTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
    private static readonly FeatureStatus AllOff = new(false, false, false, null, 0);

    private static ProbeResult Result(string host, ProbeVerdict verdict) => new(host, verdict, [], [], false);

    [Fact]
    public void Groups_steam_and_pubg_hosts()
    {
        var summary = ProbeSummary.Create(
            [Result("store.steampowered.com", ProbeVerdict.Ok), Result("prod-live-front.playbattlegrounds.com", ProbeVerdict.SniFiltered)], At);

        Assert.Equal(2, summary.Groups.Count);
        Assert.Equal(DomainGroup.Steam, summary.Groups[0].Group);
        Assert.Equal("store.steampowered.com", Assert.Single(summary.Groups[0].Results).Host);
        Assert.Equal(DomainGroup.Pubg, summary.Groups[1].Group);
        Assert.Equal(At, summary.CheckedAt);
    }

    [Fact]
    public void Without_pubg_results_there_is_one_group() =>
        Assert.Single(ProbeSummary.Create([Result("steamcommunity.com", ProbeVerdict.Ok)], At).Groups);

    [Fact]
    public void Worst_is_the_heaviest_verdict()
    {
        var summary = ProbeSummary.Create(
            [Result("a.steampowered.com", ProbeVerdict.DnsPoisoned), Result("b.steampowered.com", ProbeVerdict.SniFiltered), Result("c.steampowered.com", ProbeVerdict.Ok)], At);

        Assert.Equal(ProbeVerdict.SniFiltered, summary.Groups[0].Worst);
    }

    [Fact]
    public void Pending_remedies_drop_features_already_on()
    {
        var summary = ProbeSummary.Create([Result("store.steampowered.com", ProbeVerdict.SniFiltered)], At);

        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, summary.PendingSteamRemedies(AllOff));
        Assert.Equal(ProbeRemedies.SniProxy, summary.PendingSteamRemedies(AllOff with { DnsRouting = true }));
    }

    [Fact]
    public void Suggests_pubg_mode_only_when_blocked_and_off()
    {
        var blocked = ProbeSummary.Create([Result("prod-live-front.playbattlegrounds.com", ProbeVerdict.DnsPoisoned)], At);
        var fine = ProbeSummary.Create([Result("prod-live-front.playbattlegrounds.com", ProbeVerdict.Ok)], At);

        Assert.True(blocked.SuggestPubgMode(AllOff));
        Assert.False(blocked.SuggestPubgMode(AllOff with { PubgMode = true }));
        Assert.False(fine.SuggestPubgMode(AllOff));
    }

    [Fact]
    public void Any_unreachable_is_reported()
    {
        Assert.True(ProbeSummary.Create([Result("store.steampowered.com", ProbeVerdict.Unreachable)], At).AnyUnreachable);
        Assert.False(ProbeSummary.Create([Result("store.steampowered.com", ProbeVerdict.Ok)], At).AnyUnreachable);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận fail biên dịch**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~Diagnostics|FullyQualifiedName~Logging|FullyQualifiedName~ProbeSummary"`
Expected: FAIL, lỗi CS0246.

- [ ] **Step 3: Viết `Redactor`, `DiagnosticInput`, `DiagnosticReport`**

`src/LunaDns.Core/Diagnostics/Redactor.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace LunaDns.Core.Diagnostics;

/// <summary>
/// Che thông tin cá nhân trước khi khách gửi báo cáo qua chat (spec 4.9, KH5 quyết định 9): tên user, tên máy, SID,
/// IP LAN (10/8, 172.16/12, 192.168/16, 100.64/10, 169.254/16 thành a.b.x.x) và IPv6 ULA/link-local.
/// IP công cộng (Steam, DoH) và loopback được giữ vì cần cho hỗ trợ.
/// </summary>
public sealed partial class Redactor
{
    private readonly Regex? _user;
    private readonly Regex? _machine;

    public Redactor(string userName, string machineName)
    {
        _user = WholeWord(userName);
        _machine = WholeWord(machineName);
    }

    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_user is not null) text = _user.Replace(text, "<user>");
        if (_machine is not null) text = _machine.Replace(text, "<host>");
        text = SidPattern().Replace(text, "<sid>");
        text = Ipv4Pattern().Replace(text, MaskIpv4);
        return Ipv6LanPattern().Replace(text, "<ipv6-lan>");
    }

    private static Regex? WholeWord(string value) => string.IsNullOrWhiteSpace(value)
        ? null
        : new Regex($@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(value.Trim())}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string MaskIpv4(Match match)
    {
        var a = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var b = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var lan = a == 10
            || (a == 172 && b is >= 16 and <= 31)
            || (a == 192 && b == 168)
            || (a == 100 && b is >= 64 and <= 127)
            || (a == 169 && b == 254);
        return lan ? $"{a}.{b}.x.x" : match.Value;
    }

    [GeneratedRegex(@"S-1-5-21(?:-\d+){3,4}")]
    private static partial Regex SidPattern();

    [GeneratedRegex(@"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?!\.?\d)")]
    private static partial Regex Ipv4Pattern();

    [GeneratedRegex(@"(?<![0-9a-f:])(?:fe80|f[cd][0-9a-f]{2}):[0-9a-f:]*[0-9a-f]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6LanPattern();
}
```

`src/LunaDns.Core/Diagnostics/DiagnosticInput.cs`:
```csharp
using LunaDns.Core.Journal;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Diagnostics;

/// <summary>
/// Dữ liệu cho báo cáo (spec 4.9). Probe = null nếu chưa kiểm tra. AutoConfig là dòng đã mô tả sẵn
/// (<see cref="DiagnosticReport.DescribeAutoConfig"/> hoặc thông báo lỗi đọc).
/// </summary>
public sealed record DiagnosticInput(
    string AppVersion,
    string WindowsVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProbeResult>? Probe,
    IReadOnlyList<JournalEntry> Journal,
    IReadOnlyList<string> NrptRules,
    string AutoConfig,
    IReadOnlyList<string> HostsLines,
    IReadOnlyList<string> LogTail);
```

`src/LunaDns.Core/Diagnostics/DiagnosticReport.cs`:
```csharp
using System.Globalization;
using System.Net;
using System.Text;

namespace LunaDns.Core.Diagnostics;

/// <summary>Nội dung LunaDNS-report-&lt;ts&gt;.txt (spec 4.9). Toàn bộ văn bản đi qua <see cref="Redactor"/> trước khi trả.</summary>
public static class DiagnosticReport
{
    public const int LogLines = 200;

    public static string Build(DiagnosticInput input, Redactor redactor)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(redactor);
        var sb = new StringBuilder();
        sb.AppendLine("LunaDNS — báo cáo chẩn đoán");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Thời điểm: {input.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Phiên bản LunaDNS: {input.AppVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Windows: {input.WindowsVersion}");

        Section(sb, "Kết quả kiểm tra");
        if (input.Probe is null)
        {
            sb.AppendLine("(chưa chạy)");
        }
        else
        {
            foreach (var r in input.Probe)
            {
                var blocked = r.SystemAddressBlocked ? " (IP chặn)" : string.Empty;
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{r.Host}: {r.Verdict} | DNS hệ thống: {Addresses(r.SystemAddresses)}{blocked} | DoH: {Addresses(r.DohAddresses)}");
            }
        }

        Section(sb, string.Create(CultureInfo.InvariantCulture, $"Journal ({input.Journal.Count} mục)"));
        if (input.Journal.Count == 0) sb.AppendLine("(trống)");
        foreach (var e in input.Journal)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{e.AppliedAt:O} {e.Feature} {e.Op} {e.Id} {e.Snapshot.GetRawText()}");

        Section(sb, "Luật NRPT của LunaDNS");
        Lines(sb, input.NrptRules);
        Section(sb, "AutoConfigURL");
        sb.AppendLine(input.AutoConfig);
        Section(sb, "Dòng hosts có # LunaDNS");
        Lines(sb, input.HostsLines);
        Section(sb, string.Create(CultureInfo.InvariantCulture, $"{LogLines} dòng log cuối"));
        Lines(sb, [.. input.LogTail.TakeLast(LogLines)]);

        return redactor.Redact(sb.ToString());
    }

    /// <summary>Chỉ ghi URL PAC loopback (của LunaDNS); PAC công ty hay giá trị lạ không ra khỏi máy (KH5 quyết định 9).</summary>
    public static string DescribeAutoConfig(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(trống)";
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsLoopback
            ? value
            : "(có giá trị khác của người dùng, không ghi vào báo cáo)";
    }

    private static void Section(StringBuilder sb, string title) =>
        sb.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"== {title} ==");

    private static void Lines(StringBuilder sb, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) sb.AppendLine("(không có)");
        foreach (var line in lines) sb.AppendLine(line);
    }

    private static string Addresses(IReadOnlyList<IPAddress> addresses) =>
        addresses.Count == 0 ? "(không có)" : string.Join(", ", addresses);
}
```

- [ ] **Step 4: Viết `RollingFileLoggerProvider`**

`src/LunaDns.Core/Logging/RollingFileLoggerProvider.cs`:
```csharp
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LunaDns.Core.Logging;

/// <summary>
/// Log ra file xoay vòng (spec 6): <c>lunadns.log</c> tối đa 1 MB, rồi đẩy thành lunadns.1.log … lunadns.4.log (giữ 5 file).
/// Ghi đồng bộ dưới khoá và flush từng dòng: app ghi ít, và log phải còn nguyên nếu tiến trình chết ngay sau đó.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    public const string FileName = "lunadns.log";
    public const long DefaultMaxFileBytes = 1024 * 1024;
    public const int MaxFiles = 5;

    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly long _maxFileBytes;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private FileStream? _stream;

    public RollingFileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Information, long maxFileBytes = DefaultMaxFileBytes, TimeProvider? time = null)
    {
        _directory = directory;
        _minLevel = minLevel;
        _maxFileBytes = maxFileBytes;
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary><paramref name="count"/> dòng cuối, từ file cũ nhất tới file hiện tại (cho báo cáo chẩn đoán).</summary>
    public static IReadOnlyList<string> ReadTail(string directory, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var lines = new Queue<string>(count);
        for (var index = MaxFiles - 1; index >= 0; index--)
        {
            var path = PathOf(directory, index);
            if (!File.Exists(path)) continue;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    if (lines.Count == count) lines.Dequeue();
                    lines.Enqueue(line);
                }
            }
            catch (FileNotFoundException)
            {
                // File vừa bị xoay vòng giữa Exists và Open: phần còn lại vẫn đọc được ở file kế.
                continue;
            }
        }
        return [.. lines];
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var text = new StringBuilder()
            .Append(_time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(Label(level)).Append(' ').Append(category).Append(": ").Append(message);
        if (exception is not null)
            text.AppendLine().Append("    ").Append(exception.ToString().ReplaceLineEndings(Environment.NewLine + "    "));
        text.AppendLine();
        var bytes = Encoding.UTF8.GetBytes(text.ToString());

        lock (_gate)
        {
            try
            {
                var stream = _stream ??= Open();
                if (stream.Length > 0 && stream.Length + bytes.Length > _maxFileBytes)
                {
                    stream.Dispose();
                    Rotate();
                    stream = _stream = Open();
                }
                stream.Write(bytes);
                stream.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Không có chỗ nào khác để ghi lỗi của log: bỏ dòng này, mở lại file ở lần ghi sau.
                _stream?.Dispose();
                _stream = null;
            }
        }
    }

    private FileStream Open() =>
        new(PathOf(_directory, 0), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

    private void Rotate()
    {
        File.Delete(PathOf(_directory, MaxFiles - 1));
        for (var index = MaxFiles - 2; index >= 0; index--)
        {
            var from = PathOf(_directory, index);
            if (File.Exists(from)) File.Move(from, PathOf(_directory, index + 1));
        }
    }

    private static string PathOf(string directory, int index) =>
        Path.Combine(directory, index == 0 ? FileName : string.Create(CultureInfo.InvariantCulture, $"lunadns.{index}.log"));

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        _ => "CRIT",
    };

    private sealed class FileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            ArgumentNullException.ThrowIfNull(formatter);
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
```

- [ ] **Step 5: Viết `ProbeSummary`**

`src/LunaDns.Core/Features/ProbeSummary.cs`:
```csharp
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Features;

public sealed record ProbeGroupSummary(DomainGroup Group, IReadOnlyList<ProbeResult> Results)
{
    /// <summary>Verdict nặng nhất của nhóm, theo thứ tự Ok &lt; DnsPoisoned &lt; SniFiltered &lt; Unreachable.</summary>
    public ProbeVerdict Worst => Results.Count == 0 ? ProbeVerdict.Ok : Results.Max(r => r.Verdict);
}

/// <summary>Kết quả "Kiểm tra" gom theo nhóm Steam/PUBG cho màn hình chính (spec 4.8).</summary>
public sealed record ProbeSummary(IReadOnlyList<ProbeGroupSummary> Groups, DateTimeOffset CheckedAt)
{
    public bool AnyUnreachable => Groups.Any(g => g.Worst == ProbeVerdict.Unreachable);

    public static ProbeSummary Create(IReadOnlyList<ProbeResult> results, DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(results);
        var pubg = results.Where(IsPubg).ToList();
        var groups = new List<ProbeGroupSummary> { new(DomainGroup.Steam, [.. results.Where(r => !IsPubg(r))]) };
        if (pubg.Count > 0) groups.Add(new ProbeGroupSummary(DomainGroup.Pubg, pubg));
        return new ProbeSummary(groups, checkedAt);
    }

    /// <summary>Biện pháp cho nhóm Steam (bảng spec 4.1), bỏ những gì đang bật. Dùng cho nút "Áp dụng đề xuất".</summary>
    public ProbeRemedies PendingSteamRemedies(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var remedies = ProbeAdvisor.Recommend(Groups.Where(g => g.Group == DomainGroup.Steam).SelectMany(g => g.Results));
        if (status.DnsRouting) remedies &= ~ProbeRemedies.DnsRouting;
        if (status.SniProxy) remedies &= ~ProbeRemedies.SniProxy;
        return remedies;
    }

    /// <summary>PUBG bị chặn kiểu DNS/SNI và Chế độ PUBG đang tắt: chỉ gợi ý, không tự bật (KH5 quyết định 5).</summary>
    public bool SuggestPubgMode(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return !status.PubgMode && Groups.Any(g => g.Group == DomainGroup.Pubg
            && g.Results.Any(r => r.Verdict is ProbeVerdict.DnsPoisoned or ProbeVerdict.SniFiltered));
    }

    private static bool IsPubg(ProbeResult result) =>
        ProbeTargets.Pubg.Contains(result.Host, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 6: Chạy test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: build 0 cảnh báo; **356 passed + 4 skipped** (321 + 14 + 9 + 6 + 6).

- [ ] **Step 7: Tự kiểm, không commit**

Run: `git status --short src/LunaDns.Core tests/LunaDns.Core.Tests`
Expected: chỉ có thêm các file của Task 4. Không `git add`.

---

### Task 5: `WindowsDiagnostics` (đọc NRPT, `AutoConfigURL`, hosts cho báo cáo)

**Files:**
- Modify: `src/LunaDns.Platform.Windows/Proxy/AutoProxyRegistry.cs` (thêm `ReadCurrent`)
- Create: `src/LunaDns.Platform.Windows/Diagnostics/WindowsDiagnostics.cs`
- Modify: `tests/LunaDns.Platform.Windows.Tests/Nrpt/FakeNrptStore.cs` (thêm `FailList`)
- Test: `tests/LunaDns.Platform.Windows.Tests/Diagnostics/WindowsDiagnosticsTests.cs`

**Interfaces:**
- Consumes: `INrptStore.List`, `NrptRule`, `NrptDnsRouter.MarkerPrefix`, `NrptCimStore`, `AutoProxyRegistry(subKey, notify, logger)`, `AutoProxyRegistry.CurrentUserSid`, `HostsFileStore(path).Read/DefaultPath`, `HostsEditor.Marker`, `DiagnosticReport.DescribeAutoConfig` (T4), `ConfiguratorException`. Test dùng `FakeNrptStore`, `TempDirectory`.
- Produces:
  - `AutoProxyRegistry.ReadCurrent(string? userSid = null) : string?`: đọc `AutoConfigURL` thô (không expand), `null` nếu không có. Hive chưa nạp thì ném `ConfiguratorException(UserHiveNotLoaded)` như `Plan`.
  - `public sealed class WindowsDiagnostics(INrptStore nrpt, AutoProxyRegistry proxy, HostsFileStore hosts)` với `static WindowsDiagnostics CreateDefault()`, `static string WindowsVersion()`, `IReadOnlyList<string> LunaDnsNrptRules()`, `string AutoConfigLine(string? userSid = null)`, `IReadOnlyList<string> MarkedHostsLines()`.
  - Hàm đọc nào lỗi thì trả đúng một dòng `(không đọc được: <message>)`. Báo cáo vẫn ghi được các phần còn lại.

**Vì sao chỉ đọc:** báo cáo không được sửa hệ thống (spec 4.9). `WindowsDiagnostics` không gọi `Add`, `Remove`, `Apply` hay `Revert`. `--report` dựng nó trực tiếp, không qua `WindowsNetworkConfigurator`.

- [ ] **Step 1: Thêm `FailList` vào `FakeNrptStore`**

Trong `tests/LunaDns.Platform.Windows.Tests/Nrpt/FakeNrptStore.cs`:
1. Ngay sau dòng `public bool FailAdd { get; set; }`, thêm:
```csharp
    public bool FailList { get; set; }
```
2. Thay dòng `public IReadOnlyList<NrptRule> List() => [.. Rules];` bằng:
```csharp
    public IReadOnlyList<NrptRule> List() =>
        FailList ? throw new ConfiguratorException(ConfiguratorError.NrptFailed, "giả lập lỗi đọc NRPT") : [.. Rules];
```

- [ ] **Step 2: Viết test**

`tests/LunaDns.Platform.Windows.Tests/Diagnostics/WindowsDiagnosticsTests.cs`:
```csharp
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
```

- [ ] **Step 3: Chạy test, xác nhận lỗi biên dịch**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: lỗi biên dịch vì chưa có `LunaDns.Platform.Windows.Diagnostics`.

- [ ] **Step 4: Thêm `ReadCurrent` vào `AutoProxyRegistry`**

Trong `src/LunaDns.Platform.Windows/Proxy/AutoProxyRegistry.cs`, ngay trước dòng `private string KeyPath(string sid) => $@"{sid}\{_subKey}";`, thêm:
```csharp
    /// <summary>Giá trị AutoConfigURL hiện tại (thô, không expand) cho báo cáo chẩn đoán. Không ghi gì.</summary>
    public string? ReadCurrent(string? userSid = null)
    {
        var sid = userSid ?? CurrentUserSid();
        EnsureHiveLoaded(sid);
        using var key = Registry.Users.OpenSubKey(KeyPath(sid));
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
    }

```

- [ ] **Step 5: Viết `WindowsDiagnostics`**

`src/LunaDns.Platform.Windows/Diagnostics/WindowsDiagnostics.cs`:
```csharp
using System.Text;
using LunaDns.Core.Diagnostics;
using LunaDns.Core.Hosts;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;

namespace LunaDns.Platform.Windows.Diagnostics;

/// <summary>
/// Đọc trạng thái hệ thống cho báo cáo chẩn đoán (spec 4.9). Chỉ đọc. Mỗi phần lỗi riêng thành một dòng
/// "(không đọc được: …)" để báo cáo vẫn có các phần còn lại.
/// </summary>
public sealed class WindowsDiagnostics(INrptStore nrpt, AutoProxyRegistry proxy, HostsFileStore hosts)
{
    public static WindowsDiagnostics CreateDefault() =>
        new(new NrptCimStore(), new AutoProxyRegistry(), new HostsFileStore(HostsFileStore.DefaultPath()));

    public static string WindowsVersion() => Environment.OSVersion.VersionString;

    /// <summary>Luật NRPT có Comment bắt đầu bằng "LunaDNS ", kể cả luật mồ côi không còn trong Journal (KH5 quyết định 10).</summary>
    public IReadOnlyList<string> LunaDnsNrptRules() => Read(() =>
        nrpt.List()
            .Where(r => r.Comment?.StartsWith(NrptDnsRouter.MarkerPrefix, StringComparison.Ordinal) == true)
            .Select(r => $"{r.Comment} | {string.Join(", ", r.Namespaces)} → {string.Join(", ", r.NameServers)}")
            .ToList());

    /// <summary>Dòng AutoConfigURL đã che (chỉ hiện PAC loopback, KH5 quyết định 9).</summary>
    public string AutoConfigLine(string? userSid = null) =>
        Read(() => [DiagnosticReport.DescribeAutoConfig(proxy.ReadCurrent(userSid))])[0];

    /// <summary>Các dòng hosts có "# LunaDNS". Dòng của LunaDNS chỉ có ASCII nên đọc bằng UTF-8 là đủ.</summary>
    public IReadOnlyList<string> MarkedHostsLines() => Read(() =>
        Encoding.UTF8.GetString(hosts.Read())
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains(HostsEditor.Marker, StringComparison.Ordinal))
            .ToList());

    /// <summary>Ranh giới một mục báo cáo: lỗi gì cũng thành một dòng chữ, không làm hỏng cả báo cáo.</summary>
    private static IReadOnlyList<string> Read(Func<IReadOnlyList<string>> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return [$"(không đọc được: {ex.Message})"];
        }
    }
}
```

Lớp này không ghi log. Lỗi đã nằm ngay trong báo cáo, là chỗ người đọc cần thấy. `ex.Message` ở đây là message tiếng Việt của KH4 (`ConfiguratorException`) hoặc của .NET, không chứa giá trị `AutoConfigURL`. Toàn bộ báo cáo vẫn đi qua `Redactor` (T4).

- [ ] **Step 6: Chạy test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: build 0 cảnh báo; **37 passed + 4 skipped** (29 + 8). Bốn test skipped là test Admin của KH4. Chạy bằng Admin + mạng thì được 41/41.

- [ ] **Step 7: Tự kiểm, không commit**

Run: `git status --short src/LunaDns.Platform.Windows tests/LunaDns.Platform.Windows.Tests`
Expected: chỉ có `AutoProxyRegistry.cs` và `FakeNrptStore.cs` bị sửa, cùng hai file mới của Task 5. Không `git add`.

---

### Task 6: `LunaDns.App`, phần không giao diện (chuỗi, CLI, mutex, composition root, `--cleanup`, báo cáo)

**Files:**
- Create: `src/LunaDns.App/LunaDns.App.csproj` (lúc này là **Library**; T7 đổi sang WinExe), `Strings.resx`, `Strings.cs`, `ErrorMessages.cs`, `CommandLine.cs`, `AppPaths.cs`, `SingleInstance.cs`, `ProbeRunner.cs`, `AppServices.cs`, `CleanupCommand.cs`, `ReportWriter.cs`
- Create: `tests/LunaDns.App.Tests/LunaDns.App.Tests.csproj`, `TempDirectory.cs`, `FakeNetworkConfigurator.cs`, `EmptyNrptStore.cs`, `TestApp.cs`
- Test: `tests/LunaDns.App.Tests/CommandLineTests.cs`, `StringsTests.cs`, `ErrorMessagesTests.cs`, `SingleInstanceTests.cs`, `CleanupCommandTests.cs`, `ReportWriterTests.cs`, `AppServicesTests.cs`
- Modify: `LunaDns.sln` (bằng `dotnet sln add`, không sửa tay)

**Interfaces:**
- Consumes: T2 (`Feature`, `FeatureKeys`, `AppSettings`, `JsonFileSettingsStore`, `CoreFeatureServices`), T3 (`FeatureSet`, `FeatureError`, `FeatureOutcome`, `FeatureResult`, `FeatureStoppedEventArgs`), T4 (`Redactor`, `DiagnosticInput`, `DiagnosticReport`, `RollingFileLoggerProvider`, `ProbeSummary`), T5 (`WindowsDiagnostics`). Từ KH1–KH4: `ChangeJournal`, `JsonFileJournalStore`, `SystemChanges`, `RollbackResult`, `INetworkConfigurator` cùng các snapshot, `HostsEntry`, `ConfiguratorException`, `WindowsNetworkConfigurator.CreateDefault`, `Prober`, `TlsProbe`, `SystemHostResolver`, `DohHostResolver`, `DohClient.Create`, `ProbeTargets.For`, `ProbeResult`, `ProbeVerdict`, `DomainGroup`, `INrptStore`, `AutoProxyRegistry`, `HostsFileStore`.
- Produces (đều `internal`; test thấy được qua `InternalsVisibleTo`):
  - `static class Strings`: 50 thuộc tính chuỗi tĩnh, `Get(key)`, `Format(key, args)`, `Error(FeatureError)`, `Verdict(ProbeVerdict)`, `FeatureTitle(Feature)`, `FeatureDescription(Feature)`, `GroupName(DomainGroup)`. Khoá thiếu thì trả `"[khoá]"` chứ không ném.
  - `static class ErrorMessages { string? For(FeatureResult); string Stopped(FeatureStoppedEventArgs) }`
  - `enum CommandKind { Gui, Cleanup, Report, Invalid }`; `static class CommandLine { const string CleanupSwitch = "--cleanup", ReportSwitch = "--report"; CommandKind Parse(IReadOnlyList<string>) }`
  - `sealed record AppPaths(string DataDirectory, string LogDirectory, string ReportDirectory) { JournalFile; SettingsFile; static AppPaths Default(); void EnsureCreated() }`
  - `sealed class SingleInstance : IDisposable { const string DefaultName = @"Global\LunaDNS"; static SingleInstance? TryAcquire(string name = DefaultName); static bool SignalShow(string name = DefaultName); void ListenForShow(Action onShow) }`
  - `sealed class ProbeRunner(Func<AppSettings> settings, ILoggerFactory loggerFactory) { Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken) }`
  - `sealed partial class AppServices(AppPaths, INetworkConfigurator, ILoggerFactory, TimeProvider? = null) : IAsyncDisposable` với `static AppServices CreateDefault(AppPaths)`, `static ILoggerFactory CreateLoggerFactory(AppPaths)`, `Paths`, `LoggerFactory`, `Journal`, `Features`, `StartupRollback`, `Settings { get; set; }` (gán là lưu file), `LastProbe`, `Task<ProbeSummary> ProbeAsync(CancellationToken)`, `Task<RollbackResult> ShutdownAsync()`.
  - `static partial class CleanupCommand { int Run(AppPaths, Func<INetworkConfigurator>, ILogger, string mutexName = SingleInstance.DefaultName) }`: mã thoát 0 khi xong hoặc không có gì để làm, 1 khi còn mục hoàn tác lỗi.
  - `static class ReportWriter { string Write(AppPaths, IReadOnlyList<ProbeResult>?, WindowsDiagnostics, DateTimeOffset? time = null) }`: ghi `LunaDNS-report-yyyyMMdd-HHmmss.txt` dạng UTF-8 có BOM rồi trả đường dẫn.

**Ghi chú thiết kế:**
- `SingleInstance` dùng cờ `createdNew` của `Mutex`, không dùng `WaitOne(0)`. Lý do: mutex của .NET cho cùng một luồng lấy lại nhiều lần, nên `WaitOne(0)` trên luồng đang giữ mutex vẫn trả `true` và test "lần hai thất bại" sẽ sai. Khi tiến trình cũ bị kill, handle bị đóng và mutex biến mất, nên lần sau `createdNew` lại là `true`. Mutex có thread affinity: luồng nào lấy thì luồng đó phải `Dispose`. `Program` (T7) lấy và trả mutex trên luồng chính.
- `CleanupCommand` **giữ mutex trong suốt lúc hoàn tác**. Nhờ vậy giao diện không thể mở lên giữa chừng, và `--cleanup` không chạy khi app đang chạy (KH5 quyết định 10).
- `AppServices` hoàn tác Journal còn sót **ngay trong ctor** (spec 4.5: lần mở sau thì dọn). Việc này an toàn vì `Program` đã giữ mutex trước khi tạo `AppServices`.
- `AppServices.DisposeAsync` gọi `FeatureSet.DisposeAsync`, tức là hoàn tác toàn bộ Journal. Vì vậy `--report` (T7) **không** được dựng `AppServices`.

- [ ] **Step 1: Tạo project `LunaDns.App` (Library)**

`src/LunaDns.App/LunaDns.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <OutputType>Library</OutputType>
    <AssemblyName>LunaDNS</AssemblyName>
    <RootNamespace>LunaDns.App</RootNamespace>
    <Version>0.5.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="LunaDns.App.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LunaDns.Core\LunaDns.Core.csproj" />
    <ProjectReference Include="..\LunaDns.Platform.Windows\LunaDns.Platform.Windows.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
  </ItemGroup>

</Project>
```

`Strings.resx` nằm ở gốc project. SDK tự đưa nó vào làm `EmbeddedResource`, tên resource là `LunaDns.App.Strings.resources` (theo `RootNamespace`). Không có designer và không có `Generator`.

- [ ] **Step 2: Tạo project test và helper**

`tests/LunaDns.App.Tests/LunaDns.App.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\LunaDns.App\LunaDns.App.csproj" />
  </ItemGroup>

</Project>
```

Thêm vào solution:
```powershell
dotnet sln LunaDns.sln add src/LunaDns.App/LunaDns.App.csproj tests/LunaDns.App.Tests/LunaDns.App.Tests.csproj
```

`tests/LunaDns.App.Tests/TempDirectory.cs`: chép nguyên `tests/LunaDns.Platform.Windows.Tests/TempDirectory.cs`, chỉ đổi namespace thành `LunaDns.App.Tests`.

`tests/LunaDns.App.Tests/FakeNetworkConfigurator.cs`:
```csharp
using System.Net;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;

namespace LunaDns.App.Tests;

/// <summary>Configurator giả: Plan trả snapshot dựng sẵn, Apply không làm gì, Revert đếm số lần (hoặc ném khi FailRevert).</summary>
internal sealed class FakeNetworkConfigurator : INetworkConfigurator
{
    public bool FailRevert { get; set; }
    public int Reverts { get; private set; }

    public DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer) =>
        new("LunaDNS test", [.. domains], nameServer.ToString(), []);

    public void ApplyDnsRoute(DnsRouteSnapshot snapshot) { }

    public void RevertDnsRoute(DnsRouteSnapshot snapshot) => Revert();

    public AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride) =>
        new("S-1-5-21-1-2-3-1001", false, null, false, pacUrl.AbsoluteUri);

    public void ApplyAutoProxy(AutoProxySnapshot snapshot) { }

    public void RevertAutoProxy(AutoProxySnapshot snapshot) => Revert();

    public HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries) =>
        new([.. entries.Select(e => $"{e.Address} {e.Host} # LunaDNS")], "\r\n", false);

    public void ApplyHostsEntries(HostsSnapshot snapshot) { }

    public void RevertHostsEntries(HostsSnapshot snapshot) => Revert();

    public void FlushDnsCache() { }

    private void Revert()
    {
        if (FailRevert) throw new ConfiguratorException(ConfiguratorError.AccessDenied, "giả lập lỗi hoàn tác");
        Reverts++;
    }
}
```

`tests/LunaDns.App.Tests/EmptyNrptStore.cs`:
```csharp
using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.App.Tests;

internal sealed class EmptyNrptStore : INrptStore
{
    public IReadOnlyList<NrptRule> List() => [];

    public void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName) =>
        throw new NotSupportedException("Báo cáo chẩn đoán không được thêm luật NRPT.");

    public void Remove(string name) => throw new NotSupportedException("Báo cáo chẩn đoán không được gỡ luật NRPT.");
}
```

`tests/LunaDns.App.Tests/TestApp.cs`:
```csharp
using LunaDns.Core.Features;
using LunaDns.Core.Hosts;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;

namespace LunaDns.App.Tests;

/// <summary>Thư mục dữ liệu tạm, tên mutex riêng và configurator giả cho một test.</summary>
internal sealed class TestApp : IDisposable
{
    private readonly TempDirectory _dir = new();

    public TestApp()
    {
        Paths = new AppPaths(_dir.PathOf("data"), _dir.PathOf("logs"), _dir.PathOf("desktop"));
        Paths.EnsureCreated();
    }

    public AppPaths Paths { get; }

    public string MutexName { get; } = $@"Global\LunaDNS.Test.{Guid.NewGuid():N}";

    public FakeNetworkConfigurator Configurator { get; } = new();

    /// <summary>Giống app bị kill khi Chế độ PUBG đang bật: journal còn một mục hosts.</summary>
    public void SeedJournal()
    {
        var journal = new ChangeJournal(new JsonFileJournalStore(Paths.JournalFile), SystemChanges.Reverters(Configurator));
        new SystemChanges(journal, Configurator).AddHostsEntries(FeatureKeys.PubgMode,
            [new HostsEntry("127.0.0.1", "prod-live-front.playbattlegrounds.com")]);
    }

    public int JournalCount() => new JsonFileJournalStore(Paths.JournalFile).Load().Count;

    public void Dispose() => _dir.Dispose();
}
```

- [ ] **Step 3: Viết test**

`tests/LunaDns.App.Tests/CommandLineTests.cs`:
```csharp
namespace LunaDns.App.Tests;

public sealed class CommandLineTests
{
    // Kiểu CommandKind là internal nên tham số theory dùng tên dạng chuỗi (tránh CS0051).
    [Theory]
    [InlineData("", "Gui")]
    [InlineData("--cleanup", "Cleanup")]
    [InlineData("--CLEANUP", "Cleanup")]
    [InlineData("--report", "Report")]
    [InlineData("--bogus", "Invalid")]
    [InlineData("--cleanup --report", "Invalid")]
    public void Parses(string args, string expected) =>
        Assert.Equal(expected, CommandLine.Parse(args.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToString());
}
```

`tests/LunaDns.App.Tests/StringsTests.cs`:
```csharp
using System.Globalization;
using System.Reflection;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.App.Tests;

public sealed class StringsTests
{
    private static void AssertResolved(string value) =>
        Assert.False(string.IsNullOrWhiteSpace(value) || value.StartsWith('['), $"Thiếu chuỗi: {value}");

    [Fact]
    public void Every_static_string_resolves()
    {
        var properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .ToList();

        Assert.Equal(50, properties.Count);
        foreach (var property in properties) AssertResolved((string)property.GetValue(null)!);
    }

    [Fact]
    public void Every_feature_error_except_none_has_text()
    {
        foreach (var error in Enum.GetValues<FeatureError>().Where(e => e != FeatureError.None))
            AssertResolved(Strings.Error(error));
    }

    [Fact]
    public void Every_verdict_has_text()
    {
        foreach (var verdict in Enum.GetValues<ProbeVerdict>()) AssertResolved(Strings.Verdict(verdict));
    }

    [Fact]
    public void Every_feature_and_shown_group_has_text()
    {
        foreach (var feature in Enum.GetValues<Feature>())
        {
            AssertResolved(Strings.FeatureTitle(feature));
            AssertResolved(Strings.FeatureDescription(feature));
        }
        AssertResolved(Strings.GroupName(DomainGroup.Steam));
        AssertResolved(Strings.GroupName(DomainGroup.Pubg));
    }

    /// <summary>KH5 quyết định 7: không có satellite assembly, Windows tiếng Anh vẫn ra tiếng Việt.</summary>
    [Fact]
    public void English_ui_culture_still_gets_vietnamese()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.Equal("Kiểm tra", Strings.CheckButton);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
```

`tests/LunaDns.App.Tests/ErrorMessagesTests.cs`:
```csharp
using LunaDns.Core.Features;

namespace LunaDns.App.Tests;

public sealed class ErrorMessagesTests
{
    [Theory]
    [InlineData(FeatureOutcome.Enabled)]
    [InlineData(FeatureOutcome.Disabled)]
    [InlineData(FeatureOutcome.Unchanged)]
    [InlineData(FeatureOutcome.NeedsProxyOverride)]
    public void Non_failures_have_no_message(FeatureOutcome outcome) =>
        Assert.Null(ErrorMessages.For(new FeatureResult(Feature.SniProxy, outcome)));

    [Fact]
    public void Failure_names_feature_and_error()
    {
        var message = ErrorMessages.For(new FeatureResult(Feature.PubgMode, FeatureOutcome.Failed, FeatureError.PubgPortBusy));

        Assert.NotNull(message);
        Assert.Contains(Strings.FeatureTitle(Feature.PubgMode), message, StringComparison.Ordinal);
        Assert.Contains(Strings.Error(FeatureError.PubgPortBusy), message, StringComparison.Ordinal);
        Assert.DoesNotContain(Strings.ChangesLeftBehind, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Changes_left_behind_are_mentioned()
    {
        var message = ErrorMessages.For(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.RollbackFailed, ChangesLeftBehind: true));

        Assert.EndsWith(Strings.ChangesLeftBehind, message, StringComparison.Ordinal);
    }
}
```

`tests/LunaDns.App.Tests/SingleInstanceTests.cs`:
```csharp
namespace LunaDns.App.Tests;

/// <summary>Mutex có thread affinity: mọi test ở đây chạy đồng bộ trên một luồng.</summary>
public sealed class SingleInstanceTests
{
    private readonly string _name = $@"Global\LunaDNS.Test.{Guid.NewGuid():N}";

    [Fact]
    public void Second_acquire_fails_while_held()
    {
        using var first = SingleInstance.TryAcquire(_name);

        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(_name));
    }

    [Fact]
    public void Can_acquire_again_after_dispose()
    {
        SingleInstance.TryAcquire(_name)!.Dispose();

        using var again = SingleInstance.TryAcquire(_name);

        Assert.NotNull(again);
    }

    [Fact]
    public void Signal_show_reaches_listener()
    {
        using var owner = SingleInstance.TryAcquire(_name)!;
        using var shown = new ManualResetEventSlim();
        owner.ListenForShow(shown.Set);

        Assert.True(SingleInstance.SignalShow(_name));
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Signal_show_without_owner_returns_false() =>
        Assert.False(SingleInstance.SignalShow(_name));
}
```

`tests/LunaDns.App.Tests/CleanupCommandTests.cs`:
```csharp
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class CleanupCommandTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private int Run() => CleanupCommand.Run(_app.Paths, () => _app.Configurator, NullLogger.Instance, _app.MutexName);

    [Fact]
    public void Does_nothing_while_app_is_running()
    {
        _app.SeedJournal();
        using var running = SingleInstance.TryAcquire(_app.MutexName);

        Assert.Equal(0, Run());
        Assert.Equal(0, _app.Configurator.Reverts);
        Assert.Equal(1, _app.JournalCount());
    }

    [Fact]
    public void Missing_journal_is_success_and_creates_nothing()
    {
        Assert.Equal(0, Run());
        Assert.False(File.Exists(_app.Paths.JournalFile));
    }

    [Fact]
    public void Rolls_back_leftovers()
    {
        _app.SeedJournal();

        Assert.Equal(0, Run());
        Assert.Equal(1, _app.Configurator.Reverts);
        Assert.Equal(0, _app.JournalCount());
    }

    [Fact]
    public void Failed_revert_returns_one_and_keeps_entry()
    {
        _app.SeedJournal();
        _app.Configurator.FailRevert = true;

        Assert.Equal(1, Run());
        Assert.Equal(1, _app.JournalCount());
    }
}
```

`tests/LunaDns.App.Tests/ReportWriterTests.cs`:
```csharp
using LunaDns.Core.Logging;
using LunaDns.Platform.Windows.Diagnostics;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Proxy;

namespace LunaDns.App.Tests;

public sealed class ReportWriterTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateTimeOffset Time = new(2026, 11, 13, 8, 9, 10, TimeSpan.FromHours(7));
    private readonly TestApp _app = new();
    private readonly WindowsDiagnostics _diagnostics;

    public ReportWriterTests()
    {
        var hosts = Path.Combine(_app.Paths.DataDirectory, "hosts");
        File.WriteAllText(hosts, "127.0.0.1 localhost\r\n");
        // Khoá test không tồn tại: ReadCurrent trả null, báo cáo ghi "(trống)". Không đụng Internet Settings thật.
        _diagnostics = new WindowsDiagnostics(new EmptyNrptStore(),
            new AutoProxyRegistry($@"Software\LunaDNS.Tests\{Guid.NewGuid():N}"), new HostsFileStore(hosts));
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Writes_named_utf8_bom_file_to_report_directory()
    {
        var path = ReportWriter.Write(_app.Paths, probe: null, _diagnostics, Time);

        Assert.Equal(Path.Combine(_app.Paths.ReportDirectory, "LunaDNS-report-20261113-080910.txt"), path);
        Assert.Equal(Bom, File.ReadAllBytes(path)[..3]);
        var text = File.ReadAllText(path);
        Assert.Contains("== Journal (0 mục) ==", text, StringComparison.Ordinal);
        Assert.Contains("(chưa chạy)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_tail_is_redacted()
    {
        File.WriteAllText(Path.Combine(_app.Paths.LogDirectory, RollingFileLoggerProvider.FileName),
            $"chạy bởi {Environment.UserName} trên {Environment.MachineName}{Environment.NewLine}");

        var text = File.ReadAllText(ReportWriter.Write(_app.Paths, probe: null, _diagnostics, Time));

        Assert.Contains("chạy bởi <user> trên <host>", text, StringComparison.Ordinal);
    }
}
```

`tests/LunaDns.App.Tests/AppServicesTests.cs`:
```csharp
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class AppServicesTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Leftover_journal_is_rolled_back_on_start()
    {
        _app.SeedJournal();

        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

        Assert.Equal(1, services.StartupRollback.Reverted);
        Assert.True(services.StartupRollback.Success);
        Assert.Empty(services.Journal.Entries);
        Assert.Equal(1, _app.Configurator.Reverts);
    }

    [Fact]
    public async Task Setting_settings_saves_file()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

        services.Settings = services.Settings with { CheckPubg = true, PubgWarningAccepted = true };

        var saved = new JsonFileSettingsStore(_app.Paths.SettingsFile).Load();
        Assert.True(saved.CheckPubg);
        Assert.True(saved.PubgWarningAccepted);
    }
}
```

- [ ] **Step 4: Chạy test, xác nhận lỗi biên dịch**

Run: `dotnet test tests/LunaDns.App.Tests`
Expected: lỗi biên dịch vì chưa có `Strings`, `CommandLine`, `SingleInstance`, v.v.

- [ ] **Step 5: Viết `Strings.resx` và `Strings.cs`**

`src/LunaDns.App/Strings.resx` có 75 mục: 50 chuỗi tĩnh, `Verdict_*` ×4, `Feature_*` ×3, `FeatureDesc_*` ×3, `Group_*` ×2, `Error_*` ×13 (mọi `FeatureError` trừ `None`). Lưu file dạng UTF-8.
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>

  <!-- 50 chuỗi tĩnh (mỗi mục có một thuộc tính trong Strings.cs) -->
  <data name="AppTitle" xml:space="preserve"><value>LunaDNS</value></data>
  <data name="HeaderSubtitle" xml:space="preserve"><value>Sửa lỗi vào Steam/PUBG khi nhà mạng chặn DNS hoặc lọc SNI.</value></data>
  <data name="CheckButton" xml:space="preserve"><value>Kiểm tra</value></data>
  <data name="CheckingStatus" xml:space="preserve"><value>Đang kiểm tra…</value></data>
  <data name="NotCheckedYet" xml:space="preserve"><value>Chưa kiểm tra. Bấm "Kiểm tra" để xem Steam và PUBG có bị chặn không.</value></data>
  <data name="CheckedAtFormat" xml:space="preserve"><value>Kiểm tra lúc {0:HH:mm:ss}.</value></data>
  <data name="CheckFailed" xml:space="preserve"><value>Không chạy được kiểm tra. Hãy thử lại; chi tiết nằm trong log.</value></data>
  <data name="GroupLineFormat" xml:space="preserve"><value>{0}: {1}</value></data>
  <data name="AllOk" xml:space="preserve"><value>Không thấy bị chặn. Không cần bật gì.</value></data>
  <data name="SuggestPubgMode" xml:space="preserve"><value>PUBG có vẻ bị chặn. Bạn có thể bật Chế độ PUBG (đọc cảnh báo trước khi bật).</value></data>
  <data name="UnreachableHint" xml:space="preserve"><value>Một số máy chủ không vào được kể cả khi né chặn. Có thể mạng đang lỗi hoặc máy chủ đang bảo trì.</value></data>
  <data name="ApplyRecommendedButton" xml:space="preserve"><value>Áp dụng đề xuất</value></data>
  <data name="NothingToApply" xml:space="preserve"><value>Các biện pháp đề xuất cho Steam đều đang bật.</value></data>
  <data name="StatusOff" xml:space="preserve"><value>Đang tắt</value></data>
  <data name="StatusOn" xml:space="preserve"><value>Đang bật</value></data>
  <data name="StatusProxyFormat" xml:space="preserve"><value>Đang bật · cổng {0} · {1} kết nối</value></data>
  <data name="SettingsHeader" xml:space="preserve"><value>Cài đặt</value></data>
  <data name="DiagnosticsButton" xml:space="preserve"><value>Chẩn đoán</value></data>
  <data name="DiagnosticsRunning" xml:space="preserve"><value>Đang ghi báo cáo…</value></data>
  <data name="ReportSavedFormat" xml:space="preserve"><value>Đã lưu báo cáo: {0}</value></data>
  <data name="ReportFailedFormat" xml:space="preserve"><value>Không ghi được báo cáo: {0}</value></data>
  <data name="DisableAllButton" xml:space="preserve"><value>Tắt hết &amp; khôi phục</value></data>
  <data name="DisableAllDone" xml:space="preserve"><value>Đã tắt hết và trả lại cấu hình mạng như trước.</value></data>
  <data name="CheckPubgLabel" xml:space="preserve"><value>Kiểm tra cả PUBG</value></data>
  <data name="AdvancedHeader" xml:space="preserve"><value>Nâng cao</value></data>
  <data name="AdvancedHint" xml:space="preserve"><value>Thay đổi có hiệu lực ở lần bật tính năng kế tiếp.</value></data>
  <data name="SplitModeLabel" xml:space="preserve"><value>Điểm cắt gói</value></data>
  <data name="SplitModeFirstByte" xml:space="preserve"><value>Sau byte đầu tiên (mặc định)</value></data>
  <data name="SplitModeMidSni" xml:space="preserve"><value>Giữa tên miền (SNI)</value></data>
  <data name="SplitDelayLabel" xml:space="preserve"><value>Độ trễ giữa hai phần (ms, 0–20)</value></data>
  <data name="DohLabel" xml:space="preserve"><value>Máy chủ DoH</value></data>
  <data name="DohCloudflareFirst" xml:space="preserve"><value>Cloudflare trước, Google dự phòng</value></data>
  <data name="DohGoogleFirst" xml:space="preserve"><value>Google trước, Cloudflare dự phòng</value></data>
  <data name="TrayTooltip" xml:space="preserve"><value>LunaDNS đang chạy</value></data>
  <data name="TrayOpen" xml:space="preserve"><value>Mở LunaDNS</value></data>
  <data name="TrayQuit" xml:space="preserve"><value>Thoát &amp; khôi phục</value></data>
  <data name="ConfirmYes" xml:space="preserve"><value>Đồng ý</value></data>
  <data name="ConfirmNo" xml:space="preserve"><value>Huỷ</value></data>
  <data name="PubgWarningTitle" xml:space="preserve"><value>Bật Chế độ PUBG?</value></data>
  <data name="PubgWarningText" xml:space="preserve"><value>Chế độ này sửa file hosts và chuyển tiếp kết nối của PUBG. Chưa ghi nhận bị ban, nhưng không có gì đảm bảo với anti-cheat.</value></data>
  <data name="ProxyOverrideTitle" xml:space="preserve"><value>Thay cấu hình proxy hiện có?</value></data>
  <data name="ProxyOverrideText" xml:space="preserve"><value>Máy đang dùng cấu hình proxy riêng (file PAC hoặc proxy thủ công). Bật Proxy SNI sẽ thay cấu hình đó cho tới khi bạn tắt Proxy SNI hoặc thoát LunaDNS. Khi đó cấu hình cũ được trả lại.</value></data>
  <data name="FeatureFailedFormat" xml:space="preserve"><value>Không bật/tắt được {0}: {1}</value></data>
  <data name="ChangesLeftBehind" xml:space="preserve"><value>Một số thay đổi chưa hoàn tác được. LunaDNS sẽ thử lại khi bạn tắt lại, khi thoát, hoặc khi mở lại.</value></data>
  <data name="FeatureStoppedFormat" xml:space="preserve"><value>{0} gặp lỗi lặp lại nên đã được tắt.</value></data>
  <data name="FeatureStoppedLeftBehindFormat" xml:space="preserve"><value>{0} gặp lỗi lặp lại nên đã dừng, nhưng chưa hoàn tác hết thay đổi. Hãy mở lại LunaDNS hoặc chạy LunaDNS.exe --cleanup.</value></data>
  <data name="RestoreFailed" xml:space="preserve"><value>Chưa hoàn tác hết thay đổi. Hãy mở lại LunaDNS hoặc chạy LunaDNS.exe --cleanup.</value></data>
  <data name="StartupRestoredFormat" xml:space="preserve"><value>Đã hoàn tác {0} thay đổi còn sót từ lần chạy trước.</value></data>
  <data name="StartupRestoreFailed" xml:space="preserve"><value>Còn thay đổi từ lần chạy trước chưa hoàn tác được. Bấm "Chẩn đoán" để ghi báo cáo.</value></data>
  <data name="UnexpectedError" xml:space="preserve"><value>Có lỗi không mong muốn. Chi tiết nằm trong log.</value></data>

  <!-- Theo enum: tra bằng "Tiền tố_" + tên giá trị -->
  <data name="Verdict_Ok" xml:space="preserve"><value>Bình thường</value></data>
  <data name="Verdict_DnsPoisoned" xml:space="preserve"><value>DNS bị chặn</value></data>
  <data name="Verdict_SniFiltered" xml:space="preserve"><value>Bị lọc SNI</value></data>
  <data name="Verdict_Unreachable" xml:space="preserve"><value>Không vào được</value></data>
  <data name="Feature_DnsRouting" xml:space="preserve"><value>Định tuyến DNS</value></data>
  <data name="Feature_SniProxy" xml:space="preserve"><value>Proxy SNI</value></data>
  <data name="Feature_PubgMode" xml:space="preserve"><value>Chế độ PUBG</value></data>
  <data name="FeatureDesc_DnsRouting" xml:space="preserve"><value>Hỏi tên miền Steam qua DNS mã hoá (DoH) thay cho DNS của nhà mạng. Cần cho đăng nhập Steam.</value></data>
  <data name="FeatureDesc_SniProxy" xml:space="preserve"><value>Cắt gói mở kết nối để Store và Community của Steam qua được bộ lọc SNI.</value></data>
  <data name="FeatureDesc_PubgMode" xml:space="preserve"><value>Chuyển tiếp kết nối lobby PUBG qua máy này để né lọc SNI.</value></data>
  <data name="Group_Steam" xml:space="preserve"><value>Steam</value></data>
  <data name="Group_Pubg" xml:space="preserve"><value>PUBG</value></data>
  <data name="Error_DnsPortBusy" xml:space="preserve"><value>Cổng DNS 53 trên 127.0.0.1 đang bị chương trình khác dùng.</value></data>
  <data name="Error_ProxyPortsBusy" xml:space="preserve"><value>Không còn cổng trống cho Proxy SNI.</value></data>
  <data name="Error_PubgPortBusy" xml:space="preserve"><value>Cổng 443 trên 127.0.0.1 đang bị chương trình khác dùng (thường là web server hoặc phần mềm VPN).</value></data>
  <data name="Error_ListenerFailed" xml:space="preserve"><value>Không mở được dịch vụ nội bộ.</value></data>
  <data name="Error_AccessDenied" xml:space="preserve"><value>Không có quyền sửa cấu hình mạng. Hãy chạy LunaDNS bằng quyền Admin.</value></data>
  <data name="Error_DnsRouteConflict" xml:space="preserve"><value>Một phần mềm khác (VPN, Tailscale…) đang định tuyến DNS cho chính các tên miền này.</value></data>
  <data name="Error_ExistingProxyConfig" xml:space="preserve"><value>Máy đang dùng cấu hình proxy riêng.</value></data>
  <data name="Error_UserHiveNotLoaded" xml:space="preserve"><value>Không đọc được cấu hình của người dùng đang đăng nhập.</value></data>
  <data name="Error_UnexpectedProxyValue" xml:space="preserve"><value>Cấu hình proxy hiện có ở dạng lạ nên LunaDNS không ghi đè.</value></data>
  <data name="Error_HostsUnsupportedEncoding" xml:space="preserve"><value>File hosts dùng bảng mã mà LunaDNS không sửa an toàn được.</value></data>
  <data name="Error_NrptFailed" xml:space="preserve"><value>Windows từ chối thêm luật định tuyến DNS.</value></data>
  <data name="Error_RollbackFailed" xml:space="preserve"><value>Chưa hoàn tác được thay đổi.</value></data>
  <data name="Error_Other" xml:space="preserve"><value>Lỗi không xác định; chi tiết nằm trong log.</value></data>
</root>
```

`src/LunaDns.App/Strings.cs`:
```csharp
using System.Globalization;
using System.Resources;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

[assembly: NeutralResourcesLanguage("vi")]

namespace LunaDns.App;

/// <summary>
/// Chuỗi giao diện trong Strings.resx (bản trung tính là tiếng Việt, KH5 quyết định 7).
/// Khoá thiếu thì trả "[khoá]" để giao diện không sập; StringsTests bắt các khoá thiếu.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager Resources = new("LunaDns.App.Strings", typeof(Strings).Assembly);

    public static string AppTitle => Get(nameof(AppTitle));
    public static string HeaderSubtitle => Get(nameof(HeaderSubtitle));
    public static string CheckButton => Get(nameof(CheckButton));
    public static string CheckingStatus => Get(nameof(CheckingStatus));
    public static string NotCheckedYet => Get(nameof(NotCheckedYet));
    public static string CheckedAtFormat => Get(nameof(CheckedAtFormat));
    public static string CheckFailed => Get(nameof(CheckFailed));
    public static string GroupLineFormat => Get(nameof(GroupLineFormat));
    public static string AllOk => Get(nameof(AllOk));
    public static string SuggestPubgMode => Get(nameof(SuggestPubgMode));
    public static string UnreachableHint => Get(nameof(UnreachableHint));
    public static string ApplyRecommendedButton => Get(nameof(ApplyRecommendedButton));
    public static string NothingToApply => Get(nameof(NothingToApply));
    public static string StatusOff => Get(nameof(StatusOff));
    public static string StatusOn => Get(nameof(StatusOn));
    public static string StatusProxyFormat => Get(nameof(StatusProxyFormat));
    public static string SettingsHeader => Get(nameof(SettingsHeader));
    public static string DiagnosticsButton => Get(nameof(DiagnosticsButton));
    public static string DiagnosticsRunning => Get(nameof(DiagnosticsRunning));
    public static string ReportSavedFormat => Get(nameof(ReportSavedFormat));
    public static string ReportFailedFormat => Get(nameof(ReportFailedFormat));
    public static string DisableAllButton => Get(nameof(DisableAllButton));
    public static string DisableAllDone => Get(nameof(DisableAllDone));
    public static string CheckPubgLabel => Get(nameof(CheckPubgLabel));
    public static string AdvancedHeader => Get(nameof(AdvancedHeader));
    public static string AdvancedHint => Get(nameof(AdvancedHint));
    public static string SplitModeLabel => Get(nameof(SplitModeLabel));
    public static string SplitModeFirstByte => Get(nameof(SplitModeFirstByte));
    public static string SplitModeMidSni => Get(nameof(SplitModeMidSni));
    public static string SplitDelayLabel => Get(nameof(SplitDelayLabel));
    public static string DohLabel => Get(nameof(DohLabel));
    public static string DohCloudflareFirst => Get(nameof(DohCloudflareFirst));
    public static string DohGoogleFirst => Get(nameof(DohGoogleFirst));
    public static string TrayTooltip => Get(nameof(TrayTooltip));
    public static string TrayOpen => Get(nameof(TrayOpen));
    public static string TrayQuit => Get(nameof(TrayQuit));
    public static string ConfirmYes => Get(nameof(ConfirmYes));
    public static string ConfirmNo => Get(nameof(ConfirmNo));
    public static string PubgWarningTitle => Get(nameof(PubgWarningTitle));
    public static string PubgWarningText => Get(nameof(PubgWarningText));
    public static string ProxyOverrideTitle => Get(nameof(ProxyOverrideTitle));
    public static string ProxyOverrideText => Get(nameof(ProxyOverrideText));
    public static string FeatureFailedFormat => Get(nameof(FeatureFailedFormat));
    public static string ChangesLeftBehind => Get(nameof(ChangesLeftBehind));
    public static string FeatureStoppedFormat => Get(nameof(FeatureStoppedFormat));
    public static string FeatureStoppedLeftBehindFormat => Get(nameof(FeatureStoppedLeftBehindFormat));
    public static string RestoreFailed => Get(nameof(RestoreFailed));
    public static string StartupRestoredFormat => Get(nameof(StartupRestoredFormat));
    public static string StartupRestoreFailed => Get(nameof(StartupRestoreFailed));
    public static string UnexpectedError => Get(nameof(UnexpectedError));

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) is { Length: > 0 } value ? value : $"[{key}]";

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static string Error(FeatureError error) => Get("Error_" + error);

    public static string Verdict(ProbeVerdict verdict) => Get("Verdict_" + verdict);

    public static string FeatureTitle(Feature feature) => Get("Feature_" + feature);

    public static string FeatureDescription(Feature feature) => Get("FeatureDesc_" + feature);

    public static string GroupName(DomainGroup group) => Get("Group_" + group);
}
```

- [ ] **Step 6: Viết `ErrorMessages`, `CommandLine`, `AppPaths`**

`src/LunaDns.App/ErrorMessages.cs`:
```csharp
using LunaDns.Core.Features;

namespace LunaDns.App;

/// <summary>Đổi kết quả của FeatureSet thành câu cho người dùng. Giao diện không hiện message của exception trong Core.</summary>
internal static class ErrorMessages
{
    /// <summary>null khi không có gì cần báo. NeedsProxyOverride do hộp thoại xác nhận xử lý, không phải lỗi.</summary>
    public static string? For(FeatureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome != FeatureOutcome.Failed) return null;
        var message = Strings.Format(nameof(Strings.FeatureFailedFormat), Strings.FeatureTitle(result.Feature), Strings.Error(result.Error));
        return result.ChangesLeftBehind ? $"{message} {Strings.ChangesLeftBehind}" : message;
    }

    public static string Stopped(FeatureStoppedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var key = e.ChangesLeftBehind ? nameof(Strings.FeatureStoppedLeftBehindFormat) : nameof(Strings.FeatureStoppedFormat);
        return Strings.Format(key, Strings.FeatureTitle(e.Feature));
    }
}
```

`src/LunaDns.App/CommandLine.cs`:
```csharp
namespace LunaDns.App;

internal enum CommandKind
{
    Gui,
    Cleanup,
    Report,
    Invalid,
}

/// <summary>LunaDNS.exe | LunaDNS.exe --cleanup | LunaDNS.exe --report (spec 4.5, 4.9). Không phân biệt hoa thường.</summary>
internal static class CommandLine
{
    public const string CleanupSwitch = "--cleanup";
    public const string ReportSwitch = "--report";

    public static CommandKind Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args switch
        {
            [] => CommandKind.Gui,
            [var only] when Is(only, CleanupSwitch) => CommandKind.Cleanup,
            [var only] when Is(only, ReportSwitch) => CommandKind.Report,
            _ => CommandKind.Invalid,
        };
    }

    private static bool Is(string arg, string expected) => string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase);
}
```

`src/LunaDns.App/AppPaths.cs`:
```csharp
namespace LunaDns.App;

/// <summary>
/// Journal, settings và log nằm trong %ProgramData%\LunaDNS (spec 4.5). Tác vụ dọn dẹp chạy bằng SYSTEM ở KH4b đọc cùng chỗ này.
/// Thư mục được tạo với ACL mặc định; ACL riêng thuộc KH4b (KH5 quyết định 12). Báo cáo ghi ra Desktop.
/// </summary>
internal sealed record AppPaths(string DataDirectory, string LogDirectory, string ReportDirectory)
{
    public string JournalFile => Path.Combine(DataDirectory, "journal.json");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static AppPaths Default()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LunaDNS");
        return new AppPaths(data, Path.Combine(data, "logs"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
```

Các đường dẫn này khớp spec: journal theo 4.5, log theo 6, báo cáo ra Desktop theo 4.9. Spec không nói `settings.json` nằm đâu, nên KH5 để nó cùng thư mục với journal.

- [ ] **Step 7: Viết `SingleInstance`**

`src/LunaDns.App/SingleInstance.cs`:
```csharp
namespace LunaDns.App;

/// <summary>
/// Mutex Global\LunaDNS (spec 4.5) cùng sự kiện "&lt;tên&gt;.Show": lần mở thứ hai chỉ bật sự kiện để cửa sổ đang chạy hiện lên, rồi thoát.
/// Dùng cờ createdNew chứ không dùng WaitOne(0), vì mutex .NET cho cùng một luồng lấy lại nhiều lần.
/// Có thread affinity: luồng nào gọi TryAcquire thì luồng đó phải Dispose.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string DefaultName = @"Global\LunaDNS";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    public static SingleInstance? TryAcquire(string name = DefaultName)
    {
        Mutex mutex;
        bool created;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name, out created);
        }
        catch (UnauthorizedAccessException)
        {
            // Mutex đã có, do phiên khác tạo với DACL chặt hơn: coi như app đang chạy.
            return null;
        }
        if (!created)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName(name)));
    }

    /// <summary>Báo bản đang chạy hiện cửa sổ. Trả false nếu không có bản nào đang chạy.</summary>
    public static bool SignalShow(string name = DefaultName)
    {
        if (!EventWaitHandle.TryOpenExisting(ShowEventName(name), out var handle)) return false;
        using (handle) return handle.Set();
    }

    /// <summary>onShow chạy trên luồng thread pool; bên gọi tự chuyển về luồng UI.</summary>
    public void ListenForShow(Action onShow)
    {
        ArgumentNullException.ThrowIfNull(onShow);
        _wait?.Unregister(null);
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _show.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static string ShowEventName(string name) => name + ".Show";
}
```

- [ ] **Step 8: Viết `ProbeRunner` và `AppServices`**

`src/LunaDns.App/ProbeRunner.cs`:
```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>Dựng Prober theo settings hiện tại cho mỗi lần "Kiểm tra" (spec 4.1), rồi bỏ DohClient sau khi xong.</summary>
internal sealed class ProbeRunner(Func<AppSettings> settings, ILoggerFactory loggerFactory)
{
    public async Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken)
    {
        var current = settings();
        using var doh = DohClient.Create(current.DohServers(), loggerFactory.CreateLogger<DohClient>());
        var prober = new Prober(
            new SystemHostResolver(loggerFactory.CreateLogger<SystemHostResolver>()),
            new DohHostResolver(doh),
            new TlsProbe(current.SplitMode, loggerFactory.CreateLogger<TlsProbe>()),
            logger: loggerFactory.CreateLogger<Prober>());
        return await prober.RunAsync(ProbeTargets.For(current.CheckPubg), cancellationToken);
    }
}
```

`src/LunaDns.App/AppServices.cs`:
```csharp
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Logging;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Platform.Windows;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// Composition root của giao diện. Ctor hoàn tác Journal còn sót từ lần chạy trước (spec 4.5); bên gọi phải giữ mutex trước.
/// DisposeAsync = FeatureSet.ShutdownAsync, tức là hoàn tác toàn bộ: KHÔNG dựng lớp này cho --report hay --cleanup.
/// </summary>
internal sealed partial class AppServices : IAsyncDisposable
{
    private readonly JsonFileSettingsStore _settingsStore;
    private readonly ProbeRunner _probes;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private volatile AppSettings _settings;

    public AppServices(AppPaths paths, INetworkConfigurator configurator, ILoggerFactory loggerFactory, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        paths.EnsureCreated();
        Paths = paths;
        LoggerFactory = loggerFactory;
        _time = time ?? TimeProvider.System;
        _logger = loggerFactory.CreateLogger<AppServices>();
        _settingsStore = new JsonFileSettingsStore(paths.SettingsFile);
        _settings = _settingsStore.Load();

        Journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile), SystemChanges.Reverters(configurator), _time);
        StartupRollback = Journal.RollbackAll();
        if (StartupRollback.Reverted > 0) LogStartupReverted(_logger, StartupRollback.Reverted);
        if (!StartupRollback.Success) LogStartupLeftovers(_logger, StartupRollback.Failed.Count);

        var services = new CoreFeatureServices(() => Settings.DohServers(), loggerFactory);
        Features = new FeatureSet(Journal, new SystemChanges(Journal, configurator), services,
            () => Settings.Tuning(), _time, loggerFactory.CreateLogger<FeatureSet>());
        _probes = new ProbeRunner(() => Settings, loggerFactory);
    }

    public AppPaths Paths { get; }

    public ILoggerFactory LoggerFactory { get; }

    public ChangeJournal Journal { get; }

    public FeatureSet Features { get; }

    public RollbackResult StartupRollback { get; }

    /// <summary>Gán là lưu settings.json ngay. Watchdog và DoH đọc giá trị mới ở lần khởi động dịch vụ kế tiếp.</summary>
    public AppSettings Settings
    {
        get => _settings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _settingsStore.Save(value);
            _settings = value;
        }
    }

    /// <summary>Kết quả "Kiểm tra" gần nhất, dùng cho nút "Chẩn đoán" (KH5 quyết định 15).</summary>
    public IReadOnlyList<ProbeResult>? LastProbe { get; private set; }

    public static AppServices CreateDefault(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureCreated();
        var loggerFactory = CreateLoggerFactory(paths);
        return new AppServices(paths, WindowsNetworkConfigurator.CreateDefault(loggerFactory), loggerFactory);
    }

    /// <summary>
    /// Log file xoay vòng ở %ProgramData%\LunaDNS\logs (spec 6). Dùng LoggerFactory.AddProvider (không dùng LoggerFactory.Create)
    /// để factory sở hữu provider và đóng file khi Dispose. --cleanup và --report (T7) dùng chung hàm này.
    /// </summary>
    public static ILoggerFactory CreateLoggerFactory(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var factory = new LoggerFactory();
        factory.AddProvider(new RollingFileLoggerProvider(paths.LogDirectory));
        return factory;
    }

    public async Task<ProbeSummary> ProbeAsync(CancellationToken cancellationToken)
    {
        var results = await _probes.RunAsync(cancellationToken);
        LastProbe = results;
        return ProbeSummary.Create(results, _time.GetLocalNow());
    }

    public Task<RollbackResult> ShutdownAsync() => Features.ShutdownAsync();

    public async ValueTask DisposeAsync()
    {
        await Features.DisposeAsync();
        LoggerFactory.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã hoàn tác {Count} thay đổi còn sót từ lần chạy trước.")]
    private static partial void LogStartupReverted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Còn {Count} thay đổi từ lần chạy trước chưa hoàn tác được; giữ trong journal.")]
    private static partial void LogStartupLeftovers(ILogger logger, int count);
}
```

- [ ] **Step 9: Viết `CleanupCommand` và `ReportWriter`**

`src/LunaDns.App/CleanupCommand.cs`:
```csharp
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// LunaDNS.exe --cleanup: hoàn tác mọi mục trong journal rồi thoát (spec 4.5). Giữ mutex trong suốt lúc chạy.
/// App đang chạy thì không làm gì và trả 0. Không đụng luật NRPT mồ côi (KH5 quyết định 10).
/// Mã thoát: 0 = xong hoặc không có gì để làm; 1 = còn mục lỗi, giữ trong journal.
/// </summary>
internal static partial class CleanupCommand
{
    public static int Run(AppPaths paths, Func<INetworkConfigurator> configurator, ILogger logger, string mutexName = SingleInstance.DefaultName)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(logger);
        using var instance = SingleInstance.TryAcquire(mutexName);
        if (instance is null)
        {
            LogAppRunning(logger);
            return 0;
        }
        if (!File.Exists(paths.JournalFile))
        {
            LogNothingToDo(logger);
            return 0;
        }

        var journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile), SystemChanges.Reverters(configurator()));
        if (journal.Entries.Count == 0)
        {
            LogNothingToDo(logger);
            return 0;
        }
        var result = journal.RollbackAll();
        foreach (var failure in result.Failed) LogRevertFailed(logger, failure.Entry.Feature, failure.Entry.Op, failure.Error);
        LogDone(logger, result.Reverted, result.Failed.Count);
        return result.Success ? 0 : 1;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "--cleanup: LunaDNS đang chạy, bỏ qua.")]
    private static partial void LogAppRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "--cleanup: journal trống, không có gì để hoàn tác.")]
    private static partial void LogNothingToDo(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--cleanup: không hoàn tác được {Feature}/{Op}; giữ trong journal.")]
    private static partial void LogRevertFailed(ILogger logger, string feature, string op, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "--cleanup: đã hoàn tác {Reverted} mục, còn {Failed} mục lỗi.")]
    private static partial void LogDone(ILogger logger, int reverted, int failed);
}
```

`src/LunaDns.App/ReportWriter.cs`:
```csharp
using System.Globalization;
using System.Text;
using LunaDns.Core.Diagnostics;
using LunaDns.Core.Journal;
using LunaDns.Core.Logging;
using LunaDns.Core.Probing;
using LunaDns.Platform.Windows.Diagnostics;

namespace LunaDns.App;

/// <summary>
/// Ghi LunaDNS-report-&lt;ts&gt;.txt (spec 4.9) dạng UTF-8 có BOM để Notepad cũ đọc đúng tiếng Việt.
/// Đọc journal bằng JsonFileJournalStore: file hỏng sẽ bị đổi tên .corrupt-&lt;ts&gt; (KH5 quyết định 15).
/// </summary>
internal static class ReportWriter
{
    public static string Write(AppPaths paths, IReadOnlyList<ProbeResult>? probe, WindowsDiagnostics diagnostics, DateTimeOffset? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var now = time ?? DateTimeOffset.Now;
        var input = new DiagnosticInput(
            typeof(ReportWriter).Assembly.GetName().Version?.ToString() ?? "?",
            WindowsDiagnostics.WindowsVersion(),
            now,
            probe,
            new JsonFileJournalStore(paths.JournalFile).Load(),
            diagnostics.LunaDnsNrptRules(),
            diagnostics.AutoConfigLine(),
            diagnostics.MarkedHostsLines(),
            RollingFileLoggerProvider.ReadTail(paths.LogDirectory, DiagnosticReport.LogLines));
        var text = DiagnosticReport.Build(input, new Redactor(Environment.UserName, Environment.MachineName));

        Directory.CreateDirectory(paths.ReportDirectory);
        var path = Path.Combine(paths.ReportDirectory, string.Create(CultureInfo.InvariantCulture, $"LunaDNS-report-{now:yyyyMMdd-HHmmss}.txt"));
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }
}
```

- [ ] **Step 10: Chạy test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.App.Tests`
Expected: build 0 cảnh báo; **29 passed** (CommandLine 6 + Strings 5 + ErrorMessages 6 + SingleInstance 4 + Cleanup 4 + Report 2 + AppServices 2).

Nếu analyzer báo lỗi thì sửa tại chỗ, không tắt rule cho cả project. Ví dụ:
- `CA1863` báo nên cache `CompositeFormat` trong `Strings.Format`: thêm `[SuppressMessage]` ngay trên hàm đó, `Justification` ghi "chuỗi định dạng đọc từ resx theo khoá lúc chạy".

- [ ] **Step 11: Chạy lại toàn bộ và tự kiểm, không commit**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests; dotnet test tests/LunaDns.Platform.Windows.Tests; dotnet test tests/LunaDns.App.Tests`
Expected: Core **356 + 4 skipped**, Platform **37 + 4 skipped**, App **29**.

Run: `git status --short`
Expected: so với trước Task 6, chỉ thêm `src/LunaDns.App/`, `tests/LunaDns.App.Tests/` và `LunaDns.sln` bị sửa. Không `git add`.

---

### Task 7: Giao diện Avalonia (Program, App, khay, MainWindow, ViewModel)

**Files:**
- Modify: `src/LunaDns.App/LunaDns.App.csproj` (Library → WinExe, thêm Avalonia)
- Create: `src/LunaDns.App/app.manifest`, `Assets/make-icon.ps1`, `Assets/lunadns.ico` (sinh bằng script)
- Create: `src/LunaDns.App/Program.cs`, `App.axaml`, `App.axaml.cs`
- Create: `src/LunaDns.App/Views/MainWindow.axaml`, `MainWindow.axaml.cs`, `ConfirmDialog.cs`
- Create: `src/LunaDns.App/ViewModels/ObservableObject.cs`, `AsyncCommand.cs`, `IUserPrompts.cs`, `FeatureViewModel.cs`, `MainViewModel.cs`

**Interfaces:**
- Consumes: toàn bộ T6 (`AppServices`, `AppPaths`, `CommandLine`, `SingleInstance`, `CleanupCommand`, `ReportWriter`, `ProbeRunner`, `Strings`, `ErrorMessages`); T3 (`FeatureSet` cùng event `FeatureStopped`, `FeatureStatus`, `FeatureOutcome`); T4 (`ProbeSummary`); T5 (`WindowsDiagnostics.CreateDefault`); `ProbeRemedies`, `ProbeVerdict`, `SplitMode`, `AppSettings`.
- Produces: `LunaDNS.exe` (WinExe, `requireAdministrator`), không có API mới cho task khác. Mọi lớp là `internal`. Các root XAML dùng `x:ClassModifier="internal"`.

**Không có unit test mới** (KH5 quyết định 13). Phần kiểm tay nằm ở Task 8. Lệnh kiểm tra của task này là build 0 cảnh báo và giữ xanh ba bộ test.

**Ghi chú thiết kế (đọc trước khi viết):**
- **Luồng.** `FeatureSet` có thể chặn luồng (CIM, registry, file hosts), nên VM gọi nó qua `Task.Run` rồi `await` (không `ConfigureAwait(false)`), để phần tiếp theo quay về luồng UI. Hai chỗ phải **chờ đồng bộ**: `ShutdownRequested` và `finally` của `Program`. Ở hai chỗ này, việc bọc trong `Task.Run(...)` là bắt buộc: luồng chính vẫn còn `AvaloniaSynchronizationContext`, nên nếu chờ trực tiếp, phần tiếp theo sẽ đợi một dispatcher không còn bơm message.
- **`ToggleSwitch`.** `IsChecked` bind `OneWay`, còn `Command` gọi VM. Khi người dùng bấm, control tự lật trước rồi mới chạy lệnh. Vì vậy `FeatureViewModel.Update` **luôn** phát `PropertyChanged(IsOn)`, kể cả khi giá trị không đổi, để binding đẩy lại trạng thái thật (ví dụ khi bật thất bại hoặc khi người dùng bấm Huỷ ở hộp thoại). Trong lúc lệnh chạy, `AsyncCommand.CanExecute` là `false`, nên công tắc bị khoá.
- **Timer** 2 giây chỉ cập nhật `StatusText` (cổng và số kết nối). Nó không đụng `IsOn`, để không lật công tắc khi người dùng đang bấm.
- **Thoát.** Có hai đường thoát, cả hai đều qua `App.QuitAsync`: nút "Thoát & khôi phục" trên khay, và đăng xuất hoặc tắt máy qua `ShutdownRequested`. Cờ `_shuttingDown` bảo đảm chỉ một đường chạy. Nút "Tắt hết & khôi phục" (spec 4.8) **không thoát app**: nó gọi `DisableAllAsync`. Đóng cửa sổ chỉ thu xuống khay.
- **Mở app thì tự chạy "Kiểm tra"** (spec 5) và hiện kết quả hoàn tác lúc khởi động (`StartupRollback`).
- **Mục Nâng cao** (spec 4.8) đổi `AppSettings` ngay khi người dùng chọn. Dịch vụ đang chạy không khởi động lại: thay đổi có hiệu lực ở lần bật kế tiếp (chuỗi `AdvancedHint`). Mục Cài đặt chỉ có "Chẩn đoán" (KH5 quyết định 11).
- Nếu Avalonia không nhận `x:ClassModifier="internal"`, đổi `App`, `MainWindow` và các VM sang `public` cùng lúc (tránh CS0051), rồi ghi lại trong báo cáo. Không đổi gì khác.

- [ ] **Step 1: Đổi csproj sang WinExe + Avalonia**

`src/LunaDns.App/LunaDns.App.csproj`, thay toàn bộ nội dung:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <OutputType>WinExe</OutputType>
    <AssemblyName>LunaDNS</AssemblyName>
    <RootNamespace>LunaDns.App</RootNamespace>
    <Version>0.5.0</Version>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\lunadns.ico</ApplicationIcon>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="LunaDns.App.Tests" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="Assets\lunadns.ico" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LunaDns.Core\LunaDns.Core.csproj" />
    <ProjectReference Include="..\LunaDns.Platform.Windows\LunaDns.Platform.Windows.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.23" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.23" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.23" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
  </ItemGroup>

</Project>
```

`src/LunaDns.App/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="0.5.0.0" name="LunaDNS.App"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <!-- Sửa NRPT, hosts và HKU\<SID> cần Admin (spec 4.8). -->
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10/11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

- [ ] **Step 2: Sinh icon**

`src/LunaDns.App/Assets/make-icon.ps1`:
```powershell
# Sinh lunadns.ico: một ảnh PNG 256x256 bọc trong ICO (Windows Vista+ đọc được). Chạy bằng Windows PowerShell 5.1 vì cần System.Drawing.
param([string]$OutFile = (Join-Path $PSScriptRoot 'lunadns.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$blue = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
$moon = [System.Drawing.Color]::FromArgb(255, 241, 245, 249)
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), 8, 8, 240, 240)
# Trăng lưỡi liềm: hình tròn sáng, rồi hình tròn nền lệch phải đè lên.
$g.FillEllipse((New-Object System.Drawing.SolidBrush $moon), 56, 52, 150, 150)
$g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), 100, 30, 150, 150)
$g.Dispose()

$png = New-Object System.IO.MemoryStream
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$bytes = $png.ToArray()

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]1)                # ICONDIR: reserved, type = icon, count = 1
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)   # 256x256 (0 nghĩa là 256), không palette, reserved
$w.Write([UInt16]1); $w.Write([UInt16]32)                                     # planes, bit/pixel
$w.Write([UInt32]$bytes.Length); $w.Write([UInt32]22)                         # kích thước PNG, offset = 6 + 16
$w.Write($bytes)
$w.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
Write-Output "Đã ghi $OutFile ($($out.Length) byte)"
```

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File src/LunaDns.App/Assets/make-icon.ps1`
Expected: có file `src/LunaDns.App/Assets/lunadns.ico`, nặng vài KB. File icon được đưa vào repo cùng script.

- [ ] **Step 3: Viết `Program`**

`src/LunaDns.App/Program.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Platform.Windows;
using LunaDns.Platform.Windows.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

internal static partial class Program
{
    private static readonly TimeSpan ReportProbeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Mã thoát: 0 = xong, 1 = còn việc chưa xong (xem log), 2 = tham số sai (WinExe không có console để in cách dùng).</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = AppPaths.Default();
        paths.EnsureCreated();
        return CommandLine.Parse(args) switch
        {
            CommandKind.Gui => RunGui(paths, args),
            CommandKind.Cleanup => RunCleanup(paths),
            CommandKind.Report => RunReport(paths),
            _ => 2,
        };
    }

    private static int RunGui(AppPaths paths, string[] args)
    {
        // Mutex lấy và trả trên luồng chính (thread affinity); lấy TRƯỚC khi AppServices hoàn tác journal còn sót.
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalShow();
            return 0;
        }
        var services = AppServices.CreateDefault(paths);
        try
        {
            return AppBuilder.Configure(() => new App { Services = services, Instance = instance })
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            // Hoàn tác lần cuối (idempotent nếu QuitAsync đã chạy). Chạy trên thread pool: xem ghi chú "Luồng" của Task 7.
            Task.Run(() => services.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
    }

    private static int RunCleanup(AppPaths paths)
    {
        using var loggerFactory = AppServices.CreateLoggerFactory(paths);
        return CleanupCommand.Run(paths, () => WindowsNetworkConfigurator.CreateDefault(loggerFactory),
            loggerFactory.CreateLogger(nameof(CleanupCommand)));
    }

    /// <summary>
    /// Không dựng AppServices: DisposeAsync của nó hoàn tác journal, kể cả khi app giao diện đang chạy.
    /// Tự chạy "Kiểm tra" tối đa 20 giây (KH5 quyết định 15); hết giờ thì báo cáo ghi "(chưa chạy)".
    /// </summary>
    private static int RunReport(AppPaths paths)
    {
        using var loggerFactory = AppServices.CreateLoggerFactory(paths);
        var logger = loggerFactory.CreateLogger(nameof(Program));
        var settings = new JsonFileSettingsStore(paths.SettingsFile).Load();
        IReadOnlyList<ProbeResult>? probe = null;
        using (var timeout = new CancellationTokenSource(ReportProbeTimeout))
        {
            try
            {
                probe = Task.Run(() => new ProbeRunner(() => settings, loggerFactory).RunAsync(timeout.Token)).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                LogReportProbeTimedOut(logger);
            }
        }
        try
        {
            ReportWriter.Write(paths, probe, WindowsDiagnostics.CreateDefault());
            LogReportWritten(logger);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReportFailed(logger, ex);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "--report: kiểm tra quá 20 giây, báo cáo không có kết quả kiểm tra.")]
    private static partial void LogReportProbeTimedOut(ILogger logger);

    // Không log đường dẫn: nó chứa tên user, mà log lại được đưa vào báo cáo sau này.
    [LoggerMessage(Level = LogLevel.Information, Message = "--report: đã ghi báo cáo ra Desktop.")]
    private static partial void LogReportWritten(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--report: không ghi được báo cáo.")]
    private static partial void LogReportFailed(ILogger logger, Exception error);
}
```

- [ ] **Step 4: Viết `App` (khay, thoát, đăng xuất)**

`src/LunaDns.App/App.axaml`:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LunaDns.App.App"
             x:ClassModifier="internal"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

`src/LunaDns.App/App.axaml.cs`:
```csharp
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using LunaDns.App.ViewModels;
using LunaDns.App.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App;

/// <summary>
/// Vòng đời giao diện: cửa sổ chính, khay (dựng bằng code), "Thoát &amp; khôi phục", và đăng xuất/tắt máy.
/// ShutdownMode.OnExplicitShutdown: đóng cửa sổ không thoát app (spec 4.8).
/// </summary>
/// <remarks>IDisposable chỉ để thoả CA1001 (giữ TrayIcon). Avalonia không gọi Dispose; QuitAsync và OnShutdownRequested tự gọi CloseTray.</remarks>
internal sealed partial class App : Application, IDisposable
{
    private static readonly TimeSpan SessionEndBudget = TimeSpan.FromSeconds(4);
    private static readonly Uri IconUri = new("avares://LunaDNS/Assets/lunadns.ico");

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private ILogger _logger = NullLogger.Instance;
    private int _shuttingDown;

    public AppServices? Services { get; init; }

    public SingleInstance? Instance { get; init; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            _desktop = desktop;
            _logger = Services.LoggerFactory.CreateLogger<App>();
            Dispatcher.UIThread.UnhandledException += OnUnhandledException;

            var prompts = new DialogPrompts(() => ShowWindow());
            _viewModel = new MainViewModel(Services, prompts, QuitAsync);
            _window = new MainWindow { DataContext = _viewModel };
            desktop.MainWindow = _window;
            desktop.ShutdownRequested += OnShutdownRequested;
            CreateTray();
            Instance?.ListenForShow(() => Dispatcher.UIThread.Post(() => ShowWindow()));

            _window.Show();
            _viewModel.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray()
    {
        var open = new NativeMenuItem(Strings.TrayOpen);
        open.Click += (_, _) => ShowWindow();
        var quit = new NativeMenuItem(Strings.TrayQuit);
        quit.Click += async (_, _) => await QuitAsync();
        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        using var icon = AssetLoader.Open(IconUri);
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(icon),
            ToolTipText = Strings.TrayTooltip,
            Menu = menu,
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private Window ShowWindow()
    {
        var window = _window!;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        return window;
    }

    /// <summary>"Thoát &amp; khôi phục": hoàn tác toàn bộ, báo nếu còn mục lỗi, rồi thoát. Thoát luôn hoàn tác Journal (spec 4.8).</summary>
    private async Task QuitAsync()
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) == 1) return;
        var result = await Task.Run(Services!.ShutdownAsync);
        if (!result.Success) await new DialogPrompts(() => ShowWindow()).InformAsync(Strings.AppTitle, Strings.RestoreFailed);
        CloseTray();
        _desktop!.Shutdown();
    }

    /// <summary>
    /// Đăng xuất/tắt máy (WM_QUERYENDSESSION): hoàn tác trong tối đa 4 giây (KH5 quyết định 14). Chưa chắc Avalonia 11.3 phát sự kiện
    /// này khi session kết thúc; T8 bước 6 đo bằng các dòng log ở đây. Không kịp thì lần mở sau hoặc --cleanup sẽ hoàn tác.
    /// </summary>
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) == 1) return;
        LogSessionEnding(_logger);
        var watch = Stopwatch.StartNew();
        try
        {
            var shutdown = Task.Run(Services!.ShutdownAsync);
            if (!shutdown.Wait(SessionEndBudget)) LogSessionEndTimedOut(_logger);
            else if (!shutdown.Result.Success) LogSessionEndLeftovers(_logger, shutdown.Result.Failed.Count);
            else LogSessionEndDone(_logger, watch.ElapsedMilliseconds);
        }
        catch (AggregateException ex)
        {
            LogSessionEndFailed(_logger, ex);
        }
        CloseTray();
    }

    public void Dispose() => CloseTray();

    private void CloseTray()
    {
        _viewModel?.Stop();
        if (_tray is null) return;
        _tray.IsVisible = false;
        _tray.Dispose();
        _tray = null;
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ranh giới cuối của luồng UI: log, báo chung chung, giữ app chạy để còn "Thoát & khôi phục".
        LogUnhandled(_logger, e.Exception);
        _viewModel?.ShowUnexpectedError();
        e.Handled = true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Nhận ShutdownRequested (đăng xuất/tắt máy), bắt đầu hoàn tác.")]
    private static partial void LogSessionEnding(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hoàn tác khi đăng xuất xong sau {ElapsedMs} ms.")]
    private static partial void LogSessionEndDone(ILogger logger, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hoàn tác khi đăng xuất quá 4 giây; phần còn lại để lần mở sau hoặc --cleanup.")]
    private static partial void LogSessionEndTimedOut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Đăng xuất: còn {Count} thay đổi chưa hoàn tác được.")]
    private static partial void LogSessionEndLeftovers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Đăng xuất: hoàn tác lỗi.")]
    private static partial void LogSessionEndFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lỗi không bắt trên luồng UI.")]
    private static partial void LogUnhandled(ILogger logger, Exception error);
}
```

`TrayIcon.SetIcons(this, [_tray])` dùng collection expression cho `TrayIcons`. Nếu C# 12 không suy ra được kiểu, viết `new TrayIcons { _tray }`.

- [ ] **Step 5: Viết ViewModel**

`src/LunaDns.App/ViewModels/ObservableObject.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LunaDns.App.ViewModels;

internal abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
```

`src/LunaDns.App/ViewModels/AsyncCommand.cs`:
```csharp
using System.Windows.Input;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Lệnh bất đồng bộ cho một nút bấm: không chạy chồng (CanExecute = false khi đang chạy).
/// Execute là ranh giới lỗi của nút: mọi exception được chuyển cho onError (log + thông báo), không bao giờ nuốt im.
/// </summary>
internal sealed class AsyncCommand(Func<Task> execute, Action<Exception> onError, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception ex)
        {
            onError(ex);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

`src/LunaDns.App/ViewModels/IUserPrompts.cs`:
```csharp
namespace LunaDns.App.ViewModels;

/// <summary>Hộp thoại cho VM, để VM không tham chiếu Window.</summary>
internal interface IUserPrompts
{
    Task<bool> ConfirmAsync(string title, string message);

    Task InformAsync(string title, string message);
}
```

`src/LunaDns.App/ViewModels/FeatureViewModel.cs`:
```csharp
using System.Globalization;
using LunaDns.Core.Features;

namespace LunaDns.App.ViewModels;

/// <summary>Một dòng công tắc (spec 4.8). IsOn chỉ đổi qua Update; ToggleSwitch bind OneWay.</summary>
internal sealed class FeatureViewModel : ObservableObject
{
    private bool _isOn;
    private string _statusText = Strings.StatusOff;

    public FeatureViewModel(Feature feature, Func<FeatureViewModel, Task> toggle, Action<Exception> onError)
    {
        Feature = feature;
        Title = Strings.FeatureTitle(feature);
        Description = Strings.FeatureDescription(feature);
        ToggleCommand = new AsyncCommand(() => toggle(this), onError);
    }

    public Feature Feature { get; }

    public string Title { get; }

    public string Description { get; }

    public AsyncCommand ToggleCommand { get; }

    public bool IsOn => _isOn;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Luôn phát IsOn, kể cả khi không đổi: ToggleSwitch đã tự lật lúc bấm, binding OneWay cần được đẩy lại giá trị thật.</summary>
    public void Update(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _isOn = Feature switch
        {
            Feature.DnsRouting => status.DnsRouting,
            Feature.SniProxy => status.SniProxy,
            _ => status.PubgMode,
        };
        OnPropertyChanged(nameof(IsOn));
        UpdateStatusText(status);
    }

    /// <summary>Gọi từ timer: chỉ đổi dòng trạng thái, không đụng IsOn.</summary>
    public void UpdateStatusText(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        StatusText = !_isOn ? Strings.StatusOff
            : Feature == Feature.SniProxy && status.ProxyPort is int port
                ? Strings.Format(nameof(Strings.StatusProxyFormat), port.ToString(CultureInfo.InvariantCulture), status.ProxyConnections)
                : Strings.StatusOn;
    }
}
```

`src/LunaDns.App/ViewModels/MainViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using Avalonia.Threading;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;
using LunaDns.Platform.Windows.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Màn hình chính (spec 4.8). Mọi lệnh gọi FeatureSet qua Task.Run vì Configurator chặn luồng.
/// FeatureStopped (luồng nền của watchdog) được chuyển về luồng UI bằng Dispatcher.UIThread.Post.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);

    private readonly AppServices _services;
    private readonly IUserPrompts _prompts;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private ProbeSummary? _summary;
    private string _probeStatus = Strings.NotCheckedYet;
    private string _message = string.Empty;
    private bool _canApplyRecommended;

    public MainViewModel(AppServices services, IUserPrompts prompts, Func<Task> quit)
    {
        _services = services;
        _prompts = prompts;
        _logger = services.LoggerFactory.CreateLogger<MainViewModel>();
        Features =
        [
            new FeatureViewModel(Feature.DnsRouting, ToggleAsync, OnError),
            new FeatureViewModel(Feature.SniProxy, ToggleAsync, OnError),
            new FeatureViewModel(Feature.PubgMode, ToggleAsync, OnError),
        ];
        CheckCommand = new AsyncCommand(CheckAsync, OnError);
        ApplyRecommendedCommand = new AsyncCommand(ApplyRecommendedAsync, OnError);
        DisableAllCommand = new AsyncCommand(DisableAllAsync, OnError);
        DiagnosticsCommand = new AsyncCommand(DiagnosticsAsync, OnError);
        QuitCommand = new AsyncCommand(quit, OnError);
        _services.Features.FeatureStopped += OnFeatureStopped;
        _timer = new DispatcherTimer(StatusInterval, DispatcherPriority.Background, (_, _) => RefreshStatusText());
        Refresh();
    }

    public IReadOnlyList<FeatureViewModel> Features { get; }

    public ObservableCollection<string> ProbeLines { get; } = [];

    public AsyncCommand CheckCommand { get; }

    public AsyncCommand ApplyRecommendedCommand { get; }

    public AsyncCommand DisableAllCommand { get; }

    public AsyncCommand DiagnosticsCommand { get; }

    public AsyncCommand QuitCommand { get; }

    public string ProbeStatus
    {
        get => _probeStatus;
        private set => SetProperty(ref _probeStatus, value);
    }

    public bool CanApplyRecommended
    {
        get => _canApplyRecommended;
        private set => SetProperty(ref _canApplyRecommended, value);
    }

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => _message.Length > 0;

    // Mục Nâng cao + ô "Kiểm tra cả PUBG": ghi settings.json ngay; dịch vụ đang chạy giữ cấu hình cũ tới lần bật sau.
    public bool CheckPubg
    {
        get => _services.Settings.CheckPubg;
        set => SaveSettings(s => s with { CheckPubg = value });
    }

    public int SplitModeIndex
    {
        get => _services.Settings.SplitMode == SplitMode.MidSni ? 1 : 0;
        set => SaveSettings(s => s with { SplitMode = value == 1 ? SplitMode.MidSni : SplitMode.FirstByte });
    }

    public decimal? SplitDelayMs
    {
        get => _services.Settings.SplitDelayMs;
        set => SaveSettings(s => s with { SplitDelayMs = (int)Math.Clamp(value ?? 0, 0, AppSettings.MaxSplitDelayMs) });
    }

    public int DohIndex
    {
        get => _services.Settings.PreferGoogleDoh ? 1 : 0;
        set => SaveSettings(s => s with { PreferGoogleDoh = value == 1 });
    }

    /// <summary>Gọi sau khi cửa sổ hiện: báo kết quả hoàn tác lúc khởi động, bật timer, tự chạy "Kiểm tra" (spec 5).</summary>
    public void Start()
    {
        var startup = _services.StartupRollback;
        if (!startup.Success) Message = Strings.StartupRestoreFailed;
        else if (startup.Reverted > 0) Message = Strings.Format(nameof(Strings.StartupRestoredFormat), startup.Reverted);
        _timer.Start();
        CheckCommand.Execute(null);
    }

    public void ShowUnexpectedError()
    {
        Message = Strings.UnexpectedError;
        Refresh();
    }

    /// <summary>Gọi khi thoát: dừng timer, bỏ đăng ký FeatureStopped.</summary>
    public void Stop()
    {
        _timer.Stop();
        _services.Features.FeatureStopped -= OnFeatureStopped;
    }

    private async Task ToggleAsync(FeatureViewModel item)
    {
        try
        {
            var feature = item.Feature;
            FeatureResult result;
            if (_services.Features.IsEnabled(feature))
            {
                result = await Task.Run(() => _services.Features.DisableAsync(feature));
            }
            else
            {
                if (feature == Feature.PubgMode && !await AcceptPubgWarningAsync()) return;
                result = await EnableAsync(feature);
            }
            Message = ErrorMessages.For(result) ?? string.Empty;
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>NeedsProxyOverride: hỏi người dùng rồi bật lại với allowProxyOverride. Bấm Huỷ thì coi như Unchanged.</summary>
    private async Task<FeatureResult> EnableAsync(Feature feature)
    {
        var result = await Task.Run(() => _services.Features.EnableAsync(feature));
        if (result.Outcome != FeatureOutcome.NeedsProxyOverride) return result;
        if (!await _prompts.ConfirmAsync(Strings.ProxyOverrideTitle, Strings.ProxyOverrideText))
            return result with { Outcome = FeatureOutcome.Unchanged };
        return await Task.Run(() => _services.Features.EnableAsync(feature, allowProxyOverride: true));
    }

    /// <summary>Cảnh báo spec 4.4 chỉ ở lần bật đầu; đồng ý thì lưu PubgWarningAccepted (KH5 quyết định 5).</summary>
    private async Task<bool> AcceptPubgWarningAsync()
    {
        if (_services.Settings.PubgWarningAccepted) return true;
        if (!await _prompts.ConfirmAsync(Strings.PubgWarningTitle, Strings.PubgWarningText)) return false;
        SaveSettings(s => s with { PubgWarningAccepted = true });
        return true;
    }

    private async Task CheckAsync()
    {
        ProbeStatus = Strings.CheckingStatus;
        ProbeLines.Clear();
        CanApplyRecommended = false;
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        try
        {
            _summary = await Task.Run(() => _services.ProbeAsync(timeout.Token));
        }
        catch (OperationCanceledException)
        {
            ProbeStatus = Strings.CheckFailed;
            return;
        }
        ShowSummary();
    }

    private void ShowSummary()
    {
        if (_summary is null) return;
        var status = _services.Features.GetStatus();
        ProbeStatus = Strings.Format(nameof(Strings.CheckedAtFormat), _summary.CheckedAt);
        ProbeLines.Clear();
        foreach (var group in _summary.Groups)
            ProbeLines.Add(Strings.Format(nameof(Strings.GroupLineFormat), Strings.GroupName(group.Group), Strings.Verdict(group.Worst)));
        if (_summary.Groups.All(g => g.Worst == ProbeVerdict.Ok)) ProbeLines.Add(Strings.AllOk);
        if (_summary.SuggestPubgMode(status)) ProbeLines.Add(Strings.SuggestPubgMode);
        if (_summary.AnyUnreachable) ProbeLines.Add(Strings.UnreachableHint);
        CanApplyRecommended = _summary.PendingSteamRemedies(status) != ProbeRemedies.None;
    }

    /// <summary>Chỉ bật biện pháp cho Steam; PUBG chỉ gợi ý (KH5 quyết định 5). DNS trước, proxy sau.</summary>
    private async Task ApplyRecommendedAsync()
    {
        if (_summary is null) return;
        try
        {
            var pending = _summary.PendingSteamRemedies(_services.Features.GetStatus());
            if (pending == ProbeRemedies.None)
            {
                Message = Strings.NothingToApply;
                return;
            }
            var messages = new List<string?>();
            if (pending.HasFlag(ProbeRemedies.DnsRouting)) messages.Add(ErrorMessages.For(await EnableAsync(Feature.DnsRouting)));
            if (pending.HasFlag(ProbeRemedies.SniProxy)) messages.Add(ErrorMessages.For(await EnableAsync(Feature.SniProxy)));
            Message = string.Join(" ", messages.OfType<string>());
        }
        finally
        {
            Refresh();
            ShowSummary();
        }
    }

    /// <summary>"Tắt hết &amp; khôi phục" (spec 4.8): tắt mọi tính năng, app vẫn chạy.</summary>
    private async Task DisableAllAsync()
    {
        try
        {
            var result = await Task.Run(_services.Features.DisableAllAsync);
            Message = result.Success ? Strings.DisableAllDone : Strings.RestoreFailed;
        }
        finally
        {
            Refresh();
            ShowSummary();
        }
    }

    private async Task DiagnosticsAsync()
    {
        Message = Strings.DiagnosticsRunning;
        try
        {
            var path = await Task.Run(() => ReportWriter.Write(_services.Paths, _services.LastProbe, WindowsDiagnostics.CreateDefault()));
            Message = Strings.Format(nameof(Strings.ReportSavedFormat), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReportFailed(_logger, ex);
            Message = Strings.Format(nameof(Strings.ReportFailedFormat), ex.Message);
        }
    }

    private void SaveSettings(Func<AppSettings, AppSettings> change)
    {
        try
        {
            _services.Settings = change(_services.Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Setter của binding: Avalonia nuốt exception, nên ranh giới phải nằm ở đây.
            LogSettingsSaveFailed(_logger, ex);
            Message = Strings.UnexpectedError;
        }
        OnPropertyChanged(nameof(CheckPubg));
        OnPropertyChanged(nameof(SplitModeIndex));
        OnPropertyChanged(nameof(SplitDelayMs));
        OnPropertyChanged(nameof(DohIndex));
    }

    private void OnFeatureStopped(object? sender, FeatureStoppedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            Message = ErrorMessages.Stopped(e);
            Refresh();
        });

    private void OnError(Exception ex)
    {
        LogCommandFailed(_logger, ex);
        ShowUnexpectedError();
    }

    private void Refresh()
    {
        var status = _services.Features.GetStatus();
        foreach (var feature in Features) feature.Update(status);
    }

    private void RefreshStatusText()
    {
        var status = _services.Features.GetStatus();
        foreach (var feature in Features) feature.UpdateStatusText(status);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Lệnh trên giao diện lỗi.")]
    private static partial void LogCommandFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Không ghi được báo cáo chẩn đoán.")]
    private static partial void LogReportFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Không lưu được settings.json.")]
    private static partial void LogSettingsSaveFailed(ILogger logger, Exception error);
}
```

- [ ] **Step 6: Viết `MainWindow` và `ConfirmDialog`**

`src/LunaDns.App/Views/MainWindow.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:sys="using:System"
        xmlns:app="using:LunaDns.App"
        xmlns:vm="using:LunaDns.App.ViewModels"
        x:Class="LunaDns.App.Views.MainWindow"
        x:ClassModifier="internal"
        x:DataType="vm:MainViewModel"
        Title="{x:Static app:Strings.AppTitle}"
        Icon="/Assets/lunadns.ico"
        Width="560" Height="720" MinWidth="460" MinHeight="520"
        WindowStartupLocation="CenterScreen">
  <ScrollViewer>
    <StackPanel Margin="20" Spacing="16">

      <StackPanel Spacing="4">
        <TextBlock Text="{x:Static app:Strings.AppTitle}" FontSize="24" FontWeight="SemiBold" />
        <TextBlock Text="{x:Static app:Strings.HeaderSubtitle}" TextWrapping="Wrap" Opacity="0.8" />
      </StackPanel>

      <!-- Kiểm tra + kết quả theo nhóm + Áp dụng đề xuất (spec 4.8) -->
      <Border Padding="16" CornerRadius="8" BorderThickness="1" BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}">
        <StackPanel Spacing="8">
          <TextBlock Text="{Binding ProbeStatus}" TextWrapping="Wrap" />
          <ItemsControl ItemsSource="{Binding ProbeLines}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="sys:String">
                <TextBlock Text="{Binding}" TextWrapping="Wrap" />
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
          <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Classes="accent" Content="{x:Static app:Strings.CheckButton}" Command="{Binding CheckCommand}" />
            <Button Content="{x:Static app:Strings.ApplyRecommendedButton}" Command="{Binding ApplyRecommendedCommand}"
                    IsVisible="{Binding CanApplyRecommended}" />
            <CheckBox Content="{x:Static app:Strings.CheckPubgLabel}" IsChecked="{Binding CheckPubg}" />
          </StackPanel>
        </StackPanel>
      </Border>

      <!-- Ba công tắc, mỗi cái có một dòng giải thích và dòng trạng thái -->
      <ItemsControl ItemsSource="{Binding Features}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="vm:FeatureViewModel">
            <Border Margin="0,0,0,8" Padding="16" CornerRadius="8" BorderThickness="1"
                    BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}">
              <Grid ColumnDefinitions="*,Auto">
                <StackPanel Spacing="4">
                  <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
                  <TextBlock Text="{Binding Description}" TextWrapping="Wrap" Opacity="0.8" />
                  <TextBlock Text="{Binding StatusText}" Opacity="0.8" />
                </StackPanel>
                <ToggleSwitch Grid.Column="1" VerticalAlignment="Center" OnContent="" OffContent=""
                              IsChecked="{Binding IsOn, Mode=OneWay}" Command="{Binding ToggleCommand}" />
              </Grid>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <TextBlock Text="{Binding Message}" TextWrapping="Wrap" IsVisible="{Binding HasMessage}" />

      <Button HorizontalAlignment="Stretch" HorizontalContentAlignment="Center"
              Content="{x:Static app:Strings.DisableAllButton}" Command="{Binding DisableAllCommand}" />

      <Expander Header="{x:Static app:Strings.AdvancedHeader}" HorizontalAlignment="Stretch">
        <StackPanel Spacing="8">
          <TextBlock Text="{x:Static app:Strings.SplitModeLabel}" />
          <ComboBox SelectedIndex="{Binding SplitModeIndex}" HorizontalAlignment="Stretch">
            <ComboBoxItem Content="{x:Static app:Strings.SplitModeFirstByte}" />
            <ComboBoxItem Content="{x:Static app:Strings.SplitModeMidSni}" />
          </ComboBox>
          <TextBlock Text="{x:Static app:Strings.SplitDelayLabel}" />
          <NumericUpDown Value="{Binding SplitDelayMs}" Minimum="0" Maximum="20" Increment="1" FormatString="0" />
          <TextBlock Text="{x:Static app:Strings.DohLabel}" />
          <ComboBox SelectedIndex="{Binding DohIndex}" HorizontalAlignment="Stretch">
            <ComboBoxItem Content="{x:Static app:Strings.DohCloudflareFirst}" />
            <ComboBoxItem Content="{x:Static app:Strings.DohGoogleFirst}" />
          </ComboBox>
          <TextBlock Text="{x:Static app:Strings.AdvancedHint}" TextWrapping="Wrap" Opacity="0.8" />
        </StackPanel>
      </Expander>

      <Expander Header="{x:Static app:Strings.SettingsHeader}" HorizontalAlignment="Stretch">
        <Button Content="{x:Static app:Strings.DiagnosticsButton}" Command="{Binding DiagnosticsCommand}" />
      </Expander>

    </StackPanel>
  </ScrollViewer>
</Window>
```

`QuitCommand` của VM không gắn nút nào trong cửa sổ, vì spec 4.8 chỉ đặt "Thoát & khôi phục" ở menu khay. Lệnh này để dành nếu PO muốn thêm nút ở T8.

`src/LunaDns.App/Views/MainWindow.axaml.cs`:
```csharp
using Avalonia.Controls;

namespace LunaDns.App.Views;

internal sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Nút X chỉ thu xuống khay (spec 4.8). Thoát thật là menu khay hoặc đăng xuất (CloseReason khác WindowClosing).</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
```

`src/LunaDns.App/Views/ConfirmDialog.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Views;

/// <summary>Hộp thoại xác nhận/thông báo dựng bằng code (không cần thêm XAML). Đóng bằng X thì trả false.</summary>
internal sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText, string? cancelText)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var confirm = new Button { Content = confirmText, IsDefault = true };
        confirm.Classes.Add("accent");
        confirm.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(confirm);
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        var root = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(buttons);
        Content = root;
    }
}

/// <summary>Hiện cửa sổ chính trước (có thể đang ở khay) để làm owner cho hộp thoại modal.</summary>
internal sealed class DialogPrompts(Func<Window> showOwner) : IUserPrompts
{
    public Task<bool> ConfirmAsync(string title, string message) =>
        new ConfirmDialog(title, message, Strings.ConfirmYes, Strings.ConfirmNo).ShowDialog<bool>(showOwner());

    public Task InformAsync(string title, string message) =>
        new ConfirmDialog(title, message, Strings.ConfirmYes, cancelText: null).ShowDialog<bool>(showOwner());
}
```

- [ ] **Step 7: Build và chạy lại test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests; dotnet test tests/LunaDns.Platform.Windows.Tests; dotnet test tests/LunaDns.App.Tests`
Expected: build 0 cảnh báo; Core **356 + 4 skipped**, Platform **37 + 4 skipped**, App **29** (không đổi so với Task 6).

Run: `Test-Path src/LunaDns.App/bin/Debug/net8.0-windows/LunaDNS.exe`
Expected: `True`. Agent **không** chạy exe: exe cần UAC và sẽ sửa cấu hình mạng thật. Việc chạy thử là của Task 8.

- [ ] **Step 8: Tự kiểm, không commit**

Run: `git status --short src/LunaDns.App`
Expected: chỉ có `LunaDns.App.csproj` bị sửa và các file mới của Task 7 (có cả `Assets/lunadns.ico`). Không `git add`.

---

### Task 8: PO chạy tay bằng quyền Admin (giao diện, khay, `--cleanup`, `--report`, đăng xuất)

**Ai làm:** PO, trong PowerShell **Admin**, trên máy thật có Steam (và PUBG nếu có). Agent không làm task này. Phiên chính ghi kết quả vào mục "Kết quả Task 8" ở cuối file này.

**Chuẩn bị:** đã xong Task 1–7, ba bộ test xanh. Đóng Steam. Exe nằm ở `src/LunaDns.App/bin/Debug/net8.0-windows/LunaDNS.exe`.

- [ ] **Step 1: Chụp trạng thái trước**

```powershell
$exe = (Resolve-Path src/LunaDns.App/bin/Debug/net8.0-windows/LunaDNS.exe).Path
$snap = "$env:TEMP\lunadns-kh5"; New-Item -ItemType Directory -Force $snap | Out-Null
function Save-State($name) {
  Get-DnsClientNrptRule | Sort-Object Name | Format-Table Name, Namespace, NameServers, Comment -AutoSize | Out-String -Width 300 | Set-Content "$snap\$name-nrpt.txt"
  Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' | Select-Object AutoConfigURL, ProxyEnable | Format-List | Out-String | Set-Content "$snap\$name-proxy.txt"
  (Get-FileHash "$env:SystemRoot\System32\drivers\etc\hosts").Hash | Set-Content "$snap\$name-hosts.txt"
}
function Compare-State($name) {
  foreach ($part in 'nrpt', 'proxy', 'hosts') {
    $same = (Get-Content "$snap\before-$part.txt" -Raw) -eq (Get-Content "$snap\$name-$part.txt" -Raw)
    "{0,-6} {1}" -f $part, $(if ($same) { 'giống trước' } else { 'KHÁC' })
  }
}
Save-State before
```
Expected: có 3 file `before-*.txt`. Luật Tailscale (nếu có) nằm trong `before-nrpt.txt`.

- [ ] **Step 2: Giao diện và ba công tắc**

1. Chạy `& $exe`. Expected: cửa sổ tiếng Việt có dấu hiện lên, có biểu tượng LunaDNS trên khay, và **tự chạy "Kiểm tra"**. Trên mạng PO, Steam cho kết quả `DNS bị chặn` hoặc `Bị lọc SNI` (KH3 T10), nên nút "Áp dụng đề xuất" hiện.
2. Bấm "Áp dụng đề xuất". Expected: Định tuyến DNS và Proxy SNI bật. Dòng trạng thái của proxy ghi `cổng <N> · <k> kết nối`.
   - Nếu máy có PAC hoặc proxy thủ công, hộp thoại "Thay cấu hình proxy hiện có?" hiện ra. Bấm Huỷ thì công tắc quay về tắt và không có thông báo lỗi.
   ```powershell
   Get-DnsClientNrptRule | Where-Object Comment -like 'LunaDNS *' | Format-Table Namespace, NameServers, Comment
   Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' AutoConfigURL
   ```
   Expected: luật NRPT có `Comment` `LunaDNS …` trỏ `127.0.0.1`. `AutoConfigURL` là `http://127.0.0.1:<N>/proxy.pac`. Luật Tailscale không đổi.
3. Mở Steam: đăng nhập được, Store và Community tải được. Bấm "Kiểm tra" lại. Expected: Steam `Bình thường`, và "Áp dụng đề xuất" ẩn đi.
4. Bật **Chế độ PUBG**. Expected: lần đầu hiện cảnh báo đúng câu của spec 4.4. Bấm Huỷ thì công tắc vẫn tắt. Bật lại và bấm Đồng ý: hosts có dòng `127.0.0.1 prod-live-front.playbattlegrounds.com # LunaDNS`, và `Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 443 -State Listen` có kết quả. Tắt rồi bật lại thì **không** hỏi lại. Nếu có PUBG: vào được lobby.
5. Mục Nâng cao: đổi điểm cắt sang "Giữa tên miền", rồi tắt và bật lại Proxy SNI. Expected: `%ProgramData%\LunaDNS\settings.json` có `"SplitMode": "MidSni"`, và Store vẫn tải được. Sau đó trả về mặc định.
6. Bấm "Tắt hết & khôi phục". Expected: ba công tắc đều tắt, app vẫn chạy.
   ```powershell
   Save-State afteroff; Compare-State afteroff
   ```
   Expected: cả ba dòng `giống trước`.
7. Khay và một phiên duy nhất: bấm X thì cửa sổ ẩn, khay còn. Chạy `& $exe` lần nữa thì cửa sổ cũ hiện lên, **không** có biểu tượng khay thứ hai. Bấm trái vào khay, hoặc chọn menu "Mở LunaDNS", cũng hiện cửa sổ.
8. Bật lại Định tuyến DNS, rồi chọn menu khay "Thoát & khôi phục". Expected: tiến trình kết thúc, `Save-State quit; Compare-State quit` cho cả ba dòng `giống trước`.

- [ ] **Step 3: Chết đột ngột rồi `--cleanup`**

```powershell
& $exe   # bật cả ba công tắc trong giao diện rồi quay lại đây
Stop-Process -Name LunaDNS -Force
Save-State killed; Compare-State killed          # Expected: cả ba KHÁC (thay đổi còn nằm lại)
$p = Start-Process $exe -ArgumentList '--cleanup' -Wait -PassThru; $p.ExitCode   # Expected: 0
Save-State cleaned; Compare-State cleaned        # Expected: cả ba "giống trước"
$p = Start-Process $exe -ArgumentList '--cleanup' -Wait -PassThru; $p.ExitCode   # chạy lần 2, Expected: 0, không đổi gì
Get-Content "$env:ProgramData\LunaDNS\logs\lunadns.log" -Tail 5
```
Expected: log có `--cleanup: đã hoàn tác 3 mục, còn 0 mục lỗi.` ở lần đầu, và `journal trống` ở lần hai.

Kiểm thêm: mở app, bật Định tuyến DNS, rồi chạy `--cleanup` trong lúc app vẫn chạy. Expected: mã thoát 0, log ghi `LunaDNS đang chạy, bỏ qua`, luật NRPT vẫn còn. Thoát app bằng khay.

Kiểm việc hoàn tác lúc mở lại: bật một công tắc, `Stop-Process -Force`, rồi mở `& $exe`. Expected: thông báo "Đã hoàn tác 1 thay đổi còn sót từ lần chạy trước.", và mọi công tắc ở trạng thái tắt.

- [ ] **Step 4: `--report`**

```powershell
& $exe   # bật Định tuyến DNS + Proxy SNI, để app chạy
$p = Start-Process $exe -ArgumentList '--report' -Wait -PassThru; $p.ExitCode   # Expected: 0, xong trong khoảng 20 giây
$r = Get-ChildItem ([Environment]::GetFolderPath('Desktop')) -Filter 'LunaDNS-report-*.txt' | Sort-Object LastWriteTime | Select-Object -Last 1
Select-String -Path $r.FullName -Pattern $env:USERNAME, $env:COMPUTERNAME -SimpleMatch
Get-DnsClientNrptRule | Where-Object Comment -like 'LunaDNS *' | Measure-Object | Select-Object Count
```
Expected:
- Lệnh `Select-String` không in dòng nào, tức là không lộ tên user hay tên máy.
- Báo cáo có đủ các mục: kết quả kiểm tra, Journal (2 mục, SID đã thành `<sid>`), luật NRPT của LunaDNS, `AutoConfigURL` loopback, hosts, log. Notepad hiện đúng tiếng Việt.
- **Sau khi chạy `--report`, luật NRPT vẫn còn (Count ≥ 1) và giao diện vẫn bật.** Điều này chứng minh `--report` không hoàn tác journal của app đang chạy.

Trong giao diện, bấm Cài đặt → "Chẩn đoán". Expected: thông báo "Đã lưu báo cáo: …", và có thêm một file trên Desktop. Thoát app bằng khay.

- [ ] **Step 5: (tuỳ chọn) Windows tiếng Anh**

Nếu có máy hoặc tài khoản dùng giao diện tiếng Anh: chạy app và kiểm chuỗi vẫn là tiếng Việt (KH5 quyết định 7). Bỏ qua được, vì `StringsTests.English_ui_culture_still_gets_vietnamese` đã kiểm.

- [ ] **Step 6: Đăng xuất (đo `ShutdownRequested`, KH5 quyết định 14)**

1. Mở app, bật Định tuyến DNS và Proxy SNI. Ghi lại giờ. Đăng xuất Windows (Start → Sign out). **Không** tắt máy.
2. Đăng nhập lại, **chưa** mở LunaDNS:
   ```powershell
   Save-State logoff; Compare-State logoff
   Select-String -Path "$env:ProgramData\LunaDNS\logs\lunadns.log*" -Pattern 'ShutdownRequested|đăng xuất' | Select-Object -Last 5
   ```
3. Ghi một trong ba kết quả:
   - **A (đạt):** cả ba dòng `giống trước`, log có `Hoàn tác khi đăng xuất xong sau <ms> ms`. Ghi số ms.
   - **B (quá giờ):** log có dòng `quá 4 giây`, hoặc còn dòng `KHÁC`. Chạy `--cleanup` để dọn, rồi ghi số liệu.
   - **C (không có sự kiện):** log không có `Nhận ShutdownRequested`, còn `KHÁC`. Chạy `--cleanup` để dọn. Avalonia 11.3 không phát sự kiện khi session kết thúc, nên phải xử lý `WM_QUERYENDSESSION` qua hook Win32. Ghi vào "Việc kế tiếp" của KH4b/KH6; **không** sửa trong KH5.

- [ ] **Step 7: Ghi kết quả**

Phiên chính thêm mục `## Kết quả Task 8 (<ngày>)` vào cuối file plan này. Mục này gồm: kết quả từng bước (đạt/không, kèm số liệu); kết quả A/B/C của bước 6; các lỗi giao diện đã thấy. Mỗi lỗi giao diện đi kèm task nhỏ để sửa sau. **Không** sửa code trong lúc ghi kết quả. Cập nhật memory `lunadns-plan5-2026-11-13.md`.

---

## Mục spec → task

| Mục spec | Nội dung | Task KH5 | Ghi chú |
|---|---|---|---|
| 3 | `App → Core + Platform.Windows`, Core không dùng API riêng của Windows | 2, 4, 6 | `WindowsDiagnostics` nằm ở Platform (T5) |
| 4.2 | Định tuyến DNS chỉ cho domain game | 2, 3 | Chỉ nhóm Steam (quyết định 1) |
| 4.3 | Proxy SNI | 1, 2, 3 | Watchdog mở lại đúng cổng cũ (quyết định 2) |
| 4.4 | Chế độ PUBG: dòng hosts + listener `127.0.0.1:443`, SNI lạ thì đóng, cảnh báo lần đầu | 1, 2, 3, 7 | Một host (quyết định 3). Ngoại lệ "chuyển hướng anti-cheat khi probe thấy bị chặn" **chưa làm** |
| 4.5 | Journal ghi trước, hoàn tác LIFO, mở lại thì hoàn tác, `--cleanup` | 3, 6, 7 | `--cleanup` không dọn luật NRPT mồ côi (quyết định 10) |
| 4.6 | ACL tường minh cho `%ProgramData%\LunaDNS` | — | KH4b (quyết định 12). Chưa có thì không phát hành |
| 4.7 | Task dọn dẹp lúc khởi động máy | — | KH4b. Task đó gọi `LunaDNS.exe --cleanup` của KH5 |
| 4.8 | Màn hình chính, công tắc, trạng thái, "Tắt hết & khôi phục", khay, Nâng cao, Cài đặt, `requireAdministrator`, resx | 6, 7 | Cài đặt chỉ có Chẩn đoán (quyết định 11). Resx trung tính thay cho `.vi.resx` (quyết định 7) |
| 4.9 | `--report` / nút Chẩn đoán, che user/máy/IP LAN | 4, 5, 6, 7 | Che thêm SID, CGNAT, PAC lạ (quyết định 9) |
| 4.10 | Kiểm tra cập nhật | — | KH6 |
| 5 | Luồng chính: mutex → hoàn tác còn sót → tự kiểm tra → bật → khay → thoát hoàn tác → đăng xuất | 6, 7, 8 | Bước "kiểm tra ACL" thuộc KH4b. Đăng xuất được đo ở T8 bước 6 (quyết định 14) |
| 6 | Mã lỗi → thông báo tiếng Việt, không `catch { }` rỗng, log xoay vòng 1 MB × 5, watchdog 3 lần/phút | 2, 3, 4, 6 | `ErrorMessages` + `Strings.Error_*` |
| 9 | Kiểm thử | 1–8 | Core +88, Platform +8, App 29; giao diện kiểm tay (quyết định 13) |

## Việc không thuộc KH5

- **KH4b:** ACL `%ProgramData%\LunaDNS` (4.6); task dọn dẹp lúc khởi động máy chạy bằng SYSTEM và gọi `--cleanup` (4.7); nút "Gỡ khỏi máy"; kiểm ACL trong luồng chính (5). Nếu T8 bước 6 ra kết quả C thì thêm việc hook `WM_QUERYENDSESSION`.
- **KH6:** kiểm tra cập nhật (4.10); publish trimmed hoặc single-file và kiểm cảnh báo trim của Journal (quyết định 16); chặn phát hành khi KH4b chưa xong (quyết định 12); ký số exe.
- **Cần PO đo bằng game thật rồi mới thêm:** host lobby/entry PUBG khác ngoài `prod-live-front` (quyết định 3); chuyển hướng anti-cheat khi probe thấy bị chặn (4.4); bật cho nhóm `Other` (Naraka, Delta Force) (quyết định 1).
- **Chưa làm:** watchdog cho NRPT (đã đo ở KH4 T7 là không cần); dọn luật NRPT mồ côi; Avalonia.Headless và test cho ViewModel; dịch sang tiếng Anh (`Strings.en.resx`).

## Kết quả Task 8 (2026-11-13)

PO chạy tay bằng PowerShell Admin (Windows PowerShell 5.1), app Debug `0.5.0.0`. Máy có 2 luật NRPT Tailscale, không có PAC hoặc proxy thủ công.

| Bước | Kết quả | Ghi chú |
|---|---|---|
| 1 Chụp trạng thái | Đạt | `before-*` có 2 luật Tailscale; `AutoConfigURL` trống, `ProxyEnable 0` |
| 2.1 Mở app | Đạt | Tiếng Việt đủ dấu; tự Kiểm tra lúc mở: `Steam: Bị lọc SNI`; có nút "Áp dụng đề xuất"; một biểu tượng khay |
| 2.2 Áp dụng đề xuất | Đạt | DNS + Proxy bật, `cổng 18080`; 1 luật `LunaDNS …` gồm 22 namespace Steam → `127.0.0.1`; `AutoConfigURL` = `http://127.0.0.1:18080/proxy.pac`; luật Tailscale còn nguyên (tổng 3). Hộp "Thay cấu hình proxy hiện có?" không kiểm được (máy không có proxy sẵn) |
| 2.3 Steam qua LunaDNS | Đạt (lệch Expected) | Đăng nhập, Store và Community tải được, 33 kết nối. Kiểm tra lại vẫn `Bị lọc SNI`, không phải `Bình thường`: Prober đo đường mạng gốc, theo thiết kế. Plan viết sai Expected → UX-1 |
| 2.4 Chế độ PUBG | Bỏ qua (PO) | PO chưa có nhu cầu. Chỉ bật một lần ở Step 3, không vào game |
| 2.5 Nâng cao, MidSni | Không đạt (do mạng) | `settings.json` ghi `"SplitMode": "MidSni"` đúng; nhưng Store **màn đen**, và Kiểm tra ra `Không vào được` (`ProbeRunner` dùng `current.SplitMode` cho `TlsProbe`). Trả về FirstByte thì tải lại được, Kiểm tra về `Bị lọc SNI`. Khớp với KH3 T10 (chỉ FirstByte qua được) → UX-2 |
| 2.6 Tắt hết & khôi phục | Đạt | Ba công tắc tắt, có thông báo "Đã tắt hết và trả lại cấu hình mạng như trước.", ba dòng `giống trước` |
| 2.7 Khay, một phiên | Đạt | Chạy lần hai: vẫn 1 tiến trình, 1 biểu tượng khay; bấm trái và "Mở LunaDNS" đều mở cửa sổ |
| 2.8 Thoát & khôi phục | Đạt | Tiến trình kết thúc, ba dòng `giống trước` |
| 3a Kill + `--cleanup` | Đạt | `killed` ba dòng KHÁC (có hosts); cleanup lần 1 mã 0, log "đã hoàn tác 3 mục, còn 0 mục lỗi."; `cleaned` giống trước; lần 2 mã 0, "journal trống" |
| 3b `--cleanup` khi app chạy | Đạt | Mã 0, log "LunaDNS đang chạy, bỏ qua.", luật NRPT còn (Count 1) |
| 3c Hoàn tác lúc mở lại | Đạt | Log `AppServices: Đã hoàn tác 1 thay đổi còn sót từ lần chạy trước.`; NRPT chỉ còn 2 luật Tailscale cùng GUID |
| 4 `--report` | Đạt | Mã 0; không lộ user/máy/SID (grep `Users\`, `S-1-5-`, tên user = 0); đủ các mục, Journal 2 mục `<sid>`; luật NRPT vẫn còn sau report; nút Chẩn đoán tạo file; Notepad hiện đúng |
| 5 Windows tiếng Anh | Bỏ qua | Đã có `StringsTests.English_ui_culture_still_gets_vietnamese` |
| 6 Đăng xuất | Bỏ qua (PO) | Chưa đo, nên chưa biết A/B/C; quyết định 14 giữ nguyên, việc đo chuyển sang KH4b |

**Xem lại quyết định sau T8 (13/11):**
- **Quyết định 3, PO chốt:** hiện LunaDNS **không cần hỗ trợ PUBG**. PUBG ra khỏi phạm vi v1. Task sửa sau: ẩn hoặc gỡ công tắc Chế độ PUBG, ô "Kiểm tra cả PUBG" và chữ "PUBG" trong tiêu đề phụ; gỡ phần PUBG khỏi README và `docs/guide`. Code `FeatureDomains.PubgHosts` và Prober nhóm PUBG có thể giữ lại để dùng về sau.
- **Quyết định 14:** giữ code hiện tại. Lần mở app sau và `--cleanup` là lưới an toàn. Việc đo đăng xuất chuyển sang KH4b, kèm hook `WM_QUERYENDSESSION` nếu ra kết quả C. (Phiên chính đề xuất, PO chưa phản đối.)

Lỗi và quan sát (mỗi mục là một task nhỏ, sửa sau, **không** sửa trong KH5):
- **UX-1:** khi biện pháp đang bật, dòng `Steam: Bị lọc SNI` làm người dùng tưởng chưa sửa được. Task: khi `PendingSteamRemedies` rỗng mà verdict khác Ok, thêm hậu tố "· đã khắc phục bằng Định tuyến DNS + Proxy SNI" (chuỗi resx mới + `MainViewModel` dòng probe). Cũng sửa Expected của T8 2.3 trong plan.
- **UX-2:** chọn "Giữa tên miền" trên mạng PO làm Store đen mà app không báo gì. Task: ghi chú dưới ô chọn ("có thể không qua được một số nhà mạng"), hoặc cho Kiểm tra thử cả hai điểm cắt rồi gợi ý điểm cắt qua được. Giữ FirstByte làm mặc định.
- **UX-3:** "Áp dụng đề xuất" ẩn ngay khi bấm, không đợi Kiểm tra lại. Chấp nhận được, chỉ ghi lại.
- **OBS-1:** log UTF-8 không BOM, nên `Get-Content` của PowerShell 5.1 hiện chữ vỡ (phải thêm `-Encoding UTF8`). Task: cân nhắc ghi BOM khi tạo file log.
- **OBS-2:** Prober ghi nguyên stack trace cho mỗi lần bắt tay bị lọc SNI, nên trong 200 dòng log cuối của báo cáo có 99 dòng stack. Task: ghi lỗi probe trên một dòng (kiểu + thông điệp của exception), stack chỉ ở mức Debug.
- **OBS-3:** mỗi lần Chẩn đoán thêm một file ~30 KB trên Desktop và không tự dọn. Log có giới hạn 5 × 1 MB, báo cáo cố định 200 dòng, nên không nặng dần. **PO chốt (13/11):** báo cáo lưu vào thư mục con của thư mục LunaDNS (đề xuất `%ProgramData%\LunaDNS\reports\`), không lưu ra Desktop nữa, và có giới hạn kích thước. Đề xuất: giữ 10 báo cáo gần nhất; mỗi báo cáo giới hạn khoảng 64 KB (cắt bớt phần log nếu vượt); thông báo "Đã lưu báo cáo: …" có thêm nút mở thư mục. Task: đổi đích lưu trong `--report` và nút Chẩn đoán, thêm phần dọn file cũ cùng test, rồi sửa Expected Step 4 của T8.
- **OBS-4:** dòng log `SplitProxy: Từ chối CONNECT: host ngoài danh sách…` không ghi host bị từ chối nên khó chẩn đoán. Memory KH3 có ghi "guard log host để KH5"; cần xem lại việc đó đã làm chưa.
- **Ghi chú thử nghiệm:** `Compare-State` phụ thuộc định dạng của PowerShell 5.1; chạy bằng pwsh 7 sẽ báo KHÁC nhầm. Dán cả khối có `& $exe` thì lệnh sau chạy ngay, vì exe GUI không chặn.
