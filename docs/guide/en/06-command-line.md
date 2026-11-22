# 6. Command line

<sub>[Tiếng Việt](../06-dong-lenh.md)</sub>

Run from PowerShell/CMD **opened as Admin**. Arguments are not case-sensitive, and only **one** argument is accepted. The exe is a windowed application, so it **prints nothing to the console**: look at the exit code and the log for the result.

To wait and read the exit code in PowerShell (calling `.\LunaDNS.exe --cleanup` directly does not make the shell wait):

```powershell
$p = Start-Process .\LunaDNS.exe -ArgumentList '--cleanup' -Wait -PassThru
$p.ExitCode
```

| Command | Exit code |
|---|---|
| `LunaDNS.exe` | the interface |
| `--cleanup` | `0` = done or nothing to do; `1` = some items failed (kept in the journal) or the sweep failed |
| `--report` | `0` = written; `1` = the file could not be written |
| `--install-system` | `0` = done; `1` = error (see the log); `3` = the exe is not in Program Files, nothing changed |
| `--uninstall` | `0` = done; `1` = some work not finished (see the log); `4` = LunaDNS is running |
| any other / several arguments | `2` |

Every command (including opening the interface) locks down the permissions of `%ProgramData%\LunaDNS` before doing its work; if locking fails, only a warning is written to the log.

## --cleanup

Undoes every entry in `%ProgramData%\LunaDNS\journal.json` in reverse order, sweeps orphaned changes, then exits.

- If a copy of LunaDNS is running: it **does nothing** and exits with code 0 (log: *"LunaDNS đang chạy, bỏ qua."*, "LunaDNS is running, skipping."). Use "Thoát & khôi phục" (Quit & restore) in the app instead.
- **Sweep:** removes NRPT rules whose Comment is `LunaDNS …` and hosts lines `… # LunaDNS` that the journal no longer holds ("orphaned", for example because the journal was deleted or damaged). Rules without a Comment from Tailscale/VPN and other hosts lines are never touched. The PAC (`AutoConfigURL`) is not swept (see [Uninstall and cleanup](07-uninstall-and-cleanup.md#when---cleanup-is-not-enough-manual-only-if-needed)). This step runs even when there is no journal.
- Nothing to undo or sweep: code 0.
- The result is written to `logs\lunadns.log`: *"--cleanup: đã hoàn tác N mục, còn M mục lỗi; quét dọn gỡ K mục mồ côi, L loại lỗi."* ("--cleanup: undid N entries, M entries still failing; the sweep removed K orphaned items, L kinds failed.")

## --install-system

Setup calls this after copying the files; normally you do not need to run it by hand.

- Only runs when `LunaDNS.exe` is under `%ProgramFiles%` (the Setup version). Elsewhere (Downloads, a portable folder) it exits with code **3** and changes nothing: the task runs as SYSTEM, so the exe must be somewhere only Admin can write.
- Locks down the permissions of `%ProgramData%\LunaDNS`, then registers (or overwrites) the task `\LunaDNS\Cleanup`. Running it several times is fine.

## --uninstall

The Setup uninstaller calls this; for the portable version, run it by hand before deleting the folder.

1. If LunaDNS is running, it exits with code **4** and does nothing. Quit the app ("Thoát & khôi phục") and run it again.
2. Undoes every entry in `journal.json` and sweeps like `--cleanup`. If some entries still fail, it stops here with code **1**, keeping the journal and the cleanup task so the work can continue next time.
3. Deletes the task `\LunaDNS\Cleanup` (skipped if the portable version does not have it).
4. Deletes the folder `%ProgramData%\LunaDNS` (journal, settings, log). If it cannot be deleted: code **1**.

## Report

`LunaDNS.exe --report` (or the **Chẩn đoán** (Diagnose) button in the app) writes `LunaDNS-report-<yyyyMMdd-HHmmss>.txt`, UTF-8 with BOM.

- **For now it is written to the Desktop** and is not cleaned up automatically. A later version is planned to move it to `%ProgramData%\LunaDNS\reports\` (keeping the 10 most recent reports).
- `--report` runs Kiểm tra (Check) by itself for up to 20 seconds (if it runs out of time, the check section says "(chưa chạy)", "(not run)"). The Chẩn đoán button uses the most recent Kiểm tra result.
- Contents: version, Windows, check results, journal, LunaDNS's NRPT rules, `AutoConfigURL`, the `# LunaDNS` hosts lines, the last 200 log lines.
- **Personal information is masked:** user name, PC name, SID, CGNAT/Tailscale IPs (`100.64/10`), `169.254/16`, IPv6 ULA/link-local. `AutoConfigURL` is only written when it is LunaDNS's own loopback address; any other PAC is written only as "has another value". Still read it again before sending.
- `--report` does not start the app's services, so it undoes nothing, even when the app is running.
