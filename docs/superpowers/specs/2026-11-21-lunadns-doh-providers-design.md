# D2: Thêm máy chủ DoH và đo độ trễ (thiết kế)

Ngày: 21/11/2026. Lộ trình: [2026-11-21-lunadns-lo-trinh-tinh-nang.md](2026-11-21-lunadns-lo-trinh-tinh-nang.md) mục D2.
Spec gốc: [2026-11-13-lunadns-design.md](2026-11-13-lunadns-design.md) mục 4.2. Lấy ý từ DNSveil.

## Vấn đề

Hiện nay chỉ có hai máy chủ DoH (Cloudflare, Google), và người dùng chỉ đổi được thứ tự của chúng (`PreferGoogleDoh`).
Có mạng chặn hoặc làm chậm cả hai, có người muốn dùng máy chủ của riêng mình, và không ai biết máy nào nhanh nhất trên
mạng đang dùng.

## Hành vi

- Ô **"Máy chủ DoH"** trong mục Nâng cao là combobox 5 mục: Cloudflare, Google, Quad9, AdGuard, Tự nhập. Mặc định vẫn
  là Cloudflare. Ai đã chọn "Google trước" thì vẫn ra Google.
- Máy được chọn đi đầu chuỗi. Theo sau là **hai** máy dựng sẵn kế tiếp theo thứ tự danh mục, bỏ máy trùng host. Chuỗi
  tối đa 3 máy, nên thời gian chờ xấu nhất vẫn là 3 × 3 giây. Ví dụ: Quad9 → Quad9, Cloudflare, Google. Tự nhập → URL
  tự nhập, Cloudflare, Google.
- Nút **"Đo"** cạnh combobox đo mọi lựa chọn dùng được trên mạng hiện tại. Sau khi đo:
  - mỗi mục hiện thêm số đo ("Quad9 — 18 ms") hoặc "lỗi";
  - app tự chọn máy nhanh nhất, lưu lại và báo "Đã chọn Quad9 (18 ms)";
  - mọi máy đều lỗi thì giữ lựa chọn cũ và báo lỗi.

  Khi đang đo, nút bị khoá và hiện "Đang đo…". Kết quả đo chỉ giữ trong bộ nhớ, không ghi `settings.json`.
- Chọn **"Tự nhập"** thì hiện thêm:
  - ô URL;
  - ô "IP (tuỳ chọn)";
  - nút "Lưu";
  - một dòng trạng thái: "Dùng 1.2.3.4, 5.6.7.8", hoặc báo lỗi.

  "Lưu" kiểm URL, lấy IP bootstrap (xem dưới) rồi ghi vào settings. Có thể chọn Tự nhập khi chưa lưu URL. Khi đó app
  dùng chuỗi Cloudflare và dòng trạng thái ghi rõ điều này.
- **Bootstrap an toàn**: app không bao giờ hỏi DNS hệ thống cho tên máy chủ DoH.
  - Có IP tay thì dùng IP tay.
  - Host là IP literal thì dùng luôn IP đó.
  - Còn lại, app hỏi tên host qua chuỗi DoH dựng sẵn (bắt đầu từ máy dựng sẵn đang chọn, hoặc Cloudflare nếu đang chọn
    Tự nhập).

  IP được lấy lúc bấm "Lưu" và đầu mỗi lần "Đo", rồi ghi vào `settings.json`. TLS vẫn kiểm chứng chỉ theo host trong
  URL, nên IP sai hay cũ chỉ gây lỗi kết nối và chuỗi rơi sang máy dự phòng; không thể bị giả mạo.
- Lựa chọn có hiệu lực ở lần bật tính năng kế tiếp, như mọi cài đặt Nâng cao hiện nay (`dohServers` được gọi lúc tạo
  `DohClient`). **Kiểm tra / Áp dụng đề xuất** dùng ngay lựa chọn hiện tại.
