# LunaDNS: lộ trình tính năng sau bản 0.5 (đã duyệt 21/11/2026)

Nguồn: so sánh với README của Watt Toolkit (BeyondDimension/SteamTools), zapret, zapret-discord-youtube (Flowseal),
GreenTunnel, DNSveil, Accesser, PowerTunnel, GoodbyeDPI. Mỗi mục dưới đây cần spec riêng (brainstorming → spec → plan)
trước khi code. Tài liệu này chỉ chốt phạm vi và thứ tự.

## Đã duyệt

| # | Tính năng | Lấy ý từ | Ghi chú phạm vi |
|---|---|---|---|
| D1 | **Cắt TLS record** làm thêm một lựa chọn cho "Điểm cắt gói" | GreenTunnel `--tls-records` | Đóng gói lại ClientHello thành nhiều TLS record hợp lệ; dành cho DPI biết ghép lại TCP. Vẫn không giải mã TLS. |
| D2 | **Thêm máy chủ DoH + đo độ trễ** | DNSveil | Thêm vài nhà cung cấp (ví dụ Quad9, AdGuard), ô URL DoH tự nhập, nút "Đo" xếp theo độ trễ thực tế. |
| D3 | **Tự dò cách vượt** ("Tìm cấu hình hợp mạng của tôi") | Flowseal *Run Tests*, zapret `blockcheck` | Mở rộng Kiểm tra / Áp dụng đề xuất: thử các tổ hợp điểm cắt × độ trễ × DoH với vài địa chỉ Steam, áp tổ hợp đầu tiên chạy được. Dùng các lựa chọn của D1, D2. |
| D4 | **Cập nhật danh sách tên miền theo lớp** | Accesser `rules.toml`, Flowseal *Update IPSet List* | Lớp 1 đóng kèm (luôn có); lớp 2 file từ xa trên GitHub Releases, có ký số, chỉ lấy khi mạng đã thông, lỗi thì giữ bản cũ, có công tắc tắt; lớp 3 tên miền tuỳ chỉnh không bao giờ bị ghi đè. |
| D5 | **Gom trạng thái / thử / cập nhật / gỡ về một chỗ** | menu `service.bat` của Flowseal | Sắp lại giao diện theo việc người dùng muốn làm; không thêm chức năng mới. |

## Thứ tự làm

1. **D1** và **D2**: độc lập với nhau, mỗi cái gói gọn trong Core (proxy / DoH stub) + một ô ở Nâng cao.
2. **D3**: sau D1 và D2, để lượt dò có đủ lựa chọn.
3. **D4**: sau khi nhánh `feat/custom-domains` (lớp 3) đã merge.
4. **D5**: cuối cùng, khi D3 đã định hình phần Kiểm tra; làm sớm hơn sẽ phải sắp lại hai lần.

## Còn cân nhắc (chưa duyệt)

- Bộ dịch vụ chọn sẵn ngoài Steam (Epic, Discord…), proxy vẫn chỉ nhận tên miền trong danh sách.
- Chia sẻ cho máy khác trong mạng LAN (Steam Deck): đổi mô hình bảo mật "chỉ nghe 127.0.0.1", cần thiết kế riêng.
- Chế độ lưu lượng game UDP (chế độ PUBG).
- Tự bật khi khởi động máy trên Windows.

## Không làm

- Cài chứng chỉ gốc, giải mã TLS (MITM): trái cam kết hiện tại của LunaDNS.
- Đổi tài khoản, mở khoá thành tựu, treo giờ chơi, quản lý kho đồ Steam: lệch mục đích, dễ vướng điều khoản Steam.
- Bắt người dùng tự chọn giữa nhiều file chiến lược: D3 làm việc đó tự động.
