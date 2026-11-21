# D5: Gom trạng thái / thử / cập nhật / gỡ về một chỗ (thiết kế)

Ngày: 21/11/2026. Lộ trình: [2026-11-21-lunadns-lo-trinh-tinh-nang.md](2026-11-21-lunadns-lo-trinh-tinh-nang.md) mục D5.
Lấy ý từ menu `service.bat` của Flowseal (cài/gỡ, Check Status, Run Tests/Diagnostics, Check Updates, Update IPSet List).

## Vấn đề

Sau D1–D4, cửa sổ chính có đủ việc nhưng sắp theo thứ tự các tính năng ra đời. Các nút bảo trì nằm rải rác ở hai hàng
chân trang, không thuộc khu nào: **Tắt hết & khôi phục** | **Chẩn đoán**, rồi **Kiểm tra cập nhật** | **Gỡ khỏi máy**.
Dòng **Tự cập nhật danh sách tên miền** (D4) nằm trong thẻ Tên miền, xa nút kiểm bản app. Guide 01 và 05 còn ghi
"Kiểm tra cập nhật ở mục Nâng cao", trong khi nút đó nằm ở chân trang. Người dùng phải đọc hết trang mới biết làm một
việc ở đâu.

## Quyết định đã chốt (PO, 21/11/2026)

1. **Một trang cuộn, 4 khu có tiêu đề.** Không dùng tab, không thêm cửa sổ "Công cụ".
2. **Thứ tự:** Kiểm tra → Trạng thái → Cập nhật → Gỡ & dọn. Kiểm tra vẫn ở trên cùng như KH6: mở app thì thấy kết quả,
   bấm Áp dụng đề xuất, các công tắc bật ngay bên dưới.
3. Dòng **Tự cập nhật danh sách tên miền** chuyển sang khu **Cập nhật**.
4. Mục **Nâng cao** chuyển lên cuối khu **Kiểm tra**: "Tìm cấu hình hợp mạng" ghi vào chính các ô này.
5. Thêm hai dòng chữ, không thêm chức năng: **"Đang dùng LunaDNS x.y.z"** ở khu Cập nhật; **gợi ý cách gỡ** khi không có
   nút Gỡ khỏi máy (bản portable, macOS).
6. **Menu khay / thanh menu giữ nguyên** (Mở LunaDNS / Tắt hết & khôi phục / Thoát & khôi phục).

## Bố cục mới

Header, huy hiệu trạng thái tổng và ba banner (dịch vụ nền macOS, thông báo, có bản mới) giữ nguyên ở đầu trang. Chúng
báo cho cả trang chứ không cho một khu. Dưới đó:

| Khu | Tiêu đề khu | Nội dung (theo thứ tự) |
|---|---|---|
| 1 | **Kiểm tra** · bên phải là dòng `ProbeStatus` | Thẻ kết quả kiểm: các dòng theo nhóm, gợi ý Tuỳ chỉnh, gợi ý kiểm, hàng nút **Kiểm tra** / **Áp dụng đề xuất** / **Tìm cấu hình hợp mạng** (hoặc **Dừng**), dòng trạng thái dò, rồi nút phẳng **Chẩn đoán**. Dưới thẻ: Expander **Nâng cao** (vẫn gập sẵn, vẫn ẩn trên macOS). |
| 2 | **Trạng thái** | Hai thẻ công tắc (Định tuyến DNS, Proxy SNI); thẻ **TÊN MIỀN** (các nhóm, số tên miền, chú thích), không còn dòng tự cập nhật. |
| 3 | **Cập nhật** | Một thẻ: hàng **"Đang dùng LunaDNS x.y.z"** + nút **Kiểm tra cập nhật**; hàng **Tự cập nhật danh sách tên miền** (công tắc, trạng thái, **Cập nhật ngay**). Hàng sau vẫn ẩn khi dịch vụ nền là bản cũ, như D4. |
| 4 | **Gỡ & dọn** | Một thẻ: nút **Tắt hết & khôi phục**; nút **Gỡ khỏi máy** (chỉ bản Setup), hoặc dòng gợi ý cách gỡ khi không có nút. |

Phác thảo (Windows, bản Setup):

```
LunaDNS                           [● Đang bật]
(banner)
Kiểm tra                    Kiểm lúc 10:42
┌────────────────────────────────────────┐
│ ✓ Steam   …                            │
│ [Kiểm tra] [Áp dụng đề xuất] [Tìm cấu…]│
│ Chẩn đoán                              │
└────────────────────────────────────────┘
▸ Nâng cao
Trạng thái
[Định tuyến DNS ................ (on)]
[Proxy SNI ..................... (off)]
┌ TÊN MIỀN ─────────────── 42 tên miền ┐
└────────────────────────────────────────┘
Cập nhật
┌────────────────────────────────────────┐
│ Đang dùng LunaDNS 0.9.0  [Kiểm tra cập…]│
│ ☑ Tự cập nhật danh sách … [Cập nhật ngay]│
└────────────────────────────────────────┘
Gỡ & dọn
┌────────────────────────────────────────┐
│ [Tắt hết & khôi phục]   [Gỡ khỏi máy]   │
└────────────────────────────────────────┘
```

### Tiêu đề khu

