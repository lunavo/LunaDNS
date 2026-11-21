<p align="center"><img src="docs/assets/lunadns-mark.svg" width="96" alt="Logo LunaDNS" /></p>

# LunaDNS

<p align="center"><a href="README.md">English</a> · <b>Tiếng Việt</b></p>

LunaDNS là ứng dụng Windows và macOS giúp vào **Steam** khi nhà mạng chặn DNS hoặc lọc SNI. Giao diện tiếng Việt, chạy ở khay hệ thống / thanh menu, chỉ đụng các tên miền được định tuyến (mặc định nhóm Steam, thêm nhóm khác hoặc tên miền tự nhập nếu bạn chọn), mọi thay đổi vào hệ thống đều được ghi lại để hoàn tác.

> **Trạng thái: bản thử 0.10.x (prerelease).** Bản chưa ký số nên Windows SmartScreen và macOS Gatekeeper cảnh báo ở lần chạy đầu.

**Mục lục:** [Cho người dùng](#cho-người-dùng) · [Cách hoạt động](#cách-hoạt-động) · [Hướng dẫn](#hướng-dẫn) · [Cho lập trình viên](#cho-lập-trình-viên) · [Những gì chưa có](#những-gì-chưa-có)

## Cho người dùng

### 1. Tải

Mở [Releases](https://github.com/lunavo/LunaDNS/releases), chọn bản mới nhất, kéo xuống mục **Assets**:

| Máy của bạn | File | |
|---|---|---|
| Windows 10/11 (khuyên dùng) | `LunaDNS-Setup-x.y.z.exe` | Trình cài |
| Windows, không muốn cài | `LunaDNS-x.y.z-portable.zip` | **Không phải trình cài**: giải nén cả thư mục rồi chạy `LunaDNS.exe` |
| Mac chip Apple (M1–M4) | `LunaDNS-x.y.z-macos-arm64.dmg` | |
| Mac chip Intel | `LunaDNS-x.y.z-macos-x64.dmg` | |

**Đừng** chọn `Source code (zip)`: đó là mã nguồn cho lập trình viên, không có app bên trong. Không cần cài .NET.

### 2. Cài và chạy

- **Windows Setup:** bấm đúp. Hiện *"Windows protected your PC"* thì bấm **More info → Run anyway**; hộp UAC ghi *Unknown publisher* thì bấm **Yes**.
- **Windows portable:** chuột phải file zip → Properties → tích **Unblock**, giải nén (ví dụ vào `C:\Tools\LunaDNS`), bấm đúp `LunaDNS.exe`. Bấm đúp exe ngay trong cửa sổ zip thì không chạy được vì thiếu các file `.dll` đi kèm.
- **macOS:** kéo LunaDNS vào Applications rồi mở. macOS từ chối thì vào **System Settings → Privacy & Security → Open Anyway**. Sau đó bấm **Cài dịch vụ nền** và nhập mật khẩu.

### 3. Dùng

Mở app là app tự chạy **Kiểm tra**. Bấm **Áp dụng đề xuất**, hoặc tự bật **Định tuyến DNS** và **Proxy SNI** nếu được đề xuất. Rồi mở Steam. Vẫn không vào được thì bấm **Tìm cấu hình hợp mạng**: app tự thử các tổ hợp điểm cắt, độ trễ, máy chủ DoH và áp tổ hợp đầu tiên chạy được.

Cửa sổ chia bốn vùng: **Kiểm tra**, **Trạng thái**, **Cập nhật**, **Gỡ & dọn**. Xem [Các công tắc](docs/guide/03-ba-cong-tac.md).

Nút X chỉ thu cửa sổ xuống khay. Muốn tắt và hoàn tác hết: biểu tượng khay → **Thoát & khôi phục**. Có bản mới thì app hiện dải báo kèm nút **Tải**.

Từng bước và câu hỏi hay gặp: **[Bắt đầu nhanh](docs/guide/00-bat-dau-nhanh.md)**. Gặp lỗi: **[Xử lý sự cố](docs/guide/08-su-co.md)**.

## Cách hoạt động

| Công tắc | Việc làm | Khi nào cần |
|---|---|---|
| **Định tuyến DNS** | Chỉ các tên miền được định tuyến được hỏi qua DoH nội bộ thay cho DNS nhà mạng. Máy chủ: Cloudflare (mặc định), Google, Quad9, AdGuard hoặc URL DoH tự nhập, có hai máy dự phòng và nút **Đo** để chọn máy nhanh nhất. Windows: một luật NRPT → `127.0.0.1:53`. macOS: mỗi tên miền một file `/etc/resolver/<domain>` → `127.0.0.1:15353`. | Đăng nhập Steam lỗi, tên miền Steam phân giải sai. |
| **Proxy SNI** | Bật proxy nội bộ ở `127.0.0.1:18080` (bận thì thử đến `18090`) và trỏ PAC hệ thống tới `http://127.0.0.1:18080/proxy.pac`. Proxy cắt gói mở kết nối TLS để qua bộ lọc SNI; gặp bộ lọc biết ghép lại TCP thì bật thêm **Tách TLS record** để gửi ClientHello thành hai TLS record. Không giải mã TLS, không cài chứng chỉ. | Store, Community của Steam không tải được dù DNS đã đúng. |

DNS của card mạng, mạng LAN, các trang khác và UDP của game giữ nguyên. Proxy từ chối host ngoài danh sách định tuyến nên không bao giờ là proxy mở.

**Danh sách tên miền ba lớp:** lớp có sẵn (luôn có), lớp cập nhật có ký số app tải từ GitHub mỗi 24 giờ (chỉ thêm tên miền, lỗi thì giữ bản cũ, tắt được), và tên miền tuỳ chỉnh của bạn, không bao giờ bị bản cập nhật ghi đè. Xem [Các công tắc](docs/guide/03-ba-cong-tac.md). App bị kill hay mất điện thì lần mở sau, `LunaDNS.exe --cleanup`, tác vụ dọn dẹp Windows (bản Setup) hoặc dịch vụ nền macOS sẽ hoàn tác phần còn sót.

**Yêu cầu:** Windows 10 1809+ / Windows 11 64-bit, có quyền Administrator (hỏi qua UAC); hoặc macOS 12+ chip Apple hay Intel, nhập mật khẩu admin một lần.

## Hướng dẫn

| | Tiếng Việt | English |
|---|---|---|
| 0 | [Bắt đầu nhanh](docs/guide/00-bat-dau-nhanh.md) | [Quick start](docs/guide/en/00-quick-start.md) |
| 1 | [Cài đặt](docs/guide/01-cai-dat.md) | [Install](docs/guide/en/01-install.md) |
| 2 | [Quyền Admin, khay hệ thống](docs/guide/02-chay-va-quyen-admin.md) | [Running as admin](docs/guide/en/02-running-as-admin.md) |
| 3 | [Các công tắc](docs/guide/03-ba-cong-tac.md) | [The switches](docs/guide/en/03-switches.md) |
| 4 | [Kiểm tra và Áp dụng đề xuất](docs/guide/04-kiem-tra-va-de-xuat.md) | [Check and suggestions](docs/guide/en/04-check-and-suggestions.md) |
| 5 | [Mục Nâng cao](docs/guide/05-nang-cao.md) | [Advanced](docs/guide/en/05-advanced.md) |
| 6 | [Dòng lệnh](docs/guide/06-dong-lenh.md) | [Command line](docs/guide/en/06-command-line.md) |
| 7 | [Gỡ và dọn khi app chết](docs/guide/07-go-va-don-dep.md) | [Uninstall and cleanup](docs/guide/en/07-uninstall-and-cleanup.md) |
| 8 | [Xử lý sự cố](docs/guide/08-su-co.md) | [Troubleshooting](docs/guide/en/08-troubleshooting.md) |
| 9 | [macOS](docs/guide/09-macos.md) | [macOS](docs/guide/en/09-macos.md) |

### Các lệnh (Windows, mở bằng Admin)

| Lệnh | Tác dụng |
|---|---|
| `LunaDNS.exe` | Mở giao diện. Chỉ một bản chạy cùng lúc; mở lần hai thì cửa sổ cũ hiện lên. |
| `--cleanup` | Hoàn tác mọi thay đổi còn trong journal rồi thoát. Dùng khi app chết giữa chừng. |
| `--report` | Ghi báo cáo chẩn đoán (đã che thông tin cá nhân) ra Desktop rồi thoát. |
| `--install-system` | Setup gọi sau khi cài. Chỉ chạy khi exe nằm trong Program Files. |
| `--uninstall` | Trình gỡ gọi; bản portable thì chạy tay trước khi xoá thư mục. |

Mã thoát: `0` xong · `1` còn việc chưa xong, xem log · `2` tham số sai · `3` `--install-system` ngoài Program Files · `4` `--uninstall` khi LunaDNS đang chạy. macOS có lệnh `lunadns on|off|status`: xem [macOS](docs/guide/09-macos.md).

### Dữ liệu và log

| | Windows | macOS |
|---|---|---|
| Thư mục | `%ProgramData%\LunaDNS` | `/Library/Application Support/LunaDNS` |
| Journal | `journal.json` | như trên |
| Log | `logs\lunadns.log` (1 MB × 5) | `logs/lunadns.log`, `logs/daemon.out` |
| Bảo vệ | chỉ SYSTEM và Administrators ghi được | root, quyền `700` |

## Cho lập trình viên

```powershell
git clone https://github.com/lunavo/LunaDNS.git lunadns
cd lunadns
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
```

Hoặc bấm đúp `Chay-tu-ma-nguon.cmd` (Windows) để build và mở app. Cần .NET 8 SDK.

| | Tiếng Việt | English |
|---|---|---|
| Các project, journal, tính năng, lớp nền tảng | [Kiến trúc](docs/dev/architecture.vi.md) | [Architecture](docs/dev/architecture.md) |
| SDK, đóng gói, test, test Admin, CI | [Build và test](docs/dev/building-and-testing.vi.md) | [Building and testing](docs/dev/building-and-testing.md) |
| Tag, bản nháp, phát hành, ký số | [Phát hành](docs/dev/releasing.vi.md) | [Releasing](docs/dev/releasing.md) |
| Báo lỗi và pull request | [Đóng góp](CONTRIBUTING.vi.md) | [Contributing](CONTRIBUTING.md) |

Spec và kế hoạch thiết kế: `docs/superpowers/`.

## Những gì chưa có

- **Ký số:** exe Windows chưa ký (Smart App Control trên Windows 11 có thể chặn hẳn); bản macOS ký ad-hoc, chưa notarize.
- **Tự cập nhật:** app báo có bản mới và mở trang tải; chưa tự tải hay tự cài.
- **PUBG:** chưa hỗ trợ.
- **Báo cáo chẩn đoán** hiện ghi ra Desktop.
- **macOS:** chưa có mục Nâng cao (điểm cắt, độ trễ, máy chủ DoH dùng mặc định; nút **Tìm cấu hình hợp mạng** thì đã có). Chưa kiểm chứng mọi kết nối Steam trên macOS có theo PAC hệ thống.
- Đăng xuất/tắt máy: chưa đo xong thời gian hoàn tác. Không kịp thì tác vụ dọn dẹp (bản Setup) hoặc dịch vụ nền (macOS), lần mở app sau, hoặc `--cleanup` sẽ hoàn tác.

## Giấy phép

[MIT](LICENSE). Ghi công bên thứ ba: [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md).
