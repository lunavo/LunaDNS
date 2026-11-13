# LunaDNS — Thiết kế v1

- Ngày: 2026-11-13
- Trạng thái: chờ PO duyệt
- Nguồn tham khảo: đánh giá SteamAccessFix v1.2.0 (gitlab.com/vuviett47/SteamAccessFix). Repo đó **không có file LICENSE**, nên LunaDNS viết lại từ đầu theo cùng kỹ thuật, không chép mã.

## 1. Mục tiêu

App Windows giúp người chơi Việt Nam vào được Steam (Store, Community, CDN, đăng nhập) và tuỳ chọn PUBG khi nhà mạng chặn bằng đầu độc DNS hoặc lọc SNI. Tiêu chí:

1. **Chỉ can thiệp khi cần:** kiểm tra kết nối trước, chỉ bật đúng biện pháp cho lỗi phát hiện được.
2. **Không làm hỏng mạng khác:** DNS của card mạng, LAN nội bộ, CDN của trang khác và lưu lượng UDP của trận đấu giữ nguyên.
3. **Luôn khôi phục được** đúng trạng thái cũ, kể cả sau khi crash, tắt máy ngang hay mất điện.
4. **Không mở lỗ bảo mật:** không có đường leo thang quyền, cập nhật có chữ ký.
5. Giao diện đồ hoạ **tiếng Việt có dấu**, mỗi tính năng có công tắc riêng.

### Ngoài phạm vi v1
- Bản macOS. Kiến trúc chừa sẵn chỗ (mục 3), chưa làm.
- Kích hoạt bằng mã đơn hàng hoặc thu phí. v1 miễn phí.
- Tự cài bản cập nhật. v1 chỉ thông báo và mở trang tải.
- Windows Service. v1 là một exe chạy quyền Admin. Phần lõi tách riêng để sau này nâng lên service mà không phải viết lại.
- Tinh chỉnh MTU, `NetworkThrottlingIndex`, `SystemResponsiveness`: bỏ, vì không có tác dụng thật.

## 2. Quyết định đã chốt (PO, 2026-11-13)

| # | Quyết định |
|---|---|
| D1 | App Windows riêng, đặt ở `D:\Projects\lunadns`. LunaVoShop chỉ thêm trang giới thiệu và link tải. |
| D2 | Phân phối miễn phí. |
| D3 | Dùng .NET 8 + Avalonia UI, Windows trước. Lõi viết thuần .NET, phần đụng vào hệ điều hành nằm sau interface. |
| D4 | Viết lại, không chép mã SteamAccessFix. |

## 3. Kiến trúc

```
LunaDns.sln
├── src/LunaDns.Core/              # thuần .NET 8, không gọi API riêng của Windows
│   ├── Probing/                   # kiểm tra và phân loại lỗi kết nối
│   ├── Dns/                       # DoH client + stub resolver 127.0.0.1:53
│   ├── Proxy/                     # proxy HTTP CONNECT + cắt gói ClientHello + phục vụ PAC
│   ├── Journal/                   # nhật ký thay đổi, áp dụng và hoàn tác
│   ├── Features/                  # FeatureSet: bật/tắt từng tính năng qua Journal
│   └── Platform/INetworkConfigurator.cs
├── src/LunaDns.Platform.Windows/  # bản cài đặt INetworkConfigurator cho Windows
├── src/LunaDns.App/               # Avalonia UI + điểm vào CLI (--cleanup, --report)
└── tests/
    ├── LunaDns.Core.Tests/        # xUnit, không cần quyền Admin
    └── LunaDns.Platform.Windows.Tests/  # tích hợp, chạy tay trên máy ảo
```

Quy tắc phụ thuộc: `App → Core + Platform.Windows`, `Platform.Windows → Core`, còn `Core` không phụ thuộc gì. Chỉ `Platform.Windows` được gọi registry, `netsh`, P/Invoke và Task Scheduler.

### `INetworkConfigurator`: các thao tác hệ thống
Mỗi thao tác trả về `Snapshot` (giá trị trước khi đổi) để Journal hoàn tác được:

