# Hồ sơ theo mạng (kế hoạch)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** App nhớ tổ hợp DoH + kiểu cắt + độ trễ đã chạy được cho từng mạng (nhận theo MAC gateway), tự áp lại khi quay lại mạng đó, và trên mạng mới thì Kiểm tra ngầm rồi gợi ý Tìm cấu hình hợp mạng.

**Architecture:** Core có `NetworkId`/`INetworkIdentity` (định danh mạng) và `NetworkProfileStore` (`profiles.json`). Mỗi nền tảng một `INetworkIdentity` (Windows: `GetBestInterface` + `SendARP`; macOS: `route`/`arp`). App có `NetworkWatcher` (sự kiện đổi mạng + poll 60 s, chờ yên 3 s) và `NetworkProfileCoordinator` (quyết định áp / Kiểm tra / lưu) nói chuyện với `MainViewModel` qua `INetworkProfileHost`. Áp tổ hợp dùng `AppServices.ApplyTuningAsync` có sẵn; giao thức daemon không đổi.

**Tech Stack:** .NET 8, C#, Avalonia 11.3, xUnit, System.Text.Json source-gen, Microsoft.Extensions.TimeProvider.Testing.

Spec: `docs/superpowers/specs/2026-11-28-lunadns-network-profiles-design.md`.

## Global Constraints

- Không thêm `Co-Authored-By` hay nhắc Claude trong commit; tác giả chỉ Luna Vo. Không PR: làm trên nhánh `feat/network-profiles`, xong merge thẳng `main`.
- `NetworkId.Value` = hex chữ thường của SHA-256 trên chuỗi UTF-8 `"mac|{mac}|{kind}"` (mạnh) hoặc `"ip|{gatewayIp}|{adapter}"` (`Weak = true`). MAC chuẩn hoá: 6 octet hai chữ số hex thường, nối bằng `:`. MAC toàn 0, `ff:ff:ff:ff:ff:ff` hoặc không đọc được thì coi như không có MAC.
- Log chỉ `NetworkId.Short` (8 ký tự đầu). Không ghi MAC hay IP gateway vào log.
- `profiles.json` nằm trong `AppPaths.DataDirectory`; tối đa **32** hồ sơ, đầy thì bỏ `LastSeen` cũ nhất; file hỏng đổi tên `profiles.json.corrupt-yyyyMMddHHmmss` (giờ UTC) và chạy với danh sách rỗng.
- `NetworkWatcher`: chờ yên **3 giây**, poll dự phòng **60 giây**.
- `AppSettings.RememberPerNetwork` mặc định `true`; file settings cũ không có khoá thì coi như `true`.
- "Đạt" = mọi host `ProbeTargets.Steam` là `Ok`, hoặc `SniFiltered` khi Proxy SNI đang bật. Còn lại là "bị chặn".
- Câu chữ (`Strings.resx`, tiếng Việt):
  - `NetworkSavedFormat`: `Mạng: {0} — có cấu hình đã lưu ({1})`
  - `NetworkNotSavedFormat`: `Mạng: {0} — chưa có cấu hình đã lưu`
  - `NetworkAppliedFormat`: `Mạng: {0} — đã dùng cấu hình đã lưu ({1})`
  - `NetworkBlockedBanner`: `Mạng mới có vẻ chặn Steam.`
  - `ForgetNetwork`: `Quên mạng này`
  - `RememberPerNetworkSwitch`: `Nhớ cấu hình theo mạng`
  - `RememberPerNetworkHint`: `Lưu cấu hình đã chạy được cho từng mạng (nhận theo router) và tự dùng lại khi quay lại mạng đó.`
  - `ClearNetworkProfiles`: `Xoá mọi cấu hình theo mạng`
  - `ClearNetworkProfilesTitle`: `Xoá cấu hình theo mạng?`
  - `ClearNetworkProfilesText`: `LunaDNS sẽ quên cấu hình đã lưu của mọi mạng. Cài đặt đang dùng giữ nguyên.`
  - `{1}` là ngày `dd/MM` theo giờ máy.
- Verify cuối: `dotnet test` cả solution. Trên Windows, 5 test `MacOS/*` của App.Tests `-f net8.0` (SetUnixFileMode) trượt sẵn: mốc cũ, không phải lỗi mới. CI `build-test-macos` phải xanh.

## File Structure

| File | Việc |
|---|---|
| `src/LunaDns.Core/Net/NetworkId.cs` (mới) | `NetworkId`, `NetworkSnapshot`, chuẩn hoá MAC |
| `src/LunaDns.Core/Platform/INetworkIdentity.cs` (mới) | interface đọc mạng hiện tại |
| `src/LunaDns.Core/Settings/NetworkProfile.cs` (mới) | record hồ sơ |
| `src/LunaDns.Core/Settings/NetworkProfileStore.cs` (mới) | đọc/ghi `profiles.json` |
| `src/LunaDns.Core/Settings/SettingsJsonContext.cs` | thêm `List<NetworkProfile>` |
| `src/LunaDns.Core/Settings/AppSettings.cs`, `JsonFileSettingsStore.cs` | `RememberPerNetwork` |
| `src/LunaDns.Platform.MacOS/Net/MacNetworkIdentity.cs` (mới) | `route`/`arp` + hàm phân tích thuần |
| `src/LunaDns.Platform.Windows/Net/WindowsNetworkIdentity.cs` (mới) | `GetBestInterface` + `SendARP` |
| `src/LunaDns.App/NetworkProfiles/NetworkWatcher.cs` (mới) | gộp sự kiện đổi mạng |
| `src/LunaDns.App/NetworkProfiles/INetworkProfileHost.cs` (mới) | những gì coordinator cần từ view model |
| `src/LunaDns.App/NetworkProfiles/NetworkProfileCoordinator.cs` (mới) | quyết định hành động |
| `src/LunaDns.App/ProbeRunner.cs` | truyền cả độ trễ vào `TlsProbe` |
| `src/LunaDns.App/PlatformInfo*.cs`, `AppPaths.cs`, `ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml`, `Strings.cs`, `Strings.resx` | nối dây + giao diện |
| `docs/guide/en/03-switches.md`, `docs/guide/03-ba-cong-tac.md` | mục mới |

---

### Task 1: Core — `NetworkId`, `NetworkProfile`, `NetworkProfileStore`, `RememberPerNetwork`

**Files:**
- Create: `src/LunaDns.Core/Net/NetworkId.cs`, `src/LunaDns.Core/Platform/INetworkIdentity.cs`, `src/LunaDns.Core/Settings/NetworkProfile.cs`, `src/LunaDns.Core/Settings/NetworkProfileStore.cs`
- Modify: `src/LunaDns.Core/Settings/SettingsJsonContext.cs`, `src/LunaDns.Core/Settings/AppSettings.cs`, `src/LunaDns.Core/Settings/JsonFileSettingsStore.cs`
- Test: `tests/LunaDns.Core.Tests/Net/NetworkIdTests.cs`, `tests/LunaDns.Core.Tests/Settings/NetworkProfileStoreTests.cs`, `tests/LunaDns.Core.Tests/Settings/JsonFileSettingsStoreTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record NetworkId(string Value, bool Weak)` với `string Short`, `static NetworkId? From(string? mac, string? gatewayIp, string kind, string adapter)`, `static string? NormalizeMac(string? raw)`
  - `public sealed record NetworkSnapshot(NetworkId Id, string Label)`
  - `public interface INetworkIdentity { NetworkSnapshot? Current(); }`
  - `public sealed record NetworkProfile(string Id, bool Weak, string Label, DohProvider Doh, TuneCandidate Candidate, DateTimeOffset SavedAt, DateTimeOffset LastSeen, bool Stale = false)`
  - `public sealed class NetworkProfileStore(string path)` với `Find(string id)`, `Upsert(NetworkProfile)`, `Touch(string id, DateTimeOffset at)`, `MarkStale(string id)`, `Remove(string id)`, `Clear()`, `IReadOnlyList<NetworkProfile> All`; `const int MaxProfiles = 32`
  - `AppSettings.RememberPerNetwork` (bool, mặc định true)

- [ ] **Step 1: Viết test hỏng**

`tests/LunaDns.Core.Tests/Net/NetworkIdTests.cs`:

