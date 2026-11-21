# D3: Tìm cấu hình hợp mạng — Kế hoạch triển khai

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nút "Tìm cấu hình hợp mạng" tự chọn máy DoH và tổ hợp cắt gói (điểm cắt × tách record × độ trễ) chạy được với 4 host Steam, rồi tự lưu và bật biện pháp, trên cả Windows và macOS.

**Architecture:** `NetworkTuner` (Core, mới) điều phối 3 bước: đo DoH bằng `DohLatencyMeter` (D2), phân loại bằng `Prober`, thử tối đa 8 `TuneCandidate` bằng `ITlsProbe`. Mọi phụ thuộc nhận qua tham số nên test dùng bản giả. `AppServices.TuneAsync` ghép Core cho giao diện. Phần áp đi qua `IFeatureController.ApplyTuningAsync`: Windows lưu settings rồi `FeatureSet.ReapplyTuningAsync`; macOS gửi lệnh daemon mới `set-tuning`, daemon tự kiểm, áp rồi lưu, đọc settings qua `SettingsHolder` dùng chung với `Engine`.

**Tech Stack:** .NET 8, C# 12, xUnit, Avalonia 11.3.23, System.Text.Json source generator (`SettingsJsonContext`, `DaemonJsonContext`).

**Spec:** [2026-11-21-lunadns-auto-tune-design.md](../specs/2026-11-21-lunadns-auto-tune-design.md)

## Global Constraints

- Tổng thời gian dò tối đa **60 giây** (`CancellationTokenSource` ở `MainViewModel`). Mỗi lần bắt tay thử timeout **3 giây** (`Prober.DefaultCheckTimeout`). Đo DoH trong lượt dò: tối đa **10 giây**.
- Host thử: đúng `ProbeTargets.Steam` (4 host), không theo lựa chọn nhóm tên miền.
- Tổ hợp **chạy được** = cả 4 host bắt tay TLS có cắt gói thành công tới `DohAddresses[0]`, chứng chỉ kiểm theo host. Dừng ở tổ hợp đầu tiên chạy được.
- Thứ tự: tổ hợp đang dùng trước, rồi bảng FirstByte/tắt/0, FirstByte/tắt/10, MidSni/tắt/0, MidSni/tắt/10, FirstByte/bật/0, FirstByte/bật/10, MidSni/bật/0, MidSni/bật/10. Bỏ dòng trùng tổ hợp đang dùng, tổng ≤ **8**.
- Tìm được thì **tự lưu và bật**, không hỏi. Huỷ (nút Dừng, đóng cửa sổ, hết giờ) trong bước dò thì **không đổi gì**. Đã sang bước áp thì chạy cho xong.
- macOS: lệnh `get-tuning` / `set-tuning`. Daemon **không nhận** `DohProvider.Custom`, độ trễ phải trong `0..AppSettings.MaxSplitDelayMs` (20). Daemon cũ trả "Lệnh lạ:" thì app báo cần cài lại dịch vụ nền và không áp gì. Dây JSON cũ (`set-domains`, `status`…) giữ nguyên từng byte.
- Không cài chứng chỉ, không MITM. Probe đi socket trần, không qua proxy hệ thống.
- Build: `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`. CI build `-c Release -warnaserror`. Code mới không được sinh cảnh báo.
- Chú thích và chuỗi giao diện bằng tiếng Việt, theo giọng các file xung quanh. Commit và PR bằng tiếng Anh, **không** thêm `Co-Authored-By` hay dòng "Generated with Claude Code".
- `dotnet` có thể không nằm trong PATH: `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet`.

## Nhánh và thứ tự

Làm ở worktree riêng, nhánh `feat/auto-tune` tách từ `main` (D1, D2 đã merge qua PR #4, #5). Nhánh `claude/docs-d3-auto-tune-*` chỉ chứa spec và kế hoạch này; merge nó trước hoặc cherry-pick vào nhánh code. Merge qua PR để CI chạy cả Windows và macOS.

Task 1–4 chỉ đụng Core, làm tuần tự. Task 5 (daemon) và Task 6 (app) đều cần Task 2 và 4. Task 7 cần Task 6.

## Điều chỉnh so với spec

Kế hoạch chốt các chi tiết spec còn để mở. Spec đã được sửa theo, cùng commit với kế hoạch này:

1. `LocalFeatureController.ApplyTuningAsync` chỉ gọi `FeatureSet.ReapplyTuningAsync`. Lưu settings trước khi chạy lại, và lưu lại bản cũ khi lỗi, là việc của `AppServices.ApplyTuningAsync` (giống `ApplyDomainsAsync` hiện nay). Controller không nhận callback.
2. `AsyncCommand` không cho bấm lại khi lệnh đang chạy, nên "Dừng" là lệnh riêng (`StopTuneCommand`). Nút này hiện thay nút dò khi `IsTuning`. Bỏ `TuneButtonText`.
3. Trong lúc dò, `CheckCommand`, `ApplyRecommendedCommand`, `MeasureDohCommand` bị khoá qua `canExecute`. Thẻ tính năng chỉ bị khoá ở bước áp (`RunBusyAsync(Features, …)`).
4. Khi `Found`, `TuneResult.Probe` đổi host `Unreachable` có IP DoH thành `SniFiltered` (bắt tay nguyên vẹn lỗi, cắt gói qua), để dòng Steam không báo "không vào được" sai.
5. Kết quả dò thay `_summary` (khối kết quả Kiểm tra, chỉ nhóm Steam). Các dòng nhóm tên miền (`DomainGroups`) không đổi.
6. Thêm chuỗi `DaemonNeedsUpdateTuning`, vì `DaemonNeedsUpdate` hiện nói về danh sách tên miền, và `TuneFoundNotAppliedFormat` ("Tìm được {0}. {1}") cho mọi lỗi áp.

## Bản đồ file

| File | Trách nhiệm | Task |
|---|---|---|
| `src/LunaDns.Core/Probing/SplittingStream.cs` | Nhận độ trễ giữa hai phần | 1 |
| `src/LunaDns.Core/Probing/TlsProbe.cs` | Nhận độ trễ, truyền xuống `SplittingStream` | 1 |
| `src/LunaDns.Core/Probing/TuneCandidate.cs` (mới) | `TuneCandidate`, `TuningState`, `TuneCandidates` | 2 |
| `src/LunaDns.Core/Settings/AppSettings.cs` | `CurrentCandidate()`, `WithTuning()` | 2 |
| `src/LunaDns.Core/Probing/TuneResult.cs` (mới) | `TuneOutcome`, `TuneStage`, `TuneProgress`, `TuneResult` | 3 |
| `src/LunaDns.Core/Probing/NetworkTuner.cs` (mới) | Ba bước dò | 3 |
| `src/LunaDns.Core/Features/FeatureSet.cs` | `ReapplyTuningAsync` | 4 |
| `src/LunaDns.Core/Settings/SettingsHolder.cs` (mới) | Settings dùng chung trong daemon | 5 |
| `src/LunaDns.Platform.MacOS/Daemon/DaemonProtocol.cs` | `get-tuning`, `set-tuning`, `DaemonTuning` | 5 |
| `src/LunaDns.Platform.MacOS/Daemon/DaemonCommands.cs` | Xử lý hai lệnh mới, dùng `SettingsHolder` | 5 |
| `src/LunaDns.MacCli/Engine.cs`, `Program.cs` | Tạo và chia `SettingsHolder` | 5 |
| `src/LunaDns.App/Features/TuningApplyResult.cs` (mới) | Kết quả áp | 6 |
| `src/LunaDns.App/Features/IFeatureController.cs` | `GetTuningAsync`, `ApplyTuningAsync` | 6 |
| `src/LunaDns.App/Features/LocalFeatureController.cs` | Bản Windows | 6 |
| `src/LunaDns.App/MacOS/DaemonFeatureController.cs` | Bản macOS | 6 |
| `src/LunaDns.App/AppServices.cs` | `TuneAsync`, `ApplyTuningAsync`, `RefreshCustomDohAsync`, `TuneRun` | 6 |
| `src/LunaDns.App/TuneText.cs` (mới) | Câu hiển thị cho tổ hợp, tiến độ | 7 |
| `src/LunaDns.App/ViewModels/MainViewModel.cs` | Lệnh dò, Dừng, áp | 7 |
| `src/LunaDns.App/Views/MainWindow.axaml` | Hai nút, dòng trạng thái | 7 |
| `src/LunaDns.App/Strings.resx`, `Strings.cs` | Chuỗi mới | 6, 7 |
| `docs/guide/04-kiem-tra-va-de-xuat.md`, `09-macos.md`, bản `en` | Hướng dẫn | 7 |

Lệnh test chung (thay tên lớp):

```bash
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet
dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~<TênLớpTest>"
```

---

### Task 1: Độ trễ trong `SplittingStream` và `TlsProbe`

**Files:**
- Modify: `src/LunaDns.Core/Probing/SplittingStream.cs`
- Modify: `src/LunaDns.Core/Probing/TlsProbe.cs`
- Test: `tests/LunaDns.Core.Tests/Probing/SplittingStreamTests.cs`, `tests/LunaDns.Core.Tests/Probing/TlsProbeTests.cs`

**Interfaces:**
- Consumes: `SplitWriter.WriteAsync(Stream, ReadOnlyMemory<byte>, HelloSplit, TimeSpan, CancellationToken)`, `SplitWriter.MaxDelay` (20 ms).
- Produces: `new SplittingStream(Stream inner, HelloSplit split, TimeSpan delay = default)`; `public TlsProbe(HelloSplit split, TimeSpan delay, ILogger<TlsProbe>? logger = null)`; internal `TlsProbe(int port, HelloSplit split, RemoteCertificateValidationCallback? validation, ILogger<TlsProbe>? logger, TimeSpan delay = default)`.

- [ ] **Step 1: Viết test hỏng**

Thêm vào `SplittingStreamTests`:

```csharp
    [Fact]
    public async Task Delay_is_applied_between_pieces_of_the_first_write()
    {
        var inner = new RecordingStream();
        await using var stream = new SplittingStream(inner, HelloSplit.Default, TimeSpan.FromMilliseconds(20));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await stream.WriteAsync(Hello);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(15), $"chỉ nghỉ {watch.Elapsed.TotalMilliseconds} ms");
        var writes = inner.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Equal(Hello, writes[0].Concat(writes[1]).ToArray());
    }

    [Fact]
    public void Sync_write_also_applies_delay()
    {
        var inner = new RecordingStream();
        using var stream = new SplittingStream(inner, HelloSplit.Default, TimeSpan.FromMilliseconds(20));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        stream.Write(Hello, 0, Hello.Length);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(15));
        Assert.Equal(2, inner.Writes.Count);
    }
```

Thêm vào `TlsProbeTests`:

```csharp
    [Fact]
    public async Task Split_handshake_with_delay_is_accepted_by_the_server()
    {
        var probe = new TlsProbe(_server.Port, new HelloSplit(SplitMode.MidSni, Records: true), _server.Trusts, null, TimeSpan.FromMilliseconds(10));

        Assert.True(await probe.HandshakeAsync(IPAddress.Loopback, Host, split: true, CancellationToken.None));
    }

    [Fact]
    public void Delay_above_max_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TlsProbe(HelloSplit.Default, TimeSpan.FromMilliseconds(21)));
```

- [ ] **Step 2: Chạy, thấy hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplittingStreamTests|FullyQualifiedName~TlsProbeTests"`
Expected: lỗi build. `SplittingStream` chưa có tham số thứ ba, `TlsProbe` chưa có constructor nhận `TimeSpan`.

- [ ] **Step 3: Sửa `SplittingStream`**

```csharp
    private readonly Stream _inner;
    private readonly HelloSplit _split;
    private readonly TimeSpan _delay;
    private bool _first = true;

    /// <param name="delay">Nghỉ giữa các phần của record đầu, như proxy (SplitWriter). 0 = không nghỉ.</param>
    public SplittingStream(Stream inner, HelloSplit split, TimeSpan delay = default)
    {
        _inner = inner;
        _split = split;
        _delay = delay;
    }
```

Trong `Write(byte[], int, int)`, thay vòng `foreach` bằng:

```csharp
        var pieces = HelloSplitter.Split(data[..first], _split);
        for (var i = 0; i < pieces.Count; i++)
        {
            if (i > 0 && _delay > TimeSpan.Zero) Thread.Sleep(_delay);
            _inner.Write(pieces[i].Span);
            _inner.Flush();
        }
```

Trong `WriteAsync(ReadOnlyMemory<byte>, …)`, đổi `TimeSpan.Zero` thành `_delay`:

```csharp
        await SplitWriter.WriteAsync(_inner, buffer[..first], _split, _delay, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 4: Sửa `TlsProbe`**

```csharp
    private readonly int _port;
    private readonly HelloSplit _split;
    private readonly TimeSpan _delay;
    private readonly RemoteCertificateValidationCallback? _validation;
    private readonly ILogger _logger;

    public TlsProbe(HelloSplit? split = null, ILogger<TlsProbe>? logger = null)
        : this(443, split ?? HelloSplit.Default, null, logger)
    {
    }

    /// <summary>D3: thử đúng tổ hợp cắt gói + độ trễ mà proxy sẽ dùng.</summary>
    public TlsProbe(HelloSplit split, TimeSpan delay, ILogger<TlsProbe>? logger = null)
        : this(443, split, null, logger, delay)
    {
    }

    /// <summary>Cho test: cổng và cách kiểm tra chứng chỉ tuỳ chọn.</summary>
    internal TlsProbe(int port, HelloSplit split, RemoteCertificateValidationCallback? validation, ILogger<TlsProbe>? logger,
        TimeSpan delay = default)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, SplitWriter.MaxDelay);
        _port = port;
        _split = split;
        _delay = delay;
        _validation = validation;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }
