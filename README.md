<p align="center"><img src="docs/assets/lunadns-mark.svg" width="96" alt="Logo LunaDNS" /></p>

# LunaDNS

LunaDNS là ứng dụng Windows giúp vào **Steam** khi nhà mạng chặn DNS hoặc lọc SNI. Giao diện tiếng Việt, chạy ở khay hệ thống, mọi thay đổi vào hệ thống đều được ghi lại để hoàn tác.

> **Không phải dev?** Tải `LunaDNS-Setup-x.y.z.exe` ở [Releases](../../releases), bấm đúp, xong. Không cần cài .NET.

> **Trạng thái: bản thử 0.9.x (prerelease).** Exe chưa ký số nên Windows có thể cảnh báo ở lần chạy đầu, xem [Cài đặt](docs/guide/01-cai-dat.md). Xem thêm [Những gì chưa có](#những-gì-chưa-có).

## Hai tính năng

| Công tắc | Việc làm | Khi nào cần |
|---|---|---|
| **Định tuyến DNS** | Thêm một luật NRPT để tên miền Steam được hỏi qua DNS mã hoá (DoH, Cloudflare hoặc Google) thay cho DNS nhà mạng. Có dịch vụ DoH nội bộ ở `127.0.0.1:53`. | Đăng nhập Steam lỗi, tên miền Steam phân giải sai (DNS bị đầu độc). |
| **Proxy SNI** | Bật proxy nội bộ ở `127.0.0.1:18080` (nếu bận thì thử đến `18090`) và trỏ Windows tới `http://127.0.0.1:18080/proxy.pac`. Proxy cắt gói mở kết nối TLS để qua bộ lọc SNI. | Store, Community của Steam không tải được dù DNS đã đúng. |

App chỉ can thiệp tên miền Steam, không đụng phần còn lại của mạng.

## Yêu cầu

- Windows 10 1809 trở lên hoặc Windows 11, 64-bit (đã thử trên Windows 10 Pro 19045).
- Quyền **Administrator**: exe tự đòi nâng quyền (UAC).
- Bản Setup và bản portable đã kèm sẵn .NET, không cần cài thêm. Chỉ khi build từ mã nguồn mới cần **.NET 8 SDK**.

## Cài đặt

- **Setup (khuyên dùng):** `LunaDNS-Setup-x.y.z.exe` cài vào `C:\Program Files\LunaDNS`, có tác vụ tự dọn lúc khởi động máy, gỡ được ở Settings → Apps.
- **Portable:** `LunaDNS-x.y.z-portable.zip`, giải nén rồi bấm đúp `LunaDNS.exe`. Không có tác vụ tự dọn.
- Mỗi bản phát hành có `SHA256SUMS.txt` để kiểm file tải về.

Chi tiết: [Cài đặt](docs/guide/01-cai-dat.md).

## Cài nhanh (từ mã nguồn)

Bấm đúp `Chay-tu-ma-nguon.cmd` ở gốc repo (cần .NET 8 SDK; thiếu thì script chỉ cách cài). Script build bản phát hành vào `artifacts\publish\win-x64\` rồi mở `LunaDNS.exe`. Hoặc chạy tay:

```powershell
git clone https://github.com/lunavo/LunaDNS.git lunadns
cd lunadns
pwsh build/publish.ps1
```

`build/publish.ps1` sinh vào `artifacts\` thư mục chạy được, zip portable, Setup (cần Inno Setup 6) và `SHA256SUMS.txt`.

## Các lệnh

| Lệnh | Tác dụng |
|---|---|
| `LunaDNS.exe` | Mở giao diện. Chỉ một bản chạy cùng lúc; mở lần hai thì cửa sổ cũ hiện lên. |
| `LunaDNS.exe --cleanup` | Hoàn tác mọi thay đổi còn trong journal rồi thoát. Dùng khi app chết giữa chừng. |
| `LunaDNS.exe --report` | Ghi báo cáo chẩn đoán (đã che thông tin cá nhân) rồi thoát. |
| `LunaDNS.exe --install-system` | Setup gọi sau khi cài: khoá quyền thư mục dữ liệu, đăng ký tác vụ dọn dẹp. Chỉ chạy khi exe nằm trong Program Files. |
| `LunaDNS.exe --uninstall` | Trình gỡ gọi (bản portable thì chạy tay): hoàn tác mọi thay đổi, xoá tác vụ dọn dẹp và thư mục dữ liệu. |

| Mã thoát | Nghĩa |
|---|---|
| `0` | Xong |
| `1` | Còn việc chưa xong, xem log |
| `2` | Tham số sai |
| `3` | `--install-system` khi exe không nằm trong Program Files (không đổi gì) |
| `4` | `--uninstall` khi LunaDNS đang chạy |

Chi tiết: [Dòng lệnh](docs/guide/06-dong-lenh.md).

## Hướng dẫn

1. [Cài đặt](docs/guide/01-cai-dat.md)
2. [Chạy bằng quyền Admin, khay hệ thống](docs/guide/02-chay-va-quyen-admin.md)
3. [Các công tắc](docs/guide/03-ba-cong-tac.md)
4. [Kiểm tra và Áp dụng đề xuất](docs/guide/04-kiem-tra-va-de-xuat.md)
5. [Mục Nâng cao](docs/guide/05-nang-cao.md)
6. [Dòng lệnh](docs/guide/06-dong-lenh.md)
7. [Gỡ và dọn khi app chết](docs/guide/07-go-va-don-dep.md)
8. [Xử lý sự cố](docs/guide/08-su-co.md)

## Dữ liệu và log

Thư mục `%ProgramData%\LunaDNS` (thường `C:\ProgramData\LunaDNS`):

- `journal.json`: các thay đổi hệ thống đang áp dụng, để hoàn tác.
- `settings.json`: lựa chọn ở mục Nâng cao.
- `logs\lunadns.log`: log xoay vòng, 1 MB mỗi file, giữ 5 file.

Mỗi lần chạy, app khoá quyền thư mục này: chỉ SYSTEM và Administrators ghi được, Users chỉ đọc. Lý do: tác vụ dọn dẹp chạy bằng SYSTEM đọc `journal.json`, nên ai ghi được journal là điều khiển được việc SYSTEM sửa hệ thống.

## Phát triển

```powershell
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.Platform.Windows.Tests
dotnet test tests/LunaDns.App.Tests
```

Mã nguồn: `src/LunaDns.Core` (logic, không phụ thuộc Windows), `src/LunaDns.Platform.Windows` (NRPT, hosts, proxy hệ thống, ACL, tác vụ dọn dẹp), `src/LunaDns.App` (giao diện Avalonia). Đóng gói: `build/` (script publish, Inno Setup, Windows Sandbox). CI: `.github/workflows/`. Kế hoạch và quyết định thiết kế: `docs/superpowers/plans/`.

## Những gì chưa có

- **Ký số exe:** chưa. Windows SmartScreen có thể cảnh báo; Smart App Control trên Windows 11 có thể chặn hẳn.
- **Tự cập nhật:** chưa. Nút "Kiểm tra cập nhật" chỉ báo có bản mới và mở trang tải; nâng cấp bằng cách chạy Setup mới.
- **Báo cáo chẩn đoán** hiện ghi ra **Desktop**. PO đã chốt sẽ chuyển sang `%ProgramData%\LunaDNS\reports\` (giữ 10 báo cáo gần nhất), nhưng chưa code.
- **PUBG chưa hỗ trợ.**
- Đăng xuất/tắt máy: chưa có kết quả đo cuối. Nếu app không kịp hoàn tác thì tác vụ dọn dẹp (bản Setup), lần mở app sau hoặc `--cleanup` sẽ hoàn tác.
