# LunaDNS — Kế hoạch 4: `Platform.Windows` (NRPT, AutoConfigURL, hosts) qua ChangeJournal

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Viết ba thao tác hệ thống đầu tiên của LunaDNS: luật NRPT trỏ domain game về stub `127.0.0.1`, `AutoConfigURL` trỏ về PAC của `SplitProxy`, và dòng hosts `# LunaDNS`. Mọi thay đổi đi qua `ChangeJournal` (ghi trước, hoàn tác LIFO, idempotent). Luật NRPT của Tailscale có sẵn trên máy PO phải còn nguyên sau mọi vòng áp dụng/hoàn tác.

**Architecture:** `LunaDns.Core/Platform` định nghĩa `INetworkConfigurator` theo ba bước Plan (chỉ đọc, trả snapshot) → Apply → Revert, cùng `SystemChanges` là cổng duy nhất `Plan → Journal.Record → Apply`. Phần sửa file hosts ở mức byte là logic thuần (`Core/Hosts/HostsEditor`), test không cần Windows. Project mới `LunaDns.Platform.Windows` (`net8.0-windows`) cài đặt interface: `NrptDnsRouter` trên `INrptStore` (bản thật `NrptCimStore` gọi CIM), `AutoProxyRegistry` (HKEY_USERS\<SID>), `HostsFileStore` (ghi nguyên tử, giữ thuộc tính và DACL) và P/Invoke `InternetSetOption` cùng `DnsFlushResolverCache`.

**Tech Stack:** .NET 8 SDK 8.0.428, C# 12, `Microsoft.Win32.Registry` (có sẵn trong `net8.0-windows`), `[LibraryImport]`, **`Microsoft.Management.Infrastructure` 3.0.0 (package mới, chờ PO duyệt, xem quyết định 2)**, xUnit 2.5.3.

**Spec:** `docs/superpowers/specs/2026-11-13-lunadns-design.md` (mục 3, 4.2, 4.4, 4.5, 6, 9). **Kế hoạch trước:** `docs/superpowers/plans/2026-11-13-lunadns-03-splitproxy-prober.md`. **Spike:** `docs/spikes/2026-11-spike-results.md` (NRPT local + `127.0.0.1` chạy trên Win10 Pro 19045; máy có sẵn luật Tailscale). **Lộ trình:** bảng ở đầu `docs/superpowers/plans/2026-11-13-lunadns-01-core-foundation.md`. KH4 trong bảng gồm cả ACL và task dọn dẹp; plan này đề xuất tách hai việc đó sang **KH4b** (quyết định 10).

## Sự thật đã đo trên máy PO (13/11/2026, chỉ đọc)

- NRPT: đúng 2 luật, `{5abe529b-675b-4486-8459-25a634dacc23}` (50 namespace) và `{94C06E32-4296-4110-AEF6-DC1D1230C545}` (17 namespace). Cả hai có nameserver `100.100.100.100,fd7a:115c:a1e0::53`, `Comment` và `DisplayName` rỗng, không namespace nào trùng hay chứa domain game, không có namespace `.`. Hai luật này nằm ở `HKLM\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DnsPolicyConfig\{GUID}`. Tailscale dùng GUID cố định, nên LunaDNS nhận diện luật của mình bằng `Comment`, không bằng vị trí hay thứ tự.
- Lệnh `Add/Get/Remove-DnsClientNrptRule` là CDXML trên lớp CIM `root\Microsoft\Windows\DNS:PS_DnsClientNrptRule`, gồm các phương thức tĩnh:
  - `Add(Namespace: string[], NameServers: string[], Comment, DisplayName, …, cmdletOutput[out])`
  - `Get(Name: string[], …, cmdletOutput: Instance[] [out])`
  - `Remove(Name, Force, …)`
  
  `Get` chạy được khi không có quyền Admin. Mỗi instance trả về có `Name`, `Namespace` (string[]), `NameServers` (string[]), `Comment`, `DisplayName`.
- `HKCU\…\Internet Settings`: `AutoConfigURL` trống, `ProxyEnable = 0`.
- File hosts nằm ở `C:\WINDOWS\System32\drivers\etc` (đọc từ `DataBasePath`). File có BOM UTF-8, dùng CRLF, kết thúc bằng xuống dòng, thuộc tính `Archive`, dài 1091 byte, chưa có mục steam.
- Mạng PO: DNS hệ thống trả `127.0.0.1` cho `store.steampowered.com`. Test admin + mạng ở Task 5 dựa vào đúng điều này để phân biệt có hay không có NRPT.

## Global Constraints

- Target: `net8.0` cho Core, `net8.0-windows` cho `LunaDns.Platform.Windows` và test của nó (SDK 8.0.428). `Directory.Build.props` (Nullable, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `LangVersion 12`) giữ nguyên; project Windows chỉ ghi đè `TargetFramework`.
- `LunaDns.Core` **không** tham chiếu `Microsoft.Win32`, P/Invoke hay package riêng của Windows. Chỉ `Platform.Windows` gọi registry, CIM và P/Invoke (spec 3).
- **Package mới duy nhất:** `Microsoft.Management.Infrastructure` 3.0.0, chỉ ở `src/LunaDns.Platform.Windows`, và chỉ khi PO duyệt quyết định 2. Test project dùng lại đúng các phiên bản package mà `LunaDns.Core.Tests` đang dùng.
- **Không gọi PowerShell, `netsh` hay `ipconfig` từ mã** (spec 3). Lệnh PowerShell trong plan này chỉ để PO kiểm tra bằng tay.
- **Ghi Journal trước, đổi hệ thống sau** (spec 4.5, hợp đồng KH1): mọi thay đổi đi qua `SystemChanges`. Revert phải idempotent. Không có thao tác nào ngoài Journal: không reset WinHTTP, không đụng `DefaultConnectionSettings` hay WPAD.
- **Không bao giờ sửa hay xoá luật NRPT không mang dấu `Comment` bắt đầu bằng `"LunaDNS "`.** Luật lạ (Tailscale, VPN công ty, GPO) chỉ được đọc.
- Không có `catch { }` rỗng (spec 6). Lỗi hệ thống được đổi thành `ConfiguratorException` có `ConfiguratorError` và message tiếng Việt có dấu. Log dùng `[LoggerMessage]`. Log không chứa giá trị `AutoConfigURL` cũ (có thể là URL nội bộ công ty) hay tên host nằm ngoài `GameDomains`.
- Không chép mã từ SteamAccessFix (D4).
- Hằng mảng truyền làm đối số thì đưa lên thành `static readonly` (CA1861). Test không dùng collection expression làm đối số `expected` của `Assert.Equal` khi kiểu không suy ra được; dùng mảng `static readonly`.
- Test chạm cấu hình thật của Windows (NRPT, file hosts thật, `AutoConfigURL` thật, flush DNS) chỉ chạy khi `LUNADNS_ADMIN_TESTS=1` (và `LUNADNS_NETWORK_TESTS=1` nếu cần mạng), trong PowerShell Admin, và đều nằm trong `[Collection("RealSystem")]` để không chạy song song. Test mặc định dùng file tạm và khoá registry `HKU\<SID>\Software\LunaDNS.Tests\<guid>`.
- Lệnh kiểm tra (PowerShell):
  `$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"; dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests; dotnet test tests/LunaDns.Platform.Windows.Tests`
  Trạng thái nền: Core **242 passed + 4 skipped** khi chạy mặc định, **246 passed** khi bật `LUNADNS_NETWORK_TESTS=1`.
- Repo chỉ ở local và **không commit** (giữ quyết định của KH3): không `git add`, `git commit`, `git switch`, không tạo nhánh. Phiên chính review bằng `git diff` và `git status`. Các mục untracked của KH1–KH3 để nguyên; chỉ sửa đúng file mà task nêu.

## Gợi ý chia việc cho sub-agent (Sonnet)

Dự kiến 3 agent, đều `model: "sonnet"`, chạy tuần tự. Task 7 do PO chạy trong PowerShell Admin, vì sub-agent không có quyền nâng quyền.

| Agent | Task | Lý do gộp |
|---|---|---|
| A | 1, 2 | Toàn bộ phần thuần trong Core: hợp đồng, `SystemChanges`, `HostsEditor` |
| B | 3, 4 | Dựng project Windows, `HostsFileStore`, P/Invoke, `AutoProxyRegistry` |
| C | 5, 6 | NRPT (package CIM, rủi ro cao nhất) và ráp `WindowsNetworkConfigurator` |

Sau mỗi agent, phiên chính tự review bằng `git diff` rồi chạy lại lệnh kiểm tra. Sonnet trượt verify 2 lần thì giao lại Opus đúng một lần.

## Cấu trúc file của kế hoạch 4

```
LunaDns.sln                                   # (sửa, T3) thêm 2 project
src/LunaDns.Core/
├── Platform/
│   ├── ConfiguratorError.cs                  # T1
│   ├── ConfiguratorException.cs              # T1
│   ├── DnsRouteSnapshot.cs                   # T1
│   ├── AutoProxySnapshot.cs                  # T1
│   ├── HostsEntry.cs                         # T1
│   ├── HostsSnapshot.cs                      # T1
│   ├── ChangeOps.cs                          # T1  tên op trong journal
│   ├── INetworkConfigurator.cs               # T1  Plan / Apply / Revert
│   ├── SnapshotReverter.cs                   # T1  JsonElement → snapshot → Revert*
│   └── SystemChanges.cs                      # T1  Plan → Record → Apply (+ Flush)
└── Hosts/
    └── HostsEditor.cs                        # T2  thêm/gỡ dòng hosts ở mức byte
src/LunaDns.Platform.Windows/
├── LunaDns.Platform.Windows.csproj           # T3 (T5 thêm package CIM)
├── NativeMethods.cs                          # T4  InternetSetOptionW, DnsFlushResolverCache
├── WinInet.cs                                # T4
├── WindowsDns.cs                             # T4
├── WindowsNetworkConfigurator.cs             # T6
├── Hosts/HostsFileStore.cs                   # T3
├── Proxy/AutoProxyRegistry.cs                # T4
└── Nrpt/
    ├── NrptRule.cs                           # T5
    ├── INrptStore.cs                         # T5
    ├── NrptDnsRouter.cs                      # T5  dấu Comment, bỏ namespace trùng luật lạ
    └── NrptCimStore.cs                       # T5  CIM PS_DnsClientNrptRule
tests/LunaDns.Core.Tests/
├── Platform/FakeConfigurator.cs              # T1
├── Platform/SystemChangesTests.cs            # T1
└── Hosts/HostsEditorTests.cs                 # T2
tests/LunaDns.Platform.Windows.Tests/
├── LunaDns.Platform.Windows.Tests.csproj     # T3
├── TempDirectory.cs                          # T3
├── AdminFactAttribute.cs                     # T3  (+ AdminNetworkFactAttribute, RealSystemCollection)
├── Hosts/HostsFileStoreTests.cs              # T3
├── NativeTests.cs                            # T4
├── Proxy/AutoProxyRegistryTests.cs           # T4
├── Nrpt/FakeNrptStore.cs                     # T5
├── Nrpt/NrptDnsRouterTests.cs                # T5
├── Nrpt/NrptCimStoreTests.cs                 # T5  (1 test thường + 2 test admin)
├── WindowsNetworkConfiguratorTests.cs        # T6
└── WindowsNetworkConfiguratorAdminTests.cs   # T6
```

---

### Task 1: Hợp đồng `INetworkConfigurator` và `SystemChanges` (Core)

**Files:**
- Create: `src/LunaDns.Core/Platform/ConfiguratorError.cs`, `ConfiguratorException.cs`, `DnsRouteSnapshot.cs`, `AutoProxySnapshot.cs`, `HostsEntry.cs`, `HostsSnapshot.cs`, `ChangeOps.cs`, `INetworkConfigurator.cs`, `SnapshotReverter.cs`, `SystemChanges.cs`
- Test: `tests/LunaDns.Core.Tests/Platform/FakeConfigurator.cs`, `tests/LunaDns.Core.Tests/Platform/SystemChangesTests.cs`

**Interfaces:**
- Consumes: `ChangeJournal` (`Record<T>(feature, op, snapshot)`, `Rollback(feature)`, `RollbackAll()`, `Entries`), `IChangeReverter` (`Op`, `Revert(JsonElement)`), `InMemoryJournalStore` (test) của KH1.
- Produces:
  - `enum ConfiguratorError { Other, AccessDenied, DnsRouteConflict, ExistingProxyConfig, UserHiveNotLoaded, UnexpectedRegistryValue, HostsUnsupportedEncoding, NrptFailed }`
  - `ConfiguratorException(ConfiguratorError error, string message[, Exception inner])`, có `.Error`
  - `record DnsRouteSnapshot(string Marker, IReadOnlyList<string> Namespaces, string NameServer, IReadOnlyList<string> SkippedNamespaces)`
  - `record AutoProxySnapshot(string UserSid, bool HadPreviousValue, string? PreviousValue, bool PreviousWasExpandString, string NewValue)`
  - `record HostsEntry(string Address, string Host)`, `record HostsSnapshot(IReadOnlyList<string> Lines, string LineEnding, bool AddedNewline)`
  - `ChangeOps.DnsRoute = "dns-route"`, `ChangeOps.AutoProxy = "auto-proxy"`, `ChangeOps.HostsEntries = "hosts-entries"`
  - `INetworkConfigurator` (Plan/Apply/Revert cho 3 thao tác, cùng `FlushDnsCache()`)
  - `SystemChanges(ChangeJournal, INetworkConfigurator)`: `AddDnsRoute(feature, domains, IPAddress)`, `SetAutoProxy(feature, Uri, bool allowOverride = false)`, `AddHostsEntries(feature, entries)`; `static IReadOnlyList<IChangeReverter> Reverters(INetworkConfigurator)`

- [ ] **Step 1: Viết các kiểu hợp đồng**

`src/LunaDns.Core/Platform/ConfiguratorError.cs`:
```csharp
namespace LunaDns.Core.Platform;

public enum ConfiguratorError
{
    Other,
    AccessDenied,
    DnsRouteConflict,
    ExistingProxyConfig,
    UserHiveNotLoaded,
    UnexpectedRegistryValue,
    HostsUnsupportedEncoding,
    NrptFailed,
}
```

