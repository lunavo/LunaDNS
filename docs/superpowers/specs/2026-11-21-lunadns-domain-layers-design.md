# D4: Cập nhật danh sách tên miền theo lớp (thiết kế)

Ngày: 21/11/2026 · Trạng thái: PO duyệt 21/11/2026. Kế hoạch: [2026-11-21-lunadns-12-domain-layers.md](../plans/2026-11-21-lunadns-12-domain-layers.md).
Lộ trình: [2026-11-21-lunadns-lo-trinh-tinh-nang.md](2026-11-21-lunadns-lo-trinh-tinh-nang.md) mục D4. Nối tiếp
[nhóm tên miền và Tuỳ chỉnh](2026-11-21-lunadns-custom-domains-design.md) (lớp 3) và [spec v1](2026-11-13-lunadns-design.md)
mục 4.5 (Journal), 4.6 (thư mục dữ liệu và ACL), 4.10 (cập nhật có chữ ký). Lấy ý từ Accesser `rules.toml` và Flowseal
*Update IPSet List*.

## Vấn đề

Nhóm dựng sẵn (Steam, Reddit…) nằm cứng trong `DomainCatalog`. Steam thêm tên miền CDN mới thì người dùng phải chờ bản
app mới, hoặc tự thêm vào Tuỳ chỉnh. Bản này cho phép bổ sung tên miền cho nhóm dựng sẵn mà không cần phát hành app, và
vẫn giữ các cam kết hiện có: không có đường leo thang quyền, dữ liệu từ xa phải có chữ ký, lớp của người dùng không bao
giờ bị đụng tới.

| Lớp | Nguồn | Ai sửa | Luôn có |
|---|---|---|---|
| 1 | `DomainCatalog`, đóng kèm app | Bản phát hành | Có |
| 2 | `domains.json` trên GitHub Releases, ký ECDSA | PO ký trên máy mình | Không (tắt được, tải lỗi thì giữ bản cũ) |
| 3 | `DomainSelection.Custom` | Người dùng | Có, không bao giờ bị ghi đè |

## Quyết định đã chốt (PO, 21/11/2026)

| # | Quyết định |
|---|---|
| L1 | Lớp 2 **chỉ thêm** tên miền vào nhóm dựng sẵn đã có. Không xoá tên miền của lớp 1, không tạo nhóm mới. |
| L2 | Ký bằng **ECDSA P-256 / SHA-256** có sẵn trong .NET 8, không thêm thư viện. Khoá công khai nhúng trong app; khoá bí mật chỉ nằm trên máy PO; CI không giữ khoá. |
| L3 | **Một file JSON bọc chữ ký** (envelope chứa payload base64 và chữ ký). `serial` tăng dần, chống hạ cấp; giới hạn 64 KB. File nằm ở release cố định tag `domain-list`. |
| L4 | **App tải.** Windows: app (Admin) tự xác minh và ghi. macOS: app gửi nguyên byte cho daemon, daemon tự xác minh lại rồi mới ghi. Daemon không có code mạng. |
| L5 | Tải khi **mở app + 30 giây**, và **24 giờ một lần** khi app vẫn chạy, có nút **Cập nhật ngay**. "Mạng đã thông" nghĩa là HTTPS tới GitHub thành công và chữ ký đúng. |
| L6 | Hộp [Xem] của nhóm dựng sẵn ghi **nguồn của từng dòng** (Có sẵn / Bản cập nhật). |
| L7 | Công tắc nằm ở **khu Tên miền**, hiện cả trên Windows và macOS. Mặc định bật. Tắt nghĩa là ngừng tải **và** bỏ lớp 2 khỏi định tuyến. |

## Hành vi

### Khi nào tải
- `DomainListUpdater` chạy sau khi app mở 30 giây. Nó tải nếu công tắc đang bật **và** lần kiểm thành công gần nhất
  (`checkedAt`) đã quá 24 giờ, hoặc chưa kiểm lần nào. Xong thì hẹn lượt tiếp theo vào `checkedAt + 24h`.
