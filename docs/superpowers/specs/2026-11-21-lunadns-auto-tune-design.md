# D3: Tìm cấu hình hợp mạng (thiết kế)

Ngày: 21/11/2026. Lộ trình: [2026-11-21-lunadns-lo-trinh-tinh-nang.md](2026-11-21-lunadns-lo-trinh-tinh-nang.md) mục D3.
Dùng lựa chọn của D1 ([tách TLS record](2026-11-21-lunadns-tls-record-split-design.md)) và D2
([máy chủ DoH](2026-11-21-lunadns-doh-providers-design.md)). Lấy ý từ Flowseal *Run Tests* và zapret `blockcheck`.

## Vấn đề

Sau D1 và D2, người dùng có 4 kiểu cắt (`SplitMode` × `SplitRecords`), độ trễ 0–20 ms và 4–5 máy DoH. Chỉ người
rành mạng mới biết chọn gì. Kiểm tra hiện nay chỉ thử **một** cấu hình (cài đặt đang lưu, không dùng độ trễ), nên khi
cấu hình đó không qua, người dùng không biết nên đổi gì.

## Hành vi

- Khối Kiểm tra có thêm nút **"Tìm cấu hình hợp mạng"**, cạnh "Áp dụng đề xuất". Nút nằm ngoài mục Nâng cao, nên hiện
  trên cả **Windows và macOS**.
- Bấm nút là đồng ý cho app tự đổi cài đặt. App dò trong tối đa **60 giây**, theo ba bước:
  1. **DoH.** Đo mọi lựa chọn dùng được (`DohChoices()`) bằng `DohLatencyMeter` (D2), chọn máy nhanh nhất. Mọi máy
     đều lỗi thì dừng, không đổi gì.
  2. **Phân loại.** Chạy `Prober` trên 4 host Steam (`ProbeTargets.Steam`, không theo lựa chọn nhóm tên miền), dùng
     máy DoH vừa chọn và tổ hợp cắt đang dùng. Không host nào `SniFiltered` hay `Unreachable` thì bỏ bước 3. Khi đó
     biện pháp cần bật lấy theo verdict (`ProbeAdvisor.Recommend`), như Áp dụng đề xuất.
  3. **Thử kiểu cắt.** Thử lần lượt từng tổ hợp, tối đa 8. Trong một tổ hợp, 4 host bắt tay TLS song song, có cắt gói,
     tới IP đầu tiên DoH trả về, chứng chỉ kiểm theo host, mỗi host timeout 3 giây.
     - Tổ hợp **chạy được** khi cả 4 host qua. Gặp tổ hợp đầu tiên chạy được thì dừng.
     - Tìm được thì biện pháp cần bật là Định tuyến DNS + Proxy SNI.
     - Không tổ hợp nào chạy thì không đổi gì, và báo các host vẫn lỗi.
- **Thứ tự tổ hợp** đi từ ít can thiệp tới nhiều. Đầu tiên là tổ hợp đang dùng, để mạng vẫn ổn thì không đổi gì. Sau đó
  thử theo bảng dưới, bỏ tổ hợp trùng với tổ hợp đang dùng:

  | # | Điểm cắt | Tách record | Độ trễ |
  |---|---|---|---|
  | 1 | Sau byte đầu tiên | tắt | 0 ms |
  | 2 | Sau byte đầu tiên | tắt | 10 ms |
  | 3 | Giữa SNI | tắt | 0 ms |
  | 4 | Giữa SNI | tắt | 10 ms |
  | 5 | Sau byte đầu tiên | bật | 0 ms |
  | 6 | Sau byte đầu tiên | bật | 10 ms |
  | 7 | Giữa SNI | bật | 0 ms |
  | 8 | Giữa SNI | bật | 10 ms |

  Tổ hợp đang dùng nằm trong bảng thì tổng là 8 tổ hợp. Nó không nằm trong bảng (ví dụ độ trễ 5 ms) thì nó được thử đầu
  tiên, rồi tới 7 dòng đầu của bảng, để tổng vẫn là 8.