`src/LunaDns.Core/Platform/ConfiguratorException.cs`:
```csharp
namespace LunaDns.Core.Platform;

/// <summary>Lỗi khi đổi hoặc hoàn tác cấu hình hệ thống (spec 6). Message viết cho người dùng, tiếng Việt.</summary>
public sealed class ConfiguratorException : Exception
{
    public ConfiguratorException()
    {
    }

    public ConfiguratorException(string message) : base(message)
    {
    }

    public ConfiguratorException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ConfiguratorException(ConfiguratorError error, string message) : base(message) =>
        Error = error;

    public ConfiguratorException(ConfiguratorError error, string message, Exception innerException) : base(message, innerException) =>
        Error = error;

    public ConfiguratorError Error { get; } = ConfiguratorError.Other;
}
```

`src/LunaDns.Core/Platform/DnsRouteSnapshot.cs`:
```csharp
namespace LunaDns.Core.Platform;

/// <summary>
/// Luật định tuyến DNS sắp thêm. <see cref="Marker"/> được ghi vào Comment của luật và là duy nhất cho mỗi lần áp dụng;
/// hoàn tác chỉ gỡ luật mang đúng dấu này. <see cref="SkippedNamespaces"/> là các namespace bỏ qua vì đã có luật của phần mềm khác.
/// </summary>
public sealed record DnsRouteSnapshot(string Marker, IReadOnlyList<string> Namespaces, string NameServer, IReadOnlyList<string> SkippedNamespaces);
```

`src/LunaDns.Core/Platform/AutoProxySnapshot.cs`:
```csharp
namespace LunaDns.Core.Platform;

/// <summary>Giá trị AutoConfigURL của một người dùng (theo SID) trước khi LunaDNS ghi <see cref="NewValue"/>.</summary>
public sealed record AutoProxySnapshot(string UserSid, bool HadPreviousValue, string? PreviousValue, bool PreviousWasExpandString, string NewValue);
```

`src/LunaDns.Core/Platform/HostsEntry.cs`:
```csharp
namespace LunaDns.Core.Platform;

/// <summary>Một dòng hosts muốn thêm. Address phải là loopback, Host phải thuộc GameDomains (kiểm tra ở HostsEditor).</summary>
public sealed record HostsEntry(string Address, string Host);
```

`src/LunaDns.Core/Platform/HostsSnapshot.cs`:
```csharp
namespace LunaDns.Core.Platform;

/// <summary>
/// Các dòng LunaDNS thêm vào hosts (đã định dạng, kèm "# LunaDNS"), kiểu xuống dòng của file,
/// và việc LunaDNS có phải tự thêm dấu xuống dòng cho dòng cuối cũ hay không.
/// </summary>
public sealed record HostsSnapshot(IReadOnlyList<string> Lines, string LineEnding, bool AddedNewline);
```

`src/LunaDns.Core/Platform/ChangeOps.cs`:
```csharp
namespace LunaDns.Core.Platform;

/// <summary>Tên thao tác ghi trong journal. Không đổi giá trị: journal cũ trên máy khách dùng các chuỗi này.</summary>
public static class ChangeOps
{
    public const string DnsRoute = "dns-route";
    public const string AutoProxy = "auto-proxy";
    public const string HostsEntries = "hosts-entries";
}
```

`src/LunaDns.Core/Platform/INetworkConfigurator.cs`:
```csharp
using System.Net;

namespace LunaDns.Core.Platform;

/// <summary>
/// Thao tác hệ thống chia ba bước để Journal ghi TRƯỚC khi đổi (spec 4.5):
/// Plan* chỉ đọc và trả snapshot, Apply* đổi hệ thống theo snapshot (chạy lại không nhân đôi),
/// Revert* hoàn tác theo snapshot (idempotent). Chỉ gọi qua <see cref="SystemChanges"/>.
/// </summary>
public interface INetworkConfigurator
{
    DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer);
    void ApplyDnsRoute(DnsRouteSnapshot snapshot);
    void RevertDnsRoute(DnsRouteSnapshot snapshot);

    AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride);
    void ApplyAutoProxy(AutoProxySnapshot snapshot);
    void RevertAutoProxy(AutoProxySnapshot snapshot);

    HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries);
    void ApplyHostsEntries(HostsSnapshot snapshot);
    void RevertHostsEntries(HostsSnapshot snapshot);

    /// <summary>Xoá cache DNS của hệ điều hành. Không ném lỗi; lỗi thì bản cài đặt tự ghi log.</summary>
    void FlushDnsCache();
}
```

- [ ] **Step 2: Viết fake và test (chưa có `SystemChanges`, `SnapshotReverter`)**

`tests/LunaDns.Core.Tests/Platform/FakeConfigurator.cs`:
```csharp
using System.Net;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Tests.Platform;

/// <summary>Ghi lại thứ tự gọi. BeforeApply chạy ngay trước mỗi Apply, để test kiểm tra journal đã ghi chưa.</summary>
internal sealed class FakeConfigurator : INetworkConfigurator
{
    public List<string> Calls { get; } = [];
    public Action? BeforeApply { get; set; }
    public bool FailApply { get; set; }

    public DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer)
    {
        Calls.Add("plan:dns");
        return new DnsRouteSnapshot("LunaDNS test", [.. domains.Select(d => "." + d)], nameServer.ToString(), []);
    }

    public void ApplyDnsRoute(DnsRouteSnapshot snapshot) => Apply("apply:dns");

    public void RevertDnsRoute(DnsRouteSnapshot snapshot) => Calls.Add($"revert:dns:{snapshot.Marker}");

    public AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride)
    {
        Calls.Add("plan:proxy");
        return new AutoProxySnapshot("S-1-5-21-1", false, null, false, pacUrl.AbsoluteUri);
    }

    public void ApplyAutoProxy(AutoProxySnapshot snapshot) => Apply("apply:proxy");

    public void RevertAutoProxy(AutoProxySnapshot snapshot) => Calls.Add($"revert:proxy:{snapshot.NewValue}");

    public HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries)
    {
        Calls.Add("plan:hosts");
        return new HostsSnapshot([.. entries.Select(e => $"{e.Address} {e.Host}")], "\r\n", false);
    }

    public void ApplyHostsEntries(HostsSnapshot snapshot) => Apply("apply:hosts");

    public void RevertHostsEntries(HostsSnapshot snapshot) => Calls.Add($"revert:hosts:{snapshot.Lines[0]}");

    public void FlushDnsCache() => Calls.Add("flush");

    private void Apply(string call)
    {
        BeforeApply?.Invoke();
        Calls.Add(call);
        if (FailApply) throw new ConfiguratorException(ConfiguratorError.Other, "giả lập lỗi");
    }
}
```

`tests/LunaDns.Core.Tests/Platform/SystemChangesTests.cs`:
```csharp
using System.Net;
using System.Text.Json;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Tests.Journal;

namespace LunaDns.Core.Tests.Platform;

public class SystemChangesTests
{
    private static readonly string[] SteamDomains = ["steampowered.com"];
    private static readonly HostsEntry[] PubgHosts = [new("127.0.0.1", "pubg.com")];
    private static readonly Uri PacUrl = new("http://127.0.0.1:18080/proxy.pac");
    private static readonly string[] AllOps = [ChangeOps.DnsRoute, ChangeOps.AutoProxy, ChangeOps.HostsEntries];
    private static readonly string[] ApplyCalls =
        ["plan:dns", "apply:dns", "flush", "plan:proxy", "apply:proxy", "plan:hosts", "apply:hosts", "flush"];
    private static readonly string[] RevertCalls =
    [
        "revert:hosts:127.0.0.1 pubg.com", "flush",
        "revert:proxy:http://127.0.0.1:18080/proxy.pac",
        "revert:dns:LunaDNS test", "flush",
    ];

    private readonly InMemoryJournalStore _store = new();
    private readonly FakeConfigurator _cfg = new();

    private ChangeJournal NewJournal() => new(_store, SystemChanges.Reverters(_cfg));

    private static void ApplyAll(SystemChanges changes)
    {
        changes.AddDnsRoute("dns", SteamDomains, IPAddress.Loopback);
        changes.SetAutoProxy("proxy", PacUrl);
        changes.AddHostsEntries("pubg", PubgHosts);
    }

    [Fact]
    public void Records_journal_entry_before_each_apply()
    {
        var journal = NewJournal();
        var expected = 0;
        _cfg.BeforeApply = () => Assert.Equal(++expected, journal.Entries.Count);

        ApplyAll(new SystemChanges(journal, _cfg));

        Assert.Equal(3, expected);
        Assert.Equal(AllOps, journal.Entries.Select(e => e.Op));
    }

    [Fact]
    public void Flushes_dns_cache_after_dns_route_and_hosts_only()
    {
        ApplyAll(new SystemChanges(NewJournal(), _cfg));

        Assert.Equal(ApplyCalls, _cfg.Calls);
    }

    [Fact]
    public void Failed_apply_keeps_entry_and_feature_rollback_reverts_it()
    {
        var journal = NewJournal();
        _cfg.FailApply = true;

        Assert.Throws<ConfiguratorException>(() => new SystemChanges(journal, _cfg).AddDnsRoute("dns", SteamDomains, IPAddress.Loopback));
        Assert.Single(journal.Entries);

        Assert.True(journal.Rollback("dns").Success);
        Assert.Empty(journal.Entries);
        Assert.Contains("revert:dns:LunaDNS test", _cfg.Calls);
    }

    [Fact]
    public void Rollback_all_after_restart_reverts_lifo_and_flushes()
    {
        ApplyAll(new SystemChanges(NewJournal(), _cfg));
        _cfg.Calls.Clear();

        var restarted = NewJournal();
        var result = restarted.RollbackAll();

        Assert.True(result.Success);
        Assert.Equal(3, result.Reverted);
        Assert.Equal(RevertCalls, _cfg.Calls);
    }

    [Fact]
    public void Rollback_after_crash_between_record_and_apply_runs_revert_once()
    {
        _cfg.FailApply = true;
        Assert.Throws<ConfiguratorException>(() => new SystemChanges(NewJournal(), _cfg).SetAutoProxy("proxy", PacUrl));
        _cfg.FailApply = false;
        _cfg.Calls.Clear();

        var result = NewJournal().RollbackAll();

        Assert.Equal(1, result.Reverted);
        Assert.Equal(["revert:proxy:http://127.0.0.1:18080/proxy.pac"], _cfg.Calls);
    }

    [Fact]
    public void Snapshot_with_wrong_shape_is_kept_as_failure()
    {
        var journal = NewJournal();
        journal.Record("dns", ChangeOps.DnsRoute, "không phải object");

        var result = journal.RollbackAll();

        var failure = Assert.Single(result.Failed);
        Assert.IsType<JsonException>(failure.Error);
        Assert.Single(journal.Entries);
    }
}
```

Lưu ý cho người thực thi: nếu `Assert.Equal(["revert:proxy:…"], _cfg.Calls)` không suy ra được kiểu (CS0411), đưa mảng lên thành `private static readonly string[] ProxyRevertCall`.

- [ ] **Step 3: Chạy test, xác nhận lỗi biên dịch**

Run: `$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"; dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SystemChangesTests"`
Expected: build FAIL với CS0103/CS0246 cho `SystemChanges`.

- [ ] **Step 4: Viết `SnapshotReverter` và `SystemChanges`**

`src/LunaDns.Core/Platform/SnapshotReverter.cs`:
```csharp
using System.Text.Json;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Platform;

/// <summary>Đọc snapshot kiểu <typeparamref name="T"/> từ journal rồi gọi hàm hoàn tác. Snapshot sai dạng thì ném JsonException (mục được giữ lại).</summary>
internal sealed class SnapshotReverter<T>(string op, Action<T> revert) : IChangeReverter
    where T : class
{
    public string Op { get; } = op;

    public void Revert(JsonElement snapshot) =>
        revert(snapshot.Deserialize<T>() ?? throw new JsonException($"Snapshot rỗng cho thao tác '{Op}'."));
}
```

`src/LunaDns.Core/Platform/SystemChanges.cs`:
```csharp
using System.Net;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Platform;

/// <summary>
/// Cổng duy nhất để đổi hệ thống: Plan (chỉ đọc) → ChangeJournal.Record → Apply (spec 4.5, hợp đồng KH1).
/// Nếu Apply lỗi thì mục journal vẫn còn; người gọi (FeatureSet, KH5) gọi journal.Rollback(feature).
/// </summary>
public sealed class SystemChanges(ChangeJournal journal, INetworkConfigurator configurator)
{
    public DnsRouteSnapshot AddDnsRoute(string feature, IReadOnlyList<string> domains, IPAddress nameServer)
    {
        var snapshot = configurator.PlanDnsRoute(domains, nameServer);
        journal.Record(feature, ChangeOps.DnsRoute, snapshot);
        configurator.ApplyDnsRoute(snapshot);
        configurator.FlushDnsCache();
        return snapshot;
    }

    public AutoProxySnapshot SetAutoProxy(string feature, Uri pacUrl, bool allowOverride = false)
    {
        var snapshot = configurator.PlanAutoProxy(pacUrl, allowOverride);
        journal.Record(feature, ChangeOps.AutoProxy, snapshot);
        configurator.ApplyAutoProxy(snapshot);
        return snapshot;
    }

    public HostsSnapshot AddHostsEntries(string feature, IReadOnlyList<HostsEntry> entries)
    {
        var snapshot = configurator.PlanHostsEntries(entries);
        journal.Record(feature, ChangeOps.HostsEntries, snapshot);
        configurator.ApplyHostsEntries(snapshot);
        configurator.FlushDnsCache();
        return snapshot;
    }

    /// <summary>Reverter cho ChangeJournal. Dựng journal bằng các reverter này trước, rồi mới tạo SystemChanges.</summary>
    public static IReadOnlyList<IChangeReverter> Reverters(INetworkConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        return
        [
            new SnapshotReverter<DnsRouteSnapshot>(ChangeOps.DnsRoute, s =>
            {
                configurator.RevertDnsRoute(s);
                configurator.FlushDnsCache();
            }),
            new SnapshotReverter<AutoProxySnapshot>(ChangeOps.AutoProxy, configurator.RevertAutoProxy),
            new SnapshotReverter<HostsSnapshot>(ChangeOps.HostsEntries, s =>
            {
                configurator.RevertHostsEntries(s);
                configurator.FlushDnsCache();
            }),
        ];
    }
}
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SystemChangesTests"`
Expected: 6 passed.

