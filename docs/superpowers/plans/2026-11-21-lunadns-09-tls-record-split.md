# D1: Tách TLS record — Kế hoạch triển khai

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thêm công tắc "Tách TLS record" (mặc định tắt) để ClientHello được gửi thành hai TLS record, kết hợp với "Điểm cắt gói" hiện có.

**Architecture:** Kiểu cắt gộp thành `record HelloSplit(SplitMode Mode, bool Records)` trong `LunaDns.Core.Tls`, thay cho `SplitMode` ở mọi chỗ truyền đi (options proxy/forwarder, `FeatureTuning`, `TlsProbe`, `SplittingStream`). Hàm thuần mới `TlsRecordSplitter` dựng hai record từ một record; `HelloSplitter` chọn điểm cắt và gọi nó khi `Records = true`; `SplitWriter` giữ nguyên cách ghi mỗi phần một lần Write. Cài đặt thêm `AppSettings.SplitRecords`; giao diện thêm một `CheckBox` ở mục Nâng cao.

**Tech Stack:** .NET 8, C# 12, xUnit, Avalonia 11.3.23, System.Text.Json source generator (`SettingsJsonContext`).

**Spec:** [2026-11-21-lunadns-tls-record-split-design.md](../specs/2026-11-21-lunadns-tls-record-split-design.md)

## Global Constraints

- Công tắc mặc định **tắt**; `settings.json` cũ (không có `SplitRecords`) đọc ra `false`. Hành vi khi tắt phải giữ nguyên từng byte như hiện nay.
- Không giải mã TLS, không cài chứng chỉ. Record mới giữ content type + version của record gốc (RFC 8446 §5.1).
- Không bao giờ sinh record có thân rỗng; thân record dưới 2 byte thì không tách.
- Không sửa giao thức daemon macOS (`DaemonProtocol`). Trên macOS mục Nâng cao đang ẩn (`PlatformInfo.HasAdvancedSettings()`), và daemon đọc `settings.json` riêng của root, không phải file của giao diện: công tắc chỉ hiện trên Windows. Code Core vẫn chạy cho cả hai nền tảng (daemon nhận `SplitRecords` nếu file của nó có trường này).
- Build: `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended` (Directory.Build.props). Code mới không được sinh cảnh báo.
- Chú thích và chuỗi giao diện bằng tiếng Việt, theo giọng các file xung quanh.
- `dotnet` có thể không nằm trong PATH của shell agent: `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet`.

## Nhánh và thứ tự

Code D1 sửa cùng file với `feat/custom-domains` (`AppSettings`, `MainViewModel`, `MainWindow.axaml`, `Strings`). Làm ở worktree riêng, nhánh `feat/tls-record-split` tách từ `feat/custom-domains` (hoặc từ `main` nếu nhánh đó đã merge). Thư mục chính `/Users/lunavo/Documents/Projects/LunaDNS` đang có thay đổi logo chưa commit của user: không đụng.

## Bản đồ file

| File | Trách nhiệm | Task |
|---|---|---|
| `src/LunaDns.Core/Tls/HelloSplit.cs` (mới) | Kiểu cắt: cắt ở đâu + có tách record không | 1 |
| `src/LunaDns.Core/Tls/TlsRecordSplitter.cs` (mới) | Hàm thuần: một record → hai record | 1 |
| `src/LunaDns.Core/Tls/HelloSplitter.cs` | Điểm cắt theo `HelloSplit`; gọi `TlsRecordSplitter` khi `Records` | 2, 3 |
| `src/LunaDns.Core/Tls/SplitWriter.cs` | Tham số `HelloSplit` | 2 |
| `src/LunaDns.Core/Proxy/TlsTunnel.cs`, `SplitProxy.cs`, `SniForwarder.cs`, `SplitProxyOptions.cs`, `SniForwarderOptions.cs` | Truyền `HelloSplit` | 2 |
| `src/LunaDns.Core/Probing/TlsProbe.cs`, `SplittingStream.cs` | Truyền `HelloSplit`; chỉ cắt record đầu | 2, 3 |
| `src/LunaDns.Core/Features/FeatureTuning.cs`, `CoreFeatureServices.cs` | `FeatureTuning.Split` | 2 |
| `src/LunaDns.Core/Settings/AppSettings.cs` | `SplitRecords`, `Tuning()` | 2, 4 |
| `src/LunaDns.App/ProbeRunner.cs` | Kiểm tra dùng `Tuning().Split` | 2, 4 |
| `src/LunaDns.App/ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml`, `Strings.cs`, `Strings.resx` | Công tắc giao diện | 5 |
| `docs/guide/05-nang-cao.md` | Hướng dẫn người dùng | 5 |

---

### Task 0: Nhánh làm việc và tài liệu

**Files:** `docs/superpowers/specs/2026-11-21-lunadns-lo-trinh-tinh-nang.md`, `docs/superpowers/specs/2026-11-21-lunadns-tls-record-split-design.md`, file kế hoạch này (đang chưa commit ở thư mục chính).

- [ ] **Step 1: Tạo worktree**

```bash
cd /Users/lunavo/Documents/Projects/LunaDNS
git worktree add -b feat/tls-record-split ../LunaDNS-d1 feat/custom-domains
```

- [ ] **Step 2: Chép ba file tài liệu sang worktree rồi commit**

