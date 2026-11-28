# B8 + F5: Thoát khi dịch vụ nền không trả lời, báo watchdog dừng trên macOS (kế hoạch)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Thoát & khôi phục" trên macOS không còn đóng app êm khi dịch vụ nền không trả lời (B8), và app trên macOS báo "Đã tự tắt vì lỗi" khi watchdog của dịch vụ nền dừng một tính năng (F5).

**Architecture:** B8 nằm gọn trong `DaemonFeatureController.ShutdownAsync` (gửi `disable-all` cả khi lần poll gần nhất lỗi, thử lại 1 lần, trả -1 khi không biết) và `App.QuitAsync` (hộp thoại mới). F5: daemon ghi lần dừng gần nhất vào lớp `WatchdogStops` và gửi qua trường tuỳ chọn `DaemonStatus.Stopped`; app so số thứ tự với mốc và phát `FeatureStopped` thật, `MainViewModel` giữ nguyên.

**Tech Stack:** .NET 8, C#, Avalonia 11.3, xUnit, System.Text.Json source-gen.

Spec: `docs/superpowers/specs/2026-11-28-lunadns-quit-and-watchdog-design.md`.

## Global Constraints

- Không thêm `Co-Authored-By` hay nhắc Claude trong commit; tác giả chỉ Luna Vo.
- `ShutdownOutcome.Unknown` = `-1`; `LocalFeatureController` không bao giờ trả -1.
- Thử lại khi thoát: đúng 1 lần, sau 1 giây; chỉ với `ShutdownReason.UserQuit`. `SessionEnd` gửi đúng 1 lần.
- `DaemonNotInstalled` và `DaemonOtherUser` → không gửi gì khi thoát, trả 0.
- `DaemonStatus.Stopped` là trường tuỳ chọn, mặc định null; `Seq == 0` thì không gửi. `Feature` ghi bằng `FeatureKeys.For`.
- Lần đầu app thấy `Stopped` chỉ lấy làm mốc, không phát event. Mất kết nối giữ mốc.
- Câu chữ (chỉ `Strings.resx` tiếng Việt):
  - `RestoreUnknown`: `Không biết đã hoàn tác xong chưa. Hãy mở lại LunaDNS hoặc chạy LunaDNS.exe --cleanup.`
  - `RestoreUnknown_Mac`: `Dịch vụ nền không trả lời nên LunaDNS có thể vẫn đang định tuyến. Mở Terminal và chạy: sudo lunadns off`
- Verify cuối: `dotnet test` cả solution. Trên Windows, 5 test `MacOS/*` của App.Tests `-f net8.0` (SetUnixFileMode) trượt sẵn: mốc cũ, không phải lỗi mới. CI `build-test-macos` phải xanh.

## File Structure

| File | Việc |
|---|---|
| `src/LunaDns.App/Features/IFeatureController.cs` | thêm `ShutdownOutcome.Unknown`, tài liệu -1 |
| `src/LunaDns.App/MacOS/DaemonFeatureController.cs` | B8 (`ShutdownAsync`, `QuitRetryDelay`) + F5 (event thật, mốc `Seq`) |
| `src/LunaDns.App/App.axaml.cs` | hộp thoại -1 khi thoát, log -1 khi đăng xuất |
| `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs` | `RestoreUnknown`, `RestoreUnknown_Mac` |
| `src/LunaDns.Platform.MacOS/Daemon/DaemonProtocol.cs` | `DaemonStopped`, `DaemonStatus.Stopped`, đăng ký JSON |
| `src/LunaDns.Platform.MacOS/Daemon/WatchdogStops.cs` (mới) | ghi lần dừng gần nhất, an toàn luồng |
| `src/LunaDns.Platform.MacOS/Daemon/DaemonCommands.cs` | đăng ký `FeatureStopped`, gửi `Stopped` trong `Ok()` |
| `tests/LunaDns.App.Tests/MacOS/DaemonFeatureControllerTests.cs` | test B8 + F5 phía app, mở rộng `FakeDaemon` |
| `tests/LunaDns.Platform.MacOS.Tests/WatchdogStopsTests.cs` (mới) | test `WatchdogStops` |
| `tests/LunaDns.Platform.MacOS.Tests/DaemonTests.cs` | test `status` có/không có `Stopped` |

Thứ tự: Task 1 (B8) độc lập. Task 2 (daemon) trước Task 3 (app F5) vì Task 3 dùng `DaemonStopped`.

