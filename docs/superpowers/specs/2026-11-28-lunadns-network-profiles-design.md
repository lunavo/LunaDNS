# Hồ sơ theo mạng: nhớ cấu hình hợp từng mạng, áp lại khi đổi mạng (thiết kế)

Ngày: 28/11/2026. Nguồn: đề xuất sau v0.10.2 (tham khảo zapret, ByeDPI, RethinkDNS); `2026-11-21-lunadns-auto-tune-design.md`
để "tự dò lại khi đổi mạng" ở Ngoài phạm vi.

## Vấn đề

Tìm cấu hình hợp mạng (D3) cho ra một tổ hợp DoH + kiểu cắt + độ trễ hợp **một** mạng. Laptop chuyển giữa Wi‑Fi nhà,
4G phát từ điện thoại, mạng công ty thì tổ hợp đó có thể sai, và người dùng phải tự nhớ bấm dò lại. App cũng không biết
mạng đã đổi.

## Quyết định PO (28/11)

1. Tách hai spec: spec này chỉ làm **hồ sơ theo mạng** (dữ liệu trên máy). Preset theo nhà mạng (cần tra ASN qua dịch vụ
   ngoài và nguồn dữ liệu cộng đồng) để spec sau.
2. Nhận ra mạng bằng **MAC của gateway mặc định**, không đọc SSID (tránh xin quyền Vị trí trên macOS 14+ / Windows 11 24H2).
3. Đổi mạng: mạng **đã có hồ sơ** thì tự áp, không hỏi; mạng **mới** thì chạy Kiểm tra ngầm, bị chặn thì hiện banner có
   nút Tìm cấu hình hợp mạng. App không tự dò.
4. Chỉ lưu hồ sơ khi cấu hình **được chứng minh chạy**: sau Tìm cấu hình hợp mạng thành công, hoặc sau Kiểm tra đạt với
   tổ hợp đang dùng. Đổi tay trong Nâng cao không tự lưu.
5. Cách làm A: **app** theo dõi đổi mạng (không phải daemon/tác vụ nền), dùng lại `IFeatureController.GetTuningAsync` /
   `ApplyTuningAsync` có sẵn trên cả hai nền tảng; giao thức daemon không đổi.

## Thiết kế

### Đơn vị

| Đơn vị | Nơi | Việc |
|---|---|---|
| `NetworkId` | `Core/Net` | Định danh mạng: SHA‑256 của (MAC gateway + loại interface), lưu dạng hex. Không có MAC (PPP, một số VPN) thì băm (IP gateway + tên adapter) và đặt `Weak = true`. Không có gateway thì không có `NetworkId`. |
| `INetworkIdentity` | `Core/Platform` | `NetworkSnapshot? Current()`: `NetworkId` + nhãn gợi ý ("Wi‑Fi · 192.168.1.1"). |
| `WindowsNetworkIdentity` | `Platform.Windows` | Gateway từ `NetworkInterface.GetIPProperties().GatewayAddresses` (interface Up, có gateway IPv4, metric thấp nhất); MAC qua `SendARP` (iphlpapi, P/Invoke). |
| `MacNetworkIdentity` | `Platform.MacOS` | Gateway và interface từ `route -n get default`; MAC từ `arp -n <gateway>`. Chạy với quyền người dùng. Phần phân tích chữ tách thành hàm thuần để test. |
| `NetworkProfile` | `Core/Settings` | `{ Id, Weak, Label, Doh, Candidate, SavedAt, LastSeen, Stale }`. |
| `NetworkProfileStore` | `Core/Settings` | Đọc/ghi `profiles.json` cạnh `settings.json` (`AppPaths.DataDirectory`). Ghi file tạm rồi đổi tên. File hỏng: đổi tên thành `profiles.json.bad`, chạy tiếp với danh sách rỗng. Tối đa 32 hồ sơ, đầy thì bỏ hồ sơ có `LastSeen` cũ nhất. |
| `NetworkWatcher` | `App` | Gộp `NetworkChange.NetworkAddressChanged` và poll dự phòng 60 giây thành sự kiện `Changed(NetworkSnapshot?)`: chờ yên 3 giây, chỉ phát khi `NetworkId` khác lần trước. Đồng hồ và timer qua interface để test. |
| `NetworkProfileCoordinator` | `App` | Quyết định hành động (mục dưới). Phụ thuộc `INetworkIdentity`, `NetworkProfileStore`, `IFeatureController`, `ProbeRunner` (qua interface) và cờ "đang bận" của view model. Không chạm Dispatcher. |
| `NetworkProfileNotice` | `App` | Dòng trạng thái và banner cho view model, theo mẫu `ResumeNotice`. |

Thêm `AppSettings.RememberPerNetwork` (mặc định `true`). Tắt thì giữ hồ sơ, không áp, không lưu.

### "Đạt" khi lưu hồ sơ

Kiểm tra (nhóm Steam) **đạt** khi mọi host là `Ok`, hoặc là `SniFiltered` trong lúc Proxy SNI đang bật (TLS cắt gói với
tổ hợp đang dùng thành công). Mọi kết quả khác là **bị chặn**. Kế hoạch triển khai phải kiểm `TlsProbe` dùng đúng tổ
hợp hiện tại khi thử cắt gói; nếu không thì truyền tổ hợp vào.

### Luồng khi đổi mạng

Chỉ chạy khi `RememberPerNetwork` bật và có ít nhất một tính năng (Định tuyến DNS hoặc Proxy SNI) đang bật.

