# Tự kiểm tra cập nhật khi mở app — thiết kế

Ngày: 15/11/2026. PO duyệt hướng "Tự kiểm khi mở app" (15/11).
Nền: spec v1 mục 4.10 ([2026-11-13-lunadns-design.md](2026-11-13-lunadns-design.md)) và KH7 K10, đã có nút "Kiểm tra cập nhật" thủ công đọc GitHub Releases API.

## Mục tiêu

Người dùng biết có bản mới mà không phải tự bấm nút. Không làm phiền khi không có gì mới, hoặc khi mạng lỗi.

## Ngoài phạm vi

- Tự tải, kiểm SHA-256 hay tự chạy bộ cài (spec v1 đã loại khỏi v1).
- Kiểm định kỳ khi app chạy nền, hay thông báo ở khay.
- Công tắc tắt tự kiểm, và lưu mốc lần kiểm cuối.
- Manifest ký Ed25519 (4.10 bản gốc). Vẫn giữ nguồn dữ liệu như KH7 K10.

## Hành vi

1. `MainViewModel.Start()` gọi thêm `_ = CheckUpdateSilentlyAsync()`. Hàm này đợi `UpdateStartupDelay` (10 giây) rồi mới kiểm một lần. Mỗi lần mở app kiểm đúng một lần, không lưu gì xuống đĩa.
2. Nếu có bản mới hơn (`UpdateStatus.NewerAvailable`), dải báo hiện lên.
3. Nếu `UpToDate`, `Failed`, hay có ngoại lệ bất kỳ thì im lặng: không hộp thoại, không `ShowMessage`. Không ghi log (`UpdateChecker` không trả lý do lỗi).
4. Dải báo nằm ở đầu nội dung cửa sổ chính, trên các thẻ tính năng:
   - Chữ: `UpdateBannerFormat` = "Đã có LunaDNS {0} (bạn đang dùng {1})."
   - Nút **Tải** (`UpdateDownloadButton`): gọi `shell.OpenUrl` với URL của Release. Dải báo vẫn còn sau khi bấm.
   - Nút **✕**, có tooltip `UpdateDismissTooltip` = "Ẩn đến lần mở app sau": ẩn dải báo.
5. Nút "Kiểm tra cập nhật" thủ công giữ nguyên hộp thoại như cũ. Ngoài ra, nếu kết quả là `NewerAvailable` thì cũng hiện dải báo, kể cả khi trước đó dải báo đã bị ẩn.
6. URL mở ra vẫn phải qua bộ lọc hiện có của `UpdateChecker`, tức chỉ nhận `https://github.com/<repo>/releases/...`.

## Sửa lỗi kèm theo

`UpdateChecker.CheckAsync`: khi phản hồi HTTP 200 nhưng không có release nào hợp lệ (mảng rỗng, chỉ có draft, hoặc không có tag `vX.Y.Z`), trả `UpToDate` với `Latest = null, Url = null` thay vì `Failed`.
Lỗi HTTP, JSON hỏng, timeout và URL lạ vẫn trả `Failed` như cũ. Nhánh `UpToDate` của `Maintenance.CheckForUpdateAsync` chỉ dùng `current`, nên không cần sửa.

## Thay đổi code

| File | Thay đổi |
|---|---|
| `src/LunaDns.App/Updates/UpdateChecker.cs` | Nhánh "không có ứng viên" trả `UpToDate`; cập nhật summary. |
| `src/LunaDns.App/Maintenance.cs` | Thêm `CheckSilentlyAsync(check, ct)`: trả `UpdateCheckResult?`, khác null chỉ khi `NewerAvailable`; nuốt mọi ngoại lệ trừ huỷ do người gọi; không đụng `prompts`. `CheckForUpdateAsync` trả về `UpdateCheckResult` để VM đặt dải báo. `CheckSilentlyAsync` là `static` (CA1822). |
| `src/LunaDns.App/ViewModels/UpdateBannerViewModel.cs` (mới) | Trạng thái dải báo, test được không cần Dispatcher: `IsVisible`, `Text`, `OpenCommand`, `DismissCommand`, `Show(UpdateCheckResult)`. |
| `src/LunaDns.App/ViewModels/MainViewModel.cs` | `UpdateBanner`; tự kiểm sau 10 giây trong `Start()`; nút thủ công đẩy kết quả vào banner. |
| `src/LunaDns.App/Views/MainWindow.axaml` | `Border` dải báo, `IsVisible="{Binding UpdateBanner.IsVisible}"`, dùng màu/biểu tượng có sẵn trong `Styles` (theme chỉ-tối). |
| `src/LunaDns.App/Strings.resx`, `Strings.cs` | `UpdateBannerFormat`, `UpdateDownloadButton`, `UpdateDismissTooltip`. |

Không đụng Core, Platform hay `AppSettings`.

## Test (`tests/LunaDns.App.Tests`)

- `UpdateCheckerTests`: mảng rỗng → `UpToDate`; chỉ có draft → `UpToDate`; chỉ có tag lạ → `UpToDate`; HTTP 500 vẫn `Failed`.
- `MaintenanceTests`:
  - `CheckSilentlyAsync` với `NewerAvailable` → trả kết quả.
  - Với `UpToDate`, `Failed`, hay `check` ném `HttpRequestException` → trả null, prompts không bị gọi.
  - `CheckForUpdateAsync` trả về đúng kết quả của `check`.
- `UpdateBannerViewModelTests`: ẩn lúc đầu; có bản mới thì hiện đúng chữ; UpToDate/Failed không đổi; Tải gọi `OpenUrl` đúng URL và giữ banner; ✕ ẩn, kiểm lại thì hiện lại.
- `StringsTests`: 3 khoá mới có giá trị.

Lệnh verify: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows` (và `-f net8.0`), `dotnet build -c Release -warnaserror`.

Thử tay: chạy app với `<Version>` tạm hạ xuống `0.0.1`, khi đã có ít nhất một Release **Published** trên `lunavo/LunaDNS`; sau khoảng 10 giây dải báo hiện, bấm Tải mở đúng trang.

## Việc của PO ngoài code

`release.yml` tạo Release ở dạng nháp. Phải bấm **Publish** trên GitHub thì API công khai mới thấy bản đó; đến 15/11 API trả mảng rỗng và remote chưa có tag.
