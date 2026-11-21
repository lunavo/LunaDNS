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

Bước 2–3 ban đầu không thêm gì. Sau đó PO quyết định (21/11/2026) cấm phòng xa `ac.ea.com` dựa trên dấu vết diễn đàn EA ở trên. Vì `DomainDenyList` khớp cả tên cha, `ea.com` trong `DomainCatalog.OtherGames` được thay bằng các tên con thấy được khi thử thật: `www.ea.com`, `drop-assets.ea.com`. EA app (đăng nhập, tải game) chưa thử, nên có thể còn thiếu tên con.

## Thử thật

Thử ngày 21/11/2026 trên một nhà mạng (Wi-Fi VNPT, AS45899). PO cho phép chỉ thử một mạng. Dịch vụ nền bản `0.5.0+5624b47` (nhánh này, trước commit chặn `ac.ea.com`). Bật cả ba nhóm cùng lúc với Định tuyến DNS + Proxy SNI. Kết luận "trước khi bật" lấy từ `Prober` của Core (cùng bộ kiểm tra với nút Kiểm tra) khi LunaDNS tắt. "Vào được" đo bằng curl (trực tiếp sau khi định tuyến DNS và qua proxy 127.0.0.1:18080) và Chrome dùng PAC hệ thống.

| Nhóm | Nhà mạng | Kết luận trước khi bật | Vào được sau khi bật | Tên miền thêm | Ghi chú |
|---|---|---|---|---|---|
| Steam | VNPT | store.steampowered.com, steamcommunity.com: SniFiltered; api.steampowered.com, cdn.akamai.steamstatic.com: DnsPoisoned | Có. curl qua proxy 200; Chrome tải đủ trang chủ (store/avatars/shared/cdn.fastly.steamstatic.com) | không | Không bật thì curl trực tiếp hết giờ (15 s). |
| Nền tảng game khác | VNPT | store.epicgames.com, eu.battle.net, www.ea.com: Ok (không bị chặn) | Có, nhưng không bật cũng vào được | không | Epic trả 403 cho curl là chặn bot của Cloudflare, không phải nhà mạng. www.ea.com tải từ `www.ea.com`, `drop-assets.ea.com`. LunaDNS không giúp được gì trên mạng này. |
| Reddit | VNPT | www.reddit.com, i.redd.it: DnsPoisoned | Có. curl trực tiếp và qua proxy 200 (i.redd.it gốc trả 404 như bình thường) | không | Chrome của extension không cho mở reddit.com nên chưa xem DevTools; chỉ DNS là đủ. |

## Chốt

- Steam, Reddit: giữ.
- Nền tảng game khác: trên mạng đã thử, mọi host đều Ok nên LunaDNS không giúp được. PO quyết định (21/11/2026) bỏ khỏi `DomainCatalog.Selectable`. Code nhóm giữ lại; settings cũ còn bật nhóm này thì `FeatureDomains.Routed` bỏ qua.
