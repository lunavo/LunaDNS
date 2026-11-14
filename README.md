<p align="center"><img src="docs/assets/lunadns-mark.svg" width="96" alt="LunaDNS logo" /></p>

# LunaDNS

<p align="center"><b>English</b> · <a href="README.vi.md">Tiếng Việt</a></p>

LunaDNS is a Windows and macOS app that gets you into **Steam** when your ISP poisons DNS or filters TLS by SNI. It runs in the system tray / menu bar, and every change it makes to the system is journaled so it can be undone.

> **Note:** the app's interface and the detailed guides in `docs/guide/` are in **Vietnamese** (the app is built for players in Vietnam). This README is the English overview.

> **Not a developer?** Download `LunaDNS-Setup-x.y.z.exe` (Windows) or `LunaDNS-x.y.z-macos-*.dmg` (macOS) from [Releases](../../releases). No .NET install needed.

> **Status: preview (prerelease).** Builds are not code-signed yet, so Windows SmartScreen and macOS Gatekeeper warn on first launch. See [What's missing](#whats-missing).

## Two features

| Switch | What it does | When you need it |
|---|---|---|
| **DNS routing** | Sends only Steam domains to a local DNS-over-HTTPS stub (Cloudflare, Google as fallback) instead of the ISP resolver. Windows: an NRPT rule → `127.0.0.1:53`. macOS: one `/etc/resolver/<domain>` file per Steam domain → `127.0.0.1:15353`. | Steam login fails, Steam domains resolve to wrong IPs (DNS poisoning). |
| **SNI proxy** | Starts a local proxy on `127.0.0.1:18080` (tries up to `18090` if busy) and points the system PAC at `http://127.0.0.1:18080/proxy.pac`. The proxy splits the TLS ClientHello so SNI filters miss it. Never decrypts TLS, installs no certificates. | Steam Store / Community still won't load even though DNS is correct. |

Only Steam domains are touched. Adapter DNS settings, LAN, other sites and game UDP traffic stay as they are. The proxy refuses non-Steam hosts, so it is never an open proxy.

## Requirements

- **Windows:** Windows 10 1809+ or Windows 11, 64-bit. Needs **Administrator** (the exe asks via UAC).
- **macOS:** Apple Silicon or Intel. Needs your admin password **once** to install the background service.
- Release builds bundle .NET. Only building from source needs the **.NET 8 SDK**.

## Install on Windows

- **Setup (recommended):** `LunaDNS-Setup-x.y.z.exe` installs to `C:\Program Files\LunaDNS`, registers a boot-time cleanup task, and can be removed from Settings → Apps.
- **Portable:** `LunaDNS-x.y.z-portable.zip`: unzip and double-click `LunaDNS.exe`. No cleanup task.
- Each release ships `SHA256SUMS.txt` to verify downloads.

## Install on macOS (preview)

Download `LunaDNS-x.y.z-macos-arm64.dmg` (Apple Silicon, M1–M4) or `…-macos-x64.dmg` (Intel). Check your chip under  → About This Mac.

1. Open the `.dmg` and drag **LunaDNS** into **Applications**.
2. Open LunaDNS. The build is not notarized yet, so macOS refuses the first time: go to **System Settings → Privacy & Security**, scroll down, click **Open Anyway**, then open the app again.
3. When the app says the background service is not installed, click **Cài dịch vụ nền** (Install background service) and enter your Mac password.
4. Turn on **Định tuyến DNS** (DNS routing), then open Steam.

To stop: menu bar icon → **Thoát & khôi phục** (Quit & restore). To uninstall completely, run this in Terminal, then drag the app to the Trash:

```bash
sudo /Library/PrivilegedHelperTools/vn.lunavo.lunadns/lunadns uninstall
```

Verify the download with `shasum -a 256 ~/Downloads/LunaDNS-*.dmg` against `SHA256SUMS-macos.txt`.

How it works on macOS: a launchd daemon (`vn.lunavo.lunadns`, root) owns the DNS stub, proxy and journal; the app and the `lunadns` command talk to it over `/var/run/lunadns.sock`. Only root and the user at the console may control it (UID checked with `getpeereid`). On every start the daemon rolls back anything left in the journal, so a crash or power loss never leaves Steam DNS stuck. Details (Vietnamese): [docs/guide/09-macos.md](docs/guide/09-macos.md).

## Command line

**Windows** (`LunaDNS.exe`, run elevated):

| Command | Effect |
|---|---|
| `LunaDNS.exe` | Open the UI. Single instance; a second launch brings the window forward. |
| `--cleanup` | Undo everything left in the journal, then exit. Use after a crash. |
| `--report` | Write a diagnostics report (personal info redacted), then exit. |
| `--install-system` | Called by Setup: locks down the data folder ACL and registers the cleanup task. Only runs from Program Files. |
| `--uninstall` | Called by the uninstaller: undo all changes, remove the cleanup task and data folder. |

Exit codes: `0` done · `1` something left undone, see log · `2` bad arguments · `3` `--install-system` outside Program Files · `4` `--uninstall` while LunaDNS is running.

**macOS** (`lunadns`):

| Command | Effect |
|---|---|
| `lunadns on [--proxy] [--allow-proxy-override]` | Turn on DNS routing (and the SNI proxy) through the daemon. No sudo. |
| `lunadns off` | Turn everything off and restore. |
| `lunadns status` | Show what LunaDNS has changed. |
| `sudo lunadns install` / `uninstall` | Install or remove the background service. |
| `sudo lunadns run [--proxy]` | Run in the foreground without the daemon; Ctrl+C restores. |
| `sudo lunadns cleanup` | Undo everything left in the journal. |

If `/usr/local/bin` does not exist (common on Apple Silicon without Homebrew), call it as `/Library/PrivilegedHelperTools/vn.lunavo.lunadns/lunadns`.

## Data and logs

| | Windows | macOS |
|---|---|---|
| Folder | `%ProgramData%\LunaDNS` | `/Library/Application Support/LunaDNS` |
| Journal | `journal.json`: system changes currently applied, for rollback | same |
| Logs | `logs\lunadns.log`, 1 MB per file, 5 files | `logs/lunadns.log`, `logs/daemon.out` |
| Protection | ACL: only SYSTEM and Administrators can write | owned by root, mode `700` |

The folder is locked down because the privileged cleanup reads `journal.json`: whoever can write the journal could steer what SYSTEM/root changes. A pre-existing folder that others can write to is quarantined and recreated.

## Build from source

Windows: double-click `Chay-tu-ma-nguon.cmd`, or:

```powershell
git clone https://github.com/lunavo/LunaDNS.git lunadns
cd lunadns
pwsh build/publish.ps1
```

macOS:

```bash
build/publish-macos.sh
```

This produces `artifacts/LunaDNS-<version>-macos-{arm64,x64}.dmg` (ad-hoc signed) and `SHA256SUMS-macos.txt`.

Tests:

```bash
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.App.Tests
dotnet test tests/LunaDns.Platform.Windows.Tests   # Windows
dotnet test tests/LunaDns.Platform.MacOS.Tests     # macOS
```

Tests that change the real system are skipped unless `LUNADNS_ADMIN_TESTS=1` is set and run elevated. See the guides for how to run them by hand.

Layout: `src/LunaDns.Core` (platform-independent logic: probing, DoH stub, SNI proxy, journal, features), `src/LunaDns.Platform.Windows` (NRPT, hosts, WinINet proxy, ACL, cleanup task), `src/LunaDns.Platform.MacOS` (`/etc/resolver`, `networksetup`, `/etc/hosts`, daemon protocol), `src/LunaDns.MacCli` (`lunadns` command and daemon), `src/LunaDns.App` (Avalonia UI; `net8.0-windows` on Windows, `net8.0` on macOS). Packaging in `build/`, CI in `.github/workflows/`, design notes in `docs/superpowers/`.

## What's missing

- **Code signing:** Windows exe is not Authenticode-signed; macOS builds are ad-hoc signed and not notarized.
- **Auto-update:** not yet. "Check for updates" only reports a new version and opens the download page.
- **PUBG mode:** not supported.
- **Diagnostics report** is written to the Desktop for now.
- **macOS:** no PUBG mode or Advanced section yet. Not yet confirmed whether every Steam connection on macOS honours the system PAC.
- Sign-out/shutdown timing is not fully measured. If the app cannot restore in time, the cleanup task (Windows Setup) or daemon (macOS), the next launch, or `--cleanup` will.

## Guides (Vietnamese)

1. [Install](docs/guide/01-cai-dat.md)
2. [Running as admin, system tray](docs/guide/02-chay-va-quyen-admin.md)
3. [The switches](docs/guide/03-ba-cong-tac.md)
4. [Check and apply suggestions](docs/guide/04-kiem-tra-va-de-xuat.md)
5. [Advanced](docs/guide/05-nang-cao.md)
6. [Command line](docs/guide/06-dong-lenh.md)
7. [Uninstall and cleanup after a crash](docs/guide/07-go-va-don-dep.md)
8. [Troubleshooting](docs/guide/08-su-co.md)
9. [macOS](docs/guide/09-macos.md)

## License

[MIT](LICENSE). Third-party notices: [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md).
