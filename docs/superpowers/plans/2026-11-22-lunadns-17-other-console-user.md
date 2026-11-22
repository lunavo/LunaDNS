# KH17: tài khoản khác đang ngồi trước máy (B4 + U3 + B11)

Spec: [2026-11-22-lunadns-other-console-user-design.md](../specs/2026-11-22-lunadns-other-console-user-design.md).

Lệnh kiểm (máy Mac):

```bash
~/.dotnet/dotnet build -warnaserror
~/.dotnet/dotnet test tests/LunaDns.Core.Tests
~/.dotnet/dotnet test tests/LunaDns.Platform.MacOS.Tests
~/.dotnet/dotnet test tests/LunaDns.App.Tests -f net8.0
```

## T1. Giao thức: mã từ chối

1. Test trước (`DaemonTests`): câu từ chối có `Code = "not-console-user"` và câu cũ; `IsNotConsoleUser` đúng với mã mới,
   đúng với câu cũ không mã, sai với câu khác hoặc khi có `Status`; JSON không có `code` đọc được.
2. Code: `DaemonProtocol.NotConsoleUser`, `NotConsoleUserMessage`, `IsNotConsoleUser`; `DaemonResponse.Code`;
   `DaemonServer` gửi mã.

## T2. `ConsoleUser` không sinh tiến trình (B11)

1. Test trước (`ConsoleUserTests`): hàm đọc giả cho UID/tên; "loginwindow" ra tên null; không đọc được → chỉ root.
   Test máy thật (chỉ macOS): UID của P/Invoke khớp `stat -f %u /dev/console` (stat ra 0 thì chấp nhận null).
2. Code: `ConsoleUser` dùng `SCDynamicStoreCopyConsoleUser`; `MacCli/Program.cs` bỏ `ProcessRunner` khi tạo.

## T3. Giao diện: `DaemonOtherUser`

1. Test trước: `DaemonFeatureControllerTests` (mã mới và câu cũ → `DaemonOtherUser`; câu khác → `DaemonNotResponding`);
   `ServiceBannerTests` (mỗi trạng thái ra chữ/nút đúng, `DaemonOtherUser` không có nút kể cả khi cần cài lại).
2. Code: `ControllerAvailability.DaemonOtherUser`, `DaemonFeatureController.SendCoreAsync`, `ServiceBanner`,
   `MainViewModel` (`ServiceSetupText`, `InstallServiceText`, `ShowInstallServiceButton`), `MainWindow.axaml`,
   `Strings.resx` (`ServiceOtherUser`).

## T4. Giãn poll khi cửa sổ ẩn

1. Test trước: `MainViewModel.PollInterval(true/false)` = 2 s / 15 s.
2. Code: `MainViewModel.SetWindowShown`, `App.axaml.cs` theo dõi `IsVisible`/`WindowState` của cửa sổ.

## T5. Tài liệu

`docs/guide/09-macos.md` và `docs/guide/en/09-macos.md`.
