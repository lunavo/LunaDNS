# 7. Uninstall and cleanup after a crash

<sub>[Tiếng Việt](../07-go-va-don-dep.md)</sub>

## Uninstall the Setup version

Choose one:

- **Settings → Apps → LunaDNS → Uninstall.** If LunaDNS is running, the uninstaller asks you to quit the app first.
- The **Gỡ khỏi máy** (Remove from this PC) button in the app's **Gỡ & dọn** (Remove & clean up) zone (only shown in the Setup version): the app asks for confirmation, opens the uninstaller, then quits itself with "Thoát & khôi phục" (Quit & restore).

The uninstaller runs `LunaDNS.exe --uninstall`: it undoes every change, deletes the cleanup task and `%ProgramData%\LunaDNS`, and only then removes the app. If some changes cannot be undone, the uninstaller **stops, with nothing removed**, and says *"Chưa khôi phục được hết cấu hình mạng (mã …)"* ("Could not restore all network settings (code …)"). In that case:

1. Open LunaDNS, click **Tắt hết & khôi phục** (Turn all off & restore) in the **Gỡ & dọn** zone, then quit with **Thoát & khôi phục** (Quit & restore).
2. Look at the log `C:\ProgramData\LunaDNS\logs\lunadns.log` (command below) to see which entry failed; clean up by hand if needed ([When `--cleanup` is not enough](#when---cleanup-is-not-enough-manual-only-if-needed)).
3. Uninstall again.

## Uninstall the portable version

The portable version has no **Gỡ khỏi máy** button; the **Gỡ & dọn** zone shows a line summarising the steps below.

1. Quit the app with the tray menu **Thoát & khôi phục** (Quit & restore).
2. Open PowerShell with **Run as administrator**, run `--uninstall` and check the exit code (0 = clean):

```powershell
$p = Start-Process C:\Tools\LunaDNS\LunaDNS.exe -ArgumentList '--uninstall' -Wait -PassThru
$p.ExitCode
```

3. Delete the folder that holds the exe.

Code 4 means the app is still running; for code 1 see [Command line](06-command-line.md#--uninstall).

## Cleanup task (Setup version)

Setup registers the task `\LunaDNS\Cleanup` in Task Scheduler: it runs `C:\Program Files\LunaDNS\LunaDNS.exe --cleanup` as the SYSTEM account **at PC startup** and **when someone signs in**. This way, if the app is killed or the PC loses power, the network settings go back to normal the next time the PC starts.

- At PC startup, the user's proxy settings (`AutoConfigURL`) cannot be changed yet because Windows has not loaded the user profile; that entry is kept and undone at the sign-in run. NRPT rules and hosts are undone right away.
- If LunaDNS is running, the task does nothing.
- Uninstalling with Setup removes the task automatically. To remove it by hand (PowerShell as Admin):

```powershell
schtasks /Delete /TN \LunaDNS\Cleanup /F
```

The portable version does not have this task.

## App killed, frozen, or power lost

The system changes stay behind: the NRPT rule, `AutoConfigURL`, the hosts lines. Symptom: Steam does not open because its domain names point to `127.0.0.1:53` with no stub running, or the PAC points to a dead port.

Ways to clean up, choose one:

1. **Setup version:** restart the PC and sign in; the cleanup task undoes it automatically (see above).
2. **Open LunaDNS again as Admin:** at startup the app undoes the leftover entries (*"Đã hoàn tác N thay đổi còn sót…"*, "Undid N leftover changes…").
3. **Admin command line:** `LunaDNS.exe --cleanup` (exit code 0 = clean). To see the log:

```powershell
Get-Content C:\ProgramData\LunaDNS\logs\lunadns.log -Tail 20 -Encoding UTF8
```

Both rely on `journal.json`, so they only work while that file still exists.

## Check that it is clean (PowerShell as Admin)

```powershell
# Luật NRPT của LunaDNS (Comment bắt đầu bằng "LunaDNS ")
# LunaDNS's NRPT rules (Comment starts with "LunaDNS ")
Get-DnsClientNrptRule | Where-Object Comment -like 'LunaDNS *'
# PAC
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name AutoConfigURL -ErrorAction SilentlyContinue
# hosts
Select-String -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Pattern '# LunaDNS'
```

If all three show nothing (and `AutoConfigURL` is not `http://127.0.0.1:.../proxy.pac`), it is clean.

## When `--cleanup` is not enough (manual, only if needed)

- **Orphaned NRPT rules** (no longer in the journal, for example `journal.json` was lost): `--cleanup` does **not** remove these. Remove by hand only the rules whose Comment is `LunaDNS …`, and **never** touch the rules without a Comment that belong to Tailscale/VPN:

```powershell
Get-DnsClientNrptRule | Where-Object Comment -like 'LunaDNS *' | Remove-DnsClientNrptRule -Force
ipconfig /flushdns
```

- **hosts:** delete the lines with `# LunaDNS` in `C:\Windows\System32\drivers\etc\hosts` (open Notepad as Admin).
- **PAC:** if `AutoConfigURL` is exactly LunaDNS's PAC, reset it in Settings → Network & Internet → Proxy, or delete that value.
- A damaged `journal.json` is renamed `journal.json.corrupt-<time>`; in that case you have to clean up by hand as above.

## The data folder is editable only by Admin

`%ProgramData%\LunaDNS` can be opened and viewed in Explorer (Users can read), but only SYSTEM and Administrators can write to it. This is on purpose: the cleanup task runs as SYSTEM and reads `journal.json`. To edit or delete files in there by hand, use Notepad/PowerShell opened as Admin.