- Lỗi mạng, timeout, HTTP khác 200, hoặc vượt 64 KB: giữ bản cũ, không hiện hộp thoại, log một dòng, thử lại sau
  **1 giờ**. Captive portal hay mạng chưa lên đều rơi vào nhánh này.
- **Cập nhật ngay** tải liền, bỏ qua mốc 24 giờ. Nút bị khoá khi đang tải, khi công tắc tắt, và khi có thao tác áp
  dụng khác đang chạy (bật/tắt tính năng, Lưu Tuỳ chỉnh, Tìm cấu hình).
- URL: `https://github.com/<LunaDnsRepository>/releases/download/domain-list/domains.json`, lấy repo từ thuộc tính
  `LunaDnsRepository` có sẵn trong `LunaDns.App.csproj` (mặc định `lunavo/LunaDNS`), giống `UpdateChecker`. HttpClient tự đi theo
  chuyển hướng tới `*.githubusercontent.com`, và không bao giờ hạ từ HTTPS xuống HTTP. Timeout 15 giây; User-Agent
  `LunaDNS/<phiên bản>`. Host tải về không quan trọng về mặt an toàn, vì niềm tin nằm ở chữ ký.
- Không có tải nền khi app đã thoát. Trên macOS, daemon cũng không tự tải.

### Gộp ba lớp
- `EffectiveCatalog.For(group)` trả lớp 1 ∪ lớp 2 của nhóm đó (đã bỏ trùng, giữ thứ tự: lớp 1 trước).
- Lớp 2 chỉ áp cho nhóm trong `DomainCatalog.Selectable`. Nhóm lạ, `Pubg`, `Custom`, hoặc nhóm chưa phát hành (như
  `OtherGames` nếu chưa nằm trong `Selectable`) thì bỏ qua. Khi bản app sau đưa nhóm đó vào `Selectable`, file đang giữ
  sẽ tự có hiệu lực. **PUBG không bao giờ nhận lớp 2**: `HostsEditor` vẫn khoá theo `DomainCatalog.All`.
- Lớp 3 giữ nguyên. Nếu một dòng Tuỳ chỉnh nay đã có ở lớp 2 thì nó **vẫn ở lại** trong Tuỳ chỉnh, chỉ thêm ghi chú
  xám "Cũng có trong nhóm Steam". `FeatureDomains.Routed` vẫn bỏ trùng như hiện nay. Thêm mới vào Tuỳ chỉnh một tên miền
  đã có trong nhóm đang bật (kể cả từ lớp 2) thì báo `Duplicate`, như lớp 3 đang làm.
- Bản app mới đưa một tên miền lớp 2 vào lớp 1 thì lúc gộp tên miền đó được tính là lớp 1. File đang giữ không đổi.

### Áp dụng bản mới
1. Xác minh (mục *Định dạng và thứ tự kiểm*).
2. Tính `Routed` mới. Nếu khác `Routed` hiện tại **và** Định tuyến DNS hoặc Proxy SNI đang bật thì gọi
   `FeatureSet.ReapplyAsync` (đã có từ lớp 3, mỗi bước có journal).
3. Chỉ ghi file trạng thái **sau khi** reapply thành công. Reapply lỗi: `ReapplyAsync` tự khôi phục danh sách cũ;
   bản mới không được ghi; log lỗi; thử lại ở lượt 1 giờ sau. Cùng một `serial` lỗi reapply hai lần liên tiếp thì
   chờ đủ 24 giờ mới thử lại, và dòng trạng thái hiện lỗi (mục *Giao diện*).
4. `Routed` không đổi (ví dụ chỉ thêm vào nhóm đang tắt): ghi file, không reapply.

