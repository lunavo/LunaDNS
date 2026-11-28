# 3. The switches

<sub>[Tiếng Việt](../03-ba-cong-tac.md)</sub>

There are two switches: Định tuyến DNS (DNS routing) and Proxy SNI (SNI proxy). Each switch turns on and off independently. A switch shows *"Đang tắt"* (Off) or *"Đang bật"* (On) (DNS routing also shows the DoH server that is answering: *"Đang bật · DoH: Cloudflare"*, "On · DoH: Cloudflare"; Proxy SNI also shows the port and the number of connections: *"Đang bật · cổng 18080 · 33 kết nối"*, "On · port 18080 · 33 connections").

Every system change is **written to the journal before it is made**, and undone in reverse order (LIFO). The app only touches things it created itself.

## The four zones of the window

The main window is arranged by what you want to do, top to bottom:

| Zone | What is in it | See |
|---|---|---|
| **Kiểm tra** (Check) | Check results by group, **Kiểm tra** (Check), **Áp dụng đề xuất** (Apply suggestions), **Tìm cấu hình hợp mạng** (Find a configuration that works), **Chẩn đoán** (Diagnose); right below is the **Nâng cao** (Advanced) section (Windows only) | [Check and suggestions](04-check-and-suggestions.md), [Advanced](05-advanced.md) |
| **Trạng thái** (Status) | The two switches, the **Tự bật lại** (Turn back on) box and the **TÊN MIỀN** (Domains) section | this page |
| **Cập nhật** (Update) | The line *"Đang dùng LunaDNS x.y.z"* ("Using LunaDNS x.y.z"), **Kiểm tra cập nhật** (Check for updates), **Tự cập nhật danh sách tên miền** (Auto-update domain list) | [Install](01-install.md#upgrade), [Auto-update the domain list](#auto-update-the-domain-list) |
| **Gỡ & dọn** (Remove & clean up) | **Tắt hết & khôi phục** (Turn all off & restore), **Gỡ khỏi máy** (Remove from this PC; Setup version) or a line telling you how to uninstall (portable, macOS) | [Turning off](#turning-off), [Uninstall and cleanup](07-uninstall-and-cleanup.md) |

Banners (messages, new version, macOS background service) always appear at the top of the window.

## DNS routing (Định tuyến DNS)

- Starts a DoH stub at `127.0.0.1:53`, asks Cloudflare (`1.1.1.1`) first and Google (`8.8.8.8`) as a fallback; change the order in [Advanced](05-advanced.md). The DoH connection goes straight to the IP and does not ask the system DNS.
- **A DoH server that is blocked or slow:** when the server being asked has not answered after 0.5 seconds, the app asks the next one at the same time and takes whichever answer comes first. A server that fails, or loses to a server asked after it, rests for 5 minutes: meanwhile new names go straight to a healthy server. After 5 minutes the app tries the main server again. So if the ISP silently blocks `1.1.1.1`, new names are only about 0.5 seconds slower for the first few lookups, instead of 3 seconds for every name.
- The card shows the DoH server that answered last: *"Đang bật · DoH: Google (dự phòng)"* ("On · DoH: Google (fallback)") means the main server (usually Cloudflare) is not answering. Before the first lookup it only shows *"Đang bật"* (On).
- Adds **one NRPT rule** with display name "LunaDNS" and a Comment starting with `LunaDNS `, covering the domains of the **Steam** group (`steampowered.com`, `steamcommunity.com`, `steamstatic.com`, `steam-chat.com`, `steamusercontent.com`, `steamgames.com`, `steamcontent.com`, `steamserver.net`, `counter-strike.net`, `dota2.com`, `valvesoftware.com`; each name also covers its subdomains), pointing to `127.0.0.1`.
- Rules from other software (VPN, Tailscale…) are not changed or removed. If other software already routes DNS for these same domains, the app does not turn on and says *"Một phần mềm khác (VPN, Tailscale…) đang định tuyến DNS cho chính các tên miền này."* ("Other software (VPN, Tailscale…) is already routing DNS for these exact domains.")
- Port 53 busy error: *"Cổng DNS 53 trên 127.0.0.1 đang bị chương trình khác dùng."* ("DNS port 53 on 127.0.0.1 is being used by another program.")

## SNI proxy (Proxy SNI)

- Opens an HTTP CONNECT proxy on `127.0.0.1`, on the first free port in **18080–18090**. The proxy only accepts CONNECT to Steam domain names on port 443 and serves `/proxy.pac`.
- Sets `AutoConfigURL` (Internet Settings of the signed-in user) to `http://127.0.0.1:<port>/proxy.pac`. Only Steam domain names go through the proxy.
- Splits the TLS ClientHello packet at the split point chosen in Advanced (default: **after the first byte**).
- If the PC already has a PAC or a manual proxy, the app asks *"Thay cấu hình proxy hiện có?"* ("Replace the existing proxy settings?"). If you agree, the old settings are put back when you turn Proxy SNI off or quit. If you cancel, the switch does not change. If `AutoConfigURL` is in an unusual form, the app does not overwrite it.
- Names with several IPs: when the IP being connected has not connected after 1 second, the app also connects to the next IP at the same time (up to 3 IPs), so one silently blocked IP does not break the connection.
- Watchdog: if the proxy dies it is restarted on the same port; more than 3 failures in 1 minute and the feature turns itself off and tells you.
- Out of ports: *"Không còn cổng trống cho Proxy SNI."* ("No free port left for Proxy SNI.")

## Domains

The **TÊN MIỀN** (Domains) section lists the domain groups the two switches route. Each group shows how many domains it has; **Xem** (View) opens the list with a source column (*"Có sẵn"*, built in, or *"Bản cập nhật dd/MM"*, update of dd/MM). The list has three layers:

1. **Built in:** shipped with the app, always present.
2. **Update:** extra domains published by LunaDNS with a digital signature. The app only accepts a correctly signed file; this layer only **adds** domains to existing groups and never removes any. A group with extra domains shows *"N tên miền (M từ bản cập nhật)"* (N domains, M from the update).
3. **Tuỳ chỉnh (Custom):** domains you add yourself. Updates **never overwrite** this layer. A custom domain that is already in an enabled group shows a grey note *"Cũng có trong nhóm …"* (Also in group …).

### Auto-update the domain list

The **Tự cập nhật danh sách tên miền** (Auto-update domain list) row sits in the **Cập nhật** (Update) zone and has a switch and an **Cập nhật ngay** (Update now) button. When it is on, the app checks for a new list 30 seconds after launch and every 24 hours (after a network error it retries in 1 hour). A new list is applied to the running switches first and saved only after that succeeds. Turning the switch off goes back to the built-in list and removes layer-2 domains from the running rules.

| Status line | Meaning |
|---|---|
| *Bản dd/MM · kiểm lúc HH:mm* | Using the update issued on dd/MM, last checked at HH:mm. |
| *Chưa tải bản nào* | No valid update yet; using the built-in list. |
| *Đang dùng danh sách có sẵn* | The switch is off; using the built-in list. |
| *Đã là bản mới nhất* / *Đã thêm N tên miền* | Result of **Cập nhật ngay**: already up to date / N domains added. |
| *Không tải được, thử lại sau* | Network error; the app retries on its own. |
| *Không kiểm được lúc HH:mm, giữ bản dd/MM* | The check failed; the existing update is kept (or *"dùng danh sách có sẵn"*, built-in list, if there is none). |
| *Chữ ký không hợp lệ, giữ bản cũ* | The downloaded file is damaged, wrongly signed or signed with an unknown key; it is ignored. |
| *Cần bản LunaDNS mới hơn để đọc danh sách này* | The list uses a format this app version cannot read. |
| *Không lưu được cài đặt, giữ như cũ* | The switch changed but settings.json could not be written; the app puts the previous configuration back. |
| *Không áp được bản dd/MM, xem Chẩn đoán* | Applying it to the running switches failed; the old configuration stays. **Chẩn đoán** (Diagnostics) has a "Danh sách tên miền cập nhật" block. |

## Turn back on automatically (Tự bật lại)

The app remembers which of the two switches you left on and turns them back on by itself:

- **Windows:** when the app opens (after it undoes anything left over from the last run). The box reads *"Tự bật lại khi mở LunaDNS"* ("Turn back on when LunaDNS opens").
- **macOS:** when the background service starts, that is after a restart or after **Cài lại dịch vụ nền** (Reinstall background service), even before you open the app. The box reads *"Tự bật lại sau khi khởi động máy hoặc cài lại dịch vụ nền"* ("Turn back on after a restart or a background service reinstall"). See [macOS](09-macos.md#background-service-recommended).

The box sits right under the two switches and is **on** by default. What is remembered:

- Flipping a switch on or off, **Áp dụng đề xuất** (Apply suggestions), **Tìm cấu hình hợp mạng** (Find settings that suit your network): the state right after that action.
- **Tắt hết & khôi phục** (Turn all off & restore), **Thoát & khôi phục** (Quit & restore; on macOS also Cmd+Q and Quit in the Dock): nothing is turned back on next time.
- Signing out, shutting down, a feature stopping itself after repeated errors: still remembered, turned back on next time.

Turning back on **never overrides an existing proxy/PAC** on the machine, even if you agreed to it last time. PUBG mode is never turned back on.

When a feature you left on is now off (the box is off, turning back on failed, the machine uses a proxy/PAC, the feature stopped itself…), a band at the top of the window says *"Định tuyến DNS đang tắt dù lần trước bạn để bật."* ("DNS routing is off although you left it on last time.") with the reason and a **Bật lại** (Turn back on) button. The button works like flipping the switch by hand (including the proxy override question). Click **×** to hide the band until the set of missing features changes.

## Remember per network (Nhớ cấu hình theo mạng)

When a configuration gets Steam through (after **Tìm cấu hình hợp mạng** (Find settings that suit your network), or a **Kiểm tra** (Check) that passes), LunaDNS remembers it for the network you are on, recognised by your router. When you come back to that network, it switches to the saved configuration by itself. On a network it has not seen, it runs a quiet check and, if Steam looks blocked, shows a band *"Mạng mới có vẻ chặn Steam."* ("The new network seems to block Steam.") with **Tìm cấu hình hợp mạng**. This only happens while at least one of the two switches is on.

The Status zone shows the current network and whether a configuration is saved for it. Nothing is sent anywhere; profiles live in `profiles.json` next to `settings.json`. **Quên mạng này** (Forget this network) in the Status zone and **Xoá mọi cấu hình theo mạng** (Delete all per-network configurations) in Remove & clean up delete them; Quit & restore keeps them, because they are not system changes. Untick **Nhớ cấu hình theo mạng** (Remember per network), under Turn back on automatically, to stop it.

## PUBG mode: not supported

LunaDNS currently **does not support PUBG**; the interface has no switch or check box for PUBG. If the hosts file still has `# LunaDNS` lines from an old test build, "Tắt hết & khôi phục" (Turn all off & restore) or `--cleanup` will undo them.

## Turning off

- Flip a switch off: undoes that feature's part. If the undo fails, the service **keeps running**, the entry stays in the journal, and the message *"Một số thay đổi chưa hoàn tác được…"* ("Some changes could not be undone…") appears; turn it off again to retry.
- **Tắt hết & khôi phục** (Turn all off & restore; in the **Gỡ & dọn** zone, or the tray menu): turns off every switch and puts the network settings back as before (*"Đã tắt hết và trả lại cấu hình mạng như trước."*, "Everything is off and the network settings are back as before."). Next time the app does not [turn anything back on](#turn-back-on-automatically-tự-bật-lại).
- A feature that keeps failing stops by itself and says *"… gặp lỗi lặp lại nên đã được tắt."* ("… kept failing, so it was turned off.")
