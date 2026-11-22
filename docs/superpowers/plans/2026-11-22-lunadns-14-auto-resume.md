# KH14: Tự bật lại (F1 + U1)

Spec: [2026-11-22-lunadns-auto-resume-design.md](../specs/2026-11-22-lunadns-auto-resume-design.md).

Lệnh kiểm (máy Mac):

```bash
~/.dotnet/dotnet build -warnaserror
~/.dotnet/dotnet test tests/LunaDns.Core.Tests
~/.dotnet/dotnet test tests/LunaDns.Platform.MacOS.Tests
~/.dotnet/dotnet test tests/LunaDns.App.Tests -f net8.0
```

(App.Tests có 19 test chỉ chạy trên Windows, đã biết là đỏ trên Mac.)

## T1. Core: WantedFeatures, AppSettings, FeatureResumer

1. Test trước (`tests/LunaDns.Core.Tests/Features/FeatureResumerTests.cs`, `Settings/…`):
   - settings.json cũ không có `AutoResume`/`Wanted` → `AutoResume = true`, `Wanted = None`; ghi rồi đọc lại giữ nguyên.
   - `RunAsync` bật DNS rồi SNI; PUBG không có trong wanted; `allowProxyOverride` luôn false (`NeedsProxyOverride` → Failure, không thử lại).
   - công tắc tắt hoặc hoàn tác không sạch → không bật, `Failure` đúng (`RollbackFailed` cho trường hợp sau).
   - bật lỗi → thử lại theo `retryDelays` (test truyền độ trễ 0), hết lượt thì giữ `Failure`.
   - `Record` trong lúc thử lại → dừng vòng thử; `Record`/`Forget` ghi settings; ghi lỗi không ném.
2. Code: `src/LunaDns.Core/Features/WantedFeatures.cs`, `ResumeState.cs`, `FeatureResumer.cs`; hai trường mới trong `AppSettings`.

## T2. Daemon macOS

1. Test (`tests/LunaDns.Platform.MacOS.Tests/DaemonResumeTests.cs`): enable/disable ghi wanted; disable-all xoá; disable-all
   `sessionEnd` giữ; `set-auto-resume`; câu trả lời có `resume`; `run` (không có resumer) không có `resume`; dạng dây.
2. Code: `DaemonProtocol` (lệnh `set-auto-resume`, `DaemonRequest.SessionEnd`, `DaemonResume`, `DaemonResponse.Resume`),
   `DaemonCommands`, `Engine` (tạo `FeatureResumer`), `Program.DaemonAsync` (mở socket rồi `RunAsync` nền), help/`status` CLI in dòng Tự bật lại.
3. `QuitReason` (libobjc) trong `LunaDns.Platform.MacOS/AppleEvents/`.

## T3. App

1. Test: `DaemonFeatureControllerTests` (Resume từ câu trả lời, `ShutdownAsync(UserQuit/SessionEnd)`, wanted còn mà không
   bật gì vẫn gửi disable-all khi UserQuit, `set-auto-resume` gặp daemon cũ); `LocalFeatureController` (ghi wanted, `ResumeAsync`,
   `ShutdownAsync(UserQuit)` xoá wanted); `ResumeNoticeTests`.
2. Code: `IFeatureController`, `ShutdownReason`, hai controller, `ResumeNotice`, `MainViewModel` (banner, ô công tắc,
   Bật lại, cài lại có chụp trước), `MainWindow.axaml`, `App.axaml.cs`, `Strings`.

## T4. Tài liệu

`docs/guide/03-ba-cong-tac.md`, `09-macos.md` và bản `en/`; lộ trình ghi F1 đã làm; spec gốc 4.7 trỏ sang spec này.

## T5. Kiểm trên máy thật

`build/run-macos.sh`; kịch bản ở cuối spec. Hỏi trước khi thoát bản LunaDNS trong /Applications.