```csharp
using LunaDns.Core.Net;

namespace LunaDns.Core.Tests.Net;

public class NetworkIdTests
{
    [Theory]
    [InlineData("AA-BB-CC-01-02-03", "aa:bb:cc:01:02:03")]
    [InlineData("0:1a:2b:3:4:5", "00:1a:2b:03:04:05")]
    [InlineData("00:00:00:00:00:00", null)]
    [InlineData("ff:ff:ff:ff:ff:ff", null)]
    [InlineData("(incomplete)", null)]
    [InlineData(null, null)]
    public void NormalizeMac(string? raw, string? expected) => Assert.Equal(expected, NetworkId.NormalizeMac(raw));

    [Fact]
    public void SameMacAndKind_SameId()
    {
        var a = NetworkId.From("aa:bb:cc:01:02:03", "192.168.1.1", "Wireless80211", "Wi-Fi")!;
        var b = NetworkId.From("AA-BB-CC-01-02-03", "10.0.0.1", "Wireless80211", "Wi-Fi 2")!;
        Assert.Equal(a, b);
        Assert.False(a.Weak);
        Assert.Equal(64, a.Value.Length);
        Assert.Equal(a.Value[..8], a.Short);
    }

    [Fact]
    public void DifferentKind_DifferentId() =>
        Assert.NotEqual(NetworkId.From("aa:bb:cc:01:02:03", "192.168.1.1", "Ethernet", "Ethernet"),
            NetworkId.From("aa:bb:cc:01:02:03", "192.168.1.1", "Wireless80211", "Wi-Fi"));

    [Fact]
    public void NoMac_FallsBackToGatewayIp_Weak()
    {
        var id = NetworkId.From(null, "10.64.0.1", "Ppp", "Cellular")!;
        Assert.True(id.Weak);
        Assert.Equal(id, NetworkId.From("00:00:00:00:00:00", "10.64.0.1", "Other", "Cellular"));
    }

    [Fact]
    public void NoGateway_Null() => Assert.Null(NetworkId.From("aa:bb:cc:01:02:03", null, "Ethernet", "Ethernet"));
}
```

Ghi chú: id mạnh không phụ thuộc IP/adapter; id yếu không phụ thuộc kind (PPP có thể đổi kiểu báo).

`tests/LunaDns.Core.Tests/Settings/NetworkProfileStoreTests.cs`:

```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Settings;

public sealed class NetworkProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lunadns-profiles-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "profiles.json");
    private static readonly DateTimeOffset T0 = new(2026, 11, 28, 8, 0, 0, TimeSpan.Zero);

    public NetworkProfileStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static NetworkProfile Profile(string id, DateTimeOffset seen) =>
        new(id, false, "Wi-Fi · 192.168.1.1", DohProvider.Google, new TuneCandidate(new HelloSplit(SplitMode.MidSni, Records: true), 10), T0, seen);

    [Fact]
    public void RoundTrip()
    {
        new NetworkProfileStore(FilePath).Upsert(Profile("a", T0));
        var loaded = new NetworkProfileStore(FilePath).Find("a");
        Assert.Equal(Profile("a", T0), loaded);
    }

    [Fact]
    public void MissingFile_Empty() => Assert.Empty(new NetworkProfileStore(FilePath).All);

    [Fact]
    public void CorruptFile_MovedAside_Empty()
    {
        File.WriteAllText(FilePath, "{ not json");
        var store = new NetworkProfileStore(FilePath);
        Assert.Empty(store.All);
        Assert.Single(Directory.GetFiles(_dir, "profiles.json.corrupt-*"));
    }

    [Fact]
    public void Upsert_SameId_Overwrites()
    {
        var store = new NetworkProfileStore(FilePath);
        store.Upsert(Profile("a", T0));
        store.Upsert(Profile("a", T0) with { Doh = DohProvider.Quad9 });
        Assert.Equal(DohProvider.Quad9, Assert.Single(store.All).Doh);
    }

    [Fact]
    public void Full_DropsOldestLastSeen()
    {
        var store = new NetworkProfileStore(FilePath);
        for (var i = 0; i < NetworkProfileStore.MaxProfiles; i++) store.Upsert(Profile($"p{i}", T0.AddMinutes(i)));
        store.Upsert(Profile("new", T0.AddDays(1)));
        Assert.Equal(NetworkProfileStore.MaxProfiles, store.All.Count);
        Assert.Null(store.Find("p0"));
        Assert.NotNull(store.Find("new"));
    }

    [Fact]
    public void Touch_MarkStale_Remove_Clear()
    {
        var store = new NetworkProfileStore(FilePath);
        store.Upsert(Profile("a", T0));
        store.Upsert(Profile("b", T0));
        store.Touch("a", T0.AddHours(1));
        store.MarkStale("a");
        var a = new NetworkProfileStore(FilePath).Find("a")!;
        Assert.Equal(T0.AddHours(1), a.LastSeen);
        Assert.True(a.Stale);
        store.Remove("a");
        Assert.Null(store.Find("a"));
        store.Clear();
        Assert.Empty(new NetworkProfileStore(FilePath).All);
    }
}
```

Thêm vào `JsonFileSettingsStoreTests.cs` (theo mẫu test `AutoResume` có sẵn trong file):

```csharp
[Fact]
public void OldFileWithoutRememberPerNetwork_DefaultsToTrue()
{
    File.WriteAllText(_path, """{ "AutoResume": false }""");
    Assert.True(new JsonFileSettingsStore(_path).Load().RememberPerNetwork);
}

[Fact]
public void RememberPerNetworkFalse_RoundTrips()
{
    var store = new JsonFileSettingsStore(_path);
    store.Save(new AppSettings { RememberPerNetwork = false });
    Assert.False(store.Load().RememberPerNetwork);
}
```

(Dùng đúng tên trường đường dẫn mà các test khác trong file đang dùng; nếu không phải `_path` thì đổi theo.)

- [ ] **Step 2: Chạy test, thấy hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~NetworkId|FullyQualifiedName~NetworkProfileStore|FullyQualifiedName~JsonFileSettingsStore"`
Expected: FAIL biên dịch (`NetworkId`, `NetworkProfileStore`, `RememberPerNetwork` chưa có).

- [ ] **Step 3: Cài đặt**

`src/LunaDns.Core/Net/NetworkId.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LunaDns.Core.Net;

/// <summary>Định danh một mạng (spec hồ sơ theo mạng): băm MAC gateway + loại interface; không có MAC thì băm IP gateway + adapter (yếu).</summary>
public sealed record NetworkId(string Value, bool Weak)
{
    /// <summary>8 ký tự đầu, chỉ dùng cho log.</summary>
    public string Short => Value[..8];

    public static NetworkId? From(string? mac, string? gatewayIp, string kind, string adapter)
    {
        if (string.IsNullOrWhiteSpace(gatewayIp)) return null;
        var normalized = NormalizeMac(mac);
        return normalized is not null
            ? new NetworkId(Hash($"mac|{normalized}|{kind}"), Weak: false)
            : new NetworkId(Hash($"ip|{gatewayIp.Trim()}|{adapter}"), Weak: true);
    }

    /// <summary>"AA-BB-CC-1-2-3" hay "0:1a:2b:3:4:5" thành "aa:bb:cc:01:02:03"; MAC không hợp lệ, toàn 0 hoặc broadcast thì null.</summary>
    public static string? NormalizeMac(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Trim().Split(':', '-');
        if (parts.Length != 6) return null;
        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            if (parts[i].Length is < 1 or > 2
                || !byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i])) return null;
        }
        if (bytes.All(b => b == 0) || bytes.All(b => b == 0xFF)) return null;
        return string.Join(':', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>Mạng hiện tại: định danh và nhãn gợi ý cho giao diện (vd. "Wi-Fi · 192.168.1.1").</summary>
public sealed record NetworkSnapshot(NetworkId Id, string Label);
```

`src/LunaDns.Core/Platform/INetworkIdentity.cs`:

```csharp
using LunaDns.Core.Net;

namespace LunaDns.Core.Platform;

/// <summary>Đọc mạng đang dùng đường mặc định. Không có gateway (mất mạng) thì null. Không ném.</summary>
public interface INetworkIdentity
{
    NetworkSnapshot? Current();
}
```

