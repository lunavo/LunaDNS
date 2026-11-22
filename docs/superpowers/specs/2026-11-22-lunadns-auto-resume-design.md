# F1 + U1: Nhớ trạng thái bật và tự bật lại (thiết kế)

Ngày: 22/11/2026. Nguồn: review đối kháng sau v0.10.0, mục F1 (tính năng) và U1 (lỗi trải nghiệm, mức Cao); F5 và B8 liên quan.
Lộ trình: [2026-11-21-lunadns-lo-trinh-tinh-nang.md](2026-11-21-lunadns-lo-trinh-tinh-nang.md), mục "Tự bật khi khởi động máy trên Windows".

## Vấn đề

- **macOS:** sau khi khởi động lại máy, hoặc sau **Cài lại dịch vụ nền** (thường bấm ngay sau khi nâng cấp app), mọi
  tính năng tắt mà không ai báo. `Installer.Bootout` gửi SIGTERM cho daemon cũ, daemon tắt hết và hoàn tác. Daemon mới
  (`Engine`) hoàn tác journal còn sót rồi chỉ chờ lệnh (spec gốc 4.7: "không tự bật tính năng nào"). App chỉ báo "Đã cài".
- **Windows:** app hoàn tác journal còn sót lúc mở (`LocalFeatureController`), rồi để mọi công tắc tắt.
- Kết quả: Steam hỏng lại sau mỗi lần khởi động máy, cho tới khi người dùng mở app và bật tay.

## Quyết định

1. **Nhớ "trạng thái muốn có"** (wanted) của hai tính năng **Định tuyến DNS** và **Proxy SNI**. **Chế độ PUBG không
   bao giờ tự bật**: nó có cảnh báo riêng và chỉ dùng lúc chơi.
2. Wanted chỉ đổi khi **người dùng ra lệnh tường minh**:
   - bật một tính năng thành công (`Enabled` hoặc `Unchanged`) → wanted của tính năng đó = bật;
   - tắt một tính năng thành công → wanted của tính năng đó = tắt;
   - **Tắt hết & khôi phục**, `lunadns off`, **Thoát & khôi phục** (menu khay) và **Cmd+Q / Quit trên Dock** (macOS)
     → wanted = không có gì.
   - Bật lỗi, hoặc đang chờ xác nhận ghi đè proxy (`NeedsProxyOverride`) → wanted giữ nguyên.
3. Những đường tắt **không phải ý người dùng** giữ nguyên wanted: SIGTERM (tắt máy, `launchctl bootout` khi cài lại),
   đăng xuất/tắt máy làm app thoát, watchdog dừng tính năng, áp danh sách tên miền hay cấu hình cắt gói lỗi.
4. **Công tắc "Tự bật lại"**, mặc định **bật**. Tắt công tắc thì vẫn nhớ wanted (để U1 còn đề nghị được), chỉ không tự bật.
5. Lúc khởi động (daemon macOS; lúc mở app trên Windows): hoàn tác journal như cũ; nếu hoàn tác **sạch**, công tắc bật và
   wanted có gì thì bật lại theo thứ tự Định tuyến DNS → Proxy SNI. **Không bao giờ tự ghi đè proxy/PAC của máy**
   (`allowProxyOverride = false`), kể cả khi lần trước người dùng đã đồng ý: cấu hình proxy có thể đã đổi.
   Hoàn tác không sạch thì không tự bật (journal còn mục cũ, bật thêm dễ chồng chéo).
6. Bật lỗi (`Failed`) thì thử lại sau 5 giây rồi 15 giây (lúc khởi động máy, mạng và `configd` có thể chưa sẵn sàng).
   `NeedsProxyOverride`, `NoDomains` không thử lại. Người dùng ra lệnh tường minh trong lúc đó thì dừng thử lại.
7. **U1: thông báo kèm nút.** Khi `wanted − đang chạy` khác rỗng, không đang tự bật dở, app hiện banner
   "**X** đang tắt dù lần trước bạn để bật." + lý do, cùng nút **Bật lại** và nút đóng. Lý do:
   - công tắc Tự bật lại đang tắt → "Tự bật lại đang tắt.";
   - lần tự bật cuối lỗi (cho đúng tính năng còn thiếu) → câu lỗi quen thuộc (`ErrorMessages`), hoặc với
     `NeedsProxyOverride` → "Máy đang dùng proxy/PAC; bấm Bật lại để xác nhận.";
   - còn lại (đăng xuất rồi đăng nhập lại, watchdog dừng…) không thêm lý do.
   **Bật lại** đi đúng đường bật tay (có hộp thoại xác nhận ghi đè proxy), nên cũng ghi wanted như bật tay.
   Nút đóng chỉ ẩn banner tới khi tập tính năng còn thiếu đổi.
