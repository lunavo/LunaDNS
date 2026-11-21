# 0. Quick start (for regular users)

<sub>[Tiếng Việt](../00-bat-dau-nhanh.md)</sub>

This page is for you if you just want to get into Steam and don't need the technical details. The later chapters cover each part in more depth.

## What does LunaDNS do?

Some internet providers break the way into Steam: login errors, or a blank Store or Community page. LunaDNS fixes exactly that, **for Steam domain names only**. Other websites, your local network and games are left alone. Quit the app with **Thoát & khôi phục** (Quit & restore) and the PC is back to how it was.

## Which file to download?

Go to the [Releases](https://github.com/lunavo/LunaDNS/releases) page, open the newest release, and scroll down to **Assets**:

| Your computer | Download | Notes |
|---|---|---|
| Windows 10/11 (recommended) | `LunaDNS-Setup-x.y.z.exe` | Installer, double-click to install |
| Windows, no install | `LunaDNS-x.y.z-portable.zip` | **Not an installer**: extract it, then run `LunaDNS.exe` |
| Mac with Apple chip (M1–M4) | `LunaDNS-x.y.z-macos-arm64.dmg` | |
| Mac with Intel chip | `LunaDNS-x.y.z-macos-x64.dmg` | |

**Do not download** `Source code (zip)` or `Source code (tar.gz)`: that is source code for programmers and has nothing you can run.

You do not need to install .NET or anything else.

## Windows: install with Setup

1. Double-click `LunaDNS-Setup-x.y.z.exe`.
2. If **"Windows protected your PC"** appears: click **More info**, then **Run anyway**. This warning appears because the test build is not digitally signed.
3. The box **"Do you want to allow this app…"** says *Unknown publisher*: click **Yes**.
4. Click Next until the end, and tick **Mở LunaDNS** (Open LunaDNS) on the last step.

## Windows: use the portable version (zip)

1. Right-click the zip file → **Properties** → tick **Unblock** → **OK** (if there is no such box, skip this).
2. Right-click the zip file → **Extract All…** → choose a folder, for example `C:\Tools\LunaDNS`. You must **extract the whole folder**. If you double-click the exe inside the zip window, the app cannot run because the `.dll` files that go with it are missing.
3. Open the extracted folder, double-click `LunaDNS.exe`, and click **Yes** when Windows asks for Admin permission.

## Mac

1. Open the `.dmg` file and drag **LunaDNS** into **Applications**.
2. Open LunaDNS. The first time, macOS says it cannot open the app: go to **System Settings → Privacy & Security**, scroll down, click **Open Anyway**, then open the app again.
3. Click **Cài dịch vụ nền** (Install background service) and enter your Mac password.

More details: [macOS](09-macos.md).

## Use it

1. Open the app. It runs **Kiểm tra** (Check) by itself and tells you how Steam is being blocked.
2. Click **Áp dụng đề xuất** (Apply suggestions), or turn on **Định tuyến DNS** (DNS routing) yourself, and **Proxy SNI** (SNI proxy) if it is suggested.
3. Open Steam.

The **X** button only shrinks the app to the system tray (right side of the taskbar); LunaDNS keeps running. When you are done: right-click the tray icon → **Thoát & khôi phục** (Quit & restore).

When a new version is available, the app shows a banner with a **Tải** (Download) button. Download the new Setup and run it; you do not need to uninstall the old version.

## Frequently asked questions

**I downloaded the zip file but cannot install it?**
The zip file is the portable version and has no installer. Extract the whole folder, then double-click `LunaDNS.exe` (see above). If you want to install it, download `LunaDNS-Setup-x.y.z.exe`. Also check that you did not download `Source code (zip)` by mistake.

**Windows blocks it completely, with no "Run anyway" button?**
Windows 11 with Smart App Control turned on blocks unsigned exe files completely. See [Install](01-install.md#smart-app-control-windows-11).

**I can't see any version on the Releases page?**
A new release that is still a draft is visible only to the people who make the app. Wait until it is published, or ask whoever sent you the link.

**I turned it on, but Kiểm tra (Check) still says "Bị lọc SNI" (SNI filtered)?**
This is normal: Kiểm tra measures the original network path, not the one through LunaDNS. Just try opening Steam.

**The app was shut down abruptly (power cut, killed) and now Steam won't open?**
Open LunaDNS again: it undoes whatever was left behind. With the Setup version, restarting the PC also works. See [Uninstall and cleanup](07-uninstall-and-cleanup.md).

**Is it safe?**
The app does not decrypt connections, does not install certificates, and only handles Steam domain names. The source code is open in this repo. Check a downloaded file with `SHA256SUMS.txt` ([how to](01-install.md#check-the-downloaded-file-sha-256)).

**How do I uninstall it?**
Setup version: Settings → Apps → LunaDNS → Uninstall. Portable version: quit the app with "Thoát & khôi phục" (Quit & restore), then delete the folder. Details: [Uninstall and cleanup](07-uninstall-and-cleanup.md).

Still not working? See [Troubleshooting](08-troubleshooting.md), or open an [Issue](https://github.com/lunavo/LunaDNS/issues) with the report from the **Chẩn đoán** (Diagnose) button.
