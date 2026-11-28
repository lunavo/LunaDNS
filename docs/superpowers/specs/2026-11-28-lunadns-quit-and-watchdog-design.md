# B8 + F5: Thoát khi dịch vụ nền không trả lời, và báo watchdog dừng trên macOS (thiết kế)

Ngày: 28/11/2026. Nguồn: review đối kháng sau v0.10.0, mục B8 (lỗi, macOS) và F5 (tính năng, macOS); cả hai để riêng
trong `2026-11-22-lunadns-auto-resume-design.md` (mục Ngoài phạm vi).

## Vấn đề

- **B8.** `DaemonFeatureController.ShutdownAsync` trả 0 mà không gửi gì khi snapshot không phải `DaemonRunning`: lần
  poll gần nhất lỗi thì snapshot là "tắt hết", `Resume` null. Gửi `disable-all` mà không có trả lời cũng trả 0. Cả hai
  trường hợp, **Thoát & khôi phục** đóng app êm như đã khôi phục, trong khi dịch vụ nền vẫn định tuyến DNS/proxy và
  giữ wanted, nên lần khởi động máy sau tính năng tự bật lại.
- **F5.** `FeatureSet` của daemon phát `FeatureStopped` khi watchdog bỏ cuộc, nhưng không ai đăng ký.
  `DaemonFeatureController.FeatureStopped` là event rỗng. Trên Mac thẻ chỉ chuyển sang tắt, không có
  "Đã tự tắt vì lỗi" và thông báo như trên Windows.

## Quyết định PO (28/11)

1. Thoát mà dịch vụ nền vẫn không trả lời sau khi thử lại: **báo rồi vẫn thoát** (hộp thoại một nút OK, như luồng
   `RestoreFailed` hiện có).
2. Lần dừng watchdog xảy ra trước khi app mở (hoặc lúc app chưa nối được) **không báo**; banner U1 "Bật lại" đã lo khi
   wanted còn.

## B8: thiết kế

`DaemonFeatureController.ShutdownAsync(ShutdownReason.UserQuit)`:

1. `Availability` là `DaemonNotInstalled` hoặc `DaemonOtherUser` → trả 0, không gửi (như cũ). Tài khoản khác đang ngồi
   trước máy (B4) thì tính năng đang chạy là của người đó; thoát app này không được tắt chúng.
2. `Availability == DaemonRunning` và không có gì để tắt (không tính năng nào bật, journal 0/null, không còn wanted) →
   trả 0, không gửi (như cũ).
3. Mọi trường hợp còn lại (gồm `DaemonConnecting`, `DaemonNotResponding`, snapshot cũ) → gửi `disable-all`.
   Không có trả lời (`SendAsync` trả null) → chờ 1 giây, gửi lại đúng 1 lần.
4. Có trả lời → trả `Remaining(response)` như cũ. Câu từ chối (ví dụ tài khoản khác đang ngồi trước máy, B4) là có trả
   lời, không phải "không rõ".
5. Cả hai lần đều không có trả lời → trả `ShutdownOutcome.Unknown` = **-1**.

`ShutdownReason.SessionEnd` giữ nguyên hoàn toàn: điều kiện cũ, gửi đúng 1 lần, không thử lại, trong giới hạn 4 giây của
`App`. Không có trả lời thì trả -1 (chỉ để ghi log).

`IFeatureController.ShutdownAsync`: tài liệu thêm "-1 = không biết đã hoàn tác chưa (dịch vụ nền không trả lời)". Hằng
`ShutdownOutcome.Unknown` đặt cạnh `ShutdownReason`. `LocalFeatureController` không bao giờ trả -1.

`App.QuitAsync`:

- `result == -1` → `InformAsync(AppTitle, Strings.RestoreUnknown_Mac)` rồi thoát.
- `result > 0` → như cũ (`RestoreFailed`).
- `OnShutdownRequested`: -1 ghi log (`LogSessionEndLeftovers` hoặc dòng log mới), không hộp thoại.

## F5: thiết kế

### Daemon

- `DaemonCommands` đăng ký `FeatureSet.FeatureStopped` khi tạo (cùng vòng đời với `FeatureSet` nên không cần bỏ đăng
  ký). Phần ghi nhớ tách thành lớp nhỏ `WatchdogStops` để test riêng. Mỗi lần nhận: tăng `Seq`
  (`long`, bắt đầu 0, chỉ trong bộ nhớ; daemon khởi động lại thì về 0), ghi `Feature` và `ChangesLeftBehind`. Ghi bằng
  `Interlocked`/khoá nhỏ vì event đến từ luồng watchdog.
