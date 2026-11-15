# 8. Xử lý sự cố

<sub>[English](en/08-troubleshooting.md)</sub>

Log ở `C:\ProgramData\LunaDNS\logs\lunadns.log` (UTF-8 không BOM). Cần gửi người khác thì tạo báo cáo ([Dòng lệnh](06-dong-lenh.md#báo-cáo)).

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| *"Không có quyền sửa cấu hình mạng"* | Thiếu quyền Admin, hoặc diệt virus khoá hosts | Chạy bằng Admin; tạm tắt diệt virus nếu liên quan hosts |
| *"Cổng DNS 53 … đang bị chương trình khác dùng"* | Phần mềm khác giữ cổng 53 | Tắt phần mềm đó, bật lại Định tuyến DNS |
| *"Một phần mềm khác … đang định tuyến DNS …"* | VPN/Tailscale đã có luật NRPT cho domain Steam | LunaDNS không sửa luật của người khác; tắt luật đó hoặc dùng VPN thay LunaDNS |
| *"Không còn cổng trống cho Proxy SNI"* | Cả 18080–18090 bận | Giải phóng cổng |
| Hộp *"Thay cấu hình proxy hiện có?"* | Máy có PAC hoặc proxy thủ công | Đồng ý (cấu hình cũ được trả lại khi tắt) hoặc Huỷ |
| *"Cấu hình proxy hiện có ở dạng lạ"* | `AutoConfigURL` không phải chuỗi | Sửa tay trong registry rồi bật lại |
| *"Không đọc được cấu hình của người dùng đang đăng nhập"* | Hive người dùng chưa nạp | Mở app bằng tài khoản đang đăng nhập |
| *"File hosts dùng bảng mã … không sửa an toàn"* | hosts lưu UTF-16 | Lưu lại UTF-8 hoặc ANSI |
| *"Windows từ chối thêm luật định tuyến DNS"* | NRPT/CIM lỗi | Chạy Admin; xem log |
| Kiểm tra vẫn "Bị lọc SNI" sau khi đã bật | Kiểm tra đo mạng gốc | Thử mở Steam thật |
| Store Steam màn đen | Đang chọn MidSni | Chọn FirstByte ở Nâng cao, tắt/bật lại Proxy SNI |
| Mạng/Steam hỏng sau khi app chết | Thay đổi còn sót | [Gỡ và dọn](07-go-va-don-dep.md) |
| *"… gặp lỗi lặp lại nên đã được tắt"* | Dịch vụ chết 3 lần trong 1 phút | Xem log rồi bật lại; nếu có *"chưa hoàn tác hết"* thì chạy `--cleanup` |
| Mở app lần hai không thấy cửa sổ mới | Bản cũ đang ở khay | Bấm biểu tượng khay |
| Log hiện chữ vỡ trong PowerShell 5.1 | Log không có BOM | `Get-Content … -Encoding UTF8` |
| `--cleanup` không làm gì | App đang chạy | Thoát app trước |
| Setup báo *"Không khôi phục được cấu hình mạng của bản LunaDNS đang cài"* | `--cleanup` của bản cũ còn mục lỗi | Mở LunaDNS bản cũ, bấm "Tắt hết & khôi phục", thoát, chạy lại Setup; vẫn lỗi thì xem log và [dọn tay](07-go-va-don-dep.md#khi---cleanup-không-đủ-thủ-công-chỉ-khi-cần) |
| Setup báo *"… chưa đăng ký được tác vụ dọn dẹp lúc khởi động máy"* | `schtasks` lỗi (chính sách máy, diệt virus) | App vẫn dùng được, chỉ thiếu tự dọn; xem log, chạy lại Setup |
| Trình gỡ báo *"Chưa khôi phục được hết cấu hình mạng"* | Còn mục chưa hoàn tác | [Gỡ bản Setup](07-go-va-don-dep.md#gỡ-bản-setup) |
| Mở được thư mục log nhưng không sửa/xoá được file | Đúng thiết kế: chỉ Admin ghi | Mở Notepad/PowerShell bằng Admin ([lý do](07-go-va-don-dep.md#thư-mục-dữ-liệu-chỉ-admin-sửa-được)) |
| `--install-system` thoát mã 3 | Exe không nằm trong Program Files | Bình thường với bản portable; muốn có tác vụ dọn dẹp thì dùng Setup |
| SmartScreen chặn / Smart App Control chặn | Exe chưa ký số | [Cài đặt](01-cai-dat.md) |

## Giới hạn đã biết

- MidSni không qua được trên mạng đã thử; FirstByte là mặc định.
- PUBG chưa hỗ trợ.
- Đăng xuất/tắt máy: hoàn tác trong 4 giây, chưa đo chắc (bản Setup có tác vụ dọn dẹp bù).
- Exe chưa ký số. App tự báo có bản mới nhưng chưa tự tải và cài.

Báo lỗi: đính kèm báo cáo `--report` và mô tả bước tái hiện.