| Thao tác | Bản Windows | Bản macOS (sau này) |
|---|---|---|
| `AddDnsRoute(domain[], server)` / `RemoveDnsRoute` | Quy tắc NRPT (`Add-DnsClientNrptRule`, gọi qua WMI/CIM, không cần PowerShell) | file `/etc/resolver/<domain>` |
| `SetAutoProxyUrl(url)` / restore | `HKCU\…\Internet Settings\AutoConfigURL` + `InternetSetOption(39/37)` | `networksetup -setautoproxyurl` |
| `AddHostsEntries(entries)` / restore | Ghi file hosts theo đúng encoding cũ, giữ nguyên thuộc tính | `/etc/hosts` |
| `FlushDnsCache()` | `DnsFlushResolverCache` | `dscacheutil -flushcache` |
| `RegisterBootCleanup()` / `Unregister` | Task Scheduler, chạy lúc khởi động máy bằng tài khoản SYSTEM | `launchd` daemon |

## 4. Thành phần

### 4.1 Probing: kiểm tra trước
`Prober` chạy song song, mỗi phép kiểm tra hết hạn sau 3 giây, trên các host mẫu: `store.steampowered.com`, `steamcommunity.com`, `api.steampowered.com`, `cdn.akamai.steamstatic.com`, và `prod-live-front.playbattlegrounds.com` nếu bật nhóm PUBG.

Với mỗi host:
1. Phân giải bằng DNS hệ thống, rồi phân giải bằng DoH (Cloudflare, dự phòng Google).
2. Bắt tay TLS tới IP lấy từ DoH, gửi ClientHello nguyên vẹn.
3. Nếu bước 2 lỗi, bắt tay lại với ClientHello đã cắt gói.

Kết quả là một `ProbeVerdict` cho mỗi host:

| Verdict | Điều kiện | Biện pháp đề xuất |
|---|---|---|
| `Ok` | DNS hệ thống đúng và TLS nguyên vẹn thành công | không làm gì |
| `DnsPoisoned` | IP từ DNS hệ thống không khớp DoH (khác ASN hoặc là IP chặn), còn TLS tới IP DoH thì thành công | Định tuyến DNS (4.2) |
| `SniFiltered` | TLS nguyên vẹn bị reset hoặc hết hạn, TLS cắt gói thành công | Định tuyến DNS + Proxy SNI (4.3) |
| `Unreachable` | cả hai cách đều lỗi | không bật gì, hiện hướng dẫn và nút Chẩn đoán |

"Khớp IP" nghĩa là cùng nằm trong tập IP DoH trả về, hoặc cùng ASN theo một bảng ASN nhỏ nhúng sẵn cho Valve và Akamai. Danh sách IP chặn đã biết của các nhà mạng nằm trong `Core/Probing/KnownBlockIps.cs`.

### 4.2 Định tuyến DNS: chỉ cho domain game
- `DohStubResolver` lắng nghe **127.0.0.1:53** (UDP và TCP). Nó chỉ trả lời các domain trong `GameDomains` (lấy từ danh sách `AllGameDomains` của SAF, bỏ các domain anti-cheat). Domain khác bị trả `REFUSED`.
- Truy vấn được chuyển sang DoH theo RFC 8484 (Cloudflare, dự phòng Google), cache theo TTL, tối đa 15 phút.
- Quy tắc NRPT trỏ namespace `.steampowered.com`, `.steamcommunity.com`… về `127.0.0.1`. DNS của card mạng **không bị đổi**.
- Dùng stub DoH thay vì NRPT trỏ thẳng 1.1.1.1 vì một số nhà mạng chặn hoặc chuyển hướng UDP 53 tới mọi IP, và Windows 10 không có DoH gốc.
- Nếu cổng 127.0.0.1:53 đã bị app khác dùng (bind với `ExclusiveAddressUse` thất bại), báo lỗi trên giao diện và không bật tính năng.

### 4.3 Proxy SNI
- `SplitProxy` lắng nghe `127.0.0.1:<cổng trống trong 18080–18090>`, bind bằng `ExclusiveAddressUse = true`.
- Phục vụ `/proxy.pac`, chỉ gồm các domain trong `GameDomains`. Mọi domain khác trả `DIRECT`.
- Với `CONNECT` tới host **thuộc `GameDomains`**: đọc trọn TLS record đầu tiên, cắt tại byte thứ nhất (mặc định) hoặc giữa SNI (chọn trong mục Nâng cao), rồi nối tiếp hai chiều. Host không thuộc danh sách thì trả `403`, để proxy không thành proxy mở cho mọi app trên máy.
- Không chờ `Thread.Sleep(20)`: dùng `NoDelay` cùng hai lần `Write` riêng. Nếu probe cho thấy mạng cần độ trễ thì chỉnh trong Nâng cao (0–20 ms).
- Không bao giờ giải mã TLS và không cài chứng chỉ.

