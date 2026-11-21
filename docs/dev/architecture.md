# Architecture

<sub>[Tiếng Việt](architecture.vi.md)</sub>

LunaDNS is a .NET 8 / Avalonia 11 desktop app. Most logic lives in a platform-independent core; each OS plugs in an `INetworkConfigurator` that knows how to change that OS's DNS routing, proxy and hosts settings.

## Projects

| Project | Target | Role |
|---|---|---|
| `src/LunaDns.Core` | `net8.0` | Probing, DoH client and DNS stub, SNI-splitting proxy, change journal, features, settings, logging, diagnostics. No OS-specific code. |
| `src/LunaDns.Platform.Windows` | `net8.0-windows` | NRPT rules (CIM), hosts file, WinINet `AutoConfigURL`, data-folder ACL, the `\LunaDNS\Cleanup` scheduled task. |
| `src/LunaDns.Platform.MacOS` | `net8.0` | `/etc/resolver/<domain>` files, `networksetup` auto-proxy, `/etc/hosts`, the daemon socket protocol and peer-credential checks. |
| `src/LunaDns.MacCli` | `net8.0` | The `lunadns` command and the launchd daemon (`vn.lunavo.lunadns`). |
| `src/LunaDns.App` | `net8.0-windows` and `net8.0` | Avalonia UI. On Windows it runs the features in-process (elevated). On macOS it runs as the user and drives the daemon. |
| `tests/*` | | xUnit. One test project per source project. |

`Directory.Build.props` sets `net8.0`, C# 12, nullable, `TreatWarningsAsErrors` and `AnalysisLevel=latest-recommended` for every project.

## Core concepts

### Probe first, then act

`Probing/Prober` checks sample Steam hosts over the **original** network path (not through LunaDNS) and classifies each one (`ProbeVerdict`): normal, DNS poisoned, SNI filtered, unreachable. `ProbeAdvisor` turns verdicts into remedies (`ProbeRemedies`): DNS routing, or DNS routing plus the SNI proxy. The UI's "Áp dụng đề xuất" (Apply suggestions) button enables only the remedies that are missing.

### Features

`Features/FeatureSet` owns the switches (`Feature`): DNS routing and SNI proxy. A third value, PUBG, still exists in the enum so old journal entries can be rolled back, but it is not exposed in the UI.

- `EnableAsync` plans the system change, **records it in the journal**, applies it, then starts the service (DNS stub or proxy).
- `DisableAsync` stops the service and reverts its journal entries. If a revert fails, the service keeps running so the network is not left broken, and the entry stays in the journal.
- `DisableAllAsync` backs "Tắt hết & khôi phục" (Turn all off & restore).
- A `RestartPolicy` watchdog restarts a crashed service on the same port; three failures within a minute turn the feature off.

### Change journal

`Journal/ChangeJournal` is the safety net. Every system change is written to `journal.json` **before** it is made, as an op name plus a snapshot of the previous state (`Platform/*Snapshot`). Rollback runs LIFO through `IChangeReverter`s and keeps failed entries for the next attempt. Rollback runs:

- when the app starts (leftovers from a crash),
- on "Thoát & khôi phục" (Quit & restore) and on session end,
- from `LunaDNS.exe --cleanup` and the SYSTEM cleanup task (Windows Setup),
- every time the macOS daemon starts.

Because a privileged process reads the journal, the data folder is locked down: `%ProgramData%\LunaDNS` writable only by SYSTEM and Administrators on Windows, `/Library/Application Support/LunaDNS` owned by root with mode 700 on macOS. A pre-existing folder that others can write to is quarantined and recreated.

Op and feature names stored in the journal must never change: entries written by an older version on a user's machine must still be revertible.

### Platform interface

```csharp
public interface INetworkConfigurator
{
    DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer);
    void ApplyDnsRoute(DnsRouteSnapshot snapshot);
    void RevertDnsRoute(DnsRouteSnapshot snapshot);
    AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride);
    // … ApplyAutoProxy / RevertAutoProxy, Plan/Apply/RevertHostsEntries, FlushDnsCache
}
```

`Plan*` reads the current state and returns a snapshot without changing anything. That snapshot is what gets journaled, and `Revert*` restores exactly it. Implementations only touch what LunaDNS created: NRPT rules whose Comment starts with `LunaDNS `, resolver files whose first line is `# LunaDNS`, hosts lines tagged `# LunaDNS`.

| | Windows | macOS |
|---|---|---|
| DNS routing | One NRPT rule → DoH stub on `127.0.0.1:53` | One `/etc/resolver/<domain>` per Steam domain → `127.0.0.1:15353` |
| SNI proxy | `AutoConfigURL` of the signed-in user | `networksetup -setautoproxyurl` on each active hardware network service |
| hosts | `%SystemRoot%\System32\drivers\etc\hosts` | `/private/etc/hosts` |

### DNS stub

`Dns/DohStubServer` answers UDP/TCP DNS on loopback, forwards queries for Steam domains over DNS-over-HTTPS (`DohClient`, Cloudflare then Google, connecting straight to the IP so it never asks the system resolver) and caches answers (`DnsCache`). The domain list is in `Domains/GameDomains`.

### SNI proxy

`Proxy/SplitProxy` is an HTTP CONNECT proxy on `127.0.0.1:18080–18090`. It serves `/proxy.pac` (`PacBuilder`, Steam domains only) and accepts CONNECT only to Steam domains on port 443, so it is never an open proxy. `Tls/HelloSplitter` writes the TLS ClientHello in two TCP segments (default: after the first byte, `SplitMode.FirstByte`) so SNI filters that inspect a single segment miss it. TLS is never decrypted and no certificate is installed.

### macOS daemon

On macOS the root-only work runs in a launchd daemon (`LunaDns.MacCli`). The app and the `lunadns` command talk to it over `/var/run/lunadns.sock` (`Daemon/DaemonProtocol`). Only root and the console user may connect; the UID comes from `getpeereid`. The app polls the daemon every 2 seconds.

### App

`LunaDns.App` uses a small hand-written MVVM (`ViewModels/`). `Features/IFeatureController` hides the difference between in-process features (`LocalFeatureController`, Windows) and the daemon (`MacOS/DaemonFeatureController`). UI strings are in `Strings.resx` (Vietnamese only). `Updates/UpdateChecker` reads `GET /repos/lunavo/LunaDNS/releases`, skips drafts and only accepts download URLs under `https://github.com/lunavo/LunaDNS/releases/`.

## Design records

Specs and step-by-step plans (Vietnamese) are in `docs/superpowers/specs/` and `docs/superpowers/plans/`. Network spike results: `docs/spikes/`.