`src/LunaDns.Core/Settings/NetworkProfile.cs`:

```csharp
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Settings;

/// <summary>Tổ hợp đã chạy được trên một mạng. <c>Stale</c>: lần Kiểm tra sau trên mạng này bị chặn.</summary>
public sealed record NetworkProfile(
    string Id,
    bool Weak,
    string Label,
    DohProvider Doh,
    TuneCandidate Candidate,
    DateTimeOffset SavedAt,
    DateTimeOffset LastSeen,
    bool Stale = false);
```

`SettingsJsonContext.cs`: thêm `[JsonSerializable(typeof(List<NetworkProfile>))]`.

`src/LunaDns.Core/Settings/NetworkProfileStore.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace LunaDns.Core.Settings;

/// <summary>profiles.json: ghi file tạm rồi thay; file hỏng thì cất sang bên cạnh và chạy với danh sách rỗng (như JsonFileSettingsStore).</summary>
public sealed class NetworkProfileStore
{
    public const int MaxProfiles = 32;

    private readonly string _path;
    private readonly object _gate = new();
    private List<NetworkProfile> _profiles;

    public NetworkProfileStore(string path)
    {
        _path = path;
        _profiles = Load(path);
    }

    public IReadOnlyList<NetworkProfile> All
    {
        get { lock (_gate) return [.. _profiles]; }
    }

    public NetworkProfile? Find(string id)
    {
        lock (_gate) return _profiles.Find(p => p.Id == id);
    }

    public void Upsert(NetworkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Change(list =>
        {
            list.RemoveAll(p => p.Id == profile.Id);
            list.Add(profile);
            while (list.Count > MaxProfiles) list.Remove(list.MinBy(p => p.LastSeen)!);
        });
    }

    public void Touch(string id, DateTimeOffset at) => Replace(id, p => p with { LastSeen = at });

    public void MarkStale(string id) => Replace(id, p => p with { Stale = true });

    public void Remove(string id) => Change(list => list.RemoveAll(p => p.Id == id));

    public void Clear() => Change(list => list.Clear());

    private void Replace(string id, Func<NetworkProfile, NetworkProfile> change) => Change(list =>
    {
        var i = list.FindIndex(p => p.Id == id);
        if (i >= 0) list[i] = change(list[i]);
    });

    private void Change(Action<List<NetworkProfile>> change)
    {
        lock (_gate)
        {
            var next = new List<NetworkProfile>(_profiles);
            change(next);
            Save(next);
            _profiles = next;
        }
    }

    private void Save(List<NetworkProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(profiles, SettingsJsonContext.Default.ListNetworkProfile));
        File.Move(temp, _path, overwrite: true);
    }

    private static List<NetworkProfile> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var list = JsonSerializer.Deserialize(File.ReadAllBytes(path), SettingsJsonContext.Default.ListNetworkProfile) ?? [];
            list.RemoveAll(p => p is null || string.IsNullOrEmpty(p.Id) || p.Candidate is null);
            return list;
        }
        catch (JsonException)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return [];
        }
    }
}
```

`AppSettings.cs`: thêm cạnh `AutoResume`:

```csharp
    /// <summary>Hồ sơ theo mạng: lưu và tự áp tổ hợp đã chạy được cho từng mạng. Tắt thì giữ hồ sơ, không áp, không lưu.</summary>
    public bool RememberPerNetwork { get; init; } = true;
```

`JsonFileSettingsStore.Load`: theo mẫu `autoResume`:

```csharp
            var perNetwork = HasProperty(bytes, nameof(AppSettings.RememberPerNetwork)) ? loaded.RememberPerNetwork : true;
```

và thêm `RememberPerNetwork = perNetwork` vào `with { ... }` cuối hàm.

- [ ] **Step 4: Chạy test, thấy qua**

Run: `dotnet test tests/LunaDns.Core.Tests`
Expected: PASS hết.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core tests/LunaDns.Core.Tests
git commit -m "feat(core): network id from the gateway MAC and a profiles.json store for per-network tuning"
```

---

### Task 2: macOS — `MacNetworkIdentity`

**Files:**
- Create: `src/LunaDns.Platform.MacOS/Net/MacNetworkIdentity.cs`
- Test: `tests/LunaDns.Platform.MacOS.Tests/MacNetworkIdentityTests.cs`

**Interfaces:**
- Consumes: `NetworkId`, `NetworkSnapshot`, `INetworkIdentity` (Task 1); `IProcessRunner.Run(string fileName, IReadOnlyList<string> arguments) → ProcessResult(int ExitCode, string Output)` (có sẵn).
- Produces: `public sealed class MacNetworkIdentity(IProcessRunner runner) : INetworkIdentity`; `internal static (string Gateway, string Interface)? ParseRoute(string output)`; `internal static string? ParseArp(string output)`.

- [ ] **Step 1: Viết test hỏng**

```csharp
using LunaDns.Platform.MacOS.Net;
using LunaDns.Platform.MacOS.Processes;

namespace LunaDns.Platform.MacOS.Tests;

public class MacNetworkIdentityTests
{
    private const string Route = """
           route to: default
        destination: default
               mask: default
            gateway: 192.168.1.1
          interface: en0
              flags: <UP,GATEWAY,DONE,STATIC,PRCLONING>
        """;

    [Fact]
    public void ParseRoute_ReadsGatewayAndInterface() => Assert.Equal(("192.168.1.1", "en0"), MacNetworkIdentity.ParseRoute(Route));

    [Fact]
    public void ParseRoute_NoGateway_Null() => Assert.Null(MacNetworkIdentity.ParseRoute("route: writing to routing socket: not in table"));

    [Theory]
    [InlineData("? (192.168.1.1) at 0:1a:2b:3:4:5 on en0 ifscope [ethernet]", "0:1a:2b:3:4:5")]
    [InlineData("? (192.168.1.1) at (incomplete) on en0 ifscope [ethernet]", null)]
    [InlineData("192.168.1.1 (192.168.1.1) -- no entry", null)]
    public void ParseArp(string output, string? mac) => Assert.Equal(mac, MacNetworkIdentity.ParseArp(output));

    [Fact]
    public void Current_UsesRouteThenArp()
    {
        var runner = new ScriptedRunner(new()
        {
            ["/sbin/route"] = new(0, Route),
            ["/usr/sbin/arp"] = new(0, "? (192.168.1.1) at 0:1a:2b:3:4:5 on en0 ifscope [ethernet]"),
        });
        var snapshot = new MacNetworkIdentity(runner).Current()!;
        Assert.False(snapshot.Id.Weak);
        Assert.Equal("en0 · 192.168.1.1", snapshot.Label);
    }

    [Fact]
    public void Current_RouteFails_Null()
    {
        var runner = new ScriptedRunner(new() { ["/sbin/route"] = new(1, "") });
        Assert.Null(new MacNetworkIdentity(runner).Current());
    }

    [Fact]
    public void Current_ArpThrows_WeakId()
    {
        var runner = new ScriptedRunner(new() { ["/sbin/route"] = new(0, Route) });
        Assert.True(new MacNetworkIdentity(runner).Current()!.Id.Weak);
    }

    private sealed class ScriptedRunner(Dictionary<string, ProcessResult> results) : IProcessRunner
    {
        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            results.TryGetValue(fileName, out var r) ? r : throw new InvalidOperationException(fileName);
    }
}
```

- [ ] **Step 2: Chạy test, thấy hỏng**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests --filter "FullyQualifiedName~MacNetworkIdentityTests"`
Expected: FAIL biên dịch.

- [ ] **Step 3: Cài đặt**