### Công tắc
- `AppSettings.DomainListUpdates` (bool, mặc định `true`). File `settings.json` cũ không có trường này thì đọc ra `true`.
- **Tắt:** ngừng tải; `EffectiveCatalog` chỉ còn lớp 1; reapply nếu `Routed` đổi. File trạng thái và `highestSerial`
  vẫn giữ, để bật lại không cần tải và không bị hạ cấp.
- **Bật:** dùng lại envelope đang giữ (xác minh lại), reapply nếu cần, rồi chạy một lượt tải nếu đã quá 24 giờ.
- Không thể làm danh sách rỗng: lớp 1 của mọi nhóm `Selectable` đều có tên miền. Vì vậy không cần kiểm
  `FeatureError.NoDomains` cho công tắc này.

### Giao diện (Windows và macOS)
Thêm một dòng dưới danh sách nhóm ở khu **Tên miền**:

```
[✓] Tự cập nhật danh sách tên miền    Bản 21/11 · kiểm lúc 14:02        [Cập nhật ngay]
```

| Trạng thái | Chữ giữa dòng |
|---|---|
| Công tắc tắt | "Đang dùng danh sách có sẵn" |
| Chưa có bản nào, chưa kiểm | "Chưa tải bản nào" |
| Đang tải | "Đang kiểm…" |
| Có bản, lần kiểm gần nhất thành công | "Bản {issued:dd/MM} · kiểm lúc {checkedAt:HH:mm}" |
| Lần kiểm gần nhất lỗi | "Không kiểm được lúc {HH:mm}, giữ bản {dd/MM}" (hoặc "…, dùng danh sách có sẵn") |
| Lỗi reapply 2 lần | "Không áp được bản {dd/MM}, xem Chẩn đoán" (đỏ) |

- **Cập nhật ngay** báo kết quả bằng một dòng tạm (6 giây) ở cùng chỗ: "Đã thêm {n} tên miền", "Đã là bản mới nhất",
  "Chữ ký không hợp lệ, giữ bản cũ", "Cần bản LunaDNS mới hơn để đọc danh sách này", "Không tải được, thử lại sau".
- **Dòng nhóm** đổi số đếm thành "14 tên miền (3 từ bản cập nhật)" khi lớp 2 có thêm vào nhóm đó; không thêm thì giữ chữ
  như cũ.
- **[Xem]** đổi từ danh sách chuỗi sang danh sách có cột nguồn: "Có sẵn" hoặc "Bản cập nhật {dd/MM}".
  `IUserPrompts.ShowDomainListAsync(string, IReadOnlyList<string>)` đổi thành nhận `IReadOnlyList<CatalogEntry>`.
- **Hộp thoại Tuỳ chỉnh:** dòng trùng với nhóm dựng sẵn đang bật có ghi chú xám "Cũng có trong nhóm {tên}".
- **Chẩn đoán** (`--report`) thêm: công tắc, `serial`, `issued`, `checkedAt`, `highestSerial`, kết quả lượt tải cuối,
  và số tên miền lớp 2 theo nhóm.

### Trên macOS
- Dòng "Tự cập nhật danh sách" **có** trên macOS, vì khu Tên miền đã có trên macOS (qua `get-domains`/`set-domains`).
  Mục Nâng cao vẫn ẩn; D4 không đụng tới nó.
- App tải bằng quyền user, rồi gửi nguyên byte cho daemon (`set-domain-list`). Daemon chạy lại toàn bộ bước xác minh
  với khoá nhúng trong chính nó. Không tin bất cứ kết quả kiểm nào của app.
- Công tắc lưu trong `settings.json` của daemon (root), đổi qua `set-domain-list-enabled`.
- Số đếm, nhãn nguồn và dòng trạng thái lấy từ `get-domain-list` (kèm đủ danh sách lớp 2). App không đọc file của root.
- `checkedAt` của lượt **thành công** do daemon ghi. Lượt lỗi chỉ nằm trong bộ nhớ của app (hẹn 1 giờ). Mở lại app
  sau một lượt lỗi thì thử lại sau 30 giây, như lần đầu.
