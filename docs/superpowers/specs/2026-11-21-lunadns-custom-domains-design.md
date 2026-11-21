# LunaDNS: nhóm tên miền và danh sách tuỳ chỉnh (thiết kế)

Ngày: 2026-11-21 · Trạng thái: chờ PO duyệt · Nối tiếp [spec v1](2026-11-13-lunadns-design.md)

## 1. Mục tiêu

Hiện Định tuyến DNS và Proxy SNI chỉ áp dụng cho tên miền Steam, cố định trong code (`GameDomains`, `FeatureDomains.Routed`). Bản này cho phép:

1. Bật/tắt từng **nhóm dựng sẵn**: Steam, Nền tảng game khác, Reddit.
2. Một nhóm **Tuỳ chỉnh** do user thêm/sửa/xoá trong app.

Các nguyên tắc ở mục 1 của spec v1 giữ nguyên. Riêng nguyên tắc 2 ("không làm hỏng mạng khác") được đảm bảo bằng bộ kiểm tra tên miền (mục 4), không còn bằng danh sách cố định.

### Ngoài phạm vi bản này
- Chọn cách tách gói riêng cho từng tên miền (hướng B). Cả danh sách dùng chung `SplitMode` của mục Nâng cao.
- Tự phát hiện tên miền bị chặn rồi gợi ý thêm (hướng C).
- Tải danh sách nhóm từ xa. Nhóm dựng sẵn chỉ cập nhật qua bản phát hành.
- Kiểu khớp "chỉ đúng tên này". `/etc/resolver` trên macOS luôn áp cho cả tên miền con, nên hai nền tảng không làm giống nhau được. Mọi mục luôn **gồm cả tên miền con**.
- Chế độ PUBG: giữ nguyên, vẫn dùng nhóm PUBG và file hosts như cũ.
- Đổi tên dự án (xem mục 9).

## 2. Quyết định đã chốt (PO, 2026-11-21)

| # | Quyết định |
|---|---|
| E1 | Hướng A: danh mục nhóm, gồm nhóm dựng sẵn và nhóm Tuỳ chỉnh. |
| E2 | Nhóm dựng sẵn ở bản đầu: Steam, Nền tảng game khác, Reddit. Mỗi nhóm phải thử thật trên mạng VN trước khi phát hành. |
| E3 | Nhóm dựng sẵn chỉ bật/tắt và xem được. Muốn bỏ một tên miền thì tắt cả nhóm, rồi tự thêm phần cần vào Tuỳ chỉnh. |
| E4 | Nhóm Tuỳ chỉnh: thêm, sửa, xoá, bật/tắt từng dòng, nhập/xuất `.txt`. Thay đổi chỉ áp dụng khi bấm **Lưu**. |
| E5 | Giữ tên LunaDNS. Đổi khẩu hiệu README và thêm topics GitHub. |
| E6 | Repo phát hành độc lập trên GitHub. Mục D1 và mục 8 của spec v1, phần nhắc LunaVoShop, đã lỗi thời. |

## 3. Kiến trúc

```
DomainCatalog (Core)              ← nhóm dựng sẵn, nhúng trong code
DomainSelection (Core, settings)  ← nhóm nào bật + danh sách Tuỳ chỉnh
DomainPolicy (Core)               ← chuẩn hoá + kiểm tra, dùng ở mọi lớp
        │
        ▼  RoutedDomains = hợp các nhóm bật + dòng Tuỳ chỉnh bật (đã qua DomainPolicy)
FeatureSet ── StartDnsStub / StartSniProxy / AddDnsRoute(…, RoutedDomains)
        │
        ├─ Windows: NrptDnsRouter, PAC, settings.json (%ProgramData%, chỉ Admin ghi)
        └─ macOS:   ResolverDnsRouter, PAC, settings.json của dịch vụ nền (root)
```

### 3.1 Thay đổi trong Core
- **Đổi tên `GameDomains` thành `DomainCatalog`.** Thêm `DomainGroup.OtherGames` (gộp `Other` cũ với các launcher) và `DomainGroup.Reddit`. `DomainGroup.Pubg` giữ nguyên, nhưng không hiện trong danh sách nhóm của Định tuyến DNS.
- **Thêm `DomainSelection`** (record bất biến):
  - `EnabledGroups`: tập `DomainGroup`, mặc định `{Steam, Custom}`. `DomainGroup.Custom` là công tắc của nhóm Tuỳ chỉnh; danh sách Tuỳ chỉnh rỗng thì bật cũng không có tác dụng;
  - `Custom`: danh sách `CustomDomain(string Domain, bool Enabled)`.
