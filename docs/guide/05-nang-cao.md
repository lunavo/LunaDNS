# 5. Mục Nâng cao

<sub>[English](en/05-advanced.md)</sub>

Mở rộng mục **Nâng cao**. Thay đổi **có hiệu lực ở lần bật tính năng kế tiếp** (tắt rồi bật lại công tắc). Lựa chọn lưu ở `%ProgramData%\LunaDNS\settings.json`.

| Tuỳ chọn | Giá trị | Ghi chú |
|---|---|---|
| **Điểm cắt gói** | *Sau byte đầu tiên* (mặc định, `FirstByte`) / *Giữa tên miền (SNI)* (`MidSni`) | **Giữ mặc định.** Trên mạng đã thử, MidSni làm Store Steam màn đen và Kiểm tra ra "Không vào được"; chỉ FirstByte qua được. Chọn MidSni mà lỗi thì chọn lại FirstByte rồi tắt/bật lại Proxy SNI. |
| **Tách TLS record** | bật / tắt (mặc định tắt) | Gửi ClientHello thành hai TLS record, cắt tại điểm chọn ở ô trên (với *Sau byte đầu tiên*, record đầu giữ 6 byte đầu của ClientHello vì có máy chủ TLS (như SChannel của Windows) từ chối record đầu ngắn hơn), để bộ lọc ghép lại gói TCP vẫn không thấy trọn tên miền. Chỉ thử khi Kiểm tra vẫn báo "Không vào được" với cả hai điểm cắt. Bật mà lỗi thì tắt đi rồi tắt/bật lại Proxy SNI. |
| **Độ trễ giữa hai phần (ms, 0–20)** | số nguyên | Chờ giữa hai nửa gói đã cắt. Mặc định 0. Giá trị ngoài khoảng bị kẹp về 0–20. |
| **Máy chủ DoH** | *Cloudflare trước, Google dự phòng* (mặc định) / *Google trước, Cloudflare dự phòng* | Đổi nếu một trong hai bị chặn. |

Dưới hàng **Tắt hết & khôi phục** / **Chẩn đoán** (xem [Dòng lệnh](06-dong-lenh.md#báo-cáo)) có thêm hai nút:

- **Kiểm tra cập nhật:** hỏi GitHub Releases xem có bản mới không; có thì hỏi mở trang tải. Ngoài ra app **tự kiểm một lần, khoảng 10 giây sau khi mở**, không hiện hộp thoại: có bản mới thì hiện dải *"Đã có LunaDNS x.y.z (bạn đang dùng a.b.c)."* kèm nút **Tải**. App không tự tải hay tự cài.
- **Gỡ khỏi máy:** chỉ hiện ở bản Setup. Xem [Gỡ và dọn](07-go-va-don-dep.md#gỡ-bản-setup).

Muốn đặt lại mặc định: thoát app (tắt hết) rồi xoá `settings.json`. File hỏng được tự đổi tên `settings.json.corrupt-<thời gian>` và app dùng mặc định.