- **Daemon cũ** trả "Lệnh lạ:" cho `get-domain-list`: app ẩn dòng "Tự cập nhật danh sách", không tải, và hiện banner
  "Cần cập nhật dịch vụ nền" sẵn có. Lớp 1 và lớp 3 vẫn chạy như cũ.

## Định dạng và thứ tự kiểm

**File phát hành** `domains.json`:
```json
{ "keyId": "2026a", "payload": "<base64 của payload UTF-8>", "sig": "<base64, 64 byte IEEE P1363>" }
```
**Payload** (sau khi giải base64):
```json
{ "format": 1, "serial": 202611211402, "issued": "2026-11-21T14:02:00Z",
  "groups": { "steam": ["example-cdn.steamstatic.net"], "reddit": [] } }
```
Khoá của `groups` là tên `DomainGroup` viết thường (`steam`, `reddit`, `othergames`).

**Thứ tự kiểm** (`DomainListVerifier.Verify(bytes, keys, highestSerial)`); bước 1–3 chỉ làm việc trên byte:

| # | Kiểm | Lỗi |
|---|---|---|
| 1 | Tổng file ≤ 65 536 byte. Đọc stream có giới hạn, không tin `Content-Length` | `TooLarge` |
| 2 | Envelope là JSON hợp lệ; có `keyId`, `payload`, `sig`; `keyId` nằm trong `DomainListKeys`; `sig` giải ra đúng 64 byte | `Malformed` / `UnknownKey` |
| 3 | `ECDsa.VerifyData(payloadBytes, sig, SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)` | `BadSignature` |
| 4 | `format == 1` | `UnknownFormat` |
| 5 | `serial`, `issued`, `groups` đúng kiểu (khoá JSON trùng: bản sau ghi đè, theo System.Text.Json) | `Malformed` |
| 6 | `serial > highestSerial`. Bằng nhau thì trả `NotNewer` (lượt kiểm bình thường); nhỏ hơn thì `Downgrade` | `NotNewer` / `Downgrade` |
| 7 | Mỗi nhóm ≤ 200 tên miền, toàn file ≤ 1000 | `Malformed` |
| 8 | Từng tên miền: `DomainPolicy.Normalize(x) == x` và `DomainPolicy.Validate(x) == None`; không trùng lớp 1 | bỏ **dòng đó**, đếm vào `Dropped`, log |

- Bước 8 chỉ bỏ dòng, không bỏ cả file. Nhờ vậy app mới có danh sách cấm dài hơn vẫn dùng được file cũ, còn tên miền
  bị cấm thì không bao giờ được định tuyến.
- `issued` chỉ để hiển thị. Không có hạn dùng: lớp 2 chỉ thêm, nên bản cũ vẫn an toàn.
- **Thu hồi** một tên miền thêm nhầm: phát hành file có `serial` cao hơn mà không còn tên miền đó.
- **Đổi khoá:** thêm khoá mới vào `DomainListKeys` trong một bản app, ký bằng khoá mới từ bản sau. Lộ khoá thì ra bản
  app bỏ khoá cũ. Kẻ có khoá cũng chỉ thêm được tên miền đã qua `DomainPolicy` và danh sách cấm. Đó là rủi ro được chấp
  nhận, vì không có đường leo thang quyền.

## Lưu trữ và quyền

**File trạng thái** `domain-list.json` nằm trong thư mục dữ liệu sẵn có: Windows `%ProgramData%\LunaDNS` (ACL chỉ
SYSTEM/Administrators, `DataDirectoryAcl`, spec 4.6), macOS `/Library/Application Support/LunaDNS` (root, 700,
`MacPaths`). Không tạo thư mục mới và không đặt ACL riêng: file kế thừa quyền của thư mục. Thư mục bị cách ly
(`LunaDNS.untrusted-<ts>`) thì file trong đó cũng bị bỏ, như journal.
```json
{ "envelope": "<base64 của nguyên file domains.json đã tải>",
  "highestSerial": 202611211402, "checkedAt": "2026-11-22T07:00:00Z" }
```
- Ghi nguyên tử (file tạm rồi `File.Replace`), như journal. File này không phải thay đổi hệ thống, nên không có mục
  journal.