```bash
cd /Users/lunavo/Documents/Projects/LunaDNS
cp docs/superpowers/specs/2026-11-21-lunadns-lo-trinh-tinh-nang.md docs/superpowers/specs/2026-11-21-lunadns-tls-record-split-design.md ../LunaDNS-d1/docs/superpowers/specs/
cp docs/superpowers/plans/2026-11-21-lunadns-09-tls-record-split.md ../LunaDNS-d1/docs/superpowers/plans/
cd ../LunaDNS-d1
git add docs/superpowers/specs/2026-11-21-lunadns-lo-trinh-tinh-nang.md docs/superpowers/specs/2026-11-21-lunadns-tls-record-split-design.md docs/superpowers/plans/2026-11-21-lunadns-09-tls-record-split.md
git commit -m "docs: lộ trình tính năng, spec và kế hoạch D1 tách TLS record"
```

Không xoá bản gốc ở thư mục chính (user tự dọn). Mọi task sau chạy trong `../LunaDNS-d1`.

- [ ] **Step 3: Kiểm mốc xanh**

Run: `dotnet build -warnaserror && dotnet test tests/LunaDns.Core.Tests && dotnet test tests/LunaDns.App.Tests`
Expected: build sạch, mọi test PASS. Nếu không xanh thì dừng và báo, không sửa test của nhánh khác.

---

### Task 1: `HelloSplit` và `TlsRecordSplitter`

**Files:**
- Create: `src/LunaDns.Core/Tls/HelloSplit.cs`
- Create: `src/LunaDns.Core/Tls/TlsRecordSplitter.cs`
- Test: `tests/LunaDns.Core.Tests/Tls/TlsRecordSplitterTests.cs`

**Interfaces:**
- Consumes: `ClientHelloParser.RecordLength(ReadOnlySpan<byte>)` (trả `5 + độ dài thân` nếu byte đầu là 0x16, ngược lại -1); `TestClientHello.Build(string? sni, int paddingLength = 0)` (test helper có sẵn).
- Produces:
  - `public sealed record HelloSplit(SplitMode Mode, bool Records = false)` với `public static HelloSplit Default` = `new(SplitMode.FirstByte)`.
  - `public static class TlsRecordSplitter` với `public const int HeaderLength = 5;` và `public static IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> record, int cut)` — `record` phải là đúng một record handshake hoàn chỉnh, `HeaderLength < cut < record.Length`.

- [ ] **Step 1: Viết test hỏng**

`tests/LunaDns.Core.Tests/Tls/TlsRecordSplitterTests.cs`:

```csharp
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class TlsRecordSplitterTests
{
    private static readonly byte[] Hello = TestClientHello.Build("store.steampowered.com", paddingLength: 40);

    private static int BodyLength(ReadOnlyMemory<byte> record) => (record.Span[3] << 8) | record.Span[4];

    [Fact]
    public void Two_records_with_own_headers_and_original_body()
    {
        var cut = 50;

        var records = TlsRecordSplitter.Split(Hello, cut);

        Assert.Equal(2, records.Count);
        Assert.Equal(cut - 5, BodyLength(records[0]));
        Assert.Equal(Hello.Length - cut, BodyLength(records[1]));
        foreach (var record in records)
        {
            Assert.Equal(record.Length - 5, BodyLength(record));
            Assert.Equal(Hello.AsSpan(0, 3).ToArray(), record.Span[..3].ToArray()); // content type + version
        }
        var body = records.SelectMany(r => r.Span[5..].ToArray()).ToArray();
        Assert.Equal(Hello.AsSpan(5).ToArray(), body);
    }

    [Theory]
    [InlineData(5)]   // record 1 rỗng
    [InlineData(0)]
    public void Cut_inside_header_is_rejected(int cut) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TlsRecordSplitter.Split(Hello, cut));

    [Fact]
    public void Cut_at_end_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TlsRecordSplitter.Split(Hello, Hello.Length));

    [Fact]
    public void Input_that_is_not_one_whole_record_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => TlsRecordSplitter.Split(Hello.AsMemory(0, Hello.Length - 1), 10));
        Assert.Throws<ArgumentException>(() => TlsRecordSplitter.Split((byte[])[.. Hello, 1, 2], 10));
    }

    [Fact]
    public void Largest_record_splits_into_records_within_limit()
    {
        var record = new byte[5 + 16384];
        record[0] = 0x16;
        record[1] = 0x03;
        record[2] = 0x01;
        record[3] = 0x40; // 16384 = 0x4000
        record[4] = 0x00;

        var records = TlsRecordSplitter.Split(record, 6);

        Assert.All(records, r => Assert.InRange(BodyLength(r), 1, 16384));
        Assert.Equal(16384, records.Sum(BodyLength));
    }

    [Fact]
    public void Default_split_is_first_byte_without_records()
    {
        Assert.Equal(SplitMode.FirstByte, HelloSplit.Default.Mode);
        Assert.False(HelloSplit.Default.Records);
    }
}
```

- [ ] **Step 2: Chạy test để thấy hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~TlsRecordSplitterTests`
Expected: FAIL biên dịch — `TlsRecordSplitter`, `HelloSplit` chưa có.

- [ ] **Step 3: Viết code**

`src/LunaDns.Core/Tls/HelloSplit.cs`:

```csharp
namespace LunaDns.Core.Tls;

