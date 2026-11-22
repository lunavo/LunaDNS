# B4 + U3 + B11: tài khoản khác đang ngồi trước máy (thiết kế)

Ngày: 22/11/2026. Nguồn: review đối kháng sau v0.10.0, mục B4 (lỗi, mức Vừa, macOS), U3 (giao diện) và B11 (lỗi, mức Thấp).

## Vấn đề

- Dịch vụ nền chỉ nhận lệnh của root hoặc chủ `/dev/console` (`ConsoleUser.IsAllowed`). Với Fast User Switching, app
  của tài khoản đang chạy nền bị từ chối: câu trả lời `Ok=false`, không có `Status`.
- `DaemonFeatureController` coi mọi câu từ chối như vậy là `DaemonNotResponding`, nên giao diện hiện banner "đã cài nhưng
  không trả lời" kèm nút **Cài lại dịch vụ nền**. Bấm nút đó thì `lunadns install` bootout dịch vụ nền: tắt định tuyến
  của người đang ngồi trước máy.
- B11: mỗi kết nối, `ConsoleUser` sinh một tiến trình `/usr/bin/stat`; app hỏi lại mỗi 2 giây kể cả khi cửa sổ ẩn
  (khoảng 43 000 tiến trình mỗi ngày).

## Quyết định

1. **Mã máy đọc được cho câu từ chối.** `DaemonResponse` thêm trường tuỳ chọn cuối `Code` (chuỗi, bỏ khi null).
   Dịch vụ nền gửi `Code = "not-console-user"` kèm câu cũ. `DaemonProtocol.IsNotConsoleUser(response)` đúng khi
   `Ok=false`, không có `Status`, và `Code` là mã đó **hoặc** `Message` đúng nguyên câu cũ "Chỉ người đang dùng máy này
   (hoặc root) mới điều khiển được LunaDNS." (dịch vụ nền 0.10.0 trở về trước chỉ gửi câu này).
   - App cũ gặp trường lạ thì bỏ qua (mặc định System.Text.Json): hành vi như trước.
   - App mới gặp dịch vụ nền cũ: nhận ra qua câu cũ, nên cũng không hiện nút Cài lại.
2. **Trạng thái mới `ControllerAvailability.DaemonOtherUser`.** `DaemonFeatureController` đặt trạng thái này khi
   `IsNotConsoleUser`; câu `Ok=false` khác không kèm `Status` vẫn là `DaemonNotResponding`. Mọi tính năng hiện là tắt
   (app này không biết trạng thái thật), hai công tắc xám như mọi trạng thái khác `Local`/`DaemonRunning`.
3. **Banner không có nút.** Banner dịch vụ nền tách thành `ServiceBanner(Text, ButtonText?)`, tính từ trạng thái:
   - `DaemonOtherUser`: "Tài khoản khác đang dùng LunaDNS trên máy này. Khi bạn quay lại dùng máy (tài khoản kia
     chuyển đi hoặc đăng xuất), LunaDNS tự điều khiển được." **Không có nút** (kể cả khi trước đó app ghi nhận cần
     cài lại). Log cảnh báo chỉ ghi khi trạng thái đổi, không ghi mỗi lần poll.
   - Các trạng thái khác giữ nguyên chữ và nút như trước.
   - `InstallServiceAsync` cũng tự thoát khi trạng thái là `DaemonOtherUser` (phòng nút cũ còn bấm được).
4. **Hỏi console không sinh tiến trình (B11).** `ConsoleUser` dùng `SCDynamicStoreCopyConsoleUser` của
   SystemConfiguration (P/Invoke) để lấy UID và tên người đang ngồi trước máy. Lý do không gọi `stat()`: bố cục
   `struct stat` khác nhau giữa arm64 và x86_64 (`stat$INODE64`), còn hàm của SystemConfiguration trả UID qua tham số
   và là API Apple dành đúng cho việc này. Không có ai trước máy (màn đăng nhập) thì UID null hoặc 0: chỉ root được
   phép, như cũ. Tên "loginwindow" coi như không có tên. `ConsoleUser` nhận một hàm đọc (seam cho test).
5. **Giãn poll khi cửa sổ ẩn.** Cửa sổ hiện: 2 giây như cũ. Cửa sổ ẩn hoặc thu nhỏ: 15 giây (icon thanh menu chậm
   tối đa 15 giây khi bật/tắt bằng Terminal). Cửa sổ hiện lại thì hỏi ngay một lần rồi về 2 giây.

## Không làm

- Không cho tài khoản chạy nền xem trạng thái thật (lệnh chỉ đọc cho mọi UID): cần bàn thêm về quyền riêng tư.
- CLI `lunadns` của tài khoản chạy nền vẫn in câu từ chối như cũ.

## Kiểm

- Unit test: `DaemonServer` gửi `Code`; `IsNotConsoleUser` với mã mới, câu cũ, câu khác; JSON thiếu `code` vẫn đọc
  được; controller ra `DaemonOtherUser`; `ServiceBanner` không có nút; `PollInterval`; `ConsoleUser` với hàm đọc giả và
  (trên Mac) so với `stat -f %u /dev/console`.
- Kiểm hai tài khoản thật (tuỳ chọn, hỏi user trước).