- [ ] **Step 6: Chạy lệnh kiểm tra đầy đủ, rồi review**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: 0 warning; 248 passed, 4 skipped. Không commit; báo cáo task liệt kê file mới.

---

### Task 2: `HostsEditor` — thêm/gỡ dòng hosts ở mức byte (Core)

**Files:**
- Create: `src/LunaDns.Core/Hosts/HostsEditor.cs`
- Test: `tests/LunaDns.Core.Tests/Hosts/HostsEditorTests.cs`

**Interfaces:**
- Consumes: `HostsEntry`, `HostsSnapshot`, `ConfiguratorException`, `ConfiguratorError.HostsUnsupportedEncoding` (Task 1); `DomainMatcher.Normalize/Matches`, `GameDomains.All` (KH1).
- Produces: `static class HostsEditor` với `const string Marker = "# LunaDNS"`, `HostsSnapshot Plan(ReadOnlySpan<byte> original, IReadOnlyList<HostsEntry> entries)`, `byte[] Apply(ReadOnlySpan<byte> current, HostsSnapshot snapshot)`, `byte[] Revert(ReadOnlySpan<byte> current, HostsSnapshot snapshot)`. Dòng có dạng `127.0.0.1 <host> # LunaDNS` (báo cáo chẩn đoán ở KH5 lọc theo `# LunaDNS`, spec 4.9).

Vì sao làm ở mức byte: file hosts có thể có comment tiếng Việt ở UTF-8 hoặc ANSI (cp1258). Nếu giải mã rồi mã hoá lại, chữ sẽ hỏng. Dòng của LunaDNS chỉ có ký tự ASCII, nên chèn và gỡ trên byte thì giữ được phần còn lại đúng từng byte (spec 9: "giống hệt byte ban đầu").

- [ ] **Step 1: Viết test**

`tests/LunaDns.Core.Tests/Hosts/HostsEditorTests.cs`:
```csharp
using System.Text;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Tests.Hosts;

public class HostsEditorTests
{
    private static readonly HostsEntry[] Pubg = [new("127.0.0.1", "prod-live-front.playbattlegrounds.com")];
    private static readonly HostsEntry[] TwoEntries = [new("127.0.0.1", "pubg.com"), new("127.0.0.1", "lobby.pubg.com")];

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] RoundTrip(byte[] original, IReadOnlyList<HostsEntry> entries)
    {
        var snapshot = HostsEditor.Plan(original, entries);
        var applied = HostsEditor.Apply(original, snapshot);
        return HostsEditor.Revert(applied, snapshot);
    }

    [Theory]
    [InlineData("127.0.0.1 localhost\r\n")]
    [InlineData("127.0.0.1 localhost")]
    [InlineData("127.0.0.1 localhost\n# lf\n")]
    [InlineData("")]
    public void Round_trip_is_byte_identical(string text)
    {
        var original = Ascii(text);

        Assert.Equal(original, RoundTrip(original, TwoEntries));
    }

    [Fact]
    public void Round_trip_keeps_utf8_bom_and_ansi_vietnamese_bytes()
    {
        byte[] original =
        [
            0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Ghi chú tiếng Việt\r\n"),
            .. Ascii("# ANSI: "), 0xE0, 0xEA, 0xF4, 0x0D, 0x0A,
        ];

        Assert.Equal(original, RoundTrip(original, Pubg));
    }

    [Fact]
    public void Apply_appends_marked_lines_with_file_line_ending()
    {
        var original = Ascii("a\nb\n");

        var applied = HostsEditor.Apply(original, HostsEditor.Plan(original, TwoEntries));

        Assert.Equal("a\nb\n127.0.0.1 pubg.com # LunaDNS\n127.0.0.1 lobby.pubg.com # LunaDNS\n", Encoding.ASCII.GetString(applied));
    }

    [Fact]
    public void Apply_terminates_last_line_before_appending()
    {
        var original = Ascii("127.0.0.1 localhost");
        var snapshot = HostsEditor.Plan(original, Pubg);

        Assert.True(snapshot.AddedNewline);
        Assert.Equal(
            "127.0.0.1 localhost\r\n127.0.0.1 prod-live-front.playbattlegrounds.com # LunaDNS\r\n",
            Encoding.ASCII.GetString(HostsEditor.Apply(original, snapshot)));
    }

    [Fact]
    public void Apply_twice_does_not_duplicate_lines()
    {
        var original = Ascii("127.0.0.1 localhost\r\n");
        var snapshot = HostsEditor.Plan(original, Pubg);
        var once = HostsEditor.Apply(original, snapshot);

        Assert.Equal(once, HostsEditor.Apply(once, snapshot));
    }

    [Fact]
    public void Revert_twice_is_noop()
    {
        var original = Ascii("127.0.0.1 localhost");
        var snapshot = HostsEditor.Plan(original, Pubg);
        var once = HostsEditor.Revert(HostsEditor.Apply(original, snapshot), snapshot);

        Assert.Equal(original, once);
        Assert.Equal(once, HostsEditor.Revert(once, snapshot));
    }

    [Fact]
    public void Revert_keeps_lines_added_after_ours()
    {
        var original = Ascii("127.0.0.1 localhost\r\n");
        var snapshot = HostsEditor.Plan(original, Pubg);
        byte[] edited = [.. HostsEditor.Apply(original, snapshot), .. Ascii("10.0.0.5 nas.lan\r\n")];

        Assert.Equal("127.0.0.1 localhost\r\n10.0.0.5 nas.lan\r\n", Encoding.ASCII.GetString(HostsEditor.Revert(edited, snapshot)));
    }

    [Fact]
    public void Revert_does_not_touch_lookalike_lines()
    {
        var snapshot = HostsEditor.Plan([], Pubg);
        var line = snapshot.Lines[0];
        var content = Ascii($"#{line}\r\n{line} extra\r\nx {line}\r\n");

        Assert.Equal(content, HostsEditor.Revert(content, snapshot));
    }

    [Fact]
    public void Plan_removes_duplicate_entries()
    {
        HostsEntry[] twice = [Pubg[0], Pubg[0] with { Host = "PROD-LIVE-FRONT.playbattlegrounds.com." }];

        Assert.Single(HostsEditor.Plan([], twice).Lines);
    }

    [Fact]
    public void Plan_rejects_utf16_file()
    {
        byte[] utf16 = [0xFF, 0xFE, (byte)'a', 0];

        var error = Assert.Throws<ConfiguratorException>(() => HostsEditor.Plan(utf16, Pubg));

        Assert.Equal(ConfiguratorError.HostsUnsupportedEncoding, error.Error);
    }

    [Theory]
    [InlineData("evilpubg.com")]
    [InlineData("example.com")]
    [InlineData("pubg.com\r\n1.2.3.4 example.com")]
    [InlineData("pubg.com example.com")]
    public void Plan_rejects_host_outside_game_domains_or_with_separators(string host)
    {
        HostsEntry[] entries = [new("127.0.0.1", host)];

        Assert.Throws<ArgumentException>(() => HostsEditor.Plan([], entries));
    }

    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("not-an-ip")]
    public void Plan_rejects_non_loopback_address(string address)
    {
        HostsEntry[] entries = [new(address, "pubg.com")];

        Assert.Throws<ArgumentException>(() => HostsEditor.Plan([], entries));
    }

    [Fact]
    public void Plan_rejects_empty_entry_list()
    {
        Assert.Throws<ArgumentException>(() => HostsEditor.Plan([], []));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận lỗi biên dịch**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~HostsEditorTests"`
Expected: build FAIL, `HostsEditor` không tồn tại.

- [ ] **Step 3: Viết `HostsEditor`**

`src/LunaDns.Core/Hosts/HostsEditor.cs`:
```csharp
using System.Net;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Hosts;

/// <summary>
/// Thêm/gỡ dòng của LunaDNS trong nội dung file hosts, làm việc trên byte: phần còn lại của file
/// (kể cả comment tiếng Việt ở UTF-8 hay ANSI) giữ nguyên từng byte. Không đọc/ghi file (spec 4.4, 9).
/// </summary>
public static class HostsEditor
{
    public const string Marker = "# LunaDNS";

    public static HostsSnapshot Plan(ReadOnlySpan<byte> original, IReadOnlyList<HostsEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) throw new ArgumentException("Cần ít nhất một dòng hosts.", nameof(entries));
        EnsureSupportedEncoding(original);
        List<string> lines = [.. entries.Select(FormatLine).Distinct(StringComparer.Ordinal)];
        var lineEnding = original.IndexOf("\r\n"u8) < 0 && original.IndexOf((byte)'\n') >= 0 ? "\n" : "\r\n";
        var addedNewline = original.Length > 0 && original[^1] != (byte)'\n';
        return new HostsSnapshot(lines, lineEnding, addedNewline);
    }

    /// <summary>Thêm các dòng còn thiếu vào cuối file. Chạy lại sau crash không nhân đôi dòng.</summary>
    public static byte[] Apply(ReadOnlySpan<byte> current, HostsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureSupportedEncoding(current);
        var eol = Encoding.ASCII.GetBytes(snapshot.LineEnding);
        var missing = new List<byte[]>();
        foreach (var text in snapshot.Lines)
        {
            var line = Encoding.ASCII.GetBytes(text);
            if (FindLine(current, line, eol, 0) < 0) missing.Add(line);
        }
        if (missing.Count == 0) return current.ToArray();

        using var output = new MemoryStream(current.Length + 128);
        output.Write(current);
        if (current.Length > 0 && current[^1] != (byte)'\n') output.Write(eol);
        foreach (var line in missing)
        {
            output.Write(line);
            output.Write(eol);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Gỡ đúng các dòng đã thêm. Nếu các dòng đó vẫn nằm liền nhau ở cuối file (trường hợp thường gặp)
    /// thì trả lại đúng từng byte như trước Apply, kể cả dấu xuống dòng LunaDNS đã thêm. Chạy lại không đổi gì.
    /// </summary>
    public static byte[] Revert(ReadOnlySpan<byte> current, HostsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var eol = Encoding.ASCII.GetBytes(snapshot.LineEnding);
        var block = Encoding.ASCII.GetBytes(string.Concat(snapshot.Lines.Select(l => l + snapshot.LineEnding)));
        if (current.EndsWith(block) && IsLineStart(current, current.Length - block.Length))
        {
            var cut = current.Length - block.Length;
            if (snapshot.AddedNewline && current[..cut].EndsWith(eol)) cut -= eol.Length;
            return current[..cut].ToArray();
        }

        var result = current.ToArray();
        foreach (var text in snapshot.Lines) result = RemoveLine(result, Encoding.ASCII.GetBytes(text), eol);
        return result;
    }

    private static string FormatLine(HostsEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IPAddress.TryParse(entry.Address, out var address) || !IPAddress.IsLoopback(address))
            throw new ArgumentException("LunaDNS chỉ trỏ domain về chính máy này (địa chỉ loopback).", nameof(entry));
        var host = DomainMatcher.Normalize(entry.Host);
        if (host.Length == 0 || !host.All(IsHostChar) || !DomainMatcher.Matches(host, GameDomains.All))
            throw new ArgumentException("Có domain không thuộc danh sách domain game.", nameof(entry));
        return $"{address} {host} {Marker}";
    }

    private static bool IsHostChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-';

    private static void EnsureSupportedEncoding(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> utf16Le = [0xFF, 0xFE];
        ReadOnlySpan<byte> utf16Be = [0xFE, 0xFF];
        if (content.StartsWith(utf16Le) || content.StartsWith(utf16Be))
            throw new ConfiguratorException(ConfiguratorError.HostsUnsupportedEncoding,
                "File hosts đang lưu dạng UTF-16 nên LunaDNS không sửa. Hãy lưu lại file hosts dạng UTF-8 hoặc ANSI.");
    }

    private static bool IsLineStart(ReadOnlySpan<byte> content, int index) => index == 0 || content[index - 1] == (byte)'\n';

    /// <summary>Vị trí đầu tiên từ <paramref name="from"/> mà <paramref name="line"/> chiếm trọn một dòng; -1 nếu không có.</summary>
    private static int FindLine(ReadOnlySpan<byte> content, ReadOnlySpan<byte> line, ReadOnlySpan<byte> eol, int from)
    {
        while (from <= content.Length - line.Length)
        {
            var relative = content[from..].IndexOf(line);
            if (relative < 0) return -1;
            var at = from + relative;
            var end = at + line.Length;
            if (IsLineStart(content, at) && (end == content.Length || content[end..].StartsWith(eol))) return at;
            from = at + 1;
        }
        return -1;
    }

    private static byte[] RemoveLine(byte[] content, byte[] line, byte[] eol)
    {
        using var output = new MemoryStream(content.Length);
        var from = 0;
        int at;
        while ((at = FindLine(content, line, eol, from)) >= 0)
        {
            output.Write(content, from, at - from);
            var end = at + line.Length;
            from = end == content.Length ? end : end + eol.Length;
        }
        output.Write(content, from, content.Length - from);
        return output.ToArray();
    }
}
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~HostsEditorTests"`
Expected: 20 passed.

- [ ] **Step 5: Chạy lệnh kiểm tra đầy đủ, rồi review**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: 0 warning; 268 passed, 4 skipped (số đếm khác thì ghi số thật vào báo cáo). Không commit.

---

### Task 3: Project `LunaDns.Platform.Windows` + `HostsFileStore`

**Files:**
- Create: `src/LunaDns.Platform.Windows/LunaDns.Platform.Windows.csproj`, `src/LunaDns.Platform.Windows/Hosts/HostsFileStore.cs`
- Create: `tests/LunaDns.Platform.Windows.Tests/LunaDns.Platform.Windows.Tests.csproj`, `TempDirectory.cs`, `AdminFactAttribute.cs`, `Hosts/HostsFileStoreTests.cs`
- Modify: `LunaDns.sln` (qua `dotnet sln add`)