- **Thêm vào `AppSettings`:** `Domains : DomainSelection`. Đọc `settings.json` cũ không có trường này thì ra mặc định; vì danh sách Tuỳ chỉnh rỗng nên hành vi giống hệt bản 0.9.
- **`FeatureDomains.Routed`** đổi từ thuộc tính tĩnh thành hàm `Routed(DomainSelection)`. Hàm này trả danh sách đã chuẩn hoá, bỏ trùng, và bỏ tên miền con khi tên miền cha đã có trong danh sách.
- **Thêm `FeatureSet.ReapplyAsync(DomainSelection)`:** với mỗi tính năng đang bật (DNS, SNI), tắt rồi bật lại theo danh sách mới. Mỗi bước đều ghi journal như hiện nay. Bật lại lỗi thì khôi phục danh sách cũ, bật lại theo danh sách cũ và trả lỗi cho UI. Settings chỉ được ghi sau khi áp dụng thành công.

### 3.2 Chốt an toàn ở lớp nền tảng
Ba chỗ hiện kiểm tra cứng theo `GameDomains.All`:
- `NrptDnsRouter.cs:28`;
- `HostsEditor.cs:79`;
- `ResolverDnsRouter.IsSafeGameDomain`, dùng khi đọc journal để xoá file trong `/etc/resolver`.

Đổi thành:
- **`NrptDnsRouter` và `ResolverDnsRouter`:** dùng `DomainPolicy.IsRoutable(host)`, tức đúng cú pháp, không phải hậu tố công cộng và không nằm trong danh sách cấm. Riêng `ResolverDnsRouter` vẫn giữ kiểm tra ký tự, không có `/` hay `..`, vì tên miền là tên file.
- **`HostsEditor`:** giữ `DomainCatalog.For(Pubg)`. File hosts chỉ phục vụ Chế độ PUBG, không mở rộng.

Lý do vẫn an toàn: journal nằm trong thư mục chỉ SYSTEM/Administrators (Windows) hoặc root (macOS) ghi được. Kẻ ghi được journal thì đã có quyền quản trị. `DomainPolicy` vẫn chặn tên lạ, nên ghi tên tuỳ ý vào journal cũng không định tuyến được TLD hay tên nội bộ.

### 3.3 macOS: dịch vụ nền giữ settings
Hiện `Engine.cs` của dịch vụ nền dùng `new AppSettings()` (mặc định), không lưu gì. Cần thêm:
- **File settings của dịch vụ nền:** `/Library/Application Support/LunaDNS/settings.json`, quyền root 0644. Dịch vụ nền là nơi giữ thật; app chỉ đọc qua lệnh `status`.
- **Lệnh mới trong `DaemonProtocol`:**
  - `get-domains`: trả `DomainSelection` hiện tại;
  - `set-domains`: nhận `DomainSelection`, **chạy lại toàn bộ `DomainPolicy`** (không tin dữ liệu từ app), gọi `ReapplyAsync`, rồi mới ghi file.
  - Quá giới hạn (mục 4.3) hoặc có dòng không hợp lệ thì từ chối cả gói và trả mã lỗi cùng chỉ số dòng.
- **Kiểm tra người gọi:** `set-domains` dùng chung cách kiểm tra `PeerCredentials` với `enable`/`disable`.

Windows giữ mô hình hiện tại: app chạy bằng quyền Admin, tự ghi settings và gọi `ReapplyAsync` trong cùng tiến trình.

## 4. `DomainPolicy`: chuẩn hoá và kiểm tra

### 4.1 Chuẩn hoá (khi thêm, sửa, nhập file)
Áp lần lượt trên từng dòng:
1. Cắt khoảng trắng. Bỏ dòng trống và dòng bắt đầu bằng `#` (chú thích trong file `.txt`).
2. Có `://` thì lấy phần host của URL. Bỏ cổng, đường dẫn, `?` và `#`.
3. Bỏ tiền tố `*.`, rồi bỏ `www.` ở đầu.
4. Bỏ dấu chấm cuối và đổi sang chữ thường (`DomainMatcher.Normalize`).
5. Tên miền Unicode thì chuyển sang punycode bằng `IdnMapping`.