```csharp
using System.Text.RegularExpressions;
using LunaDns.Core.Net;
using LunaDns.Core.Platform;
using LunaDns.Platform.MacOS.Processes;

namespace LunaDns.Platform.MacOS.Net;

/// <summary>Mạng hiện tại trên macOS: <c>route -n get default</c> cho gateway, <c>arp -n</c> cho MAC. Chạy được với quyền người dùng.</summary>
public sealed partial class MacNetworkIdentity(IProcessRunner runner) : INetworkIdentity
{
    public NetworkSnapshot? Current()
    {
        try
        {
            var route = runner.Run("/sbin/route", ["-n", "get", "default"]);
            if (route.ExitCode != 0 || ParseRoute(route.Output) is not { } parsed) return null;
            var (gateway, iface) = parsed;
            string? mac = null;
            try
            {
                var arp = runner.Run("/usr/sbin/arp", ["-n", gateway]);
                if (arp.ExitCode == 0) mac = ParseArp(arp.Output);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                // Không có MAC thì dùng id yếu.
            }
            var id = NetworkId.From(mac, gateway, iface, iface);
            return id is null ? null : new NetworkSnapshot(id, $"{iface} · {gateway}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    internal static (string Gateway, string Interface)? ParseRoute(string output)
    {
        var gateway = RouteGateway().Match(output);
        var iface = RouteInterface().Match(output);
        return gateway.Success && iface.Success ? (gateway.Groups[1].Value, iface.Groups[1].Value) : null;
    }

    internal static string? ParseArp(string output)
    {
        var m = ArpMac().Match(output);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^\s*gateway:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex RouteGateway();

    [GeneratedRegex(@"^\s*interface:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex RouteInterface();

    [GeneratedRegex(@"\sat\s+([0-9A-Fa-f]{1,2}(?::[0-9A-Fa-f]{1,2}){5})\s")]
    private static partial Regex ArpMac();
}
```

Ghi chú: trên mac `kind` = tên interface (`en0` Wi‑Fi, `en1`… dây), đủ để Wi‑Fi và dây cùng router ra hai hồ sơ.

- [ ] **Step 4: Chạy test, thấy qua**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests --filter "FullyQualifiedName~MacNetworkIdentityTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Platform.MacOS/Net tests/LunaDns.Platform.MacOS.Tests/MacNetworkIdentityTests.cs
git commit -m "feat(macos): read the default gateway and its MAC to identify the current network"
```

---

### Task 3: Windows — `WindowsNetworkIdentity`

**Files:**
- Create: `src/LunaDns.Platform.Windows/Net/WindowsNetworkIdentity.cs`
- Test: `tests/LunaDns.Platform.Windows.Tests/WindowsNetworkIdentityTests.cs`

**Interfaces:**
- Consumes: `NetworkId`, `NetworkSnapshot`, `INetworkIdentity` (Task 1).
- Produces: `public sealed class WindowsNetworkIdentity : INetworkIdentity`; `internal static string? FormatMac(ReadOnlySpan<byte> mac)`.

- [ ] **Step 1: Viết test hỏng**

```csharp
using LunaDns.Platform.Windows.Net;

namespace LunaDns.Platform.Windows.Tests;

public class WindowsNetworkIdentityTests
{
    [Fact]
    public void FormatMac_SixBytes() =>
        Assert.Equal("aa:bb:cc:01:02:03", WindowsNetworkIdentity.FormatMac([0xAA, 0xBB, 0xCC, 1, 2, 3]));

    [Fact]
    public void FormatMac_WrongLength_Null() => Assert.Null(WindowsNetworkIdentity.FormatMac([1, 2, 3]));

    [WindowsFact]
    public void Current_OnRealMachine_DoesNotThrow()
    {
        var snapshot = new WindowsNetworkIdentity().Current();
        if (snapshot is not null) Assert.Equal(64, snapshot.Id.Value.Length);
    }
}
```

(`WindowsFact` ở `tests/Shared/WindowsFactAttribute.cs`; kiểm project test đã link file đó, chưa thì thêm `<Compile Include="..\Shared\WindowsFactAttribute.cs" />` theo mẫu project khác.)

- [ ] **Step 2: Chạy test, thấy hỏng**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests --filter "FullyQualifiedName~WindowsNetworkIdentityTests"`
Expected: FAIL biên dịch.

- [ ] **Step 3: Cài đặt**

```csharp
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using LunaDns.Core.Net;
using LunaDns.Core.Platform;

namespace LunaDns.Platform.Windows.Net;

/// <summary>Mạng hiện tại trên Windows: interface Windows chọn để ra 1.1.1.1 (GetBestInterface), gateway IPv4 của nó, MAC qua SendARP.</summary>
public sealed partial class WindowsNetworkIdentity : INetworkIdentity
{
    private static readonly uint Probe = BinaryPrimitives.ReadUInt32LittleEndian(IPAddress.Parse("1.1.1.1").GetAddressBytes());

    public NetworkSnapshot? Current()
    {
        try
        {
            if (GetBestInterface(Probe, out var index) != 0) return null;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var props = nic.GetIPProperties();
                if (props.GetIPv4Properties()?.Index != (int)index) continue;
                var gateway = props.GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                if (gateway is null) return null;
                var id = NetworkId.From(ReadMac(gateway), gateway.ToString(), nic.NetworkInterfaceType.ToString(), nic.Name);
                return id is null ? null : new NetworkSnapshot(id, $"{nic.Name} · {gateway}");
            }
            return null;
        }
        catch (Exception ex) when (ex is NetworkInformationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    internal static string? FormatMac(ReadOnlySpan<byte> mac) =>
        mac.Length == 6 ? NetworkId.NormalizeMac(string.Join(':', mac.ToArray().Select(b => b.ToString("x2", CultureInfo.InvariantCulture)))) : null;

    private static string? ReadMac(IPAddress gateway)
    {
        var dest = BinaryPrimitives.ReadUInt32LittleEndian(gateway.GetAddressBytes());
        var buffer = new byte[8];
        var length = (uint)buffer.Length;
        return SendARP(dest, 0, buffer, ref length) == 0 && length >= 6 ? FormatMac(buffer.AsSpan(0, 6)) : null;
    }

    [LibraryImport("iphlpapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetBestInterface(uint destAddr, out uint bestIfIndex);

    [LibraryImport("iphlpapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SendARP(uint destIp, uint srcIp, [Out] byte[] macAddr, ref uint physicalAddrLen);
}
```

Nếu `LibraryImport` báo cần `AllowUnsafeBlocks` thì thêm `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` vào `LunaDns.Platform.Windows.csproj`. `SendARP` có thể mất tới ~3 giây khi gateway không trả lời; chỉ gọi từ luồng nền (Task 4 gọi trong timer).

- [ ] **Step 4: Chạy test, thấy qua**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests --filter "FullyQualifiedName~WindowsNetworkIdentityTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Platform.Windows tests/LunaDns.Platform.Windows.Tests
git commit -m "feat(windows): read the default gateway and its MAC to identify the current network"
```

---

### Task 4: App — `NetworkWatcher`

**Files:**
- Create: `src/LunaDns.App/NetworkProfiles/NetworkWatcher.cs`
- Test: `tests/LunaDns.App.Tests/NetworkProfiles/NetworkWatcherTests.cs`

**Interfaces:**
- Consumes: `INetworkIdentity`, `NetworkSnapshot` (Task 1).
- Produces: `internal sealed class NetworkWatcher(INetworkIdentity identity, TimeProvider time) : IDisposable` với `NetworkSnapshot? Current`, `event Action<NetworkSnapshot?>? Changed`, `void Start()`, `internal void Poke()`; `static readonly TimeSpan Settle = 3 s, PollInterval = 60 s`.

- [ ] **Step 1: Viết test hỏng**

```csharp
using LunaDns.App.NetworkProfiles;
using LunaDns.Core.Net;
using LunaDns.Core.Platform;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests.NetworkProfiles;

public class NetworkWatcherTests
{
    private static NetworkSnapshot Net(string mac) => new(NetworkId.From(mac, "192.168.1.1", "Wireless80211", "Wi-Fi")!, "Wi-Fi · 192.168.1.1");

    private sealed class FakeIdentity : INetworkIdentity
    {
        public NetworkSnapshot? Value { get; set; }
        public int Calls { get; private set; }
        public NetworkSnapshot? Current() { Calls++; return Value; }
    }