### 4.4 Chế độ PUBG (tuỳ chọn, mặc định TẮT)
- Thêm dòng hosts `127.0.0.1 <domain> # LunaDNS` cho các domain lobby/entry của PUBG, cùng một listener `127.0.0.1:443` (bind exclusive) chuyển tiếp theo SNI như SAF.
- **Không** chuyển hướng các domain anti-cheat (`*xenuine*`) và `accounts.pubg.com`, trừ khi probe cho thấy chính các domain đó bị chặn. Khi đó giao diện cảnh báo riêng và người dùng phải xác nhận lần nữa.
- Kết nối không có SNI hoặc SNI không thuộc danh sách thì đóng luôn, không mặc định đẩy sang PUBG như SAF.
- Khi bật lần đầu, hiện cảnh báo: "Chế độ này sửa file hosts và chuyển tiếp kết nối của PUBG. Chưa ghi nhận bị ban, nhưng không có gì đảm bảo với anti-cheat."

### 4.5 Journal: nhật ký thay đổi
- File `%ProgramData%\LunaDNS\journal.json` ghi mỗi thay đổi theo dạng `{ id, feature, op, snapshot, appliedAt }`. Ghi nguyên tử: viết file tạm, rồi `File.Replace`.
- Thứ tự luôn là: **ghi Journal, sau đó mới đổi hệ thống**. Hoàn tác chạy theo thứ tự ngược (LIFO), mỗi bước idempotent (chạy hai lần vẫn đúng). Bước nào lỗi thì giữ lại trong Journal để lần sau thử tiếp.
- Mỗi lần khởi động (App hoặc `--cleanup`): nếu Journal còn mục nào mà app không đang chạy (kiểm tra bằng mutex `Global\LunaDNS`) thì hoàn tác hết.
- Không có thao tác nào ngoài Journal: không reset WinHTTP, không đụng tới `DefaultConnectionSettings` hay WPAD.

### 4.6 Thư mục dữ liệu và ACL
- `%ProgramData%\LunaDNS` được tạo với ACL tường minh, tắt kế thừa: chỉ `SYSTEM` và `Administrators` có Full, còn `Users` không có quyền gì.
- Khi mở app: nếu thư mục đã tồn tại mà owner không phải `SYSTEM` hoặc `Administrators`, hoặc ACL cho `Users` quyền ghi, thì **từ chối dùng**. App đổi tên thư mục thành `LunaDNS.untrusted-<ts>`, tạo thư mục mới và ghi log cảnh báo. Việc này chặn kiểu tấn công đặt sẵn file đã thấy ở SAF.

### 4.7 Dọn dẹp khi khởi động máy
- Bật mặc định khi người dùng bật tính năng đầu tiên. App copy chính nó vào `%ProgramFiles%\LunaDNS\LunaDNS.exe` (chỉ Admin ghi được), rồi đăng ký task `LunaDNS Cleanup` với trigger lúc khởi động máy, chạy bằng tài khoản `SYSTEM` với lệnh `LunaDNS.exe --cleanup`.
- `--cleanup` chỉ hoàn tác Journal rồi thoát, không bật tính năng nào và không có giao diện.
- Gỡ task bằng nút "Gỡ khỏi máy" trong Cài đặt, nút này đồng thời xoá `%ProgramFiles%\LunaDNS` và `%ProgramData%\LunaDNS`.
- v1 không tự bật tính năng khi khởi động. Người dùng mở app khi cần chơi.

