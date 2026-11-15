# 3. The switches

<sub>[Tiếng Việt](../03-ba-cong-tac.md)</sub>

There are two switches: Định tuyến DNS (DNS routing) and Proxy SNI (SNI proxy). Each switch turns on and off independently. A switch shows *"Đang tắt"* (Off) or *"Đang bật"* (On) (Proxy SNI also shows the port and the number of connections: *"Đang bật · cổng 18080 · 33 kết nối"*, "On · port 18080 · 33 connections").

Every system change is **written to the journal before it is made**, and undone in reverse order (LIFO). The app only touches things it created itself.

## DNS routing (Định tuyến DNS)

- Starts a DoH stub at `127.0.0.1:53`, asks Cloudflare (`1.1.1.1`) first and Google (`8.8.8.8`) as a fallback; change the order in [Advanced](05-advanced.md). The DoH connection goes straight to the IP and does not ask the system DNS.
- Adds **one NRPT rule** with display name "LunaDNS" and a Comment starting with `LunaDNS `, covering the domains of the **Steam** group (`steampowered.com`, `steamcommunity.com`, `steamstatic.com`, `steam-chat.com`, `steamusercontent.com`, `steamgames.com`, `steamcontent.com`, `steamserver.net`, `counter-strike.net`, `dota2.com`, `valvesoftware.com`; each name also covers its subdomains), pointing to `127.0.0.1`.
- Rules from other software (VPN, Tailscale…) are not changed or removed. If other software already routes DNS for these same domains, the app does not turn on and says *"Một phần mềm khác (VPN, Tailscale…) đang định tuyến DNS cho chính các tên miền này."* ("Other software (VPN, Tailscale…) is already routing DNS for these exact domains.")
- Port 53 busy error: *"Cổng DNS 53 trên 127.0.0.1 đang bị chương trình khác dùng."* ("DNS port 53 on 127.0.0.1 is being used by another program.")

## SNI proxy (Proxy SNI)

- Opens an HTTP CONNECT proxy on `127.0.0.1`, on the first free port in **18080–18090**. The proxy only accepts CONNECT to Steam domain names on port 443 and serves `/proxy.pac`.
- Sets `AutoConfigURL` (Internet Settings of the signed-in user) to `http://127.0.0.1:<port>/proxy.pac`. Only Steam domain names go through the proxy.
- Splits the TLS ClientHello packet at the split point chosen in Advanced (default: **after the first byte**).
- If the PC already has a PAC or a manual proxy, the app asks *"Thay cấu hình proxy hiện có?"* ("Replace the existing proxy settings?"). If you agree, the old settings are put back when you turn Proxy SNI off or quit. If you cancel, the switch does not change. If `AutoConfigURL` is in an unusual form, the app does not overwrite it.
- Watchdog: if the proxy dies it is restarted on the same port; more than 3 failures in 1 minute and the feature turns itself off and tells you.
- Out of ports: *"Không còn cổng trống cho Proxy SNI."* ("No free port left for Proxy SNI.")

## PUBG mode: not supported

LunaDNS currently **does not support PUBG**; the interface has no switch or check box for PUBG. If the hosts file still has `# LunaDNS` lines from an old test build, "Tắt hết & khôi phục" (Turn all off & restore) or `--cleanup` will undo them.

## Turning off

- Flip a switch off: undoes that feature's part. If the undo fails, the service **keeps running**, the entry stays in the journal, and the message *"Một số thay đổi chưa hoàn tác được…"* ("Some changes could not be undone…") appears; turn it off again to retry.
- **Tắt hết & khôi phục** (Turn all off & restore): turns off every switch and puts the network settings back as before (*"Đã tắt hết và trả lại cấu hình mạng như trước."*, "Everything is off and the network settings are back as before.").
- A feature that keeps failing stops by itself and says *"… gặp lỗi lặp lại nên đã được tắt."* ("… kept failing, so it was turned off.")