8. **Cài lại dịch vụ nền từ bản cũ chưa biết wanted** (v0.10.0 trở về trước): trước khi cài, app chụp tập tính năng đang
   bật. Cài xong, chờ daemon mới tự bật xong (tối đa ~10 giây). Nếu daemon mới có công tắc Tự bật lại bật, app tự bật
   lại phần còn thiếu (đường bật tay, lỗi hiện trên thẻ như thường); công tắc tắt thì hiện banner U1 với tập đã chụp.
   Tập đã chụp hết hiệu lực khi người dùng bật/tắt bất kỳ tính năng nào, bấm Tắt hết, hoặc đóng banner.
9. **Windows làm luôn**, dùng chung lõi: wanted và công tắc nằm trong `settings.json` của app; tự bật lại chạy **lúc mở
   app** (sau khi hoàn tác journal). Việc app tự chạy khi đăng nhập vẫn là mục riêng của lộ trình; có nó thì F1 trên
   Windows thành "tự bật khi khởi động máy".

## Phân biệt Cmd+Q với đăng xuất/tắt máy (macOS)

Avalonia phát `ShutdownRequested` cho cả Cmd+Q, Quit trên Dock, đăng xuất và tắt máy (`applicationShouldTerminate`).
Nếu coi tất cả là "Thoát & khôi phục", lúc khởi động lại máy app sẽ xoá wanted trước khi daemon nhận SIGTERM, và F1 mất
tác dụng đúng ở trường hợp chính. Cách phân biệt: trong lúc xử lý, đọc Apple Event hiện tại
(`NSAppleEventManager.sharedAppleEventManager.currentAppleEvent`) qua libobjc. loginwindow gửi `quit` kèm thuộc tính
`keyAEQuitReason` (`'why?'`) = `logo`/`rlgo`/`rrst`/`rest`/`rsdn`/`shut`. Cmd+Q (menu) không có Apple Event; Quit trên
Dock hay `osascript … quit` có Apple Event nhưng không có lý do. Có lý do → **hết phiên** (giữ wanted); không có, hoặc đọc
lỗi → **người dùng thoát** (xoá wanted). Đọc lỗi mà xoá nhầm thì U1 vẫn đề nghị bật lại, nên đây là phía an toàn.

Windows: `ShutdownRequested` chỉ đến từ `WM_QUERYENDSESSION` → luôn là hết phiên.

## Thiết kế

### Core

- `WantedFeatures(bool DnsRouting, bool SniProxy)`: `None`, `IsEmpty()`, `With(feature, on)`, `Missing(FeatureStatus)`,
  `Union`, `List()`. Hàm chứ không phải thuộc tính, để không ra JSON.
- `AppSettings` thêm `AutoResume` (mặc định `true`) và `Wanted` (mặc định `None`). File cũ không có hai trường này
  đọc ra mặc định.
- `ResumeState(bool Enabled, WantedFeatures Wanted, bool Restoring, FeatureResult? Failure)`: ảnh chụp cho giao diện.
- `FeatureResumer` bọc `FeatureSet` + đọc/ghi settings:
  - `RunAsync(bool rollbackClean, CancellationToken)`: bật lại theo mục 5–6; giữ `Failure` của lần thử cuối.
  - `Record(Feature, bool on)` / `Forget()`: ghi wanted theo mục 2, dừng vòng thử lại, xoá `Failure` của tính năng đó.
    Ghi file lỗi thì log, không làm hỏng lệnh bật/tắt.
  - `SetEnabled(bool)`: lưu công tắc.
  - `State`.

### Daemon macOS

- `DaemonCommands` nhận `FeatureResumer?` (null với `sudo lunadns run`: chế độ tiền cảnh không nhớ, không tự bật).
  `enable`/`disable` thành công gọi `Record`; `disable-all` gọi `Forget`, trừ khi yêu cầu có `sessionEnd: true`.