/// <summary>
/// Cách cắt ClientHello (mục Nâng cao): <see cref="Mode"/> là cắt ở đâu, <see cref="Records"/> là có tách thành hai
/// TLS record không (D1). Tắt Records thì giữ một record và chỉ cắt TCP segment, như spec 4.3.
/// </summary>
public sealed record HelloSplit(SplitMode Mode, bool Records = false)
{
    public static HelloSplit Default { get; } = new(SplitMode.FirstByte);
}
```

`src/LunaDns.Core/Tls/TlsRecordSplitter.cs`:

```csharp
using System.Buffers.Binary;

namespace LunaDns.Core.Tls;

/// <summary>
/// Tách một TLS record handshake thành hai record tại <c>cut</c> (offset trong record, nằm trong phần thân).
/// Mỗi record mới giữ content type + version của record gốc; RFC 8446 §5.1 cho phép handshake trải nhiều record.
/// </summary>
public static class TlsRecordSplitter
{
    public const int HeaderLength = 5;

    public static IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> record, int cut)
    {
        var span = record.Span;
        if (ClientHelloParser.RecordLength(span) != record.Length)
            throw new ArgumentException("Cần đúng một TLS record handshake hoàn chỉnh.", nameof(record));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cut, HeaderLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cut, record.Length);
        return [Build(span, span[HeaderLength..cut]), Build(span, span[cut..])];
    }

    private static byte[] Build(ReadOnlySpan<byte> original, ReadOnlySpan<byte> body)
    {
        var result = new byte[HeaderLength + body.Length];
        original[..3].CopyTo(result);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(3), (ushort)body.Length);
        body.CopyTo(result.AsSpan(HeaderLength));
        return result;
    }
}
```

- [ ] **Step 4: Chạy test để thấy qua**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~TlsRecordSplitterTests`
Expected: PASS (7 test).

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Tls/HelloSplit.cs src/LunaDns.Core/Tls/TlsRecordSplitter.cs tests/LunaDns.Core.Tests/Tls/TlsRecordSplitterTests.cs
git commit -m "feat(core): HelloSplit và TlsRecordSplitter tách một TLS record thành hai"
```

---

### Task 2: Truyền `HelloSplit` thay cho `SplitMode` (chưa đổi hành vi)

Đổi kiểu thuần cơ học. `Records` chưa được dùng ở đâu; mọi test hiện có phải qua với cùng kết quả.

**Files:**
- Modify: `src/LunaDns.Core/Tls/HelloSplitter.cs`, `src/LunaDns.Core/Tls/SplitWriter.cs`
- Modify: `src/LunaDns.Core/Proxy/TlsTunnel.cs` (`WriteFirstRecordAsync`), `src/LunaDns.Core/Proxy/SplitProxy.cs:275`, `src/LunaDns.Core/Proxy/SniForwarder.cs:188`, `src/LunaDns.Core/Proxy/SplitProxyOptions.cs`, `src/LunaDns.Core/Proxy/SniForwarderOptions.cs`
- Modify: `src/LunaDns.Core/Probing/TlsProbe.cs`, `src/LunaDns.Core/Probing/SplittingStream.cs`
- Modify: `src/LunaDns.Core/Features/FeatureTuning.cs`, `src/LunaDns.Core/Features/CoreFeatureServices.cs` (khoảng dòng 49 và 68), `src/LunaDns.Core/Settings/AppSettings.cs` (`Tuning()`), `src/LunaDns.App/ProbeRunner.cs:19`
- Test (sửa cơ học): `tests/LunaDns.Core.Tests/Tls/HelloSplitterTests.cs`, `Tls/SplitWriterTests.cs`, `Probing/SplittingStreamTests.cs`, `Probing/TlsProbeTests.cs`, `Proxy/SplitProxyTests.cs`, `Proxy/SplitProxyOptionsTests.cs`, `Features/FeatureSetTests.cs`

**Interfaces:**
- Consumes: `HelloSplit` (Task 1).
- Produces (chữ ký mới, các task sau dùng đúng tên này):
  - `HelloSplitter.CutPoints(ReadOnlySpan<byte> hello, HelloSplit split) : int[]`
  - `HelloSplitter.Split(ReadOnlyMemory<byte> hello, HelloSplit split) : IReadOnlyList<ReadOnlyMemory<byte>>`
  - `SplitWriter.WriteAsync(Stream destination, ReadOnlyMemory<byte> hello, HelloSplit split, TimeSpan delay, CancellationToken cancellationToken)`
  - `TlsTunnel.WriteFirstRecordAsync(Stream upstream, byte[] buffer, int count, int recordLength, HelloSplit split, TimeSpan delay, CancellationToken cancellationToken)`
  - `SplitProxyOptions.Split` và `SniForwarderOptions.Split`: `public HelloSplit Split { get; init; } = HelloSplit.Default;` (thay thuộc tính `SplitMode`)
  - `TlsProbe(HelloSplit? split = null, ILogger<TlsProbe>? logger = null)` và `internal TlsProbe(int port, HelloSplit split, RemoteCertificateValidationCallback? validation, ILogger<TlsProbe>? logger)`
  - `SplittingStream(Stream inner, HelloSplit split)`
  - `FeatureTuning(HelloSplit Split, TimeSpan SplitDelay)`; `FeatureTuning.Default = new(HelloSplit.Default, TimeSpan.Zero)`

- [ ] **Step 1: Sửa `HelloSplitter` và `SplitWriter`**

`HelloSplitter.cs` (giữ nguyên logic, chỉ đổi tham số):

```csharp
namespace LunaDns.Core.Tls;