---

### Task 1: B8 — Thoát vẫn gửi `disable-all`, thử lại, trả -1 khi không biết

**Files:**
- Modify: `src/LunaDns.App/Features/IFeatureController.cs` (cạnh `enum ShutdownReason`, dòng ~29; tài liệu `ShutdownAsync` dòng ~100–104)
- Modify: `src/LunaDns.App/MacOS/DaemonFeatureController.cs:205-218` (`ShutdownAsync`)
- Modify: `src/LunaDns.App/App.axaml.cs:163-170` (`QuitAsync`), `:186-189` (`OnShutdownRequested`), log ở cuối file (~dòng 238)
- Modify: `src/LunaDns.App/Strings.resx` (sau dòng 116 `RestoreFailed`), `src/LunaDns.App/Strings.cs` (sau dòng 143)
- Test: `tests/LunaDns.App.Tests/MacOS/DaemonFeatureControllerTests.cs`

**Interfaces:**
- Produces: `internal static class ShutdownOutcome { public const int Unknown = -1; }` (namespace `LunaDns.App.Features`); `DaemonFeatureController.QuitRetryDelay` (`internal TimeSpan`, mặc định 1 giây); `Strings.RestoreUnknown`.

- [ ] **Step 1: Mở rộng `FakeDaemon` để đếm lượt gọi và lỗi N lượt đầu**

Trong `FakeDaemon` (cuối file test), thêm hai thuộc tính và sửa đầu `SendAsync`:

```csharp
        /// <summary>Số lượt gửi tiếp theo ném DaemonUnavailableException (như Down, nhưng tự hết).</summary>
        public int DownFor { get; set; }
        /// <summary>Mọi lượt gửi, kể cả lượt lỗi hoặc bị từ chối (Requests chỉ ghi lượt được xử lý).</summary>
        public int Attempts { get; set; }

        public Task<DaemonResponse> SendAsync(DaemonRequest request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (DownFor > 0)
            {
                DownFor--;
                throw new DaemonUnavailableException("down");
            }
            if (Down) throw new DaemonUnavailableException("down");
            // ... phần còn lại giữ nguyên
```

Sửa `Create` để test không phải chờ 1 giây:

```csharp
    private DaemonFeatureController Create(bool installed = true, Version? appVersion = null) =>
        new(_daemon, () => installed, NullLoggerFactory.Instance, appVersion) { QuitRetryDelay = TimeSpan.Zero };
```

- [ ] **Step 2: Viết test hỏng**

Thêm vào `DaemonFeatureControllerTests` (gần `Shutdown_disables_all_when_something_is_on`):

```csharp
    [Fact]
    public async Task User_quit_after_a_failed_poll_still_disables_all()
    {
        _daemon.Down = true;
        await using var controller = Create();
        await controller.RefreshAsync();
        Assert.Equal(ControllerAvailability.DaemonNotResponding, controller.Availability);
        _daemon.Down = false;
        _daemon.Dns = true;

        var remaining = await controller.ShutdownAsync(ShutdownReason.UserQuit);

        Assert.Equal(0, remaining);
        Assert.Equal(new DaemonRequest(DaemonProtocol.DisableAll), _daemon.LastRequest);
        Assert.False(_daemon.Dns);
    }

    [Fact]
    public async Task User_quit_retries_once_when_the_daemon_does_not_answer()
    {
        await using var controller = Create();
        _daemon.Dns = true;
        _daemon.DownFor = 1;

        var remaining = await controller.ShutdownAsync(ShutdownReason.UserQuit);

        Assert.Equal(0, remaining);
        Assert.Equal(2, _daemon.Attempts);
        Assert.Equal(new DaemonRequest(DaemonProtocol.DisableAll), _daemon.LastRequest);
    }

    [Fact]
    public async Task User_quit_is_unknown_when_both_tries_get_no_answer()
    {
        await using var controller = Create();
        _daemon.Down = true;

        var remaining = await controller.ShutdownAsync(ShutdownReason.UserQuit);

        Assert.Equal(ShutdownOutcome.Unknown, remaining);
        Assert.Equal(2, _daemon.Attempts);
    }

    [Fact]
    public async Task User_quit_does_not_call_a_daemon_that_is_not_installed()
    {
        _daemon.Down = true;
        await using var controller = Create(installed: false);
        await controller.RefreshAsync();
        _daemon.Attempts = 0;

        Assert.Equal(0, await controller.ShutdownAsync(ShutdownReason.UserQuit));
        Assert.Equal(0, _daemon.Attempts);
    }

    [Fact]
    public async Task User_quit_leaves_the_other_console_users_features_alone()
    {
        _daemon.Reject = true;
        await using var controller = Create();
        await controller.RefreshAsync();
        Assert.Equal(ControllerAvailability.DaemonOtherUser, controller.Availability);
        _daemon.Attempts = 0;

        Assert.Equal(0, await controller.ShutdownAsync(ShutdownReason.UserQuit));
        Assert.Equal(0, _daemon.Attempts);
    }

    [Fact]
    public async Task Session_end_sends_once_and_is_unknown_without_an_answer()
    {
        _daemon.Dns = true;
        await using var controller = Create();
        await controller.RefreshAsync();
        _daemon.Down = true;
        _daemon.Attempts = 0;

        var remaining = await controller.ShutdownAsync(ShutdownReason.SessionEnd);

        Assert.Equal(ShutdownOutcome.Unknown, remaining);
        Assert.Equal(1, _daemon.Attempts);
    }
```

