# 6. Dòng lệnh

Chạy từ PowerShell/CMD **mở bằng Admin**. Tham số không phân biệt hoa thường; chỉ nhận **một** tham số. exe là ứng dụng cửa sổ nên **không in gì ra console**: kết quả xem ở mã thoát và log.

Chờ và đọc mã thoát trong PowerShell (gọi thẳng `.\LunaDNS.exe --cleanup` thì shell không đợi):

```powershell
$p = Start-Process .\LunaDNS.exe -ArgumentList '--cleanup' -Wait -PassThru
$p.ExitCode
```

| Lệnh | Mã thoát |
|---|---|
| `LunaDNS.exe` | giao diện |
| `--cleanup` | `0` = xong hoặc không có gì làm; `1` = còn mục lỗi (giữ trong journal) |
| `--report` | `0` = ghi được; `1` = không ghi được file |
| `--install-system` | `0` = xong; `1` = lỗi (xem log); `3` = exe không nằm trong Program Files, không đổi gì |
| `--uninstall` | `0` = xong; `1` = còn việc chưa xong (xem log); `4` = LunaDNS đang chạy |
| tham số khác / nhiều tham số | `2` |

Mọi lệnh (kể cả mở giao diện) đều khoá quyền `%ProgramData%\LunaDNS` trước khi làm việc; lỗi khoá quyền chỉ ghi cảnh báo vào log.

## --cleanup

Hoàn tác mọi mục trong `%ProgramData%\LunaDNS\journal.json` theo thứ tự ngược, rồi thoát.

- Đang có bản LunaDNS chạy: **không làm gì**, thoát mã 0 (log *"LunaDNS đang chạy, bỏ qua."*). Hãy dùng "Thoát & khôi phục" trong app.
- Journal không có hoặc trống: mã 0.
- **Không** dọn luật NRPT "mồ côi" không còn trong journal (xem [Gỡ và dọn](07-go-va-don-dep.md)).
- Kết quả ghi ở `logs\lunadns.log`: *"--cleanup: đã hoàn tác N mục, còn M mục lỗi."*

## --install-system

Setup gọi lệnh này sau khi chép file; thường bạn không cần chạy tay.

- Chỉ chạy khi `LunaDNS.exe` nằm dưới `%ProgramFiles%` (bản Setup). Ở chỗ khác (Downloads, thư mục portable) thoát mã **3** và không đổi gì: tác vụ chạy bằng SYSTEM, nên exe phải ở chỗ chỉ Admin ghi được.
- Khoá quyền `%ProgramData%\LunaDNS`, rồi đăng ký (hoặc ghi đè) tác vụ `\LunaDNS\Cleanup`. Chạy lại nhiều lần không sao.

## --uninstall

Trình gỡ của Setup gọi lệnh này; với bản portable, chạy tay trước khi xoá thư mục.

1. LunaDNS đang chạy thì thoát mã **4**, không làm gì. Thoát app ("Thoát & khôi phục") rồi chạy lại.
2. Hoàn tác mọi mục trong `journal.json`. Còn mục lỗi thì dừng ở đây với mã **1**, giữ journal và tác vụ dọn dẹp để làm tiếp lần sau.
3. Xoá tác vụ `\LunaDNS\Cleanup` (bản portable không có thì bỏ qua).
4. Xoá thư mục `%ProgramData%\LunaDNS` (journal, settings, log). Không xoá được thì mã **1**.

## Báo cáo

`LunaDNS.exe --report` (hoặc nút **Chẩn đoán** trong app) ghi `LunaDNS-report-<yyyyMMdd-HHmmss>.txt`, UTF-8 có BOM.

- **Hiện tại ghi ra Desktop.** PO đã chốt sẽ chuyển sang `%ProgramData%\LunaDNS\reports\` (giữ 10 báo cáo gần nhất, khoảng 64 KB mỗi file); **chưa code**, nên bây giờ vẫn là Desktop và không tự dọn.
- `--report` tự chạy Kiểm tra tối đa 20 giây (quá giờ: mục kiểm tra ghi "(chưa chạy)"). Nút Chẩn đoán dùng kết quả Kiểm tra gần nhất.
- Nội dung: phiên bản, Windows, kết quả kiểm tra, journal, luật NRPT của LunaDNS, `AutoConfigURL`, dòng hosts `# LunaDNS`, 200 dòng log cuối.
- **Che thông tin cá nhân:** tên người dùng, tên máy, SID, IP CGNAT/Tailscale (`100.64/10`), `169.254/16`, IPv6 ULA/link-local. `AutoConfigURL` chỉ ghi khi là địa chỉ loopback của LunaDNS; PAC khác chỉ ghi "có giá trị khác". Vẫn nên đọc lại trước khi gửi.
- `--report` không dựng dịch vụ của app nên không hoàn tác gì, kể cả khi app đang chạy.