**Interfaces:**
- Consumes: `HostsEditor`, `HostsEntry`, `ConfiguratorException` (Task 1–2).
- Produces:
  - `HostsFileStore(string path)`: `string FilePath`, `static string DefaultPath()`, `byte[] Read()`, `void Write(byte[] content)`. Lỗi quyền được đổi thành `ConfiguratorError.AccessDenied`, lỗi I/O thành `Other`.
  - Test helper: `TempDirectory` (`Path`, `PathOf(name)`), `[AdminFact]`, `[AdminNetworkFact]`, `[Collection(RealSystemCollection.Name)]`.

- [ ] **Step 1: Tạo hai csproj và thêm vào solution**

`src/LunaDns.Platform.Windows/LunaDns.Platform.Windows.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="LunaDns.Platform.Windows.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LunaDns.Core\LunaDns.Core.csproj" />
  </ItemGroup>

</Project>
```

`tests/LunaDns.Platform.Windows.Tests/LunaDns.Platform.Windows.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\LunaDns.Platform.Windows\LunaDns.Platform.Windows.csproj" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln LunaDns.sln add src/LunaDns.Platform.Windows/LunaDns.Platform.Windows.csproj tests/LunaDns.Platform.Windows.Tests/LunaDns.Platform.Windows.Tests.csproj`
Expected: hai dòng "Project … added to the solution", nằm trong thư mục solution `src` và `tests`.

`tests/.editorconfig` có sẵn (tắt CA1707, CA1001 trong `tests/`) nên tự áp dụng cho project test mới.

- [ ] **Step 2: Viết helper test**

`tests/LunaDns.Platform.Windows.Tests/TempDirectory.cs`:
```csharp
namespace LunaDns.Platform.Windows.Tests;

/// <summary>Thư mục tạm riêng cho mỗi test; xoá cả file ReadOnly khi Dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("lunadns-").FullName;

    public string PathOf(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Path)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Path, recursive: true);
    }
}
```

`tests/LunaDns.Platform.Windows.Tests/AdminFactAttribute.cs`:
```csharp
namespace LunaDns.Platform.Windows.Tests;

/// <summary>
/// Test đổi cấu hình thật của Windows (NRPT, file hosts, AutoConfigURL, cache DNS). Chỉ chạy khi đặt LUNADNS_ADMIN_TESTS=1,
/// trong PowerShell chạy bằng quyền Admin. Đặt biến mà không có quyền Admin thì test hỏng (không âm thầm bỏ qua).
/// </summary>
public sealed class AdminFactAttribute : FactAttribute
{
    public AdminFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUNADNS_ADMIN_TESTS") != "1")
            Skip = "Đặt LUNADNS_ADMIN_TESTS=1 và chạy PowerShell bằng quyền Admin để chạy test này.";
    }
}

/// <summary>Như AdminFact, và cần Internet thật (LUNADNS_NETWORK_TESTS=1).</summary>
public sealed class AdminNetworkFactAttribute : FactAttribute
{
    public AdminNetworkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUNADNS_ADMIN_TESTS") != "1"
            || Environment.GetEnvironmentVariable("LUNADNS_NETWORK_TESTS") != "1")
            Skip = "Đặt LUNADNS_ADMIN_TESTS=1 và LUNADNS_NETWORK_TESTS=1, chạy bằng quyền Admin để chạy test này.";
    }
}

/// <summary>Các lớp test chạm cấu hình thật chạy tuần tự, vì chúng so sánh toàn bộ luật NRPT trước và sau.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealSystemCollection
{
    public const string Name = "RealSystem";
}
```

Nếu `DisableParallelization` không có trong xunit 2.5.3 thì bỏ tham số đó: các lớp test cùng một collection vẫn chạy tuần tự với nhau.

- [ ] **Step 3: Viết test cho `HostsFileStore`**

`tests/LunaDns.Platform.Windows.Tests/Hosts/HostsFileStoreTests.cs`:
```csharp
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Hosts;

namespace LunaDns.Platform.Windows.Tests.Hosts;

public sealed class HostsFileStoreTests : IDisposable
{
    private static readonly HostsEntry[] Pubg = [new("127.0.0.1", "prod-live-front.playbattlegrounds.com")];
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string Dacl(string path) =>
        new FileInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);

    [Fact]
    public void Write_keeps_attributes_and_explicit_acl_and_leaves_no_temp_file()
    {
        var path = _dir.PathOf("hosts");
        File.WriteAllBytes(path, "old"u8.ToArray());
        var info = new FileInfo(path);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(acl);
        File.SetAttributes(path, FileAttributes.ReadOnly | FileAttributes.Archive);
        var daclBefore = Dacl(path);

        new HostsFileStore(path).Write("new"u8.ToArray());

        Assert.Equal("new"u8.ToArray(), File.ReadAllBytes(path));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Archive, File.GetAttributes(path));
        Assert.Equal(daclBefore, Dacl(path));
        Assert.False(File.Exists(path + ".lunadns.tmp"));
    }

    [Fact]
    public void Editor_round_trip_through_store_is_byte_identical()
    {
        var path = _dir.PathOf("hosts");
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Máy chủ nội bộ\r\n127.0.0.1 localhost")];
        File.WriteAllBytes(path, original);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var store = new HostsFileStore(path);
        var snapshot = HostsEditor.Plan(store.Read(), Pubg);

        store.Write(HostsEditor.Apply(store.Read(), snapshot));
        Assert.Contains(HostsEditor.Marker, Encoding.UTF8.GetString(store.Read()));
        store.Write(HostsEditor.Revert(store.Read(), snapshot));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(path));
    }

    [Fact]
    public void Read_of_missing_file_is_configurator_error()
    {
        var store = new HostsFileStore(_dir.PathOf("missing"));

        Assert.Throws<ConfiguratorException>(() => store.Read());
    }

    [Fact]
    public void Default_path_points_to_existing_hosts_file()
    {
        var path = HostsFileStore.DefaultPath();

        Assert.EndsWith("hosts", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
    }
}
```

- [ ] **Step 4: Chạy test, xác nhận lỗi biên dịch**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: build FAIL, `HostsFileStore` không tồn tại.

- [ ] **Step 5: Viết `HostsFileStore`**

`src/LunaDns.Platform.Windows/Hosts/HostsFileStore.cs`:
```csharp
using LunaDns.Core.Platform;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Hosts;

/// <summary>
/// Đọc/ghi file hosts thật. Ghi ra file tạm cùng thư mục rồi File.Replace, nên mất điện giữa chừng không để lại file dở.
/// ReplaceFile giữ DACL của file cũ; thuộc tính (ReadOnly…) được gỡ tạm rồi đặt lại (spec 9).
/// </summary>
public sealed class HostsFileStore
{
    public HostsFileStore(string path) => FilePath = path;

    public string FilePath { get; }

    /// <summary>Thư mục hosts theo DataBasePath của Tcpip (mặc định %SystemRoot%\System32\drivers\etc).</summary>
    public static string DefaultPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
        var directory = key?.GetValue("DataBasePath") as string;
        if (string.IsNullOrWhiteSpace(directory)) directory = Path.Combine(Environment.SystemDirectory, "drivers", "etc");
        return Path.Combine(directory, "hosts");
    }

    public byte[] Read() => Guard(() => File.ReadAllBytes(FilePath));

    public void Write(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Guard(() =>
        {
            WriteCore(content);
            return true;
        });
    }

    private void WriteCore(byte[] content)
    {
        var tmp = FilePath + ".lunadns.tmp";
        try
        {
            var attributes = File.GetAttributes(FilePath);
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(FilePath, attributes & ~FileAttributes.ReadOnly);
            try
            {
                File.Replace(tmp, FilePath, destinationBackupFileName: null);
            }
            finally
            {
                File.SetAttributes(FilePath, attributes);
            }
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.AccessDenied,
                "Không có quyền sửa file hosts. LunaDNS cần chạy bằng quyền Admin, hoặc phần mềm diệt virus đang khoá file này.", ex);
        }
        catch (IOException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.Other,
                "Không đọc/ghi được file hosts (có thể đang bị chương trình khác giữ). Hãy thử lại.", ex);
        }
    }
}
```

Nếu test DACL đỏ (tức `File.Replace` không giữ ACE tường minh) thì trong `WriteCore`, ngay trước `File.Replace`, đọc `new FileInfo(FilePath).GetAccessControl(AccessControlSections.Access)`; sau khi replace xong, ghi lại bằng `SetAccessControl`. **Chỉ** dùng phần `Access`, không đụng Owner. Ghi lệch này vào báo cáo task.

- [ ] **Step 6: Chạy test, xác nhận xanh**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: 0 warning; 4 passed.

- [ ] **Step 7: Lệnh kiểm tra đầy đủ, review**

Run: lệnh kiểm tra ở Global Constraints. Expected: Core 268 passed + 4 skipped; Platform 4 passed. Không commit.

---

### Task 4: P/Invoke (`WinInet`, `WindowsDns`) và `AutoProxyRegistry`

**Files:**
- Create: `src/LunaDns.Platform.Windows/NativeMethods.cs`, `WinInet.cs`, `WindowsDns.cs`, `Proxy/AutoProxyRegistry.cs`
- Test: `tests/LunaDns.Platform.Windows.Tests/NativeTests.cs`, `tests/LunaDns.Platform.Windows.Tests/Proxy/AutoProxyRegistryTests.cs`

**Interfaces:**
- Consumes: `AutoProxySnapshot`, `ConfiguratorException`/`ConfiguratorError` (Task 1); `AdminFact`, `RealSystemCollection` (Task 3).
- Produces:
  - `WinInet.NotifySettingsChanged(): bool`, `WindowsDns.FlushResolverCache(): bool`
  - `AutoProxyRegistry(string subKey = InternetSettingsSubKey, Func<bool>? notify = null, ILogger<AutoProxyRegistry>? logger = null)`, gồm:
    - `const InternetSettingsSubKey`, `const ValueName = "AutoConfigURL"`
    - `static string CurrentUserSid()`
    - `AutoProxySnapshot Plan(Uri pacUrl, bool allowOverride, string? userSid = null)`
    - `void Apply(AutoProxySnapshot)`, `void Revert(AutoProxySnapshot)`

- [ ] **Step 1: Viết test**

`tests/LunaDns.Platform.Windows.Tests/NativeTests.cs`:
```csharp
namespace LunaDns.Platform.Windows.Tests;

[Collection(RealSystemCollection.Name)]
public class NativeTests
{
    [Fact]
    public void WinInet_accepts_settings_changed_notification() =>
        Assert.True(WinInet.NotifySettingsChanged());

    [AdminFact]
    public void Dns_cache_flush_succeeds_as_admin() =>
        Assert.True(WindowsDns.FlushResolverCache());
}
```

`tests/LunaDns.Platform.Windows.Tests/Proxy/AutoProxyRegistryTests.cs`:
```csharp
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Proxy;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Tests.Proxy;

/// <summary>Dùng khoá riêng HKU\&lt;SID&gt;\Software\LunaDNS.Tests\&lt;guid&gt; thay cho Internet Settings thật; không cần Admin.</summary>
public sealed class AutoProxyRegistryTests : IDisposable
{
    private static readonly Uri PacUrl = new("http://127.0.0.1:18080/proxy.pac");
    private readonly string _sid = AutoProxyRegistry.CurrentUserSid();
    private readonly string _testKey = $@"Software\LunaDNS.Tests\{Guid.NewGuid():N}";
    private readonly AutoProxyRegistry _registry;
    private int _notifications;

    public AutoProxyRegistryTests()
    {
        Registry.Users.CreateSubKey($@"{_sid}\{_testKey}").Dispose();
        _registry = new AutoProxyRegistry(_testKey, () =>
        {
            _notifications++;
            return true;
        });
    }

    public void Dispose() => Registry.Users.DeleteSubKeyTree($@"{_sid}\{_testKey}", throwOnMissingSubKey: false);

    private RegistryKey Key() => Registry.Users.OpenSubKey($@"{_sid}\{_testKey}", writable: true)!;

    private object? CurrentValue()
    {
        using var key = Key();
        return key.GetValue(AutoProxyRegistry.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    [Fact]
    public void Apply_then_revert_without_previous_value_removes_value()
    {
        var snapshot = _registry.Plan(PacUrl, allowOverride: false);
        Assert.False(snapshot.HadPreviousValue);

        _registry.Apply(snapshot);
        Assert.Equal(PacUrl.AbsoluteUri, CurrentValue());
        _registry.Revert(snapshot);

        Assert.Null(CurrentValue());
        Assert.Equal(2, _notifications);
    }

    [Fact]
    public void Override_restores_previous_expand_string_exactly()
    {
        using (var key = Key()) key.SetValue(AutoProxyRegistry.ValueName, @"%USERPROFILE%\corp.pac", RegistryValueKind.ExpandString);

        var snapshot = _registry.Plan(PacUrl, allowOverride: true);
        _registry.Apply(snapshot);
        _registry.Revert(snapshot);

        using var after = Key();
        Assert.Equal(RegistryValueKind.ExpandString, after.GetValueKind(AutoProxyRegistry.ValueName));
        Assert.Equal(@"%USERPROFILE%\corp.pac", CurrentValue());
    }

    [Fact]
    public void Plan_refuses_existing_pac_without_override()
    {
        using (var key = Key()) key.SetValue(AutoProxyRegistry.ValueName, "http://corp.example/proxy.pac");

        var error = Assert.Throws<ConfiguratorException>(() => _registry.Plan(PacUrl, allowOverride: false));

        Assert.Equal(ConfiguratorError.ExistingProxyConfig, error.Error);
    }

    [Fact]
    public void Plan_refuses_manual_proxy_without_override()
    {
        using (var key = Key()) key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);

        var error = Assert.Throws<ConfiguratorException>(() => _registry.Plan(PacUrl, allowOverride: false));

        Assert.Equal(ConfiguratorError.ExistingProxyConfig, error.Error);
    }

    [Fact]
    public void Revert_keeps_value_changed_after_apply()
    {
        var snapshot = _registry.Plan(PacUrl, allowOverride: false);
        _registry.Apply(snapshot);
        using (var key = Key()) key.SetValue(AutoProxyRegistry.ValueName, "http://user-choice.example/x.pac");

        _registry.Revert(snapshot);

        Assert.Equal("http://user-choice.example/x.pac", CurrentValue());
        Assert.Equal(1, _notifications);
    }

    [Fact]
    public void Revert_twice_is_noop()
    {
        var snapshot = _registry.Plan(PacUrl, allowOverride: false);
        _registry.Apply(snapshot);

        _registry.Revert(snapshot);
        _registry.Revert(snapshot);

        Assert.Null(CurrentValue());
        Assert.Equal(2, _notifications);
    }

    [Theory]
    [InlineData("http://example.com/proxy.pac")]
    [InlineData("https://127.0.0.1:18080/proxy.pac")]
    public void Plan_rejects_non_loopback_or_non_http_url(string url)
    {
        Assert.Throws<ArgumentException>(() => _registry.Plan(new Uri(url), allowOverride: true));
    }

    [Fact]
    public void Revert_for_user_without_loaded_hive_throws_hive_not_loaded()
    {
        var snapshot = new AutoProxySnapshot("S-1-5-21-1-2-3-999999", false, null, false, PacUrl.AbsoluteUri);

        var error = Assert.Throws<ConfiguratorException>(() => _registry.Revert(snapshot));

        Assert.Equal(ConfiguratorError.UserHiveNotLoaded, error.Error);
    }

    [Fact]
    public void Revert_rejects_malformed_sid()
    {
        var snapshot = new AutoProxySnapshot(@"..\Other", false, null, false, PacUrl.AbsoluteUri);

        Assert.Throws<ArgumentException>(() => _registry.Revert(snapshot));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận lỗi biên dịch**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: build FAIL (`WinInet`, `WindowsDns`, `AutoProxyRegistry` không tồn tại).

- [ ] **Step 3: Viết P/Invoke**

`src/LunaDns.Platform.Windows/NativeMethods.cs`:
```csharp
using System.Runtime.InteropServices;