### 4.8 Giao diện (Avalonia)
- **Màn hình chính:** nút "Kiểm tra", sau đó hiện kết quả theo từng nhóm (Steam, PUBG) cùng biện pháp đề xuất và nút "Áp dụng đề xuất".
- **Công tắc:** Định tuyến DNS, Proxy SNI, Chế độ PUBG. Mỗi công tắc có một dòng giải thích ngắn.
- **Khu vực trạng thái:** đang bật gì, cổng proxy, số kết nối, lần kiểm tra cuối.
- **Nút "Tắt hết & khôi phục"**, luôn hiển thị.
- **Biểu tượng khay hệ thống:** đóng cửa sổ thì thu xuống khay. Menu khay có "Mở" và "Thoát & khôi phục". Thoát luôn hoàn tác Journal.
- **Mục Nâng cao:** điểm cắt gói, độ trễ cắt gói, chọn DoH (Cloudflare/Google).
- **Mục Cài đặt:** kiểm tra cập nhật, Chẩn đoán, Gỡ khỏi máy.
- Manifest `requireAdministrator`. Thông báo tiếng Việt có dấu, gom vào file resource `Strings.vi.resx` để sau này dịch được.

### 4.9 Chẩn đoán
`--report` (hoặc nút "Chẩn đoán") tạo `LunaDNS-report-<ts>.txt` trên Desktop, gồm: phiên bản Windows và app, kết quả probe, Journal hiện tại, quy tắc NRPT của LunaDNS, giá trị AutoConfigURL, các dòng hosts có `# LunaDNS`, 200 dòng log cuối.

Báo cáo **không** chứa tên user, tên máy hay IP LAN: thay bằng `<user>`, `<host>`, và che mờ IP thành dạng `192.168.x.x`. Khách gửi file này qua chat khi cần hỗ trợ.

### 4.10 Cập nhật
- Khi mở app, tải `https://github.com/<org>/lunadns/releases/latest/download/manifest.json`. `<org>` là tài khoản GitHub của PO, chốt trước khi làm story phát hành.
- Định dạng: `{ version, url, sha256, notes, sig }`, trong đó `sig` là chữ ký Ed25519 của phần còn lại. Khoá công khai nhúng trong app, khoá bí mật chỉ giữ trên máy PO, không đưa lên CI.
- Chữ ký sai thì bỏ qua và ghi log. Chữ ký đúng và có phiên bản mới thì hiện thông báo kèm nút mở trang tải. v1 không tự tải và không tự chạy bản mới.
- Chỉ một nguồn duy nhất, không có nguồn dự phòng.

## 5. Luồng chính

```
Mở app (UAC)
 → kiểm tra ACL thư mục dữ liệu (4.6)
 → lấy mutex Global\LunaDNS; nếu đã có phiên khác thì đưa cửa sổ đó lên và thoát
 → Journal còn mục? → hoàn tác (4.5)
 → hiện màn hình chính, tự chạy "Kiểm tra"
 → người dùng bấm "Áp dụng đề xuất" hoặc bật công tắc
     → FeatureSet.Enable(f): Journal.Record → Configurator.Apply → FlushDnsCache
 → chạy nền (cửa sổ hoặc khay)
 → Thoát / "Tắt hết" → Journal.RollbackAll
 → tắt máy / đăng xuất (WM_ENDSESSION) → cố RollbackAll trong thời gian Windows cho phép
 → nếu không kịp hoặc chết đột ngột: task --cleanup lúc khởi động máy sau, hoặc lần mở app sau, sẽ hoàn tác
```

## 6. Xử lý lỗi

