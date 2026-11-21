# 1. Install

<sub>[Tiếng Việt](../01-cai-dat.md)</sub>

There are two ways. Both are downloaded from the LunaDNS Releases page on GitHub, and both already include .NET (you do not need to install a runtime):

| | Setup `LunaDNS-Setup-x.y.z.exe` | Portable `LunaDNS-x.y.z-portable.zip` |
|---|---|---|
| Install location | `C:\Program Files\LunaDNS` | A folder you choose |
| Cleanup task at PC startup | Yes | No |
| Uninstall | Settings → Apps, or the **Gỡ khỏi máy** (Remove from this PC) button | Manual (see [Uninstall and cleanup](07-uninstall-and-cleanup.md)) |

Use Setup if you can. The exe is not digitally signed, so Windows shows a warning the first time (see below).

## Check the downloaded file (SHA-256)

Each release has a `SHA256SUMS.txt`. In PowerShell, in the folder that holds the downloaded file:

```powershell
Get-FileHash .\LunaDNS-Setup-0.10.0.exe -Algorithm SHA256
```

The `Hash` string must match that file's line in `SHA256SUMS.txt` (upper and lower case do not matter). If it does not match, do not run the file; download it again.

## Install with Setup

1. Double-click `LunaDNS-Setup-x.y.z.exe`.
2. If **"Windows protected your PC"** (SmartScreen) appears: click **More info → Run anyway**.
3. The UAC box says **"Unknown publisher"**: click **Yes** (because the exe is not signed).
4. Follow Setup (its screens are in English). On the last screen, tick "Mở LunaDNS" (Open LunaDNS) if you want to open the app right away.

Setup copies the app to `C:\Program Files\LunaDNS`, locks down the permissions of `%ProgramData%\LunaDNS`, and registers the cleanup task `\LunaDNS\Cleanup` (runs as SYSTEM at PC startup and at sign-in, see [Uninstall and cleanup](07-uninstall-and-cleanup.md#cleanup-task-setup-version)). Open the app from the Start menu: **LunaDNS**.

## Use the portable version

1. **Before extracting:** right-click the zip file → **Properties** → tick **Unblock** → OK. If you skip this, Explorer marks every extracted file as "downloaded from the Internet" and Windows warns you each time.
2. Extract into a folder of its own, for example `C:\Tools\LunaDNS\` (keep the `.dll` files next to the exe).
3. Double-click `LunaDNS.exe` and accept the UAC prompt (see [step 2](02-running-as-admin.md)).

Unlike the Setup version, there is **no cleanup task at PC startup**. If the app is killed or the PC loses power while a feature is on, you have to open the app again or run `--cleanup` (see [Uninstall and cleanup](07-uninstall-and-cleanup.md)). `--install-system` also refuses to run from a portable folder (code 3): the task runs as SYSTEM, and if the exe sits where a normal user can write, anyone who swaps the exe gets SYSTEM rights.

## Smart App Control (Windows 11)

If Smart App Control is on, Windows 11 may **block** an unsigned exe completely, with no "Run anyway" button. Check it in Windows Security → App & browser control → Smart App Control. LunaDNS is not signed in this version. Turning Smart App Control off is your decision; read Windows' warning on that page carefully before you do.

## Upgrade

Run the new Setup; you do not need to uninstall the old version first. Before overwriting, Setup runs `--cleanup` of the installed version to restore the network settings the old version changed. If this step fails, Setup stops and tells you; see [Troubleshooting](08-troubleshooting.md). LunaDNS must be closed (Setup reminds you if the app is still running).

The app checks for a new version about 10 seconds after it opens; if there is one, it shows a banner with a **Tải** (Download) button (opens the Releases page). The **Kiểm tra cập nhật** (Check for updates) button in the **Cập nhật** (Update) zone, next to the line *"Đang dùng LunaDNS x.y.z"* ("Using LunaDNS x.y.z"), checks right when you click it: if there is a new version it asks whether to open the download page, otherwise it says you are on the latest version. The app never downloads or installs by itself: download the new Setup and run it.

## Build from source

You need the **.NET 8 SDK** (`dotnet --version` shows `8.x`).

- Double-click `Chay-tu-ma-nguon.cmd` in the repo root: it builds the release into `artifacts\publish\win-x64\` and then opens `LunaDNS.exe`. If the SDK is missing, the script prints a `winget` command to install it.
- Or run `pwsh build/publish.ps1` by hand (it also produces the portable zip; if Inno Setup 6 is installed, it also produces the Setup and `SHA256SUMS.txt` in `artifacts\`).

Check the build:

```powershell
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.Platform.Windows.Tests
dotnet test tests/LunaDns.App.Tests -f net8.0-windows
```

Some tests touch the real network or need Admin; they are marked separately and skip themselves.

## Uninstall

See [Uninstall and cleanup](07-uninstall-and-cleanup.md).