namespace LunaDns.Platform.Windows;

internal static partial class NativeMethods
{
    internal const int InternetOptionRefresh = 37;
    internal const int InternetOptionSettingsChanged = 39;

    [LibraryImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DnsFlushResolverCache();
}
```

`src/LunaDns.Platform.Windows/WinInet.cs`:
```csharp
namespace LunaDns.Platform.Windows;

public static class WinInet
{
    /// <summary>Báo cho WinINet đọc lại cấu hình proxy (INTERNET_OPTION_SETTINGS_CHANGED rồi REFRESH). False nếu Windows từ chối.</summary>
    public static bool NotifySettingsChanged()
    {
        var changed = NativeMethods.InternetSetOption(IntPtr.Zero, NativeMethods.InternetOptionSettingsChanged, IntPtr.Zero, 0);
        var refreshed = NativeMethods.InternetSetOption(IntPtr.Zero, NativeMethods.InternetOptionRefresh, IntPtr.Zero, 0);
        return changed && refreshed;
    }
}
```

`src/LunaDns.Platform.Windows/WindowsDns.cs`:
```csharp
namespace LunaDns.Platform.Windows;

public static class WindowsDns
{
    /// <summary>Xoá cache của DNS Client (tương đương ipconfig /flushdns). Cần quyền Admin; false nếu Windows từ chối.</summary>
    public static bool FlushResolverCache() => NativeMethods.DnsFlushResolverCache();
}
```

- [ ] **Step 4: Viết `AutoProxyRegistry`**

`src/LunaDns.Platform.Windows/Proxy/AutoProxyRegistry.cs`:
```csharp
using System.Security;
using System.Security.Principal;
using LunaDns.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Proxy;

/// <summary>
/// AutoConfigURL (WinINet) của MỘT người dùng, mở qua HKEY_USERS\&lt;SID&gt; chứ không qua HKCU: tác vụ dọn dẹp chạy bằng SYSTEM (KH4b)
/// vẫn hoàn tác đúng hive của người đã bật. Hoàn tác chỉ khi giá trị vẫn là của LunaDNS. Không đụng DefaultConnectionSettings hay WPAD (spec 4.5).
/// </summary>
public sealed partial class AutoProxyRegistry
{
    public const string InternetSettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string ValueName = "AutoConfigURL";
    private const string ProxyEnableName = "ProxyEnable";

    private readonly string _subKey;
    private readonly Func<bool> _notify;
    private readonly ILogger _logger;

    public AutoProxyRegistry(string subKey = InternetSettingsSubKey, Func<bool>? notify = null, ILogger<AutoProxyRegistry>? logger = null)
    {
        _subKey = subKey;
        _notify = notify ?? WinInet.NotifySettingsChanged;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public static string CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value
            ?? throw new ConfiguratorException(ConfiguratorError.Other, "Không đọc được SID của người dùng hiện tại.");
    }

    public AutoProxySnapshot Plan(Uri pacUrl, bool allowOverride, string? userSid = null)
    {
        ArgumentNullException.ThrowIfNull(pacUrl);
        if (!pacUrl.IsAbsoluteUri || pacUrl.Scheme != Uri.UriSchemeHttp || !pacUrl.IsLoopback)
            throw new ArgumentException("Địa chỉ PAC phải có dạng http://127.0.0.1:<cổng>/proxy.pac.", nameof(pacUrl));
        var sid = userSid ?? CurrentUserSid();
        EnsureHiveLoaded(sid);

        using var key = Registry.Users.OpenSubKey(KeyPath(sid));
        var hadValue = key?.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase) == true;
        string? previous = null;
        var expand = false;
        if (hadValue)
        {
            var kind = key!.GetValueKind(ValueName);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                throw new ConfiguratorException(ConfiguratorError.UnexpectedRegistryValue,
                    "Giá trị AutoConfigURL hiện có không phải chuỗi nên LunaDNS không ghi đè.");
            previous = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            expand = kind == RegistryValueKind.ExpandString;
        }
        var manualProxy = key?.GetValue(ProxyEnableName) is int enabled && enabled != 0;
        if (!allowOverride && (!string.IsNullOrEmpty(previous) || manualProxy))
            throw new ConfiguratorException(ConfiguratorError.ExistingProxyConfig,
                "Máy đang dùng cấu hình proxy riêng (file PAC hoặc proxy thủ công). Bật Proxy SNI sẽ thay cấu hình đó cho tới khi tắt.");
        return new AutoProxySnapshot(sid, hadValue, previous, expand, pacUrl.AbsoluteUri);
    }

    public void Apply(AutoProxySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureHiveLoaded(snapshot.UserSid);
        WithRegistryAccess(() =>
        {
            using var key = Registry.Users.CreateSubKey(KeyPath(snapshot.UserSid), writable: true);
            key.SetValue(ValueName, snapshot.NewValue, RegistryValueKind.String);
        });
        Notify();
    }

    public void Revert(AutoProxySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureHiveLoaded(snapshot.UserSid);
        var changed = false;
        WithRegistryAccess(() =>
        {
            using var key = Registry.Users.OpenSubKey(KeyPath(snapshot.UserSid), writable: true);
            var current = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (key is null || !string.Equals(current, snapshot.NewValue, StringComparison.Ordinal))
            {
                LogValueNotOurs(_logger);
                return;
            }
            if (snapshot.HadPreviousValue)
                key.SetValue(ValueName, snapshot.PreviousValue ?? string.Empty,
                    snapshot.PreviousWasExpandString ? RegistryValueKind.ExpandString : RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            changed = true;
        });
        if (changed) Notify();
    }

    private string KeyPath(string sid) => $@"{sid}\{_subKey}";

    private void Notify()
    {
        if (!_notify()) LogNotifyFailed(_logger);
    }

    /// <summary>SID lấy từ journal: kiểm tra đúng định dạng (chặn đường dẫn lạ) và hive của người đó đang được nạp.</summary>
    private static void EnsureHiveLoaded(string sid)
    {
        _ = new SecurityIdentifier(sid);
        using var hive = Registry.Users.OpenSubKey(sid);
        if (hive is null)
            throw new ConfiguratorException(ConfiguratorError.UserHiveNotLoaded,
                "Người dùng đã bật Proxy SNI hiện chưa đăng nhập; cấu hình proxy sẽ được khôi phục khi người đó đăng nhập.");
    }

    private static void WithRegistryAccess(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            throw new ConfiguratorException(ConfiguratorError.AccessDenied, "Không có quyền sửa cấu hình proxy của người dùng.", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AutoConfigURL không còn là giá trị LunaDNS đã ghi (đã đổi hoặc đã hoàn tác); giữ nguyên.")]
    private static partial void LogValueNotOurs(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows không nhận thông báo đổi cấu hình proxy (InternetSetOption).")]
    private static partial void LogNotifyFailed(ILogger logger);
}
```

Lưu ý: `new SecurityIdentifier("..\\Other")` ném `ArgumentException`, và test `Revert_rejects_malformed_sid` dựa vào điều đó. Revert lần hai không gọi `Notify` nên `_notifications` dừng ở 2, đúng như test chờ.

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: 0 warning; 15 passed, 1 skipped (`Dns_cache_flush_succeeds_as_admin`).

- [ ] **Step 6: Lệnh kiểm tra đầy đủ, review**

Run: lệnh kiểm tra ở Global Constraints. Expected: Core 268 + 4 skipped; Platform 15 + 1 skipped. Sau khi chạy, không còn khoá `HKCU\Software\LunaDNS.Tests\*` (kiểm tra: `Test-Path HKCU:\Software\LunaDNS.Tests` → chỉ còn khoá cha rỗng hoặc `False`). Không commit.

---

### Task 5: NRPT — `NrptDnsRouter` + `NrptCimStore` (CIM)

**Điều kiện:** PO đã duyệt quyết định 2 (package `Microsoft.Management.Infrastructure` 3.0.0) ngày 13/11/2026.

**Files:**
- Modify: `src/LunaDns.Platform.Windows/LunaDns.Platform.Windows.csproj` (thêm package)
- Create: `src/LunaDns.Platform.Windows/Nrpt/NrptRule.cs`, `INrptStore.cs`, `NrptDnsRouter.cs`, `NrptCimStore.cs`
- Test: `tests/LunaDns.Platform.Windows.Tests/Nrpt/FakeNrptStore.cs`, `Nrpt/NrptDnsRouterTests.cs`, `Nrpt/NrptCimStoreTests.cs`

**Interfaces:**
- Consumes: `DnsRouteSnapshot`, `ConfiguratorException` (Task 1); `DomainMatcher`, `GameDomains` (KH1); `DohClient`, `DohServer.Defaults`, `DnsQueryHandler`, `DnsCache`, `DohStubServer.Start/DefaultEndPoint` (KH2); `KnownBlockIps.IsBlockAddress` (KH3); `WindowsDns.FlushResolverCache` (Task 4).
- Produces:
  - `record NrptRule(string Name, IReadOnlyList<string> Namespaces, IReadOnlyList<string> NameServers, string? Comment, string? DisplayName)`
  - `interface INrptStore { IReadOnlyList<NrptRule> List(); void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName); void Remove(string name); }`
  - `NrptDnsRouter(INrptStore)`: `const DisplayName = "LunaDNS"`, `const MarkerPrefix = "LunaDNS "`, `Plan(domains, IPAddress)`, `Apply(snapshot)`, `Revert(snapshot)`
  - `NrptCimStore : INrptStore`

- [ ] **Step 1: Thêm package và build ngay (phát hiện sớm cảnh báo NuGet)**

Run: `dotnet add src/LunaDns.Platform.Windows package Microsoft.Management.Infrastructure --version 3.0.0; dotnet build -warnaserror`
Expected: build xanh. Nếu có cảnh báo NU1701/NU1603 hay lỗi restore thì **dừng**, chép nguyên văn cảnh báo vào báo cáo, không hạ mức cảnh báo.

- [ ] **Step 2: Viết kiểu NRPT và fake**

`src/LunaDns.Platform.Windows/Nrpt/NrptRule.cs`:
```csharp
namespace LunaDns.Platform.Windows.Nrpt;

/// <summary>Một luật NRPT như Windows trả về. Name là GUID dạng "{…}" do Windows đặt.</summary>
public sealed record NrptRule(string Name, IReadOnlyList<string> Namespaces, IReadOnlyList<string> NameServers, string? Comment, string? DisplayName);
```

`src/LunaDns.Platform.Windows/Nrpt/INrptStore.cs`:
```csharp
namespace LunaDns.Platform.Windows.Nrpt;

/// <summary>Kho luật NRPT local. Bản thật: NrptCimStore. Không có thao tác sửa luật: chỉ thêm và gỡ theo Name.</summary>
public interface INrptStore
{
    IReadOnlyList<NrptRule> List();
    void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName);
    void Remove(string name);
}
```

`tests/LunaDns.Platform.Windows.Tests/Nrpt/FakeNrptStore.cs`:
```csharp
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.Platform.Windows.Tests.Nrpt;

internal sealed class FakeNrptStore : INrptStore
{
    public List<NrptRule> Rules { get; } = [];
    public int AddCalls { get; private set; }
    public bool FailAdd { get; set; }

    public IReadOnlyList<NrptRule> List() => [.. Rules];

    public void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName)
    {
        AddCalls++;
        if (FailAdd) throw new ConfiguratorException(ConfiguratorError.NrptFailed, "giả lập lỗi");
        Rules.Add(new NrptRule("{" + Guid.NewGuid() + "}", [.. namespaces], [nameServer], comment, displayName));
    }

    public void Remove(string name) => Rules.RemoveAll(r => r.Name == name);

    /// <summary>Giống luật Tailscale đo trên máy PO: GUID cố định, không Comment/DisplayName.</summary>
    public static NrptRule Tailscale(params string[] extraNamespaces) => new(
        "{5abe529b-675b-4486-8459-25a634dacc23}",
        [".100.100.in-addr.arpa", ".tail1234.ts.net", .. extraNamespaces],
        ["100.100.100.100", "fd7a:115c:a1e0::53"],
        null,
        null);
}
```

- [ ] **Step 3: Viết test cho `NrptDnsRouter`**

`tests/LunaDns.Platform.Windows.Tests/Nrpt/NrptDnsRouterTests.cs`:
```csharp
using System.Net;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.Platform.Windows.Tests.Nrpt;

public class NrptDnsRouterTests
{
    private static readonly string[] Steam = ["steampowered.com", "steamserver.net"];
    private static readonly string[] SteamApex = ["steampowered.com"];
    private static readonly string[] SteamNamespaces = [".steampowered.com", "steampowered.com", ".steamserver.net", "steamserver.net"];
    private static readonly string[] SkippedSuffix = [".steampowered.com"];