### 4.2 Từ chối (hiện lỗi ngay trên dòng)
| Mã | Điều kiện | Câu báo (vi) |
|---|---|---|
| `Syntax` | Nhãn rỗng, dài quá 63 ký tự, tổng dài quá 253, có ký tự ngoài `a-z 0-9 -`, nhãn bắt đầu hoặc kết thúc bằng `-`, là địa chỉ IP | "Không phải tên miền hợp lệ." |
| `PublicSuffix` | Chính nó là hậu tố công cộng theo Public Suffix List, nhúng sẵn trong app (ví dụ `com`, `com.vn`, `github.io`) | "Tên miền quá rộng, sẽ ảnh hưởng mọi trang dưới `.x`." |
| `LocalName` | Đuôi `localhost`, `local`, `lan`, `home.arpa`, `internal`, `arpa`, `test`, `invalid`, hoặc chỉ có một nhãn | "Tên miền nội bộ, LunaDNS không định tuyến." |
| `Denied` | Trùng một mục trong danh sách cấm (4.4), là tên miền con của mục đó, **hoặc là tên miền cha của mục đó** (luật DNS luôn gồm tên miền con, nên định tuyến `riotgames.com` sẽ kéo theo cả tên miền anti-cheat bên dưới) | "Tên miền này bị chặn vì lý do an toàn: <lý do>." |
| `Duplicate` | Đã có trong Tuỳ chỉnh, hoặc đã nằm trong nhóm dựng sẵn đang bật | "Đã có trong nhóm <tên>." (cảnh báo vàng, không thêm) |
| `Limit` | Danh sách Tuỳ chỉnh đã có 200 dòng | "Tối đa 200 tên miền." |

### 4.3 Giới hạn
Tối đa 200 dòng Tuỳ chỉnh, độc lập với số tên miền trong nhóm dựng sẵn. Cần spike kiểm số namespace tối đa của NRPT và hiệu năng của `/etc/resolver` khi có khoảng 250 file. Nếu spike cho thấy NRPT không chịu nổi thì gộp nhiều namespace vào một luật.

### 4.4 Danh sách cấm (cố định trong code, có lý do cho từng mục)
- **Anti-cheat**, định tuyến sai có thể bị ban (mở rộng spec v1 mục 4.4): `xenuine.com`, `accounts.pubg.com`, `easyanticheat.net`, `battleye.com`. Tên miền anti-cheat của Riot (Vanguard), EA (Javelin) và Battle.net phải tra xong và thêm vào trước khi nhóm "Nền tảng game khác" được phát hành (giai đoạn 6).
- **Máy chủ DoH của chính app**, tránh vòng lặp vì stub sẽ tự hỏi chính nó: `cloudflare-dns.com`, `one.one.one.one`, `dns.google`.
- **Kiểm tra cổng đăng nhập Wi-Fi và kết nối của hệ điều hành**, tránh làm hỏng việc dò captive portal: `msftconnecttest.com`, `msftncsi.com`, `captive.apple.com`, `connectivitycheck.gstatic.com`.

Không cấm ngân hàng hay `.gov.vn`: định tuyến qua DoH chỉ ra đúng IP, không có hại về kỹ thuật.

## 5. Giao diện

### 5.1 Màn hình chính: khu "Tên miền"
Thêm khu này dưới hai công tắc hiện có. Mỗi nhóm một dòng:

```
[✓] Steam                      11 tên miền   ● Kiểm tra lúc 14:02: ổn     [Xem]
[ ] Nền tảng game khác         n tên miền    —                            [Xem]
[ ] Reddit                     n tên miền    —                            [Xem]
[✓] Tuỳ chỉnh                  7/9 đang bật  ● 2 tên miền không giúp được [Sửa…]
```

- **Bật/tắt nhóm** dùng chung luồng Lưu: đổi xong gọi `ReapplyAsync` ngay, vì mỗi lần bấm là một thay đổi trọn vẹn.
- **[Xem]** mở danh sách chỉ đọc của nhóm dựng sẵn.
- **Không còn tên miền nào trong khi công tắc DNS hoặc SNI đang bật:** app từ chối thay đổi (`FeatureError.NoDomains`), trả công tắc nhóm về như cũ và báo "Chưa có tên miền nào được chọn. Bật ít nhất một nhóm, hoặc tắt Định tuyến DNS và Proxy SNI trước." Bật công tắc DNS/SNI khi danh sách rỗng cũng bị từ chối với câu này.
- **Kết quả "Kiểm tra" ở màn hình chính** (`ProbeRunner`) chia theo nhóm đang bật. Mỗi nhóm dựng sẵn có host mẫu riêng (`ProbeTargets`). Nhóm Tuỳ chỉnh lấy tối đa 5 dòng đang bật làm mẫu.