- [ ] **Step 3: Chạy để thấy hỏng**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 --filter "FullyQualifiedName~DaemonFeatureControllerTests"`
Expected: lỗi biên dịch (`ShutdownOutcome`, `QuitRetryDelay` chưa có).

- [ ] **Step 4: Thêm `ShutdownOutcome` và sửa tài liệu**

Trong `IFeatureController.cs`, ngay sau `enum ShutdownReason { ... }`:

```csharp
/// <summary>Giá trị đặc biệt của <see cref="IFeatureController.ShutdownAsync"/>.</summary>
internal static class ShutdownOutcome
{
    /// <summary>Không biết đã hoàn tác chưa: dịch vụ nền không trả lời (B8). Chỉ bản daemon trả.</summary>
    public const int Unknown = -1;
}
```

Trong tài liệu `ShutdownAsync` của interface, thêm một dòng trước `</summary>`:
`/// Trả số thay đổi còn sót; <see cref="ShutdownOutcome.Unknown"/> khi không biết (dịch vụ nền không trả lời).`

- [ ] **Step 5: Sửa `DaemonFeatureController.ShutdownAsync`**

Thêm thuộc tính (gần các trường đầu lớp):

```csharp
    /// <summary>Chờ trước lần gửi lại duy nhất khi người dùng thoát mà dịch vụ nền không trả lời (B8). Test đặt 0.</summary>
    internal TimeSpan QuitRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
```

Thay toàn bộ `ShutdownAsync` và tài liệu của nó:

```csharp
    /// <summary>
    /// Chưa cài, hoặc tài khoản khác đang ngồi trước máy (B4): không gửi. Daemon đang chạy mà không có gì bật (và, khi người
    /// dùng thoát, không còn wanted để xoá): không làm phiền. Còn lại (kể cả lần poll gần nhất lỗi, B8) thì gửi disable-all;
    /// người dùng thoát mà không có trả lời thì gửi lại một lần. Không có trả lời → <see cref="ShutdownOutcome.Unknown"/>.
    /// Hết phiên thì gửi kèm SessionEnd để daemon giữ wanted (F1), một lần, không thử lại (giới hạn 4 giây của App).
    /// </summary>
    public async Task<int> ShutdownAsync(ShutdownReason reason)
    {
        var snapshot = _snapshot;
        if (snapshot.Availability is ControllerAvailability.DaemonNotInstalled or ControllerAvailability.DaemonOtherUser) return 0;
        if (snapshot.Availability == ControllerAvailability.DaemonRunning)
        {
            var wantedLeft = reason == ShutdownReason.UserQuit && snapshot.Resume is { } resume && !resume.Wanted.IsEmpty();
            if (!snapshot.Status.DnsRouting && !snapshot.Status.SniProxy && snapshot.JournalEntries is 0 or null && !wantedLeft) return 0;
        }
        else if (reason == ShutdownReason.SessionEnd)
        {
            return 0;
        }

        var request = new DaemonRequest(DaemonProtocol.DisableAll, SessionEnd: reason == ShutdownReason.SessionEnd ? true : null);
        var response = await SendAsync(request).ConfigureAwait(false);
        if (response is null && reason == ShutdownReason.UserQuit)
        {
            await Task.Delay(QuitRetryDelay).ConfigureAwait(false);
            response = await SendAsync(request).ConfigureAwait(false);
        }
        return response is null ? ShutdownOutcome.Unknown : Remaining(response);
    }
```

