# 5. The Advanced section

<sub>[Tiếng Việt](../05-nang-cao.md)</sub>

The **Nâng cao** (Advanced) section sits at the end of the **Kiểm tra** (Check) zone, right below the check card (Windows only). Expand it. Changes **take effect the next time you turn a feature on** (turn the switch off and on again). The choices are saved in `%ProgramData%\LunaDNS\settings.json`.

| Option | Values | Notes |
|---|---|---|
| **Điểm cắt gói** (Packet split point) | *Sau byte đầu tiên* (After the first byte; default, `FirstByte`) / *Giữa tên miền (SNI)* (In the middle of the domain name (SNI); `MidSni`) | **Keep the default.** On the network tested, MidSni made the Steam Store go black and Kiểm tra show "Không vào được" (Unreachable); only FirstByte got through. If you pick MidSni and it fails, pick FirstByte again, then turn Proxy SNI off and on. |
| **Tách TLS record** (Split TLS records) | on / off (default off) | Sends the ClientHello as two TLS records, cut at the point chosen above (with *Sau byte đầu tiên*, the first record keeps the first 6 bytes of the ClientHello, because some TLS servers (such as Windows SChannel) reject a shorter first record), so a filter that reassembles TCP still does not see the whole domain name. Only try it when Kiểm tra still says "Không vào được" with both split points. If it fails, turn it off, then turn Proxy SNI off and on. |
| **Độ trễ giữa hai phần (ms, 0–20)** (Delay between the two parts, ms, 0–20) | whole number | Wait between the two halves of the split packet. Default 0. Values outside the range are clamped to 0–20. |
| **Máy chủ DoH** (DoH server) | *Cloudflare* (default) / *Google* / *Quad9* / *AdGuard* / *Tự nhập* (Custom) | The chosen server is asked first; two other built-in servers are the fallback. Click **Đo** (Measure) to measure latency on your network and pick the fastest. *Tự nhập*: paste an https://… URL and click **Lưu** (Save); the app looks up that server's IP over the built-in DoH (not the ISP's DNS). If it cannot, fill in the "IP (tuỳ chọn)" (IP, optional) box. |

**Kiểm tra cập nhật** (Check for updates) and **Gỡ khỏi máy** (Remove from this PC) are not in this section: they are in the **Cập nhật** (Update) and **Gỡ & dọn** (Remove & clean up) zones (see [The four zones of the window](03-switches.md#the-four-zones-of-the-window), [Upgrade](01-install.md#upgrade), [Uninstall and cleanup](07-uninstall-and-cleanup.md)).

To reset to defaults: quit the app (everything turns off), then delete `settings.json`. A damaged file is automatically renamed to `settings.json.corrupt-<time>` and the app uses the defaults.