```

Trong `HandshakeAsync`: `if (split) transport = new SplittingStream(transport, _split, _delay);`

- [ ] **Step 5: Chạy, thấy qua**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplittingStreamTests|FullyQualifiedName~TlsProbeTests"`
Expected: PASS, các test cũ giữ nguyên kết quả.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.Core/Probing/SplittingStream.cs src/LunaDns.Core/Probing/TlsProbe.cs tests/LunaDns.Core.Tests/Probing/SplittingStreamTests.cs tests/LunaDns.Core.Tests/Probing/TlsProbeTests.cs
git commit -m "feat(probe): apply split delay in TlsProbe like the proxy does"
```

---

### Task 2: `TuneCandidate`, `TuneCandidates`, `TuningState` và `AppSettings`

**Files:**
- Create: `src/LunaDns.Core/Probing/TuneCandidate.cs`
- Modify: `src/LunaDns.Core/Settings/AppSettings.cs`
- Test: `tests/LunaDns.Core.Tests/Probing/TuneCandidatesTests.cs` (mới), `tests/LunaDns.Core.Tests/Settings/AppSettingsTests.cs`

**Interfaces:**
- Consumes: `HelloSplit(SplitMode Mode, bool Records)`, `HelloSplit.Default`, `AppSettings.WithDoh(DohProvider)`, `AppSettings.MaxSplitDelayMs` (20).
- Produces:
  - `public sealed record TuneCandidate(HelloSplit Split, int DelayMs)` với `TimeSpan Delay` và `static TuneCandidate Default`;
  - `public sealed record TuningState(DohProvider Doh, TuneCandidate Candidate)`;
  - `TuneCandidates.Table`, `TuneCandidates.MaxCount = 8`, `TuneCandidates.Ordered(TuneCandidate current)`;
  - `AppSettings.CurrentCandidate()`, `AppSettings.WithTuning(DohProvider, TuneCandidate)`.

- [ ] **Step 1: Viết test hỏng**

`tests/LunaDns.Core.Tests/Probing/TuneCandidatesTests.cs`:

```csharp
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Probing;

public class TuneCandidatesTests
{
    private static TuneCandidate C(SplitMode mode, bool records, int delay) => new(new HelloSplit(mode, records), delay);

    [Fact]
    public void Table_goes_from_least_to_most_intrusive()
    {
        TuneCandidate[] expected =
        [
            C(SplitMode.FirstByte, false, 0), C(SplitMode.FirstByte, false, 10),
            C(SplitMode.MidSni, false, 0), C(SplitMode.MidSni, false, 10),
            C(SplitMode.FirstByte, true, 0), C(SplitMode.FirstByte, true, 10),
            C(SplitMode.MidSni, true, 0), C(SplitMode.MidSni, true, 10),
        ];

        Assert.Equal(expected, TuneCandidates.Table);
    }

    [Fact]
    public void Current_in_table_goes_first_and_is_not_repeated()
    {
        var current = C(SplitMode.MidSni, true, 0);

        var ordered = TuneCandidates.Ordered(current);

        Assert.Equal(8, ordered.Count);
        Assert.Equal(current, ordered[0]);
        Assert.Single(ordered, c => c == current);
        Assert.Equal(TuneCandidates.Table.Where(c => c != current), ordered.Skip(1));
    }

    [Fact]
    public void Current_outside_table_goes_first_then_first_seven_rows()
    {
        var current = C(SplitMode.FirstByte, false, 5);

        var ordered = TuneCandidates.Ordered(current);

        Assert.Equal(TuneCandidates.MaxCount, ordered.Count);
        Assert.Equal(current, ordered[0]);
        Assert.Equal(TuneCandidates.Table.Take(7), ordered.Skip(1));
    }

    [Fact]
    public void Default_is_first_row() => Assert.Equal(TuneCandidates.Table[0], TuneCandidate.Default);
}
```

Thêm vào `AppSettingsTests`:

```csharp
    [Fact]
    public void Current_candidate_reflects_split_settings_and_clamps_delay()
    {
        var settings = new AppSettings { SplitMode = SplitMode.MidSni, SplitRecords = true, SplitDelayMs = 99 };

        Assert.Equal(new TuneCandidate(new HelloSplit(SplitMode.MidSni, true), AppSettings.MaxSplitDelayMs), settings.CurrentCandidate());
    }

    [Fact]
    public void With_tuning_sets_doh_and_all_split_fields()
    {
        var tuned = new AppSettings { PreferGoogleDoh = true }
            .WithTuning(DohProvider.Quad9, new TuneCandidate(new HelloSplit(SplitMode.MidSni, true), 10));

        Assert.Equal(DohProvider.Quad9, tuned.EffectiveDoh());
        Assert.False(tuned.PreferGoogleDoh);
        Assert.Equal(SplitMode.MidSni, tuned.SplitMode);
        Assert.True(tuned.SplitRecords);
        Assert.Equal(10, tuned.SplitDelayMs);
    }
```

(Nếu file test thiếu `using LunaDns.Core.Probing;`, `using LunaDns.Core.Tls;`, `using LunaDns.Core.Dns;` thì thêm vào.)

- [ ] **Step 2: Chạy, thấy hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~TuneCandidatesTests|FullyQualifiedName~AppSettingsTests"`
Expected: lỗi build, `TuneCandidate` chưa có.

- [ ] **Step 3: Viết `TuneCandidate.cs`**

```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Probing;

/// <summary>Một tổ hợp cắt gói D3 thử: kiểu cắt (D1) và độ trễ giữa hai phần.</summary>
public sealed record TuneCandidate(HelloSplit Split, int DelayMs)
{
    public static TuneCandidate Default { get; } = new(HelloSplit.Default, 0);

    public TimeSpan Delay => TimeSpan.FromMilliseconds(DelayMs);
}

/// <summary>Cấu hình đang dùng ở nơi giữ thật (Windows: settings của app; macOS: daemon).</summary>
public sealed record TuningState(DohProvider Doh, TuneCandidate Candidate);

/// <summary>Thứ tự thử của D3: tổ hợp đang dùng trước, rồi đi từ ít can thiệp tới nhiều.</summary>
public static class TuneCandidates
{
    public const int MaxCount = 8;

    public static IReadOnlyList<TuneCandidate> Table { get; } =
    [
        new(new HelloSplit(SplitMode.FirstByte, Records: false), 0),
        new(new HelloSplit(SplitMode.FirstByte, Records: false), 10),
        new(new HelloSplit(SplitMode.MidSni, Records: false), 0),
        new(new HelloSplit(SplitMode.MidSni, Records: false), 10),
        new(new HelloSplit(SplitMode.FirstByte, Records: true), 0),
        new(new HelloSplit(SplitMode.FirstByte, Records: true), 10),
        new(new HelloSplit(SplitMode.MidSni, Records: true), 0),
        new(new HelloSplit(SplitMode.MidSni, Records: true), 10),
    ];

    public static IReadOnlyList<TuneCandidate> Ordered(TuneCandidate current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return [.. Table.Where(c => c != current).Prepend(current).Take(MaxCount)];
    }
}
```

- [ ] **Step 4: Thêm vào `AppSettings`** (cạnh `Tuning()`, thêm `using LunaDns.Core.Probing;`)

```csharp
    /// <summary>Tổ hợp cắt gói đang lưu (D3). Hàm, không phải thuộc tính, để không ra JSON.</summary>
    public TuneCandidate CurrentCandidate() =>
        new(new HelloSplit(SplitMode, SplitRecords), Math.Clamp(SplitDelayMs, 0, MaxSplitDelayMs));

    /// <summary>Áp kết quả D3: máy DoH qua WithDoh (giữ PreferGoogleDoh cho bản cũ), rồi điểm cắt, tách record, độ trễ.</summary>
    public AppSettings WithTuning(DohProvider doh, TuneCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return WithDoh(doh) with
        {
            SplitMode = candidate.Split.Mode,
            SplitRecords = candidate.Split.Records,
            SplitDelayMs = Math.Clamp(candidate.DelayMs, 0, MaxSplitDelayMs),
        };
    }
```

