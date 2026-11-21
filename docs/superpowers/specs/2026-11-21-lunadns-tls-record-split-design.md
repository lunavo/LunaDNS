# D1: Tách TLS record cho ClientHello (thiết kế)

Ngày: 21/11/2026. Lộ trình: [2026-11-21-lunadns-lo-trinh-tinh-nang.md](2026-11-21-lunadns-lo-trinh-tinh-nang.md) mục D1.
Lấy ý từ GreenTunnel (`--tls-records`).

## Vấn đề

Hiện nay ClientHello đi trong **một** TLS record, được cắt thành nhiều TCP segment (`HelloSplitter` + `SplitWriter`,
spec 4.3). Bộ lọc nào ghép lại TCP trước khi đọc SNI vẫn thấy trọn tên host. Tách chính record đó thành hai record
làm SNI vắt qua hai record, kể cả sau khi ghép TCP.

## Hành vi

- Mục Nâng cao có thêm công tắc **"Tách TLS record"**, mặc định **tắt**. Người đang dùng không thấy gì thay đổi.
- Ô **"Điểm cắt gói"** giữ nguyên nghĩa *cắt ở đâu*; công tắc quyết định *cắt thế nào*:

| Điểm cắt | Công tắc tắt (như hiện nay) | Công tắc bật |
|---|---|---|
| Sau byte đầu tiên | Một record, cắt TCP sau byte 1 của record (trong header) | Hai record: record 1 chứa đúng byte đầu của thông điệp handshake |
| Giữa SNI | Một record, cắt TCP giữa tên host | Hai record, tên host vắt qua hai record; không tìm thấy SNI thì như "Sau byte đầu tiên" |

- Khi bật, mỗi record đi trong một lần Write riêng (socket bật NoDelay nên mỗi record nằm trong một TCP segment riêng).
  "Độ trễ giữa hai phần" áp giữa hai record.
- Đúng chuẩn: RFC 8446 §5.1 cho phép một thông điệp handshake nằm trên nhiều record. Không giải mã TLS, không cài
  chứng chỉ.
- **Kiểm tra / Áp dụng đề xuất** dùng đúng cài đặt hiện tại, kể cả công tắc này.
- Áp dụng cho cả SplitProxy và SniForwarder. Công tắc chỉ hiện trên **Windows**: trên macOS mục Nâng cao đang ẩn
  (`PlatformInfo.HasAdvancedSettings()`), và daemon đọc `settings.json` riêng của root chứ không phải file của giao
  diện. Code Core vẫn dùng chung, nên daemon nhận `SplitRecords` nếu file của nó có trường này; đưa mục Nâng cao lên
  macOS là việc riêng. Giao thức daemon không đổi.

## Thành phần

Kiểu cắt gộp thành một giá trị để không phải truyền thêm một `bool` qua 6 chỗ, và để D3 duyệt các tổ hợp:

```csharp
namespace LunaDns.Core.Tls;
public sealed record HelloSplit(SplitMode Mode, bool Records)
{
    public static HelloSplit Default { get; } = new(SplitMode.FirstByte, Records: false);
}
```

Độ trễ vẫn tách riêng (Kiểm tra không dùng độ trễ).

| Đơn vị | Thay đổi |
|---|---|
| `Tls/TlsRecordSplitter` (mới, hàm thuần) | `Split(ReadOnlyMemory<byte> record, int cut)`: nhận một record handshake hoàn chỉnh và điểm cắt tính theo offset trong record (`5 < cut < 5 + độ dài thân`). Trả hai record; header mỗi record = content type + version chép từ record gốc + độ dài mới (big-endian). |
| `Tls/HelloSplitter` | Nhận `HelloSplit`. `Records = false`: như hiện nay. `Records = true`: điểm cắt `FirstByte` = 11 (sau header handshake 4 byte + `legacy_version` 2 byte; xem ghi chú SChannel bên dưới), `MidSni` = `sni.Offset + sni.Length / 2` (luôn > 11 vì SNI nằm sau Random); trả hai record của `TlsRecordSplitter`. |
| `Tls/SplitWriter`, `Proxy/TlsTunnel.WriteFirstRecordAsync` | Tham số `SplitMode` → `HelloSplit`. Mỗi phần một lần Write + Flush, độ trễ giữa các phần, byte thừa sau record đầu ghi nguyên: như cũ. |
| `Probing/SplittingStream` | Nhận `HelloSplit`. Lần Write đầu: chỉ cắt record đầu tiên (giới hạn bằng `ClientHelloParser.RecordLength`); byte sau record đó ghi nguyên. |
| `Probing/TlsProbe` | Tham số `SplitMode` → `HelloSplit`. |
| `Proxy/SplitProxyOptions`, `Proxy/SniForwarderOptions` | Thuộc tính `SplitMode` → `HelloSplit Split`. |
| `Settings/AppSettings` | Thêm `public bool SplitRecords { get; init; }` (mặc định `false`; `settings.json` cũ thiếu trường thì ra `false`). `Tuning` dựng `HelloSplit(SplitMode, SplitRecords)`. |
| `Features/FeatureTuning`, `Features/CoreFeatureServices`, `App/ProbeRunner` | Truyền `HelloSplit` thay cho `SplitMode`. |
| `App/ViewModels/MainViewModel` | Thuộc tính `SplitRecords` (bool) lưu qua `SaveSettings`, báo đổi cùng chỗ với `SplitModeIndex`. |
| `App/Views/MainWindow.axaml`, `App/Strings` | `CheckBox` "Tách TLS record" ngay dưới ô "Điểm cắt gói", một dòng giải thích ngắn (`SplitRecordsLabel`, `SplitRecordsHint`). |
| `docs/guide/05-nang-cao.md` | Thêm dòng cho công tắc mới. |

