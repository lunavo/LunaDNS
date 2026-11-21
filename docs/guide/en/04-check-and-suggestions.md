# 4. Check and Apply suggestions

<sub>[Tiếng Việt](../04-kiem-tra-va-de-xuat.md)</sub>

## Check (Kiểm tra)

The check card is in the **Kiểm tra** (Check) zone at the top of the window. At the bottom of the card is the **Chẩn đoán** (Diagnose) button (writes a report to the Desktop, see [Command line](06-command-line.md#report)); right below the card is the [Advanced](05-advanced.md) section.
The **Kiểm tra** (Check) button (it also runs by itself when the app opens) tries sample hosts over the **original** network path, not through LunaDNS:

- Steam: `store.steampowered.com`, `steamcommunity.com`, `api.steampowered.com`, `cdn.akamai.steamstatic.com`

Each host gets one verdict:

| Verdict | Meaning | Suggested measure |
|---|---|---|
| Bình thường (Normal) | Reachable | none |
| DNS bị chặn (DNS blocked) | The provider's DNS gives a wrong answer (poisoning) | Định tuyến DNS (DNS routing) |
| Bị lọc SNI (SNI filtered) | DNS is correct but the TLS handshake is cut | Định tuyến DNS + Proxy SNI (DNS routing + SNI proxy) |
| Không vào được (Unreachable) | Cannot be reached even when the blocking is bypassed | none (the network may be down or the server under maintenance) |

Results are shown by group (`Steam: …`) with the time of the check. On failure there is the message *"Không chạy được kiểm tra…"* ("Could not run the check…"), with details in the log.

> **Important:** Kiểm tra measures the original network, so **after a measure is turned on, it may still say "Bị lọc SNI"**. This is normal: try opening Steam for real.

## Apply suggestions (Áp dụng đề xuất)

The **Áp dụng đề xuất** (Apply suggestions) button only appears while a measure **for Steam** is still off. Click it and the app turns on the missing switches (Định tuyến DNS and/or Proxy SNI). When there is nothing left to do, it says *"Các biện pháp đề xuất cho Steam đều đang bật."* ("The suggested measures for Steam are all on.")

- If the PC already has its own proxy, the box asking to replace the proxy settings still appears, as when you turn it on by hand.
- If nothing looks blocked: *"Không thấy bị chặn. Không cần bật gì."* ("Nothing looks blocked. There is nothing to turn on.")

## Find a configuration that works (Tìm cấu hình hợp mạng)

The **Find a configuration that works** button looks for a setup that works on your network:

- It measures the DoH servers, then checks the 4 Steam hosts.
- If they are still blocked, it tries up to 8 combinations of cut point, record splitting and a 0/10 ms delay, starting with the one in use. It stops at the first one that lets all 4 hosts through.
- When it finds one, the app **saves it automatically** and turns on DNS routing and/or the SNI proxy if needed, without asking.
- Press **Stop** (or close the window) during the search and nothing changes. A search takes 60 seconds at most.
- On Windows, the result shows in the **Advanced** section and you can still edit it by hand.
- While it runs, the Check, Apply suggestions and Measure buttons are disabled.
