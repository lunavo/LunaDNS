# 9. macOS (thử nghiệm)

<sub>[English](en/09-macos.md)</sub>

Bản macOS gồm một **dịch vụ nền** (launchd), **lệnh** `lunadns` và **giao diện** LunaDNS (cùng giao diện Avalonia với bản Windows), dùng chung lõi với bản Windows (DoH, stub DNS, proxy SNI, journal). Có file `.dmg` cho Apple Silicon và Mac Intel, ký ad-hoc, **chưa notarize** (chưa có Apple Developer ID). Chưa có Chế độ PUBG.

## Cài từ file .dmg

Tải trên trang Release: `LunaDNS-<phiên bản>-macos-arm64.dmg` (Apple Silicon, M1 trở lên) hoặc `…-macos-x64.dmg` (Mac Intel), kèm `SHA256SUMS-macos.txt` (kiểm bằng `shasum -a 256 -c SHA256SUMS-macos.txt`). Cần macOS 12 trở lên.

1. Mở `.dmg`, kéo **LunaDNS** vào **Applications**. Đừng chạy thẳng từ cửa sổ `.dmg`: macOS sẽ chạy app ở một đường dẫn tạm chỉ đọc (App Translocation).
2. Mở LunaDNS trong Applications. Vì chưa notarize, lần đầu macOS báo không mở được. Từ macOS 15, "chuột phải → Mở" không còn bỏ qua được cảnh báo này. Có hai cách:
   - **System Settings → Privacy & Security**, kéo xuống, bấm **Open Anyway** cạnh dòng nói về LunaDNS, nhập mật khẩu, rồi mở lại app.
   - Hoặc trong Terminal: `xattr -dr com.apple.quarantine /Applications/LunaDNS.app`

   Chỉ làm vậy khi bạn tin nguồn tải.
3. Bấm **Cài dịch vụ nền** trên banner rồi nhập mật khẩu (xem mục Giao diện). Lệnh cài chỉ chép thư mục `LunaDNS.app/Contents/MacOS/daemon/` vào `/Library/PrivilegedHelperTools/vn.lunavo.lunadns/` và xoá cờ quarantine trên bản chép, để launchd chạy được dịch vụ nền.

Gỡ: `sudo /Library/PrivilegedHelperTools/vn.lunavo.lunadns/lunadns uninstall` (hoặc `sudo lunadns uninstall` nếu máy có `/usr/local/bin`), rồi kéo LunaDNS từ Applications vào Thùng rác.

## Cách hoạt động

| Tính năng | Windows | macOS |
|---|---|---|
| Định tuyến DNS | luật NRPT → `127.0.0.1:53` | mỗi domain Steam một file `/etc/resolver/<domain>` → `127.0.0.1:15353` (chỉ thị `port`) |
| Proxy SNI | `AutoConfigURL` của người dùng | `networksetup -setautoproxyurl` trên từng network service đang bật **có card thật** (Wi-Fi, Ethernet...). Service của VPN không bị đụng |
| Dòng hosts (PUBG) | file hosts | `/etc/hosts` (ghi vào `/private/etc/hosts`, giữ symlink và quyền 644) |
| Xả cache DNS | `DnsFlushResolverCache` | `dscacheutil -flushcache` + `killall -HUP mDNSResponder` |
| Dữ liệu, journal, log | `%ProgramData%\LunaDNS` | `/Library/Application Support/LunaDNS` (root, quyền 700) |

Mỗi file `/etc/resolver` của LunaDNS có dòng đầu `# LunaDNS <mã>`. LunaDNS **không bao giờ sửa hay xoá file không có dấu này**: domain đã có file của VPN hoặc script tự tạo thì bị bỏ qua.

## Chạy từ mã nguồn

Cần .NET 8 SDK. Cài không cần quyền root vào `~/.dotnet`:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && bash dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet
```

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
```

Build bản chạy được:

```bash
dotnet publish src/LunaDns.MacCli -c Release -r osx-arm64 --self-contained -o artifacts/publish/osx-arm64
```

(Mac Intel thì đổi `osx-arm64` thành `osx-x64`.)

## Giao diện

```bash
build/run-macos.sh
```

Script dựng `artifacts/run/osx-arm64/LunaDNS.app` (chưa ký, chỉ để chạy trên máy build) rồi mở nó. `--build-only` để chỉ dựng. Bên trong `.app`, lệnh `lunadns` nằm ở `Contents/MacOS/daemon/`.

Biểu tượng trên thanh menu là icon **template** đen trong suốt (macOS tự tô theo thanh menu sáng/tối). Ba trạng thái khác nhau bằng hình: **mờ** = tắt, **đặc** = đang bật, **có chấm tròn** = cần chú ý.

Khác bản Windows:

- **Giao diện chạy quyền user, không tự đổi cấu hình hệ thống.** Mọi bật/tắt và trạng thái đều đi qua dịch vụ nền (socket `/var/run/lunadns.sock`). Kiểm tra (probe) vẫn chạy trong giao diện.
- **Dịch vụ nền chưa cài:** giao diện hiện banner kèm nút **Cài dịch vụ nền**. Nút này mở hộp thoại mật khẩu của macOS (`osascript … with administrator privileges`) rồi chạy `lunadns install` đi kèm `.app`. Đã cài mà không trả lời thì nút thành **Cài lại dịch vụ nền**. Hai công tắc xám tới khi dịch vụ nền trả lời.
- Giao diện hỏi lại dịch vụ nền mỗi 2 giây, nên bật/tắt bằng `lunadns on|off` trong Terminal cũng hiện lên giao diện.
- **Thoát & khôi phục** (menu bar, hoặc Cmd+Q, hoặc đăng xuất) gửi lệnh tắt hết cho dịch vụ nền, giống nghĩa nút trên Windows. Kể cả khi tính năng được bật bằng `lunadns on`. Muốn để LunaDNS tiếp tục chạy thì chỉ đóng cửa sổ (nút đỏ): app vẫn ở biểu tượng trên thanh menu.
- Chưa có mục **Nâng cao** (kiểu cắt gói, độ trễ, DoH): dịch vụ nền đang dùng cài đặt mặc định.
- Không có nút **Gỡ khỏi máy**: gỡ bằng `sudo lunadns uninstall`.
- Settings và log của giao diện ở `~/Library/Application Support/LunaDNS` (kèm `gui.lock`, `gui.sock` để chỉ một bản giao diện chạy; mở lần hai thì cửa sổ đang chạy hiện lên).
- **Chẩn đoán** ghi báo cáo ra Desktop: file `/etc/resolver` của LunaDNS, service đang dùng PAC LunaDNS, dòng hosts, phiên bản dịch vụ nền và số mục journal (journal của dịch vụ nền thuộc root, giao diện không đọc được nội dung).

## Dịch vụ nền (khuyên dùng)

Trên Windows, app chạy bằng quyền Admin sau UAC. Trên macOS, phần cần root tách thành **dịch vụ nền** (launchd daemon `vn.lunavo.lunadns`); lệnh `lunadns` của người dùng nói chuyện với nó qua Unix socket `/var/run/lunadns.sock`, nên sau khi cài **không cần sudo** để bật/tắt.

```bash
sudo artifacts/publish/osx-arm64/lunadns install
```

Lệnh này chép bản chạy được vào `/Library/PrivilegedHelperTools/vn.lunavo.lunadns/` (chỉ root ghi được), tạo `/Library/LaunchDaemons/vn.lunavo.lunadns.plist`, nạp daemon và tạo lệnh tắt `/usr/local/bin/lunadns` (nếu thư mục đó có sẵn).

- Mỗi lần daemon khởi động (kể cả sau khi khởi động lại máy hoặc daemon chết) nó **hoàn tác journal còn sót** rồi chờ lệnh. Không tự bật tính năng nào.
- Tắt máy hoặc gỡ daemon: launchd gửi SIGTERM, daemon tắt hết và hoàn tác.
- Chỉ **root** và **người đang ngồi trước máy** (chủ của `/dev/console`) điều khiển được daemon; kết nối từ tài khoản khác (SSH, user khác) bị từ chối. UID lấy từ kernel (`getpeereid`), không giả được.
- Dữ liệu, journal, log: `/Library/Application Support/LunaDNS` (root, quyền 700); log của daemon ở `logs/daemon.out` và `logs/lunadns.log`.

## Các lệnh

| Lệnh | Tác dụng |
|---|---|
| `lunadns on` | Bật Định tuyến DNS (qua dịch vụ nền). |
| `lunadns on --proxy` | Bật thêm Proxy SNI. Máy đã có PAC/proxy thủ công đang bật thì từ chối; thêm `--allow-proxy-override` để thay tạm. |
| `lunadns off` | Tắt hết và khôi phục. |
| `lunadns status` | Trạng thái. Không có dịch vụ nền thì tự đọc file resolver, PAC, dòng hosts của LunaDNS. |
| `sudo lunadns uninstall` | Tắt, khôi phục, gỡ dịch vụ nền, lệnh tắt và thư mục dữ liệu. |
| `sudo lunadns run [--proxy]` | Không cài dịch vụ nền: chạy tiền cảnh, **Ctrl+C** để tắt và khôi phục. Không chạy được khi dịch vụ nền đang chạy. |
| `sudo lunadns cleanup` | Hoàn tác mọi thay đổi còn trong journal. Không chạy khi dịch vụ nền hoặc `run` đang giữ khoá. |

Mã thoát: `0` xong · `1` còn việc chưa xong, hoặc dịch vụ nền chưa chạy (xem `/Library/Application Support/LunaDNS/logs`) · `2` tham số sai hoặc thiếu sudo.

Kiểm tra sau khi bật:

```bash
scutil --dns | grep -A3 steampowered
```