- Kiểu chữ mới `TextBlock.zone` trong `Styles/Theme.axaml`: cỡ 14, SemiBold, màu chữ mặc định của theme (không đặt Foreground). Tiêu đề thẻ
  bên trong (`TextBlock.section`, cỡ 12, chữ hoa, màu phụ) giữ nguyên, nên vẫn phân biệt được hai cấp.
- Thẻ Kiểm tra bỏ tiêu đề riêng "KIỂM TRA MẠNG", vì tiêu đề khu đã nói điều đó. Dòng `ProbeStatus` lên hàng tiêu đề khu,
  căn phải. Khoá `CheckSectionTitle` bị xoá.
- Bốn khoá mới: `ZoneCheck` = "Kiểm tra", `ZoneStatus` = "Trạng thái", `ZoneUpdate` = "Cập nhật", `ZoneCleanup` = "Gỡ & dọn".

### Dòng phiên bản

- Khoá `VersionFormat` = "Đang dùng LunaDNS {0}". `{0}` lấy từ `UpdateChecker.Normalize(AppInfo.Version)`, giống câu
  "Bạn đang dùng LunaDNS {0}, bản mới nhất" (ba số, ví dụ `0.9.0`).
- Logic để ở `Maintenance.VersionText(Version)` (static) để test được. `MainViewModel.VersionText` gọi hàm đó.

### Gợi ý gỡ

- Chỉ hiện khi `CanUninstall` = false. Chữ dùng cơ chế hậu tố `_Mac` sẵn có của `Strings`:
  - `UninstallHint` (Windows portable): "Bản portable: thoát bằng Thoát & khôi phục ở khay, chạy LunaDNS.exe --uninstall
    trong PowerShell Admin rồi xoá thư mục. Xem hướng dẫn mục 7."
  - `UninstallHint_Mac`: "Gỡ bằng lệnh sudo lunadns uninstall trong Terminal. Xem hướng dẫn mục 9."
- Không chạy lệnh nào hộ người dùng: chỉ là chữ.

### Không đổi

- Mọi lệnh (`CheckCommand`, `ApplyRecommendedCommand`, `TuneCommand`, `DiagnosticsCommand`, `CheckUpdateCommand`,
  `DisableAllCommand`, `UninstallAppCommand`, `DomainList.*`) và điều kiện bật/xám/ẩn của chúng giữ nguyên. D5 chỉ đổi
  chỗ đặt trong `Views/MainWindow.axaml`.
- Không đổi journal, tên feature, `INetworkConfigurator`, settings, dòng lệnh, menu khay.
- Nút Tắt hết vẫn xám khi không có gì bật (U8). Thẻ Tên miền vẫn có viền vàng + huy hiệu MỚI lần đầu.
- Kích thước cửa sổ 560×720, tối thiểu 460×520, giữ nguyên.

## Kiểm thử

- `StringsTests.Every_static_string_resolves`: số chuỗi 202 → 207 (−1 `CheckSectionTitle`, +4 khu, +`VersionFormat`,
  +`UninstallHint`).
- `StringsTests.Mac_overrides_…` tự bao `UninstallHint_Mac` (phải có khoá gốc và khác câu gốc).
- `MaintenanceTests`: `VersionText(new Version(0, 9, 0, 0))` cho "Đang dùng LunaDNS 0.9.0"; `VersionText(new Version(1, 2))`
  cho "… 1.2.0".
- Test mới `MainWindowLayoutTests` (App.Tests, chạy cả hai target) đọc `Views/MainWindow.axaml` như XML, không cần
  Avalonia headless:
  - bốn tiêu đề khu xuất hiện theo đúng thứ tự `ZoneCheck` < `ZoneStatus` < `ZoneUpdate` < `ZoneCleanup`;
  - mỗi lệnh bảo trì nằm sau tiêu đề khu của nó và trước tiêu đề khu kế tiếp: `CheckCommand`, `TuneCommand`,
    `DiagnosticsCommand`, Expander Nâng cao trong khu Kiểm tra; `Features` và `DomainGroups` trong khu Trạng thái;
    `CheckUpdateCommand` và `DomainList.ToggleCommand` trong khu Cập nhật; `DisableAllCommand` và `UninstallAppCommand`
    trong khu Gỡ & dọn;
  - không còn `x:Static app:Strings.CheckSectionTitle`.
  File axaml được chép vào thư mục output của test (`<None Include=… CopyToOutputDirectory>`), để test không phụ thuộc
  thư mục làm việc.
- Thử tay trên Windows: chạy app (Release), xem đủ 4 khu, bấm Kiểm tra, Chẩn đoán, Kiểm tra cập nhật, Tắt hết; thu nhỏ
  cửa sổ về 460 px chiều ngang không vỡ hàng.
- macOS (phiên sau, `build/run-macos.sh`): xem 4 khu, khu Kiểm tra không có Nâng cao, khu Gỡ & dọn hiện gợi ý `sudo lunadns
  uninstall`, banner dịch vụ nền vẫn ở đầu trang.

## Tài liệu

Cập nhật song song vi + en: `00`/`01` (chỗ nút Kiểm tra cập nhật), `03` (Tự cập nhật danh sách sang khu Cập nhật; mục
Tắt), `04` (Chẩn đoán và Nâng cao trong khu Kiểm tra), `05` (vị trí mục Nâng cao; Kiểm tra cập nhật không thuộc Nâng
cao), `07` (khu Gỡ & dọn và gợi ý gỡ bản portable), `09` (giao diện macOS). README chỉ sửa nếu có câu mô tả vị trí nút.
