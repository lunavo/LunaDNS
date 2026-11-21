# 1. Cài đặt

<sub>[English](en/01-install.md)</sub>

Có hai cách, đều tải ở trang Releases của LunaDNS trên GitHub, đều đã kèm sẵn .NET (không cần cài runtime):

| | Setup `LunaDNS-Setup-x.y.z.exe` | Portable `LunaDNS-x.y.z-portable.zip` |
|---|---|---|
| Nơi cài | `C:\Program Files\LunaDNS` | Thư mục bạn chọn |
| Tác vụ tự dọn lúc khởi động máy | Có | Không |
| Gỡ | Settings → Apps, hoặc nút "Gỡ khỏi máy" | Tay (xem [Gỡ và dọn](07-go-va-don-dep.md)) |

Nên dùng Setup. Exe chưa ký số, nên lần đầu Windows sẽ cảnh báo (xem dưới).

## Kiểm tra file tải về (SHA-256)

Mỗi bản phát hành có `SHA256SUMS.txt`. Trong PowerShell, ở thư mục chứa file tải về:

```powershell
Get-FileHash .\LunaDNS-Setup-0.9.0.exe -Algorithm SHA256
```

Chuỗi `Hash` phải trùng dòng của file đó trong `SHA256SUMS.txt` (không phân biệt hoa thường). Không trùng thì đừng chạy, tải lại.

## Cài bằng Setup

1. Bấm đúp `LunaDNS-Setup-x.y.z.exe`.
2. Nếu hiện **"Windows protected your PC"** (SmartScreen): bấm **More info → Run anyway**.
3. Hộp UAC ghi **"Unknown publisher"**: bấm **Yes** (do exe chưa ký số).
4. Làm theo Setup (giao diện tiếng Anh). Cuối cùng tích "Mở LunaDNS" nếu muốn mở luôn.

Setup chép app vào `C:\Program Files\LunaDNS`, khoá quyền `%ProgramData%\LunaDNS` và đăng ký tác vụ dọn dẹp `\LunaDNS\Cleanup` (chạy bằng SYSTEM lúc khởi động máy và lúc đăng nhập, xem [Gỡ và dọn](07-go-va-don-dep.md#tác-vụ-dọn-dẹp-bản-setup)). Mở app từ Start menu: **LunaDNS**.

## Dùng bản portable

1. **Trước khi giải nén:** chuột phải file zip → **Properties** → tích **Unblock** → OK. Nếu bỏ bước này, Explorer gắn dấu "tải từ Internet" cho mọi file giải nén ra và Windows sẽ cảnh báo từng lần.
2. Giải nén vào một thư mục riêng, ví dụ `C:\Tools\LunaDNS\` (giữ nguyên các file `.dll` cạnh exe).
3. Bấm đúp `LunaDNS.exe`, đồng ý UAC (xem [bước 2](02-chay-va-quyen-admin.md)).

Khác với bản Setup: **không có tác vụ tự dọn lúc khởi động máy**. Nếu app bị kill hay máy mất điện khi đang bật tính năng, phải mở lại app hoặc chạy `--cleanup` (xem [Gỡ và dọn](07-go-va-don-dep.md)). `--install-system` cũng từ chối chạy từ thư mục portable (mã 3): tác vụ chạy bằng SYSTEM mà exe nằm ở chỗ người dùng ghi được thì ai tráo exe là có quyền SYSTEM.

## Smart App Control (Windows 11)

Nếu Smart App Control đang bật, Windows 11 có thể **chặn hẳn** exe chưa ký số, không có nút "Run anyway". Kiểm tra ở Windows Security → App & browser control → Smart App Control. LunaDNS chưa ký số ở bản này. Tắt Smart App Control là quyết định của bạn; đọc kỹ cảnh báo của Windows ở trang đó trước khi tắt.

## Nâng cấp

Chạy Setup bản mới, không cần gỡ bản cũ. Trước khi ghi đè, Setup chạy `--cleanup` của bản đang cài để khôi phục cấu hình mạng bản cũ đã đổi. Nếu bước này lỗi, Setup dừng và báo; xem [Xử lý sự cố](08-su-co.md). LunaDNS phải đang tắt (Setup sẽ nhắc nếu app còn chạy).

App tự kiểm bản mới khoảng 10 giây sau khi mở; có bản mới thì hiện dải báo kèm nút **Tải** (mở trang Releases). Nút **Kiểm tra cập nhật** ở mục Nâng cao kiểm ngay lúc bấm. App không tự tải hay tự cài: tải Setup bản mới rồi chạy.

## Build từ mã nguồn

Cần **.NET 8 SDK** (`dotnet --version` ra `8.x`).

- Bấm đúp `Chay-tu-ma-nguon.cmd` ở gốc repo: build bản phát hành vào `artifacts\publish\win-x64\` rồi mở `LunaDNS.exe`. Thiếu SDK thì script in lệnh `winget` để cài.
- Hoặc chạy tay `pwsh build/publish.ps1` (sinh cả zip portable; có Inno Setup 6 thì sinh cả Setup và `SHA256SUMS.txt` trong `artifacts\`).

Kiểm tra bản build:

```powershell
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.Platform.Windows.Tests
dotnet test tests/LunaDns.App.Tests -f net8.0-windows
```

Một số test chạm mạng thật hoặc cần Admin được đánh dấu riêng và tự bỏ qua.

## Gỡ

Xem [Gỡ và dọn](07-go-va-don-dep.md).