- Lúc nạp, **xác minh lại chữ ký** của `envelope` với `highestSerial` của chính file (bỏ bước 6). Sai hoặc hỏng thì
  chạy với lớp 1 và log cảnh báo. `highestSerial` đọc được thì vẫn giữ. File không đọc được thì `highestSerial = 0`:
  chấp nhận, vì muốn sửa file này phải có quyền quản trị.
- `NotNewer` chỉ cập nhật `checkedAt`.

## Thành phần

### Core (dùng chung)
| File | Nội dung |
|---|---|
| `Domains/DomainListKeys.cs` (mới) | `IReadOnlyDictionary<string, string>` keyId → khoá công khai SubjectPublicKeyInfo base64. Bản đầu có một khoá `2026a`. |
| `Domains/DomainListVerifier.cs` (mới) | Hàm thuần `Verify(ReadOnlySpan<byte>, keys, highestSerial)` → `DomainListVerification` (`Outcome`, `DomainListUpdate?`, `Dropped`). Khoá truyền vào để test dùng cặp khoá sinh trong test. |
| `Domains/DomainListModels.cs` (mới) | `DomainListOutcome`, `DomainListState`, `DomainListOfferResult`, và record bất biến `DomainListUpdate`: `Serial`, `Issued`, `Groups : IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>>`, `File : byte[]` (nguyên byte đã tải). |
| `Domains/EffectiveCatalog.cs` (mới) | Bất biến, dựng từ lớp 1 + `DomainListUpdate?`. `For(group)`, `Added(group)`, `Entries(group)` → `CatalogEntry(Domain, Source)`, `Issued`, `AllAdded`. `EffectiveCatalog.Builtin` = chỉ lớp 1. |
| `Domains/DomainListStore.cs` (mới) | Đọc/ghi `domain-list.json`, xác minh lại khi nạp. Đường dẫn truyền vào. |
| `Domains/DomainListService.cs` (mới) | Chỗ chạy chung cho Windows (app) và macOS (daemon): `Current`, `Enabled`, `OfferAsync(bytes)`, `SetEnabledAsync(bool)`. Gồm xác minh, gộp, quyết định reapply, rồi ghi. Có khoá tuần tự với các lệnh reapply khác. |
| `Features/FeatureDomains.cs` | `Routed(selection, catalog)`. Overload cũ gọi với `EffectiveCatalog.Builtin`. |
| `Probing/ProbeTargets.cs` | `GroupOf(host, catalog)`. Host mẫu (`ProbeTargets.Steam`…) không đổi. |
| `Settings/AppSettings.cs` | `DomainListUpdates : bool = true`. |

### Windows (app)
| File | Nội dung |
|---|---|
| `App/DomainList/DomainListUpdater.cs` (mới) | Hẹn giờ (30 giây, 24 giờ, 1 giờ), tải có giới hạn, giao byte cho `IDomainListSink`. Đồng hồ và HttpClient tiêm vào để test. |
| `App/Features/IDomainListSink.cs` (mới), `IFeatureController.cs`, `LocalFeatureController.cs` | `IFeatureController` kế thừa `IDomainListSink` (`GetDomainListAsync`, `OfferDomainListAsync`) và thêm `SetDomainListEnabledAsync`. Windows gọi `DomainListService` trong cùng tiến trình. |
| `App/AppPaths.cs` | `DomainListFile`. |
| `App/ViewModels/DomainListRowViewModel.cs` (mới) | Công tắc, dòng trạng thái, nút Cập nhật ngay, dòng kết quả tạm. Test được không cần Dispatcher. |
| `App/ViewModels/MainViewModel.cs`, `DomainGroupViewModel.cs`, `CustomDomainsViewModel.cs` | Dùng `EffectiveCatalog`; số đếm "(n từ bản cập nhật)"; ghi chú "Cũng có trong nhóm". |
| `App/ViewModels/IUserPrompts.cs`, `Views/ConfirmDialog.cs` | `ShowDomainListAsync(title, IReadOnlyList<CatalogEntry>)` có cột nguồn. |
| `App/Views/MainWindow.axaml`, `Strings.resx`, `Strings.cs` | Dòng mới và các chuỗi ở mục *Giao diện*. |
| `Core/Diagnostics/DiagnosticReport.cs`, `App/ReportWriter.cs` | Thêm khối trạng thái danh sách. |