    private readonly FakeNrptStore _store = new();
    private readonly NrptRule _tailscale = FakeNrptStore.Tailscale();
    private readonly NrptDnsRouter _router;

    public NrptDnsRouterTests()
    {
        _store.Rules.Add(_tailscale);
        _router = new NrptDnsRouter(_store);
    }

    [Fact]
    public void Plan_routes_suffix_and_exact_name_to_stub()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        Assert.Equal(SteamNamespaces, snapshot.Namespaces);
        Assert.Equal("127.0.0.1", snapshot.NameServer);
        Assert.Empty(snapshot.SkippedNamespaces);
        Assert.StartsWith(NrptDnsRouter.MarkerPrefix, snapshot.Marker);
    }

    [Fact]
    public void Apply_then_revert_keeps_tailscale_rule_untouched()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        _router.Apply(snapshot);
        var ours = Assert.Single(_store.Rules, r => r.Comment == snapshot.Marker);
        Assert.Equal(NrptDnsRouter.DisplayName, ours.DisplayName);
        Assert.Equal(2, _store.Rules.Count);
        _router.Revert(snapshot);

        Assert.Same(_tailscale, Assert.Single(_store.Rules));
    }

    [Fact]
    public void Plan_skips_namespace_owned_by_foreign_rule_case_insensitively()
    {
        _store.Rules[0] = FakeNrptStore.Tailscale(".STEAMPOWERED.com");

        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        Assert.Equal(SkippedSuffix, snapshot.SkippedNamespaces);
        Assert.DoesNotContain(".steampowered.com", snapshot.Namespaces);
        Assert.Contains("steampowered.com", snapshot.Namespaces);
    }

    [Fact]
    public void Plan_throws_when_every_namespace_belongs_to_foreign_rule()
    {
        _store.Rules.Add(new NrptRule("{corp}", [".steampowered.com", "steampowered.com"], ["10.0.0.1"], "corp vpn", null));

        var error = Assert.Throws<ConfiguratorException>(() => _router.Plan(SteamApex, IPAddress.Loopback));

        Assert.Equal(ConfiguratorError.DnsRouteConflict, error.Error);
    }

    [Fact]
    public void Apply_twice_adds_one_rule()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        _router.Apply(snapshot);
        _router.Apply(snapshot);

        Assert.Equal(1, _store.AddCalls);
    }

    [Fact]
    public void Revert_before_apply_and_revert_twice_are_noops()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        _router.Revert(snapshot);
        _router.Apply(snapshot);
        _router.Revert(snapshot);
        _router.Revert(snapshot);

        Assert.Same(_tailscale, Assert.Single(_store.Rules));
    }

    [Fact]
    public void Revert_removes_only_rule_with_its_own_marker()
    {
        var first = _router.Plan(SteamApex, IPAddress.Loopback);
        var second = _router.Plan(Steam, IPAddress.Loopback);
        _router.Apply(first);
        _router.Apply(second);

        _router.Revert(first);

        Assert.Equal(2, _store.Rules.Count);
        Assert.Contains(_store.Rules, r => r.Comment == second.Marker);
    }

    [Fact]
    public void Snapshot_without_lunadns_marker_never_touches_rules()
    {
        _store.Rules.Add(new NrptRule("{empty-comment}", [".example.org"], ["10.0.0.1"], "", null));
        var bad = new DnsRouteSnapshot("", SteamNamespaces, "127.0.0.1", []);

        Assert.Throws<ArgumentException>(() => _router.Revert(bad));
        Assert.Throws<ArgumentException>(() => _router.Apply(bad));
        Assert.Equal(2, _store.Rules.Count);
    }

    [Theory]
    [InlineData("evilsteampowered.com")]
    [InlineData("example.com")]
    public void Plan_rejects_non_game_domain(string domain)
    {
        string[] domains = [domain];

        Assert.Throws<ArgumentException>(() => _router.Plan(domains, IPAddress.Loopback));
    }

    [Fact]
    public void Plan_rejects_non_loopback_name_server()
    {
        Assert.Throws<ArgumentException>(() => _router.Plan(Steam, IPAddress.Parse("1.1.1.1")));
    }
}
```

- [ ] **Step 4: Viết test cho `NrptCimStore` (1 test thường, 2 test admin)**

`tests/LunaDns.Platform.Windows.Tests/Nrpt/NrptCimStoreTests.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;
using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.Platform.Windows.Tests.Nrpt;

[Collection(RealSystemCollection.Name)]
public class NrptCimStoreTests
{
    private static readonly string[] TestNamespace = [".lunadns-admin-test.invalid"];
    private static readonly string[] LoopbackServer = ["127.0.0.1"];
    private static readonly string[] SteamApex = ["steampowered.com"];
    private readonly NrptCimStore _store = new();

    private static string Describe(NrptRule r) =>
        $"{r.Name}|{string.Join(',', r.Namespaces)}|{string.Join(',', r.NameServers)}|{r.Comment}|{r.DisplayName}";

    private string[] AllRules() => [.. _store.List().Select(Describe).Order(StringComparer.Ordinal)];

    [Fact]
    public void List_works_without_admin()
    {
        foreach (var rule in _store.List()) Assert.False(string.IsNullOrEmpty(rule.Name));
    }

    [AdminFact]
    public void Add_and_remove_real_rule_leave_other_rules_untouched()
    {
        var before = AllRules();
        var comment = NrptDnsRouter.MarkerPrefix + "admin-test-" + Guid.NewGuid().ToString("N");
        try
        {
            _store.Add(TestNamespace, "127.0.0.1", comment, NrptDnsRouter.DisplayName);
            var added = Assert.Single(_store.List(), r => r.Comment == comment);
            Assert.Equal(TestNamespace, added.Namespaces);
            Assert.Equal(LoopbackServer, added.NameServers);
            Assert.Equal(NrptDnsRouter.DisplayName, added.DisplayName);
            _store.Remove(added.Name);
        }
        finally
        {
            foreach (var leftover in _store.List().Where(r => r.Comment == comment)) _store.Remove(leftover.Name);
        }

        Assert.Equal(before, AllRules());
    }

    /// <summary>Mạng PO: DNS hệ thống trả 127.0.0.1 cho store.steampowered.com. Có NRPT → stub → DoH thì phải ra IP thật.</summary>
    [AdminNetworkFact]
    public async Task Steam_resolves_through_stub_while_routed_and_rules_restore_after()
    {
        var before = AllRules();
        using var doh = DohClient.Create(DohServer.Defaults);
        var handler = new DnsQueryHandler(GameDomains.All, doh, new DnsCache());
        await using var stub = DohStubServer.Start(DohStubServer.DefaultEndPoint, handler);
        var router = new NrptDnsRouter(_store);
        var snapshot = router.Plan(SteamApex, IPAddress.Loopback);
        try
        {
            router.Apply(snapshot);
            Assert.True(WindowsDns.FlushResolverCache());

            var addresses = await System.Net.Dns.GetHostAddressesAsync("store.steampowered.com", AddressFamily.InterNetwork);

            Assert.NotEmpty(addresses);
            Assert.DoesNotContain(addresses, KnownBlockIps.IsBlockAddress);
        }
        finally
        {
            router.Revert(snapshot);
            WindowsDns.FlushResolverCache();
        }

        Assert.Equal(before, AllRules());
    }
}
```

- [ ] **Step 5: Chạy test, xác nhận lỗi biên dịch**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: build FAIL (`NrptDnsRouter`, `NrptCimStore` không tồn tại).

- [ ] **Step 6: Viết `NrptDnsRouter`**

`src/LunaDns.Platform.Windows/Nrpt/NrptDnsRouter.cs`:
```csharp
using System.Net;
using LunaDns.Core.Domains;
using LunaDns.Core.Platform;

namespace LunaDns.Platform.Windows.Nrpt;

/// <summary>
/// Định tuyến DNS bằng NRPT, chỉ trên luật mang dấu của LunaDNS (Comment = Marker trong snapshot, luôn bắt đầu bằng "LunaDNS ").
/// Luật của phần mềm khác (ví dụ Tailscale: GUID cố định, không Comment) không bao giờ bị sửa hay xoá; namespace trùng y hệt
/// với luật lạ thì bỏ qua (spike 2026-11, spec 4.2). Luật lạ cụ thể hơn (ví dụ ".store.steampowered.com") vẫn thắng theo NRPT.
/// </summary>
public sealed class NrptDnsRouter(INrptStore store)
{
    public const string DisplayName = "LunaDNS";
    public const string MarkerPrefix = "LunaDNS ";

    public DnsRouteSnapshot Plan(IReadOnlyList<string> domains, IPAddress nameServer)
    {
        ArgumentNullException.ThrowIfNull(domains);
        ArgumentNullException.ThrowIfNull(nameServer);
        if (!IPAddress.IsLoopback(nameServer))
            throw new ArgumentException("NRPT của LunaDNS chỉ được trỏ về stub trên chính máy này.", nameof(nameServer));

        var wanted = new List<string>();
        foreach (var domain in domains)
        {
            var host = DomainMatcher.Normalize(domain);
            if (!DomainMatcher.Matches(host, GameDomains.All))
                throw new ArgumentException("Có domain không thuộc danh sách domain game.", nameof(domains));
            wanted.Add("." + host); // mọi subdomain
            wanted.Add(host);       // chính tên gốc: luật hậu tố không chắc khớp tên gốc
        }

        var foreign = store.List()
            .Where(r => !IsLunaDns(r))
            .SelectMany(r => r.Namespaces)
            .Select(n => n.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var distinct = wanted.Distinct(StringComparer.Ordinal).ToList();
        List<string> namespaces = [.. distinct.Where(n => !foreign.Contains(n))];
        List<string> skipped = [.. distinct.Where(foreign.Contains)];
        if (namespaces.Count == 0)
            throw new ConfiguratorException(ConfiguratorError.DnsRouteConflict,
                "Các domain game đã có luật DNS của phần mềm khác (ví dụ VPN). LunaDNS không sửa luật đó nên không bật được Định tuyến DNS.");
        return new DnsRouteSnapshot(MarkerPrefix + Guid.NewGuid().ToString("N"), namespaces, nameServer.ToString(), skipped);
    }

    /// <summary>Chạy lại sau crash không tạo luật thứ hai.</summary>
    public void Apply(DnsRouteSnapshot snapshot)
    {
        EnsureMarker(snapshot);
        if (store.List().Any(r => IsOurs(r, snapshot))) return;
        store.Add(snapshot.Namespaces, snapshot.NameServer, snapshot.Marker, DisplayName);
    }

    public void Revert(DnsRouteSnapshot snapshot)
    {
        EnsureMarker(snapshot);
        foreach (var rule in store.List().Where(r => IsOurs(r, snapshot))) store.Remove(rule.Name);
    }

    private static bool IsLunaDns(NrptRule rule) =>
        rule.Comment?.StartsWith(MarkerPrefix, StringComparison.Ordinal) == true;

    private static bool IsOurs(NrptRule rule, DnsRouteSnapshot snapshot) =>
        IsLunaDns(rule) && string.Equals(rule.Comment, snapshot.Marker, StringComparison.Ordinal);

    /// <summary>Snapshot đến từ journal trên đĩa: dấu rỗng hay lạ thì từ chối, để không bao giờ khớp nhầm luật không Comment.</summary>
    private static void EnsureMarker(DnsRouteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.Marker.StartsWith(MarkerPrefix, StringComparison.Ordinal) || snapshot.Marker.Length == MarkerPrefix.Length)
            throw new ArgumentException("Snapshot NRPT không mang dấu của LunaDNS.", nameof(snapshot));
    }
}
```

- [ ] **Step 7: Viết `NrptCimStore`**

`src/LunaDns.Platform.Windows/Nrpt/NrptCimStore.cs`:
```csharp
using System.Globalization;
using LunaDns.Core.Platform;
using Microsoft.Management.Infrastructure;

namespace LunaDns.Platform.Windows.Nrpt;

/// <summary>
/// Luật NRPT local qua CIM, lớp root\Microsoft\Windows\DNS:PS_DnsClientNrptRule. Đây chính là lớp mà
/// Add/Get/Remove-DnsClientNrptRule gọi (đọc từ PS_DnsClientNRPTRule_v1.0.0.cdxml). Không chạy PowerShell (spec 3).
/// Get không cần quyền Admin; Add và Remove thì cần.
/// </summary>
public sealed class NrptCimStore : INrptStore
{
    private const string CimNamespace = @"root\Microsoft\Windows\DNS";
    private const string CimClass = "PS_DnsClientNrptRule";

    public IReadOnlyList<NrptRule> List() => Invoke("Get", _ => { }, ReadRules);

    public void Add(IReadOnlyList<string> namespaces, string nameServer, string comment, string displayName)
    {
        ArgumentNullException.ThrowIfNull(namespaces);
        Invoke("Add", p =>
        {
            p.Add(CimMethodParameter.Create("Namespace", namespaces.ToArray(), CimType.StringArray, CimFlags.In));
            p.Add(CimMethodParameter.Create("NameServers", new[] { nameServer }, CimType.StringArray, CimFlags.In));
            p.Add(CimMethodParameter.Create("Comment", comment, CimType.String, CimFlags.In));
            p.Add(CimMethodParameter.Create("DisplayName", displayName, CimType.String, CimFlags.In));
        }, _ => true);
    }

