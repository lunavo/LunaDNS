# Build và test

<sub>[English](building-and-testing.md)</sub>

## Cần có

- **.NET 8 SDK** (`dotnet --version` ra `8.x`).
- Windows: PowerShell 7 (`pwsh`) hoặc Windows PowerShell 5.1. **Inno Setup 6** chỉ cần khi build file Setup (`winget install --id JRSoftware.InnoSetup -e`).
- macOS: macOS 12 trở lên, Xcode command line tools (`codesign`, `hdiutil`). Cài SDK không cần root:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && bash dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
```

## Lấy mã nguồn

```bash
git clone https://github.com/lunavo/LunaDNS.git lunadns
cd lunadns
```

## Build và chạy

### Windows

Nhanh nhất: bấm đúp `Chay-tu-ma-nguon.cmd` ở gốc repo. Script chạy `build/publish.ps1 -SkipZip -SkipInstaller` rồi mở `artifacts\publish\win-x64\LunaDNS.exe` (hiện hộp UAC). Thiếu SDK thì script in lệnh `winget` để cài.

Đóng gói đầy đủ:

```powershell
pwsh build/publish.ps1
```

| Sinh ra trong `artifacts\` | Bỏ qua bằng |
|---|---|
| `publish\win-x64\`: `LunaDNS.exe` một file, self-contained, kèm các dll native | |
| `LunaDNS-<v>-portable.zip` (thư mục + `HUONG-DAN.txt`) | `-SkipZip` |
| `LunaDNS-Setup-<v>.exe` (Inno Setup, `build/installer/LunaDNS.iss`) | `-SkipInstaller` |
| `SHA256SUMS.txt` | |

`-Version x.y.z` để đặt phiên bản khác; mặc định lấy `<Version>` trong `src/LunaDns.App/LunaDns.App.csproj`.

Muốn thử bản build trên máy sạch: bật Windows Sandbox rồi bấm đúp `build/sandbox/LunaDNS.wsb`. File này gắn `artifacts\` (chỉ đọc) lên Desktop của sandbox. Repo không nằm ở `D:\Projects\lunadns` thì sửa `HostFolder`.

### macOS

```bash
build/run-macos.sh            # dựng LunaDNS.app chưa ký cho máy này rồi mở
build/run-macos.sh --build-only
build/publish-macos.sh        # .app ký ad-hoc và .dmg cho osx-arm64, osx-x64
```

`publish-macos.sh` nhận `--version x.y.z` và `--rid osx-arm64|osx-x64|all`, sinh `artifacts/LunaDNS-<v>-macos-{arm64,x64}.dmg` và `artifacts/SHA256SUMS-macos.txt`. Bố cục `.app` và bước ký ở `build/macos-bundle.sh`. Chi tiết: [hướng dẫn macOS](../guide/09-macos.md).

Chỉ dòng lệnh (không giao diện):

```bash
dotnet publish src/LunaDns.MacCli -c Release -r osx-arm64 --self-contained -o artifacts/publish/osx-arm64
sudo artifacts/publish/osx-arm64/lunadns install
```

### Icon

`build/icongen` vẽ `LunaDNS.ico`, icon khay và (với `--macos`) icon template cho thanh menu macOS từ cùng một nguồn; `build/make-icns.sh` dựng `LunaDNS.icns`. File sinh ra đã commit: chỉ chạy lại khi đổi hình.

## Test

```powershell
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.App.Tests -f net8.0-windows      # trên macOS: -f net8.0
dotnet test tests/LunaDns.Platform.Windows.Tests           # chỉ Windows
dotnet test tests/LunaDns.Platform.MacOS.Tests             # chạy được mọi nơi, dùng bản giả
```

`LunaDns.App.Tests` có hai đích: `net8.0-windows` là app Windows, `net8.0` là app macOS.

Test đụng hệ thống thật hoặc Internet mặc định bị bỏ qua:

| Biến | Bật | Ghi chú |
|---|---|---|
| `LUNADNS_NETWORK_TESTS=1` | Test Core với máy chủ DoH và host Steam thật | Cần Internet; kết quả tuỳ nhà mạng |
| `LUNADNS_ADMIN_TESTS=1` | Test Windows thêm/xoá luật NRPT, dòng hosts, cấu hình proxy thật | Chạy trong shell mở bằng Admin. Mỗi test áp dụng rồi hoàn tác, so trước/sau |
| `LUNADNS_ADMIN_TESTS=1` + `--filter Category=MacAdmin` | Test macOS trên `/etc/resolver`, `/etc/hosts`, `networksetup` thật | Chạy bằng sudo, xem dưới |

Windows (PowerShell mở bằng Admin):

```powershell
$env:LUNADNS_ADMIN_TESTS = '1'
dotnet test tests/LunaDns.Platform.Windows.Tests
```

macOS: build bằng user thường trước, rồi chạy bằng sudo với `--no-build`, nếu không `obj/`, `bin/` và `~/.dotnet` sẽ đổi chủ sang root:

```bash
dotnet build tests/LunaDns.Platform.MacOS.Tests
sudo env PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" LUNADNS_ADMIN_TESTS=1 dotnet test tests/LunaDns.Platform.MacOS.Tests --no-build --filter Category=MacAdmin
```

Lỡ quên `--no-build`: `sudo chown -R "$USER" ~/.dotnet src tests`.

## CI

`.github/workflows/ci.yml` chạy mỗi lần push lên `main` và mỗi pull request, trên `windows-latest` và `macos-latest`: `dotnet build -c Release -warnaserror` rồi các project test của hệ đó. Test Admin và test mạng tự bỏ qua ở đây. Phát hành: xem [Phát hành](releasing.vi.md).

## Quy ước mã

- Cảnh báo là lỗi; analyzer ở mức `latest-recommended`. Sửa cảnh báo, đừng tắt nếu không ghi lý do trong comment.
- Comment, log và chuỗi giao diện viết tiếng Việt; mã mới theo đúng kiểu của file đang sửa.
- Log dùng phương thức sinh mã `[LoggerMessage]`.
- Mọi thay đổi hệ thống đi qua `INetworkConfigurator` và journal: lập kế hoạch, ghi, áp dụng. Không bao giờ đổi trạng thái hệ thống mà không có mục journal hoàn tác được.