public static class HelloSplitter
{
    public static int[] CutPoints(ReadOnlySpan<byte> hello, HelloSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var cut = 1;
        if (split.Mode == SplitMode.MidSni && ClientHelloParser.TryFindSni(hello, out var sni) && sni.Length > 1)
            cut = sni.Offset + sni.Length / 2;
        return cut > 0 && cut < hello.Length ? [cut] : [];
    }

    public static IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> hello, HelloSplit split)
    {
        var pieces = new List<ReadOnlyMemory<byte>>();
        var start = 0;
        foreach (var cut in CutPoints(hello.Span, split))
        {
            pieces.Add(hello[start..cut]);
            start = cut;
        }
        pieces.Add(hello[start..]);
        return pieces;
    }
}
```

`SplitWriter.WriteAsync`: đổi `SplitMode mode` → `HelloSplit split`, thêm `ArgumentNullException.ThrowIfNull(split);` và gọi `HelloSplitter.Split(hello, split)`.

- [ ] **Step 2: Sửa proxy, forwarder, options**

- `TlsTunnel.WriteFirstRecordAsync`: tham số `SplitMode mode` → `HelloSplit split`, truyền `split` vào `SplitWriter.WriteAsync`.
- `SplitProxy.cs:275` và `SniForwarder.cs:188`: `_options.SplitMode` → `_options.Split`.
- `SplitProxyOptions` và `SniForwarderOptions`: thay `public SplitMode SplitMode { get; init; } = SplitMode.FirstByte;` bằng

```csharp
    /// <summary>Cắt ClientHello ở đâu và có tách TLS record không (mục Nâng cao, spec 4.3 và D1).</summary>
    public HelloSplit Split { get; init; } = HelloSplit.Default;
```

  và thêm `ArgumentNullException.ThrowIfNull(Split);` vào đầu `Validate()`.

- [ ] **Step 3: Sửa phần Kiểm tra**

- `TlsProbe`: trường `private readonly HelloSplit _split;`; constructor công khai `public TlsProbe(HelloSplit? split = null, ILogger<TlsProbe>? logger = null) : this(443, split ?? HelloSplit.Default, null, logger)`; constructor internal nhận `HelloSplit split`, `ArgumentNullException.ThrowIfNull(split)`; dòng tạo stream thành `new SplittingStream(transport, _split)`.
- `SplittingStream`: trường `_mode` → `private readonly HelloSplit _split;`, constructor `SplittingStream(Stream inner, HelloSplit split)`; `HelloSplitter.Split(..., _split)` và `SplitWriter.WriteAsync(_inner, buffer, _split, TimeSpan.Zero, cancellationToken)`.

- [ ] **Step 4: Sửa tuning, cài đặt, App**

`FeatureTuning.cs`:

```csharp
using LunaDns.Core.Tls;

namespace LunaDns.Core.Features;

/// <summary>Tuỳ chọn cắt gói từ mục Nâng cao (spec 4.3, 4.8, D1). Áp dụng khi dịch vụ được mở (bật hoặc watchdog mở lại).</summary>
public sealed record FeatureTuning(HelloSplit Split, TimeSpan SplitDelay)
{
    public static FeatureTuning Default { get; } = new(HelloSplit.Default, TimeSpan.Zero);
}
```

- `CoreFeatureServices.cs`: trong `StartSniProxy` đổi `SplitMode = tuning.SplitMode,` thành `Split = tuning.Split,`; trong `StartPubgForwarder` đổi `SplitMode = tuning.SplitMode` thành `Split = tuning.Split`.
- `AppSettings.Tuning()`:

```csharp
    public FeatureTuning Tuning() =>
        new(new HelloSplit(SplitMode), TimeSpan.FromMilliseconds(Math.Clamp(SplitDelayMs, 0, MaxSplitDelayMs)));
```

- `ProbeRunner.cs:19`: `new TlsProbe(current.SplitMode, ...)` → `new TlsProbe(current.Tuning().Split, loggerFactory.CreateLogger<TlsProbe>())`.

- [ ] **Step 5: Sửa test cơ học**

Thay mỗi đối số `SplitMode.X` ở các lời gọi đã đổi chữ ký bằng `new HelloSplit(SplitMode.X)`:
- `HelloSplitterTests`: `HelloSplitter.CutPoints(hello, new HelloSplit(SplitMode.FirstByte))`… Theory `Pieces_concatenate_to_original(SplitMode mode)` giữ `[InlineData(SplitMode.…)]`, bên trong gọi `HelloSplitter.Split(hello, new HelloSplit(mode))`.
- `SplitWriterTests`: `SplitWriter.WriteAsync(stream, Hello, new HelloSplit(SplitMode.FirstByte), …)` (4 chỗ).
- `SplittingStreamTests`: `new SplittingStream(inner, HelloSplit.Default)` (2 chỗ).
- `TlsProbeTests`: `new(port ?? _server.Port, HelloSplit.Default, _server.Trusts, null)` và `new TlsProbe(_server.Port, HelloSplit.Default, null, null)`.
- `SplitProxyTests.cs:27-29`: helper thành

```csharp
    private SplitProxy Start(HelloSplit? split = null, int firstPort = 0, int lastPort = 0) =>
        SplitProxy.Start(
            new SplitProxyOptions { Domains = DomainCatalog.All, FirstPort = firstPort, LastPort = lastPort, Split = split ?? HelloSplit.Default },
            _connector);