### macOS (daemon và app)
| File | Nội dung |
|---|---|
| `Platform.MacOS/Daemon/DaemonProtocol.cs` | Lệnh `get-domain-list`, `set-domain-list` (trường `Envelope : string` base64), `set-domain-list-enabled` (trường `Enabled`). Phản hồi `DomainList : DaemonDomainList { Enabled, Serial, Issued, CheckedAt, Added }`, trong đó `Added` là đủ danh sách tên miền lớp 2 theo khoá nhóm (`"steam": [...]`), để hộp [Xem] trên macOS có từng dòng. `MaxLineBytes` từ 65 536 lên **131 072**, vì envelope 64 KB sau base64 và JSON sẽ vượt mốc cũ. |
| `Platform.MacOS/Daemon/DaemonCommands.cs` | Ba lệnh trên gọi `DomainListService`. `PeerCredentials` đã được `DaemonServer` kiểm cho mọi lệnh, nên ba lệnh mới tự có. `get-domains` và `set-domains` dùng `EffectiveCatalog` hiện tại. |
| `MacCli/MacPaths.cs`, `MacCli/Engine.cs` | `DomainListFile`; nạp `DomainListStore` lúc khởi động trước khi bật tính năng. |
| `App/MacOS/DaemonFeatureController.cs` | Ba phương thức của `IFeatureController` gửi lệnh daemon; "Lệnh lạ:" hay không trả lời thì `null`/`Unavailable`. |

### Công cụ ký
- `tools/DomainListSigner` (console .NET 8, nằm trong `.sln` để CI build với `-warnaserror`). Lệnh:
  - `keygen --out <pem>`: sinh khoá P-256, ghi PKCS#8 có mật khẩu, in khoá công khai để dán vào `DomainListKeys.cs`;
  - `verify-source <data/domain-list.json>`: kiểm cú pháp, `DomainPolicy`, giới hạn, trùng lớp 1. CI chạy lệnh này;
  - `sign --key <pem> --key-id <id> --in <source> --out <domains.json>`: đặt `serial` = giờ UTC `yyyyMMddHHmm`,
    `issued` = giờ hiện tại, ký, rồi tự chạy `DomainListVerifier` với `DomainListKeys` nhúng sẵn trước khi ghi;
  - `verify <domains.json> [--serial n]`: kiểm một file đã ký (dùng sau khi tải từ release xuống).
- `data/domain-list.json`: nguồn của lớp 2, dạng payload không có `serial`/`issued`. Sửa qua PR. Bản đầu có thể để
  nhóm rỗng.

## Luồng dữ liệu

```
data/domain-list.json ──PR, CI verify-source──▶ main
        │ (máy PO) DomainListSigner sign
        ▼
release "domain-list" / domains.json  (gh release upload --clobber)
        │ DomainListUpdater (app): mở app +30 s, 24 h/lần, Cập nhật ngay
        ▼
Windows: DomainListService (app, Admin)      macOS: set-domain-list → DomainListService (daemon, root)
        │ Verify(bytes, DomainListKeys, highestSerial)
        ▼
EffectiveCatalog mới ─▶ Routed(selection, catalog) đổi và DNS/SNI bật? ─▶ ReapplyAsync (journal)
        │ thành công
        ▼
DomainListStore.Write(domain-list.json)  ─▶ UI: get-domain-list / Current
```