- Giao diện chỉ hiện trên **Windows**: trên macOS mục Nâng cao đang ẩn (`PlatformInfo.HasAdvancedSettings()`), và daemon
  đọc `settings.json` riêng của root. Core dùng chung, nên daemon dùng `Doh`/`CustomDoh` nếu file của nó có các trường
  này; file không có thì chạy như cũ theo `PreferGoogleDoh`. Giao thức daemon không đổi.

## Danh mục dựng sẵn

Hai máy thêm vào dùng biến thể **không lọc**, để không vô tình chặn tên miền game. Chỉ IPv4, như hiện nay.

| `DohProvider` | Endpoint | Bootstrap |
|---|---|---|
| `Cloudflare` | `https://cloudflare-dns.com/dns-query` | 1.1.1.1, 1.0.0.1 |
| `Google` | `https://dns.google/dns-query` | 8.8.8.8, 8.8.4.4 |
| `Quad9` | `https://dns.quad9.net/dns-query` | 9.9.9.9, 149.112.112.112 |
| `AdGuard` | `https://unfiltered.adguard-dns.com/dns-query` | 94.140.14.140, 94.140.14.141 |

Thứ tự trong bảng là thứ tự danh mục, dùng để dựng chuỗi dự phòng.

## Thành phần

Lựa chọn là một giá trị enum nhỏ, theo mẫu `HelloSplit` của D1, để D3 duyệt được bằng `settings with { Doh = x }`:

```csharp
namespace LunaDns.Core.Dns;

public enum DohProvider { Cloudflare, Google, Quad9, AdGuard, Custom }

/// <summary>URL DoH tự nhập. IP lưu dạng chuỗi cho gọn JSON; parse lúc dựng DohServer.</summary>
public sealed record CustomDoh(
    string Endpoint,
    IReadOnlyList<string> ManualAddresses,
    IReadOnlyList<string> ResolvedAddresses);
```

