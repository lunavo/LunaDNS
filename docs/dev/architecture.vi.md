# Kiến trúc

<sub>[English](architecture.md)</sub>

LunaDNS là app desktop .NET 8 / Avalonia 11. Phần lớn logic nằm trong một lõi không phụ thuộc hệ điều hành; mỗi hệ điều hành cắm vào một `INetworkConfigurator` biết cách đổi định tuyến DNS, proxy và file hosts của hệ đó.

## Các project

| Project | Đích | Vai trò |
|---|---|---|
| `src/LunaDns.Core` | `net8.0` | Kiểm tra (probe), DoH client và DNS stub, proxy cắt SNI, journal thay đổi, tính năng, settings, log, chẩn đoán. Không có mã riêng của hệ điều hành. |
| `src/LunaDns.Platform.Windows` | `net8.0-windows` | Luật NRPT (CIM), file hosts, `AutoConfigURL` của WinINet, ACL thư mục dữ liệu, tác vụ `\LunaDNS\Cleanup`. |
| `src/LunaDns.Platform.MacOS` | `net8.0` | File `/etc/resolver/<domain>`, auto-proxy qua `networksetup`, `/etc/hosts`, giao thức socket của dịch vụ nền và kiểm UID kết nối. |
| `src/LunaDns.MacCli` | `net8.0` | Lệnh `lunadns` và launchd daemon (`vn.lunavo.lunadns`). |
| `src/LunaDns.App` | `net8.0-windows` và `net8.0` | Giao diện Avalonia. Trên Windows chạy tính năng ngay trong tiến trình (đã nâng quyền). Trên macOS chạy quyền user và điều khiển dịch vụ nền. |
| `tests/*` | | xUnit. Mỗi project nguồn một project test. |

`Directory.Build.props` đặt cho mọi project: `net8.0`, C# 12, nullable, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`.

## Khái niệm chính

### Kiểm tra trước, can thiệp sau

`Probing/Prober` thử các host mẫu của Steam bằng đường mạng **gốc** (không qua LunaDNS) rồi phân loại từng host (`ProbeVerdict`): bình thường, DNS bị đầu độc, bị lọc SNI, không vào được. `ProbeAdvisor` đổi kết luận thành biện pháp (`ProbeRemedies`): Định tuyến DNS, hoặc Định tuyến DNS kèm Proxy SNI. Nút "Áp dụng đề xuất" chỉ bật những biện pháp còn thiếu.

### Tính năng

`Features/FeatureSet` giữ các công tắc (`Feature`): Định tuyến DNS và Proxy SNI. Giá trị thứ ba, PUBG, vẫn còn trong enum để hoàn tác được mục journal cũ, nhưng không hiện trên giao diện.

- `EnableAsync` lập kế hoạch thay đổi, **ghi vào journal**, áp dụng, rồi mới dựng dịch vụ (DNS stub hoặc proxy).
- `DisableAsync` dừng dịch vụ và hoàn tác các mục journal của nó. Hoàn tác lỗi thì dịch vụ vẫn chạy để mạng không hỏng, mục được giữ trong journal.
- `DisableAllAsync` là nút "Tắt hết & khôi phục".
- Watchdog `RestartPolicy` mở lại dịch vụ chết ở đúng cổng cũ; lỗi 3 lần trong 1 phút thì tính năng tự tắt.

### Journal thay đổi

`Journal/ChangeJournal` là lưới an toàn. Mọi thay đổi hệ thống được ghi vào `journal.json` **trước khi** làm, gồm tên op và ảnh chụp trạng thái cũ (`Platform/*Snapshot`). Hoàn tác chạy LIFO qua các `IChangeReverter`, mục lỗi giữ lại cho lần sau. Hoàn tác chạy:

- lúc mở app (mục còn sót sau khi app chết),
- khi "Thoát & khôi phục" và khi phiên đăng nhập kết thúc,
- từ `LunaDNS.exe --cleanup` và tác vụ dọn dẹp chạy bằng SYSTEM (bản Setup Windows),
- mỗi lần dịch vụ nền macOS khởi động.

