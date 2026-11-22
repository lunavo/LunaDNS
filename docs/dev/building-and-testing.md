# Building and testing

<sub>[Tiếng Việt](building-and-testing.vi.md)</sub>

## Requirements

- **.NET 8 SDK** (`dotnet --version` prints `8.x`).
- Windows: PowerShell 7 (`pwsh`) or Windows PowerShell 5.1. **Inno Setup 6** only to build the Setup exe (`winget install --id JRSoftware.InnoSetup -e`).
- macOS: macOS 12+, Xcode command line tools (`codesign`, `hdiutil`). The SDK can be installed without root:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && bash dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
```

## Get the code

```bash
git clone https://github.com/lunavo/LunaDNS.git lunadns
cd lunadns
```

## Build and run

### Windows

Fastest way: double-click `Chay-tu-ma-nguon.cmd` in the repo root. It runs `build/publish.ps1 -SkipZip -SkipInstaller` and opens `artifacts\publish\win-x64\LunaDNS.exe` (UAC prompt). If the SDK is missing it prints the `winget` command to install it.

Full packaging:

```powershell
pwsh build/publish.ps1
```

| Output in `artifacts\` | Skipped with |
|---|---|
| `publish\win-x64\` self-contained single-file `LunaDNS.exe` plus native dlls | |
| `LunaDNS-<v>-portable.zip` (folder + `HUONG-DAN.txt`) | `-SkipZip` |
| `LunaDNS-Setup-<v>.exe` (Inno Setup, `build/installer/LunaDNS.iss`) | `-SkipInstaller` |
| `SHA256SUMS.txt` | |

`-Version x.y.z` overrides the version; the default is `<Version>` in `src/LunaDns.App/LunaDns.App.csproj`.

To try a build on a clean machine, enable Windows Sandbox and double-click `build/sandbox/LunaDNS.wsb`. It maps `artifacts\` read-only onto the sandbox desktop. Edit `HostFolder` if your clone is not in `D:\Projects\lunadns`.

### macOS

```bash
build/run-macos.sh            # build an unsigned LunaDNS.app for this Mac and open it
build/run-macos.sh --build-only
build/publish-macos.sh        # ad-hoc signed .app and .dmg for osx-arm64 and osx-x64
```

`publish-macos.sh` takes `--version x.y.z` and `--rid osx-arm64|osx-x64|all`, and writes `artifacts/LunaDNS-<v>-macos-{arm64,x64}.dmg` and `artifacts/SHA256SUMS-macos.txt`. The `.app` layout and signing are in `build/macos-bundle.sh`. More detail: [user guide, macOS](../guide/en/09-macos.md).

Command line only (no UI):

```bash
dotnet publish src/LunaDns.MacCli -c Release -r osx-arm64 --self-contained -o artifacts/publish/osx-arm64
sudo artifacts/publish/osx-arm64/lunadns install
```

### Icons

`build/icongen` renders `LunaDNS.ico`, tray icons and (with `--macos`) the macOS template icons from one source; `build/make-icns.sh` builds `LunaDNS.icns`. The generated files are committed: only rerun when the artwork changes.

## Tests

```powershell
dotnet build -warnaserror
dotnet test tests/LunaDns.Core.Tests
dotnet test tests/LunaDns.App.Tests -f net8.0-windows      # on macOS: -f net8.0
dotnet test tests/LunaDns.Platform.Windows.Tests           # Windows only
dotnet test tests/LunaDns.Platform.MacOS.Tests             # runs anywhere, uses fakes
```

`LunaDns.App.Tests` has two targets: `net8.0-windows` is the Windows app, `net8.0` the macOS app.

On a Mac, `dotnet test` of the whole solution also works and should report 0 failures:

```bash
dotnet test LunaDns.sln
```

It runs the `net8.0-windows` builds too (`LunaDns.Platform.Windows.Tests` and the Windows target of `LunaDns.App.Tests`). Tests in them that need real Windows (registry, ACLs, CIM, named mutexes, `C:\` paths, `wininet.dll`) are marked `[WindowsFact]` / `[WindowsTheory]` (`tests/Shared/WindowsFactAttribute.cs`) and show up as skipped with "Chỉ chạy trên Windows."; the rest of those projects still runs. On Windows, and in CI, nothing is skipped by these attributes. When you add a test under `tests/LunaDns.Platform.Windows.Tests` or `tests/LunaDns.App.Tests/Windows/` that calls a Windows-only API, use `[WindowsFact]` / `[WindowsTheory]` instead of `[Fact]` / `[Theory]`.

Tests that touch the real system or the internet are skipped by default:

| Variable | Enables | Notes |
|---|---|---|
| `LUNADNS_NETWORK_TESTS=1` | Core tests against real DoH servers and Steam hosts | Needs internet; results depend on your ISP |
| `LUNADNS_ADMIN_TESTS=1` | Windows tests that add/remove real NRPT rules, hosts lines and proxy settings | Run from an elevated shell. Each test applies then reverts and compares before/after |
| `LUNADNS_ADMIN_TESTS=1` + `--filter Category=MacAdmin` | macOS tests on the real `/etc/resolver`, `/etc/hosts`, `networksetup` | Run with sudo, see below |

Windows (elevated PowerShell):

```powershell
$env:LUNADNS_ADMIN_TESTS = '1'
dotnet test tests/LunaDns.Platform.Windows.Tests
```

macOS: build as your normal user first, then run with sudo and `--no-build`, otherwise `obj/`, `bin/` and `~/.dotnet` become owned by root:

```bash
dotnet build tests/LunaDns.Platform.MacOS.Tests
sudo env PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" LUNADNS_ADMIN_TESTS=1 dotnet test tests/LunaDns.Platform.MacOS.Tests --no-build --filter Category=MacAdmin
```

If you forgot `--no-build`: `sudo chown -R "$USER" ~/.dotnet src tests`.

## CI

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, on `windows-latest` and `macos-latest`: `dotnet build -c Release -warnaserror` then the test projects for that OS. Admin and network tests are skipped there. Releases: see [Releasing](releasing.md).

## Code conventions

- Warnings are errors; analyzers at `latest-recommended`. Fix the warning, do not suppress it without a reason in a comment.
- Code comments, log messages and UI strings are in Vietnamese; keep new ones consistent with the file you are editing.
- Logging uses `[LoggerMessage]` source-generated methods.
- Anything that changes the system goes through `INetworkConfigurator` and the journal: plan, record, apply. Never change system state without a journal entry that can undo it.