- [ ] **Step 5: Chạy, thấy qua**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~TuneCandidatesTests|FullyQualifiedName~AppSettingsTests|FullyQualifiedName~JsonFileSettingsStoreTests"`
Expected: PASS. `JsonFileSettingsStoreTests` vẫn qua, nghĩa là JSON không có trường lạ.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.Core/Probing/TuneCandidate.cs src/LunaDns.Core/Settings/AppSettings.cs tests/LunaDns.Core.Tests/Probing/TuneCandidatesTests.cs tests/LunaDns.Core.Tests/Settings/AppSettingsTests.cs
git commit -m "feat(core): add tune candidates and AppSettings.WithTuning"
```

---

### Task 3: `NetworkTuner`

**Files:**
- Create: `src/LunaDns.Core/Probing/TuneResult.cs`, `src/LunaDns.Core/Probing/NetworkTuner.cs`
- Test: `tests/LunaDns.Core.Tests/Probing/NetworkTunerTests.cs` (mới)

**Interfaces:**
- Consumes:
  - `TuneCandidates.Ordered`, `TuneCandidate` (Task 2);
  - `Prober(IHostResolver system, IHostResolver doh, ITlsProbe tls, TimeSpan? checkTimeout, ILogger<Prober>?)`;
  - `Prober.RunAsync`, `ProbeAdvisor.Recommend`;
  - `DohChoice(DohProvider Provider, DohServer Server)`;
  - `DohLatency(DohProvider Provider, TimeSpan? Median, string? Error)` (danh sách xếp nhanh trước).
- Produces:
  - `TuneOutcome { Found, SplitNotNeeded, DohFailed, NothingWorked }`;
  - `TuneStage { MeasuringDoh, Classifying, TryingSplit }`;
  - `TuneProgress(TuneStage Stage, int Index, int Total, TuneCandidate? Candidate)`;
  - `TuneResult(TuneOutcome Outcome, DohProvider? Doh, TuneCandidate? Candidate, ProbeRemedies Remedies, IReadOnlyList<ProbeResult> Probe, IReadOnlyList<string> FailingHosts)`;
  - `new NetworkTuner(Func<IReadOnlyList<DohChoice>, CancellationToken, Task<IReadOnlyList<DohLatency>>> measureDoh, IHostResolver systemResolver, Func<DohServer, IHostResolver> dohResolver, Func<TuneCandidate, ITlsProbe> tls, TimeSpan? checkTimeout = null, ILoggerFactory? loggers = null)`;
  - `Task<TuneResult> RunAsync(IReadOnlyList<string> hosts, IReadOnlyList<DohChoice> choices, TuneCandidate current, IProgress<TuneProgress>? progress, CancellationToken cancellationToken)`.

- [ ] **Step 1: Viết `TuneResult.cs`** (chỉ kiểu dữ liệu, để test biên dịch được)

```csharp
using LunaDns.Core.Dns;

namespace LunaDns.Core.Probing;

public enum TuneOutcome
{
    /// <summary>Tìm được tổ hợp cắt gói cho cả 4 host.</summary>
    Found,
    /// <summary>Không host nào bị lọc SNI hay không vào được: chỉ cần DoH (và Định tuyến DNS nếu bị đầu độc).</summary>
    SplitNotNeeded,
    /// <summary>Không máy DoH nào trả lời.</summary>
    DohFailed,
    /// <summary>Không tổ hợp nào qua đủ 4 host.</summary>
    NothingWorked,
}

public enum TuneStage { MeasuringDoh, Classifying, TryingSplit }

/// <summary>Index/Total chỉ có nghĩa ở TryingSplit (1-based).</summary>
public sealed record TuneProgress(TuneStage Stage, int Index, int Total, TuneCandidate? Candidate);

/// <param name="Doh">Máy DoH đã chọn; null khi DohFailed.</param>
/// <param name="Candidate">Tổ hợp để áp: tìm được (Found) hoặc đang dùng (SplitNotNeeded); null khi DohFailed/NothingWorked.</param>
/// <param name="Probe">Kết quả bước phân loại, cho khối kết quả Kiểm tra; rỗng khi DohFailed.</param>
/// <param name="FailingHosts">NothingWorked: host còn lỗi ở tổ hợp qua được nhiều host nhất.</param>
public sealed record TuneResult(
    TuneOutcome Outcome,
    DohProvider? Doh,
    TuneCandidate? Candidate,
    ProbeRemedies Remedies,
    IReadOnlyList<ProbeResult> Probe,
    IReadOnlyList<string> FailingHosts);
```

- [ ] **Step 2: Viết test hỏng**

`tests/LunaDns.Core.Tests/Probing/NetworkTunerTests.cs`:

```csharp
using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Tests.Dns;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Probing;

public class NetworkTunerTests
{
    private static readonly string[] Hosts = ["a.example", "b.example", "c.example", "d.example"];
    private static readonly IPAddress Real = IPAddress.Parse("23.50.60.70");
    private static readonly TuneCandidate Current = TuneCandidate.Default;

    private static readonly IReadOnlyList<DohChoice> Choices =
    [
        new(DohProvider.Cloudflare, DohServer.Cloudflare),
        new(DohProvider.Quad9, DohCatalog.Get(DohProvider.Quad9)),
    ];

    private readonly FakeResolver _system = new() { Respond = _ => [Real] };
    private readonly FakeResolver _doh = new() { Respond = _ => [Real] };
    private readonly List<(TuneCandidate Candidate, string Host, bool Split)> _calls = [];
    private readonly List<DohServer> _dohServers = [];

    /// <summary>Bắt tay nguyên vẹn (split=false) theo IntactOk; cắt gói theo Passes(tổ hợp, host).</summary>
    private Func<string, bool> IntactOk { get; set; } = _ => true;

    private Func<TuneCandidate, string, bool> Passes { get; set; } = (_, _) => true;

    private IReadOnlyList<DohLatency> Latencies { get; set; } =
    [
        new(DohProvider.Quad9, TimeSpan.FromMilliseconds(12), null),
        new(DohProvider.Cloudflare, TimeSpan.FromMilliseconds(30), null),
    ];

    private NetworkTuner Create() => new(
        (_, _) => Task.FromResult(Latencies),
        _system,
        server =>
        {
            _dohServers.Add(server);
            return _doh;
        },
        candidate => new FakeTlsProbe
        {
            RespondAsync = (_, host, split, _) =>
            {
                lock (_calls) _calls.Add((candidate, host, split));
                return Task.FromResult(split ? Passes(candidate, host) : IntactOk(host));
            },
        },
        checkTimeout: TimeSpan.FromSeconds(1));

    private Task<TuneResult> Run(IProgress<TuneProgress>? progress = null, CancellationToken token = default) =>
        Create().RunAsync(Hosts, Choices, Current, progress, token);

    private int SplitCalls(TuneCandidate candidate)
    {
        lock (_calls) return _calls.Count(c => c.Split && c.Candidate == candidate);
    }

    [Fact]
    public async Task Picks_fastest_doh_and_skips_split_when_nothing_is_filtered()
    {
        var result = await Run();

        Assert.Equal(TuneOutcome.SplitNotNeeded, result.Outcome);
        Assert.Equal(DohProvider.Quad9, result.Doh);
        Assert.Equal(Current, result.Candidate);
        Assert.Equal(ProbeRemedies.None, result.Remedies);
        Assert.Equal(DohCatalog.Get(DohProvider.Quad9), Assert.Single(_dohServers));
        Assert.Equal(4, result.Probe.Count);
    }

    [Fact]
    public async Task Poisoned_dns_only_recommends_dns_routing()
    {
        _system.Respond = _ => []; // DNS hệ thống không trả, DoH bắt tay được: Prober kết luận DnsPoisoned

        var result = await Run();

        Assert.Equal(TuneOutcome.SplitNotNeeded, result.Outcome);
        Assert.Equal(ProbeRemedies.DnsRouting, result.Remedies);
    }

    [Fact]
    public async Task All_doh_failing_stops_before_probing()
    {
        Latencies = [new(DohProvider.Quad9, null, "lỗi"), new(DohProvider.Cloudflare, null, "lỗi")];

        var result = await Run();

        Assert.Equal(TuneOutcome.DohFailed, result.Outcome);
        Assert.Null(result.Doh);
        Assert.Empty(result.Probe);
        Assert.Empty(_system.Hosts);
    }

    [Fact]
    public async Task Stops_at_first_candidate_that_passes_every_host()
    {
        IntactOk = _ => false; // mọi host bị lọc SNI
        var winner = TuneCandidates.Table[2]; // giữa SNI, 0 ms
        Passes = (candidate, _) => candidate == winner;

        var result = await Run();

        Assert.Equal(TuneOutcome.Found, result.Outcome);
        Assert.Equal(winner, result.Candidate);
        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, result.Remedies);
        Assert.Equal(0, SplitCalls(TuneCandidates.Table[3])); // không thử tổ hợp sau tổ hợp thắng
        Assert.Equal(4, SplitCalls(winner));
    }

    [Fact]
    public async Task One_failing_host_fails_the_candidate()
    {
        IntactOk = _ => false;
        Passes = (candidate, host) => host != "d.example" || candidate == TuneCandidates.Table[7];

        var result = await Run();

        Assert.Equal(TuneOutcome.Found, result.Outcome);
        Assert.Equal(TuneCandidates.Table[7], result.Candidate);
    }

    [Fact]
    public async Task Found_marks_unreachable_hosts_as_sni_filtered()
    {
        IntactOk = _ => false;
        // Tổ hợp đang dùng không qua: Prober kết luận Unreachable; tổ hợp thứ hai qua hết.
        Passes = (candidate, _) => candidate != Current;

        var result = await Run();

        Assert.Equal(TuneOutcome.Found, result.Outcome);
        Assert.All(result.Probe, r => Assert.Equal(ProbeVerdict.SniFiltered, r.Verdict));
    }

    [Fact]
    public async Task Nothing_working_reports_hosts_of_best_candidate()
    {
        IntactOk = _ => false;
        Passes = (candidate, host) => candidate == TuneCandidates.Table[4] && host is "a.example" or "b.example";

        var result = await Run();

        Assert.Equal(TuneOutcome.NothingWorked, result.Outcome);
        Assert.Null(result.Candidate);
        Assert.Equal(new[] { "c.example", "d.example" }, result.FailingHosts);
        Assert.Equal(ProbeRemedies.None, result.Remedies);
    }

    [Fact]
    public async Task Host_without_doh_address_never_passes()
    {
        _doh.Respond = host => host == "d.example" ? [] : [Real];
        IntactOk = _ => false;

        var result = await Run();

        Assert.Equal(TuneOutcome.NothingWorked, result.Outcome);
        Assert.Equal(new[] { "d.example" }, result.FailingHosts);
    }

    [Fact]
    public async Task Cancellation_throws_instead_of_returning_a_result()
    {
        IntactOk = _ => false;
        using var cts = new CancellationTokenSource();
        var tuner = new NetworkTuner(
            (_, _) => Task.FromResult(Latencies), _system, _ => _doh,
            _ => new FakeTlsProbe
            {
                RespondAsync = async (_, _, split, token) =>
                {
                    if (!split) return false;
                    await cts.CancelAsync();
                    await Task.Delay(Timeout.Infinite, token);
                    return true;
                },
            },
            checkTimeout: TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tuner.RunAsync(Hosts, Choices, Current, null, cts.Token));
    }

    [Fact]
    public async Task Progress_reports_stages_in_order()
    {
        IntactOk = _ => false;
        Passes = (candidate, _) => candidate == TuneCandidates.Table[1];
        var seen = new List<TuneProgress>();
        var progress = new SyncProgress(seen.Add);

        await Run(progress);

        Assert.Equal(TuneStage.MeasuringDoh, seen[0].Stage);
        Assert.Equal(TuneStage.Classifying, seen[1].Stage);
        Assert.Equal(new[] { 1, 2 }, seen.Skip(2).Select(p => p.Index));
        Assert.All(seen.Skip(2), p => Assert.Equal(TuneCandidates.MaxCount, p.Total));
    }

    /// <summary>Progress&lt;T&gt; gọi qua SynchronizationContext; test cần gọi ngay.</summary>
    private sealed class SyncProgress(Action<TuneProgress> report) : IProgress<TuneProgress>
    {
        public void Report(TuneProgress value) => report(value);
    }
}
```

Ghi chú: `Prober` dùng cùng `ITlsProbe` của tổ hợp đang dùng cho cả bắt tay nguyên vẹn và cắt gói, nên `_calls` có cả lượt của bước phân loại (với `Current`). Các test đếm lượt chỉ đếm tổ hợp khác `Current`.

- [ ] **Step 3: Chạy, thấy hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~NetworkTunerTests"`
Expected: lỗi build, `NetworkTuner` chưa có.

