# Kết quả spike — 2026-11

| Spike | Máy / bản Windows | Kết quả | Quyết định cho kế hoạch sau |
|---|---|---|---|
| NRPT local + 127.0.0.1 | Microsoft Windows 10 Pro 10.0.19045 | 10.9.8.7 — NRPT hoạt động; quy tắc spike gỡ sạch (kiểm lại: 0) | Giữ NRPT cho KH4. Chưa thử Windows 10/11 Home |
| Steam đọc PAC | Steam client win64 1792618285 | CEF: **đọc** (Store báo Error -102 = ERR_CONNECTION_REFUSED tới proxy 18999), CM: **không đọc** (khởi động lại Steam 10:46:37 khi PAC bật; CM nối thẳng 103.10.124.5:27018 WebSocket, LogOnResponse 'OK' dù PAC trỏ `.steamserver.net` vào proxy chết) | Proxy/PAC chỉ tác động Store/Community (CEF). Kết nối CM chỉ chỉnh được bằng định tuyến DNS (NRPT) ⇒ ghi giới hạn này vào FAQ (KH7) và vào mô tả chế độ proxy ở KH3 |
| Kích thước Avalonia | win-x64, .NET SDK 8.0.428, Avalonia 11.3.23 | trimmed 34,4 MB (exe 18,5 + 3 DLL native 15,9); untrimmed 89,6 MB (exe 73,7 + 15,9). Bản trimmed mở cửa sổ bình thường, không có cảnh báo IL | Single-file self-contained **trimmed** (< 80 MB, không cần spec 10.3). KH5 thêm `IncludeNativeLibrariesForSelfExtract=true` nếu muốn đúng 1 file |

## Ghi chú

- **NRPT chạy chung với app khác:** máy thử có sẵn 2 quy tắc NRPT của Tailscale (MagicDNS, nameserver `100.100.100.100`, 67 namespace, không có Comment). ⇒ KH4 chỉ được thêm/gỡ quy tắc do LunaDNS tạo (nhận diện bằng Comment hoặc Name đã ghi trong journal), không bao giờ xoá hay sửa quy tắc lạ; cần thêm test cho trường hợp namespace trùng nhau.
- Khi chạy spike NRPT, phần `finally` in chậm (`Stop-Job` đợi socket đang chặn ở `Receive` hết hạn 15 s). Output dán lại bị cắt trước dòng "Còn quy tắc spike"; tôi đã kiểm lại riêng: 0 quy tắc spike, cổng 53 trống.

- **Avalonia 12 không dùng được với .NET 8.** Template `avalonia.app` hiện mặc định là `net10.0` + Avalonia 12.1.6. Khi đổi sang `-f net8.0`, build báo CS9057: source generator của Avalonia 12 cần Roslyn 4.14, mà SDK 8.0.428 chỉ có 4.11, nên `InitializeComponent` không được sinh (CS0103). ⇒ Kế hoạch 5 ghim `Avalonia*` ở `11.3.*`. Nếu sau này cần Avalonia 12 thì phải lên SDK 9.0.3xx/10 trở lên (cần PO duyệt, vì lệch ràng buộc .NET 8).
- Script NRPT lệch kế hoạch: responder chỉ trả header + question (bỏ OPT/EDNS mà Windows gắn vào), trả lời được nhiều gói, kiểm tra quyền Admin và cổng 53 trước khi chạy, lưu UTF-8 có BOM cho PowerShell 5.1. Mong đợi vẫn giữ nguyên.
