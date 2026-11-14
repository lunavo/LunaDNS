# 2. Chạy bằng quyền Admin, khay hệ thống

## Quyền Admin

`LunaDNS.exe` có manifest `requireAdministrator`: bấm đúp (hoặc mở từ Start menu) là Windows hỏi UAC, **không cần** chuột phải → "Run as administrator". Cần Admin vì app sửa luật NRPT, file hosts và cấu hình proxy của người dùng đăng nhập.

Không có quyền thì tính năng báo: *"Không có quyền sửa cấu hình mạng. Hãy chạy LunaDNS bằng quyền Admin."* Chỉ khi chạy dòng lệnh (xem [Dòng lệnh](06-dong-lenh.md)) mới cần mở PowerShell bằng "Run as administrator".

## Mở app

- Lần mở đầu: app dọn thay đổi còn sót từ lần trước (nếu có), hiện cửa sổ và **tự chạy Kiểm tra**.
- Nếu có thay đổi cũ được hoàn tác, trên cửa sổ có dòng *"Đã hoàn tác N thay đổi còn sót từ lần chạy trước."* Nếu không hoàn tác hết: *"Còn thay đổi từ lần chạy trước chưa hoàn tác được. Bấm "Chẩn đoán" để ghi báo cáo."*
- **Chỉ một bản chạy cùng lúc** (mutex `Global\LunaDNS`). Mở lần hai thì không mở bản mới mà làm cửa sổ cũ hiện lên.

## Khay hệ thống

- Nút **X** chỉ thu cửa sổ xuống khay, app và các công tắc vẫn chạy.
- Bấm trái biểu tượng khay hoặc menu **Mở LunaDNS**: hiện lại cửa sổ.
- Menu **Thoát & khôi phục**: tắt mọi dịch vụ, hoàn tác toàn bộ thay đổi, rồi thoát. Nếu còn mục chưa hoàn tác được, app báo *"Chưa hoàn tác hết thay đổi. Hãy mở lại LunaDNS hoặc chạy LunaDNS.exe --cleanup."*

## Đăng xuất, tắt máy

App cố hoàn tác trong tối đa 4 giây khi phiên kết thúc. Chưa đo được chắc chắn trên Avalonia 11.3 (Task 8 bước 6 còn chờ). Nếu không kịp, thay đổi còn trong journal và sẽ được hoàn tác bởi tác vụ dọn dẹp lúc khởi động máy/đăng nhập (bản Setup), ở lần mở app sau, hoặc bằng `--cleanup`. Trong lúc đó Steam có thể không phân giải được tên miền.

## Lưu ý

- Không tự chạy cùng Windows (chưa có tuỳ chọn khởi động cùng máy).
- Khi tắt app bằng Task Manager (kill), thay đổi **không** được hoàn tác ngay: xem [Gỡ và dọn khi app chết](07-go-va-don-dep.md).
- Mỗi lần chạy (cả dòng lệnh), app khoá quyền `%ProgramData%\LunaDNS`: chỉ SYSTEM và Administrators ghi được. Không khoá được thì app ghi cảnh báo vào log và vẫn chạy.