- [ ] **Step 4: Viết `NetworkTuner.cs`**

```csharp
using LunaDns.Core.Dns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Probing;

/// <summary>
/// D3 "Tìm cấu hình hợp mạng": chọn máy DoH nhanh nhất, phân loại các host bằng Prober, rồi thử lần lượt các tổ hợp cắt gói
/// (TuneCandidates) tới khi mọi host bắt tay được. Không tự lưu hay bật gì: người gọi áp TuneResult.
/// </summary>
public sealed partial class NetworkTuner
{
    private readonly Func<IReadOnlyList<DohChoice>, CancellationToken, Task<IReadOnlyList<DohLatency>>> _measureDoh;
    private readonly IHostResolver _system;
    private readonly Func<DohServer, IHostResolver> _dohResolver;
    private readonly Func<TuneCandidate, ITlsProbe> _tls;
    private readonly TimeSpan _checkTimeout;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;

    /// <param name="dohResolver">Resolver chỉ gồm một máy DoH (không rơi sang máy dự phòng); người gọi giữ và giải phóng.</param>
    public NetworkTuner(
        Func<IReadOnlyList<DohChoice>, CancellationToken, Task<IReadOnlyList<DohLatency>>> measureDoh,
        IHostResolver systemResolver,
        Func<DohServer, IHostResolver> dohResolver,
        Func<TuneCandidate, ITlsProbe> tls,
        TimeSpan? checkTimeout = null,
        ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(measureDoh);
        ArgumentNullException.ThrowIfNull(systemResolver);
        ArgumentNullException.ThrowIfNull(dohResolver);
        ArgumentNullException.ThrowIfNull(tls);
        _measureDoh = measureDoh;
        _system = systemResolver;
        _dohResolver = dohResolver;
        _tls = tls;
        _checkTimeout = checkTimeout ?? Prober.DefaultCheckTimeout;
        _loggers = loggers ?? NullLoggerFactory.Instance;
        _logger = _loggers.CreateLogger<NetworkTuner>();
    }

    public async Task<TuneResult> RunAsync(
        IReadOnlyList<string> hosts, IReadOnlyList<DohChoice> choices, TuneCandidate current,
        IProgress<TuneProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(current);

        progress?.Report(new TuneProgress(TuneStage.MeasuringDoh, 0, 0, null));
        var latencies = await _measureDoh(choices, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var best = latencies.FirstOrDefault(l => l.Median is not null);
        var choice = best is null ? null : choices.FirstOrDefault(c => c.Provider == best.Provider);
        if (choice is null)
        {
            LogDohFailed(_logger);
            return new TuneResult(TuneOutcome.DohFailed, null, null, ProbeRemedies.None, [], []);
        }
        LogDohChosen(_logger, choice.Provider);

        progress?.Report(new TuneProgress(TuneStage.Classifying, 0, 0, current));
        var prober = new Prober(_system, _dohResolver(choice.Server), _tls(current), _checkTimeout, _loggers.CreateLogger<Prober>());
        var probe = await prober.RunAsync(hosts, cancellationToken).ConfigureAwait(false);
        if (probe.All(r => r.Verdict is ProbeVerdict.Ok or ProbeVerdict.DnsPoisoned))
            return new TuneResult(TuneOutcome.SplitNotNeeded, choice.Provider, current, ProbeAdvisor.Recommend(probe), probe, []);

        var candidates = TuneCandidates.Ordered(current);
        IReadOnlyList<string> fewest = [.. probe.Select(r => r.Host)];
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            progress?.Report(new TuneProgress(TuneStage.TryingSplit, i + 1, candidates.Count, candidate));
            var failing = await FailingHostsAsync(probe, candidate, cancellationToken).ConfigureAwait(false);
            LogCandidate(_logger, candidate, failing.Count);
            if (failing.Count == 0)
                return new TuneResult(TuneOutcome.Found, choice.Provider, candidate,
                    ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, [.. probe.Select(Helped)], []);
            if (failing.Count < fewest.Count) fewest = failing;
        }
        return new TuneResult(TuneOutcome.NothingWorked, choice.Provider, null, ProbeRemedies.None, probe, fewest);
    }

    private async Task<IReadOnlyList<string>> FailingHostsAsync(
        IReadOnlyList<ProbeResult> probe, TuneCandidate candidate, CancellationToken cancellationToken)
    {
        var tls = _tls(candidate);
        var passed = await Task.WhenAll(probe.Select(r => PassesAsync(tls, r, cancellationToken))).ConfigureAwait(false);
        return [.. probe.Where((_, i) => !passed[i]).Select(r => r.Host)];
    }

    private async Task<bool> PassesAsync(ITlsProbe tls, ProbeResult result, CancellationToken cancellationToken)
    {
        if (result.DohAddresses.Count == 0) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_checkTimeout);
        try
        {
            return await tls.HandshakeAsync(result.DohAddresses[0], result.Host, split: true, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Bắt tay nguyên vẹn lỗi nhưng tổ hợp tìm được qua: đó là lọc SNI, không phải "không vào được".</summary>
    private static ProbeResult Helped(ProbeResult result) =>
        result.Verdict == ProbeVerdict.Unreachable && result.DohAddresses.Count > 0
            ? result with { Verdict = ProbeVerdict.SniFiltered }
            : result;

    [LoggerMessage(Level = LogLevel.Information, Message = "Dò cấu hình: không máy DoH nào trả lời")]
    private static partial void LogDohFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dò cấu hình: dùng DoH {Provider}")]
    private static partial void LogDohChosen(ILogger logger, DohProvider provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dò cấu hình: {Candidate} còn {Failing} host lỗi")]
    private static partial void LogCandidate(ILogger logger, TuneCandidate candidate, int failing);
}
```

- [ ] **Step 5: Chạy, thấy qua**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~NetworkTunerTests|FullyQualifiedName~ProberTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.Core/Probing/TuneResult.cs src/LunaDns.Core/Probing/NetworkTuner.cs tests/LunaDns.Core.Tests/Probing/NetworkTunerTests.cs
git commit -m "feat(probe): add NetworkTuner that finds a working DoH and split combination"
```

---

### Task 4: `FeatureSet.ReapplyTuningAsync`

**Files:**
- Modify: `src/LunaDns.Core/Features/FeatureSet.cs` (cạnh `ReapplyAsync`, khoảng dòng 82–106)
- Test: `tests/LunaDns.Core.Tests/Features/FeatureSetReapplyTests.cs`

**Interfaces:**
- Consumes: `RestartAsync(IReadOnlyList<Feature>)` (private, có sẵn), `_gate`, `_running`, `ReapplyResult(bool Success, bool PreviousRestored, FeatureError Error)`.
- Produces: `public Task<ReapplyResult> ReapplyTuningAsync(CancellationToken cancellationToken = default)`. Chạy lại mọi tính năng đang bật (DnsRouting, SniProxy, PubgMode) để chúng đọc `_tuning()` và DoH mới. Lỗi thì `Success = false`, `PreviousRestored = false`, không tự quay lại.

- [ ] **Step 1: Viết test hỏng**

Thêm vào `FeatureSetReapplyTests`:

```csharp
    [Fact]
    public async Task Reapply_tuning_restarts_running_features_with_new_tuning()
    {
        var tuning = FeatureTuning.Default;
        await using var features = new FeatureSet(_journal, new SystemChanges(_journal, _configurator), _services,
            () => tuning, routedDomains: Steam);
        await features.EnableAsync(Feature.SniProxy);
        tuning = new FeatureTuning(new LunaDns.Core.Tls.HelloSplit(LunaDns.Core.Tls.SplitMode.MidSni, true), TimeSpan.FromMilliseconds(10));

        var result = await features.ReapplyTuningAsync();

        Assert.True(result.Success);
        Assert.Equal(2, _services.Starts(Feature.SniProxy));
        Assert.Equal(tuning, _services.LastTuning);
        Assert.True(features.IsEnabled(Feature.SniProxy));
    }

    [Fact]
    public async Task Reapply_tuning_with_nothing_running_does_nothing()
    {
        var result = await _features.ReapplyTuningAsync();

        Assert.True(result.Success);
        Assert.Equal(0, _services.Starts(Feature.DnsRouting));
        Assert.Equal(0, _services.Starts(Feature.SniProxy));
    }

    [Fact]
    public async Task Reapply_tuning_failure_is_reported_without_rollback()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _services.FailStart(Feature.DnsRouting, new DnsStubException("giả lập cổng bận"), once: true);

        var result = await _features.ReapplyTuningAsync();

        Assert.False(result.Success);
        Assert.False(result.PreviousRestored);
        Assert.NotEqual(FeatureError.None, result.Error);
    }
```

(`FeatureTuning` là record `(HelloSplit Split, TimeSpan SplitDelay)`, nên so bằng giá trị.)

- [ ] **Step 2: Chạy, thấy hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~FeatureSetReapplyTests"`
Expected: lỗi build, `ReapplyTuningAsync` chưa có.

- [ ] **Step 3: Viết hàm**

Ngay sau `ReapplyAsync`:

```csharp
    /// <summary>
    /// D3: chạy lại mọi tính năng đang bật để chúng đọc tuning và máy DoH mới (dịch vụ chỉ đọc lúc khởi động).
    /// Lỗi thì không tự quay lại: chỉ người gọi biết settings cũ, nên người gọi đặt lại settings rồi gọi lần nữa.
    /// </summary>
    public async Task<ReapplyResult> ReapplyTuningAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = Enum.GetValues<Feature>().Where(_running.ContainsKey).ToList();
            var failure = await RestartAsync(active).ConfigureAwait(false);
            if (failure is null) return new ReapplyResult(true, false);
            LogReapplyTuningFailed(_logger, failure.Feature, failure.Error);
            return new ReapplyResult(false, false, failure.Error);
        }
        finally
        {
            _gate.Release();
        }
    }
```

Cuối lớp, cạnh các `LoggerMessage` khác:

```csharp
    [LoggerMessage(Level = LogLevel.Warning, Message = "Không chạy lại được {Feature} với cấu hình cắt gói mới ({Error})")]
    private static partial void LogReapplyTuningFailed(ILogger logger, Feature feature, FeatureError error);
```

- [ ] **Step 4: Chạy, thấy qua**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~FeatureSet"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Features/FeatureSet.cs tests/LunaDns.Core.Tests/Features/FeatureSetReapplyTests.cs
git commit -m "feat(features): restart running features to pick up new tuning"
```

---

### Task 5: Daemon macOS: `SettingsHolder`, `get-tuning`, `set-tuning`

**Files:**
- Create: `src/LunaDns.Core/Settings/SettingsHolder.cs`
- Modify: `src/LunaDns.Platform.MacOS/Daemon/DaemonProtocol.cs`, `src/LunaDns.Platform.MacOS/Daemon/DaemonCommands.cs`, `src/LunaDns.MacCli/Engine.cs`, `src/LunaDns.MacCli/Program.cs` (dòng 196, 236)
- Test: `tests/LunaDns.Core.Tests/Settings/SettingsHolderTests.cs` (mới), `tests/LunaDns.Platform.MacOS.Tests/DaemonTests.cs`

**Interfaces:**
- Consumes: `AppSettings.WithTuning`, `CurrentCandidate`, `EffectiveDoh` (Task 2); `FeatureSet.ReapplyTuningAsync` (Task 4); `JsonFileSettingsStore.Load/Save/DroppedDomains`.
- Produces:
  - `public sealed class SettingsHolder(JsonFileSettingsStore store)`, với `AppSettings Current { get; set; }`, `void Save(AppSettings)` (ghi file rồi mới đổi `Current`) và `int DroppedDomains`;
  - `DaemonProtocol.GetTuning = "get-tuning"`, `DaemonProtocol.SetTuning = "set-tuning"`;
  - `public sealed record DaemonTuning(DohProvider Doh, SplitMode SplitMode, bool SplitRecords, int SplitDelayMs)`, với `static From(AppSettings)`, `static From(DohProvider, TuneCandidate)` và `TuneCandidate ToCandidate()`;
  - `DaemonRequest` và `DaemonResponse` thêm tham số cuối `DaemonTuning? Tuning = null`;
  - `DaemonCommands(FeatureSet, ChangeJournal, string version, SettingsHolder settings, ILogger? logger = null)`. Giữ constructor cũ nhận `JsonFileSettingsStore`, bọc lại;
  - `Engine.Settings` (`SettingsHolder`) thay `Engine.SettingsStore`.

- [ ] **Step 1: Viết test hỏng cho `SettingsHolder`**

`tests/LunaDns.Core.Tests/Settings/SettingsHolderTests.cs`:

```csharp
using LunaDns.Core.Settings;

