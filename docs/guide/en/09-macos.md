# 9. macOS (experimental)

<sub>[Tiếng Việt](../09-macos.md)</sub>

The macOS version consists of a **background service** (launchd), the `lunadns` **command** and the LunaDNS **interface** (the same Avalonia interface as the Windows version). It shares its core with the Windows version (DoH, DNS stub, SNI proxy, journal). There are `.dmg` files for Apple Silicon and Intel Macs, ad-hoc signed and **not notarized** (there is no Apple Developer ID yet). There is no PUBG mode.

## Install from the .dmg file

Download from the Release page: `LunaDNS-<version>-macos-arm64.dmg` (Apple Silicon, M1 and later) or `…-macos-x64.dmg` (Intel Mac), together with `SHA256SUMS-macos.txt` (check with `shasum -a 256 -c SHA256SUMS-macos.txt`). macOS 12 or later is required.

1. Open the `.dmg` and drag **LunaDNS** into **Applications**. Do not run it straight from the `.dmg` window: macOS would run the app from a temporary read-only path (App Translocation).
2. Open LunaDNS from Applications. Because it is not notarized, macOS says it cannot open the app the first time. From macOS 15, "right-click → Open" no longer bypasses this warning. There are two ways:
   - **System Settings → Privacy & Security**, scroll down, click **Open Anyway** next to the line about LunaDNS, enter your password, then open the app again.
   - Or in Terminal: `xattr -dr com.apple.quarantine /Applications/LunaDNS.app`

   Only do this if you trust where you downloaded it from.
3. Click **Cài dịch vụ nền** (Install background service) on the banner and enter your password (see the Interface section). The install command only copies the folder `LunaDNS.app/Contents/MacOS/daemon/` to `/Library/PrivilegedHelperTools/vn.lunavo.lunadns/` and removes the quarantine flag on the copy, so launchd can run the background service.

To uninstall: `sudo /Library/PrivilegedHelperTools/vn.lunavo.lunadns/lunadns uninstall` (or `sudo lunadns uninstall` if the Mac has `/usr/local/bin`), then drag LunaDNS from Applications to the Trash.

## How it works

| Feature | Windows | macOS |
|---|---|---|
| DNS routing | NRPT rule → `127.0.0.1:53` | one `/etc/resolver/<domain>` file per Steam domain → `127.0.0.1:15353` (the `port` directive) |
| SNI proxy | the user's `AutoConfigURL` | `networksetup -setautoproxyurl` on each active network service **that has a real card** (Wi-Fi, Ethernet...). VPN services are not touched |
| Hosts lines (PUBG) | hosts file | `/etc/hosts` (written to `/private/etc/hosts`, keeping the symlink and the 644 permissions) |
| Flush DNS cache | `DnsFlushResolverCache` | `dscacheutil -flushcache` + `killall -HUP mDNSResponder` |
| Data, journal, log | `%ProgramData%\LunaDNS` | `/Library/Application Support/LunaDNS` (root, permissions 700) |

Each LunaDNS file in `/etc/resolver` has `# LunaDNS <id>` as its first line. LunaDNS **never edits or deletes a file without this mark**: a domain that already has a file from a VPN or a self-made script is skipped.

## Run from source

You need the .NET 8 SDK. Install it without root into `~/.dotnet`:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && bash dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet
```

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
```

Build a runnable version:

```bash
dotnet publish src/LunaDns.MacCli -c Release -r osx-arm64 --self-contained -o artifacts/publish/osx-arm64
```

(On an Intel Mac, change `osx-arm64` to `osx-x64`.)

## Interface

```bash
build/run-macos.sh
```

The script builds `artifacts/run/osx-arm64/LunaDNS.app` (unsigned, only to run on the machine that built it) and then opens it. Use `--build-only` to only build. Inside the `.app`, the `lunadns` command is at `Contents/MacOS/daemon/`.

The menu bar icon is a black transparent **template** icon (macOS tints it to fit a light/dark menu bar). The three states differ by shape: **faded** = off, **solid** = on, **with a round dot** = needs attention.