    [Fact]
    public void Start_ReadsCurrent_NoEvent()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        using var watcher = new NetworkWatcher(identity, new FakeTimeProvider());
        var events = 0;
        watcher.Changed += _ => events++;
        watcher.Start();
        Assert.Equal(identity.Value, watcher.Current);
        Assert.Equal(0, events);
    }

    [Fact]
    public void BurstOfPokes_OneEventAfterSettle()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        var time = new FakeTimeProvider();
        using var watcher = new NetworkWatcher(identity, time);
        var seen = new List<NetworkSnapshot?>();
        watcher.Changed += seen.Add;
        watcher.Start();
        identity.Value = Net("aa:bb:cc:09:09:09");
        watcher.Poke();
        time.Advance(TimeSpan.FromSeconds(2));
        watcher.Poke();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(seen);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([identity.Value], seen);
    }

    [Fact]
    public void SameNetwork_NoEvent()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        var time = new FakeTimeProvider();
        using var watcher = new NetworkWatcher(identity, time);
        var events = 0;
        watcher.Changed += _ => events++;
        watcher.Start();
        watcher.Poke();
        time.Advance(NetworkWatcher.Settle);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Poll_CatchesChangeWithoutEvent()
    {
        var identity = new FakeIdentity { Value = Net("aa:bb:cc:01:02:03") };
        var time = new FakeTimeProvider();
        using var watcher = new NetworkWatcher(identity, time);
        var seen = new List<NetworkSnapshot?>();
        watcher.Changed += seen.Add;
        watcher.Start();
        identity.Value = null;
        time.Advance(NetworkWatcher.PollInterval);
        time.Advance(NetworkWatcher.Settle);
        Assert.Equal([null], seen);
    }
}
```

- [ ] **Step 2: Chạy test, thấy hỏng**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter "FullyQualifiedName~NetworkWatcherTests"`
Expected: FAIL biên dịch.

- [ ] **Step 3: Cài đặt**

```csharp
using System.Net.NetworkInformation;
using LunaDns.Core.Net;
using LunaDns.Core.Platform;

namespace LunaDns.App.NetworkProfiles;

/// <summary>
/// Gộp NetworkAddressChanged và poll 60 giây thành một sự kiện mỗi lần đổi mạng thật: chờ yên 3 giây rồi đọc INetworkIdentity,
/// chỉ phát khi NetworkId khác lần trước. Changed chạy trên luồng timer; người nghe tự chuyển về luồng UI.
/// </summary>
internal sealed class NetworkWatcher(INetworkIdentity identity, TimeProvider time) : IDisposable
{
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private ITimer? _settle;
    private ITimer? _poll;
    private bool _disposed;

    public NetworkSnapshot? Current { get; private set; }

    public event Action<NetworkSnapshot?>? Changed;

    public void Start()
    {
        Current = identity.Current();
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        _poll = time.CreateTimer(_ => Poke(), null, PollInterval, PollInterval);
    }

    internal void Poke()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _settle ??= time.CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _settle.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnAddressChanged(object? sender, EventArgs e) => Poke();

    private void Check()
    {
        var now = identity.Current();
        lock (_gate)
        {
            if (_disposed || now?.Id == Current?.Id) return;
            Current = now;
        }
        Changed?.Invoke(now);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _settle?.Dispose();
        _poll?.Dispose();
    }
}
```

- [ ] **Step 4: Chạy test, thấy qua**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter "FullyQualifiedName~NetworkWatcherTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.App/NetworkProfiles/NetworkWatcher.cs tests/LunaDns.App.Tests/NetworkProfiles/NetworkWatcherTests.cs
git commit -m "feat(app): network watcher fires once per real network change after a 3 s settle"
```

---

### Task 5: App — `NetworkProfileCoordinator` và Kiểm tra dùng cả độ trễ

**Files:**
- Create: `src/LunaDns.App/NetworkProfiles/INetworkProfileHost.cs`, `src/LunaDns.App/NetworkProfiles/NetworkProfileCoordinator.cs`
- Modify: `src/LunaDns.App/ProbeRunner.cs`, `src/LunaDns.App/Strings.cs`, `src/LunaDns.App/Strings.resx`
- Test: `tests/LunaDns.App.Tests/NetworkProfiles/NetworkProfileCoordinatorTests.cs`, `tests/LunaDns.App.Tests/StringsTests.cs`

**Interfaces:**
- Consumes: `NetworkSnapshot`, `NetworkProfile`, `NetworkProfileStore` (Task 1); `TuningState(DohProvider Doh, TuneCandidate Candidate)`, `TuningApplyResult` (có `Success`, `Message`), `FeatureStatus(bool DnsRouting, bool SniProxy, …)`, `ProbeResult(Host, Verdict, …)`, `ProbeTargets.Steam` (có sẵn).
- Produces:
  ```csharp
  internal interface INetworkProfileHost
  {
      bool RememberPerNetwork { get; }
      FeatureStatus Status { get; }
      bool IsBusy { get; }
      Task<TuningState?> GetTuningAsync();
      Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate);
      Task<IReadOnlyList<ProbeResult>> ProbeSteamAsync(CancellationToken cancellationToken);
      Task RunBusyAsync(Func<Task> work);
      void ShowError(string message);
  }
  ```
  `internal sealed class NetworkProfileCoordinator(NetworkProfileStore store, INetworkProfileHost host, TimeProvider time, ILogger logger)` với:
  `NetworkSnapshot? Network`, `string StatusLine`, `bool ShowBlocked`, `event Action? Changed`,
  `Task OnNetworkChangedAsync(NetworkSnapshot? snapshot)`, `Task OnBusyEndedAsync()`,
  `Task OnCheckCompletedAsync(IReadOnlyList<ProbeResult> results)`, `void OnTuneSucceeded(DohProvider doh, TuneCandidate candidate)`,
  `void Forget()`, `void ClearAll()`, `void DismissBlocked()`, `static bool Passes(IReadOnlyList<ProbeResult> results, bool sniProxyOn)`.

- [ ] **Step 1: Chuỗi mới**

Thêm vào `Strings.resx` đúng các khoá và câu chữ ở Global Constraints. Thêm vào `Strings.cs` thuộc tính cho các khoá không có `Format` (`NetworkBlockedBanner`, `ForgetNetwork`, `RememberPerNetworkSwitch`, `RememberPerNetworkHint`, `ClearNetworkProfiles`, `ClearNetworkProfilesTitle`, `ClearNetworkProfilesText`) theo mẫu `public static string X => Get(nameof(X));`. Ba khoá `*Format` dùng qua `Strings.Format(nameof(...), …)` như `TuneNothingWorkedFormat`. Nếu `StringsTests` có danh sách khoá cần có thì thêm khoá mới vào đó.

- [ ] **Step 2: Viết test hỏng**

```csharp
using LunaDns.App.Features;
using LunaDns.App.NetworkProfiles;
using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Net;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests.NetworkProfiles;

