# 9. macOS (thử nghiệm)

Bản macOS hiện là **công cụ dòng lệnh** `lunadns`, dùng chung lõi với bản Windows (DoH, stub DNS, proxy SNI, journal). Chưa có giao diện, chưa có bản đóng gói `.app`/`.dmg`, chưa tự dọn lúc khởi động máy; các phần đó làm ở các bước sau.

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

## Các lệnh

| Lệnh | Tác dụng |
|---|---|
| `sudo ./lunadns run` | Bật Định tuyến DNS. Giữ cửa sổ Terminal mở; **Ctrl+C** để tắt và khôi phục. |
| `sudo ./lunadns run --proxy` | Bật thêm Proxy SNI. Máy đã có PAC/proxy thủ công đang bật thì từ chối; thêm `--allow-proxy-override` để thay tạm. |
| `sudo ./lunadns cleanup` | Hoàn tác mọi thay đổi còn trong journal (khi lần trước bị tắt ngang, mất điện). |
| `./lunadns status` | Xem đang có file resolver, PAC, dòng hosts nào của LunaDNS. Không cần sudo. |

Mã thoát: `0` xong · `1` còn việc chưa xong (xem `/Library/Application Support/LunaDNS/logs`) · `2` tham số sai hoặc thiếu sudo.

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

## Test tích hợp (chạy tay)

Unit test (`dotnet test tests/LunaDns.Platform.MacOS.Tests`) chạy trên thư mục tạm và `networksetup` giả. Test đụng hệ thống thật chỉ chạy khi đặt biến và dùng sudo; mỗi test áp dụng rồi hoàn tác, so trạng thái trước/sau:

Build bằng user thường trước, rồi mới chạy bằng sudo với `--no-build`, để `obj/`, `bin/` và `~/.dotnet` không bị đổi chủ sang root (lần build sau sẽ lỗi quyền):

```bash
dotnet build tests/LunaDns.Platform.MacOS.Tests
```

```bash
sudo env PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 LUNADNS_ADMIN_TESTS=1 dotnet test tests/LunaDns.Platform.MacOS.Tests --no-build --filter Category=MacAdmin
```

Lỡ chạy bằng sudo mà không có `--no-build` thì trả quyền lại: `sudo chown -R "$USER" ~/.dotnet src tests`.

Gồm: file resolver xuất hiện trong `scutil --dns` rồi biến mất; `/etc/hosts` giống hệt từng byte sau một vòng; auto-proxy của từng service trở về đúng trạng thái bật/tắt (và URL nếu trước đó có).
