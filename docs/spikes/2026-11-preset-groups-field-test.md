# Nhóm dựng sẵn: tra anti-cheat và thử thật trên mạng VN

Spec: `docs/superpowers/specs/2026-11-21-lunadns-custom-domains-design.md` mục 4.4 và 6. Task 13.

## Tên miền anti-cheat

Nguyên tắc: chỉ đưa vào `DomainDenyList` khi có URL nguồn đọc được và nguồn nói rõ đó là tên miền anti-cheat. Không có nguồn thì không thêm, không đoán. Không cấm cả tên miền gốc của hãng nếu nguồn không nói nó chỉ dành cho anti-cheat, vì sẽ chặn luôn store/launcher (DomainDenyList khớp cả tên cha).

| Hãng | Tên miền | Nguồn URL | Ngày tra | Ghi chú |
|---|---|---|---|---|
| Riot (Vanguard) | không có nguồn riêng | https://support.riotgames.com/en-us/riot/security/configure-your-firewall | 21/11/2026 | Trang firewall chính thức chỉ liệt kê file chạy `vgc.exe`, `vgm.exe`; không có tên miền nào cho Vanguard. Không cấm `riotgames.com` (gồm cả launcher/store). |
| EA (Javelin) | không có nguồn riêng | https://help.ea.com/en-us/help/pc/ea-anticheat/ (không tải được, hết thời gian chờ) | 21/11/2026 | EA không công bố danh sách tên miền. Có dấu vết từ kết quả tìm kiếm: một nhân viên nhóm anti-cheat EA nhắc tới `eaanticheat.ac.ea.com` trong luồng diễn đàn https://forums.ea.com/discussions/fc-26-technical-issues-en/javelin-error-95/13265763 , nhưng trang trả 403, chưa đọc nguyên văn nên CHƯA thêm. Nếu xác minh được, thêm `ac.ea.com` sẽ buộc đổi `ea.com` trong `DomainCatalog.OtherGames` thành tên con cụ thể (do khớp tên cha). |
| Blizzard (Warden) | không có nguồn riêng | https://us.support.blizzard.com/en/article/7842 | 21/11/2026 | Bài firewall chính thức chỉ nói về cổng và không hỗ trợ proxy/firewall; không có tên miền, và Warden không được nhắc. Warden chạy trong chính kết nối game, không có tên miền riêng được công bố. Không cấm `battle.net`/`blizzard.com`. |

Đã có sẵn trong `DomainDenyList` (không đổi): `accounts.pubg.com`, `easyanticheat.net`, `battleye.com`, nhãn chứa `xenuine`.

Hệ quả: Task 13 bước 2 và 3 không thêm tên miền nào, không đổi `DomainCatalog`, không có Theory mới (không có tên thật để điền `InlineData`).

## Thử thật

Chờ PO thử trên ≥2 nhà mạng.

| Nhóm | Nhà mạng | Kết luận trước khi bật | Vào được sau khi bật | Tên miền thêm | Ghi chú |
|---|---|---|---|---|---|