Phải thấy `nameserver[0] : 127.0.0.1` và `port : 15353`. Lưu ý `dig`/`nslookup` **không** đọc `/etc/resolver`; thử bằng `dscacheutil -q host -a name store.steampowered.com` hoặc mở Steam.

## Lưu ý

- **Đã tự tạo `/etc/resolver/steampowered.com` bằng tay** (ví dụ script `steam-dns`)? LunaDNS sẽ bỏ qua domain đó. Gỡ file tay trước rồi mới chạy LunaDNS.
- **VPN** (Surfshark, IPVanish...) khi bật có thể đẩy DNS của chính nó cho mọi tên miền và vượt qua `/etc/resolver`. Tailscale MagicDNS cũng dùng `/etc/resolver` nhưng chỉ cho tên miền của tailnet, không đụng Steam.
- **Proxy SNI:** chưa kiểm chứng Steam trên macOS có theo PAC hệ thống ở mọi kết nối hay không. Nếu Store vẫn không tải thì nguyên nhân thường là lọc SNI ở kết nối không theo PAC.
- `networksetup` không có lệnh xoá URL PAC: với service vốn chưa có PAC, khi tắt LunaDNS chỉ **tắt** auto-proxy, URL `http://127.0.0.1:1808x/proxy.pac` còn nằm lại ở trạng thái tắt (vô hại). Service có PAC riêng thì được trả về đúng URL và trạng thái cũ.
- Network service mới xuất hiện sau khi đã bật Proxy SNI (cắm thêm card) sẽ không có PAC cho tới lần bật sau.

## Đóng gói (.app, .dmg)

```bash
build/publish-macos.sh
```

Chạy trên macOS, không cần sudo. Dựng `artifacts/publish/<rid>/LunaDNS.app` cho `osx-arm64` và `osx-x64`, ký ad-hoc, rồi tạo `artifacts/LunaDNS-<v>-macos-arm64.dmg`, `…-macos-x64.dmg` (mỗi file có app, lối tắt Applications, `HUONG-DAN.txt`) và `artifacts/SHA256SUMS-macos.txt`. Tham số: `--version x.y.z` (mặc định `<Version>` trong `LunaDns.App.csproj`), `--rid osx-arm64|osx-x64|all`.

- Bố cục `.app` và hàm ký ở `build/macos-bundle.sh` (dùng chung với `run-macos.sh`). `Info.plist` sinh từ đó.
- Không có bản universal: bản .NET self-contained có runtime và assembly khác nhau theo kiến trúc, `lipo` không ghép được.
- Ký ad-hoc ký từng file trong `Contents/MacOS` rồi ký bundle; script kiểm bằng `codesign --verify --deep --strict`. `spctl` vẫn báo `rejected` vì chưa notarize: đúng như mong đợi.
- Icon: `build/make-icns.sh` sinh `Assets/LunaDNS.icns` và `Assets/tray-{off,on,error}-mac.png` từ cùng nguồn hình với `.ico` (`build/icongen --macos`). Chỉ chạy lại khi đổi hình; các file đã commit.
- GitHub Actions: khi đẩy tag `v*`, job `release-macos` (macos-latest) build, test, chạy `publish-macos.sh` rồi job Windows đính kèm hai `.dmg` và `SHA256SUMS-macos.txt` vào Release nháp.
- Khi có Apple Developer ID: thay `codesign -s -` bằng chữ ký Developer ID kèm `--options runtime` và entitlements cho JIT của .NET, rồi `xcrun notarytool submit --wait` và `xcrun stapler staple` cho `.dmg`.

## Test tích hợp (chạy tay)

Unit test chạy trên thư mục tạm, `networksetup` giả và dịch vụ nền giả:

```bash
dotnet test tests/LunaDns.Platform.MacOS.Tests
```

```bash
dotnet test tests/LunaDns.App.Tests -f net8.0
```

(`LunaDns.App.Tests` có hai đích: `net8.0` là bản macOS, `net8.0-windows` là bản Windows và chỉ chạy được trên Windows.)

Test đụng hệ thống thật chỉ chạy khi đặt biến và dùng sudo; mỗi test áp dụng rồi hoàn tác, so trạng thái trước/sau:

Build bằng user thường trước, rồi mới chạy bằng sudo với `--no-build`, để `obj/`, `bin/` và `~/.dotnet` không bị đổi chủ sang root (lần build sau sẽ lỗi quyền):

```bash
dotnet build tests/LunaDns.Platform.MacOS.Tests
```

```bash
sudo env PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 LUNADNS_ADMIN_TESTS=1 dotnet test tests/LunaDns.Platform.MacOS.Tests --no-build --filter Category=MacAdmin
```

Lỡ chạy bằng sudo mà không có `--no-build` thì trả quyền lại: `sudo chown -R "$USER" ~/.dotnet src tests`.

Gồm: file resolver xuất hiện trong `scutil --dns` rồi biến mất; `/etc/hosts` giống hệt từng byte sau một vòng; auto-proxy của từng service trở về đúng trạng thái bật/tắt (và URL nếu trước đó có).