1. **Không có gateway** (mất mạng): không làm gì, giữ cấu hình hiện tại.
2. **Mạng có hồ sơ, không `Stale`:**
   - tổ hợp đã lưu khác tổ hợp hiện tại (`GetTuningAsync`) thì `ApplyTuningAsync(doh, candidate)`, hiện dòng "Đã dùng
     cấu hình đã lưu cho mạng này" trong Trạng thái;
   - trùng thì chỉ cập nhật `LastSeen`.
3. **Mạng mới, hoặc hồ sơ `Stale`:** chạy Kiểm tra ngầm nhóm Steam.
   - đạt: lưu tổ hợp hiện tại thành hồ sơ của mạng này (`Stale = false`);
   - bị chặn: hiện banner "Mạng mới có vẻ chặn Steam" với nút Tìm cấu hình hợp mạng.

Lưu hồ sơ ở các chỗ khác:

- Tìm cấu hình hợp mạng thành công: lưu tổ hợp vừa áp cho mạng hiện tại.
- Người dùng bấm Kiểm tra và đạt: lưu (hoặc ghi đè) cho mạng hiện tại.
- Người dùng bấm Kiểm tra trên mạng có hồ sơ mà bị chặn: đặt `Stale = true`, hiện banner như mạng mới. Hồ sơ không bị
  xoá; Tìm cấu hình thành công sẽ ghi đè.

### Tranh chấp và lỗi

- Đang có Kiểm tra, Tìm cấu hình, hoặc đang bật/tắt tính năng: coordinator bỏ lượt; khi việc đó xong thì so lại
  `NetworkId` và chạy nếu đã khác lần xử lý cuối.
- Đổi mạng lần nữa giữa lúc coordinator đang chạy: huỷ lượt cũ (`CancellationToken`), chạy lượt mới. Lượt bị huỷ không
  lưu gì.
- `ApplyTuningAsync` lỗi (vd. macOS `DaemonNeedsUpdate`): hiện lỗi qua đường báo lỗi hiện có, giữ hồ sơ.
- Không đọc được MAC lẫn gateway: coi như không có `NetworkId`, không làm gì.
- Log: 8 ký tự đầu của `NetworkId`, hành động, kết quả. Không ghi MAC hay IP gateway vào log.

## Chữ trên giao diện

| Chỗ | Chữ |
|---|---|
| Trạng thái, có hồ sơ | "Mạng: {nhãn} — có cấu hình đã lưu ({ngày})" + liên kết **Quên mạng này** |
| Trạng thái, chưa có hồ sơ | "Mạng: {nhãn} — chưa có cấu hình đã lưu" |
| Trạng thái, vừa áp | "Đã dùng cấu hình đã lưu cho mạng này" |
| Banner | "Mạng mới có vẻ chặn Steam." + nút **Tìm cấu hình hợp mạng** + nút đóng |
| Cài đặt | Ô **Nhớ cấu hình theo mạng** |
| Gỡ & dọn | Nút **Xoá mọi cấu hình theo mạng** (hỏi xác nhận) |

Thoát & khôi phục **không** xoá hồ sơ: hồ sơ không phải thay đổi hệ thống. Gỡ cài đặt xoá cùng thư mục dữ liệu như
hiện nay. Chuỗi mới thêm vào `Strings.resx` và bản tiếng Anh của tài liệu hướng dẫn (`docs/guide/en/03-switches.md`).

## Ngoài phạm vi

- Preset theo nhà mạng (spec riêng).
- Đọc SSID; màn hình liệt kê/đổi tên hồ sơ (`Label` để sẵn).
- Áp hồ sơ khi app không chạy (daemon/tác vụ nền).
- Tự chạy Tìm cấu hình hợp mạng trên mạng mới.
- Hồ sơ cho nhóm tên miền ngoài Steam, hoặc lưu cả trạng thái bật/tắt tính năng theo mạng.

## Kiểm thử

- `NetworkIdTests`: cùng MAC + loại cho cùng id; khác loại cho khác id; không MAC thì `Weak`; không gateway thì null.
- `NetworkProfileStoreTests`: round-trip; file hỏng thành `.bad` và danh sách rỗng; đầy 32 thì bỏ `LastSeen` cũ nhất;
  ghi đè cùng id.
- `MacNetworkIdentityTests`: phân tích mẫu chữ `route -n get default` và `arp -n` (có MAC, `incomplete`, không gateway).
- `NetworkWatcherTests` (đồng hồ giả): nhiều sự kiện trong 3 giây phát một lần; id không đổi không phát; poll bắt được
  đổi mạng khi không có sự kiện.
- `NetworkProfileCoordinatorTests` (fake identity, controller, probe):
  - mạng có hồ sơ khác tổ hợp hiện tại thì gọi `ApplyTuningAsync` đúng tham số; trùng thì không gọi;
  - mạng mới đạt thì lưu; bị chặn thì hiện banner, không lưu;
  - `SniFiltered` khi Proxy SNI bật là đạt, khi tắt là bị chặn;
  - đang bận thì bỏ lượt, xong việc thì chạy lại khi id đã khác;
  - đổi mạng giữa lượt thì lượt cũ huỷ, không lưu;
  - `DaemonNeedsUpdate` thì báo lỗi, giữ hồ sơ;
  - `RememberPerNetwork = false` hoặc không tính năng nào bật thì không làm gì;
  - Kiểm tra tay bị chặn trên mạng có hồ sơ thì `Stale`, lần đổi mạng sau chạy Kiểm tra thay vì áp.
- `StringsTests`: có chuỗi mới.
- Thử tay trên Windows và macOS: Wi‑Fi nhà ↔ 4G phát từ điện thoại; hồ sơ được lưu sau Tìm cấu hình, được áp khi quay
  lại Wi‑Fi nhà, banner hiện trên mạng chặn; Quên mạng này và Xoá mọi cấu hình theo mạng chạy đúng.