public sealed class NetworkProfileCoordinatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lunadns-coord-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeHost _host = new();
    private readonly NetworkProfileStore _store;
    private readonly NetworkProfileCoordinator _coordinator;

    private static readonly NetworkSnapshot Home = new(NetworkId.From("aa:bb:cc:01:02:03", "192.168.1.1", "Wireless80211", "Wi-Fi")!, "Wi-Fi · 192.168.1.1");
    private static readonly NetworkSnapshot Phone = new(NetworkId.From("aa:bb:cc:09:09:09", "172.20.10.1", "Wireless80211", "Wi-Fi")!, "Wi-Fi · 172.20.10.1");
    private static readonly TuneCandidate Mid = new(new HelloSplit(SplitMode.MidSni, Records: true), 10);

    public NetworkProfileCoordinatorTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new NetworkProfileStore(Path.Combine(_dir, "profiles.json"));
        _coordinator = new NetworkProfileCoordinator(_store, _host, _time, NullLogger.Instance);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static IReadOnlyList<ProbeResult> All(ProbeVerdict verdict) =>
        [.. ProbeTargets.Steam.Select(h => new ProbeResult(h, verdict, [], [], false))];

    private void Save(NetworkSnapshot net, DohProvider doh, TuneCandidate candidate, bool stale = false) =>
        _store.Upsert(new NetworkProfile(net.Id.Value, net.Id.Weak, net.Label, doh, candidate, _time.GetUtcNow(), _time.GetUtcNow(), stale));

    [Fact]
    public async Task KnownNetwork_DifferentTuning_Applies()
    {
        Save(Home, DohProvider.Google, Mid);
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Equal((DohProvider.Google, Mid), Assert.Single(_host.Applied));
        Assert.Empty(_host.ProbeCalls);
        Assert.Contains("đã dùng cấu hình đã lưu", _coordinator.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KnownNetwork_SameTuning_OnlyTouches()
    {
        Save(Home, DohProvider.Cloudflare, TuneCandidate.Default);
        _time.Advance(TimeSpan.FromHours(1));
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Empty(_host.Applied);
        Assert.Equal(_time.GetUtcNow(), _store.Find(Home.Id.Value)!.LastSeen);
    }

    [Fact]
    public async Task NewNetwork_Passes_Saves()
    {
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Phone);
        var saved = _store.Find(Phone.Id.Value)!;
        Assert.Equal(DohProvider.Cloudflare, saved.Doh);
        Assert.Equal(TuneCandidate.Default, saved.Candidate);
        Assert.False(_coordinator.ShowBlocked);
    }

    [Fact]
    public async Task NewNetwork_Blocked_Banner_NoSave()
    {
        _host.NextProbe = All(ProbeVerdict.Unreachable);
        await _coordinator.OnNetworkChangedAsync(Phone);
        Assert.Null(_store.Find(Phone.Id.Value));
        Assert.True(_coordinator.ShowBlocked);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SniFiltered_PassesOnlyWithProxy(bool proxyOn, bool expected) =>
        Assert.Equal(expected, NetworkProfileCoordinator.Passes(All(ProbeVerdict.SniFiltered), proxyOn));

    [Fact]
    public async Task Busy_SkipsThenRunsWhenBusyEnds()
    {
        _host.Busy = true;
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Phone);
        Assert.Empty(_host.ProbeCalls);
        _host.Busy = false;
        await _coordinator.OnBusyEndedAsync();
        Assert.Single(_host.ProbeCalls);
        await _coordinator.OnBusyEndedAsync();
        Assert.Single(_host.ProbeCalls); // cùng mạng, đã xử lý
    }

    [Fact]
    public async Task ChangeDuringProbe_CancelsOld_NoSave()
    {
        _host.HoldProbe = new TaskCompletionSource();
        _host.NextProbe = All(ProbeVerdict.Ok);
        var first = _coordinator.OnNetworkChangedAsync(Phone);
        _host.Busy = true; // lượt đầu đang trong RunBusyAsync
        await _coordinator.OnNetworkChangedAsync(Home);
        _host.HoldProbe.SetResult();
        await first;
        Assert.True(_host.ProbeCalls[0].IsCancellationRequested);
        Assert.Null(_store.Find(Phone.Id.Value));
    }

    [Fact]
    public async Task ApplyFails_ShowsError_KeepsProfile()
    {
        Save(Home, DohProvider.Google, Mid);
        _host.ApplyResult = new TuningApplyResult(false, Strings.DaemonNeedsUpdate);
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Equal(Strings.DaemonNeedsUpdate, Assert.Single(_host.Errors));
        Assert.NotNull(_store.Find(Home.Id.Value));
    }

    [Fact]
    public async Task Off_OrNoFeature_DoesNothing()
    {
        Save(Home, DohProvider.Google, Mid);
        _host.Remember = false;
        await _coordinator.OnNetworkChangedAsync(Home);
        _host.Remember = true;
        _host.Status = new FeatureStatus(false, false, false, null, 0);
        await _coordinator.OnNetworkChangedAsync(Phone);
        Assert.Empty(_host.Applied);
        Assert.Empty(_host.ProbeCalls);
    }

    [Fact]
    public async Task ManualCheckBlocked_OnKnownNetwork_MarksStale_NextChangeProbes()
    {
        Save(Home, DohProvider.Google, Mid);
        await _coordinator.OnNetworkChangedAsync(Home);
        await _coordinator.OnCheckCompletedAsync(All(ProbeVerdict.Unreachable));
        Assert.True(_store.Find(Home.Id.Value)!.Stale);
        Assert.True(_coordinator.ShowBlocked);
        await _coordinator.OnNetworkChangedAsync(Phone);
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Single(_host.ProbeCalls.Skip(1)); // Home stale: Kiểm tra thay vì áp
        Assert.False(_store.Find(Home.Id.Value)!.Stale);
    }

    [Fact]
    public async Task TuneSucceeded_SavesForCurrentNetwork_Forget_Clear()
    {
        await _coordinator.OnNetworkChangedAsync(null);
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Home);
        _coordinator.OnTuneSucceeded(DohProvider.Quad9, Mid);
        Assert.Equal(DohProvider.Quad9, _store.Find(Home.Id.Value)!.Doh);
        _coordinator.Forget();
        Assert.Null(_store.Find(Home.Id.Value));
        Save(Phone, DohProvider.Google, Mid);
        _coordinator.ClearAll();
        Assert.Empty(_store.All);
    }

    private sealed class FakeHost : INetworkProfileHost
    {
        public bool Remember { get; set; } = true;
        public bool Busy { get; set; }
        public FeatureStatus Status { get; set; } = new(true, true, false, 18080, 0);
        public TuningState Tuning { get; set; } = new(DohProvider.Cloudflare, TuneCandidate.Default);
        public TuningApplyResult ApplyResult { get; set; } = new(true, null);
        public IReadOnlyList<ProbeResult> NextProbe { get; set; } = [];
        public TaskCompletionSource? HoldProbe { get; set; }
        public List<(DohProvider, TuneCandidate)> Applied { get; } = [];
        public List<CancellationToken> ProbeCalls { get; } = [];
        public List<string> Errors { get; } = [];

        public bool RememberPerNetwork => Remember;
        public bool IsBusy => Busy;
        public Task<TuningState?> GetTuningAsync() => Task.FromResult<TuningState?>(Tuning);

        public Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate)
        {
            Applied.Add((doh, candidate));
            if (ApplyResult.Success) Tuning = new TuningState(doh, candidate);
            return Task.FromResult(ApplyResult);
        }

        public async Task<IReadOnlyList<ProbeResult>> ProbeSteamAsync(CancellationToken cancellationToken)
        {
            ProbeCalls.Add(cancellationToken);
            if (HoldProbe is { } hold) { HoldProbe = null; await hold.Task; }
            return NextProbe;
        }

        public async Task RunBusyAsync(Func<Task> work)
        {
            await work();
        }

        public void ShowError(string message) => Errors.Add(message);
    }
}
```

Nếu `TuningApplyResult` có constructor khác `(bool Success, string? Message)` thì sửa test cho khớp file `src/LunaDns.App/Features/TuningApplyResult.cs`.

- [ ] **Step 3: Chạy test, thấy hỏng**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter "FullyQualifiedName~NetworkProfileCoordinatorTests"`
Expected: FAIL biên dịch.

- [ ] **Step 4: Cài đặt**

`INetworkProfileHost.cs`: đúng interface ở khối Interfaces, kèm `/// <summary>` cho từng thành viên (`IsBusy`: Kiểm tra, Tìm cấu hình, Đo, Lưu Tự nhập, bật/tắt tính năng hoặc áp tên miền đang chạy; `RunBusyAsync`: chạy như `MainViewModel.RunBusyAsync(Features, …)` để công tắc xám trong lúc áp).

`NetworkProfileCoordinator.cs`:

```csharp
using System.Globalization;
using LunaDns.App.Features;
using LunaDns.Core.Dns;
using LunaDns.Core.Net;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.NetworkProfiles;

/// <summary>
/// Hồ sơ theo mạng (spec 2026-11-28). Chạy trên luồng UI. Mạng có hồ sơ: áp tổ hợp đã lưu. Mạng mới hoặc hồ sơ cũ (Stale):
/// Kiểm tra ngầm nhóm Steam, đạt thì lưu, bị chặn thì hiện banner. Đang bận thì bỏ lượt, chạy lại khi hết bận.
/// </summary>
internal sealed partial class NetworkProfileCoordinator(NetworkProfileStore store, INetworkProfileHost host, TimeProvider time, ILogger logger)
{
    private CancellationTokenSource? _run;
    private NetworkId? _handled;
    private bool _applied;

    public NetworkSnapshot? Network { get; private set; }

    public bool ShowBlocked { get; private set; }

    public event Action? Changed;

    public string StatusLine
    {
        get
        {
            if (Network is not { } net) return string.Empty;
            var profile = store.Find(net.Id.Value);
            if (profile is null) return Strings.Format(nameof(Strings.NetworkNotSavedFormat), net.Label);
            var date = profile.SavedAt.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);
            return Strings.Format(_applied ? nameof(Strings.NetworkAppliedFormat) : nameof(Strings.NetworkSavedFormat), net.Label, date);
        }
    }

    public static bool Passes(IReadOnlyList<ProbeResult> results, bool sniProxyOn)
    {
        ArgumentNullException.ThrowIfNull(results);
        var steam = results.Where(r => ProbeTargets.Steam.Contains(r.Host, StringComparer.OrdinalIgnoreCase)).ToList();
        return steam.Count > 0 && steam.All(r => r.Verdict == ProbeVerdict.Ok || (sniProxyOn && r.Verdict == ProbeVerdict.SniFiltered));
    }

    public Task OnNetworkChangedAsync(NetworkSnapshot? snapshot)
    {
        _run?.Cancel();
        Network = snapshot;
        _applied = false;
        ShowBlocked = false;
        Changed?.Invoke();
        return HandleAsync();
    }

    public Task OnBusyEndedAsync() => Network?.Id == _handled ? Task.CompletedTask : HandleAsync();

    private async Task HandleAsync()
    {
        if (Network is not { } net || !host.RememberPerNetwork || !(host.Status.DnsRouting || host.Status.SniProxy)) return;
        if (host.IsBusy) return; // OnBusyEndedAsync chạy lại
        _handled = net.Id;
        using var run = new CancellationTokenSource();
        _run = run;
        await host.RunBusyAsync(async () =>
        {
            var profile = store.Find(net.Id.Value);
            if (profile is { Stale: false }) await ApplyAsync(net, profile);
            else await ProbeAndSaveAsync(net, run.Token);
        });
        if (ReferenceEquals(_run, run)) _run = null;
        Changed?.Invoke();
    }

    private async Task ApplyAsync(NetworkSnapshot net, NetworkProfile profile)
    {
        var current = await host.GetTuningAsync();
        store.Touch(profile.Id, time.GetUtcNow());
        if (current is { } c && c.Doh == profile.Doh && c.Candidate == profile.Candidate) return;
        var result = await host.ApplyTuningAsync(profile.Doh, profile.Candidate);
        if (!result.Success)
        {
            LogApplyFailed(logger, net.Id.Short);
            host.ShowError(result.Message ?? Strings.UnexpectedError);
            return;
        }
        _applied = Network?.Id == net.Id;
        LogApplied(logger, net.Id.Short);
    }

    private async Task ProbeAndSaveAsync(NetworkSnapshot net, CancellationToken token)
    {
        IReadOnlyList<ProbeResult> results;
        try
        {
            results = await host.ProbeSteamAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested || Network?.Id != net.Id) return;
        await RecordAsync(net, results);
    }

    public async Task OnCheckCompletedAsync(IReadOnlyList<ProbeResult> results)
    {
        if (Network is not { } net || !host.RememberPerNetwork) return;
        await RecordAsync(net, results);
        Changed?.Invoke();
    }

    private async Task RecordAsync(NetworkSnapshot net, IReadOnlyList<ProbeResult> results)
    {
        if (Passes(results, host.Status.SniProxy))
        {
            if (await host.GetTuningAsync() is { } tuning) Save(net, tuning.Doh, tuning.Candidate);
            ShowBlocked = false;
            LogSaved(logger, net.Id.Short);
            return;
        }
        if (store.Find(net.Id.Value) is not null) store.MarkStale(net.Id.Value);
        ShowBlocked = true;
        LogBlocked(logger, net.Id.Short);
    }

    public void OnTuneSucceeded(DohProvider doh, TuneCandidate candidate)
    {
        if (Network is not { } net || !host.RememberPerNetwork) return;
        Save(net, doh, candidate);
        ShowBlocked = false;
        Changed?.Invoke();
    }

    public void Forget()
    {
        if (Network is { } net) store.Remove(net.Id.Value);
        _applied = false;
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        store.Clear();
        _applied = false;
        Changed?.Invoke();
    }

    public void DismissBlocked()
    {
        ShowBlocked = false;
        Changed?.Invoke();
    }

    private void Save(NetworkSnapshot net, DohProvider doh, TuneCandidate candidate)
    {
        var now = time.GetUtcNow();
        store.Upsert(new NetworkProfile(net.Id.Value, net.Id.Weak, net.Label, doh, candidate, now, now));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Hồ sơ mạng {Network}: đã áp tổ hợp đã lưu")]
    private static partial void LogApplied(ILogger logger, string network);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hồ sơ mạng {Network}: áp tổ hợp đã lưu lỗi")]
    private static partial void LogApplyFailed(ILogger logger, string network);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hồ sơ mạng {Network}: Kiểm tra đạt, đã lưu")]
    private static partial void LogSaved(ILogger logger, string network);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hồ sơ mạng {Network}: Kiểm tra bị chặn")]
    private static partial void LogBlocked(ILogger logger, string network);
}
```

Ghi chú cho test `ChangeDuringProbe_CancelsOld_NoSave`: lượt thứ hai gặp `IsBusy` nên bỏ lượt, nhưng `OnNetworkChangedAsync` đã huỷ `_run` của lượt đầu, và lượt đầu thấy `Network` đã khác nên không lưu.

`ProbeRunner.RunAsync`: đổi `new TlsProbe(current.Tuning().Split, loggerFactory.CreateLogger<TlsProbe>())` thành

```csharp
            new TlsProbe(current.Tuning().Split, current.Tuning().SplitDelay, loggerFactory.CreateLogger<TlsProbe>()),
```

để "đạt" của Kiểm tra dùng đúng tổ hợp đang chạy (cả độ trễ), như `NetworkTuner` (spec mục "Đạt").

- [ ] **Step 5: Chạy test, thấy qua**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows`
Expected: PASS hết (kể cả `StringsTests`).

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests
git commit -m "feat(app): network profile coordinator applies a saved profile, or checks and saves on a new network"
```

---

### Task 6: Nối dây vào app, giao diện, tài liệu

**Files:**
- Modify: `src/LunaDns.App/PlatformInfo.cs`, `src/LunaDns.App/Windows/PlatformInfo.Windows.cs`, `src/LunaDns.App/MacOS/PlatformInfo.MacOS.cs`, `src/LunaDns.App/AppPaths.cs`, `src/LunaDns.App/AppServices.cs`, `src/LunaDns.App/ViewModels/MainViewModel.cs`, `src/LunaDns.App/Views/MainWindow.axaml`, `docs/guide/en/03-switches.md`, `docs/guide/03-ba-cong-tac.md`
- Test: `tests/LunaDns.App.Tests/MainWindowLayoutTests.cs` (nếu file kiểm có binding/nút theo tên), thử tay

**Interfaces:**
- Consumes: mọi thứ ở Task 1–5.
- Produces: `PlatformInfo.CreateNetworkIdentity() → INetworkIdentity`; `AppPaths.ProfilesFile`; `AppServices.ProbeSteamAsync(CancellationToken)`.

- [ ] **Step 1: Nền tảng và đường dẫn**

`PlatformInfo.cs`: `public static partial INetworkIdentity CreateNetworkIdentity();`
`PlatformInfo.Windows.cs`: `public static partial INetworkIdentity CreateNetworkIdentity() => new WindowsNetworkIdentity();`
`PlatformInfo.MacOS.cs`: `public static partial INetworkIdentity CreateNetworkIdentity() => new MacNetworkIdentity(new ProcessRunner());` (dùng cùng cách dựng `ProcessRunner` như chỗ khác trong project mac; nếu constructor cần logger thì truyền như ở đó).
`AppPaths.cs`: `public string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");` cạnh `SettingsFile`.
`AppServices.cs`: 

```csharp
    /// <summary>Kiểm tra ngầm 4 host Steam cho hồ sơ theo mạng; không ghi LastProbe để nút Chẩn đoán giữ lần Kiểm tra của người dùng.</summary>
    public Task<IReadOnlyList<ProbeResult>> ProbeSteamAsync(CancellationToken cancellationToken) =>
        _probes.RunAsync(ProbeTargets.Steam, cancellationToken);
```

