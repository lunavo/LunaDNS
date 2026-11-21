<p align="center"><img src="docs/assets/lunadns-mark.svg" width="96" alt="LunaDNS logo" /></p>

# LunaDNS

<p align="center"><b>English</b> · <a href="README.vi.md">Tiếng Việt</a></p>

LunaDNS is a Windows and macOS app that gets you into **Steam** when your ISP poisons DNS or filters TLS by SNI. It runs in the system tray / menu bar, only touches the domains you route (the Steam group by default, plus optional groups and your own domains), and every change it makes to the system is journaled so it can be undone.

> **Status: preview (0.10.x, prerelease).** Builds are not code-signed yet, so Windows SmartScreen and macOS Gatekeeper warn on first launch. The app's interface is in Vietnamese; this documentation explains each label in English.

**Contents:** [For users](#for-users) · [How it works](#how-it-works) · [User guide](#user-guide) · [For developers](#for-developers) · [What's missing](#whats-missing)

## For users

### 1. Download

Open [Releases](https://github.com/lunavo/LunaDNS/releases), pick the newest version, scroll to **Assets**:

| Your computer | File | |
|---|---|---|
| Windows 10/11 (recommended) | `LunaDNS-Setup-x.y.z.exe` | Installer |
| Windows, no install | `LunaDNS-x.y.z-portable.zip` | **Not an installer**: extract the whole folder, then run `LunaDNS.exe` |
| Mac with Apple chip (M1–M4) | `LunaDNS-x.y.z-macos-arm64.dmg` | |
| Mac with Intel chip | `LunaDNS-x.y.z-macos-x64.dmg` | |

Do **not** pick `Source code (zip)`: that is the source for developers and has no app in it. No .NET install is needed.

### 2. Install and run

- **Windows Setup:** double-click it. On *"Windows protected your PC"* click **More info → Run anyway**; on the UAC prompt (*Unknown publisher*) click **Yes**.
- **Windows portable:** right-click the zip → Properties → **Unblock**, extract it (e.g. to `C:\Tools\LunaDNS`), double-click `LunaDNS.exe`. Running the exe from inside the zip window fails because the `.dll` files next to it are missing.
- **macOS:** drag LunaDNS into Applications and open it. When macOS refuses, go to **System Settings → Privacy & Security → Open Anyway**. Then click **Cài dịch vụ nền** (Install background service) and enter your password.

### 3. Use

The app runs **Kiểm tra** (Check) when it opens. Click **Áp dụng đề xuất** (Apply suggestions), or turn on **Định tuyến DNS** (DNS routing) and, if suggested, **Proxy SNI**. Then open Steam. Still blocked? Click **Tìm cấu hình hợp mạng** (Find a configuration that works): the app tries split point, delay and DoH server combinations and applies the first one that gets through.

The window has four zones: **Kiểm tra** (Check), **Trạng thái** (Status), **Cập nhật** (Update), **Gỡ & dọn** (Remove & clean up). See [The four zones of the window](docs/guide/en/03-switches.md#the-four-zones-of-the-window).

The X button only hides the window to the tray. To stop and undo everything: tray icon → **Thoát & khôi phục** (Quit & restore). When a new version is out, the app shows a banner with a **Tải** (Download) button.

Step-by-step guide and FAQ: **[Quick start](docs/guide/en/00-quick-start.md)**. Problems: **[Troubleshooting](docs/guide/en/08-troubleshooting.md)**.

## How it works

| Switch | What it does | When you need it |
|---|---|---|
| **Định tuyến DNS** (DNS routing) | Sends only the routed domains to a local DNS-over-HTTPS stub instead of the ISP resolver. Servers: Cloudflare (default), Google, Quad9, AdGuard or your own DoH URL, with two fallbacks and a **Đo** (Measure) button to pick the fastest. Windows: an NRPT rule → `127.0.0.1:53`. macOS: one `/etc/resolver/<domain>` file per domain → `127.0.0.1:15353`. | Steam login fails, Steam domains resolve to wrong IPs. |
| **Proxy SNI** (SNI proxy) | Starts a local proxy on `127.0.0.1:18080` (up to `18090` if busy) and points the system PAC at `http://127.0.0.1:18080/proxy.pac`. The proxy splits the TLS ClientHello so SNI filters miss it; for filters that reassemble TCP it can also send the ClientHello as two TLS records (**Tách TLS record**, Split TLS records). Never decrypts TLS, installs no certificates. | Steam Store / Community still won't load even though DNS is correct. |

Adapter DNS settings, LAN, other sites and game UDP traffic stay as they are. The proxy refuses hosts outside the routed domains, so it is never an open proxy.

**Domain list in three layers:** built in (always present), a signed update the app downloads from GitHub every 24 hours (it only adds domains, keeps the old list on any error, and can be switched off), and your own custom domains, which updates never overwrite. See [Domains](docs/guide/en/03-switches.md#domains). If the app is killed or the power goes out, the next launch, `LunaDNS.exe --cleanup`, the Windows cleanup task (Setup only) or the macOS daemon undoes the leftovers.

**Requirements:** Windows 10 1809+ / Windows 11 64-bit with Administrator rights (asked via UAC), or macOS 12+ on Apple Silicon or Intel with your admin password once.

## User guide

| | English | Tiếng Việt |
|---|---|---|
| 0 | [Quick start](docs/guide/en/00-quick-start.md) | [Bắt đầu nhanh](docs/guide/00-bat-dau-nhanh.md) |
| 1 | [Install](docs/guide/en/01-install.md) | [Cài đặt](docs/guide/01-cai-dat.md) |
| 2 | [Running as admin, system tray](docs/guide/en/02-running-as-admin.md) | [Quyền Admin, khay hệ thống](docs/guide/02-chay-va-quyen-admin.md) |
| 3 | [The switches](docs/guide/en/03-switches.md) | [Các công tắc](docs/guide/03-ba-cong-tac.md) |
| 4 | [Check and suggestions](docs/guide/en/04-check-and-suggestions.md) | [Kiểm tra và đề xuất](docs/guide/04-kiem-tra-va-de-xuat.md) |
| 5 | [Advanced](docs/guide/en/05-advanced.md) | [Nâng cao](docs/guide/05-nang-cao.md) |
| 6 | [Command line](docs/guide/en/06-command-line.md) | [Dòng lệnh](docs/guide/06-dong-lenh.md) |
| 7 | [Uninstall and cleanup](docs/guide/en/07-uninstall-and-cleanup.md) | [Gỡ và dọn](docs/guide/07-go-va-don-dep.md) |
| 8 | [Troubleshooting](docs/guide/en/08-troubleshooting.md) | [Xử lý sự cố](docs/guide/08-su-co.md) |
| 9 | [macOS](docs/guide/en/09-macos.md) | [macOS](docs/guide/09-macos.md) |

### Command line (Windows, elevated)

| Command | Effect |
|---|---|
| `LunaDNS.exe` | Open the UI. Single instance; a second launch brings the window forward. |
| `--cleanup` | Undo everything left in the journal, then exit. Use after a crash. |
| `--report` | Write a diagnostics report (personal info redacted) to the Desktop, then exit. |
| `--install-system` | Called by Setup. Only runs from Program Files. |
| `--uninstall` | Called by the uninstaller; run it by hand before deleting a portable folder. |

Exit codes: `0` done · `1` something left undone, see log · `2` bad arguments · `3` `--install-system` outside Program Files · `4` `--uninstall` while LunaDNS is running. macOS has the `lunadns on|off|status` command: see [macOS](docs/guide/en/09-macos.md).

### Data and logs

| | Windows | macOS |
|---|---|---|
| Folder | `%ProgramData%\LunaDNS` | `/Library/Application Support/LunaDNS` |
| Journal | `journal.json` | same |
| Logs | `logs\lunadns.log` (1 MB × 5) | `logs/lunadns.log`, `logs/daemon.out` |
| Protection | only SYSTEM and Administrators can write | root, mode `700` |

## For developers

```powershell
git clone https://github.com/lunavo/LunaDNS.git lunadns
cd lunadns
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
```

Or double-click `Chay-tu-ma-nguon.cmd` (Windows) to build and open the app. Needs the .NET 8 SDK.

| | English | Tiếng Việt |
|---|---|---|
| Projects, journal, features, platform layer | [Architecture](docs/dev/architecture.md) | [Kiến trúc](docs/dev/architecture.vi.md) |
| SDK, packaging, tests, admin tests, CI | [Building and testing](docs/dev/building-and-testing.md) | [Build và test](docs/dev/building-and-testing.vi.md) |
| Tags, draft release, publishing, signing | [Releasing](docs/dev/releasing.md) | [Phát hành](docs/dev/releasing.vi.md) |
| Bug reports and pull requests | [Contributing](CONTRIBUTING.md) | [Đóng góp](CONTRIBUTING.vi.md) |

Design specs and implementation plans (Vietnamese): `docs/superpowers/`.

## What's missing

- **Code signing:** Windows exe is not Authenticode-signed (Smart App Control on Windows 11 may block it outright); macOS builds are ad-hoc signed and not notarized.
- **Auto-update:** the app reports a new version and opens the download page; it does not download or install by itself.
- **PUBG mode:** not supported.
- **Diagnostics report** is written to the Desktop for now.
- **macOS:** no Advanced section yet (split point, delay and DoH server use the defaults; **Find a configuration that works** is available). Not yet confirmed whether every Steam connection on macOS honours the system PAC.
- Sign-out/shutdown timing is not fully measured. If the app cannot restore in time, the cleanup task (Windows Setup) or daemon (macOS), the next launch, or `--cleanup` will.

## License

[MIT](LICENSE). Third-party notices: [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md).