namespace LunaDns.Core.Tests.Settings;

public sealed class SettingsHolderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-holder-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Loads_once_and_save_updates_file_and_current()
    {
        var store = new JsonFileSettingsStore(Path.Combine(_dir, "settings.json"));
        var holder = new SettingsHolder(store);
        Assert.Equal(0, holder.Current.SplitDelayMs);

        holder.Save(holder.Current with { SplitDelayMs = 10 });

        Assert.Equal(10, holder.Current.SplitDelayMs);
        Assert.Equal(10, store.Load().SplitDelayMs);
    }

    [Fact]
    public void Failed_save_keeps_current()
    {
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(path); // settings.json là thư mục: ghi sẽ lỗi
        var holder = new SettingsHolder(new JsonFileSettingsStore(path));

        Assert.ThrowsAny<IOException>(() => holder.Save(holder.Current with { SplitDelayMs = 10 }));
        Assert.Equal(0, holder.Current.SplitDelayMs);
    }
}
```

(Nếu `Save` vào thư mục ném `UnauthorizedAccessException` trên máy chạy test, đổi `Assert.ThrowsAny<IOException>` thành `Assert.ThrowsAny<Exception>` và kiểm kiểu là một trong hai.)

- [ ] **Step 2: Viết `SettingsHolder.cs`**

```csharp
namespace LunaDns.Core.Settings;

/// <summary>
/// settings.json của daemon macOS, dùng chung giữa Engine (DoH, tuning lúc khởi động dịch vụ) và DaemonCommands
/// (set-domains, set-tuning), để lệnh này không ghi đè trường của lệnh kia. Nạp một lần lúc tạo.
/// </summary>
public sealed class SettingsHolder
{
    private readonly JsonFileSettingsStore _store;
    private volatile AppSettings _current;

    public SettingsHolder(JsonFileSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _current = store.Load();
        DroppedDomains = store.DroppedDomains;
    }

    /// <summary>Đặt tạm (chưa ghi file) để dịch vụ khởi động lại đọc giá trị mới; Save mới ghi.</summary>
    public AppSettings Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Số dòng tên miền bị bỏ lúc nạp (để log).</summary>
    public int DroppedDomains { get; }

    /// <summary>Ghi file rồi mới đổi Current; ghi lỗi thì ném và Current giữ nguyên.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _store.Save(settings);
        _current = settings;
    }
}
```

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SettingsHolderTests"`. Expected: PASS.

- [ ] **Step 3: Viết test hỏng cho daemon**

Thêm vào `DaemonCommandsTests` trong `tests/LunaDns.Platform.MacOS.Tests/DaemonTests.cs` (thêm `using LunaDns.Core.Dns;`, `using LunaDns.Core.Probing;`, `using LunaDns.Core.Tls;` nếu thiếu):

```csharp
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
```

`Protocol_wire_format_is_stable` (đã có) phải vẫn qua mà không sửa: `Tuning = null` bị bỏ khi ghi.

- [ ] **Step 4: Chạy, thấy hỏng**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests --filter "FullyQualifiedName~DaemonCommandsTests|FullyQualifiedName~Protocol"`
Expected: lỗi build, `DaemonProtocol.GetTuning` chưa có. Project này chỉ chạy trên macOS.

- [ ] **Step 5: Sửa `DaemonProtocol.cs`**

Thêm `using LunaDns.Core.Dns;`, `using LunaDns.Core.Probing;`, `using LunaDns.Core.Settings;`, `using LunaDns.Core.Tls;`. Cập nhật chú thích danh sách lệnh, thêm `get-tuning`, `set-tuning` (kèm Tuning). Thêm hằng:

```csharp
    public const string GetTuning = "get-tuning";
    public const string SetTuning = "set-tuning";
```

Trong `CreateJson`, thêm converter:

```csharp
            Converters =
            {
                new JsonStringEnumConverter<DomainGroup>(JsonNamingPolicy.CamelCase),
                new JsonStringEnumConverter<DohProvider>(JsonNamingPolicy.CamelCase),
                new JsonStringEnumConverter<SplitMode>(JsonNamingPolicy.CamelCase),
            },
```

Context: thêm `[JsonSerializable(typeof(DaemonTuning))]`. Record:

```csharp
public sealed record DaemonRequest(string Command, string? Feature = null, bool AllowProxyOverride = false, DomainSelection? Domains = null,
    DaemonTuning? Tuning = null);

public sealed record DaemonResponse(
    bool Ok,
    string? Message = null,
    string? Version = null,
    DaemonStatus? Status = null,
    string? Outcome = null,
    string? Error = null,
    DomainSelection? Domains = null,
    DaemonTuning? Tuning = null);

/// <summary>Máy DoH và tổ hợp cắt gói của daemon (D3). Enum dạng chuỗi camelCase ("quad9", "midSni").</summary>
public sealed record DaemonTuning(DohProvider Doh, SplitMode SplitMode, bool SplitRecords, int SplitDelayMs)
{
    public static DaemonTuning From(DohProvider doh, TuneCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new(doh, candidate.Split.Mode, candidate.Split.Records, candidate.DelayMs);
    }

    public static DaemonTuning From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return From(settings.EffectiveDoh(), settings.CurrentCandidate());
    }

    /// <summary>Hàm, không phải thuộc tính, để không ra JSON.</summary>
    public TuneCandidate ToCandidate() => new(new HelloSplit(SplitMode, SplitRecords), SplitDelayMs);
}
```

- [ ] **Step 6: Sửa `DaemonCommands.cs`**

Đổi trường và constructor:

```csharp
    private readonly FeatureSet features;
    private readonly ChangeJournal journal;
    private readonly string version;
    private readonly SettingsHolder _settings;

    public DaemonCommands(FeatureSet features, ChangeJournal journal, string version, JsonFileSettingsStore settingsStore, ILogger? logger = null)
        : this(features, journal, version, new SettingsHolder(settingsStore ?? throw new ArgumentNullException(nameof(settingsStore))), logger)
    {
    }

    /// <summary>Daemon thật: dùng chung SettingsHolder với Engine, để set-tuning đổi được DoH/tuning lúc dịch vụ khởi động lại.</summary>
    public DaemonCommands(FeatureSet features, ChangeJournal journal, string version, SettingsHolder settings, ILogger? logger = null)
    {
        this.features = features;
        this.journal = journal;
        this.version = version;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        // Daemon là bên giữ settings.json trên macOS: dòng bị bỏ lúc nạp phải để lại dấu (spec mục 7).
        if (settings.DroppedDomains > 0) LogDroppedDomains(logger ?? NullLogger.Instance, settings.DroppedDomains);
    }
```

Trong `HandleAsync`:

```csharp
            case DaemonProtocol.GetDomains:
                return Ok() with { Domains = _settings.Current.Domains };
            case DaemonProtocol.SetDomains:
                return await SetDomainsAsync(request.Domains, cancellationToken).ConfigureAwait(false);
            case DaemonProtocol.GetTuning:
                return Ok() with { Tuning = DaemonTuning.From(_settings.Current) };
            case DaemonProtocol.SetTuning:
                return await SetTuningAsync(request.Tuning, cancellationToken).ConfigureAwait(false);
```

Trong `SetDomainsAsync`:
- `var updated = _settings with { Domains = wanted };` thành `var updated = _settings.Current with { Domains = wanted };`;
- `settingsStore.Save(updated);` thành `_settings.Save(updated);`;
- nhánh catch dùng `_settings.Current.Domains`;
- bỏ dòng `_settings = updated;`.

Thêm:

```csharp
    /// <summary>
    /// D3. Không tin dữ liệu từ giao diện: kiểm, đặt tạm settings mới để dịch vụ khởi động lại đọc được, chạy lại, rồi mới ghi file.
    /// Chạy lại hay ghi file lỗi thì đặt lại settings cũ và chạy lại lần nữa.
    /// </summary>
    private async Task<DaemonResponse> SetTuningAsync(DaemonTuning? wanted, CancellationToken cancellationToken)
    {
        if (wanted is null) return Refused("Thiếu cấu hình cắt gói.");
        if (DescribeTuning(wanted) is { } error) return Refused(error);
        var previous = _settings.Current;
        var updated = previous.WithTuning(wanted.Doh, wanted.ToCandidate());
        _settings.Current = updated;
        var result = await features.ReapplyTuningAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            var restored = await RestoreTuningAsync(previous).ConfigureAwait(false);
            return Ok() with
            {
                Ok = false,
                Error = result.Error.ToString(),
                Message = restored
                    ? $"Không áp được cấu hình mới ({result.Error}); đã giữ cấu hình cũ."
                    : $"Không áp được cấu hình mới ({result.Error}) và không khôi phục được; chạy: sudo lunadns cleanup",
            };
        }
        try
        {
            _settings.Save(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var restored = await RestoreTuningAsync(previous).ConfigureAwait(false);
            return Ok() with
            {
                Ok = false,
                Message = restored
                    ? $"Không lưu được settings.json ({ex.Message}); đã giữ cấu hình cũ."
                    : $"Không lưu được settings.json ({ex.Message}) và không quay về được cấu hình cũ; chạy: sudo lunadns cleanup",
            };
        }
        return Ok() with { Tuning = DaemonTuning.From(updated) };
    }

    private async Task<bool> RestoreTuningAsync(AppSettings previous)
    {
        _settings.Current = previous;
        return (await features.ReapplyTuningAsync(CancellationToken.None).ConfigureAwait(false)).Success;
    }

    /// <summary>macOS không có ô Tự nhập DoH, nên không nhận Custom.</summary>
    private static string? DescribeTuning(DaemonTuning tuning) =>
        !Enum.IsDefined(tuning.Doh) || tuning.Doh == DohProvider.Custom ? "Máy chủ DoH không hợp lệ."
        : !Enum.IsDefined(tuning.SplitMode) ? "Điểm cắt gói không hợp lệ."
        : tuning.SplitDelayMs is < 0 or > AppSettings.MaxSplitDelayMs ? $"Độ trễ phải trong 0–{AppSettings.MaxSplitDelayMs} ms."
        : null;