Ghi chú: `SessionEnd` giữ điều kiện cũ (snapshot phải `DaemonRunning`) để không tốn thời gian trong 4 giây.
`Session_end_sends_once_and_is_unknown_without_an_answer` refresh trước nên snapshot là `DaemonRunning`.

`SendAsync` không có trả lời thì `SendCoreAsync` đặt snapshot về `DaemonNotResponding`/`DaemonNotInstalled`. Nếu lần
gửi đầu thấy không còn cài (`_isInstalled()` sai), lần gửi lại vẫn được phép; không cần xử lý riêng.

- [ ] **Step 6: Chạy test**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 --filter "FullyQualifiedName~DaemonFeatureControllerTests"`
Expected: PASS, kể cả `Shutdown_with_nothing_on_does_not_call_daemon`, `User_quit_with_nothing_on_still_clears_wanted_left_in_the_daemon` và test SessionEnd cũ (dòng ~541).

- [ ] **Step 7: Thêm câu chữ**

`Strings.resx`, ngay sau dòng `RestoreFailed`:

```xml
  <data name="RestoreUnknown" xml:space="preserve"><value>Không biết đã hoàn tác xong chưa. Hãy mở lại LunaDNS hoặc chạy LunaDNS.exe --cleanup.</value></data>
  <data name="RestoreUnknown_Mac" xml:space="preserve"><value>Dịch vụ nền không trả lời nên LunaDNS có thể vẫn đang định tuyến. Mở Terminal và chạy: sudo lunadns off</value></data>
```

`Strings.cs`, ngay sau `public static string RestoreFailed => ...`:

```csharp
    public static string RestoreUnknown => Get(nameof(RestoreUnknown));
```

- [ ] **Step 8: Sửa `App.QuitAsync` và `OnShutdownRequested`**

`QuitAsync`:

```csharp
        var result = await Task.Run(() => Services!.ShutdownAsync(Features.ShutdownReason.UserQuit));
        if (result == Features.ShutdownOutcome.Unknown) await new DialogPrompts(() => ShowWindow()).InformAsync(Strings.AppTitle, Strings.RestoreUnknown);
        else if (result > 0) await new DialogPrompts(() => ShowWindow()).InformAsync(Strings.AppTitle, Strings.RestoreFailed);
```

`OnShutdownRequested`, thay dòng `else if (shutdown.Result > 0) ...`:

```csharp
            else if (shutdown.Result == Features.ShutdownOutcome.Unknown) LogSessionEndUnknown(_logger);
            else if (shutdown.Result > 0) LogSessionEndLeftovers(_logger, shutdown.Result);
```

Thêm log cạnh `LogSessionEndLeftovers`:

```csharp
    [LoggerMessage(Level = LogLevel.Error, Message = "Đăng xuất: dịch vụ nền không trả lời, không biết đã hoàn tác chưa.")]
    private static partial void LogSessionEndUnknown(ILogger logger);