```

  và dòng 180 `Start(SplitMode.MidSni)` → `Start(new HelloSplit(SplitMode.MidSni))`. Các lời gọi `Start(firstPort: …)` giữ nguyên.
- `SplitProxyOptionsTests.cs:16`: `Assert.Equal(HelloSplit.Default, options.Split);`
- `FeatureSetTests.cs:152`: `new FeatureTuning(new HelloSplit(SplitMode.MidSni), TimeSpan.FromMilliseconds(5))`.

Tìm sót: `grep -rn "SplitMode" src tests | grep -v /obj/` chỉ còn: định nghĩa enum, `HelloSplit`, `HelloSplitter`, `AppSettings.SplitMode`, `MainViewModel.SplitModeIndex`, `Strings`/`MainWindow.axaml` (`SplitModeLabel`…), và các test settings.

- [ ] **Step 6: Build và chạy toàn bộ test**

Run: `dotnet build -warnaserror && dotnet test tests/LunaDns.Core.Tests && dotnet test tests/LunaDns.App.Tests && dotnet test tests/LunaDns.Platform.MacOS.Tests`
Expected: build sạch, mọi test PASS với số test như mốc ở Task 0 (cộng 7 test của Task 1). Trên Windows chạy thêm `dotnet test tests/LunaDns.Platform.Windows.Tests`.

- [ ] **Step 7: Commit**

```bash
git add -A src tests
git commit -m "refactor(core): truyền HelloSplit thay cho SplitMode qua proxy, forwarder, Kiểm tra và tuning"
```

---

### Task 3: Chế độ tách record trong `HelloSplitter` và `SplittingStream`

**Files:**
- Modify: `src/LunaDns.Core/Tls/HelloSplitter.cs`, `src/LunaDns.Core/Probing/SplittingStream.cs`
- Test: `tests/LunaDns.Core.Tests/Tls/HelloSplitterTests.cs`, `Tls/SplitWriterTests.cs`, `Probing/SplittingStreamTests.cs`, `Probing/TlsProbeTests.cs`, `Proxy/SplitProxyTests.cs`

**Interfaces:**
- Consumes: `TlsRecordSplitter.Split`, `TlsRecordSplitter.HeaderLength` (Task 1); chữ ký Task 2.
- Produces: `HelloSplitter.Split(hello, new HelloSplit(mode, Records: true))` trả **hai record hoàn chỉnh** khi `hello` là đúng một record handshake và có điểm cắt hợp lệ; ngược lại trả một phần duy nhất là `hello` nguyên vẹn.

- [ ] **Step 1: Viết test hỏng**

Thêm vào `HelloSplitterTests`:

```csharp
    private static int BodyLength(ReadOnlyMemory<byte> record) => (record.Span[3] << 8) | record.Span[4];

    [Fact]
    public void Records_first_byte_puts_one_handshake_byte_in_first_record()
    {
        var hello = TestClientHello.Build("store.steampowered.com");

        var records = HelloSplitter.Split(hello, new HelloSplit(SplitMode.FirstByte, Records: true));

        Assert.Equal(2, records.Count);
        Assert.Equal(6, records[0].Length);
        Assert.Equal(1, BodyLength(records[0]));
        Assert.Equal(hello[5], records[0].Span[5]);
        Assert.Equal(hello.Length + 5, records.Sum(r => r.Length));
    }

    [Fact]
    public void Records_mid_sni_splits_host_name_across_records()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        Assert.True(ClientHelloParser.TryFindSni(hello, out var sni));

        var records = HelloSplitter.Split(hello, new HelloSplit(SplitMode.MidSni, Records: true));

        Assert.Equal(2, records.Count);
        Assert.Equal(sni.Offset + sni.Length / 2, records[0].Length);
        var host = System.Text.Encoding.ASCII.GetBytes(sni.Host);
        Assert.Equal(-1, records[0].Span.IndexOf(host));
        Assert.Equal(-1, records[1].Span.IndexOf(host));
    }

    [Fact]
    public void Records_mid_sni_without_sni_falls_back_to_first_byte()
    {
        var hello = TestClientHello.Build(null, paddingLength: 8);

        var records = HelloSplitter.Split(hello, new HelloSplit(SplitMode.MidSni, Records: true));

        Assert.Equal(6, records[0].Length);
    }

    [Fact]
    public void Records_with_one_byte_body_is_left_whole()
    {
        byte[] hello = [0x16, 0x03, 0x01, 0x00, 0x01, 0x01];

        var pieces = HelloSplitter.Split(hello, new HelloSplit(SplitMode.FirstByte, Records: true));

        Assert.Equal(hello, Assert.Single(pieces).ToArray());
    }

    [Fact]
    public void Records_with_incomplete_record_is_left_whole()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        var partial = hello.AsMemory(0, hello.Length - 3);

        var pieces = HelloSplitter.Split(partial, new HelloSplit(SplitMode.FirstByte, Records: true));

        Assert.Equal(partial.ToArray(), Assert.Single(pieces).ToArray());
    }
```

Thêm vào `SplitWriterTests`:

```csharp
    [Fact]
    public async Task Records_mode_writes_two_records()
    {
        using var stream = new RecordingStream();

        await SplitWriter.WriteAsync(stream, Hello, new HelloSplit(SplitMode.FirstByte, Records: true), TimeSpan.Zero, CancellationToken.None);

        var writes = stream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Equal(6, writes[0].Length);
        Assert.Equal(Hello.Length + 5, writes.Sum(w => w.Length));
        Assert.All(writes, w => Assert.Equal(0x16, w[0]));
    }
