# 7. Gỡ và dọn khi app chết

## Gỡ bản Setup

Chọn một:

- **Settings → Apps → LunaDNS → Uninstall.** Nếu LunaDNS đang chạy, trình gỡ nhắc bạn thoát app trước.
- Nút **Gỡ khỏi máy** trong app (chỉ hiện ở bản Setup): app hỏi xác nhận, mở trình gỡ rồi tự thoát bằng "Thoát & khôi phục".

Trình gỡ chạy `LunaDNS.exe --uninstall`: hoàn tác mọi thay đổi, xoá tác vụ dọn dẹp và `%ProgramData%\LunaDNS`, rồi mới xoá app. Nếu còn thay đổi chưa hoàn tác được, trình gỡ **dừng lại, chưa gỡ gì** và báo *"Chưa khôi phục được hết cấu hình mạng (mã …)"*. Khi đó:

1. Mở LunaDNS, bấm **Tắt hết & khôi phục**, rồi thoát bằng **Thoát & khôi phục**.
2. Xem log `C:\ProgramData\LunaDNS\logs\lunadns.log` (lệnh ở dưới) để biết mục nào lỗi; dọn tay nếu cần ([Khi `--cleanup` không đủ](#khi---cleanup-không-đủ-thủ-công-chỉ-khi-cần)).
3. Gỡ lại.

## Gỡ bản portable

1. Thoát app bằng menu khay **Thoát & khôi phục**.
2. Mở PowerShell bằng **Run as administrator**, chạy `--uninstall` và xem mã thoát (0 = sạch):

```powershell
$p = Start-Process C:\Tools\LunaDNS\LunaDNS.exe -ArgumentList '--uninstall' -Wait -PassThru
$p.ExitCode
```

3. Xoá thư mục chứa exe.

Mã 4 là app vẫn đang chạy; mã 1 xem [Dòng lệnh](06-dong-lenh.md#--uninstall).

## Tác vụ dọn dẹp (bản Setup)

Setup đăng ký tác vụ `\LunaDNS\Cleanup` trong Task Scheduler: chạy `C:\Program Files\LunaDNS\LunaDNS.exe --cleanup` bằng tài khoản SYSTEM **lúc khởi động máy** và **lúc có người đăng nhập**. Nhờ vậy, nếu app bị kill hay máy mất điện, cấu hình mạng tự về như cũ ở lần bật máy sau.

- Lúc khởi động máy, cấu hình proxy của người dùng (`AutoConfigURL`) chưa sửa được vì Windows chưa nạp hồ sơ người dùng; mục đó được giữ lại và hoàn tác ở lần chạy lúc đăng nhập. Luật NRPT và hosts hoàn tác được ngay.
- Đang có LunaDNS chạy thì tác vụ không làm gì.
- Gỡ bằng Setup là tác vụ tự xoá. Cần xoá tay (PowerShell Admin):

```powershell
schtasks /Delete /TN \LunaDNS\Cleanup /F
```

Bản portable không có tác vụ này.

## App bị kill, treo, mất điện

Thay đổi hệ thống nằm lại: luật NRPT, `AutoConfigURL`, dòng hosts. Triệu chứng: Steam không vào được vì tên miền trỏ về `127.0.0.1:53` mà không còn stub, hoặc PAC trỏ vào cổng chết.

Cách dọn, chọn một:

1. **Bản Setup:** khởi động lại máy rồi đăng nhập; tác vụ dọn dẹp tự hoàn tác (xem trên).
2. **Mở lại LunaDNS bằng Admin:** lúc khởi động app tự hoàn tác mục còn sót (*"Đã hoàn tác N thay đổi còn sót…"*).
3. **Dòng lệnh Admin:** `LunaDNS.exe --cleanup` (mã thoát 0 = sạch). Xem log:

```powershell
Get-Content C:\ProgramData\LunaDNS\logs\lunadns.log -Tail 20 -Encoding UTF8
```

Cả hai đều dựa vào `journal.json`, nên chỉ hoạt động khi file này còn.

## Kiểm tra đã sạch chưa (PowerShell Admin)

```powershell
# Luật NRPT của LunaDNS (Comment bắt đầu bằng "LunaDNS ")
Get-DnsClientNrptRule | Where-Object Comment -like 'LunaDNS *'
# PAC
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name AutoConfigURL -ErrorAction SilentlyContinue
# hosts
Select-String -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Pattern '# LunaDNS'
```

Cả ba không ra gì (và `AutoConfigURL` không phải `http://127.0.0.1:.../proxy.pac`) là sạch.

## Khi `--cleanup` không đủ (thủ công, chỉ khi cần)

- **Luật NRPT mồ côi** (không còn trong journal, ví dụ mất `journal.json`): `--cleanup` **không** xoá loại này. Xoá tay chỉ luật có Comment `LunaDNS …`, **tuyệt đối không** đụng luật không Comment của Tailscale/VPN:

```powershell
Get-DnsClientNrptRule | Where-Object Comment -like 'LunaDNS *' | Remove-DnsClientNrptRule -Force
ipconfig /flushdns
```

- **hosts:** xoá dòng có `# LunaDNS` trong `C:\Windows\System32\drivers\etc\hosts` (mở Notepad bằng Admin).
- **PAC:** nếu `AutoConfigURL` đúng là PAC của LunaDNS thì đặt lại ở Settings → Network & Internet → Proxy, hoặc xoá giá trị đó.
- `journal.json` hỏng được đổi tên `journal.json.corrupt-<thời gian>`; khi đó phải dọn tay như trên.

## Thư mục dữ liệu chỉ Admin sửa được

`%ProgramData%\LunaDNS` mở xem được bằng Explorer (Users được đọc), nhưng chỉ SYSTEM và Administrators ghi được. Đó là cố ý: tác vụ dọn dẹp chạy bằng SYSTEM đọc `journal.json`. Muốn sửa hay xoá tay file trong đó, dùng Notepad/PowerShell mở bằng Admin.