## Lỗi

| Tình huống | Xử lý |
|---|---|
| Mạng lỗi, timeout, HTTP ≠ 200, `TooLarge` | Giữ bản cũ, log, thử lại sau 1 giờ. Cập nhật ngay: "Không tải được, thử lại sau". |
| `Malformed`, `UnknownKey`, `BadSignature`, `Downgrade` | Giữ bản cũ, log mức cảnh báo kèm `keyId`/`serial`. Thử lại sau 24 giờ (file trên release chưa đổi thì thử sớm cũng vô ích). Cập nhật ngay: "Chữ ký không hợp lệ, giữ bản cũ". |
| `UnknownFormat` | Giữ bản cũ, thử lại sau 24 giờ. Cập nhật ngay: "Cần bản LunaDNS mới hơn để đọc danh sách này". |
| `Dropped > 0` | Áp phần còn lại, log từng dòng bị bỏ và lý do (`DomainRejection`). |
| Reapply lỗi | `ReapplyAsync` tự khôi phục; không ghi file; thử lại sau 1 giờ; lỗi 2 lần cùng serial thì chờ 24 giờ và hiện dòng đỏ. Khôi phục cũng lỗi thì tính năng sang trạng thái lỗi như hiện nay, `--cleanup` hoàn tác được. |
| File trạng thái hỏng hoặc sai chữ ký khi nạp | Lớp 1, log; lượt tải kế tiếp ghi đè bằng bản hợp lệ. |
| macOS: daemon cũ | Ẩn dòng, banner "Cần cập nhật dịch vụ nền". |
| macOS: daemon không trả lời | Giữ trạng thái đang hiện; lượt tải hẹn lại 1 giờ. |

## Kiểm thử

- **Core** (`tests/LunaDns.Core.Tests`, cặp khoá sinh trong test):
  - `DomainListVerifier`: file đúng; mỗi mã lỗi ở bảng thứ tự kiểm; sửa một byte payload thì `BadSignature`; chữ ký
    dạng DER thay vì P1363 thì `Malformed`; `serial` bằng/nhỏ hơn; 65 537 byte; 201 tên miền một nhóm; tên miền bị cấm,
    TLD, chưa chuẩn hoá, trùng lớp 1 thì chỉ bỏ dòng; nhóm `pubg`, `custom`, nhóm lạ bị bỏ qua.
  - `EffectiveCatalog`: gộp giữ thứ tự, bỏ trùng, `Added`, `Builtin` chỉ có lớp 1, nhóm không `Selectable` không
    nhận lớp 2.
  - `DomainListStore`: ghi rồi đọc lại; file sai chữ ký thì ra lớp 1 nhưng giữ `highestSerial`; file hỏng.
  - `DomainListService` (với `FakeConfigurator`): `Routed` đổi và tính năng bật thì reapply rồi mới ghi; reapply lỗi thì
    không ghi; `Routed` không đổi thì không reapply; tắt công tắc thì bỏ lớp 2 và reapply; bật lại không hạ cấp;
    lớp 3 trùng lớp 2 vẫn còn nguyên trong `DomainSelection`.
  - `FeatureDomains.Routed(selection, catalog)`, `AppSettings` đọc file cũ không có `DomainListUpdates` ra `true`.
- **Công cụ ký** (`tests/LunaDns.Core.Tests` hoặc dự án test riêng): `sign` rồi `Verify` qua; `verify-source` bắt dòng lỗi.
- **App** (`tests/LunaDns.App.Tests`):
  - `DomainListUpdater` với đồng hồ giả và `HttpMessageHandler` giả: 30 giây đầu, mốc 24 giờ, lỗi thì 1 giờ, công tắc
    tắt thì không tải, Cập nhật ngay bỏ qua mốc, phản hồi quá 64 KB bị cắt;
  - `DomainListRowViewModel`: mỗi trạng thái ra đúng chữ; dòng kết quả tạm;
  - `MainViewModel`/`DomainGroupViewModel`: số đếm "(n từ bản cập nhật)"; [Xem] có cột nguồn;
  - `CustomDomainsViewModel`: ghi chú "Cũng có trong nhóm"; `Duplicate` khi thêm tên miền lớp 2;
  - `StringsTests`: khoá mới có giá trị.
