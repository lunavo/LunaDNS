# 3. Các công tắc

Có hai công tắc dùng được (Định tuyến DNS, Proxy SNI); công tắc thứ ba, Chế độ PUBG, chưa hỗ trợ (xem cuối trang). Mỗi công tắc bật/tắt độc lập. Công tắc hiện *"Đang tắt"* hoặc *"Đang bật"* (Proxy SNI hiện thêm cổng và số kết nối: *"Đang bật · cổng 18080 · 33 kết nối"*).

Mọi thay đổi hệ thống được **ghi vào journal trước khi làm**, và hoàn tác theo thứ tự ngược (LIFO). App chỉ động tới thứ do chính nó tạo.

## Định tuyến DNS

- Dựng DoH stub tại `127.0.0.1:53`, hỏi Cloudflare (`1.1.1.1`) rồi Google (`8.8.8.8`) làm dự phòng; đổi thứ tự ở [Nâng cao](05-nang-cao.md). Kết nối DoH đi thẳng tới IP, không hỏi DNS hệ thống.
- Thêm **một luật NRPT** tên hiển thị "LunaDNS", Comment bắt đầu bằng `LunaDNS `, gồm các tên miền nhóm **Steam** (`steampowered.com`, `steamcommunity.com`, `steamstatic.com`, `steam-chat.com`, `steamusercontent.com`, `steamgames.com`, `steamcontent.com`, `steamserver.net`, `counter-strike.net`, `dota2.com`, `valvesoftware.com`; mỗi tên có cả dạng domain con), trỏ về `127.0.0.1`.
- Luật của phần mềm khác (VPN, Tailscale…) không bị sửa hay xoá. Nếu một phần mềm khác đã định tuyến DNS cho chính các tên miền này, app không bật và báo *"Một phần mềm khác (VPN, Tailscale…) đang định tuyến DNS cho chính các tên miền này."*
- Lỗi cổng 53 bận: *"Cổng DNS 53 trên 127.0.0.1 đang bị chương trình khác dùng."*

## Proxy SNI

- Mở proxy HTTP CONNECT ở `127.0.0.1`, cổng đầu tiên trống trong **18080–18090**. Proxy chỉ nhận CONNECT tới tên miền Steam ở cổng 443 và phục vụ `/proxy.pac`.
- Đặt `AutoConfigURL` (Internet Settings của người dùng đăng nhập) thành `http://127.0.0.1:<cổng>/proxy.pac`. Chỉ tên miền Steam đi qua proxy.
- Cắt gói TLS ClientHello theo điểm cắt chọn ở Nâng cao (mặc định **sau byte đầu tiên**).
- Máy đã có PAC hoặc proxy thủ công: app hỏi *"Thay cấu hình proxy hiện có?"*. Đồng ý thì cấu hình cũ được trả lại khi tắt Proxy SNI hoặc thoát. Huỷ thì công tắc không đổi. `AutoConfigURL` ở dạng lạ thì app không ghi đè.
- Watchdog: proxy chết sẽ được mở lại đúng cổng cũ; lỗi quá 3 lần trong 1 phút thì tính năng tự tắt và báo.
- Hết cổng: *"Không còn cổng trống cho Proxy SNI."*

## Chế độ PUBG: chưa hỗ trợ

PO chốt (13/11/2026): LunaDNS hiện **không hỗ trợ PUBG**. Bản build hiện tại vẫn còn công tắc thứ ba "Chế độ PUBG" và ô "Kiểm tra cả PUBG", nhưng giao diện sau này sẽ ẩn chúng. **Đừng bật**: nó sửa file hosts và mở listener `127.0.0.1:443`, và chưa được xác nhận với game thật hay anti-cheat. Nếu lỡ bật, tắt công tắc hoặc dùng "Tắt hết & khôi phục" / `--cleanup` để hoàn tác dòng hosts `# LunaDNS`.

## Tắt

- Gạt công tắc tắt: hoàn tác phần của tính năng đó. Nếu hoàn tác lỗi, dịch vụ **vẫn chạy**, mục giữ lại trong journal, thông báo *"Một số thay đổi chưa hoàn tác được…"*, bấm tắt lại để thử tiếp.
- **Tắt hết & khôi phục:** tắt mọi công tắc và trả cấu hình mạng như trước (*"Đã tắt hết và trả lại cấu hình mạng như trước."*).
- Tính năng gặp lỗi lặp lại sẽ tự dừng và báo *"… gặp lỗi lặp lại nên đã được tắt."*