| Đơn vị | Thay đổi |
|---|---|
| `Dns/DohServer` | Thêm `Quad9`, `AdGuard`. `Defaults` giữ nguyên (Cloudflare, Google) cho các test và chỗ gọi hiện có. |
| `Dns/DohCatalog` (mới, tĩnh) | `BuiltIn`: 4 máy theo thứ tự bảng trên. `Get(DohProvider)` cho máy dựng sẵn. `Chain(DohServer primary)`: primary + 2 máy dựng sẵn kế tiếp, bỏ trùng host. `Choices(AppSettings)`: 4 máy dựng sẵn, cộng `Custom` nếu `CustomDoh` dựng được `DohServer` có IP. |
| `Dns/CustomDohParser` (mới, hàm thuần) | `TryParseEndpoint(string, out Uri, out string error)`: URL tuyệt đối, `https`, có host, không userinfo, không fragment, cổng nào cũng được. `TryParseAddresses(string, out IReadOnlyList<IPAddress>, out string error)`: IPv4/IPv6 ngăn bằng dấu phẩy hoặc khoảng trắng; có giá trị sai thì lỗi cả ô. `ToServer(CustomDoh)`: trả `DohServer("Tự nhập", uri, ip)` với IP = IP tay, nếu không có thì IP literal của host, nếu không có thì `ResolvedAddresses`; trả `null` nếu URL hỏng hoặc không có IP nào. |
| `Dns/CustomDohBootstrap` (mới) | `ResolveAsync(CustomDoh, IHostResolver, CancellationToken)`: có IP tay hoặc host là IP literal thì trả nguyên, không hỏi mạng. Còn lại gọi resolver (A); có kết quả thì trả `CustomDoh` với `ResolvedAddresses` mới; lỗi hoặc rỗng thì ném `DohException`, người gọi giữ bản cũ. |
| `Dns/DohLatencyMeter` (mới) | `MeasureAsync(IReadOnlyList<(DohProvider, DohServer)>, CancellationToken)`. Đo song song; mỗi máy một `DohClient` riêng chỉ chứa máy đó (không rơi sang dự phòng, kết nối mới). Mỗi máy: 1 truy vấn khởi động (gồm TCP + TLS), rồi 3 truy vấn A cho `store.steampowered.com`; điểm = trung vị 3 truy vấn sau, vì lúc dùng thật kết nối được giữ 5 phút (`PooledConnectionLifetime`). Lỗi khi: có truy vấn lỗi hoặc hết 3 giây, hoặc kết quả không phải `NoError` với ít nhất một bản ghi A. Trả `IReadOnlyList<DohLatency>` với `DohLatency(DohProvider Provider, TimeSpan? Median, string? Error)`, xếp nhanh trước, lỗi sau. Nhận factory `Func<DohServer, DohClient>` để test dùng handler giả. |
| `Settings/AppSettings` | Thêm `DohProvider? Doh` và `CustomDoh? CustomDoh` (mặc định `null`). `EffectiveDoh => Doh ?? (PreferGoogleDoh ? Google : Cloudflare)`. `DohServers()` trả `DohCatalog.Chain(primary)`; `Custom` mà `ToServer` ra `null` thì về chuỗi Cloudflare. `WithDoh(DohProvider)` đặt `Doh` và `PreferGoogleDoh = (Doh == Google)`, để bản app cũ (nếu hạ phiên bản) vẫn hiểu gần đúng. |
| `Settings/SettingsJsonContext` | Đăng ký `CustomDoh`. Enum lưu dạng chuỗi (đã bật `UseStringEnumConverter`). |
| `App/DohActions` (mới, hoặc trong `ProbeRunner`) | Ghép Core cho giao diện: `SaveCustomAsync(url, ipText)` (parse, bootstrap qua `DohClient.Create(chuỗi dựng sẵn)` + `DohHostResolver`), `MeasureAsync()` (bootstrap lại Custom nếu có, rồi đo `Choices`). |
| `App/ViewModels/MainViewModel` | `DohIndex` ánh xạ 0–4 ↔ `DohProvider` qua `WithDoh`. Thêm `IsCustomDoh`, `CustomDohUrl`, `CustomDohAddresses`, `CustomDohStatus`, `DohItems` (tên + số đo), `MeasureDohCommand`, `SaveCustomDohCommand`, `IsMeasuringDoh`. Báo đổi cùng chỗ với `DohIndex` trong `SaveSettings`. |
| `App/Views/MainWindow.axaml`, `App/Strings` | Combobox 5 mục gắn `DohItems`, nút "Đo", khối Tự nhập (`IsVisible="{Binding IsCustomDoh}"`). Chuỗi mới: `DohQuad9`, `DohAdGuard`, `DohCustom`, `DohMeasureButton`, `DohMeasuring`, `DohMeasureChosen`, `DohMeasureAllFailed`, `DohCustomUrlLabel`, `DohCustomAddressesLabel`, `DohCustomSave`, `DohCustomUsing`, `DohCustomNotSaved`, `DohCustomInvalidUrl`, `DohCustomInvalidAddresses`, `DohCustomResolveFailed`, `DohLatencyFailed`. Bỏ `DohCloudflareFirst`, `DohGoogleFirst`. |
| `docs/guide/05-nang-cao.md` | Mô tả ô Máy chủ DoH, nút Đo, Tự nhập. |

## Luồng dữ liệu

Combobox / Đo / Lưu → `MainViewModel` → `AppSettings.WithDoh` / `CustomDoh` → `settings.json` →
`AppSettings.DohServers()` → `DohCatalog.Chain` → `CoreFeatureServices.CreateDoh` → `DohClient`
(`ConnectToBootstrapAsync`, không đổi).

Đo: `DohActions.MeasureAsync` → `CustomDohBootstrap` (nếu có Custom) → `DohCatalog.Choices` → `DohLatencyMeter` →
`MainViewModel` (số đo, chọn máy nhanh nhất qua `WithDoh`).

## Lỗi