```

Thêm vào `SplittingStreamTests`:

```csharp
    [Fact]
    public async Task Records_mode_splits_only_the_first_record_of_the_first_write()
    {
        var inner = new RecordingStream();
        await using var stream = new SplittingStream(inner, new HelloSplit(SplitMode.FirstByte, Records: true));

        await stream.WriteAsync((byte[])[.. Hello, .. Later]);

        var writes = inner.Writes;
        Assert.Equal(3, writes.Count);
        Assert.Equal(6, writes[0].Length);
        Assert.Equal(Hello.Length - 1, writes[1].Length); // 5 byte header mới + phần thân còn lại
        Assert.Equal(Later, writes[2]);
    }
```

Thêm vào `TlsProbeTests` (máy chủ `SslStream` thật của `LoopbackTlsServer` phải ghép được hai record):

```csharp
    [Theory]
    [InlineData(SplitMode.FirstByte)]
    [InlineData(SplitMode.MidSni)]
    public async Task Record_split_handshake_is_accepted_by_the_server(SplitMode mode)
    {
        var probe = new TlsProbe(_server.Port, new HelloSplit(mode, Records: true), _server.Trusts, null);

        Assert.True(await probe.HandshakeAsync(IPAddress.Loopback, Host, split: true, CancellationToken.None));
    }
```

Thêm vào `SplitProxyTests` (theo mẫu `MidSni_mode_cuts_inside_host_name`):

```csharp
    [Fact]
    public async Task Records_mode_sends_two_records_upstream()
    {
        await using var proxy = Start(new HelloSplit(SplitMode.MidSni, Records: true));
        var upstream = UseRecordingUpstream();
        Assert.True(ClientHelloParser.TryFindSni(Hello, out var sni));
        await using var tunnel = await OpenTunnelAsync(proxy);

        await tunnel.WriteAsync(Hello);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == Hello.Length + 5);

        var writes = upstream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Equal(sni.Offset + sni.Length / 2, writes[0].Length);
        Assert.All(writes, w => Assert.Equal(0x16, w[0]));
    }
```

- [ ] **Step 2: Chạy test để thấy hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~HelloSplitterTests|FullyQualifiedName~SplitWriterTests|FullyQualifiedName~SplittingStreamTests|FullyQualifiedName~TlsProbeTests|FullyQualifiedName~SplitProxyTests"`
Expected: các test `Records_*` và `Record_split_*` FAIL (đang ra một record cắt TCP); test cũ vẫn PASS.

- [ ] **Step 3: Viết code**

`HelloSplitter.cs`:

```csharp
namespace LunaDns.Core.Tls;

/// <summary>
/// Cắt ClientHello theo <see cref="HelloSplit"/>. Records tắt: các mảnh của một record (cắt TCP, spec 4.3).
/// Records bật: hai TLS record hoàn chỉnh (D1); điểm cắt luôn nằm trong phần thân.
/// </summary>
public static class HelloSplitter
{
    public static int[] CutPoints(ReadOnlySpan<byte> hello, HelloSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var cut = split.Records ? TlsRecordSplitter.HeaderLength + 1 : 1;
        if (split.Mode == SplitMode.MidSni && ClientHelloParser.TryFindSni(hello, out var sni) && sni.Length > 1)
            cut = sni.Offset + sni.Length / 2;
        return cut > 0 && cut < hello.Length ? [cut] : [];
    }

    public static IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> hello, HelloSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var cuts = CutPoints(hello.Span, split);
        if (split.Records)
        {
            // Chỉ tách khi có trọn một record; thiếu byte (client đóng sớm) thì gửi nguyên.
            return cuts.Length == 1 && ClientHelloParser.RecordLength(hello.Span) == hello.Length
                ? TlsRecordSplitter.Split(hello, cuts[0])
                : [hello];
        }

        var pieces = new List<ReadOnlyMemory<byte>>();
        var start = 0;
        foreach (var cut in cuts)
        {
            pieces.Add(hello[start..cut]);
            start = cut;
        }
        pieces.Add(hello[start..]);
        return pieces;
    }
}
```

`SplittingStream`: lần Write đầu chỉ cắt record đầu tiên, byte sau đó ghi nguyên. Thay thân `Write(byte[], int, int)` (nhánh `_first`) và `WriteAsync(ReadOnlyMemory<byte>, …)` (nhánh `_first`):

```csharp
    /// <summary>Độ dài record đầu nếu buffer chứa trọn nó, ngược lại cả buffer (cắt như trước).</summary>
    private static int FirstRecordLength(ReadOnlySpan<byte> buffer)
    {
        var length = ClientHelloParser.RecordLength(buffer);
        return length > 0 && length <= buffer.Length ? length : buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_first)
        {
            _inner.Write(buffer, offset, count);
            return;
        }
        _first = false;
        var data = buffer.AsMemory(offset, count);
        var first = FirstRecordLength(data.Span);
        foreach (var piece in HelloSplitter.Split(data[..first], _split))
        {
            _inner.Write(piece.Span);
            _inner.Flush();
        }
        if (first < count) _inner.Write(data.Span[first..]);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_first)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            return;
        }
        _first = false;
        var first = FirstRecordLength(buffer.Span);
        await SplitWriter.WriteAsync(_inner, buffer[..first], _split, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        if (first < buffer.Length)
            await _inner.WriteAsync(buffer[first..], cancellationToken).ConfigureAwait(false);
    }
```