### 5.2 Hộp thoại "Tên miền tuỳ chỉnh"
```
┌ Tên miền tuỳ chỉnh ──────────────────────────────────────────────┐
│ [ Dán một hoặc nhiều tên miền / đường link…          ] [Thêm]    │
│ ──────────────────────────────────────────────────────────────── │
│ [✓] reddit.com          Giúp được (cần Proxy SNI)            [🗑] │
│ [✓] example.org         LunaDNS không giúp được              [🗑] │
│ [ ] foo.vn              chưa kiểm tra                        [🗑] │
│ ──────────────────────────────────────────────────────────────── │
│ [Kiểm tra đã chọn] [Kiểm tra tất cả]  [Nhập .txt] [Xuất .txt]    │
│                                      9/200     [Huỷ]  [Lưu]      │
└──────────────────────────────────────────────────────────────────┘
```
- **Ô Thêm:** dán nhiều dòng; tách theo xuống dòng, dấu phẩy và khoảng trắng. Mỗi mục qua `DomainPolicy`. Mục hợp lệ vào danh sách. Mục lỗi hiện ngay dưới ô nhập, kèm câu báo ở 4.2, và không bị mất chữ để user sửa lại.
- **Sửa:** bấm đúp vào tên miền để sửa tại chỗ, kiểm tra lại như lúc thêm. Enter để nhận, Esc để huỷ.
- **Xoá:** bấm 🗑 thì dòng biến mất, hiện thanh "Đã xoá reddit.com · Hoàn tác" trong 6 giây. Không có hộp xác nhận.
- **Nhập .txt:** mỗi dòng một tên miền, chấp nhận chú thích `#`. Gộp vào danh sách hiện có, không thay thế. Xong thì báo "Đã thêm X, bỏ qua Y (trùng/lỗi)".
- **Xuất .txt:** ghi các dòng (cả dòng đang tắt, có tiền tố `# off: `) ra file user chọn.
- **Huỷ:** đóng hộp thoại, không đổi gì. Danh sách đã sửa mà chưa lưu thì hỏi "Bỏ các thay đổi?".
- **Lưu:** gửi `DomainSelection` mới:
  - Windows: gọi `ReapplyAsync` rồi ghi settings;
  - macOS: gọi `set-domains`.
  - Trong lúc áp dụng: các nút bị khoá, hiện "Đang áp dụng…". Lỗi thì giữ hộp thoại mở, hiện lỗi, và trên máy vẫn là danh sách cũ.

### 5.3 Kiểm tra từng tên miền
Dùng lại `Prober` với host là chính tên miền. `Prober` đã so DNS hệ thống với DoH (có `KnownBlockIps`), rồi thử TLS thường và TLS tách ClientHello theo `SplitMode` hiện tại, và trả một `ProbeVerdict`. Mỗi dòng hiện một kết luận; chú thích (tooltip) hiện IP từ DNS hệ thống và IP từ DoH.

| `ProbeVerdict` | Kết luận hiển thị |
|---|---|
| `Ok` | "Không bị chặn, không cần LunaDNS" (vàng) |
| `DnsPoisoned` | "Giúp được (cần Định tuyến DNS)" |
| `SniFiltered` | "Giúp được (cần Proxy SNI)" |
| `Unreachable` | "Chặn theo IP hoặc cách khác, LunaDNS không giúp được" (đỏ) |
| lỗi khi chạy (ngoại lệ, huỷ) | "Không kiểm tra được, thử lại" |

- **Kết quả kiểm tra** lưu cùng dòng, nhưng chỉ trong bộ nhớ của phiên. Không ghi vào settings.
- **Kiểm tra không phải điều kiện để Lưu.** User vẫn lưu được tên miền "không giúp được", vì mạng mỗi lúc một khác.

## 6. Nhóm dựng sẵn