- Mỗi thao tác hệ thống ném `ConfiguratorException` kèm mã lỗi. `FeatureSet` bắt lỗi, hoàn tác phần vừa làm của chính tính năng đó, rồi hiện thông báo tiếng Việt. Không có `catch { }` rỗng.
- Log ở `%ProgramData%\LunaDNS\logs\`, xoay vòng mỗi file 1 MB, giữ 5 file.
- Proxy hoặc stub DNS chết giữa chừng thì watchdog nội bộ khởi động lại tối đa 3 lần trong 1 phút. Quá giới hạn thì tắt tính năng đó qua Journal và báo cho người dùng.

## 7. Bảo mật: đối chiếu với lỗi của SAF

| Lỗi ở SAF | LunaDNS |
|---|---|
| Cập nhật dự phòng trỏ tới một namespace GitHub không ai giữ | Một nguồn duy nhất, manifest ký Ed25519, không tự chạy bản mới (4.10) |
| `irm \| iex` từ nhánh `main` với quyền Admin | Chỉ phát hành exe đã ký, kèm SHA-256 |
| User thường ghi được vào ProgramData, dẫn tới leo thang quyền | ACL tường minh, không tin thư mục lạ, exe nằm ở Program Files (4.6, 4.7) |
| Proxy mở cho mọi host | Proxy chỉ nhận `GameDomains` (4.3) |
| `ReuseAddress` trên cổng 443 | `ExclusiveAddressUse` (4.3, 4.4) |
| Tắt máy ngang làm kẹt hosts | Journal + task dọn dẹp lúc khởi động (4.5, 4.7) |

Ký code bằng Authenticode là **bắt buộc trước khi phát hành công khai**. Bản beta chưa ký chỉ phát cho người quen qua chat.

## 8. Phát hành và LunaVoShop

- Build: `dotnet publish src/LunaDns.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true`. Đích là file khoảng 40–60 MB, chạy trên Windows 10 1809 trở lên và Windows 11.
- GitHub Releases đăng `LunaDNS-<ver>.exe`, `manifest.json` và `SHA256SUMS`.
- **LunaVoShop** (story riêng trong repo lunavo-assistant): trang `/lunadns` gồm giới thiệu, ảnh chụp, hướng dẫn, link tải tới GitHub Releases, SHA-256 và mục câu hỏi thường gặp (anti-cheat, khôi phục mạng). Không cần Worker hay D1.

## 9. Kiểm thử

**Unit (`LunaDns.Core.Tests`, xUnit, chạy được trong CI không cần Admin):**
- Bộ tách ClientHello: dùng fixture byte thật của Chrome/Steam/curl, gồm cả ClientHello dài hơn một TCP segment và trường hợp không có SNI.
- Bộ cắt gói: kiểm tra điểm cắt cho từng chế độ, và nối lại các mảnh phải ra đúng byte ban đầu.
- PAC: domain trong danh sách thì đi PROXY, domain ngoài thì DIRECT, subdomain cũng khớp, chuỗi giống domain (như `evilsteampowered.com`) không khớp.
- DoH: phân tích wire format, tôn trọng TTL, trả REFUSED cho domain ngoài danh sách.
- Journal: dùng `FakeConfigurator` để kiểm tra áp dụng và hoàn tác LIFO, lỗi giữa chừng, hoàn tác hai lần, file journal hỏng, và khôi phục sau khi "crash" giả lập.
- Prober: dùng transport giả để phân loại đủ 4 verdict.
- Manifest cập nhật: chữ ký đúng, sai, thiếu, và phiên bản cũ hơn.

**Tích hợp (`LunaDns.Platform.Windows.Tests`, chạy tay, quyền Admin, máy ảo Windows 10 và 11):**
- NRPT: thêm, kiểm tra `Resolve-DnsName` đi qua stub, gỡ, rồi xác nhận không còn quy tắc nào.
- File hosts: có comment tiếng Việt UTF-8 và thuộc tính ReadOnly. Sau một vòng áp dụng rồi hoàn tác, file phải giống hệt byte ban đầu.
- AutoConfigURL: có sẵn PAC của công ty. Sau áp dụng rồi hoàn tác, giá trị phải trở về đúng như cũ.
- Task dọn dẹp: bật tính năng, kill tiến trình, khởi động lại máy, rồi kiểm tra đã sạch.
- ACL: đặt sẵn thư mục bằng user thường, rồi kiểm tra app từ chối dùng.

**Lệnh kiểm tra:** `dotnet build -warnaserror` · `dotnet test tests/LunaDns.Core.Tests`.

## 10. Rủi ro cần spike trước khi làm thật

1. **NRPT trên Windows 10/11 Home:** xác nhận quy tắc local (không qua GPO) có hiệu lực, và nameserver `127.0.0.1` được chấp nhận. Nếu không được thì phương án B là chạy stub trên `127.0.0.1:53` và chỉ đặt DNS card mạng thành `127.0.0.1` khi bật tính năng. Stub khi đó chuyển các domain khác về DNS gốc của card, và DNS card được khôi phục qua Journal.
2. **Steam client có đọc PAC của WinINet không**, ở cả phần CEF lẫn kết nối CM. Nếu phần CM không đọc thì kết nối CM chỉ dựa vào định tuyến DNS, và cần ghi rõ giới hạn này.
3. **Kích thước single-file khi dùng Avalonia cùng trimming:** nếu vượt 80 MB, cân nhắc bản framework-dependent kèm trình cài .NET.