```

- [ ] **Step 9: Chạy test App**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows` rồi `dotnet test tests/LunaDns.App.Tests -f net8.0`
Expected: net8.0-windows PASS hết (gồm `StringsTests.Mac_overrides_have_base_key_and_apply_only_on_mac`); net8.0 chỉ trượt 5 test MacOS/* SetUnixFileMode như mốc cũ.

- [ ] **Step 10: Commit**

```bash
git add src/LunaDns.App/Features/IFeatureController.cs src/LunaDns.App/MacOS/DaemonFeatureController.cs src/LunaDns.App/App.axaml.cs src/LunaDns.App/Strings.resx src/LunaDns.App/Strings.cs tests/LunaDns.App.Tests/MacOS/DaemonFeatureControllerTests.cs
git commit -m "fix(macos): Quit & restore still turns features off after a failed poll and says so when the service does not answer (B8)"
```

---

### Task 2: F5 phía daemon — ghi lần watchdog dừng và gửi trong `status`

**Files:**
- Create: `src/LunaDns.Platform.MacOS/Daemon/WatchdogStops.cs`
- Modify: `src/LunaDns.Platform.MacOS/Daemon/DaemonProtocol.cs` (dòng ~87–95 danh sách `[JsonSerializable]`; dòng ~140–149 `DaemonStatus`)
- Modify: `src/LunaDns.Platform.MacOS/Daemon/DaemonCommands.cs` (constructor dòng ~38–50; `Ok()` dòng ~262–270)
- Create: `tests/LunaDns.Platform.MacOS.Tests/WatchdogStopsTests.cs`
- Modify: `tests/LunaDns.Platform.MacOS.Tests/DaemonTests.cs` (lớp có `_commands` và `Send(...)`, dòng ~228–250)

**Interfaces:**
- Produces: `public sealed record DaemonStopped(long Seq, string Feature, bool ChangesLeftBehind);` (namespace `LunaDns.Platform.MacOS.Daemon`); `DaemonStatus` thêm tham số cuối `DaemonStopped? Stopped = null`; `internal sealed class WatchdogStops { void Record(FeatureStoppedEventArgs e); DaemonStopped? Latest { get; } }`; `DaemonCommands.Stops` (`internal WatchdogStops`, để test ghi trực tiếp).

- [ ] **Step 1: Viết test hỏng cho `WatchdogStops`**

`tests/LunaDns.Platform.MacOS.Tests/WatchdogStopsTests.cs`:

```csharp
using LunaDns.Core.Features;
using LunaDns.Platform.MacOS.Daemon;

namespace LunaDns.Platform.MacOS.Tests;

public sealed class WatchdogStopsTests
{
    [Fact]
    public void Nothing_recorded_means_no_latest()
    {
        Assert.Null(new WatchdogStops().Latest);
    }

    [Fact]
    public void Each_stop_bumps_the_sequence_and_keeps_only_the_latest()
    {
        var stops = new WatchdogStops();

        stops.Record(new FeatureStoppedEventArgs(Feature.DnsRouting, changesLeftBehind: false));
        Assert.Equal(new DaemonStopped(1, FeatureKeys.DnsRouting, false), stops.Latest);

        stops.Record(new FeatureStoppedEventArgs(Feature.SniProxy, changesLeftBehind: true));
        Assert.Equal(new DaemonStopped(2, FeatureKeys.SniProxy, true), stops.Latest);
    }

    [Fact]
    public async Task Concurrent_stops_each_get_their_own_sequence()
    {
        var stops = new WatchdogStops();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            stops.Record(new FeatureStoppedEventArgs(Feature.DnsRouting, changesLeftBehind: false)))));

        Assert.Equal(50, stops.Latest!.Seq);
    }
}
```

Kiểm namespace của các test khác trong `tests/LunaDns.Platform.MacOS.Tests` (vd. dòng đầu `DaemonTests.cs`) và dùng đúng namespace đó.

- [ ] **Step 2: Chạy để thấy hỏng**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests --filter "FullyQualifiedName~WatchdogStopsTests"`
Expected: lỗi biên dịch (`WatchdogStops`, `DaemonStopped` chưa có).

- [ ] **Step 3: Thêm `DaemonStopped` và trường `Stopped`**

`DaemonProtocol.cs`: thêm vào danh sách `[JsonSerializable(...)]` (cạnh `[JsonSerializable(typeof(DaemonResume))]`):

```csharp
[JsonSerializable(typeof(DaemonStopped))]
```

Sửa `DaemonStatus` (giữ nguyên các tham số cũ, thêm tham số cuối) và thêm một dòng vào `<remarks>` phía trên:

```csharp
/// Stopped (F5): lần watchdog dừng tính năng gần nhất kể từ khi daemon khởi động; null (không gửi) khi chưa dừng lần nào.
/// Daemon cũ không gửi, giao diện cũ bỏ qua.
public sealed record DaemonStatus(bool DnsRouting, bool SniProxy, int? ProxyPort, int ProxyConnections, int JournalEntries,
    string? DohServer = null, bool? DohFallback = null, DaemonStopped? Stopped = null);

/// <summary>Watchdog đã dừng <see cref="Feature"/> (khoá FeatureKeys). Seq tăng từ 1, chỉ trong bộ nhớ của daemon (F5).</summary>
public sealed record DaemonStopped(long Seq, string Feature, bool ChangesLeftBehind);
```

- [ ] **Step 4: Tạo `WatchdogStops`**

`src/LunaDns.Platform.MacOS/Daemon/WatchdogStops.cs`:

```csharp
using LunaDns.Core.Features;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Nhớ lần watchdog dừng tính năng gần nhất để gửi trong status (F5). Record được gọi từ luồng watchdog, Latest từ luồng
/// xử lý socket, nên ghi/đọc qua khoá.
/// </summary>
internal sealed class WatchdogStops
{
    private readonly object _lock = new();
    private DaemonStopped? _latest;

    public DaemonStopped? Latest
    {
        get
        {
            lock (_lock) return _latest;
        }
    }

    public void Record(FeatureStoppedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        lock (_lock)
        {
            _latest = new DaemonStopped((_latest?.Seq ?? 0) + 1, FeatureKeys.For(e.Feature), e.ChangesLeftBehind);
        }
    }
}
```

- [ ] **Step 5: Chạy `WatchdogStopsTests`**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests --filter "FullyQualifiedName~WatchdogStopsTests"`
Expected: PASS.

- [ ] **Step 6: Viết test hỏng cho `status`**

Trong `DaemonTests.cs`, ở lớp có `_commands` và helper `Send(string command, ...)` (dòng ~228–250), thêm:

```csharp
    [Fact]
    public async Task Status_has_no_stop_until_the_watchdog_stops_a_feature()
    {
        var before = await Send(DaemonProtocol.Status);
        Assert.Null(before.Status!.Stopped);

        _commands.Stops.Record(new FeatureStoppedEventArgs(Feature.SniProxy, changesLeftBehind: true));
        var after = await Send(DaemonProtocol.Status);

        Assert.Equal(new DaemonStopped(1, FeatureKeys.SniProxy, true), after.Status!.Stopped);
    }
```

Dòng đăng ký `features.FeatureStopped += ...` (Step 7) không có test riêng: `IdleServices` không biết crash, còn
watchdog thật đã có test ở `FeatureSetWatchdogTests` (Core). Kiểm dòng đó bằng review `git diff`.

- [ ] **Step 7: Nối `WatchdogStops` vào `DaemonCommands`**

Thêm trường và thuộc tính, đăng ký trong constructor đầy đủ (constructor nhận `SettingsHolder`, dòng ~38; constructor
nhận `JsonFileSettingsStore` gọi qua nó):

```csharp
    /// <summary>Lần watchdog dừng gần nhất (F5); cùng vòng đời với FeatureSet nên không bỏ đăng ký.</summary>
    internal WatchdogStops Stops { get; } = new();
```

Trong constructor, sau `this.features = features;`:

```csharp
        features.FeatureStopped += (_, e) => Stops.Record(e);
```

Trong `Ok()`:

```csharp
            Status: new DaemonStatus(s.DnsRouting, s.SniProxy, s.ProxyPort, s.ProxyConnections, journal.Entries.Count,
                s.Doh?.Server, s.Doh?.Fallback, Stops.Latest),
```

Nếu constructor thứ nhất (`JsonFileSettingsStore`) không gọi `: this(...)` mà tự gán trường, thêm đúng dòng đăng ký vào đó.

- [ ] **Step 8: Chạy test Platform.MacOS**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests`
Expected: PASS (trên Windows, test chỉ-macOS bị skip như cũ).

- [ ] **Step 9: Commit**

```bash
git add src/LunaDns.Platform.MacOS/Daemon/WatchdogStops.cs src/LunaDns.Platform.MacOS/Daemon/DaemonProtocol.cs src/LunaDns.Platform.MacOS/Daemon/DaemonCommands.cs tests/LunaDns.Platform.MacOS.Tests/WatchdogStopsTests.cs tests/LunaDns.Platform.MacOS.Tests/DaemonTests.cs
git commit -m "feat(macos): the background service reports the last watchdog stop in its status (F5)"
```

---

### Task 3: F5 phía app — phát `FeatureStopped` khi `Stopped.Seq` tăng

**Files:**
- Modify: `src/LunaDns.App/MacOS/DaemonFeatureController.cs` (event dòng ~50–55; `RefreshAsync` dòng ~95–106; `SendAsync` dòng ~226–237; `SendCoreAsync` nhánh `response.Status is { } s`, dòng ~256–262)
- Modify: `src/LunaDns.App/Features/IFeatureController.cs` (tài liệu event `FeatureStopped`, dòng ~44)
- Test: `tests/LunaDns.App.Tests/MacOS/DaemonFeatureControllerTests.cs`

**Interfaces:**
- Consumes: `DaemonStopped(long Seq, string Feature, bool ChangesLeftBehind)`, `DaemonStatus.Stopped` (Task 2); `FeatureKeys.TryParse(string?, out Feature)`.
- Produces: `DaemonFeatureController.FeatureStopped` là event thật; `MainViewModel.OnFeatureStopped` nhận như trên Windows.

- [ ] **Step 1: Cho `FakeDaemon` gửi `Stopped`**

Trong `FakeDaemon`, thêm `public DaemonStopped? Stopped { get; set; }` và sửa `Ok()`:

```csharp
        private DaemonResponse Ok() =>
            new(true, Version: "test", Status: new DaemonStatus(Dns, Proxy, Proxy ? 18080 : null, 0, Dns || Proxy ? 1 : 0,
                DohServer, DohServer is null ? null : DohFallback, Stopped), Resume: Resume);
```

- [ ] **Step 2: Viết test hỏng**

```csharp
    private static DaemonStopped Stop(long seq, string feature = FeatureKeys.DnsRouting, bool leftBehind = false) =>
        new(seq, feature, leftBehind);

    [Fact]
    public async Task First_stop_seen_is_a_baseline_and_later_stops_raise_once()
    {
        _daemon.Stopped = Stop(3);
        await using var controller = Create();
        var raised = new List<FeatureStoppedEventArgs>();
        controller.FeatureStopped += (_, e) => raised.Add(e);

        await controller.RefreshAsync();
        Assert.Empty(raised);

        _daemon.Stopped = Stop(4, FeatureKeys.SniProxy, leftBehind: true);
        await controller.RefreshAsync();
        await controller.RefreshAsync();

        var only = Assert.Single(raised);
        Assert.Equal(Feature.SniProxy, only.Feature);
        Assert.True(only.ChangesLeftBehind);
    }

    [Fact]
    public async Task Daemon_restart_resets_the_baseline_without_raising()
    {
        _daemon.Stopped = Stop(5);
        await using var controller = Create();
        var raised = new List<FeatureStoppedEventArgs>();
        controller.FeatureStopped += (_, e) => raised.Add(e);
        await controller.RefreshAsync();

        _daemon.Stopped = Stop(1);
        await controller.RefreshAsync();
        Assert.Empty(raised);

        _daemon.Stopped = Stop(2);
        await controller.RefreshAsync();
        Assert.Single(raised);
    }

    [Fact]
    public async Task No_stop_yet_then_a_stop_raises()
    {
        await using var controller = Create();
        var raised = new List<FeatureStoppedEventArgs>();
        controller.FeatureStopped += (_, e) => raised.Add(e);
        await controller.RefreshAsync();

        _daemon.Stopped = Stop(1);
        await controller.RefreshAsync();

        Assert.Equal(Feature.DnsRouting, Assert.Single(raised).Feature);
    }

    [Fact]
    public async Task Reconnecting_with_the_same_stop_does_not_raise_again()
    {
        _daemon.Stopped = Stop(2);
        await using var controller = Create();
        var raised = new List<FeatureStoppedEventArgs>();
        controller.FeatureStopped += (_, e) => raised.Add(e);
        await controller.RefreshAsync();

        _daemon.Down = true;
        await controller.RefreshAsync();
        _daemon.Down = false;
        await controller.RefreshAsync();

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Unknown_feature_key_moves_the_baseline_without_raising()
    {
        await using var controller = Create();
        var raised = new List<FeatureStoppedEventArgs>();
        controller.FeatureStopped += (_, e) => raised.Add(e);
        await controller.RefreshAsync();

        _daemon.Stopped = Stop(1, "future-feature");
        await controller.RefreshAsync();
        _daemon.Stopped = Stop(1);
        await controller.RefreshAsync();

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Stop_seen_in_a_command_reply_raises_too()
    {
        await using var controller = Create();
        var raised = new List<FeatureStoppedEventArgs>();
        controller.FeatureStopped += (_, e) => raised.Add(e);
        await controller.RefreshAsync();

        _daemon.Stopped = Stop(1);
        await controller.DisableAsync(Feature.DnsRouting);

        Assert.Single(raised);
    }
```

- [ ] **Step 3: Chạy để thấy hỏng**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 --filter "FullyQualifiedName~DaemonFeatureControllerTests"`
Expected: các test `raise` hỏng (event rỗng, `raised` luôn trống); `First_stop_seen...` hỏng ở `Assert.Single`.

- [ ] **Step 4: Cài đặt**

Thay event rỗng (và chú thích "Daemon không báo watchdog...") bằng:

```csharp
    /// <summary>Watchdog của dịch vụ nền đã dừng một tính năng (F5): phát khi Stopped.Seq vượt mốc, ngoài khoá _gate.</summary>
    public event EventHandler<FeatureStoppedEventArgs>? FeatureStopped;
```

Thêm trường:

```csharp
    // Mốc Stopped.Seq (F5): null = chưa thấy status nào. Không đặt lại khi mất kết nối, để nối lại không báo trùng.
    private long? _stoppedSeq;
    // Lần dừng mới thấy trong lúc giữ _gate; phát sau khi nhả khoá.
    private FeatureStoppedEventArgs? _pendingStop;
```

Trong `SendCoreAsync`, nhánh `if (response.Status is { } s)`, sau khi gán `_snapshot`:

```csharp
            NoteStopped(s.Stopped);
```

Thêm hàm:

```csharp
    private void NoteStopped(DaemonStopped? stopped)
    {
        var seq = stopped?.Seq ?? 0;
        var baseline = _stoppedSeq;
        _stoppedSeq = seq;
        // Lần đầu thấy, hoặc daemon khởi động lại (Seq nhỏ hơn mốc): chỉ lấy mốc. Banner U1 lo lần dừng trước khi app mở.
        if (baseline is not { } previous || seq <= previous) return;
        if (!FeatureKeys.TryParse(stopped!.Feature, out var feature)) return;
        _pendingStop = new FeatureStoppedEventArgs(feature, stopped.ChangesLeftBehind);
    }

    private void RaisePendingStop()
    {
        var stop = Interlocked.Exchange(ref _pendingStop, null);
        if (stop is not null) FeatureStopped?.Invoke(this, stop);
    }
```

Gọi `RaisePendingStop()` sau `_gate.Release()` ở cả `RefreshAsync` và `SendAsync`:

```csharp
        finally
        {
            _gate.Release();
        }
        RaisePendingStop();
```

Với `SendAsync` (đang `return await ...` trong `try`), đổi thành:

```csharp
    private async Task<DaemonResponse?> SendAsync(DaemonRequest request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        DaemonResponse? response;
        try
        {
            response = await SendCoreAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        RaisePendingStop();
        return response;
    }
```

`RefreshAsync` có thể ném `OperationCanceledException`; đặt `RaisePendingStop()` sau khối `try/finally` (không chạy khi
ném là được: lần poll sau sẽ phát vì `_pendingStop` vẫn giữ).

Nếu còn chỗ nào khác gọi `SendCoreAsync` trong khi giữ `_gate` (tìm `SendCoreAsync(` trong file), thêm `RaisePendingStop()` sau `_gate.Release()` ở đó.

Sửa tài liệu event trong `IFeatureController.cs` (dòng ~44): bỏ câu "Bản daemon không phát ...", thay bằng
`Bản daemon phát khi status cho thấy watchdog của dịch vụ nền vừa dừng tính năng (F5).`

- [ ] **Step 5: Chạy test**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 --filter "FullyQualifiedName~DaemonFeatureControllerTests"`
Expected: PASS.

- [ ] **Step 6: Chạy cả solution**

Run: `dotnet test`
Expected: PASS hết trừ 5 test `MacOS/*` SetUnixFileMode của App.Tests `-f net8.0` (mốc cũ trên Windows).

- [ ] **Step 7: Commit**

```bash
git add src/LunaDns.App/MacOS/DaemonFeatureController.cs src/LunaDns.App/Features/IFeatureController.cs tests/LunaDns.App.Tests/MacOS/DaemonFeatureControllerTests.cs
git commit -m "feat(macos): show 'stopped because of an error' when the background service's watchdog gives up (F5)"
```

---

### Task 4: Kiểm trên CI macOS và thử tay

- [ ] **Step 1:** Push nhánh, chờ CI `build-test` và `build-test-macos` xanh.
- [ ] **Step 2 (PO, trên Mac):** `build/run-macos.sh`; bật Định tuyến DNS; `sudo launchctl kill SIGSTOP system/<label dịch vụ nền>` để dịch vụ nền treo; bấm **Thoát & khôi phục** → sau ~1–2 giây hiện câu `RestoreUnknown_Mac`, bấm OK thì app thoát. `sudo launchctl kill SIGCONT ...`, chạy `sudo lunadns off`.
- [ ] **Step 3 (PO, tuỳ chọn):** thử F5 bằng cách làm dịch vụ DNS stub chết 4 lần trong 1 phút (vd. chiếm cổng stub); thẻ hiện "Đã tự tắt vì lỗi" cùng thông báo phía trên.
