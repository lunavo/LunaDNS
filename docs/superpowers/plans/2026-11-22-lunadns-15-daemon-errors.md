# KH15: Lỗi của dịch vụ nền đọc được (B2 + U2 + F3)

Spec: [2026-11-22-lunadns-daemon-errors-design.md](../specs/2026-11-22-lunadns-daemon-errors-design.md).

Lệnh kiểm (máy Mac):

```bash
~/.dotnet/dotnet build -warnaserror
~/.dotnet/dotnet test tests/LunaDns.Core.Tests
~/.dotnet/dotnet test tests/LunaDns.Platform.MacOS.Tests
~/.dotnet/dotnet test tests/LunaDns.App.Tests -f net8.0
```

(App.Tests có 19 test chỉ chạy trên Windows, đã biết là đỏ trên Mac.)

## T1. Core: `FeatureResult.Detail`, mục log dịch vụ nền trong báo cáo

1. Test trước: `FeatureSetTests` (apply ném `ConfiguratorException` → `Detail` = message; ném exception khác → `Detail` null);
   `DiagnosticReportTests` (có `ServiceLog` → mục mới, đã che tên; null → không có mục).
2. Code: `FeatureResult` thêm `string? Detail = null`; `FeatureSet.EnableCoreAsync`; `DiagnosticInput.ServiceLog`; `DiagnosticReport.Build`.

## T2. Platform.MacOS: `get-log`, `Describe`

1. Test trước (`DaemonLogTests`, `DaemonTests`): số dòng mặc định/kẹp, che tên user và tên máy, cắt dòng dài,
   tổng dưới `MaxLineBytes` khi serialize; thư mục không có → danh sách rỗng; lỗi chung có `Detail` → `Message` = `Detail`.
2. Code: `DaemonProtocol.GetLog`, `DaemonRequest.Lines`, `DaemonResponse.Log`; `DaemonLogReader`; `DaemonCommands`
   nhận reader tuỳ chọn; `ConsoleUser.CurrentName`; `MacCli/Program.cs` truyền reader cho `daemon` và `run`.

## T3. App: câu lỗi, log dịch vụ nền, nút mới

1. Test trước: `DaemonFeatureControllerTests` (`ToResult` giữ Message; `GetServiceLogAsync` trả dòng / daemon cũ / không trả lời);
   `ErrorMessagesTests` (`Reason`); `SystemShellTests` (lệnh mở thư mục, hiện file trên từng nền tảng); `ReportWriter` có mục mới.
2. Code: `ErrorMessages.Reason`, `MainViewModel.Failure`; `AppServices.GetServiceLogAsync`; `ReportWriter.Write(serviceLog:)`;
   `ISystemShell.OpenFolder/Reveal`; `MainViewModel.OpenLogFolderCommand`, nút hành động trên thanh thông báo; `MainWindow.axaml`; `Strings.resx`.

## T4. Tài liệu, kiểm thật

1. `docs/guide/09-macos.md`, `08-su-co.md` và bản `en/`.
2. `build/run-macos.sh`: cài lại dịch vụ nền từ app (mật khẩu do người dùng gõ), gây lỗi, Chẩn đoán, hai nút.
3. Commit thẳng `main`, push, xem CI.
