# B2 + U2 + F3: Lỗi của dịch vụ nền đọc được (thiết kế)

Ngày: 22/11/2026. Nguồn: review đối kháng sau v0.10.0, mục B2 (lỗi, mức Vừa), U2 (trải nghiệm) và F3 (tính năng).

## Vấn đề

- `DaemonFeatureController.ToResult` bỏ `DaemonResponse.Message`. Thẻ và thanh thông báo chỉ hiện `Error_Other`
  "Lỗi không xác định; chi tiết nằm trong log".
- Câu của chính daemon cho lỗi chung cũng chỉ là "Không bật được (Other); xem log." vì `FeatureSet` không giữ lý do.
- Log của daemon nằm ở `/Library/Application Support/LunaDNS/logs` (root, 700): người dùng không mở được, báo cáo
  Chẩn đoán cũng không có.
- Log của giao diện (`~/Library/Application Support/LunaDNS/logs`, `%ProgramData%\LunaDNS\logs`) không có nút mở.
  Sau Chẩn đoán, người dùng phải tự tìm file báo cáo trên Desktop.

## Quyết định

1. **Lý do lỗi đi theo kết quả.** `FeatureResult` có thêm trường `Detail` (chuỗi, có thể null).
   `FeatureSet` chỉ điền `Detail` từ `ConfiguratorException.Message`: các câu đó do code nền tảng viết sẵn bằng tiếng Việt
   cho người dùng (ví dụ "networksetup -setautoproxyurl lỗi: …", "Không tìm thấy card mạng nào đang bật…").
   Exception khác (lỗi lập trình, IO lạ) không ra giao diện, như quy ước cũ của `ErrorMessages`.
2. **Daemon:** `Describe` dùng `Detail` cho lỗi chung khi có. Không có thì câu chung mới chỉ tới Chẩn đoán
   ("…; bấm Chẩn đoán để lấy log của dịch vụ nền"), không chỉ tới log mà người dùng không đọc được.
3. **Giao diện (ánh xạ an toàn):** `ToResult` chép `Message` của daemon vào `Detail`. `ErrorMessages.Reason`:
   mã lỗi cụ thể (cổng bận, xung đột resolver…) vẫn dùng câu của app (có bản `_Mac`); chỉ mã `Other` mới dùng `Detail`
   (nếu có). Thẻ và thanh thông báo cùng dùng `Reason`. Windows hưởng luôn: `Other` từ `ConfiguratorException` hiện câu cụ thể.
4. **Lệnh socket `get-log`** (kèm `Lines`, tuỳ chọn): daemon trả `Log` là các dòng cuối của log của chính nó.
   - Số dòng: mặc định và tối đa 200 (`DiagnosticReport.LogLines`), tối thiểu 1.
   - Mỗi dòng đã qua `Redactor` (tên người đang ngồi trước máy, lấy bằng `stat -f %Su /dev/console`, và tên máy).
   - Giới hạn cỡ: mỗi dòng tối đa 1 000 ký tự (cắt, thêm "…"); tổng tối đa 16 000 ký tự, giữ các dòng mới nhất.
     JSON mặc định thoát ký tự ngoài ASCII thành `\uXXXX` (6 byte), nên câu trả lời luôn dưới `MaxLineBytes` (128 KB).
   - Không đọc được thư mục log thì một dòng "(không đọc được log: …)", vẫn `Ok`.
   - Quyền: như mọi lệnh khác (root hoặc chủ `/dev/console`).
5. **Báo cáo Chẩn đoán trên macOS** thêm mục "200 dòng log cuối của dịch vụ nền", sau mục log của giao diện.
   Toàn bộ báo cáo vẫn qua `Redactor` của giao diện lần nữa. Windows không có mục này (giao diện chính là bên ghi log).
   - Daemon cũ trả "Lệnh lạ:": mục ghi một dòng đề nghị **Cài lại dịch vụ nền**; như các lệnh mới khác, giao diện
     ghi nhớ để hiện nút cài lại (`IsUnknownCommand`).
   - Daemon không trả lời hoặc từ chối: một dòng nói rõ.
6. **Nút "Mở thư mục log"** cạnh nút Chẩn đoán: mở thư mục log của giao diện (tạo nếu chưa có).
   macOS `open <thư mục>`, Windows `explorer.exe <thư mục>`.
7. **Sau Chẩn đoán**, thanh thông báo "Đã lưu báo cáo: …" có nút **Hiện trong Finder** (macOS) / **Hiện trong Explorer**
   (Windows): `open -R <file>` / `explorer.exe /select,"<file>"`. Thông báo khác thay thế thì nút mất.

## Ngoài phạm vi

- Lệnh CLI `lunadns log` (có thể thêm sau, dùng cùng `get-log`).
- Câu lỗi cho lần tự bật lại thất bại (banner F1) vẫn dùng mã lỗi; `DaemonResume` chưa mang `Detail`.
- Câu "Dịch vụ nền LunaDNS chưa chạy." khi daemon từ chối UID: giữ như cũ.

## Kiểm

- Core: `FeatureSet` điền `Detail` từ `ConfiguratorException`, không điền từ exception khác; `DiagnosticReport` có/không có mục log dịch vụ nền.
- Platform.MacOS: `get-log` đúng số dòng, che tên, cắt dòng dài, giữ tổng dưới `MaxLineBytes` với dòng tiếng Việt dài;
  `Describe` dùng `Detail`.
- App: `ToResult` giữ `Message`; `ErrorMessages.Reason`; `GetServiceLogAsync` với daemon cũ / không trả lời; lệnh mở thư mục và hiện file.
- Chạy app thật bằng `build/run-macos.sh`: gây lỗi thật, xem thẻ; Chẩn đoán có log dịch vụ nền; hai nút mới.