Vì tiến trình có quyền cao đọc journal, thư mục dữ liệu bị khoá: `%ProgramData%\LunaDNS` chỉ SYSTEM và Administrators ghi được trên Windows; `/Library/Application Support/LunaDNS` thuộc root, quyền 700 trên macOS. Thư mục có sẵn mà người khác ghi được thì bị cách ly và tạo lại.

Tên op và tên tính năng lưu trong journal không bao giờ được đổi: mục do bản cũ ghi trên máy người dùng vẫn phải hoàn tác được.

### Giao diện nền tảng

```csharp
public interface INetworkConfigurator
{
    DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer);
    void ApplyDnsRoute(DnsRouteSnapshot snapshot);
    void RevertDnsRoute(DnsRouteSnapshot snapshot);
    AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride);
    // … ApplyAutoProxy / RevertAutoProxy, Plan/Apply/RevertHostsEntries, FlushDnsCache
}
```

`Plan*` đọc trạng thái hiện tại và trả ảnh chụp, không đổi gì. Ảnh chụp đó được ghi vào journal, và `Revert*` trả lại đúng nó. Bản cài đặt chỉ đụng thứ LunaDNS tạo: luật NRPT có Comment bắt đầu bằng `LunaDNS `, file resolver có dòng đầu `# LunaDNS`, dòng hosts gắn `# LunaDNS`.

| | Windows | macOS |
|---|---|---|
| Định tuyến DNS | Một luật NRPT → DoH stub ở `127.0.0.1:53` | Mỗi domain Steam một `/etc/resolver/<domain>` → `127.0.0.1:15353` |
| Proxy SNI | `AutoConfigURL` của người dùng đăng nhập | `networksetup -setautoproxyurl` trên từng network service có card thật đang bật |
| hosts | `%SystemRoot%\System32\drivers\etc\hosts` | `/private/etc/hosts` |

### DNS stub

`Dns/DohStubServer` trả lời DNS qua UDP/TCP trên loopback, chuyển truy vấn tên miền Steam qua DNS-over-HTTPS (`DohClient`, Cloudflare rồi Google, nối thẳng tới IP nên không bao giờ hỏi DNS hệ thống) và cache kết quả (`DnsCache`). Danh sách tên miền ở `Domains/GameDomains`.

### Proxy SNI

`Proxy/SplitProxy` là proxy HTTP CONNECT ở `127.0.0.1:18080–18090`. Proxy phục vụ `/proxy.pac` (`PacBuilder`, chỉ tên miền Steam) và chỉ nhận CONNECT tới tên miền Steam cổng 443, nên không bao giờ là proxy mở. `Tls/HelloSplitter` ghi gói TLS ClientHello thành hai đoạn TCP (mặc định: sau byte đầu, `SplitMode.FirstByte`) để bộ lọc SNI chỉ soi một đoạn bỏ sót. Không giải mã TLS, không cài chứng chỉ.

### Dịch vụ nền macOS

Trên macOS phần cần root chạy trong launchd daemon (`LunaDns.MacCli`). App và lệnh `lunadns` nói chuyện với nó qua `/var/run/lunadns.sock` (`Daemon/DaemonProtocol`). Chỉ root và người đang ngồi trước máy kết nối được; UID lấy bằng `getpeereid`. App hỏi daemon mỗi 2 giây.

### App

`LunaDns.App` dùng MVVM tự viết gọn (`ViewModels/`). `Features/IFeatureController` che khác biệt giữa tính năng chạy trong tiến trình (`LocalFeatureController`, Windows) và dịch vụ nền (`MacOS/DaemonFeatureController`). Chuỗi giao diện ở `Strings.resx` (chỉ tiếng Việt). `Updates/UpdateChecker` đọc `GET /repos/lunavo/LunaDNS/releases`, bỏ bản nháp và chỉ nhận URL tải nằm dưới `https://github.com/lunavo/LunaDNS/releases/`.

## Hồ sơ thiết kế

Spec và kế hoạch từng bước ở `docs/superpowers/specs/` và `docs/superpowers/plans/`. Kết quả thử mạng: `docs/spikes/`.