    public void Remove(string name) =>
        Invoke("Remove", p =>
        {
            p.Add(CimMethodParameter.Create("Name", name, CimType.String, CimFlags.In));
            p.Add(CimMethodParameter.Create("Force", true, CimType.Boolean, CimFlags.In));
        }, _ => true);

    private static T Invoke<T>(string method, Action<CimMethodParametersCollection> fill, Func<CimMethodResult, T> read)
    {
        try
        {
            using var session = CimSession.Create(null);
            using var parameters = new CimMethodParametersCollection();
            fill(parameters);
            var result = session.InvokeMethod(CimNamespace, CimClass, method, parameters);
            var code = Convert.ToUInt32(result.ReturnValue?.Value ?? 0u, CultureInfo.InvariantCulture);
            if (code != 0)
                throw new ConfiguratorException(ConfiguratorError.NrptFailed, $"Windows từ chối thao tác NRPT '{method}' (mã {code}).");
            return read(result);
        }
        catch (CimException ex) when (ex.NativeErrorCode == NativeErrorCode.AccessDenied)
        {
            throw new ConfiguratorException(ConfiguratorError.AccessDenied, "Sửa luật DNS (NRPT) cần quyền Admin.", ex);
        }
        catch (CimException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.NrptFailed, "Không sửa được luật DNS (NRPT) của Windows.", ex);
        }
    }

    private static IReadOnlyList<NrptRule> ReadRules(CimMethodResult result)
    {
        if (result.OutParameters["cmdletOutput"]?.Value is not CimInstance[] instances) return [];
        var rules = new List<NrptRule>(instances.Length);
        foreach (var instance in instances)
        {
            using (instance) rules.Add(ToRule(instance));
        }
        return rules;
    }

    private static NrptRule ToRule(CimInstance instance)
    {
        var p = instance.CimInstanceProperties;
        return new NrptRule(
            p["Name"]?.Value as string ?? string.Empty,
            p["Namespace"]?.Value as string[] ?? [],
            p["NameServers"]?.Value as string[] ?? [],
            p["Comment"]?.Value as string,
            p["DisplayName"]?.Value as string);
    }
}
```

Nếu API của MMI 3.0.0 lệch với mã trên (tên `CimMethodResult.ReturnValue`, `OutParameters`, `NativeErrorCode.AccessDenied`, hay `CimFlags.In`), sửa cho khớp API thật của package và ghi lệch vào báo cáo. **Không** đổi lớp CIM hay tên tham số: các tên đó đã đọc từ máy PO.

- [ ] **Step 8: Chạy test, xác nhận xanh**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: 0 warning; 27 passed, 3 skipped (test flush, CIM admin, CIM admin + mạng). Test `List_works_without_admin` phải thấy được 2 luật Tailscale trên máy PO. Nếu nó ném `ConfiguratorException` thì lớp CIM không dùng được ở mức không có Admin: dừng lại và báo.

- [ ] **Step 9: Lệnh kiểm tra đầy đủ, review**

Run: lệnh kiểm tra ở Global Constraints. Expected: Core 268 + 4 skipped; Platform 27 + 3 skipped. Không commit.

---

### Task 6: `WindowsNetworkConfigurator` và vòng journal đầy đủ

**Files:**
- Create: `src/LunaDns.Platform.Windows/WindowsNetworkConfigurator.cs`
- Test: `tests/LunaDns.Platform.Windows.Tests/WindowsNetworkConfiguratorTests.cs`, `tests/LunaDns.Platform.Windows.Tests/WindowsNetworkConfiguratorAdminTests.cs`

**Interfaces:**
- Consumes: mọi thứ của Task 1–5; `ChangeJournal`, `JsonFileJournalStore` (KH1).
- Produces:
  - `WindowsNetworkConfigurator(NrptDnsRouter, AutoProxyRegistry, HostsFileStore, Func<bool>? flushDnsCache = null, ILogger<WindowsNetworkConfigurator>? logger = null) : INetworkConfigurator`
  - `static WindowsNetworkConfigurator CreateDefault(ILoggerFactory? loggerFactory = null)`
  
  KH5 dựng như sau:
  ```csharp
  var cfg = WindowsNetworkConfigurator.CreateDefault(lf);
  var journal = new ChangeJournal(store, SystemChanges.Reverters(cfg));
  var changes = new SystemChanges(journal, cfg);
  ```

- [ ] **Step 1: Viết test (không cần Admin)**

`tests/LunaDns.Platform.Windows.Tests/WindowsNetworkConfiguratorTests.cs`:
```csharp
using System.Net;
using System.Text;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;
using LunaDns.Platform.Windows.Tests.Nrpt;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Tests;

public sealed class WindowsNetworkConfiguratorTests : IDisposable
{
    private static readonly string[] Steam = ["steampowered.com", "steamcommunity.com", "steamserver.net"];
    private static readonly HostsEntry[] Pubg = [new("127.0.0.1", "prod-live-front.playbattlegrounds.com")];
    private static readonly Uri PacUrl = new("http://127.0.0.1:18080/proxy.pac");
    private static readonly byte[] OriginalHosts = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# hosts thử\r\n127.0.0.1 localhost")];

    private readonly TempDirectory _dir = new();
    private readonly string _sid = AutoProxyRegistry.CurrentUserSid();
    private readonly string _proxyKey = $@"Software\LunaDNS.Tests\{Guid.NewGuid():N}";
    private readonly FakeNrptStore _nrpt = new();
    private readonly NrptRule _tailscale = FakeNrptStore.Tailscale();
    private int _flushes;

    public WindowsNetworkConfiguratorTests()
    {
        _nrpt.Rules.Add(_tailscale);
        File.WriteAllBytes(HostsPath, OriginalHosts);
        File.SetAttributes(HostsPath, FileAttributes.ReadOnly);
        Registry.Users.CreateSubKey($@"{_sid}\{_proxyKey}").Dispose();
    }

    private string HostsPath => _dir.PathOf("hosts");

    private string JournalPath => _dir.PathOf("journal.json");

    public void Dispose()
    {
        Registry.Users.DeleteSubKeyTree($@"{_sid}\{_proxyKey}", throwOnMissingSubKey: false);
        _dir.Dispose();
    }

    private WindowsNetworkConfigurator NewConfigurator() => new(
        new NrptDnsRouter(_nrpt),
        new AutoProxyRegistry(_proxyKey, () => true),
        new HostsFileStore(HostsPath),
        () =>
        {
            _flushes++;
            return true;
        });

    private ChangeJournal NewJournal(INetworkConfigurator cfg) =>
        new(new JsonFileJournalStore(JournalPath), SystemChanges.Reverters(cfg));

    [Fact]
    public void Three_changes_survive_restart_and_roll_back_to_original_state()
    {
        var cfg = NewConfigurator();
        var changes = new SystemChanges(NewJournal(cfg), cfg);
        changes.AddDnsRoute("dns-routing", Steam, IPAddress.Loopback);
        changes.SetAutoProxy("sni-proxy", PacUrl);
        changes.AddHostsEntries("pubg", Pubg);
        Assert.Equal(2, _nrpt.Rules.Count);
        Assert.Contains("# LunaDNS", Encoding.UTF8.GetString(File.ReadAllBytes(HostsPath)));

        // "Crash": tiến trình mới đọc lại journal từ file.
        var restarted = NewJournal(NewConfigurator());
        Assert.Equal(3, restarted.Entries.Count);
        var result = restarted.RollbackAll();

        Assert.True(result.Success);
        Assert.Equal(3, result.Reverted);
        Assert.Empty(restarted.Entries);
        Assert.Same(_tailscale, Assert.Single(_nrpt.Rules));
        Assert.Equal(OriginalHosts, File.ReadAllBytes(HostsPath));
        Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(HostsPath));
        using var key = Registry.Users.OpenSubKey($@"{_sid}\{_proxyKey}")!;
        Assert.Null(key.GetValue(AutoProxyRegistry.ValueName));
        Assert.Equal(4, _flushes); // apply dns, apply hosts, revert hosts, revert dns
    }

    [Fact]
    public void Failed_nrpt_add_leaves_entry_that_feature_rollback_clears()
    {
        _nrpt.FailAdd = true;
        var cfg = NewConfigurator();
        var journal = NewJournal(cfg);
        var changes = new SystemChanges(journal, cfg);

        var error = Assert.Throws<ConfiguratorException>(() => changes.AddDnsRoute("dns-routing", Steam, IPAddress.Loopback));

        Assert.Equal(ConfiguratorError.NrptFailed, error.Error);
        Assert.Single(journal.Entries);
        Assert.True(journal.Rollback("dns-routing").Success);
        Assert.Empty(journal.Entries);
        Assert.Same(_tailscale, Assert.Single(_nrpt.Rules));
    }
}
```

`tests/LunaDns.Platform.Windows.Tests/WindowsNetworkConfiguratorAdminTests.cs`:
```csharp
using System.Net;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Tests;

/// <summary>
/// Vòng áp dụng/hoàn tác trên hệ thống THẬT (NRPT, file hosts, AutoConfigURL của người đang chạy). Spec 9: hosts phải giống
/// hệt byte ban đầu, luật NRPT lạ (Tailscale) giữ nguyên. Có bản sao hosts ở %TEMP% để tự khôi phục nếu test sai.
/// </summary>
[Collection(RealSystemCollection.Name)]
public class WindowsNetworkConfiguratorAdminTests
{
    private static readonly string[] SteamApex = ["steampowered.com"];
    private static readonly HostsEntry[] Pubg = [new("127.0.0.1", "prod-live-front.playbattlegrounds.com")];
    private static readonly Uri PacUrl = new("http://127.0.0.1:18080/proxy.pac");

    private static string[] AllRules(NrptCimStore store) =>
        [.. store.List().Select(r => $"{r.Name}|{string.Join(',', r.Namespaces)}|{string.Join(',', r.NameServers)}|{r.Comment}").Order(StringComparer.Ordinal)];