```

Thêm `using LunaDns.Core.Dns;`. Sửa chú thích đầu lớp: giao diện đọc/ghi danh sách tên miền và cấu hình cắt gói qua `get-domains`/`set-domains`/`get-tuning`/`set-tuning`.

- [ ] **Step 7: Sửa `Engine.cs` và `Program.cs`**

`Engine` constructor:

```csharp
        Settings = new SettingsHolder(new JsonFileSettingsStore(paths.SettingsFile));
        var services = new CoreFeatureServices(() => Settings.Current.DohServers(), loggers,
            dnsEndPoint: new IPEndPoint(IPAddress.Loopback, configurator.Dns.Port));
        Features = new FeatureSet(Journal, new SystemChanges(Journal, configurator), services,
            () => Settings.Current.Tuning(), logger: loggers.CreateLogger<FeatureSet>(),
            routedDomains: FeatureDomains.Routed(Settings.Current.Domains));
```

Thay thuộc tính `SettingsStore` bằng:

```csharp
    /// <summary>settings.json của daemon, dùng chung với DaemonCommands (set-domains, set-tuning).</summary>
    public SettingsHolder Settings { get; }
```

`Program.cs` dòng 196 và 236: `engine.SettingsStore` thành `engine.Settings`. Rồi chạy `grep -rn "SettingsStore" src/LunaDns.MacCli`, kết quả phải rỗng.

- [ ] **Step 8: Chạy, thấy qua**

Run:

```bash
dotnet build LunaDns.sln -c Release -warnaserror
dotnet test tests/LunaDns.Platform.MacOS.Tests
dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SettingsHolderTests"
```

Expected: build sạch. Mọi test daemon qua, kể cả `Protocol_wire_format_is_stable` cũ.

- [ ] **Step 9: Commit**

```bash
git add src/LunaDns.Core/Settings/SettingsHolder.cs src/LunaDns.Platform.MacOS/Daemon src/LunaDns.MacCli tests/LunaDns.Core.Tests/Settings/SettingsHolderTests.cs tests/LunaDns.Platform.MacOS.Tests/DaemonTests.cs
git commit -m "feat(macos): add get-tuning and set-tuning daemon commands"
```

---

### Task 6: `IFeatureController` và `AppServices`: dò và áp

**Files:**
- Create: `src/LunaDns.App/Features/TuningApplyResult.cs`
- Modify:
  - `src/LunaDns.App/Features/IFeatureController.cs`
  - `src/LunaDns.App/Features/LocalFeatureController.cs`
  - `src/LunaDns.App/MacOS/DaemonFeatureController.cs`
  - `src/LunaDns.App/AppServices.cs`
  - `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs` (chỉ thêm `DaemonNeedsUpdateTuning`)
- Test:
  - `tests/LunaDns.App.Tests/AppServicesTests.cs`
  - `tests/LunaDns.App.Tests/MacOS/DaemonFeatureControllerTests.cs`
  - `tests/LunaDns.App.Tests/StringsTests.cs`

**Interfaces:**
- Consumes:
  - `NetworkTuner`, `TuneResult`, `TuneProgress` (Task 3);
  - `TuningState`, `TuneCandidate` (Task 2);
  - `DaemonProtocol.GetTuning/SetTuning`, `DaemonTuning` (Task 5);
  - `FeatureSet.ReapplyTuningAsync` (Task 4);
  - `DohLatencyMeter.Create(ILoggerFactory).MeasureAsync(choices, TimeSpan, ct)`;
  - `DohClient.Create(IReadOnlyList<DohServer>, ILogger<DohClient>)`;
  - `ProbeSummary.Create(results, DateTimeOffset, Func<string, DomainGroup>)`.
- Produces:
  - `internal sealed record TuningApplyResult(bool Success, string? Message, FeatureError? Error = null)`;
  - trên `IFeatureController`: `Task<TuningState?> GetTuningAsync()`, `Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate)`;
  - `AppServices.TuneAsync(IProgress<TuneProgress>? progress, CancellationToken) : Task<TuneRun>`;
  - `AppServices.ApplyTuningAsync(DohProvider, TuneCandidate) : Task<TuningApplyResult>`;
  - `public sealed record TuneRun(TuneResult Result, TuningState? Current, CustomDoh? ResolvedCustom, ProbeSummary? Summary)`.

- [ ] **Step 1: Viết test hỏng**

Trong `tests/LunaDns.App.Tests/MacOS/DaemonFeatureControllerTests.cs`:

```csharp
    [Fact]
    public async Task Get_tuning_reads_daemon_and_is_null_for_old_daemon()
    {
        var tuning = new DaemonTuning(DohProvider.Quad9, SplitMode.MidSni, true, 10);
        _daemon.Reply = r => r.Command == DaemonProtocol.GetTuning ? new DaemonResponse(true, Tuning: tuning) : null;
        await using var controller = Create();

        Assert.Equal(new TuningState(DohProvider.Quad9, tuning.ToCandidate()), await controller.GetTuningAsync());

        _daemon.Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}");
        Assert.Null(await controller.GetTuningAsync());
    }

    [Fact]
    public async Task Apply_tuning_sends_set_tuning_and_maps_old_daemon_reply()
    {
        _daemon.Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}");
        await using var controller = Create();
        var candidate = new TuneCandidate(new HelloSplit(SplitMode.MidSni, true), 0);

        var result = await controller.ApplyTuningAsync(DohProvider.Quad9, candidate);

        Assert.Equal(DaemonProtocol.SetTuning, _daemon.LastRequest!.Command);
        Assert.Equal(DaemonTuning.From(DohProvider.Quad9, candidate), _daemon.LastRequest.Tuning);
        Assert.False(result.Success);
        Assert.Equal(Strings.DaemonNeedsUpdateTuning, result.Message);
    }

    [Fact]
    public async Task Apply_tuning_succeeds_or_reports_service_unavailable()
    {
        await using var controller = Create();
        Assert.True((await controller.ApplyTuningAsync(DohProvider.Google, TuneCandidate.Default)).Success);

        _daemon.Down = true;
        var down = await controller.ApplyTuningAsync(DohProvider.Google, TuneCandidate.Default);
        Assert.False(down.Success);
        Assert.Equal(ErrorMessages.For(FeatureError.ServiceUnavailable), down.Message);
    }
```

`FakeDaemon` mặc định trả `Ok()` cho lệnh lạ. Nếu `switch` của nó có `default` ném lỗi, thêm hai nhánh `case DaemonProtocol.SetTuning: response = Ok(); break;` và `case DaemonProtocol.GetTuning: response = Ok(); break;`.

Trong `tests/LunaDns.App.Tests/AppServicesTests.cs` (dùng `AppServices` thật như các test hiện có, với `_app.Paths`, `_app.Configurator`):

```csharp
    [Fact]
    public async Task Apply_tuning_locally_saves_settings_and_reports_current_tuning()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);
        var candidate = new TuneCandidate(new HelloSplit(SplitMode.MidSni, true), 10);

        var result = await services.ApplyTuningAsync(DohProvider.Quad9, candidate);

        Assert.True(result.Success);
        Assert.Equal(DohProvider.Quad9, services.Settings.EffectiveDoh());
        Assert.Equal(candidate, services.Settings.CurrentCandidate());
        Assert.Equal(new TuningState(DohProvider.Quad9, candidate), await services.Features.GetTuningAsync());
    }
```

`SelectionOnlyController` trong cùng file thêm hai hàm:

```csharp
        public Task<TuningState?> GetTuningAsync() => Task.FromResult<TuningState?>(null);

        public Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate) => throw new NotSupportedException();
```

Trong `StringsTests.Every_static_string_resolves`, tăng `165` thành `166`.

- [ ] **Step 2: Chạy, thấy hỏng**

Run: `dotnet test tests/LunaDns.App.Tests --filter "FullyQualifiedName~DaemonFeatureControllerTests|FullyQualifiedName~AppServicesTests|FullyQualifiedName~StringsTests"`
Expected: lỗi build.

- [ ] **Step 3: Thêm kiểu và chuỗi**

`src/LunaDns.App/Features/TuningApplyResult.cs`:

```csharp
using LunaDns.Core.Features;

namespace LunaDns.App.Features;

/// <summary>Kết quả áp cấu hình D3. Message là câu cho người dùng khi Success = false.</summary>
internal sealed record TuningApplyResult(bool Success, string? Message, FeatureError? Error = null);
```

`Strings.resx` (cạnh `DaemonNeedsUpdate`):

```xml
  <data name="DaemonNeedsUpdateTuning" xml:space="preserve"><value>Dịch vụ nền cần được cài lại để áp cấu hình mới. Bấm Cài dịch vụ nền.</value></data>
```

`Strings.cs`: `public static string DaemonNeedsUpdateTuning => Get(nameof(DaemonNeedsUpdateTuning));`

- [ ] **Step 4: `IFeatureController` và hai bản cài**

`IFeatureController` (thêm `using LunaDns.Core.Dns;`, `using LunaDns.Core.Probing;`):

```csharp
    /// <summary>Máy DoH và tổ hợp cắt gói nơi giữ thật đang dùng (D3). null nếu không hỏi được (daemon cũ hoặc không trả lời).</summary>
    Task<TuningState?> GetTuningAsync();

    /// <summary>
    /// Áp cấu hình D3 cho tính năng đang chạy. Bản daemon tự kiểm, áp và lưu. Bản Local chạy lại FeatureSet,
    /// vì AppServices đã lưu settings mới trước khi gọi.
    /// </summary>
    Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate);
```

`LocalFeatureController`:

```csharp
    public Task<TuningState?> GetTuningAsync()
    {
        var settings = _settings();
        return Task.FromResult<TuningState?>(new TuningState(settings.EffectiveDoh(), settings.CurrentCandidate()));
    }

    /// <summary>FeatureSet đọc tuning và DoH qua settings() lúc khởi động lại; AppServices đã lưu bản mới.</summary>
    public async Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate)
    {
        var result = await Features.ReapplyTuningAsync().ConfigureAwait(false);
        return result.Success ? new(true, null) : new(false, ErrorMessages.For(result.Error), result.Error);
    }
```

`DaemonFeatureController` (cạnh `ApplyDomainsAsync`):

```csharp
    public async Task<TuningState?> GetTuningAsync() =>
        await SendAsync(new DaemonRequest(DaemonProtocol.GetTuning)).ConfigureAwait(false) is { Ok: true, Tuning: { } tuning }
            ? new TuningState(tuning.Doh, tuning.ToCandidate())
            : null;

    /// <summary>Dịch vụ nền tự kiểm, áp và lưu. Bản cũ chưa biết set-tuning trả "Lệnh lạ:" nên đề nghị cài lại.</summary>
    public async Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate)
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetTuning, Tuning: DaemonTuning.From(doh, candidate))).ConfigureAwait(false);
        if (response is null) return new(false, ErrorMessages.For(FeatureError.ServiceUnavailable));
        if (response.Ok) return new(true, null);
        if (IsUnknownCommand(response)) return new(false, Strings.DaemonNeedsUpdateTuning);
        var error = Enum.TryParse<FeatureError>(response.Error, out var parsed) && Enum.IsDefined(parsed) ? parsed : (FeatureError?)null;
        return new(false, response.Message, error);
    }
```

- [ ] **Step 5: `AppServices`**

Tách phần phân giải lại Tự nhập của `MeasureDohAsync` thành:

```csharp
    /// <summary>Phân giải lại host Tự nhập (D2). Lỗi hoặc không có Tự nhập thì null; người gọi dùng IP cũ.</summary>
    private async Task<CustomDoh?> RefreshCustomDohAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var custom = Settings.CustomDoh;
        if (custom is null || !CustomDohParser.TryParseEndpoint(custom.Endpoint, out _)) return null;
        using var resolveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        resolveTimeout.CancelAfter(timeout);
        try
        {
            return await ResolveCustomDohAsync(custom, resolveTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DohException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null; // giữ IP cũ; kết quả đo cho thấy máy đó có dùng được không
        }
    }
