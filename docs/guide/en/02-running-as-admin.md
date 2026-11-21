# 2. Running as Admin, system tray

<sub>[Tiếng Việt](../02-chay-va-quyen-admin.md)</sub>

## Admin rights

`LunaDNS.exe` has the manifest `requireAdministrator`: double-click it (or open it from the Start menu) and Windows asks with UAC. You do **not** need to right-click → "Run as administrator". Admin is needed because the app changes NRPT rules, the hosts file and the signed-in user's proxy settings.

Without the rights, a feature shows: *"Không có quyền sửa cấu hình mạng. Hãy chạy LunaDNS bằng quyền Admin."* ("No permission to change network settings. Run LunaDNS as Admin."). Only when using the command line (see [Command line](06-command-line.md)) do you need to open PowerShell with "Run as administrator".

## Opening the app

- First open: the app cleans up changes left over from last time (if any), shows its window and **runs Kiểm tra** (Check) by itself.
- If old changes were undone, the window shows *"Đã hoàn tác N thay đổi còn sót từ lần chạy trước."* ("Undid N changes left over from the previous run."). If not everything could be undone: *"Còn thay đổi từ lần chạy trước chưa hoàn tác được. Bấm "Chẩn đoán" để ghi báo cáo."* ("Some changes from the previous run could not be undone. Click "Chẩn đoán" (Diagnose) to write a report.")
- **Only one copy runs at a time** (mutex `Global\LunaDNS`). If you open it a second time, no new copy starts; the existing window is brought up instead.

## System tray

- The **X** button only shrinks the window to the tray; the app and the switches keep running.
- Left-click the tray icon, or use the menu **Mở LunaDNS** (Open LunaDNS): shows the window again.
- Menu **Thoát & khôi phục** (Quit & restore): turns off every service, undoes all changes, then exits. If some items could not be undone, the app says *"Chưa hoàn tác hết thay đổi. Hãy mở lại LunaDNS hoặc chạy LunaDNS.exe --cleanup."* ("Not all changes were undone. Open LunaDNS again or run LunaDNS.exe --cleanup.")

## Sign-out, shutdown

The app tries to undo everything within 4 seconds when the session ends. This time has not been reliably measured on every PC. If it is not fast enough, the changes stay in the journal and are undone by the cleanup task at PC startup/sign-in (Setup version), the next time the app is opened, or with `--cleanup`. In the meantime Steam may not be able to resolve its domain names.

## Notes

- It does not start with Windows (there is no start-with-PC option yet).
- When the app is ended with Task Manager (kill), the changes are **not** undone right away: see [Uninstall and cleanup after a crash](07-uninstall-and-cleanup.md).
- On every run (including the command line), the app locks down the permissions of `%ProgramData%\LunaDNS`: only SYSTEM and Administrators can write to it. If it cannot lock it, the app writes a warning to the log and keeps running.