- **Áp tự động** khi tìm được, theo thứ tự:
  1. lưu máy DoH, điểm cắt, tách record, độ trễ;
  2. chạy lại các tính năng đang bật để nhận cài đặt mới;
  3. bật thêm biện pháp còn thiếu;
  4. báo một dòng, ví dụ "Đã chọn Quad9 · giữa SNI · tách record · 0 ms". Không hỏi thêm.

  Mọi cài đặt vẫn sửa tay được ở mục Nâng cao (Windows).
- **Tiến độ và huỷ.** Khi đang dò, nút thay bằng nút **"Dừng"** và có dòng tiến độ ("Đang đo DoH…", "Đang thử 3/8: giữa
  SNI, 0 ms"). Bấm Dừng, đóng cửa sổ hoặc hết 60 giây trong bước 1–3 thì huỷ mọi lượt đang chạy, không lưu, không bật
  gì, và báo "Đã dừng, cài đặt giữ nguyên". Đã sang bước áp thì chạy cho xong, nút Dừng bị khoá.
- Kết quả bước phân loại thay khối kết quả Kiểm tra (chỉ nhóm Steam), để Áp dụng đề xuất và Chẩn đoán dùng được. Khi
  tìm được tổ hợp, host "không vào được" ở bước phân loại hiện là "bị lọc SNI", vì tổ hợp mới đã qua. Các dòng nhóm tên
  miền không đổi.
- Trong lúc dò, Kiểm tra, Áp dụng đề xuất và Đo bị khoá. Thẻ tính năng chỉ bị khoá ở bước áp.
- Thời gian thường gặp: bước 1 khoảng 1–3 giây, bước 2 ≤ 9 giây, bước 3 ≤ 8 × 3 giây. Tổng xấu nhất khoảng 40 giây, dưới
  mốc 60 giây.

### Trên macOS

- Nút có ở macOS. App dò bằng quyền user (socket thường, không cần root), với 4 máy DoH dựng sẵn; macOS không có ô Tự
  nhập.
- Tổ hợp "đang dùng" lấy từ daemon (`get-tuning`). Hỏi không được thì coi tổ hợp đang dùng là mặc định
  (`HelloSplit.Default`, 0 ms, Cloudflare).
- Áp bằng lệnh mới `set-tuning`. Daemon kiểm lại dữ liệu, chạy lại tính năng, rồi mới ghi `settings.json` của root,
  theo mẫu `set-domains`. Biện pháp còn thiếu bật qua `enable` như hiện nay.
- Daemon cũ chưa biết lệnh: trả "Lệnh lạ:". App báo `DaemonNeedsUpdate`, kèm tổ hợp tìm được, và không áp gì.
- Mục Nâng cao trên macOS vẫn ẩn. Đưa nó lên macOS là việc riêng.

## Thành phần

```csharp
namespace LunaDns.Core.Probing;

public sealed record TuneCandidate(HelloSplit Split, int DelayMs);

public enum TuneOutcome { Found, SplitNotNeeded, DohFailed, NothingWorked }

public sealed record TuneResult(
    TuneOutcome Outcome,
    DohProvider? Doh,               // máy DoH đã chọn; null khi DohFailed
    TuneCandidate? Candidate,       // tổ hợp tìm được; khi SplitNotNeeded là tổ hợp đang dùng; null khi DohFailed/NothingWorked
    ProbeRemedies Remedies,         // biện pháp cần bật
    IReadOnlyList<ProbeResult> Probe,       // kết quả bước 2, để hiện lên dòng nhóm Steam
    IReadOnlyList<string> FailingHosts);    // NothingWorked: host vẫn lỗi ở tổ hợp qua nhiều host nhất

public sealed record TuneProgress(TuneStage Stage, int Index, int Total, TuneCandidate? Candidate);
public enum TuneStage { MeasuringDoh, Classifying, TryingSplit }

/// <summary>Cấu hình đang dùng ở nơi giữ thật (Windows: settings của app; macOS: daemon).</summary>
public sealed record TuningState(DohProvider Doh, TuneCandidate Candidate);
```

### Core (dùng chung Windows và macOS)

| Đơn vị | Thay đổi |
|---|---|
| `Probing/TuneCandidates` (mới, hàm thuần) | `Ordered(TuneCandidate current)`: tối đa 8 tổ hợp theo thứ tự ở trên, không trùng. `Table` là bảng 8 dòng. |
| `Probing/NetworkTuner` (mới) | `RunAsync(IReadOnlyList<string> hosts, IReadOnlyList<DohChoice> choices, TuneCandidate current, IProgress<TuneProgress>? progress, CancellationToken)`. Phụ thuộc nhận qua constructor: `Func<IReadOnlyList<DohChoice>, CancellationToken, Task<IReadOnlyList<DohLatency>>> measureDoh`, `Func<DohServer, TuneCandidate, Prober> prober` (dựng `Prober` với resolver DoH chỉ gồm máy đã chọn và `TlsProbe` của tổ hợp), `Func<TuneCandidate, ITlsProbe> tls`. Test thay bằng bản giả. Máy DoH đã chọn tra trong `choices` theo `DohLatency.Provider`. Bước 3 dùng IP DoH đã phân giải ở bước 2 (`ProbeResult.DohAddresses[0]`), không hỏi lại. Host DoH không cho IP thì không có tổ hợp nào chạy được cho host đó. |
| `Probing/TlsProbe` | Constructor nhận thêm `TimeSpan delay` (mặc định 0, chặn bằng `SplitWriter.MaxDelay`), truyền xuống `SplittingStream`. |
| `Probing/SplittingStream` | Nhận `TimeSpan delay`. Lần Write đầu ghi record đầu qua `SplitWriter.WriteAsync(inner, record, split, delay, ct)`, nên nghỉ giữa các phần giống hệt proxy; byte sau record đầu ghi nguyên. |
| `Settings/AppSettings` | `CurrentCandidate()`: `TuneCandidate(new HelloSplit(SplitMode, SplitRecords), clamp(SplitDelayMs))`. `WithTuning(DohProvider, TuneCandidate)`: `WithDoh(provider)` rồi đặt `SplitMode`, `SplitRecords`, `SplitDelayMs`. Cả hai là hàm, không ra JSON. |
| `Features/FeatureSet` | `ReapplyTuningAsync(CancellationToken)`: giữ `_gate`, gọi `RestartAsync(active)` để các tính năng đang bật đọc `_tuning()` và DoH mới. Trả `ReapplyResult`. Lỗi thì không tự quay lại, người gọi lo khôi phục, vì chỉ người gọi biết settings cũ. |

### Windows (app)

| Đơn vị | Thay đổi |
|---|---|
| `App/Features/IFeatureController` | Thêm `Task<TuningState?> GetTuningAsync()` và `Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate)`. `TuningApplyResult(bool Success, string? Message, FeatureError? Error)` theo mẫu `DomainApplyResult`. |
| `App/Features/LocalFeatureController` | `GetTuningAsync` trả từ settings của app. `ApplyTuningAsync` chỉ gọi `FeatureSet.ReapplyTuningAsync`. |
| `App/AppServices.ApplyTuningAsync` | Windows: lưu `WithTuning` trước (FeatureSet đọc settings lúc khởi động lại), gọi controller; lỗi thì lưu lại settings cũ và gọi lần nữa; khôi phục cũng lỗi thì thêm `Strings.ChangesLeftBehind`. macOS: gọi controller, thành công mới chép `WithTuning` vào settings của app để Kiểm tra dùng cùng cấu hình. |
| `App/AppServices` | `TuneAsync(IProgress<TuneProgress>, CancellationToken)`: lấy `current` qua `Features.GetTuningAsync()`. Không có thì dùng `Settings.CurrentCandidate()`, rồi tới mặc định. Phần phân giải lại Tự nhập của `MeasureDohAsync` tách thành hàm riêng `RefreshCustomDohAsync`, dùng chung cho cả hai. `choices = DohCatalog.Choices(resolved ?? Settings.CustomDoh)`; trên macOS chỉ có máy dựng sẵn vì app không có `CustomDoh`. `measureDoh` bọc `DohLatencyMeter`. Chạy `NetworkTuner`, lưu `LastProbe` để nút Chẩn đoán dùng, và trả `TuneRun(TuneResult Result, CustomDoh? ResolvedCustom)`. Không ghi settings ở luồng nền: giao diện áp `ResolvedCustom` ở luồng UI, như nút Đo. |
| `App/ViewModels/MainViewModel` | `TuneCommand`, `StopTuneCommand` (lệnh riêng, vì `AsyncCommand` không cho bấm lại khi đang chạy), `IsTuning`, `TuneStatus` (dòng tiến độ / kết quả). `CheckCommand`, `ApplyRecommendedCommand`, `MeasureDohCommand` khoá khi `IsTuning`. `CancellationTokenSource` 60 giây, nối với huỷ khi đóng cửa sổ, theo mẫu `_cancelDoh` của nút Đo. Có kết quả `Found` / `SplitNotNeeded` thì gọi `ApplyTuningAsync` (bỏ qua nếu DoH và tổ hợp không đổi), rồi bật biện pháp còn thiếu bằng `EnableAsync` (DNS trước, proxy sau, như `ApplyRecommendedAsync`). Cuối cùng báo đổi `DohIndex`, `SplitModeIndex`, `SplitRecords`, `SplitDelayMs`. Nút Dừng chỉ bấm được trong bước 1–3. Câu hiển thị nằm ở `App/TuneText` (hàm thuần, có test). |
| `App/Views/MainWindow.axaml` | Trong khối Kiểm tra: nút dò (`IsVisible="{Binding !IsTuning}"`), nút Dừng (`IsVisible="{Binding IsTuning}"`), một dòng `TuneStatus`. |
| `App/Strings` (+ `Strings.resx`) | `TuneButton`, `TuneStop`, `TuneMeasuringDoh`, `TuneClassifying`, `TuneTryingFormat` ("Đang thử {0}/{1}: {2}"), `TuneCandidateFormat` ("{0} · {1} · {2} ms"), `TuneRecordsOn`, `TuneChosenFormat` ("Đã chọn {0}"), `TuneNoChange` ("Cấu hình hiện tại đã chạy, không cần đổi"), `TuneDohFailed`, `TuneNothingWorkedFormat` ("Không tổ hợp nào qua; vẫn lỗi: {0}"), `TuneCancelled`, `TuneFoundNotAppliedFormat` ("Tìm được {0}. {1}", cho mọi lỗi áp), `TuneSplitFirstByte`, `TuneSplitMidSni`, `TuneRecordsOff`, `DaemonNeedsUpdateTuning` (câu `DaemonNeedsUpdate` hiện nói về danh sách tên miền). |
| `docs/guide/04-kiem-tra-va-de-xuat.md`, `09-macos.md` (+ bản `en`) | Mô tả nút mới, thứ tự thử, nút Dừng. macOS: cần cập nhật dịch vụ nền. |

### macOS (daemon)

| Đơn vị | Thay đổi |
|---|---|
| `Platform.MacOS/Daemon/DaemonProtocol` | Lệnh `get-tuning`, `set-tuning`. `DaemonRequest` và `DaemonResponse` thêm `DaemonTuning? Tuning`. `record DaemonTuning(DohProvider Doh, SplitMode SplitMode, bool SplitRecords, int SplitDelayMs)`, đăng ký trong `DaemonJsonContext`, enum dạng chuỗi camelCase. Client cũ bỏ qua trường lạ. |
| `Platform.MacOS/Daemon/DaemonCommands` | `get-tuning` trả `Tuning` từ `_settings`. `set-tuning` không tin dữ liệu gửi tới:<br>1. kiểm `Tuning` khác null, enum có định nghĩa, `Doh != Custom`, `0 ≤ SplitDelayMs ≤ AppSettings.MaxSplitDelayMs`; sai thì `Refused`;<br>2. cập nhật nơi giữ settings, rồi `ReapplyTuningAsync`;<br>3. ổn thì `settingsStore.Save`; lỗi chạy lại thì trả settings cũ và chạy lại;<br>4. ghi file lỗi thì trả settings cũ, chạy lại, rồi báo như `SetDomainsAsync`. |
| `MacCli/Engine` | Hiện bắt `settings` một lần lúc khởi động. Đổi thành một nơi giữ dùng chung (`SettingsHolder`: `Current { get; set; }`), cho `CoreFeatureServices(() => holder.Current.DohServers())`, `FeatureSet(..., () => holder.Current.Tuning())` và `DaemonCommands`. `set-domains` đọc/ghi qua cùng nơi giữ, để hai lệnh không ghi đè nhau. |
| `App/MacOS/DaemonFeatureController` | `GetTuningAsync` gửi `get-tuning`; "Lệnh lạ:" hoặc lỗi thì trả `null`. `ApplyTuningAsync` gửi `set-tuning`; "Lệnh lạ:" thì trả `Strings.DaemonNeedsUpdate`, như `ApplyDomainsAsync`. |
| `MacCli` (lệnh `lunadns`) | Không thêm lệnh dòng lệnh. |

## Luồng dữ liệu

Nút → `MainViewModel.TuneAsync` → `AppServices.TuneAsync`:
- `Features.GetTuningAsync()` (Windows: settings của app; macOS: `get-tuning`) cho `current`;
- `MeasureDohAsync` → `DohLatencyMeter`;
- `NetworkTuner`: `Prober` (DoH đã chọn, tổ hợp `current`) → `TuneCandidates.Ordered` → `TlsProbe(split, delay)` → `SplittingStream`.

Áp: `MainViewModel` → `Features.ApplyTuningAsync`:
- Windows: `AppSettings.WithTuning` → `settings.json` → `FeatureSet.ReapplyTuningAsync`;
- macOS: `set-tuning` → `DaemonCommands` → `SettingsHolder` → `FeatureSet.ReapplyTuningAsync` → `settings.json` của root.

Sau đó `EnableAsync` bật các biện pháp còn thiếu.

## Lỗi

- **Mọi máy DoH lỗi** (`DohFailed`): báo `TuneDohFailed`, không đổi gì.
- **Không tổ hợp nào chạy** (`NothingWorked`): không đổi gì. Báo các host vẫn lỗi ở tổ hợp qua được nhiều host nhất
  (hoà thì lấy tổ hợp đứng trước), kèm `UnreachableHint`.
- **Huỷ, hết giờ, đóng cửa sổ** trong bước 1–3: `OperationCanceledException`, báo `TuneCancelled`, không đổi gì.
- **Chạy lại tính năng lỗi khi áp**:
  - Windows khôi phục settings cũ và chạy lại một lần nữa;
  - macOS do daemon tự khôi phục;
  - báo lỗi kèm `ChangesLeftBehind` nếu khôi phục không được;
  - không bật thêm biện pháp nào.
- **Bật biện pháp còn thiếu lỗi**: hiện lỗi trên thẻ tính năng như Áp dụng đề xuất (`ShowFailures`). Cài đặt đã lưu
  vẫn giữ.
- **Daemon** không chạy, không trả lời, hoặc từ chối vì UID: báo như mọi lệnh (`ServiceUnavailable`). Daemon cũ: xem
  mục macOS ở trên.
- **Bấm Kiểm tra / Áp dụng đề xuất / Đo trong lúc dò**: các nút này bị khoá cho tới khi dò xong.
- **Dò khi proxy đang bật**: `TlsProbe` đi socket trần tới IP của DoH, không qua proxy hệ thống, nên vẫn đo đường mạng
  gốc. Định tuyến DNS đang bật cũng không ảnh hưởng, vì DoH đi qua `DohClient` riêng.
- **Hai lệnh cùng lúc trên daemon** (`set-domains` và `set-tuning`): `FeatureSet._gate` xếp hàng phần chạy lại. Cả hai
  ghi qua `SettingsHolder`, nên không lệnh nào ghi đè trường của lệnh kia.

## Kiểm thử

- `TuneCandidatesTests`:
  - đúng bảng 8 dòng;
  - tổ hợp đang dùng nằm trong bảng thì đi đầu và không lặp lại;
  - tổ hợp đang dùng ngoài bảng (5 ms) thì đi đầu, rồi tới 7 dòng đầu bảng;
  - luôn ≤ 8 tổ hợp.
- `NetworkTunerTests` (đo DoH giả, resolver giả, `ITlsProbe` giả theo bảng (tổ hợp, host) → bool):
  - chọn DoH nhanh nhất;
  - DoH lỗi hết thì ra `DohFailed`;
  - không host nào bị lọc thì ra `SplitNotNeeded` với remedies theo verdict;
  - dừng ở tổ hợp đầu tiên chạy được (đếm số lần gọi probe);
  - một host lỗi thì tổ hợp không đạt;
  - không tổ hợp nào chạy thì `FailingHosts` đúng;
  - huỷ giữa bước 3 ném `OperationCanceledException`;
  - `TuneProgress` báo đúng thứ tự và đúng `Total`.
- `SplittingStreamTests`, `TlsProbeTests`:
  - có độ trễ thì vẫn đúng số lần Write và byte nguyên vẹn (theo mẫu `SplitWriterTests.Delay_between_pieces_keeps_bytes_intact`);
  - độ trễ 0 thì như cũ;
  - bắt tay qua `LoopbackTlsServer` thành công với 10 ms.
- `AppSettingsTests`: `CurrentCandidate` chặn độ trễ; `WithTuning(Quad9, …)` đặt đủ 4 trường và `PreferGoogleDoh`; lưu
  rồi đọc lại.
- `FeatureSetTests`: `ReapplyTuningAsync` chạy lại đúng các tính năng đang bật, dùng tuning mới; không có tính năng nào
  bật thì không làm gì; lỗi thì trả `ReapplyResult` lỗi.
- `DaemonCommandsTests`:
  - `get-tuning`;
  - `set-tuning` hợp lệ thì chạy lại rồi lưu;
  - từ chối `Custom`, độ trễ 25 ms, thiếu `Tuning`;
  - chạy lại lỗi thì trả settings cũ;
  - ghi file lỗi thì chạy lại với bản cũ;
  - `set-domains` sau `set-tuning` giữ tuning mới.
- `DaemonProtocolTests`: round-trip JSON `DaemonTuning`; request cũ không có `tuning` vẫn đọc được.
- `DaemonFeatureControllerTests`: "Lệnh lạ:" thì `GetTuningAsync` trả `null` và `ApplyTuningAsync` trả `DaemonNeedsUpdate`.
- `StringsTests`: có chuỗi mới.
- Repo chưa có test dựng `MainViewModel`. Thuộc tính mới theo mẫu nút Đo của D2; compiled bindings bắt sai tên lúc
  build, còn hành vi kiểm bằng thử tay.
- Thử tay trên mạng thật, cả Windows và macOS:
  - bấm nút, xem tiến độ và kết quả;
  - bấm Dừng giữa chừng, rồi xem cài đặt giữ nguyên;
  - mở Steam Store và Community;
  - Windows: mục Nâng cao hiện đúng tổ hợp mới;
  - macOS: chạy với daemon cũ để thấy thông báo cần cập nhật.

## Ngoài phạm vi

- Thử cả ma trận DoH × kiểu cắt, hoặc thử máy DoH thứ hai khi không tổ hợp nào chạy.
- Độ trễ khác 0 và 10 ms; cắt hơn hai record.
- Tự dò lại định kỳ hoặc khi đổi mạng.
- Dò cho nhóm tên miền ngoài Steam.
- Đưa mục Nâng cao lên macOS; Tự nhập DoH trên macOS.
- Lệnh dò trong `lunadns` dòng lệnh.

## Thứ tự với các nhánh khác

D3 sửa cùng các file với D1 và D2 (`AppSettings`, `MainViewModel`, `MainWindow.axaml`, `Strings`, `TlsProbe`). Cả hai đã
merge vào `main` (PR #4, #5), nên tách nhánh từ `main`. D5 sắp lại khối Kiểm tra sau khi D3 xong. Merge qua PR để CI
chạy cả Windows và macOS.