Thêm `using LunaDns.Core.Tls;` nếu file chưa có. Proxy và forwarder không cần sửa: `TlsTunnel.WriteFirstRecordAsync` đã chỉ đưa record đầu vào `SplitWriter` và ghi byte thừa riêng.

- [ ] **Step 4: Chạy test để thấy qua**

Run: `dotnet test tests/LunaDns.Core.Tests`
Expected: mọi test PASS, gồm các test mới ở Step 1.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Tls/HelloSplitter.cs src/LunaDns.Core/Probing/SplittingStream.cs tests/LunaDns.Core.Tests
git commit -m "feat(core): chế độ tách ClientHello thành hai TLS record cho proxy, forwarder và Kiểm tra"
```

---

### Task 4: Cài đặt `SplitRecords`

**Files:**
- Modify: `src/LunaDns.Core/Settings/AppSettings.cs`
- Test: `tests/LunaDns.Core.Tests/Settings/AppSettingsTests.cs`, `tests/LunaDns.Core.Tests/Settings/JsonFileSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `HelloSplit` (Task 1), `FeatureTuning(HelloSplit, TimeSpan)` (Task 2).
- Produces: `AppSettings.SplitRecords : bool` (init, mặc định `false`); `AppSettings.Tuning().Split == new HelloSplit(SplitMode, SplitRecords)`. `ProbeRunner` (Task 2) và daemon macOS (`Engine.cs` dùng `settings.Tuning`) tự nhận giá trị mới.

- [ ] **Step 1: Viết test hỏng**

`AppSettingsTests.Defaults_match_spec`: thêm `Assert.False(settings.SplitRecords);`. Thêm test:

```csharp
    [Fact]
    public void Tuning_carries_record_split()
    {
        var settings = new AppSettings { SplitMode = SplitMode.MidSni, SplitRecords = true, SplitDelayMs = 3 };

        Assert.Equal(new FeatureTuning(new HelloSplit(SplitMode.MidSni, Records: true), TimeSpan.FromMilliseconds(3)), settings.Tuning());
    }
```

`JsonFileSettingsStoreTests`:
- `Save_then_load_round_trips`: thêm `SplitRecords = true` vào `new AppSettings { … }`.
- Tìm test đang ghi `{ "SplitMode": "FirstByte", "SplitDelayMs": 0 }` (dòng 24) và thêm `Assert.False(loaded.SplitRecords);` sau khi load (đặt tên biến theo test đó). Nếu test đó không hợp, thêm test riêng:

```csharp
    [Fact]
    public void Old_file_without_split_records_loads_as_off()
    {
        File.WriteAllText(PathOf("settings.json"), """{ "SplitMode": "MidSni", "SplitDelayMs": 2 }""");

        var loaded = new JsonFileSettingsStore(PathOf("settings.json")).Load();

        Assert.Equal(SplitMode.MidSni, loaded.SplitMode);
        Assert.False(loaded.SplitRecords);
    }
```

- [ ] **Step 2: Chạy test để thấy hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~AppSettingsTests|FullyQualifiedName~JsonFileSettingsStoreTests"`
Expected: FAIL biên dịch — `SplitRecords` chưa có.

- [ ] **Step 3: Viết code**

`AppSettings.cs`: ngay sau `SplitDelayMs` thêm

```csharp
    /// <summary>Tách ClientHello thành hai TLS record (D1). Mặc định tắt.</summary>
    public bool SplitRecords { get; init; }
```

và đổi `Tuning()`:

```csharp
    public FeatureTuning Tuning() =>
        new(new HelloSplit(SplitMode, SplitRecords), TimeSpan.FromMilliseconds(Math.Clamp(SplitDelayMs, 0, MaxSplitDelayMs)));
```

`SettingsJsonContext` sinh metadata cho thuộc tính mới, không cần sửa.

- [ ] **Step 4: Chạy test để thấy qua**

Run: `dotnet test tests/LunaDns.Core.Tests && dotnet test tests/LunaDns.Platform.MacOS.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Settings/AppSettings.cs tests/LunaDns.Core.Tests/Settings
git commit -m "feat(core): cài đặt SplitRecords đưa vào FeatureTuning"
```

---

### Task 5: Công tắc ở mục Nâng cao và hướng dẫn

**Files:**
- Modify: `src/LunaDns.App/ViewModels/MainViewModel.cs` (khối "Mục Nâng cao" khoảng dòng 174-190 và các `OnPropertyChanged` trong `SaveSettings` khoảng dòng 523-525)
- Modify: `src/LunaDns.App/Views/MainWindow.axaml` (khối Nâng cao khoảng dòng 275-289)
- Modify: `src/LunaDns.App/Strings.cs` (cạnh `SplitModeMidSni`, dòng 79), `src/LunaDns.App/Strings.resx` (cạnh `SplitModeMidSni`, dòng 53)
- Modify: `docs/guide/05-nang-cao.md` (bảng, ngay dưới dòng "Điểm cắt gói")
- Test: `tests/LunaDns.App.Tests/StringsTests.cs`