The window has the same four zones as on Windows (Kiểm tra, Trạng thái, Cập nhật, Gỡ & dọn; see [The four zones of the window](03-switches.md#the-four-zones-of-the-window)). Differences from the Windows version:

- **The interface runs with user rights and does not change system settings itself.** Every on/off action and the status go through the background service (socket `/var/run/lunadns.sock`). The check (probe) still runs inside the interface.
- **Background service not installed:** the interface shows a banner with the **Cài dịch vụ nền** (Install background service) button. This button opens the macOS password box (`osascript … with administrator privileges`) and then runs the `lunadns install` that ships inside the `.app`. If it is installed but not answering, or the background service is older than the app (usual right after upgrading the app), the banner shows **Cài lại dịch vụ nền** (Reinstall background service). The two switches stay grey until the background service answers.
- **Several accounts on one Mac (Fast User Switching):** only the person sitting at the machine can control LunaDNS. The app of an account running in the background shows the banner "Tài khoản khác đang dùng LunaDNS trên máy này" (Another account is using LunaDNS on this Mac), **with no** Reinstall button (a reinstall would turn off the other person's routing), and the two switches stay grey. Switch back to that account and the app can control LunaDNS again within a few seconds. Older background services are recognised too.
- The interface asks the background service every 2 seconds while the window is open, so switching on/off with `lunadns on|off` in Terminal also shows up in the interface. With the window closed (only the menu bar icon left) or minimised it asks every 15 seconds; reopening the window asks right away.
- The DNS routing card shows the DoH server that is answering (*"Đang bật · DoH: Cloudflare"*, see [DNS routing](03-switches.md#dns-routing-định-tuyến-dns)). An older background service does not send this: the card only shows *"Đang bật"* (On).
- **Thoát & khôi phục** (Quit & restore) (menu bar, or Cmd+Q, or Quit in the Dock) sends the turn-everything-off command to the background service, meaning the same as the button on Windows, and the background service forgets the remembered state, so the next start does not [turn anything back on](03-switches.md#turn-back-on-automatically-tự-bật-lại). This is true even when a feature was turned on with `lunadns on`. Signing out or shutting down also turns everything off but **keeps** the remembered state (the app reads the quit reason macOS sends). To keep LunaDNS running, only close the window (red button): the app stays as an icon in the menu bar.
- The **Tự bật lại sau khi khởi động máy hoặc cài lại dịch vụ nền** (Turn back on after a restart or a background service reinstall) box in the Status zone is saved as `AutoResume` in `/Library/Application Support/LunaDNS/settings.json`; the remembered state is `Wanted`. If the box is missing, the background service is an old version: click **Cài lại dịch vụ nền** (Reinstall background service).
- **Cài lại dịch vụ nền** while features are on: the new background service turns them back on. When upgrading from an old version that does not remember (0.10.0 or earlier), the app takes a snapshot before installing and turns things back on afterwards; if the Turn back on box is off, the app shows a band with a **Bật lại** (Turn back on) button.
- The **Find a configuration that works** button is available on macOS: the background service checks, applies and saves the result. An older background service must be reinstalled (**Cài lại dịch vụ nền**) before it can apply. There is no custom DoH field.
- The **Tự cập nhật danh sách tên miền** (Auto-update domain list) row (in the **Cập nhật** zone) is available on macOS: the background service verifies the signature, applies and saves the list (`DomainListUpdates` lives in `/Library/Application Support/LunaDNS/settings.json`). If the row is missing, the background service is an older version: click **Cài lại dịch vụ nền** (Reinstall background service).
- The **Kiểm tra** (Check) zone has no **Nâng cao** (Advanced) section yet (split type, delay, DoH): the background service uses the default settings.
- There is no **Gỡ khỏi máy** (Remove from this PC) button: the **Gỡ & dọn** (Remove & clean up) zone shows a line telling you to uninstall with `sudo lunadns uninstall`.
- The interface's settings and log are in `~/Library/Application Support/LunaDNS` (with `gui.lock` and `gui.sock` so that only one interface runs; if you open it a second time, the running window comes up).
- **Chẩn đoán** (Diagnose) writes a report to the Desktop: LunaDNS's `/etc/resolver` files, the services using LunaDNS's PAC, the hosts lines, the background service version, the number of journal entries (the background service's journal belongs to root, so the interface cannot read its contents), the last 200 lines of the interface's log and **the last 200 lines of the background service's log** (the service has already masked the user name, machine name and LAN IPs). The message bar has a **Hiện trong Finder** (Show in Finder) button that opens the report file. An older background service cannot send its log yet: the report has one line asking you to **Cài lại dịch vụ nền** (Reinstall background service).
- The **Mở thư mục log** (Open log folder) button, next to Diagnose, opens `~/Library/Application Support/LunaDNS/logs`, the interface's log.
- When turning a feature on or off fails, the card and the message bar show the **specific reason** the background service sends back (for example the error text from `networksetup`), not only "Lỗi không xác định" (unknown error). If there is no specific reason, press **Chẩn đoán**: the background service's log is in the report.

## Background service (recommended)

On Windows, the app runs with Admin rights after UAC. On macOS, the part that needs root is split off into a **background service** (launchd daemon `vn.lunavo.lunadns`); the user's `lunadns` command talks to it through the Unix socket `/var/run/lunadns.sock`, so after installing you **do not need sudo** to turn things on/off.

```bash
sudo artifacts/publish/osx-arm64/lunadns install
```

This command copies the runnable build to `/Library/PrivilegedHelperTools/vn.lunavo.lunadns/` (only root can write there), creates `/Library/LaunchDaemons/vn.lunavo.lunadns.plist`, loads the daemon and creates the shortcut command `/usr/local/bin/lunadns` (if that folder already exists).

- Each time the daemon starts (including after a restart, a reinstall, or when the daemon dies) it **undoes any leftover journal**, opens the socket, then **turns back on** what the user last left on (unless the Turn back on box is off, or the undo was not clean). If that fails it tries again after 5 and then 15 seconds. It never replaces an existing PAC/proxy by itself: if the machine uses a PAC, the SNI proxy waits until the user clicks **Bật lại** (Turn back on) in the app. `lunadns status` shows a *"Tự bật lại: …"* line, and once DNS routing has answered at least one name, a *"DoH đang trả lời: Google (dự phòng)"* ("DoH answering: Google (fallback)") line.
- On shutdown or when the daemon is removed: launchd sends SIGTERM, and the daemon turns everything off and undoes it, but keeps the remembered state to turn it back on at the next start.
- Only **root** and **the person sitting at the machine** (the console user, also the owner of `/dev/console`) can control the daemon; connections from another account (SSH, another user) are refused, and the reply carries the code `not-console-user`. The UID comes from the kernel (`getpeereid`) and cannot be faked; the person at the machine is asked from macOS directly (`SCDynamicStoreCopyConsoleUser`), without starting a process.
- Data, journal, log: `/Library/Application Support/LunaDNS` (root, permissions 700); the daemon's logs are in `logs/daemon.out` and `logs/lunadns.log`. Normal users cannot open this folder; **Chẩn đoán** in the app fetches the tail of `lunadns.log` through the `get-log` socket command (personal data masked, at most 200 lines).

## Commands

| Command | What it does |
|---|---|
| `lunadns on` | Turns on DNS routing (through the background service). |
| `lunadns on --proxy` | Also turns on the SNI proxy. If the Mac already has an active PAC/manual proxy it refuses; add `--allow-proxy-override` to replace it temporarily. |
| `lunadns off` | Turns everything off and restores; nothing is turned back on at the next start. |
| `lunadns auto-resume on\|off` | Turns Turn back on (Tự bật lại) on or off (on by default). |
| `lunadns status` | Status. Without the background service, it reads LunaDNS's resolver files, PAC and hosts lines itself. |
| `sudo lunadns uninstall` | Turns off, restores, removes the background service, the shortcut command and the data folder. |
| `sudo lunadns run [--proxy]` | No background service installed: runs in the foreground, **Ctrl+C** to turn off and restore. Does not remember or turn anything back on. Cannot run while the background service is running. |
| `sudo lunadns cleanup` | Undoes every change still in the journal. Does not run while the background service or `run` holds the lock. |

Exit codes: `0` done · `1` some work not finished, or the background service is not running (see `/Library/Application Support/LunaDNS/logs`) · `2` wrong or missing arguments, or sudo missing.

Check after turning it on:

```bash
scutil --dns | grep -A3 steampowered
```

You should see `nameserver[0] : 127.0.0.1` and `port : 15353`. Note that `dig`/`nslookup` do **not** read `/etc/resolver`; test with `dscacheutil -q host -a name store.steampowered.com` or open Steam.

## Notes

- **Did you create `/etc/resolver/steampowered.com` by hand** (for example with a `steam-dns` script)? LunaDNS will skip that domain. Remove the hand-made file first, then run LunaDNS.
- **VPNs** (Surfshark, IPVanish...) when on may push their own DNS for every domain and override `/etc/resolver`. Tailscale MagicDNS also uses `/etc/resolver`, but only for the tailnet's domain names, and does not touch Steam.
- **SNI proxy:** it has not been verified whether Steam on macOS follows the system PAC for every connection. If the Store still does not load, the cause is usually SNI filtering on a connection that does not follow the PAC.
- `networksetup` has no command to delete a PAC URL: for a service that had no PAC to begin with, turning LunaDNS off only **disables** auto-proxy, and the URL `http://127.0.0.1:1808x/proxy.pac` stays behind in the disabled state (harmless). A service that had its own PAC gets its original URL and state back.
- A network service that appears after the SNI proxy was turned on (for example you plug in another card) will not have the PAC until the next time you turn it on.

## Packaging (.app, .dmg)

```bash
build/publish-macos.sh
```

Run it on macOS, no sudo needed. It builds `artifacts/publish/<rid>/LunaDNS.app` for `osx-arm64` and `osx-x64`, signs them ad-hoc, then creates `artifacts/LunaDNS-<v>-macos-arm64.dmg`, `…-macos-x64.dmg` (each has the app, an Applications shortcut, `HUONG-DAN.txt`) and `artifacts/SHA256SUMS-macos.txt`. Arguments: `--version x.y.z` (default is `<Version>` in `LunaDns.App.csproj`), `--rid osx-arm64|osx-x64|all`.

- The `.app` layout and the signing function are in `build/macos-bundle.sh` (shared with `run-macos.sh`). `Info.plist` is generated from there.
- There is no universal build: a self-contained .NET build has a runtime and assemblies that differ by architecture, and `lipo` cannot merge them.
- Ad-hoc signing signs each file in `Contents/MacOS` and then the bundle; the script checks with `codesign --verify --deep --strict`. `spctl` still says `rejected` because it is not notarized: this is as expected.
- Icon: `build/make-icns.sh` generates `Assets/LunaDNS.icns` and `Assets/tray-{off,on,error}-mac.png` from the same image source as the `.ico` (`build/icongen --macos`). Only re-run it when the image changes; the files are already committed.
- GitHub Actions: when a `v*` tag is pushed, the job `release-macos` (macos-latest) builds, tests and runs `publish-macos.sh`, then the Windows job attaches the two `.dmg` files and `SHA256SUMS-macos.txt` to the draft Release.
- Once there is an Apple Developer ID: replace `codesign -s -` with a Developer ID signature plus `--options runtime` and the entitlements for .NET's JIT, then run `xcrun notarytool submit --wait` and `xcrun stapler staple` on the `.dmg`.

## Integration tests (run by hand)

Unit tests run on a temporary folder, a fake `networksetup` and a fake background service:

```bash
dotnet test tests/LunaDns.Platform.MacOS.Tests
```

```bash
dotnet test tests/LunaDns.App.Tests -f net8.0
```

(`LunaDns.App.Tests` has two targets: `net8.0` is the macOS build, `net8.0-windows` is the Windows build and only runs on Windows.)

Tests that touch the real system only run when you set the variable and use sudo; each test applies and then undoes, comparing the state before and after:

Build as a normal user first, and only then run with sudo and `--no-build`, so that `obj/`, `bin/` and `~/.dotnet` are not changed to root ownership (the next build would fail on permissions):

```bash
dotnet build tests/LunaDns.Platform.MacOS.Tests
```

```bash
sudo env PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 LUNADNS_ADMIN_TESTS=1 dotnet test tests/LunaDns.Platform.MacOS.Tests --no-build --filter Category=MacAdmin
```

If you ran it with sudo without `--no-build` by mistake, give ownership back: `sudo chown -R "$USER" ~/.dotnet src tests`.

They cover: the resolver file appearing in `scutil --dns` and then disappearing; `/etc/hosts` being byte-for-byte identical after one round; each service's auto-proxy returning to exactly its on/off state (and its URL, if it had one before).