    private static object? AutoConfigUrl()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutoProxyRegistry.InternetSettingsSubKey);
        return key?.GetValue(AutoProxyRegistry.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    [AdminFact]
    public void Real_system_round_trip_restores_hosts_bytes_proxy_and_nrpt()
    {
        var hostsPath = HostsFileStore.DefaultPath();
        var hostsBefore = File.ReadAllBytes(hostsPath);
        var attributesBefore = File.GetAttributes(hostsPath);
        var backup = Path.Combine(Path.GetTempPath(), $"lunadns-hosts-backup-{Guid.NewGuid():N}");
        File.Copy(hostsPath, backup);
        var nrpt = new NrptCimStore();
        var rulesBefore = AllRules(nrpt);
        var proxyBefore = AutoConfigUrl();
        var journalPath = Path.Combine(Path.GetTempPath(), $"lunadns-journal-{Guid.NewGuid():N}.json");

        var cfg = WindowsNetworkConfigurator.CreateDefault();
        var journal = new ChangeJournal(new JsonFileJournalStore(journalPath), SystemChanges.Reverters(cfg));
        var changes = new SystemChanges(journal, cfg);
        RollbackResult result;
        try
        {
            changes.AddDnsRoute("dns-routing", SteamApex, IPAddress.Loopback);
            changes.SetAutoProxy("sni-proxy", PacUrl);
            changes.AddHostsEntries("pubg", Pubg);
            Assert.Equal(rulesBefore.Length + 1, AllRules(nrpt).Length);
        }
        finally
        {
            result = journal.RollbackAll();
        }

        var hostsAfter = File.ReadAllBytes(hostsPath);
        if (!hostsAfter.AsSpan().SequenceEqual(hostsBefore))
        {
            File.SetAttributes(hostsPath, FileAttributes.Normal);
            File.Copy(backup, hostsPath, overwrite: true);
            File.SetAttributes(hostsPath, attributesBefore);
        }
        else
        {
            File.Delete(backup);
        }
        File.Delete(journalPath);

        Assert.True(result.Success);
        Assert.Equal(hostsBefore, hostsAfter);
        Assert.Equal(attributesBefore, File.GetAttributes(hostsPath));
        Assert.Equal(rulesBefore, AllRules(nrpt));
        Assert.Equal(proxyBefore, AutoConfigUrl());
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận lỗi biên dịch**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: build FAIL (`WindowsNetworkConfigurator` không tồn tại).

- [ ] **Step 3: Viết `WindowsNetworkConfigurator`**

`src/LunaDns.Platform.Windows/WindowsNetworkConfigurator.cs`:
```csharp
using System.Net;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Platform.Windows;

/// <summary>Bản Windows của INetworkConfigurator: NRPT qua CIM, AutoConfigURL qua HKU\&lt;SID&gt;, file hosts ghi nguyên tử.</summary>
public sealed partial class WindowsNetworkConfigurator : INetworkConfigurator
{
    private readonly NrptDnsRouter _dns;
    private readonly AutoProxyRegistry _proxy;
    private readonly HostsFileStore _hosts;
    private readonly Func<bool> _flush;
    private readonly ILogger _logger;

    public WindowsNetworkConfigurator(
        NrptDnsRouter dns,
        AutoProxyRegistry proxy,
        HostsFileStore hosts,
        Func<bool>? flushDnsCache = null,
        ILogger<WindowsNetworkConfigurator>? logger = null)
    {
        _dns = dns;
        _proxy = proxy;
        _hosts = hosts;
        _flush = flushDnsCache ?? WindowsDns.FlushResolverCache;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public static WindowsNetworkConfigurator CreateDefault(ILoggerFactory? loggerFactory = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        return new WindowsNetworkConfigurator(
            new NrptDnsRouter(new NrptCimStore()),
            new AutoProxyRegistry(logger: factory.CreateLogger<AutoProxyRegistry>()),
            new HostsFileStore(HostsFileStore.DefaultPath()),
            logger: factory.CreateLogger<WindowsNetworkConfigurator>());
    }

    public DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer) => _dns.Plan(domains, nameServer);

    public void ApplyDnsRoute(DnsRouteSnapshot snapshot) => _dns.Apply(snapshot);

    public void RevertDnsRoute(DnsRouteSnapshot snapshot) => _dns.Revert(snapshot);

    public AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride) => _proxy.Plan(pacUrl, allowOverride);

    public void ApplyAutoProxy(AutoProxySnapshot snapshot) => _proxy.Apply(snapshot);

    public void RevertAutoProxy(AutoProxySnapshot snapshot) => _proxy.Revert(snapshot);

    public HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries) => HostsEditor.Plan(_hosts.Read(), entries);

    public void ApplyHostsEntries(HostsSnapshot snapshot) => RewriteHosts(content => HostsEditor.Apply(content, snapshot));

    public void RevertHostsEntries(HostsSnapshot snapshot) => RewriteHosts(content => HostsEditor.Revert(content, snapshot));

    public void FlushDnsCache()
    {
        if (!_flush()) LogFlushFailed(_logger);
    }

    /// <summary>Chỉ ghi khi nội dung thật sự đổi, để Revert lần hai không đụng tới file.</summary>
    private void RewriteHosts(Func<byte[], byte[]> edit)
    {
        var current = _hosts.Read();
        var updated = edit(current);
        if (!current.AsSpan().SequenceEqual(updated)) _hosts.Write(updated);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không xoá được bộ nhớ đệm DNS của Windows; tên miền cũ có thể còn tới khi hết TTL.")]
    private static partial void LogFlushFailed(ILogger logger);
}
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Platform.Windows.Tests`
Expected: 0 warning; 29 passed, 4 skipped.

- [ ] **Step 5: Lệnh kiểm tra đầy đủ (bật mạng), review**

Run: `$env:LUNADNS_NETWORK_TESTS="1"`, rồi chạy lệnh kiểm tra ở Global Constraints.
Expected: Core 272 passed, 0 skipped; Platform 29 passed, 4 skipped (đều là test admin). Phiên chính review `git diff` và `git status`: chỉ thêm file trong `src/LunaDns.Core/Platform`, `src/LunaDns.Core/Hosts`, `src/LunaDns.Platform.Windows`, `tests/…`, cùng `LunaDns.sln`. Không commit.

---

### Task 7: PO chạy nghiệm thu trên máy thật (PowerShell Admin)

**Files:** không sửa mã. Ghi kết quả vào memory `lunadns-plan4-2026-11-13.md` (mục "Thực thi").

**Interfaces:**
- Produces: bằng chứng cho spec 9 (phần NRPT, hosts, AutoConfigURL) trên Win10 Pro 19045, máy có Tailscale.

- [ ] **Step 1: Ghi trạng thái trước**

Mở PowerShell bằng quyền Admin tại `D:\Projects\lunadns`:
```powershell
Get-DnsClientNrptRule | Select-Object Name, Comment, @{n='NS';e={$_.Namespace.Count}}
Resolve-DnsName store.steampowered.com -Type A -DnsOnly | Select-Object -ExpandProperty IPAddress
(Get-FileHash C:\Windows\System32\drivers\etc\hosts).Hash
```
Expected: 2 luật Tailscale, `store.steampowered.com` → `127.0.0.1` (DNS nhà mạng), và một mã hash. Ghi lại cả ba.

- [ ] **Step 2: Chạy test admin + mạng**

```powershell
$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"; $env:LUNADNS_ADMIN_TESTS="1"; $env:LUNADNS_NETWORK_TESTS="1"; dotnet test tests/LunaDns.Platform.Windows.Tests
```
Expected: 33 passed, 0 skipped. `Steam_resolves_through_stub_while_routed_and_rules_restore_after` cho thấy NRPT → stub → DoH vượt được DNS trả `127.0.0.1`.

- [ ] **Step 3: Kiểm tra trạng thái sau**

Chạy lại ba lệnh ở Step 1.
Expected: vẫn đúng 2 luật Tailscale, không có luật `LunaDNS *`, `store.steampowered.com` lại ra `127.0.0.1`, hash hosts không đổi, `%TEMP%\lunadns-hosts-backup-*` không còn.

- [ ] **Step 4: Tailscale tự dựng lại luật thì luật LunaDNS có còn không (bằng tay)**

```powershell
Add-DnsClientNrptRule -Namespace '.lunadns-tailscale-check.invalid' -NameServers 127.0.0.1 -Comment 'LunaDNS manual-check' -DisplayName 'LunaDNS'
Restart-Service Tailscale
Start-Sleep 15
Get-DnsClientNrptRule | Select-Object Name, Comment, @{n='NS';e={$_.Namespace.Count}}
Get-DnsClientNrptRule | Where-Object Comment -eq 'LunaDNS manual-check' | ForEach-Object { Remove-DnsClientNrptRule -Name $_.Name -Force }
```
Expected: sau khi Tailscale khởi động lại, luật `LunaDNS manual-check` vẫn còn, và luật của Tailscale có lại đủ. Nếu Tailscale xoá luật lạ thì ghi vào memory: KH5 cần watchdog kiểm tra luật NRPT định kỳ.

- [ ] **Step 5: Ghi kết quả**

Ghi các số đo vào memory `lunadns-plan4-2026-11-13.md`: số test, IP ra ở Step 2, kết quả Step 4. Nếu Windows Defender báo về file hosts thì ghi tên cảnh báo.

---

## Đối chiếu spec

| Mục spec | Task | Còn lại cho kế hoạch sau |
|---|---|---|
| 3. `INetworkConfigurator` trong Core, chỉ `Platform.Windows` gọi registry/CIM/P/Invoke | T1, T3–T6 | macOS: ngoài v1 |
| 3. Bảng thao tác: `AddDnsRoute`/`Remove` qua CIM, không PowerShell | T5 (`NrptCimStore`), T1 (Plan/Apply/Revert) | **Lệch tên thao tác, xem quyết định 1** |
| 3. `SetAutoProxyUrl` + `InternetSetOption(39/37)` | T4 | — |
| 3. `AddHostsEntries`: giữ encoding cũ và thuộc tính | T2 (mức byte), T3 (thuộc tính, DACL, ghi nguyên tử) | — |
| 3. `FlushDnsCache` = `DnsFlushResolverCache` | T4, T6 | — |
| 3. `RegisterBootCleanup` (Task Scheduler, SYSTEM) | — | **KH4b (quyết định 10)** |
| 4.2 NRPT trỏ namespace game về `127.0.0.1`, DNS card mạng không đổi | T5 | KH5 chọn danh sách domain theo công tắc |
| 4.4 dòng hosts `127.0.0.1 <domain> # LunaDNS` | T2 | KH5: danh sách domain PUBG, bỏ anti-cheat, cảnh báo |
| 4.5 ghi Journal trước, LIFO, idempotent, lỗi giữ lại | T1 (`SystemChanges`), T6 (vòng qua file journal + "crash") | KH5: `FeatureSet` gọi `Rollback(feature)` khi Apply lỗi |
| 4.5 không đụng WinHTTP, `DefaultConnectionSettings`, WPAD | Global Constraints, T4 | — |
| 4.6 ACL `%ProgramData%\LunaDNS` | — | **KH4b (quyết định 10)** |
| 4.7 task dọn dẹp lúc khởi động, copy exe vào Program Files | — | **KH4b**; cần thêm trigger lúc đăng nhập cho AutoConfigURL (quyết định 3) |
| 4.9 báo cáo: luật NRPT của LunaDNS, AutoConfigURL, dòng `# LunaDNS` | `NrptDnsRouter.MarkerPrefix`, `HostsEditor.Marker`, `AutoProxyRegistry.ValueName` | KH5 viết báo cáo |
| 6. `ConfiguratorException` kèm mã lỗi, không `catch {}` rỗng | T1, T3–T5 | KH5 hiện message |
| 9. NRPT: thêm, `Resolve` qua stub, gỡ, không còn luật | T5 (test admin + mạng), T7 | VM Win10/11 Home: chưa có (quyết định 9) |
| 9. hosts UTF-8 có comment tiếng Việt, ReadOnly, giống hệt byte | T2, T3, T6 (test admin trên hosts thật) | — |
| 9. AutoConfigURL có sẵn PAC công ty → hoàn tác về như cũ | T4 (`Override_restores_previous_expand_string_exactly`) | — |
| 9. Journal: `FakeConfigurator`, lỗi giữa chừng, hoàn tác hai lần, crash | T1, T6 | — |
| Spike: luật Tailscale, namespace trùng | T5 (`FakeNrptStore.Tailscale`, test trùng namespace), T7 Step 4 | — |
| Spike: CM của Steam không đọc PAC, chỉ đi theo NRPT | T5 đưa `steamserver.net` vào được NRPT | KH5 bật NRPT cùng Proxy SNI |

## Quyết định thiết kế mới trong KH4 (khác hoặc thêm so với spec). PO đã duyệt cả 11 điểm ngày 2026-11-13, chọn đúng phương án khuyến nghị cho 2, 4 và 10. Không hỏi lại khi thực thi.

1. **Plan/Apply/Revert thay cho "thao tác trả về Snapshot".** Spec 3 viết rằng mỗi thao tác trả về snapshot (giá trị trước khi đổi), nghĩa là chỉ có snapshot *sau* khi đã đổi. Điều đó trái quy tắc 4.5 "ghi Journal rồi mới đổi". KH4 tách thành `Plan*` (chỉ đọc, trả snapshot), `Apply*` và `Revert*`, và bắt mọi lời gọi đi qua `SystemChanges`. Crash ở giữa Record và Apply vẫn an toàn vì Revert idempotent.
2. **Thêm package `Microsoft.Management.Infrastructure` 3.0.0** để gọi CIM (spec ghi "qua WMI/CIM, không cần PowerShell", mà .NET 8 không có sẵn CIM). Các phương án khác:
   - (b) Ghi thẳng registry `DnsPolicyConfig\{GUID}`: không cần package, nhưng chưa biết Dnscache có nhận luật ngay hay không, phải spike thêm.
   - (c) `System.Management` (WMI COM): cũng là package, và kém thân thiện với trimming.
3. **`AutoConfigURL` ghi qua `HKEY_USERS\<SID>`** của người đang chạy app, thay cho `HKCU`. SID được ghi trong snapshot, để task dọn dẹp chạy bằng SYSTEM (không có HKCU của người dùng) vẫn hoàn tác đúng chỗ.
   - Lúc khởi động máy, hive của người dùng chưa được nạp. Revert khi đó ném `UserHiveNotLoaded` và mục được giữ lại, nên KH4b cần thêm trigger lúc đăng nhập cho task.
   - v1 không hỗ trợ kiểu "người dùng thường nhập mật khẩu của tài khoản Admin khác", vì proxy sẽ ghi vào hive của tài khoản Admin. Giới hạn này được ghi vào FAQ ở KH7.
4. **Có sẵn cấu hình proxy thì từ chối, trừ khi `allowOverride`.** Nếu `AutoConfigURL` khác rỗng hoặc `ProxyEnable = 1`, `Plan` ném `ExistingProxyConfig`. KH5 hỏi người dùng rồi gọi lại với `allowOverride: true`. Lý do: PAC của LunaDNS trả `DIRECT` cho mọi domain không phải game. Nếu ghi đè, toàn bộ lưu lượng web sẽ bỏ qua proxy công ty, trái tiêu chí 2 của spec. Spec 9 chỉ yêu cầu "hoàn tác về như cũ", và yêu cầu đó vẫn được giữ.
5. **Chỉ hoàn tác `AutoConfigURL` khi giá trị vẫn là của LunaDNS.** Nếu người dùng đổi proxy trong lúc LunaDNS đang bật, giá trị mới của họ được giữ, kèm log cảnh báo không chứa URL.
6. **hosts: chỉ gỡ đúng dòng của mình, không chép lại cả file cũ.**
   - Snapshot chỉ giữ các dòng đã thêm. Khi các dòng đó còn nằm ở cuối file thì hoàn tác ra đúng từng byte. Dòng mà người dùng hay app khác thêm trong lúc LunaDNS bật vẫn được giữ.
   - File UTF-16 bị từ chối.
   - Mỗi dòng chỉ được trỏ về địa chỉ loopback, với domain thuộc `GameDomains`.
7. **Cách đặt luật NRPT.**
   - Mỗi lần áp dụng tạo một luật gồm mọi namespace, với `Comment = "LunaDNS <guid>"` và `DisplayName = "LunaDNS"`.
   - Mỗi domain có hai namespace: `.d` và `d`. Chưa chắc luật hậu tố khớp được tên gốc, nên thêm cả tên gốc.
   - Namespace trùng y hệt với luật lạ thì bỏ qua và ghi vào `SkippedNamespaces`. Trùng hết thì báo `DnsRouteConflict`.
   - Snapshot có dấu rỗng hoặc lạ thì bị từ chối, để không bao giờ khớp nhầm luật Tailscale (luật này không có Comment).
8. **Flush cache DNS lỗi chỉ ghi log, không làm hỏng tính năng.** Nếu flush lỗi, tên miền cũ chỉ còn tới khi hết TTL.
9. **Test của `Platform.Windows` vào lệnh kiểm tra mặc định.** Spec 9 coi phần này là "tích hợp, chạy tay trên máy ảo". KH4 làm hai tầng:
   - Test không cần Admin (file tạm, khoá registry riêng, NRPT giả) chạy mỗi lần kiểm tra.
   - Test admin chạy trên máy PO (Win10 Pro). Win10/11 Home trên máy ảo vẫn chưa có ai chạy.
10. **ACL `%ProgramData%\LunaDNS` (4.6) và task dọn dẹp + copy exe (4.7) tách sang KH4b,** không làm trong KH4 như bảng lộ trình ở KH1. Task cần exe có `--cleanup` của App, và cần thêm trigger lúc đăng nhập (quyết định 3). KH4 giữ ở ba thao tác hệ thống cùng journal.
11. **Không tự dọn luật NRPT "mồ côi"** (mang dấu `LunaDNS ` nhưng journal đã mất hoặc bị cách ly). Việc dọn loại luật này để KH5 quyết định khi viết `--cleanup`, vì đó là thao tác nằm ngoài Journal (spec 4.5).

**Việc không thuộc KH4:** ACL thư mục dữ liệu, task dọn dẹp, copy exe (KH4b); `FeatureSet`, danh sách domain theo công tắc, hỏi người dùng khi có proxy sẵn, `--cleanup`, `--report`, watchdog luật NRPT (KH5); FAQ về giới hạn elevation và CM của Steam (KH7).