- **macOS** (`tests/LunaDns.Platform.MacOS.Tests`): `set-domain-list` chạy lại xác minh trong daemon (byte bị sửa thì từ
  chối); `PeerCredentials`; dòng 131 072 byte đọc được, dòng dài hơn bị từ chối; `get-domain-list`; công tắc lưu vào
  `settings.json` mà không đè `Domains`/tuning.
- **Lệnh verify:** `dotnet build -c Release -warnaserror`, `dotnet test` cho Core, App (`-f net8.0` và
  `-f net8.0-windows`), Platform.MacOS. Trên Mac, 52 test chỉ dành cho Windows hỏng sẵn từ trước.
- **Thử tay:**
  - Windows 10/11 (sandbox `build/sandbox/LunaDNS.wsb`): mở app với release `domain-list` thật, xem dòng trạng thái và
    [Xem]; ngắt mạng rồi Cập nhật ngay; tắt công tắc khi DNS đang bật thì luật NRPT của lớp 2 biến mất.
  - macOS arm64: như trên, cộng chạy với daemon cũ.

## Quy trình phát hành danh sách (thêm vào `docs/dev/releasing.md` và `releasing.vi.md`)

1. Một lần: `DomainListSigner keygen`. Giữ file PEM có mật khẩu trên máy PO, kèm một bản sao lưu ngoại tuyến. Dán khoá
   công khai vào `DomainListKeys.cs` qua PR. Tạo release `gh release create domain-list --prerelease --title "Domain list"`.
   Release này không được đánh dấu *latest*. Tag không có dạng `vX.Y.Z`, nên `UpdateChecker` bỏ qua.
2. Mỗi lần cập nhật: sửa `data/domain-list.json` và mở PR (CI chạy `verify-source`), rồi merge.
3. Trên máy PO: `sign`, rồi `gh release upload domain-list artifacts/domains.json --clobber`, rồi `verify` lại file tải
   từ release xuống.

Điều kiện để tính năng có tác dụng ở người dùng: khoá công khai thật đã nằm trong bản app phát hành, và release
`domain-list` đã có `domains.json`. Trước đó app chỉ báo "Không tải được" hoặc "Chưa tải bản nào", và chạy với lớp 1.

## Ngoài phạm vi

- Lớp 2 xoá tên miền của lớp 1, tạo nhóm mới, có tên nhóm đa ngôn ngữ.
- Cập nhật Public Suffix List, danh sách cấm hay host mẫu (`ProbeTargets`) từ xa.
- Lớp 2 cho PUBG / file hosts.
- Tải nền khi app đã thoát; daemon macOS tự tải.
- Nhiều nguồn, hoặc người dùng tự nhập URL danh sách.
- Ký trong CI, ký Ed25519, hay manifest cập nhật app có chữ ký (spec v1 mục 4.10). Công cụ ký này có thể dùng lại cho
  manifest về sau, nhưng việc đó cần spec riêng.
- Đưa mục Nâng cao lên macOS.

## Thứ tự với các nhánh khác

Lớp 3 (custom-domains), D1, D2, D3 đã merge vào `main` (CI xanh ở 67be054). D4 tách nhánh từ `main`, sửa
`FeatureDomains`, `ProbeTargets`, `AppSettings`, `MainViewModel`, `MainWindow.axaml`, `Strings`, `DaemonProtocol`,
`DaemonCommands`. D5 làm sau D4 và có thể dời dòng "Tự cập nhật danh sách" khi sắp lại giao diện. Spec, plan và code
đều merge qua PR, để CI chạy cả Windows và macOS trước khi vào `main`.