**Interfaces:**
- Consumes: `AppSettings.SplitRecords` (Task 4); `MainViewModel.SaveSettings(Func<AppSettings, AppSettings>)` có sẵn.
- Produces: `MainViewModel.SplitRecords : bool` (binding), `Strings.SplitRecordsLabel`, `Strings.SplitRecordsHint`.

Repo chưa có test cho `MainViewModel` (cần dựng cả app); thuộc tính mới đi theo đúng mẫu `DohIndex` và được kiểm bằng thử tay ở Task 6.

- [ ] **Step 1: Viết test hỏng**

`StringsTests.Every_static_string_resolves`: tăng số đếm thêm 2 so với giá trị hiện tại trong file (ví dụ `144` → `146`; nếu nhánh gốc đã đổi số thì cộng 2 vào số đó).

Run: `dotnet test tests/LunaDns.App.Tests --filter FullyQualifiedName~StringsTests`
Expected: FAIL — số thuộc tính ít hơn 2.

- [ ] **Step 2: Thêm chuỗi**

`Strings.resx`, ngay sau dòng `SplitModeMidSni`:

```xml
  <data name="SplitRecordsLabel" xml:space="preserve"><value>Tách TLS record</value></data>
  <data name="SplitRecordsHint" xml:space="preserve"><value>Gửi ClientHello thành hai TLS record, cắt tại điểm đã chọn ở trên. Thử khi Kiểm tra vẫn báo không vào được dù đã đổi điểm cắt.</value></data>
```

`Strings.cs`, ngay sau `SplitModeMidSni`:

```csharp
    public static string SplitRecordsLabel => Get(nameof(SplitRecordsLabel));
    public static string SplitRecordsHint => Get(nameof(SplitRecordsHint));
```

- [ ] **Step 3: Thuộc tính ViewModel**

`MainViewModel.cs`, ngay sau `SplitModeIndex`:

```csharp
    public bool SplitRecords
    {
        get => _services.Settings.SplitRecords;
        set => SaveSettings(s => s with { SplitRecords = value });
    }
```

Trong `SaveSettings`, sau `OnPropertyChanged(nameof(SplitModeIndex));` thêm `OnPropertyChanged(nameof(SplitRecords));`.

- [ ] **Step 4: Giao diện**

`MainWindow.axaml`, ngay sau `</ComboBox>` của ô "Điểm cắt gói" (trước `TextBlock` `SplitDelayLabel`):

```xml
          <CheckBox IsChecked="{Binding SplitRecords}" Content="{x:Static app:Strings.SplitRecordsLabel}" />
          <TextBlock Classes="secondary" Text="{x:Static app:Strings.SplitRecordsHint}" TextWrapping="Wrap" />
```

- [ ] **Step 5: Hướng dẫn**

`docs/guide/05-nang-cao.md`, thêm dòng ngay dưới dòng "Điểm cắt gói" của bảng:

```markdown
| **Tách TLS record** | bật / tắt (mặc định tắt) | Gửi ClientHello thành hai TLS record, cắt tại điểm chọn ở ô trên, để bộ lọc ghép lại gói TCP vẫn không thấy trọn tên miền. Chỉ thử khi Kiểm tra vẫn báo "Không vào được" với cả hai điểm cắt. Bật mà lỗi thì tắt đi rồi tắt/bật lại Proxy SNI. |
```

- [ ] **Step 6: Build và chạy test**

Run: `dotnet build -warnaserror && dotnet test tests/LunaDns.App.Tests`
Expected: build sạch (compiled bindings bắt lỗi tên `SplitRecords` lúc build), mọi test PASS.

- [ ] **Step 7: Commit**

```bash
git add src/LunaDns.App docs/guide/05-nang-cao.md tests/LunaDns.App.Tests/StringsTests.cs
git commit -m "feat(app): công tắc Tách TLS record ở mục Nâng cao"
```

---

### Task 6: Kiểm tra toàn bộ và thử tay

- [ ] **Step 1: Toàn bộ test**

Run: `dotnet build -warnaserror && dotnet test tests/LunaDns.Core.Tests && dotnet test tests/LunaDns.App.Tests && dotnet test tests/LunaDns.Platform.MacOS.Tests`
Expected: PASS hết. Trên Windows thêm `dotnet test tests/LunaDns.Platform.Windows.Tests`.

- [ ] **Step 2: Thử tay Windows** (`pwsh build/publish.ps1 -SkipZip -SkipInstaller`, chạy `artifacts/publish/win-x64/LunaDNS.exe` bằng quyền Administrator)

1. Mở Nâng cao: thấy ô "Tách TLS record", mặc định không tick.
2. Tick ô; `%ProgramData%\LunaDNS\settings.json` có `"SplitRecords": true`.
3. Chạy Kiểm tra: kết quả TLS không tệ hơn khi tắt.
4. Bật Định tuyến DNS + Proxy SNI, mở Steam Store và Community: tải được.
5. Lặp 3–4 với "Giữa tên miền (SNI)".
6. Bỏ tick, tắt/bật Proxy SNI: hoạt động như trước.

- [ ] **Step 3: Thử tay macOS** (`build/run-macos.sh`): mục Nâng cao vẫn ẩn; bật/tắt Định tuyến DNS và Proxy SNI chạy như trước (công tắc mặc định tắt nên hành vi không đổi).

Ghi kết quả thử tay (mạng, nhà mạng, điểm cắt nào qua) vào mô tả PR. Phiên agent không có máy Windows: để Step 2 cho user và ghi rõ là chưa thử.