## Luồng dữ liệu

Công tắc → `settings.json` → `AppSettings.Tuning` (`HelloSplit` + độ trễ) → bật lại tính năng →
SplitProxy/SniForwarder → `TlsTunnel.WriteFirstRecordAsync` → `SplitWriter` → `HelloSplitter` → `TlsRecordSplitter`.
Kiểm tra: `ProbeRunner` → `TlsProbe(HelloSplit)` → `SplittingStream` → `HelloSplitter`.

## Lỗi

- Record không phải handshake hoặc khai dài quá cỡ: chuyển nguyên, không cắt (như hiện nay).
- Thân record dưới 2 byte: giữ một record. Không bao giờ sinh record rỗng.
- Máy chủ từ chối handshake đã tách: lỗi TLS hiện ở Kiểm tra như mọi lỗi TLS khác; log của proxy ghi kiểu cắt đang
  dùng. Không tự tắt công tắc (tự chọn là việc của D3).

### Ghi chú SChannel (21/11/2026)

Bản đầu cắt FirstByte ở offset 6 (record đầu có thân 1 byte). CI Windows đỏ: SChannel làm server từ chối với
"The message received was unexpected or badly formatted" và alert fatal 70 (`protocol_version`). Quét offset trên
`windows-latest` (build 26100) và `windows-2022` (20348): 6–10 bị từ chối, 11 trở lên (11, 20, 43, giữa SNI, len−1)
được nhận; gửi hai record trong một hay hai TCP segment cho cùng kết quả, nên lỗi do cấu trúc record chứ không do thời
gian. Máy chủ macOS nhận mọi offset. Kết luận: SChannel cần record đầu chứa trọn header handshake và `legacy_version`.
Vì vậy FirstByte khi tách record cắt ở 11; SNI vẫn nằm trọn ở record sau, đúng mục đích D1.

## Kiểm thử

- `TlsRecordSplitterTests` (byte cố định): độ dài và header từng record đúng; nối thân hai record ra đúng thân gốc;
  version giữ nguyên; điểm cắt ngoài khoảng ném `ArgumentOutOfRangeException`; record cỡ tối đa (16 384 byte thân)
  cho hai record đều ≤ 16 384.
- `HelloSplitterTests`: `Records = true` với FirstByte (record 1 có thân 6 byte), MidSni (tên host vắt hai record),
  không SNI (về FirstByte), thân quá ngắn cho điểm cắt (không cắt). Các test `Records = false` hiện có giữ nguyên kết quả.
- `SplitWriterTests`, `SplittingStreamTests`: đúng 2 lần Write khi bật; có độ trễ; byte thừa sau record đầu ghi nguyên.
- `TlsProbeTests` (tích hợp): máy chủ `SslStream` thật (`LoopbackTlsServer`) handshake thành công khi bật tách
  record, với cả hai điểm cắt. `SplitProxyTests`: proxy gửi lên đúng hai record (kiểm bằng upstream ghi lại byte).
- `AppSettingsTests`, `JsonFileSettingsStoreTests`: `settings.json` cũ ra `SplitRecords = false`; ghi rồi đọc lại giữ giá trị.
- `StringsTests` có chuỗi mới. Repo chưa có test dựng `MainViewModel`; thuộc tính mới theo đúng mẫu `DohIndex`,
  compiled bindings bắt sai tên lúc build, còn hành vi kiểm bằng thử tay.
- Thử tay trên Windows: bật tách record trên mạng thật, chạy Kiểm tra, mở Steam Store và Community. macOS: chỉ kiểm
  hành vi không đổi.

## Ngoài phạm vi

- Cắt thành hơn hai record.
- Tự chọn kiểu cắt (D3).
- Sửa giao thức daemon macOS.

## Thứ tự với các nhánh khác

Code D1 sửa cùng các file với `feat/custom-domains` (`AppSettings`, `MainViewModel`, `MainWindow.axaml`, `Strings`):
làm sau khi nhánh đó đã merge, hoặc tách nhánh từ nó.
