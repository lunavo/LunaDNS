# 8. Troubleshooting

<sub>[Tiếng Việt](../08-su-co.md)</sub>

The log is at `C:\ProgramData\LunaDNS\logs\lunadns.log` (UTF-8 without BOM); the **Mở thư mục log** (Open log folder) button next to **Chẩn đoán** (Diagnose) opens this folder. If you need to send it to someone, press **Chẩn đoán** (the message bar then has a **Hiện trong Explorer** (Show in Explorer) button) or create a report from the command line ([Command line](06-command-line.md#report)).

macOS: the interface's log is in `~/Library/Application Support/LunaDNS/logs` (the **Mở thư mục log** button); the background service's log belongs to root, so you can only see it through the **Chẩn đoán** report ([macOS](09-macos.md#interface)).

| Symptom | Usual cause | What to do |
|---|---|---|
| *"Không có quyền sửa cấu hình mạng"* ("No permission to change network settings") | No Admin rights, or antivirus locks the hosts file | Run as Admin; turn the antivirus off for a while if it concerns hosts |
| *"Cổng DNS 53 … đang bị chương trình khác dùng"* ("DNS port 53 … is being used by another program") | Other software holds port 53 | Close that software, turn Định tuyến DNS (DNS routing) on again |
| *"Một phần mềm khác … đang định tuyến DNS …"* ("Other software … is routing DNS …") | A VPN/Tailscale already has an NRPT rule for the Steam domains | LunaDNS does not change other people's rules; turn that rule off, or use the VPN instead of LunaDNS |
| *"Không còn cổng trống cho Proxy SNI"* ("No free port left for Proxy SNI") | All of 18080–18090 are busy | Free a port |
| Box *"Thay cấu hình proxy hiện có?"* ("Replace the existing proxy settings?") | The PC has a PAC or manual proxy | Agree (the old settings are put back when you turn it off) or Huỷ (Cancel) |
| *"Cấu hình proxy hiện có ở dạng lạ"* ("The existing proxy settings are in an unusual form") | `AutoConfigURL` is not a string | Fix it by hand in the registry, then turn it on again |
| *"Không đọc được cấu hình của người dùng đang đăng nhập"* ("Cannot read the signed-in user's settings") | The user hive is not loaded | Open the app with the account that is signed in |
| *"File hosts dùng bảng mã … không sửa an toàn"* ("The hosts file uses encoding … not safe to edit") | hosts is saved as UTF-16 | Save it again as UTF-8 or ANSI |
| *"Windows từ chối thêm luật định tuyến DNS"* ("Windows refused to add the DNS routing rule") | NRPT/CIM error | Run as Admin; see the log |
| Kiểm tra (Check) still says "Bị lọc SNI" (SNI filtered) after turning it on | Kiểm tra measures the original network | Try opening Steam for real |
| Steam Store goes black | MidSni is selected | Choose FirstByte in Nâng cao (Advanced), turn Proxy SNI off and on |
| Network/Steam broken after the app was killed | Changes left behind | [Uninstall and cleanup](07-uninstall-and-cleanup.md) |
| macOS: the card says *"Lỗi không xác định"* (unknown error) or a sentence with *"Bấm Chẩn đoán"* (press Diagnose) | The background service failed without a prepared reason | Press **Chẩn đoán** and send the report: the background service's log is at the end |
| macOS: the report says *"Dịch vụ nền là bản cũ, chưa gửi được log"* (the background service is old and cannot send its log) | The background service is older than the app | Press **Cài lại dịch vụ nền** (Reinstall background service), then run Diagnose again |
| *"… gặp lỗi lặp lại nên đã được tắt"* ("… kept failing, so it was turned off") | The service died 3 times in 1 minute | Read the log, then turn it on again; if you see *"chưa hoàn tác hết"* ("not everything was undone"), run `--cleanup` |
| Opening the app a second time shows no new window | The old copy is in the tray | Click the tray icon |
| Log shows garbled text in PowerShell 5.1 | The log has no BOM | `Get-Content … -Encoding UTF8` |
| `--cleanup` does nothing | The app is running | Quit the app first |
| Setup says *"Không khôi phục được cấu hình mạng của bản LunaDNS đang cài"* ("Could not restore the network settings of the installed LunaDNS") | `--cleanup` of the old version still has failing entries | Open the old LunaDNS, click "Tắt hết & khôi phục" (Turn all off & restore), quit, run Setup again; if it still fails, read the log and [clean up by hand](07-uninstall-and-cleanup.md#when---cleanup-is-not-enough-manual-only-if-needed) |
| Setup says *"… chưa đăng ký được tác vụ dọn dẹp lúc khởi động máy"* ("… could not register the cleanup task at PC startup") | `schtasks` failed (PC policy, antivirus) | The app still works, only the automatic cleanup is missing; read the log, run Setup again |
| The uninstaller says *"Chưa khôi phục được hết cấu hình mạng"* ("Could not restore all network settings") | Some entries are not undone yet | [Uninstall the Setup version](07-uninstall-and-cleanup.md#uninstall-the-setup-version) |
| You can open the log folder but cannot edit/delete files | By design: only Admin can write | Open Notepad/PowerShell as Admin ([why](07-uninstall-and-cleanup.md#the-data-folder-is-editable-only-by-admin)) |
| `--install-system` exits with code 3 | The exe is not in Program Files | Normal for the portable version; use Setup if you want the cleanup task |
| SmartScreen blocks / Smart App Control blocks | The exe is not signed | [Install](01-install.md) |

## Known limits

- MidSni does not get through on the network tested; FirstByte is the default.
- PUBG is not supported.
- Sign-out/shutdown: the undo takes place within 4 seconds, not reliably measured (the Setup version has the cleanup task to make up for it).
- The exe is not signed. The app tells you about a new version but does not download and install it by itself.

Reporting a bug: attach the `--report` report and describe the steps to reproduce it.