| Nhóm | Ứng viên (chốt sau khi thử thật ở giai đoạn 6) | Host mẫu để kiểm tra |
|---|---|---|
| Steam | Giữ nguyên danh sách hiện tại | Giữ `ProbeTargets.Steam` |
| Nền tảng game khác | Danh sách `Other` hiện có + launcher/store của Epic Games, Battle.net, Riot, EA. Không có tên miền anti-cheat (4.4) | Mỗi nền tảng một host store |
| Reddit | `reddit.com`, `redd.it`, `redditmedia.com`, `redditstatic.com`, `reddituploads.com` | `www.reddit.com`, `i.redd.it` |

Nhóm nào thử thật trên ít nhất hai nhà mạng VN mà LunaDNS không giúp được thì không đưa vào bản phát hành. Kết quả thử ghi vào `docs/spikes/`.

## 7. Xử lý lỗi
- **`ReapplyAsync` lỗi giữa chừng:** quay về danh sách cũ, mọi bước có journal. Quay về cũng lỗi thì tính năng chuyển sang trạng thái lỗi như hiện nay, và `--cleanup` hoàn tác được.
- **`settings.json` có `Domains` hỏng** (sửa tay, có dòng không hợp lệ): khi đọc, bỏ các dòng không qua `DomainPolicy` và ghi log cảnh báo. Không làm app chết.
- **App mới, dịch vụ nền cũ** (macOS): dịch vụ nền không hiểu `set-domains` thì app báo "Cần cập nhật dịch vụ nền" và dẫn tới nút cài lại dịch vụ nền có sẵn.

## 8. Kiểm thử
- **Core:**
  - `DomainPolicy`: mỗi bước chuẩn hoá, mỗi mã từ chối, IDN, URL có cổng/đường dẫn;
  - `FeatureDomains.Routed`: bỏ trùng, gộp tên miền con;
  - `AppSettings`: đọc file 0.9 không có `Domains` ra mặc định Steam;
  - `ReapplyAsync`: thành công, lỗi khi bật lại thì khôi phục, journal đúng thứ tự.
- **Windows:** `NrptDnsRouter` chấp nhận tên miền tuỳ chỉnh hợp lệ, từ chối TLD hoặc tên trong danh sách cấm; dọn đúng luật cũ khi danh sách thay đổi.
- **macOS:**
  - `ResolverDnsRouter` với tên miền tuỳ chỉnh, chặn `/` và `..`;
  - `set-domains`: từ chối gói có dòng lỗi, chạy lại kiểm tra ở dịch vụ nền, kiểm `PeerCredentials`.
- **App:** ViewModel của hộp thoại (dán nhiều dòng, sửa, xoá + hoàn tác, nhập/xuất, Huỷ khi có thay đổi chưa lưu, Lưu lỗi thì giữ hộp thoại).
- **Thử tay:** Windows 10/11 (VM sandbox `build/sandbox/LunaDNS.wsb`), macOS arm64. Thử trên các nhà mạng VN cho từng nhóm dựng sẵn. Kiểm việc dọn sau crash khi đang có tên miền tuỳ chỉnh.

## 9. Tên và tài liệu
- Giữ tên LunaDNS, bundle id `vn.lunavo.lunadns` và mọi đường dẫn. Lý do: tên đã nằm trong đường dẫn hệ thống và journal của các máy đã cài 0.9.0. DNS vẫn là lớp nền của cả hai tính năng.
- **Khẩu hiệu README (en/vi):** "Vào lại Steam, Reddit… khi nhà mạng chặn DNS hoặc lọc SNI." Thêm topics GitHub: `dns-over-https`, `sni`, `dpi-bypass`, `vietnam`, `steam`.
- **Tài liệu hướng dẫn:**
  - thêm trang `docs/guide/10-ten-mien.md`: nhóm dựng sẵn, Tuỳ chỉnh, đọc kết quả kiểm tra, giới hạn "LunaDNS không vượt được chặn theo IP";
  - sửa `03-ba-cong-tac.md` cho khớp.
- **Spec v1:** thêm ghi chú lỗi thời ở mục D1 và mục 8 (phần nhắc LunaVoShop).

## 10. Rủi ro và spike
1. **Số namespace NRPT** và hiệu năng `/etc/resolver` với khoảng 250 mục (4.3).
2. **Reddit và nền tảng game có thể bị chặn theo IP** ở một số nhà mạng. Khi đó nhóm không vào bản phát hành (mục 6).
3. **Public Suffix List nhúng sẵn sẽ cũ dần.** Chấp nhận được, vì chỉ dùng để chặn tên quá rộng. Cập nhật mỗi lần phát hành.