```

`MeasureDohAsync` thành:

```csharp
    public async Task<DohMeasurement> MeasureDohAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        var resolved = await RefreshCustomDohAsync(timeout, cancellationToken).ConfigureAwait(false);
        var remaining = timeout - _time.GetElapsedTime(started);
        var results = await DohLatencyMeter.Create(LoggerFactory)
            .MeasureAsync(DohCatalog.Choices(resolved ?? Settings.CustomDoh), remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, cancellationToken)
            .ConfigureAwait(false);
        return new DohMeasurement(results, resolved);
    }
```

Thêm:

```csharp
    /// <summary>Thời gian đo DoH trong một lượt dò D3 (cả lượt tối đa 60 giây, do giao diện giữ).</summary>
    internal static readonly TimeSpan TuneDohTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// "Tìm cấu hình hợp mạng" (D3) trên 4 host Steam. Không ghi Settings (chạy ở luồng nền): trả TuneRun để giao diện áp ở
    /// luồng UI. Tổ hợp "đang dùng" lấy từ nơi giữ thật (macOS: daemon); hỏi không được thì dùng mặc định.
    /// </summary>
    public async Task<TuneRun> TuneAsync(IProgress<TuneProgress>? progress, CancellationToken cancellationToken)
    {
        var current = await Features.GetTuningAsync().ConfigureAwait(false);
        var resolved = await RefreshCustomDohAsync(TuneDohTimeout, cancellationToken).ConfigureAwait(false);
        var choices = DohCatalog.Choices(resolved ?? Settings.CustomDoh);
        var meter = DohLatencyMeter.Create(LoggerFactory);
        var clients = new List<DohClient>();
        try
        {
            var tuner = new NetworkTuner(
                (c, token) => meter.MeasureAsync(c, TuneDohTimeout, token),
                new SystemHostResolver(LoggerFactory.CreateLogger<SystemHostResolver>()),
                server =>
                {
                    var client = DohClient.Create([server], LoggerFactory.CreateLogger<DohClient>());
                    lock (clients) clients.Add(client);
                    return new DohHostResolver(client);
                },
                candidate => new TlsProbe(candidate.Split, candidate.Delay, LoggerFactory.CreateLogger<TlsProbe>()),
                loggers: LoggerFactory);
            var result = await tuner
                .RunAsync(ProbeTargets.Steam, choices, current?.Candidate ?? TuneCandidate.Default, progress, cancellationToken)
                .ConfigureAwait(false);
            ProbeSummary? summary = null;
            if (result.Probe.Count > 0)
            {
                LastProbe = result.Probe;
                summary = ProbeSummary.Create(result.Probe, _time.GetLocalNow(), _ => DomainGroup.Steam);
            }
            return new TuneRun(result, current, resolved, summary);
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    /// <summary>
    /// Windows: lưu trước (FeatureSet đọc settings lúc khởi động lại); lỗi thì lưu lại bản cũ và chạy lại lần nữa.
    /// macOS: daemon tự áp và lưu; xong mới chép vào settings.json của app để Kiểm tra dùng cùng cấu hình.
    /// </summary>
    public async Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (Features.Availability != ControllerAvailability.Local)
        {
            var remote = await Features.ApplyTuningAsync(doh, candidate).ConfigureAwait(false);
            if (remote.Success) Settings = Settings.WithTuning(doh, candidate);
            return remote;
        }
        var previous = Settings;
        Settings = previous.WithTuning(doh, candidate);
        var result = await Features.ApplyTuningAsync(doh, candidate).ConfigureAwait(false);
        if (result.Success) return result;
        Settings = previous;
        var back = await Features.ApplyTuningAsync(previous.EffectiveDoh(), previous.CurrentCandidate()).ConfigureAwait(false);
        return back.Success ? result : result with { Message = $"{result.Message} {Strings.ChangesLeftBehind}" };
    }
```

Cuối file, cạnh `DohMeasurement`:

```csharp
/// <param name="Current">Cấu hình trước khi dò; null nếu không hỏi được nơi giữ thật.</param>
/// <param name="ResolvedCustom">Tự nhập đã phân giải lại, để giao diện lưu ở luồng UI (như nút Đo).</param>
/// <param name="Summary">Kết quả phân loại nhóm Steam cho khối Kiểm tra; null khi không máy DoH nào trả lời.</param>
public sealed record TuneRun(TuneResult Result, TuningState? Current, CustomDoh? ResolvedCustom, ProbeSummary? Summary);
```

`AppServices` là `internal`, nên trả `TuningApplyResult` (internal) không vướng phạm vi. `TuneRun` để `public` theo mẫu `DohMeasurement`.

- [ ] **Step 6: Chạy, thấy qua**

Run:

```bash
dotnet build LunaDns.sln -c Release -warnaserror
dotnet test tests/LunaDns.App.Tests
```

Expected: build sạch, mọi test qua.

- [ ] **Step 7: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests
git commit -m "feat(app): wire NetworkTuner and tuning apply through feature controllers"
```

---

### Task 7: Giao diện, chuỗi, hướng dẫn

**Files:**
- Create: `src/LunaDns.App/TuneText.cs`, `tests/LunaDns.App.Tests/TuneTextTests.cs`
- Modify: `src/LunaDns.App/ViewModels/MainViewModel.cs`, `src/LunaDns.App/Views/MainWindow.axaml` (khối Kiểm tra, khoảng dòng 150–163), `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs`, `tests/LunaDns.App.Tests/StringsTests.cs`
- Modify docs: `docs/guide/04-kiem-tra-va-de-xuat.md`, `docs/guide/09-macos.md`, `docs/guide/en/04-check-and-suggestions.md`, `docs/guide/en/09-macos.md`

**Interfaces:**
- Consumes: `AppServices.TuneAsync`, `AppServices.ApplyTuningAsync`, `TuneRun` (Task 6); `TuneProgress`, `TuneOutcome` (Task 3); `RunBusyAsync`, `EnableAsync(Feature)`, `Failure`, `ShowFailures`, `ShowMessage`, `SaveSettings` (có sẵn trong `MainViewModel`).
- Produces: `TuneCommand`, `StopTuneCommand`, `IsTuning`, `TuneStatus`, `HasTuneStatus` cho XAML; `TuneText.Candidate/Progress/Chosen` (internal static).

- [ ] **Step 1: Chuỗi mới**

`Strings.resx` (sau `ApplyRecommendedButton`):

```xml
  <data name="TuneButton" xml:space="preserve"><value>Tìm cấu hình hợp mạng</value></data>
  <data name="TuneStop" xml:space="preserve"><value>Dừng</value></data>
  <data name="TuneMeasuringDoh" xml:space="preserve"><value>Đang đo máy chủ DoH…</value></data>
  <data name="TuneClassifying" xml:space="preserve"><value>Đang kiểm tra Steam…</value></data>
  <data name="TuneTryingFormat" xml:space="preserve"><value>Đang thử {0}/{1}: {2}</value></data>
  <data name="TuneCandidateFormat" xml:space="preserve"><value>{0} · {1} · {2} ms</value></data>
  <data name="TuneSplitFirstByte" xml:space="preserve"><value>sau byte đầu</value></data>
  <data name="TuneSplitMidSni" xml:space="preserve"><value>giữa SNI</value></data>
  <data name="TuneRecordsOn" xml:space="preserve"><value>tách record</value></data>
  <data name="TuneRecordsOff" xml:space="preserve"><value>một record</value></data>
  <data name="TuneChosenFormat" xml:space="preserve"><value>Đã chọn {0} · {1}</value></data>
  <data name="TuneNoChange" xml:space="preserve"><value>Cấu hình hiện tại đã chạy được, không cần đổi.</value></data>
  <data name="TuneDohFailed" xml:space="preserve"><value>Không máy chủ DoH nào trả lời. Cài đặt giữ nguyên.</value></data>
  <data name="TuneNothingWorkedFormat" xml:space="preserve"><value>Không cấu hình nào vào được hết Steam. Vẫn lỗi: {0}. Cài đặt giữ nguyên.</value></data>
  <data name="TuneCancelled" xml:space="preserve"><value>Đã dừng, cài đặt giữ nguyên.</value></data>
  <data name="TuneFoundNotAppliedFormat" xml:space="preserve"><value>Tìm được {0}. {1}</value></data>
```

`Strings.cs` thêm 16 thuộc tính, cùng mẫu `public static string TuneButton => Get(nameof(TuneButton));`: `TuneButton`, `TuneStop`, `TuneMeasuringDoh`, `TuneClassifying`, `TuneTryingFormat`, `TuneCandidateFormat`, `TuneSplitFirstByte`, `TuneSplitMidSni`, `TuneRecordsOn`, `TuneRecordsOff`, `TuneChosenFormat`, `TuneNoChange`, `TuneDohFailed`, `TuneNothingWorkedFormat`, `TuneCancelled`, `TuneFoundNotAppliedFormat`. `StringsTests`: `166` thành `182`.

- [ ] **Step 2: Viết test hỏng cho `TuneText`**

`tests/LunaDns.App.Tests/TuneTextTests.cs`:

```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.App.Tests;

public sealed class TuneTextTests
{
    private static readonly TuneCandidate MidSniRecords = new(new HelloSplit(SplitMode.MidSni, true), 10);

    [Fact]
    public void Candidate_names_cut_records_and_delay() =>
        Assert.Equal("giữa SNI · tách record · 10 ms", TuneText.Candidate(MidSniRecords));

    [Fact]
    public void Chosen_names_doh_and_candidate() =>
        Assert.Equal("Đã chọn Quad9 · sau byte đầu · một record · 0 ms", TuneText.Chosen(DohProvider.Quad9, TuneCandidate.Default));

    [Fact]
    public void Progress_per_stage()
    {
        Assert.Equal(Strings.TuneMeasuringDoh, TuneText.Progress(new TuneProgress(TuneStage.MeasuringDoh, 0, 0, null)));
        Assert.Equal(Strings.TuneClassifying, TuneText.Progress(new TuneProgress(TuneStage.Classifying, 0, 0, TuneCandidate.Default)));
        Assert.Equal("Đang thử 3/8: giữa SNI · tách record · 10 ms",
            TuneText.Progress(new TuneProgress(TuneStage.TryingSplit, 3, 8, MidSniRecords)));
    }
}
```

Test so câu tiếng Việt cụ thể, giống `DomainProbeTextTests`. Nếu test chạy dưới culture khác làm `Strings` trả bản dịch, lấy câu kỳ vọng từ `Strings.Format(...)` thay vì viết cứng.

Run: `dotnet test tests/LunaDns.App.Tests --filter "FullyQualifiedName~TuneTextTests"`. Expected: lỗi build.

- [ ] **Step 3: Viết `TuneText.cs`**

```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.App;

/// <summary>Câu hiển thị cho lượt dò D3 "Tìm cấu hình hợp mạng".</summary>
internal static class TuneText
{
    public static string Candidate(TuneCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Strings.Format(nameof(Strings.TuneCandidateFormat),
            candidate.Split.Mode == SplitMode.MidSni ? Strings.TuneSplitMidSni : Strings.TuneSplitFirstByte,
            candidate.Split.Records ? Strings.TuneRecordsOn : Strings.TuneRecordsOff,
            candidate.DelayMs);
    }

    public static string Chosen(DohProvider doh, TuneCandidate candidate) =>
        Strings.Format(nameof(Strings.TuneChosenFormat), DohName(doh), Candidate(candidate));

    public static string Progress(TuneProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return progress.Stage switch
        {
            TuneStage.MeasuringDoh => Strings.TuneMeasuringDoh,
            TuneStage.Classifying => Strings.TuneClassifying,
            _ => Strings.Format(nameof(Strings.TuneTryingFormat), progress.Index, progress.Total,
                progress.Candidate is { } c ? Candidate(c) : string.Empty),
        };
    }

    public static string DohName(DohProvider provider) =>
        provider == DohProvider.Custom ? Strings.DohCustom : DohCatalog.Get(provider).Name;
}
```

Trong `MainViewModel`, đổi `DohName` private thành gọi `TuneText.DohName` (xoá hàm cũ, sửa chỗ gọi) để không trùng code.

Run: `dotnet test tests/LunaDns.App.Tests --filter "FullyQualifiedName~TuneTextTests|FullyQualifiedName~StringsTests"`. Expected: PASS.

- [ ] **Step 4: `MainViewModel`**

Trường và thuộc tính (đặt cạnh khối DoH):

```csharp
    private static readonly TimeSpan TuneTimeout = TimeSpan.FromSeconds(60);

    private Action? _cancelTune; // huỷ lượt dò đang chạy; như _cancelDoh, không giữ CTS
    private bool _isTuning;
    private bool _applyingTune;
    private string _tuneStatus = string.Empty;

    public AsyncCommand TuneCommand { get; }

    public AsyncCommand StopTuneCommand { get; }

    public bool IsTuning
    {
        get => _isTuning;
        private set
        {
            if (SetProperty(ref _isTuning, value)) RaiseTuneCommands();
        }
    }

    public string TuneStatus
    {
        get => _tuneStatus;
        private set
        {
            if (SetProperty(ref _tuneStatus, value)) OnPropertyChanged(nameof(HasTuneStatus));
        }
    }

    public bool HasTuneStatus => TuneStatus.Length > 0;
```

Trong constructor, sửa và thêm:

```csharp
        CheckCommand = new AsyncCommand(CheckAsync, OnError, () => !_isTuning);
        ApplyRecommendedCommand = new AsyncCommand(ApplyRecommendedAsync, OnError, () => !_isTuning);
        MeasureDohCommand = new AsyncCommand(MeasureDohAsync, OnError, () => !_isSavingCustomDoh && !_isTuning);
        TuneCommand = new AsyncCommand(TuneAsync, OnError, () => !_isTuning);
        StopTuneCommand = new AsyncCommand(StopTuneAsync, OnError, () => _isTuning && !_applyingTune);
```

`Stop()` thêm `_cancelTune?.Invoke();` cạnh `_cancelDoh?.Invoke();`.

Hàm:

```csharp
    private void RaiseTuneCommands()
    {
        TuneCommand.RaiseCanExecuteChanged();
        StopTuneCommand.RaiseCanExecuteChanged();
        CheckCommand.RaiseCanExecuteChanged();
        ApplyRecommendedCommand.RaiseCanExecuteChanged();
        MeasureDohCommand.RaiseCanExecuteChanged();
    }

    private Task StopTuneAsync()
    {
        _cancelTune?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// "Tìm cấu hình hợp mạng" (D3). Dò ở luồng nền tối đa 60 giây; huỷ (Dừng, đóng cửa sổ, hết giờ) thì không đổi gì.
    /// Có kết quả thì áp ở luồng UI, lúc này không huỷ được nữa.
    /// </summary>
    private async Task TuneAsync()
    {
        using var cts = new CancellationTokenSource(TuneTimeout);
        _cancelTune = cts.Cancel;
        IsTuning = true;
        TuneStatus = Strings.TuneMeasuringDoh;
        try
        {
            // Progress<T> chuyển về luồng UI; bỏ báo muộn sau khi đã sang bước áp.
            var progress = new Progress<TuneProgress>(p =>
            {
                if (_isTuning && !_applyingTune) TuneStatus = TuneText.Progress(p);
            });
            var token = cts.Token;
            TuneRun run;
            try
            {
                run = await Task.Run(() => _services.TuneAsync(progress, token));
            }
            catch (OperationCanceledException)
            {
                TuneStatus = Strings.TuneCancelled;
                return;
            }
            _applyingTune = true;
            RaiseTuneCommands();
            await ApplyTuneRunAsync(run);
        }
        finally
        {
            _cancelTune = null;
            _applyingTune = false;
            IsTuning = false;
        }
    }

    private Task ApplyTuneRunAsync(TuneRun run) => RunBusyAsync(Features, async () =>
    {
        if (run.ResolvedCustom is { } resolved)
            SaveSettings(s => s.CustomDoh?.Endpoint == resolved.Endpoint ? s with { CustomDoh = resolved } : s);
        if (run.Summary is not null) _summary = run.Summary; // RunBusyAsync gọi ShowSummary khi xong
        var result = run.Result;
        if (result.Outcome == TuneOutcome.DohFailed)
        {
            TuneStatus = Strings.TuneDohFailed;
            return;
        }
        if (result.Outcome == TuneOutcome.NothingWorked)
        {
            TuneStatus = Strings.Format(nameof(Strings.TuneNothingWorkedFormat), string.Join(", ", result.FailingHosts));
            return;
        }
        var doh = result.Doh!.Value;
        var candidate = result.Candidate!;
        var unchanged = run.Current is { } current && current.Doh == doh && current.Candidate == candidate;
        if (!unchanged)
        {
            var applied = await Task.Run(() => _services.ApplyTuningAsync(doh, candidate));
            NotifyTuningSettingsChanged();
            if (!applied.Success)
            {
                TuneStatus = Strings.Format(nameof(Strings.TuneFoundNotAppliedFormat), TuneText.Chosen(doh, candidate),
                    applied.Message ?? Strings.UnexpectedError);
                return;
            }
        }
        foreach (var card in Features) card.SetError(string.Empty);
        var pending = result.Remedies & ~_services.Features.Status.Remedies;
        var failures = new List<string?>();
        if (pending.HasFlag(ProbeRemedies.DnsRouting)) failures.Add(Failure(await EnableAsync(Feature.DnsRouting)));
        if (pending.HasFlag(ProbeRemedies.SniProxy)) failures.Add(Failure(await EnableAsync(Feature.SniProxy)));
        ShowFailures(failures);
        TuneStatus = unchanged ? Strings.TuneNoChange : TuneText.Chosen(doh, candidate);
    });
```

Tách 5 dòng `OnPropertyChanged(nameof(SplitModeIndex))` … `OnPropertyChanged(nameof(IsCustomDoh))` ở cuối `SaveSettings` ra hàm `private void NotifyTuningSettingsChanged()`, rồi gọi hàm đó từ `SaveSettings`.

Ghi chú: `unchanged` đúng mà còn biện pháp thiếu (ví dụ `SplitNotNeeded` với DnsPoisoned) thì vẫn bật biện pháp. Khi đó `TuneStatus` báo `TuneNoChange`, thanh thông báo báo lỗi bật nếu có.

- [ ] **Step 5: XAML**

Trong `StackPanel` chứa `CheckCommand` (khoảng dòng 154), sau nút Áp dụng đề xuất:

```xml
            <Button Content="{x:Static app:Strings.TuneButton}" Command="{Binding TuneCommand}"
                    IsVisible="{Binding !IsTuning}" />
            <Button Content="{x:Static app:Strings.TuneStop}" Command="{Binding StopTuneCommand}"
                    IsVisible="{Binding IsTuning}" />
```

Ngay sau `StackPanel` nút, cùng cha:

```xml
          <TextBlock Classes="secondary" Text="{Binding TuneStatus}" TextWrapping="Wrap" IsVisible="{Binding HasTuneStatus}" />
```

Khối này không nằm trong `Expander` Nâng cao, nên hiện trên cả hai nền tảng.

- [ ] **Step 6: Hướng dẫn**

`docs/guide/04-kiem-tra-va-de-xuat.md`: thêm mục `## Tìm cấu hình hợp mạng` sau mục Áp dụng đề xuất, gồm:
- nút làm gì: đo DoH, kiểm 4 host Steam, thử tối đa 8 tổ hợp điểm cắt × tách record × độ trễ 0/10 ms;
- tìm được thì tự lưu và bật Định tuyến DNS + Proxy SNI;
- nút Dừng: dừng là không đổi gì;
- tối đa 60 giây;
- trên Windows, kết quả hiện ở mục Nâng cao và vẫn sửa tay được.

`docs/guide/09-macos.md`: nút có trên macOS; dịch vụ nền bản cũ cần cài lại ("Cài dịch vụ nền") mới áp được; không có ô Tự nhập DoH. Bản `en` dịch tương ứng.

- [ ] **Step 7: Build và test toàn bộ**

Run:

```bash
dotnet build LunaDns.sln -c Release -warnaserror
dotnet test LunaDns.sln
```

Expected: build sạch (compiled bindings bắt sai tên thuộc tính), mọi test qua. Trên máy Windows, project `LunaDns.Platform.MacOS.Tests` có thể bị bỏ qua.

- [ ] **Step 8: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests docs/guide
git commit -m "feat(app): add Find a configuration that works button with stop and progress"
```

---

### Task 8: Thử tay, PR

- [ ] **Step 1: Thử tay trên Windows**, mạng thật, chạy bằng quyền Admin:
  1. Bấm "Tìm cấu hình hợp mạng": dòng tiến độ đổi qua đo DoH, kiểm Steam, "Đang thử n/8…".
  2. Kết quả hiện "Đã chọn …" hoặc "Cấu hình hiện tại đã chạy được…". Mục Nâng cao hiện đúng máy DoH, điểm cắt, tách record, độ trễ.
  3. Định tuyến DNS và Proxy SNI bật; mở Steam Store và Community.
  4. Bấm lại, rồi bấm "Dừng" ngay: báo "Đã dừng…", mục Nâng cao không đổi.
  5. Đặt độ trễ 5 ms ở Nâng cao, bấm dò: log có dòng thử tổ hợp 5 ms đầu tiên.
- [ ] **Step 2: Thử tay trên macOS**:
  1. Với dịch vụ nền bản mới: bấm dò, xem kết quả, rồi kiểm `sudo cat "/Library/Application Support/LunaDNS/settings.json"` (đường dẫn theo `MacPaths`) có các trường mới.
  2. Với dịch vụ nền bản cũ: dò xong báo "Tìm được … Dịch vụ nền cần được cài lại…", không bật gì.
- [ ] **Step 3: Đẩy nhánh và mở PR** (tiếng Anh, không có dòng attribution):

```bash
git push -u origin feat/auto-tune
gh pr create --base main --title "feat: D3 find a configuration that works on my network" --body "$(cat <<'EOF'
## Summary
- New "Find a configuration that works" button next to Apply recommended (Windows and macOS).
- Measures DoH, classifies the 4 Steam hosts, then tries up to 8 split combinations (cut point x TLS record split x 0/10 ms delay); stops at the first one every host passes.
- Applies automatically: saves DoH + split settings, restarts running features, enables missing remedies. Stop / close / 60 s timeout leaves settings unchanged.
- macOS: new daemon commands get-tuning / set-tuning (validated, applied, then persisted); old daemons get a reinstall prompt.

Spec: docs/superpowers/specs/2026-11-21-lunadns-auto-tune-design.md

## Test plan
- [ ] CI green on Windows and macOS (Release, -warnaserror)
- [ ] Manual: Windows tune, stop, Steam Store/Community open
- [ ] Manual: macOS tune with new daemon; old daemon shows reinstall prompt
EOF
)"
```

- [ ] **Step 4: Chờ CI cả hai nền tảng xanh** rồi mới merge.