- Lệnh mới `set-auto-resume` (kèm `enabled`). Mọi câu trả lời có trạng thái kèm thêm
  `resume: { enabled, dnsRouting, sniProxy, restoring, failedFeature?, outcome?, error? }`.
  App mới gặp daemon cũ: không có `resume` → ẩn công tắc; `set-auto-resume` trả "Lệnh lạ:" → đề nghị cài lại.
  Daemon cũ gặp app mới: bỏ qua trường lạ `sessionEnd`, `disable-all` chạy như cũ.
- `Program.DaemonAsync`: mở socket **trước**, rồi mới chạy `RunAsync` ở nền (installer chỉ chờ `ping` 5 giây; bật lại
  có thể lâu hơn). SIGTERM huỷ vòng thử lại rồi tắt như cũ.
- Tên op/feature trong journal không đổi.

### App

- `IFeatureController`: thêm `Resume` (null = không biết), `ResumeAsync()` (Local: chạy `RunAsync`; daemon: không làm gì),
  `SetAutoResumeAsync(bool)`; `ShutdownAsync(ShutdownReason)` với `UserQuit` / `SessionEnd`.
- `DaemonFeatureController.ShutdownAsync(UserQuit)` vẫn gửi `disable-all` khi không có gì đang bật mà wanted còn, để
  Cmd+Q xoá được wanted (B8 để riêng).
- `ResumeNotice` (thuần, test được): từ availability, `Resume`, trạng thái, tập chụp lúc cài lại, tập đã đóng → có hiện
  không, câu chữ, tập cần bật.
- `MainViewModel`: banner U1 dưới banner dịch vụ nền; ô **Tự bật lại khi khởi động** dưới hai thẻ công tắc ở khu Trạng
  thái (ẩn khi `Resume` null). Windows: `Start()` gọi `ResumeAsync` trong `RunBusyAsync` (thẻ hiện đang xử lý).
- `App`: menu khay Thoát → `UserQuit`; `ShutdownRequested` → macOS hỏi `QuitReason`, Windows luôn `SessionEnd`.

### Chữ trên giao diện

| Khoá | Câu |
|---|---|
| `AutoResumeSwitch` (macOS) | Tự bật lại sau khi khởi động máy hoặc cài lại dịch vụ nền |
| `AutoResumeSwitch` (Windows) | Tự bật lại khi mở LunaDNS |
| `AutoResumeHint` | Bật lại đúng những gì bạn để bật lần trước. Thoát &amp; khôi phục hoặc Tắt hết thì lần sau không tự bật. |
| `ResumeNoticeFormat` | {0} đang tắt dù lần trước bạn để bật. |
| `ResumeNoticeSwitchOff` | Tự bật lại đang tắt. |
| `ResumeNoticeProxy` | Máy đang dùng proxy hoặc PAC; bấm Bật lại để xác nhận. |
| `ResumeButton` | Bật lại |

## Ngoài phạm vi

- B8 (Thoát lúc lần poll gần nhất lỗi), F5 (daemon báo lý do watchdog dừng): banner U1 đã che một phần F5 (tính năng
  dừng mà wanted còn thì có nút Bật lại), phần còn lại để riêng.
- App tự chạy khi đăng nhập (Windows, macOS).

## Kiểm thử

- Core: `FeatureResumer` (bật theo thứ tự, bỏ PUBG, không ghi đè proxy, thử lại rồi dừng, dừng khi có lệnh tường minh,
  công tắc tắt, hoàn tác không sạch), `WantedFeatures`, settings cũ đọc ra mặc định.
- Platform.MacOS: `DaemonCommands` ghi wanted, `sessionEnd` giữ wanted, `set-auto-resume`, dạng dây của `resume`.
- App: `DaemonFeatureController` (Resume, ShutdownReason, set-auto-resume với daemon cũ), `LocalFeatureController`
  (ghi wanted, tự bật lúc mở), `ResumeNotice`.
- Máy thật (`build/run-macos.sh`): bật DNS → cài lại dịch vụ nền → tự bật lại; tắt công tắc → cài lại → banner + Bật lại;
  Cmd+Q → wanted rỗng; Apple Event quit có lý do `rest` → wanted giữ.
