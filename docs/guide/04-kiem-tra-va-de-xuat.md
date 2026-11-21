# 4. Kiểm tra và Áp dụng đề xuất

<sub>[English](en/04-check-and-suggestions.md)</sub>

## Kiểm tra

Nút **Kiểm tra** (cũng tự chạy khi mở app) thử các host mẫu bằng đường mạng **gốc**, không qua LunaDNS:

- Steam: `store.steampowered.com`, `steamcommunity.com`, `api.steampowered.com`, `cdn.akamai.steamstatic.com`

Mỗi host nhận một kết luận:

| Kết luận | Nghĩa | Biện pháp đề xuất |
|---|---|---|
| Bình thường | Vào được | không |
| DNS bị chặn | DNS nhà mạng trả sai (đầu độc) | Định tuyến DNS |
| Bị lọc SNI | DNS đúng nhưng bắt tay TLS bị cắt | Định tuyến DNS + Proxy SNI |
| Không vào được | Không vào được kể cả khi né chặn | không (có thể mạng lỗi hoặc máy chủ bảo trì) |

Kết quả hiện theo nhóm (`Steam: …`) kèm giờ kiểm tra. Lỗi thì có thông báo *"Không chạy được kiểm tra…"*, chi tiết trong log.

> **Quan trọng:** Kiểm tra đo mạng gốc, nên **sau khi đã bật biện pháp, nó vẫn có thể báo "Bị lọc SNI"**. Đó là bình thường: hãy thử mở Steam thật.

## Áp dụng đề xuất

Nút **Áp dụng đề xuất** chỉ hiện khi còn biện pháp **cho Steam** chưa bật. Bấm thì app bật các công tắc còn thiếu (Định tuyến DNS và/hoặc Proxy SNI). Hết việc thì báo *"Các biện pháp đề xuất cho Steam đều đang bật."*

- Nếu máy đang có proxy riêng, hộp hỏi thay cấu hình proxy vẫn hiện như khi bật tay.
- Không thấy bị chặn: *"Không thấy bị chặn. Không cần bật gì."*