- `DaemonStatus` thêm trường tuỳ chọn `DaemonStopped? Stopped = null`:
  `record DaemonStopped(long Seq, string Feature, bool ChangesLeftBehind)`. `Feature` ghi bằng `FeatureKeys.For`
  (như các lệnh `enable`/`disable`), app đọc bằng `FeatureKeys.TryParse`. `Seq == 0`
  (chưa dừng lần nào) thì không gửi. Đăng ký kiểu trong context JSON của `DaemonProtocol`.
- App cũ bỏ qua trường lạ; daemon cũ không gửi → app mới không bao giờ phát event (như hôm nay).

### App

`DaemonFeatureController`:

- `FeatureStopped` thành event thật (bỏ add/remove rỗng và chú thích dòng 50).
- Giữ `_stoppedSeq` (`long?`, null = chưa có mốc). Daemon khởi động lại nhận ra qua `Seq` nhỏ hơn mốc.
- Mỗi câu trả lời có `Status`:
  - `_stoppedSeq` null → đặt mốc = `Stopped?.Seq ?? 0`, không phát.
  - `Stopped?.Seq ?? 0` nhỏ hơn mốc (daemon đã khởi động lại) → đặt mốc mới, không phát.
  - Lớn hơn mốc → đặt mốc mới, phát **một** `FeatureStopped(feature, changesLeftBehind)` cho lần dừng gần nhất (nếu
    giữa hai lần poll có nhiều lần dừng thì chỉ báo lần cuối). `Feature` không parse được → không phát, chỉ cập nhật mốc.
- Mất kết nối (snapshot về Not*) **giữ** mốc, để nối lại không báo trùng.
- Phát event ngoài `_gate` (sau khi nhả khoá), để handler gọi lại controller không bị kẹt.

`MainViewModel` không đổi: `OnFeatureStopped` đã `Dispatcher.UIThread.Post` và hiện thẻ lỗi + thông báo.

## Chữ trên giao diện

| Khoá | Câu |
|---|---|
| `RestoreUnknown` | Không biết đã hoàn tác xong chưa. Hãy mở lại LunaDNS hoặc chạy LunaDNS.exe --cleanup. |
| `RestoreUnknown_Mac` | Dịch vụ nền không trả lời nên LunaDNS có thể vẫn đang định tuyến. Mở Terminal và chạy: sudo lunadns off |

`Strings.Get` dùng `X_Mac` thay `X` khi chạy trên macOS, và `StringsTests` đòi mọi khoá `_Mac` có khoá chung, nên cần cả
hai. Thực tế chỉ macOS hiện câu này (`LocalFeatureController` không trả -1). App chỉ có `Strings.resx` tiếng Việt.

## Ngoài phạm vi

- Lý do chi tiết watchdog dừng (lỗi socket nào): `FeatureStoppedEventArgs` không mang lý do; thêm sau nếu cần.
- Lưu lần dừng qua lần daemon khởi động lại.
- Windows: không đổi (đã có event thật từ `FeatureSet` cùng tiến trình).

## Kiểm thử

- `DaemonFeatureControllerTests` (transport giả):
  - UserQuit khi lần poll gần nhất lỗi (`DaemonNotResponding`) vẫn gửi `disable-all`.
  - Lần gửi đầu lỗi, lần hai trả lời → trả `Remaining`, gửi đúng 2 lần.
  - Cả hai lần lỗi → trả -1.
  - `DaemonNotInstalled` → không gửi, trả 0.
  - SessionEnd lỗi → gửi đúng 1 lần, trả -1.
  - Lần đầu thấy `Stopped.Seq = 3` → không phát; poll sau `Seq = 4` → phát đúng 1 lần với đúng feature/cờ; poll lại
    `Seq = 4` → không phát; `Seq = 1` (daemon khởi động lại) → không phát; sau đó `Seq = 2` → phát.
  - Mất kết nối rồi nối lại cùng `Seq` → không phát.
- `DaemonTests` (Platform.MacOS): `FeatureSet` giả phát `FeatureStopped` → `status` có `Stopped.Seq` tăng và đúng
  feature; chưa dừng → không có `Stopped`.
- `StringsTests` hiện có (khoá `_Mac` phải có khoá chung) phủ hai khoá mới.
- Verify: `dotnet test` cả solution (Windows: trượt sẵn 5 test MacOS/* của App.Tests `-f net8.0` là mốc cũ); CI
  `build-test-macos` xanh.