- URL sai hoặc ô IP sai: dòng trạng thái báo lỗi, không lưu gì.
- Phân giải host Tự nhập lỗi: giữ `ResolvedAddresses` cũ (nếu có), báo lỗi. Chưa có IP nào thì Tự nhập không vào
  `Choices`, và `DohServers()` dùng chuỗi Cloudflare.
- `settings.json` có `Doh: "Custom"` mà `CustomDoh` thiếu hoặc hỏng: dùng chuỗi Cloudflare, log cảnh báo một lần lúc
  dựng chuỗi. Không sửa file.
- Giá trị `Doh` lạ (file từ bản mới hơn): deserializer đang trả lỗi JSON và `JsonFileSettingsStore` giữ file hỏng, dùng
  mặc định. Hành vi này đã có từ trước; D2 không đổi.
- Đo bị huỷ (đóng cửa sổ): dừng hết, không đổi lựa chọn.
- URL tự nhập trùng host với máy dựng sẵn: `Chain` bỏ máy dựng sẵn trùng host, nên `ConnectToBootstrapAsync` (tìm máy
  theo host) không bị lẫn.

## Kiểm thử

- `DohCatalogTests`: thứ tự `BuiltIn`; `Chain` cho từng máy dựng sẵn và cho Custom; bỏ trùng host; `Choices` có/không có
  Custom.
- `AppSettingsTests`: `settings.json` cũ với `PreferGoogleDoh` true/false ra đúng máy chính và đúng chuỗi; Custom chưa có
  IP về chuỗi Cloudflare; `WithDoh(Google)` đặt `PreferGoogleDoh = true`, `WithDoh(Quad9)` đặt `false`.
- `JsonFileSettingsStoreTests`: ghi rồi đọc lại `Doh` và `CustomDoh`; file cũ không có hai trường này ra `null`.
- `CustomDohParserTests`: `http://` bị từ chối; có userinfo; có fragment; host là IP literal (IPv4, IPv6 trong `[]`);
  cổng riêng; ô IP có giá trị sai; `ToServer` theo thứ tự ưu tiên IP tay → literal → đã phân giải.
- `CustomDohBootstrapTests` (resolver giả): IP tay thì không gọi resolver; phân giải được thì ghi IP; lỗi hoặc rỗng thì
  ném `DohException`.
- `DohLatencyMeterTests` (handler giả có độ trễ cố định): đúng thứ tự; đúng trung vị (bỏ truy vấn khởi động); máy lỗi,
  hết giờ, `ServFail` hay không có bản ghi A xếp cuối với `Error`; huỷ thì dừng.
- `DohClientNetworkTests` (mạng thật): chạy qua cả 4 máy của `DohCatalog.BuiltIn`.
- `StringsTests` có chuỗi mới, không còn chuỗi đã bỏ.
- Repo chưa có test dựng `MainViewModel`; thuộc tính mới theo mẫu `DohIndex`, compiled bindings bắt sai tên lúc build,
  còn hành vi kiểm bằng thử tay.
- Thử tay trên Windows:
  - bấm Đo và xem số đo cùng máy được chọn;
  - nhập `https://dns.quad9.net/dns-query` không kèm IP, Lưu;
  - nhập URL `http://` để thấy lỗi;
  - bật Định tuyến DNS và mở Steam Store.

  macOS: chỉ kiểm hành vi không đổi.

## Ngoài phạm vi

- DoH qua HTTP/3, DoT, DNSCrypt.
- Nhiều URL tự nhập.
- Tự đo lại định kỳ.
- Tự chọn tổ hợp với điểm cắt và độ trễ (D3).
- Đưa mục Nâng cao lên macOS, sửa giao thức daemon.

## Thứ tự với các nhánh khác

D2 sửa cùng các file với D1 (`AppSettings`, `MainViewModel`, `MainWindow.axaml`, `Strings`). D1 đã merge vào `main`, nên
tách nhánh từ `main`. Merge qua PR để CI chạy cả Windows và macOS.
