# 5. The Advanced section

<sub>[Tiếng Việt](../05-nang-cao.md)</sub>

Expand the **Nâng cao** (Advanced) section. Changes **take effect the next time you turn a feature on** (turn the switch off and on again). The choices are saved in `%ProgramData%\LunaDNS\settings.json`.

| Option | Values | Notes |
|---|---|---|
| **Điểm cắt gói** (Packet split point) | *Sau byte đầu tiên* (After the first byte; default, `FirstByte`) / *Giữa tên miền (SNI)* (In the middle of the domain name (SNI); `MidSni`) | **Keep the default.** On the network tested, MidSni made the Steam Store go black and Kiểm tra show "Không vào được" (Unreachable); only FirstByte got through. If you pick MidSni and it fails, pick FirstByte again, then turn Proxy SNI off and on. |
| **Độ trễ giữa hai phần (ms, 0–20)** (Delay between the two parts, ms, 0–20) | whole number | Wait between the two halves of the split packet. Default 0. Values outside the range are clamped to 0–20. |
| **Máy chủ DoH** (DoH server) | *Cloudflare trước, Google dự phòng* (Cloudflare first, Google as fallback; default) / *Google trước, Cloudflare dự phòng* (Google first, Cloudflare as fallback) | Change it if one of the two is blocked. |

Below the row with **Tắt hết & khôi phục** (Turn all off & restore) and **Chẩn đoán** (Diagnose) (see [Command line](06-command-line.md#report)) there are two more buttons:

- **Kiểm tra cập nhật** (Check for updates): asks GitHub Releases whether there is a new version; if there is, it asks whether to open the download page. The app also **checks once by itself, about 10 seconds after it opens**, without a dialog: if there is a new version, a banner appears saying *"Đã có LunaDNS x.y.z (bạn đang dùng a.b.c)."* ("LunaDNS x.y.z is available (you are using a.b.c).") with a **Tải** (Download) button. The app never downloads or installs by itself.
- **Gỡ khỏi máy** (Remove from this PC): only shown in the Setup version. See [Uninstall and cleanup](07-uninstall-and-cleanup.md#uninstall-the-setup-version).

To reset to defaults: quit the app (everything turns off), then delete `settings.json`. A damaged file is automatically renamed to `settings.json.corrupt-<time>` and the app uses the defaults.