- [ ] **Step 2: `MainViewModel` làm host**

- Cho `MainViewModel` cài `INetworkProfileHost` (tường minh, các thành viên `internal`/private qua explicit interface):
  - `RememberPerNetwork => _services.Settings.RememberPerNetwork`; `Status => _services.Features.Status`;
  - `IsBusy => _busyCount > 0 || _isTuning || _isMeasuringDoh || _isSavingCustomDoh || _applyingDomains`;
  - `GetTuningAsync => Task.Run(_services.Features.GetTuningAsync)`;
  - `ApplyTuningAsync`: `var r = await Task.Run(() => _services.ApplyTuningAsync(doh, c)); NotifyTuningSettingsChanged(); if (r.Message == Strings.DaemonNeedsUpdate) SetDaemonNeedsUpdate(true); return r;`
  - `ProbeSteamAsync(token) => Task.Run(() => _services.ProbeSteamAsync(token))`;
  - `RunBusyAsync(work) => RunBusyAsync(Features, work)`; `ShowError(m) => ShowMessage(m, Tone.Error)`.
- Constructor: nhận thêm tham số `INetworkIdentity? networkIdentity = null` (giữ chữ ký cũ cho test). Có thì dựng `_profiles = new NetworkProfileCoordinator(new NetworkProfileStore(_services.Paths.ProfilesFile), this, TimeProvider.System, _services.LoggerFactory.CreateLogger<NetworkProfileCoordinator>())`, `_watcher = new NetworkWatcher(networkIdentity, TimeProvider.System)`; `_watcher.Changed += s => Dispatcher.UIThread.Post(() => _ = _profiles.OnNetworkChangedAsync(s))`; `_profiles.Changed += RaiseNetworkProperties`. `Start()` watcher ở luồng nền (`Task.Run(_watcher.Start)`, vì `SendARP` có thể chậm) rồi Post `_profiles.OnNetworkChangedAsync(_watcher.Current)` sau `ResumeThenCheckAsync` xong. Dispose watcher cạnh chỗ gỡ `FeatureStopped`.
- `App.axaml.cs:50`: truyền `PlatformInfo.CreateNetworkIdentity()`.
- Gọi coordinator:
  - cuối `RunBusyAsync` (khi `_busyCount` về 0) và cuối `TuneAsync` `finally`: `_ = _profiles?.OnBusyEndedAsync();`
  - `CheckAsync` sau `ShowGroupProbes`: `if (_services.LastProbe is { } probe && _profiles is not null) await _profiles.OnCheckCompletedAsync(probe);`
  - `ApplyTuneRunAsync`, nhánh thành công (cả `unchanged`): `_profiles?.OnTuneSucceeded(doh, candidate);`
- Thuộc tính cho XAML: `NetworkLine => _profiles?.StatusLine ?? ""`, `ShowNetworkLine => NetworkLine.Length > 0`, `ShowNetworkBlocked => _profiles?.ShowBlocked ?? false`, `CanForgetNetwork`, `RememberPerNetwork`. `RaiseNetworkProperties` gọi `OnPropertyChanged` cho từng cái.
- Lệnh: `ForgetNetworkCommand` (`_profiles.Forget()`), `DismissNetworkBlockedCommand`, `ToggleRememberPerNetworkCommand` (`SaveSettings(s => s with { RememberPerNetwork = !s.RememberPerNetwork })` rồi raise), `ClearNetworkProfilesCommand` (`if (await _prompts.ConfirmAsync(Strings.ClearNetworkProfilesTitle, Strings.ClearNetworkProfilesText)) _profiles.ClearAll();`). Nút banner Tìm cấu hình dùng `TuneCommand` có sẵn.

- [ ] **Step 3: XAML**

`MainWindow.axaml`:
- Ngay dưới banner U1 (dòng ~89), banner mới theo đúng mẫu U1: `IsVisible="{Binding ShowNetworkBlocked}"`, chữ `{x:Static app:Strings.NetworkBlockedBanner}`, nút accent `Command="{Binding TuneCommand}"` nội dung `{x:Static app:Strings.TuneButton}` (dùng khoá nút Tìm cấu hình đang có; tên khoá thật xem trong `Strings.cs`), nút đóng `DismissNetworkBlockedCommand`.
- Khu Trạng thái (comment dòng ~240), trên hai công tắc: `StackPanel Orientation="Horizontal"` với `TextBlock Classes="secondary" Text="{Binding NetworkLine}"` và nút liên kết `ForgetNetwork` (`IsVisible="{Binding CanForgetNetwork}"`), cả khối `IsVisible="{Binding ShowNetworkLine}"`.
- Dưới ô Tự bật lại (dòng ~284–288), khối cùng mẫu: `CheckBox` `RememberPerNetworkSwitch` (`IsChecked="{Binding RememberPerNetwork, Mode=OneWay}"`, `Command="{Binding ToggleRememberPerNetworkCommand}"`) và `TextBlock` `RememberPerNetworkHint`. Hiện trên cả hai nền tảng.
- Khu Gỡ & dọn (comment dòng ~367): nút `ClearNetworkProfiles` → `ClearNetworkProfilesCommand`.

- [ ] **Step 4: Tài liệu**

Thêm mục sau "Turn back on automatically" trong `docs/guide/en/03-switches.md`:

```markdown
## Remember per network (Nhớ cấu hình theo mạng)

When a configuration gets Steam through (after **Tìm cấu hình hợp mạng**, or a **Kiểm tra** that passes), LunaDNS remembers it for the network you are on, recognised by your router. When you come back to that network, it switches to the saved configuration by itself. On a network it has not seen, it runs a quiet check and, if Steam looks blocked, shows a banner with **Tìm cấu hình hợp mạng**. Nothing is sent anywhere; profiles live in `profiles.json` next to `settings.json`. **Quên mạng này** (Forget this network) in the Status zone and **Xoá mọi cấu hình theo mạng** (Delete all per-network configurations) in Remove & clean up delete them; Quit & restore keeps them, because they are not system changes.
```

và mục tiếng Việt tương ứng (cùng nội dung, nhãn tiếng Việt) trong `docs/guide/03-ba-cong-tac.md` sau mục Tự bật lại.

- [ ] **Step 5: Build và test cả solution**

Run: `dotnet build LunaDns.sln -warnaserror` rồi `dotnet test`
Expected: build sạch; test PASS trừ 5 test `MacOS/*` trượt sẵn trên Windows `-f net8.0`.

- [ ] **Step 6: Commit**

```bash
git add src docs/guide tests
git commit -m "feat(app): remember the working configuration per network and reapply it when the network changes"
```

- [ ] **Step 7: Thử tay (PO, Windows rồi macOS)**

1. Wi‑Fi nhà, bật Định tuyến DNS + Proxy SNI, bấm Tìm cấu hình hợp mạng → dòng Trạng thái "Mạng: … — có cấu hình đã lưu (dd/MM)"; `profiles.json` có một mục, log không chứa MAC/IP gateway.
2. Chuyển sang 4G phát từ điện thoại → sau ~3–5 giây: Kiểm tra ngầm; đạt thì dòng "có cấu hình đã lưu", bị chặn thì banner "Mạng mới có vẻ chặn Steam."
3. Đổi tay Nâng cao (Windows) sang tổ hợp khác, quay lại Wi‑Fi nhà → tổ hợp đã lưu được áp, dòng "đã dùng cấu hình đã lưu".
4. Quên mạng này → dòng "chưa có cấu hình đã lưu". Xoá mọi cấu hình theo mạng → hỏi xác nhận, `profiles.json` rỗng.
5. Bỏ ô Nhớ cấu hình theo mạng, đổi mạng → không áp, không Kiểm tra ngầm.
6. macOS với daemon cũ: đổi sang mạng có hồ sơ khác tổ hợp → banner cần cập nhật dịch vụ nền, hồ sơ còn nguyên.

- [ ] **Step 8: Merge**

Sau khi CI trên nhánh xanh và PO thử tay xong: `git checkout main && git merge --ff-only feat/network-profiles && git push`.
