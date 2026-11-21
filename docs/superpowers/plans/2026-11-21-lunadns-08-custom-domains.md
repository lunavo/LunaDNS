# LunaDNS — Kế hoạch 8: Nhóm tên miền và danh sách tuỳ chỉnh

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Định tuyến DNS và Proxy SNI không còn chỉ áp cho Steam. User bật/tắt các nhóm dựng sẵn (Steam, Nền tảng game khác, Reddit) và tự quản lý danh sách Tuỳ chỉnh (thêm, sửa, xoá, kiểm tra, nhập/xuất), trên cả Windows và macOS.

**Architecture:** Danh sách cố định `GameDomains` đổi thành `DomainCatalog` (các nhóm dựng sẵn). Lựa chọn của user là `DomainSelection`, nằm trong `AppSettings`. Mọi tên miền đi qua `DomainPolicy`, gồm chuẩn hoá, Public Suffix List và danh sách cấm. Chốt này dùng ở mọi lớp: UI, settings, `FeatureSet`, router của từng nền tảng và dịch vụ nền macOS. `FeatureSet` giữ danh sách đang định tuyến và có `ReapplyAsync`: tắt rồi bật lại các tính năng đang chạy theo danh sách mới, lỗi thì quay về danh sách cũ.

**Tech Stack:** .NET 8, C# 12, Avalonia 11, xUnit 2.5, System.Text.Json (source-gen cho settings).

**Spec:** `docs/superpowers/specs/2026-11-21-lunadns-custom-domains-design.md` (gọi tắt "spec"), nối tiếp `docs/superpowers/specs/2026-11-13-lunadns-design.md` ("spec v1").

**Mockup:** https://claude.ai/artifact/LBQ8uWaT4RD659sEboSGtx (11 màn, dùng logo mới và icon thật của `Icons.axaml`). Khi plan và mockup lệch nhau về chữ hay bố cục, theo mockup; về hành vi, theo spec.

## Global Constraints

- Target `net8.0`, `Nullable` bật, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended` (đã có trong `Directory.Build.props`). Build phải sạch cảnh báo.
- `LunaDns.Core` không tham chiếu API riêng của Windows hay macOS.
- Không có `catch { }` rỗng. Không bắt `Exception` chung trừ ở ranh giới đã có tiền lệ (FeatureSet, ViewModel).
- Câu cho người dùng viết tiếng Việt có dấu, đặt trong `Strings.resx` (App) hoặc `DaemonCommands.Describe` (macOS). Tên định danh viết tiếng Anh.
- Mọi tên miền luôn **gồm cả tên miền con**. Không có kiểu khớp chính xác (spec mục 1).
- Tối đa **200** dòng Tuỳ chỉnh (`DomainPolicy.MaxCustomDomains`).
- Mặc định `EnabledGroups = [Steam, Custom]`, `Custom = []`, nên hành vi giống hệt bản 0.9.
- Lệnh kiểm tra:
  - Core: `dotnet build -warnaserror` · `dotnet test tests/LunaDns.Core.Tests`
  - macOS: `dotnet test tests/LunaDns.Platform.MacOS.Tests` (chạy được trên Mac)
  - Windows: `dotnet test tests/LunaDns.Platform.Windows.Tests` và `dotnet test tests/LunaDns.App.Tests` (chạy trên Windows; App.Tests cũng build cho `net8.0-windows`, xem commit 32beab3)
- Làm trên nhánh `feat/custom-domains`. Mỗi task một commit, thông điệp theo kiểu đang dùng trong repo (`feat(core): …`, `fix(macos): …`, `docs: …`).

## Cấu trúc file

```
src/LunaDns.Core/Domains/
  DomainGroup.cs           (sửa)  thêm OtherGames, Reddit, Custom; bỏ Other
  DomainCatalog.cs         (mới, thay GameDomains.cs) nhóm dựng sẵn
  PublicSuffixList.cs      (mới)  đọc public_suffix_list.dat nhúng sẵn
  public_suffix_list.dat   (mới)  EmbeddedResource, MPL-2.0
  DomainDenyList.cs        (mới)  danh sách cấm có lý do
  DomainPolicy.cs          (mới)  chuẩn hoá, kiểm tra, mã từ chối
  DomainSelection.cs       (mới)  lựa chọn của user + CustomDomain
src/LunaDns.Core/Settings/AppSettings.cs, JsonFileSettingsStore.cs (sửa)
src/LunaDns.Core/Features/FeatureDomains.cs, FeatureSet.cs, FeatureError.cs (sửa), ReapplyResult.cs (mới)
src/LunaDns.Core/Probing/ProbeTargets.cs, Features/ProbeSummary.cs (sửa)
src/LunaDns.Platform.Windows/Nrpt/NrptDnsRouter.cs (sửa)
src/LunaDns.Platform.MacOS/Resolver/ResolverDnsRouter.cs, Daemon/DaemonProtocol.cs, Daemon/DaemonCommands.cs (sửa)
src/LunaDns.MacCli/MacPaths.cs, Engine.cs (sửa)
src/LunaDns.App/Features/IFeatureController.cs, LocalFeatureController.cs, DomainApplyResult.cs (mới)
src/LunaDns.App/MacOS/DaemonFeatureController.cs, AppServices.cs, ProbeRunner.cs, ErrorMessages.cs (sửa)
src/LunaDns.App/DomainProbeText.cs (mới)
src/LunaDns.App/ViewModels/DomainGroupViewModel.cs, CustomDomainsViewModel.cs, CustomDomainRowViewModel.cs (mới), MainViewModel.cs, IUserPrompts.cs (sửa)
src/LunaDns.App/Views/CustomDomainsWindow.axaml(.cs), DomainListWindow.axaml(.cs) (mới), MainWindow.axaml(.cs) (sửa)
src/LunaDns.App/Strings.resx, Strings.cs (sửa)
docs/guide/10-ten-mien.md (mới), 03-ba-cong-tac.md, README.md, README.vi.md, docs/THIRD-PARTY-NOTICES.md (sửa)
docs/spikes/2026-11-custom-domains-spike.md (mới)
```

---

### Task 1: Nhánh làm việc + spike giới hạn NRPT và /etc/resolver

Spec mục 4.3 và 10.1. Kết quả spike quyết định NRPT dùng một luật cho mọi namespace như hiện nay, hay phải chia nhỏ.

**Files:**
- Create: `docs/spikes/2026-11-custom-domains-spike.md`
- Commit cùng: spec `docs/superpowers/specs/2026-11-21-lunadns-custom-domains-design.md` và plan này

- [ ] **Step 1: Tạo nhánh, commit spec + plan**

```bash
git switch -c feat/custom-domains
git add docs/superpowers/specs/2026-11-21-lunadns-custom-domains-design.md docs/superpowers/plans/2026-11-21-lunadns-08-custom-domains.md
git commit -m "docs: spec và kế hoạch nhóm tên miền + danh sách tuỳ chỉnh"
```

- [ ] **Step 2: Spike NRPT trên Windows** (máy thật hoặc `build/sandbox/LunaDNS.wsb`, PowerShell Admin)

```powershell
$ns = 1..250 | ForEach-Object { ".spike$_.example.org"; "spike$_.example.org" }
Measure-Command { Add-DnsClientNrptRule -Namespace $ns -NameServers 127.0.0.1 -Comment "LunaDNS spike" } | Select TotalMilliseconds
(Get-DnsClientNrptRule | Where-Object Comment -eq "LunaDNS spike").Namespace.Count
Resolve-DnsName spike1.example.org -DnsOnly -ErrorAction SilentlyContinue   # chỉ cần không treo
Get-DnsClientNrptRule | Where-Object Comment -eq "LunaDNS spike" | Remove-DnsClientNrptRule -Force
```

Ghi lại: lệnh có lỗi không, thời gian, số namespace đọc lại được (mong đợi 500).

- [ ] **Step 3: Spike /etc/resolver trên macOS**

```bash
for i in $(seq 1 250); do printf '# LunaDNS spike\nnameserver 127.0.0.1\nport 15353\n' | sudo tee /etc/resolver/spike$i.example.org >/dev/null; done
time scutil --dns | grep -c "spike"
time dscacheutil -q host -a name www.apple.com
sudo rm /etc/resolver/spike*.example.org
```

Ghi lại: số resolver `scutil` thấy (mong đợi 250), thời gian phân giải một tên miền ngoài danh sách có chậm đi rõ không.

- [ ] **Step 4: Ghi kết quả vào `docs/spikes/2026-11-custom-domains-spike.md`**

Mẫu nội dung (điền số đo thật):

```markdown
# Spike: giới hạn số tên miền (2026-11)

| Nền tảng | Số mục | Kết quả | Thời gian áp | Ghi chú |
|---|---|---|---|---|
| Windows 10 19045, NRPT 1 luật | 500 namespace | OK / lỗi … | … ms | |
| macOS 15, /etc/resolver | 250 file | OK / chậm … | … s | |

**Kết luận:** giữ 1 luật NRPT cho mọi namespace / chia luật mỗi N namespace.
```

Nếu NRPT lỗi ở 500 namespace: mở thêm một task sau Task 6 để `NrptDnsRouter.Apply` chia namespace thành nhiều luật cùng marker. Báo PO trước khi làm.

- [ ] **Step 5: Commit**

```bash
git add docs/spikes/2026-11-custom-domains-spike.md
git commit -m "docs(spike): giới hạn số namespace NRPT và /etc/resolver"
```

---

### Task 2: `DomainCatalog`, các nhóm mới và host mẫu theo nhóm

**Files:**
- Modify: `src/LunaDns.Core/Domains/DomainGroup.cs`
- Create: `src/LunaDns.Core/Domains/DomainCatalog.cs`
- Delete: `src/LunaDns.Core/Domains/GameDomains.cs`
- Modify: `src/LunaDns.Core/Probing/ProbeTargets.cs`
- Modify (đổi tên tham chiếu): `src/LunaDns.Core/Features/FeatureDomains.cs`, `src/LunaDns.Core/Hosts/HostsEditor.cs`, `src/LunaDns.Platform.Windows/Nrpt/NrptDnsRouter.cs`, `src/LunaDns.Platform.MacOS/Resolver/ResolverDnsRouter.cs`, mọi test đang dùng `GameDomains`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainCatalogTests.cs`

**Interfaces:**
- Produces:
  - `enum DomainGroup { Steam, Pubg, OtherGames, Reddit, Custom }`;
  - `static class DomainCatalog`: `IReadOnlyList<string> For(DomainGroup)`, trả rỗng cho `Custom`; `IReadOnlyList<DomainGroup> Selectable`, gồm `[Steam, OtherGames, Reddit]`; `IReadOnlyList<string> All`;
  - `ProbeTargets.For(DomainGroup)`: host mẫu của từng nhóm.

- [ ] **Step 1: Viết test hỏng**

```csharp
// tests/LunaDns.Core.Tests/Domains/DomainCatalogTests.cs
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainCatalogTests
{
    [Fact]
    public void Selectable_groups_are_steam_other_games_reddit() =>
        Assert.Equal([DomainGroup.Steam, DomainGroup.OtherGames, DomainGroup.Reddit], DomainCatalog.Selectable);

    [Fact]
    public void Custom_group_has_no_built_in_domains() =>
        Assert.Empty(DomainCatalog.For(DomainGroup.Custom));

    [Fact]
    public void Steam_group_is_unchanged_from_v1() =>
        Assert.Equal(
            ["steampowered.com", "steamcommunity.com", "steamstatic.com", "steam-chat.com",
             "steamusercontent.com", "steamgames.com", "steamcontent.com", "steamserver.net",
             "counter-strike.net", "dota2.com", "valvesoftware.com"],
            DomainCatalog.For(DomainGroup.Steam));

    [Theory]
    [InlineData(DomainGroup.Steam)]
    [InlineData(DomainGroup.OtherGames)]
    [InlineData(DomainGroup.Reddit)]
    public void Every_selectable_group_has_domains_and_probe_targets_inside_it(DomainGroup group)
    {
        Assert.NotEmpty(DomainCatalog.For(group));
        Assert.NotEmpty(ProbeTargets.For(group));
        foreach (var host in ProbeTargets.For(group))
            Assert.True(DomainMatcher.Matches(host, DomainCatalog.For(group)), host);
    }

    [Fact]
    public void Catalog_never_contains_anticheat() =>
        Assert.DoesNotContain(DomainCatalog.All, d => d.Contains("xenuine", StringComparison.Ordinal) || d == "accounts.pubg.com");
}
```

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainCatalogTests`
Expected: lỗi biên dịch, `DomainCatalog` không tồn tại.

- [ ] **Step 3: Cài đặt**

```csharp
// src/LunaDns.Core/Domains/DomainGroup.cs
namespace LunaDns.Core.Domains;

public enum DomainGroup
{
    Steam,
    Pubg,
    OtherGames,
    Reddit,
    /// <summary>Công tắc của nhóm Tuỳ chỉnh; tên miền nằm trong DomainSelection.Custom.</summary>
    Custom,
}
```

```csharp
// src/LunaDns.Core/Domains/DomainCatalog.cs
namespace LunaDns.Core.Domains;

/// <summary>
/// Nhóm tên miền dựng sẵn (spec KH8 mục 6). Không bao giờ chứa domain anti-cheat (spec v1 4.4, DomainDenyList).
/// Danh sách OtherGames và Reddit là ứng viên; Task 13 chốt sau khi thử thật trên mạng VN.
/// </summary>
public static class DomainCatalog
{
    private static readonly string[] Steam =
    [
        "steampowered.com", "steamcommunity.com", "steamstatic.com", "steam-chat.com",
        "steamusercontent.com", "steamgames.com", "steamcontent.com", "steamserver.net",
        "counter-strike.net", "dota2.com", "valvesoftware.com",
    ];

    // Chỉ là lobby/web; *xenuine* và accounts.pubg.com cố ý không có (spec v1 4.4).
    private static readonly string[] Pubg =
    [
        "playbattlegrounds.com", "pubg.com", "krafton.com", "pubgapp.com", "pubgesports.com",
    ];

    // Gộp nhóm Other cũ với store/launcher. Không có tên miền anti-cheat; xem DomainDenyList.
    private static readonly string[] OtherGames =
    [
        "narakathegame.com", "24entertainment.net", "playdeltaforce.com", "levelinfinite.com",
        "epicgames.com", "epicgames.dev", "unrealengine.com",
        "battle.net", "blizzard.com",
        "ea.com",
    ];

    private static readonly string[] Reddit =
    [
        "reddit.com", "redd.it", "redditmedia.com", "redditstatic.com", "reddituploads.com",
    ];

    public static IReadOnlyList<DomainGroup> Selectable { get; } = [DomainGroup.Steam, DomainGroup.OtherGames, DomainGroup.Reddit];

    public static IReadOnlyList<string> For(DomainGroup group) => group switch
    {
        DomainGroup.Steam => Steam,
        DomainGroup.Pubg => Pubg,
        DomainGroup.OtherGames => OtherGames,
        DomainGroup.Reddit => Reddit,
        DomainGroup.Custom => [],
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    public static IReadOnlyList<string> All { get; } = [.. Steam.Concat(Pubg).Concat(OtherGames).Concat(Reddit).Distinct()];
}
```

Riot chưa có trong danh sách: tên miền Vanguard có thể nằm dưới `riotgames.com`. Task 13 tra xong mới thêm.

```csharp
// src/LunaDns.Core/Probing/ProbeTargets.cs — thay toàn bộ file
using LunaDns.Core.Domains;

namespace LunaDns.Core.Probing;

/// <summary>Host mẫu để kiểm tra (spec v1 4.1, spec KH8 mục 6).</summary>
public static class ProbeTargets
{
    public static IReadOnlyList<string> Steam { get; } =
    [
        "store.steampowered.com", "steamcommunity.com", "api.steampowered.com", "cdn.akamai.steamstatic.com",
    ];

    public static IReadOnlyList<string> Pubg { get; } = ["prod-live-front.playbattlegrounds.com"];

    private static readonly string[] OtherGames = ["store.epicgames.com", "eu.battle.net", "www.ea.com"];

    private static readonly string[] Reddit = ["www.reddit.com", "i.redd.it"];

    public static IReadOnlyList<string> For(bool includePubg)
    {
        if (!includePubg) return Steam;
        return [.. Steam, .. Pubg];
    }

    public static IReadOnlyList<string> For(DomainGroup group) => group switch
    {
        DomainGroup.Steam => Steam,
        DomainGroup.Pubg => Pubg,
        DomainGroup.OtherGames => OtherGames,
        DomainGroup.Reddit => Reddit,
        DomainGroup.Custom => [],
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };
}
```

Sau đó:
- `git rm src/LunaDns.Core/Domains/GameDomains.cs`;
- thay mọi `GameDomains.` bằng `DomainCatalog.` (`grep -rln "GameDomains" src tests | xargs sed -i '' 's/GameDomains\./DomainCatalog./g'`);
- sửa các chỗ dùng `DomainGroup.Other` (nếu có) thành `DomainGroup.OtherGames`.

`NrptDnsRouter` và `ResolverDnsRouter` tạm thời vẫn dùng `DomainCatalog.All`; Task 6 sẽ thay. `HostsEditor` tiếp tục dùng `DomainCatalog.All`.

- [ ] **Step 4: Chạy test**

Run: `dotnet build -warnaserror && dotnet test tests/LunaDns.Core.Tests`
Expected: PASS toàn bộ, gồm `DomainMatcherTests` và `FeatureDomainsTests` cũ đã đổi tên tham chiếu.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(core): DomainCatalog thay GameDomains, thêm nhóm OtherGames, Reddit, Custom"
```

---

### Task 3: `PublicSuffixList` nhúng sẵn

**Files:**
- Create: `src/LunaDns.Core/Domains/public_suffix_list.dat` (tải từ `https://publicsuffix.org/list/public_suffix_list.dat`; **hỏi PO trước khi tải**, ghi ngày tải vào commit)
- Create: `src/LunaDns.Core/Domains/PublicSuffixList.cs`
- Modify: `src/LunaDns.Core/LunaDns.Core.csproj`
- Modify: `docs/THIRD-PARTY-NOTICES.md`
- Test: `tests/LunaDns.Core.Tests/Domains/PublicSuffixListTests.cs`

**Interfaces:**
- Produces: `sealed class PublicSuffixList`, gồm `static PublicSuffixList Default`, `static PublicSuffixList Parse(TextReader)` và `bool IsPublicSuffix(string host)`. `host` đã chuẩn hoá (chữ thường, punycode).

- [ ] **Step 1: Viết test hỏng**

```csharp
// tests/LunaDns.Core.Tests/Domains/PublicSuffixListTests.cs
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

public sealed class PublicSuffixListTests
{
    private const string Sample = """
        // comment
        com
        vn
        com.vn
        *.ck
        !www.ck
        // ===BEGIN PRIVATE DOMAINS===
        github.io
        公司.cn
        """;

    private static readonly PublicSuffixList List = PublicSuffixList.Parse(new StringReader(Sample));

    [Theory]
    [InlineData("com", true)]
    [InlineData("com.vn", true)]
    [InlineData("github.io", true)]
    [InlineData("foo.ck", true)]          // *.ck
    [InlineData("www.ck", false)]         // !www.ck
    [InlineData("anything", true)]        // luật ngầm "*": một nhãn luôn là hậu tố công cộng
    [InlineData("xn--55qx5d.cn", true)]   // 公司.cn đã chuyển punycode khi đọc
    [InlineData("reddit.com", false)]
    [InlineData("tuoitre.com.vn", false)]
    [InlineData("user.github.io", false)]
    public void Recognises_public_suffixes(string host, bool expected) =>
        Assert.Equal(expected, List.IsPublicSuffix(host));

    [Fact]
    public void Embedded_list_loads_and_knows_common_suffixes()
    {
        Assert.True(PublicSuffixList.Default.IsPublicSuffix("com"));
        Assert.True(PublicSuffixList.Default.IsPublicSuffix("com.vn"));
        Assert.True(PublicSuffixList.Default.IsPublicSuffix("github.io"));
        Assert.False(PublicSuffixList.Default.IsPublicSuffix("steampowered.com"));
    }
}
```

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter PublicSuffixListTests`
Expected: lỗi biên dịch, `PublicSuffixList` không tồn tại.

- [ ] **Step 3: Cài đặt**

```xml
<!-- src/LunaDns.Core/LunaDns.Core.csproj, thêm vào ItemGroup -->
<ItemGroup>
  <EmbeddedResource Include="Domains/public_suffix_list.dat" LogicalName="LunaDns.Core.public_suffix_list.dat" />
</ItemGroup>
```

```csharp
// src/LunaDns.Core/Domains/PublicSuffixList.cs
using System.Globalization;

namespace LunaDns.Core.Domains;

/// <summary>
/// Public Suffix List (publicsuffix.org, MPL-2.0), dùng cả phần ICANN lẫn PRIVATE. Chỉ trả lời "tên này có phải hậu tố công cộng
/// không" để chặn tên miền quá rộng (spec KH8 4.2). Cập nhật file .dat mỗi lần phát hành.
/// </summary>
public sealed class PublicSuffixList
{
    private const string ResourceName = "LunaDns.Core.public_suffix_list.dat";
    private static readonly IdnMapping Idn = new();
    private static readonly Lazy<PublicSuffixList> Embedded = new(LoadEmbedded);

    private readonly HashSet<string> _rules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _wildcards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exceptions = new(StringComparer.Ordinal);

    private PublicSuffixList()
    {
    }

    public static PublicSuffixList Default => Embedded.Value;

    public static PublicSuffixList Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var list = new PublicSuffixList();
        while (reader.ReadLine() is { } line)
        {
            var rule = line.Trim();
            if (rule.Length == 0 || rule.StartsWith("//", StringComparison.Ordinal)) continue;
            var space = rule.IndexOfAny([' ', '\t']);
            if (space >= 0) rule = rule[..space];
            if (rule.StartsWith('!')) list._exceptions.Add(ToAscii(rule[1..]));
            else if (rule.StartsWith("*.", StringComparison.Ordinal)) list._wildcards.Add(ToAscii(rule[2..]));
            else list._rules.Add(ToAscii(rule));
        }
        return list;
    }

    public bool IsPublicSuffix(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_exceptions.Contains(host)) return false;
        if (_rules.Contains(host)) return true;
        var dot = host.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0) return true; // luật ngầm "*"
        return _wildcards.Contains(host[(dot + 1)..]);
    }

    private static string ToAscii(string rule) => Idn.GetAscii(rule).ToLowerInvariant();

    private static PublicSuffixList LoadEmbedded()
    {
        using var stream = typeof(PublicSuffixList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Thiếu public_suffix_list.dat nhúng trong LunaDns.Core.");
        using var reader = new StreamReader(stream);
        return Parse(reader);
    }
}
```

Thêm vào `docs/THIRD-PARTY-NOTICES.md` mục: "Public Suffix List, https://publicsuffix.org, Mozilla Public License 2.0. Nhúng nguyên văn tại `src/LunaDns.Core/Domains/public_suffix_list.dat`."

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Core.Tests --filter PublicSuffixListTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core tests/LunaDns.Core.Tests docs/THIRD-PARTY-NOTICES.md
git commit -m "feat(core): nhúng Public Suffix List (bản tải ngày YYYY-MM-DD)"
```

---

### Task 4: `DomainDenyList` và `DomainPolicy`

Spec mục 4.1, 4.2, 4.4.

**Files:**
- Create: `src/LunaDns.Core/Domains/DomainDenyList.cs`
- Create: `src/LunaDns.Core/Domains/DomainPolicy.cs`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainPolicyTests.cs`

**Interfaces:**
- Consumes: `PublicSuffixList.Default.IsPublicSuffix(string)`, `DomainMatcher.Normalize(string)`, `DomainCatalog.All`.
- Produces:
  - `enum DomainRejection { None, Syntax, PublicSuffix, LocalName, Denied, Duplicate, Limit }`;
  - `sealed record DeniedDomain(string Domain, string Reason)`;
  - `static class DomainDenyList`: `IReadOnlyList<DeniedDomain> Domains`, `DeniedDomain? Find(string host)`;
  - `sealed record DomainCheck(string Input, string? Domain, DomainRejection Rejection, string? Detail)` có `bool Ok`;
  - `static class DomainPolicy`, gồm:
    - `const int MaxCustomDomains = 200`;
    - `string? Normalize(string raw)`;
    - `DomainRejection Validate(string host)`;
    - `bool IsRoutable(string host)`;
    - `IReadOnlyList<string> SplitInput(string text)`;
    - `DomainCheck Check(string raw, IReadOnlyCollection<string> existingCustom, IReadOnlyList<string> enabledCatalog)`.

- [ ] **Step 1: Viết test hỏng**

```csharp
// tests/LunaDns.Core.Tests/Domains/DomainPolicyTests.cs
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainPolicyTests
{
    [Theory]
    [InlineData("  Reddit.COM ", "reddit.com")]
    [InlineData("https://www.reddit.com/r/vietnam?x=1#top", "reddit.com")]
    [InlineData("http://user@old.reddit.com:8080/path", "old.reddit.com")]
    [InlineData("*.redd.it", "redd.it")]
    [InlineData("reddit.com.", "reddit.com")]
    [InlineData("tiếngviệt.vn", "xn--tingvit-n7a9b.vn")]
    public void Normalize_cleans_what_people_paste(string raw, string expected) =>
        Assert.Equal(expected, DomainPolicy.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# chú thích")]
    public void Normalize_skips_blank_and_comment_lines(string raw) =>
        Assert.Null(DomainPolicy.Normalize(raw));

    [Theory]
    [InlineData("reddit.com", DomainRejection.None)]
    [InlineData("i.redd.it", DomainRejection.None)]
    [InlineData("a..b.com", DomainRejection.Syntax)]
    [InlineData("-bad.com", DomainRejection.Syntax)]
    [InlineData("bad_.com", DomainRejection.Syntax)]
    [InlineData("1.2.3.4", DomainRejection.Syntax)]
    [InlineData("com", DomainRejection.LocalName)]          // một nhãn
    [InlineData("com.vn", DomainRejection.PublicSuffix)]
    [InlineData("github.io", DomainRejection.PublicSuffix)]
    [InlineData("printer.local", DomainRejection.LocalName)]
    [InlineData("nas.home.arpa", DomainRejection.LocalName)]
    [InlineData("router.lan", DomainRejection.LocalName)]
    [InlineData("dns.google", DomainRejection.Denied)]
    [InlineData("cloudflare-dns.com", DomainRejection.Denied)]
    [InlineData("www.msftconnecttest.com", DomainRejection.Denied)]   // tên miền con của mục cấm
    [InlineData("cdn.xenuine.example.com", DomainRejection.Denied)]   // nhãn chứa "xenuine"
    [InlineData("pubg.com", DomainRejection.Denied)]                  // tên miền cha của accounts.pubg.com
    public void Validate_follows_spec_table(string host, DomainRejection expected) =>
        Assert.Equal(expected, DomainPolicy.Validate(host));

    [Fact]
    public void Validate_rejects_too_long_names()
    {
        var label = new string('a', 64);
        Assert.Equal(DomainRejection.Syntax, DomainPolicy.Validate(label + ".com"));
        var longName = string.Join('.', Enumerable.Repeat(new string('a', 60), 5)) + ".com";
        Assert.Equal(DomainRejection.Syntax, DomainPolicy.Validate(longName));
    }

    [Fact]
    public void Routable_catalog_groups_never_hit_the_policy()
    {
        foreach (var group in DomainCatalog.Selectable)
            foreach (var domain in DomainCatalog.For(group))
                Assert.True(DomainPolicy.IsRoutable(domain), domain);
    }

    [Fact]
    public void Split_input_accepts_newlines_commas_and_spaces() =>
        Assert.Equal(["a.com", "b.com", "c.com", "d.com"], DomainPolicy.SplitInput("a.com\r\nb.com, c.com\t d.com\n\n"));

    [Fact]
    public void Split_input_keeps_comment_lines_out()
    {
        var parts = DomainPolicy.SplitInput("# danh sách\nreddit.com # chính\nredd.it");
        Assert.Equal(["reddit.com", "redd.it"], parts);
    }

    [Fact]
    public void Check_reports_duplicates_in_custom_and_in_enabled_groups()
    {
        string[] custom = ["reddit.com"];
        string[] catalog = ["steampowered.com"];

        Assert.Equal(DomainRejection.Duplicate, DomainPolicy.Check("REDDIT.com", custom, catalog).Rejection);
        var inGroup = DomainPolicy.Check("store.steampowered.com", custom, catalog);
        Assert.Equal(DomainRejection.Duplicate, inGroup.Rejection);
        Assert.Equal("steampowered.com", inGroup.Detail);
        Assert.True(DomainPolicy.Check("redd.it", custom, catalog).Ok);
    }

    [Fact]
    public void Check_enforces_the_limit()
    {
        var full = Enumerable.Range(0, DomainPolicy.MaxCustomDomains).Select(i => $"d{i}.example.org").ToArray();
        Assert.Equal(DomainRejection.Limit, DomainPolicy.Check("new.example.org", full, []).Rejection);
    }

    [Fact]
    public void Check_explains_denied_entries()
    {
        var check = DomainPolicy.Check("dns.google", [], []);
        Assert.Equal(DomainRejection.Denied, check.Rejection);
        Assert.False(string.IsNullOrWhiteSpace(check.Detail));
    }
}
```

Ghi chú cho test IDN: nếu chuỗi punycode kỳ vọng sai, chạy `new System.Globalization.IdnMapping().GetAscii("tiếngviệt.vn")` một lần để lấy giá trị đúng rồi sửa `InlineData`. Đừng sửa code cho khớp một giá trị đoán.

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainPolicyTests`
Expected: lỗi biên dịch.

- [ ] **Step 3: Cài đặt**

```csharp
// src/LunaDns.Core/Domains/DomainDenyList.cs
namespace LunaDns.Core.Domains;

public sealed record DeniedDomain(string Domain, string Reason);

/// <summary>
/// Tên miền LunaDNS không bao giờ định tuyến (spec KH8 4.4). Khớp cả ba chiều: trùng, tên miền con, tên miền cha.
/// Luật DNS luôn gồm tên miền con, nên định tuyến tên cha sẽ kéo theo tên bị cấm.
/// </summary>
public static class DomainDenyList
{
    private const string AntiCheat = "anti-cheat, định tuyến sai có thể bị khoá tài khoản game";
    private const string OwnDoh = "máy chủ DoH của chính LunaDNS, định tuyến sẽ gây vòng lặp";
    private const string Connectivity = "hệ điều hành dùng để kiểm tra mạng và trang đăng nhập Wi-Fi";

    public static IReadOnlyList<DeniedDomain> Domains { get; } =
    [
        new("accounts.pubg.com", AntiCheat),
        new("easyanticheat.net", AntiCheat),
        new("battleye.com", AntiCheat),
        new("cloudflare-dns.com", OwnDoh),
        new("one.one.one.one", OwnDoh),
        new("dns.google", OwnDoh),
        new("msftconnecttest.com", Connectivity),
        new("msftncsi.com", Connectivity),
        new("captive.apple.com", Connectivity),
        new("connectivitycheck.gstatic.com", Connectivity),
    ];

    /// <summary>Nhãn chứa chuỗi này là anti-cheat (spec v1 4.4: <c>*xenuine*</c>).</summary>
    private static readonly string[] LabelFragments = ["xenuine"];

    public static DeniedDomain? Find(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        foreach (var fragment in LabelFragments)
            if (host.Contains(fragment, StringComparison.Ordinal)) return new DeniedDomain(host, AntiCheat);
        foreach (var denied in Domains)
        {
            var d = denied.Domain;
            if (host == d || IsUnder(host, d) || IsUnder(d, host)) return denied;
        }
        return null;
    }

    private static bool IsUnder(string child, string parent) =>
        child.Length > parent.Length && child.EndsWith(parent, StringComparison.Ordinal) && child[child.Length - parent.Length - 1] == '.';
}
```

```csharp
// src/LunaDns.Core/Domains/DomainPolicy.cs
using System.Globalization;
using System.Net;

namespace LunaDns.Core.Domains;

public enum DomainRejection
{
    None,
    Syntax,
    PublicSuffix,
    LocalName,
    Denied,
    Duplicate,
    Limit,
}

/// <param name="Input">Chữ user nhập (để giữ lại khi lỗi).</param>
/// <param name="Domain">Tên đã chuẩn hoá, null nếu dòng trống hoặc chú thích.</param>
/// <param name="Detail">Denied: lý do. Duplicate: tên miền (hoặc tên cha) đã có.</param>
public sealed record DomainCheck(string Input, string? Domain, DomainRejection Rejection, string? Detail = null)
{
    public bool Ok => Rejection == DomainRejection.None && Domain is not null;
}

/// <summary>Chuẩn hoá và kiểm tra tên miền (spec KH8 mục 4). Dùng ở UI, settings, FeatureSet, router và dịch vụ nền macOS.</summary>
public static class DomainPolicy
{
    public const int MaxCustomDomains = 200;

    private static readonly IdnMapping Idn = new();
    private static readonly string[] LocalSuffixes = ["localhost", "local", "lan", "home.arpa", "internal", "arpa", "test", "invalid"];
    private static readonly char[] Separators = ['\n', '\r', ',', ' ', '\t', ';'];

    public static string? Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var s = raw.Trim();
        if (s.Length == 0 || s.StartsWith('#')) return null;
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        var cut = s.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) s = s[..cut];
        var at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        var colon = s.LastIndexOf(':');
        if (colon >= 0 && s.IndexOf(':', StringComparison.Ordinal) == colon) s = s[..colon]; // một dấu ":" = cổng; IPv6 để Validate từ chối
        if (s.StartsWith("*.", StringComparison.Ordinal)) s = s[2..];
        s = DomainMatcher.Normalize(s);
        if (s.StartsWith("www.", StringComparison.Ordinal)) s = s[4..];
        try
        {
            s = Idn.GetAscii(s);
        }
        catch (ArgumentException)
        {
            return s; // nhãn rỗng/ký tự cấm: Validate trả Syntax
        }
        return s.ToLowerInvariant();
    }

    public static DomainRejection Validate(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Length is 0 or > 253) return DomainRejection.Syntax;
        if (IPAddress.TryParse(host, out _)) return DomainRejection.Syntax;
        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-') return DomainRejection.Syntax;
            if (!label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return DomainRejection.Syntax;
        }
        if (labels.Length == 1 || LocalSuffixes.Any(s => host == s || host.EndsWith("." + s, StringComparison.Ordinal)))
            return DomainRejection.LocalName;
        if (PublicSuffixList.Default.IsPublicSuffix(host)) return DomainRejection.PublicSuffix;
        if (DomainDenyList.Find(host) is not null) return DomainRejection.Denied;
        return DomainRejection.None;
    }

    public static bool IsRoutable(string host) => Validate(DomainMatcher.Normalize(host)) == DomainRejection.None;

    /// <summary>Tách chữ dán vào thành từng mục. Bỏ phần sau "#" của mỗi dòng.</summary>
    public static IReadOnlyList<string> SplitInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var items = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            var content = hash >= 0 ? line[..hash] : line;
            items.AddRange(content.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return items;
    }

    public static DomainCheck Check(string raw, IReadOnlyCollection<string> existingCustom, IReadOnlyList<string> enabledCatalog)
    {
        ArgumentNullException.ThrowIfNull(existingCustom);
        ArgumentNullException.ThrowIfNull(enabledCatalog);
        var domain = Normalize(raw);
        if (domain is null) return new DomainCheck(raw, null, DomainRejection.None);
        var rejection = Validate(domain);
        if (rejection == DomainRejection.Denied)
            return new DomainCheck(raw, domain, rejection, DomainDenyList.Find(domain)?.Reason);
        if (rejection != DomainRejection.None) return new DomainCheck(raw, domain, rejection);
        if (existingCustom.Contains(domain, StringComparer.Ordinal))
            return new DomainCheck(raw, domain, DomainRejection.Duplicate, domain);
        var parent = enabledCatalog.FirstOrDefault(c => DomainMatcher.Matches(domain, [c]));
        if (parent is not null) return new DomainCheck(raw, domain, DomainRejection.Duplicate, parent);
        if (existingCustom.Count >= MaxCustomDomains) return new DomainCheck(raw, domain, DomainRejection.Limit);
        return new DomainCheck(raw, domain, DomainRejection.None);
    }
}
```

Lưu ý: `"com"` trả `LocalName` vì chỉ có một nhãn. Đây là điều test kỳ vọng. Câu báo cho `LocalName` ở Task 12 phải đúng cho cả hai trường hợp.

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "DomainPolicyTests|DomainCatalogTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Domains tests/LunaDns.Core.Tests/Domains
git commit -m "feat(core): DomainPolicy chuẩn hoá, kiểm tra và danh sách cấm"
```

---

### Task 5: `DomainSelection` trong settings và `FeatureDomains.Routed(selection)`

**Files:**
- Create: `src/LunaDns.Core/Domains/DomainSelection.cs`
- Modify: `src/LunaDns.Core/Settings/AppSettings.cs`, `src/LunaDns.Core/Settings/SettingsJsonContext.cs`, `src/LunaDns.Core/Settings/JsonFileSettingsStore.cs`
- Modify: `src/LunaDns.Core/Features/FeatureDomains.cs`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainSelectionTests.cs`, `tests/LunaDns.Core.Tests/Settings/JsonFileSettingsStoreTests.cs`, `tests/LunaDns.Core.Tests/Features/FeatureDomainsTests.cs`

**Interfaces:**
- Consumes: `DomainPolicy`, `DomainCatalog`.
- Produces:
  - `sealed record CustomDomain(string Domain, bool Enabled = true)`;
  - `sealed record DomainSelection`:
    - `IReadOnlyList<DomainGroup> EnabledGroups` (mặc định `[Steam, Custom]`), `IReadOnlyList<CustomDomain> Custom` (mặc định `[]`);
    - `bool IsEnabled(DomainGroup)`, `DomainSelection WithGroup(DomainGroup, bool)`;
    - `DomainSelection Sanitized(out int dropped)`;
    - `int? FirstInvalidIndex()`: trả `null` khi hợp lệ, `MaxCustomDomains` khi quá giới hạn;
    - `bool SameAs(DomainSelection other)`;
  - `AppSettings.Domains : DomainSelection`;
  - `JsonFileSettingsStore.DroppedDomains : int`, số dòng bị bỏ ở lần `Load()` gần nhất;
  - `FeatureDomains.Routed(DomainSelection) : IReadOnlyList<string>`. Thuộc tính tĩnh `Routed` cũ bị xoá.

- [ ] **Step 1: Viết test hỏng**

```csharp
// tests/LunaDns.Core.Tests/Domains/DomainSelectionTests.cs
using LunaDns.Core.Domains;
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainSelectionTests
{
    [Fact]
    public void Default_routes_exactly_the_v1_steam_list() =>
        Assert.Equal(DomainCatalog.For(DomainGroup.Steam), FeatureDomains.Routed(new DomainSelection()));

    [Fact]
    public void Routed_merges_groups_and_enabled_custom_entries()
    {
        var selection = new DomainSelection
        {
            EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom],
            Custom = [new("example.org"), new("off.example.net", Enabled: false)],
        };

        var routed = FeatureDomains.Routed(selection);

        Assert.Contains("reddit.com", routed);
        Assert.Contains("example.org", routed);
        Assert.DoesNotContain("off.example.net", routed);
        Assert.DoesNotContain("steampowered.com", routed);
    }

    [Fact]
    public void Routed_ignores_custom_entries_when_custom_group_is_off()
    {
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Steam], Custom = [new("example.org")] };
        Assert.DoesNotContain("example.org", FeatureDomains.Routed(selection));
    }

    [Fact]
    public void Routed_drops_subdomains_already_covered_and_never_includes_pubg()
    {
        var selection = new DomainSelection
        {
            EnabledGroups = [DomainGroup.Steam, DomainGroup.Pubg, DomainGroup.Custom],
            Custom = [new("store.steampowered.com"), new("example.org"), new("a.example.org")],
        };

        var routed = FeatureDomains.Routed(selection);

        Assert.DoesNotContain("store.steampowered.com", routed);
        Assert.DoesNotContain("a.example.org", routed);
        Assert.DoesNotContain("pubg.com", routed);
        Assert.Equal(routed.Count, routed.Distinct().Count());
    }

    [Fact]
    public void Sanitized_drops_invalid_duplicate_and_overflow_entries()
    {
        var custom = new List<CustomDomain> { new("Example.org"), new("example.org"), new("com"), new("dns.google") };
        custom.AddRange(Enumerable.Range(0, 250).Select(i => new CustomDomain($"d{i}.example.net")));
        var selection = new DomainSelection { Custom = custom };

        var clean = selection.Sanitized(out var dropped);

        Assert.Equal(DomainPolicy.MaxCustomDomains, clean.Custom.Count);
        Assert.Equal("example.org", clean.Custom[0].Domain);
        Assert.Equal(custom.Count - DomainPolicy.MaxCustomDomains, dropped);
    }

    [Fact]
    public void First_invalid_index_requires_normalised_valid_unique_entries()
    {
        Assert.Null(new DomainSelection { Custom = [new("example.org")] }.FirstInvalidIndex());
        Assert.Equal(1, new DomainSelection { Custom = [new("example.org"), new("Example.ORG")] }.FirstInvalidIndex());
        Assert.Equal(0, new DomainSelection { Custom = [new("com.vn")] }.FirstInvalidIndex());
        var tooMany = Enumerable.Range(0, 201).Select(i => new CustomDomain($"d{i}.example.org")).ToArray();
        Assert.Equal(DomainPolicy.MaxCustomDomains, new DomainSelection { Custom = tooMany }.FirstInvalidIndex());
    }

    [Fact]
    public void With_group_toggles_without_duplicates()
    {
        var s = new DomainSelection().WithGroup(DomainGroup.Reddit, true).WithGroup(DomainGroup.Reddit, true).WithGroup(DomainGroup.Steam, false);
        Assert.Equal([DomainGroup.Custom, DomainGroup.Reddit], s.EnabledGroups);
    }

    [Fact]
    public void Same_as_compares_content_not_references()
    {
        var a = new DomainSelection { Custom = [new("example.org")] };
        var b = new DomainSelection { Custom = [new("example.org")] };
        Assert.True(a.SameAs(b));
        Assert.False(a.SameAs(b with { Custom = [new("example.org", Enabled: false)] }));
    }
}
```

Thêm vào `tests/LunaDns.Core.Tests/Settings/JsonFileSettingsStoreTests.cs` (dùng đúng helper thư mục tạm mà file đó đang dùng):

```csharp
[Fact]
public void Old_settings_without_domains_load_with_default_selection()
{
    var path = Path.Combine(_dir.Path, "settings.json");
    File.WriteAllText(path, """{ "SplitMode": "FirstByte", "SplitDelayMs": 0 }""");

    var settings = new JsonFileSettingsStore(path).Load();

    Assert.Equal([DomainGroup.Steam, DomainGroup.Custom], settings.Domains.EnabledGroups);
    Assert.Empty(settings.Domains.Custom);
}

[Fact]
public void Domains_round_trip_and_bad_entries_are_dropped_on_load()
{
    var path = Path.Combine(_dir.Path, "settings.json");
    var store = new JsonFileSettingsStore(path);
    store.Save(new AppSettings { Domains = new DomainSelection { EnabledGroups = [DomainGroup.Reddit], Custom = [new("example.org"), new("com")] } });

    var loaded = store.Load();

    Assert.Equal([DomainGroup.Reddit], loaded.Domains.EnabledGroups);
    Assert.Equal([new CustomDomain("example.org")], loaded.Domains.Custom);
    Assert.Equal(1, store.DroppedDomains);
}
```

Nếu file test chưa có trường `_dir`, dùng helper thư mục tạm có sẵn trong file đó (đọc 20 dòng đầu file để biết tên).

Sửa `FeatureDomainsTests.Routed_is_the_steam_group` thành:

```csharp
[Fact]
public void Routed_default_is_the_steam_group() =>
    Assert.Equal(DomainCatalog.For(DomainGroup.Steam), FeatureDomains.Routed(new DomainSelection()));
```

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests`
Expected: lỗi biên dịch (`DomainSelection`, `AppSettings.Domains` không tồn tại).

- [ ] **Step 3: Cài đặt**

```csharp
// src/LunaDns.Core/Domains/DomainSelection.cs
namespace LunaDns.Core.Domains;

public sealed record CustomDomain(string Domain, bool Enabled = true);

/// <summary>Nhóm nào đang bật và danh sách Tuỳ chỉnh (spec KH8 3.1). Bất biến; đổi bằng with/WithGroup.</summary>
public sealed record DomainSelection
{
    public IReadOnlyList<DomainGroup> EnabledGroups { get; init; } = [DomainGroup.Steam, DomainGroup.Custom];

    public IReadOnlyList<CustomDomain> Custom { get; init; } = [];

    public bool IsEnabled(DomainGroup group) => EnabledGroups.Contains(group);

    public DomainSelection WithGroup(DomainGroup group, bool enabled) => this with
    {
        EnabledGroups = enabled
            ? [.. EnabledGroups.Where(g => g != group), group]
            : [.. EnabledGroups.Where(g => g != group)],
    };

    /// <summary>Dùng khi đọc file: bỏ dòng không qua DomainPolicy, dòng trùng và phần vượt giới hạn.</summary>
    public DomainSelection Sanitized(out int dropped)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<CustomDomain>();
        foreach (var entry in Custom)
        {
            var domain = entry.Domain is null ? null : DomainPolicy.Normalize(entry.Domain);
            if (domain is null || DomainPolicy.Validate(domain) != DomainRejection.None || !seen.Add(domain)) continue;
            if (kept.Count == DomainPolicy.MaxCustomDomains) break;
            kept.Add(entry with { Domain = domain });
        }
        dropped = Custom.Count - kept.Count;
        return this with { EnabledGroups = [.. EnabledGroups.Distinct()], Custom = kept };
    }

    /// <summary>Kiểm nghiêm ngặt cho dữ liệu đến từ ngoài (dịch vụ nền macOS): null = hợp lệ.</summary>
    public int? FirstInvalidIndex()
    {
        if (Custom.Count > DomainPolicy.MaxCustomDomains) return DomainPolicy.MaxCustomDomains;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Custom.Count; i++)
        {
            var domain = Custom[i].Domain;
            if (domain is null || DomainPolicy.Normalize(domain) != domain
                || DomainPolicy.Validate(domain) != DomainRejection.None || !seen.Add(domain))
                return i;
        }
        return null;
    }

    public bool SameAs(DomainSelection other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return EnabledGroups.OrderBy(g => g).SequenceEqual(other.EnabledGroups.OrderBy(g => g)) && Custom.SequenceEqual(other.Custom);
    }
}
```

```csharp
// src/LunaDns.Core/Settings/AppSettings.cs — thêm thuộc tính (using LunaDns.Core.Domains;)
public DomainSelection Domains { get; init; } = new();
```

```csharp
// src/LunaDns.Core/Settings/SettingsJsonContext.cs — thêm dòng
[JsonSerializable(typeof(DomainSelection))]
```

```csharp
// src/LunaDns.Core/Settings/JsonFileSettingsStore.cs — Load(): sau khi deserialize
public int DroppedDomains { get; private set; }

public AppSettings Load()
{
    DroppedDomains = 0;
    if (!File.Exists(path)) return new AppSettings();
    try
    {
        using var stream = File.OpenRead(path);
        var loaded = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        var domains = (loaded.Domains ?? new DomainSelection()).Sanitized(out var dropped);
        DroppedDomains = dropped;
        return loaded with { Domains = domains };
    }
    catch (JsonException)
    {
        // giữ nguyên nhánh cũ
    }
}
```

Nhánh `catch` giữ nguyên nội dung hiện có (cất file `.corrupt-…`, trả mặc định). `loaded.Domains ?? …` cần thiết vì JSON có `"Domains": null` sẽ ghi đè giá trị mặc định. Analyzer có thể báo "luôn khác null" (CS8073/IDE0270) thì dùng `#pragma warning disable` kèm chú thích lý do, không bỏ dòng này.

```csharp
// src/LunaDns.Core/Features/FeatureDomains.cs — thay thuộc tính Routed
/// <summary>Tên miền mà Định tuyến DNS (stub + NRPT/resolver) và Proxy SNI (PAC, CONNECT) áp dụng (spec KH8 3.1).</summary>
public static IReadOnlyList<string> Routed(DomainSelection selection)
{
    ArgumentNullException.ThrowIfNull(selection);
    var groups = selection.EnabledGroups.Where(g => g != DomainGroup.Pubg).Distinct();
    var custom = selection.IsEnabled(DomainGroup.Custom)
        ? selection.Custom.Where(c => c.Enabled).Select(c => c.Domain)
        : [];
    var all = groups.SelectMany(DomainCatalog.For).Concat(custom)
        .Select(DomainMatcher.Normalize)
        .Where(DomainPolicy.IsRoutable)
        .Distinct(StringComparer.Ordinal)
        .ToList();
    return [.. all.Where(d => !all.Any(p => p != d && DomainMatcher.Matches(d, [p])))];
}
```

`FeatureSet` đang dùng `FeatureDomains.Routed` dạng thuộc tính. Ở task này tạm sửa thành `FeatureDomains.Routed(new DomainSelection())` cho biên dịch được; Task 7 sẽ thay hẳn.

- [ ] **Step 4: Chạy test**

Run: `dotnet build -warnaserror && dotnet test tests/LunaDns.Core.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core tests/LunaDns.Core.Tests
git commit -m "feat(core): DomainSelection trong settings, FeatureDomains.Routed theo lựa chọn"
```

---

### Task 6: Chốt an toàn ở router Windows và macOS dùng `DomainPolicy`

Spec mục 3.2.

**Files:**
- Modify: `src/LunaDns.Platform.Windows/Nrpt/NrptDnsRouter.cs:26-30` và câu báo `DnsRouteConflict`
- Modify: `src/LunaDns.Platform.MacOS/Resolver/ResolverDnsRouter.cs` (`Plan`, `IsSafeGameDomain`, câu báo `DnsRouteConflict`)
- Modify: `src/LunaDns.Platform.MacOS/Daemon/DaemonCommands.cs` (câu `DnsRouteConflict` nhắc "Steam")
- Test: `tests/LunaDns.Platform.Windows.Tests/Nrpt/NrptDnsRouterTests.cs`, `tests/LunaDns.Platform.MacOS.Tests/ResolverDnsRouterTests.cs`

**Interfaces:**
- Consumes: `DomainPolicy.IsRoutable(string)`.
- Produces: `NrptDnsRouter.Plan` và `ResolverDnsRouter.Plan` nhận mọi tên miền mà `IsRoutable` chấp nhận. Gặp tên không hợp lệ thì vẫn ném `ArgumentException`, nhưng câu báo đổi thành "Có tên miền không hợp lệ để định tuyến."

- [ ] **Step 1: Viết test hỏng (macOS, chạy được trên Mac)**

Thêm vào `ResolverDnsRouterTests.cs` (dùng `TempDirectory` có sẵn trong project test):

```csharp
[Fact]
public void Plan_accepts_custom_routable_domains()
{
    using var dir = new TempDirectory();
    var router = new ResolverDnsRouter(15353, dir.Path);

    var snapshot = router.Plan(["reddit.com", "example.org"], IPAddress.Loopback);

    Assert.Equal(["reddit.com", "example.org"], snapshot.Namespaces);
}

[Theory]
[InlineData("com")]
[InlineData("com.vn")]
[InlineData("printer.local")]
[InlineData("dns.google")]
[InlineData("../etc/passwd")]
public void Plan_rejects_non_routable_domains(string domain)
{
    using var dir = new TempDirectory();
    var router = new ResolverDnsRouter(15353, dir.Path);

    Assert.Throws<ArgumentException>(() => router.Plan([domain], IPAddress.Loopback));
}
```

Nếu `TempDirectory` có tên thuộc tính khác `Path`, dùng đúng tên trong `tests/LunaDns.Platform.MacOS.Tests/TempDirectory.cs`. Thêm hai test tương tự vào `NrptDnsRouterTests.cs`, dùng fake `INrptStore` mà file đó đang dùng; kỳ vọng namespace `[".reddit.com", "reddit.com", ".example.org", "example.org"]`.

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests --filter ResolverDnsRouterTests`
Expected: `Plan_accepts_custom_routable_domains` FAIL với `ArgumentException` "Có domain không thuộc danh sách domain game."

- [ ] **Step 3: Cài đặt**

`NrptDnsRouter.Plan`:

```csharp
var host = DomainMatcher.Normalize(domain);
if (!DomainPolicy.IsRoutable(host))
    throw new ArgumentException("Có tên miền không hợp lệ để định tuyến.", nameof(domains));
```

Câu `DnsRouteConflict` của NRPT đổi thành: "Các tên miền đã chọn đều có luật DNS của phần mềm khác (ví dụ VPN). LunaDNS không sửa luật đó nên không bật được Định tuyến DNS."

`ResolverDnsRouter`: đổi tên `IsSafeGameDomain` thành `IsSafeDomain`. Kiểm tra ký tự giữ nguyên, vì tên miền là tên file:

```csharp
/// <summary>Tên file lấy từ journal trên đĩa: chỉ tên miền định tuyến được (DomainPolicy), không bao giờ có "/" hay "..".</summary>
private static bool IsSafeDomain(string host) =>
    host.Length > 0
    && host.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.')
    && !host.StartsWith('.') && !host.Contains("..", StringComparison.Ordinal)
    && DomainPolicy.IsRoutable(host);
```

Sửa mọi chỗ gọi `IsSafeGameDomain`, câu `ArgumentException` như bên NRPT, và câu `DnsRouteConflict`: "Các tên miền đã chọn đều có file /etc/resolver của phần mềm khác (VPN hoặc script tự tạo). LunaDNS không sửa file đó nên không bật được Định tuyến DNS."

`DaemonCommands.Describe`, nhánh `DnsRouteConflict`: "Các tên miền đã chọn đã có file /etc/resolver của phần mềm khác (VPN hoặc script tự tạo). Gỡ file đó rồi thử lại."

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests` (Mac) · `dotnet test tests/LunaDns.Platform.Windows.Tests` (Windows)
Expected: PASS. Test cũ nào kiểm câu báo "domain game" thì sửa theo câu mới.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Platform.Windows src/LunaDns.Platform.MacOS tests/LunaDns.Platform.Windows.Tests tests/LunaDns.Platform.MacOS.Tests
git commit -m "feat(platform): router NRPT và /etc/resolver nhận tên miền qua DomainPolicy"
```

---

### Task 7: `FeatureSet` giữ danh sách định tuyến và có `ReapplyAsync`

Spec mục 3.1, 5.1, 7.

**Files:**
- Modify: `src/LunaDns.Core/Features/FeatureSet.cs`
- Modify: `src/LunaDns.Core/Features/FeatureError.cs`
- Create: `src/LunaDns.Core/Features/ReapplyResult.cs`
- Test: `tests/LunaDns.Core.Tests/Features/FeatureSetReapplyTests.cs`

**Interfaces:**
- Consumes: `FeatureDomains.Routed(DomainSelection)`.
- Produces:
  - `FeatureError.NoDomains`, thêm ngay trước `Other`;
  - `sealed record ReapplyResult(bool Success, bool PreviousRestored, FeatureError Error = FeatureError.None)`;
  - constructor `FeatureSet(..., ILogger<FeatureSet>? logger = null, IReadOnlyList<string>? routedDomains = null)`, tham số cuối, mặc định `FeatureDomains.Routed(new DomainSelection())`;
  - `IReadOnlyList<string> FeatureSet.RoutedDomains`;
  - `Task<ReapplyResult> FeatureSet.ReapplyAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken = default)`.
  - `EnableAsync` cho DNS/SNI trả `FeatureOutcome.Failed` + `FeatureError.NoDomains` khi danh sách rỗng.

- [ ] **Step 1: Viết test hỏng**

```csharp
// tests/LunaDns.Core.Tests/Features/FeatureSetReapplyTests.cs
using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Tests.Journal;
using LunaDns.Core.Tests.Platform;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureSetReapplyTests : IAsyncLifetime
{
    private static readonly string[] Steam = ["steampowered.com"];
    private static readonly string[] Reddit = ["reddit.com"];

    private readonly FakeConfigurator _configurator = new();
    private readonly FakeFeatureServices _services = new();
    private readonly ChangeJournal _journal;
    private readonly FeatureSet _features;

    public FeatureSetReapplyTests()
    {
        _journal = new ChangeJournal(new InMemoryJournalStore(), SystemChanges.Reverters(_configurator));
        _features = new FeatureSet(_journal, new SystemChanges(_journal, _configurator), _services, routedDomains: Steam);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _features.DisposeAsync();

    [Fact]
    public async Task Reapply_with_nothing_running_only_swaps_the_list()
    {
        var result = await _features.ReapplyAsync(Reddit);

        Assert.True(result.Success);
        Assert.Equal(Reddit, _features.RoutedDomains);
        Assert.Equal(0, _services.Starts(Feature.DnsRouting));
    }

    [Fact]
    public async Task Reapply_restarts_running_dns_and_proxy_with_new_list()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        await _features.EnableAsync(Feature.SniProxy);

        var result = await _features.ReapplyAsync(Reddit);

        Assert.True(result.Success);
        Assert.Equal(2, _services.Starts(Feature.DnsRouting));
        Assert.Equal(2, _services.Starts(Feature.SniProxy));
        Assert.Equal(Reddit, _services.LastDomains);
        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.True(_features.IsEnabled(Feature.SniProxy));
        Assert.False(_services.Latest(Feature.DnsRouting).Disposed);
        Assert.True(_services.All(Feature.DnsRouting)[0].Disposed);
    }

    [Fact]
    public async Task Reapply_failure_restores_previous_list_and_features()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _services.FailStart(Feature.DnsRouting, new DnsStubException("giả lập cổng bận"));

        var result = await _features.ReapplyAsync(Reddit);

        Assert.False(result.Success);
        Assert.Equal(Steam, _features.RoutedDomains);
    }

    [Fact]
    public async Task Reapply_with_empty_list_is_refused_while_a_feature_runs()
    {
        await _features.EnableAsync(Feature.DnsRouting);

        var result = await _features.ReapplyAsync([]);

        Assert.False(result.Success);
        Assert.Equal(FeatureError.NoDomains, result.Error);
        Assert.Equal(Steam, _features.RoutedDomains);
        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
    }

    [Fact]
    public async Task Enable_with_empty_list_fails_with_no_domains()
    {
        await _features.ReapplyAsync([]);

        var result = await _features.EnableAsync(Feature.SniProxy);

        Assert.Equal(FeatureOutcome.Failed, result.Outcome);
        Assert.Equal(FeatureError.NoDomains, result.Error);
    }
}
```

Trước khi chạy:
- đọc `FakeFeatureServices.FailStart` để biết lỗi lần sau sẽ áp cho mọi lần khởi động hay chỉ một lần;
- `Reapply_failure_restores_previous_list_and_features` cần lỗi **chỉ ở lần khởi động đầu** sau khi gọi. Nếu `FailStart` áp mãi, thêm một tham số `once: true` vào fake; đó là sửa test helper, không phải code sản phẩm;
- kiểm tên kiểu `DnsStubException` và constructor của nó (`src/LunaDns.Core/Dns/`).

Thêm vào test trên khẳng định: khi `FailStart` chỉ áp một lần thì `Assert.True(result.PreviousRestored)` và `Assert.True(_features.IsEnabled(Feature.DnsRouting))`.

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FeatureSetReapplyTests`
Expected: lỗi biên dịch (`routedDomains`, `ReapplyAsync` không tồn tại).

- [ ] **Step 3: Cài đặt**

```csharp
// src/LunaDns.Core/Features/ReapplyResult.cs
namespace LunaDns.Core.Features;

/// <param name="Success">Danh sách mới đang được áp dụng.</param>
/// <param name="PreviousRestored">Khi Success = false: các tính năng đã chạy lại được với danh sách cũ.</param>
public sealed record ReapplyResult(bool Success, bool PreviousRestored, FeatureError Error = FeatureError.None);
```

`FeatureError.cs`, thêm trước `Other`:

```csharp
/// <summary>Danh sách tên miền định tuyến rỗng (spec KH8 5.1).</summary>
NoDomains,
```

`FeatureSet.cs`:

```csharp
private static readonly Feature[] RoutedFeatures = [Feature.DnsRouting, Feature.SniProxy];
private volatile IReadOnlyList<string> _routed;

// constructor: thêm tham số cuối và gán
IReadOnlyList<string>? routedDomains = null)
{
    // … các gán hiện có …
    _routed = routedDomains ?? FeatureDomains.Routed(new DomainSelection());
}

public IReadOnlyList<string> RoutedDomains => _routed;

public async Task<FeatureResult> EnableAsync(Feature feature, bool allowProxyOverride = false, CancellationToken cancellationToken = default)
{
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        return await EnableCoreAsync(feature, allowProxyOverride).ConfigureAwait(false);
    }
    finally
    {
        _gate.Release();
    }
}

/// <summary>Áp danh sách tên miền mới cho DNS/SNI đang chạy (spec KH8 3.1). Lỗi thì quay về danh sách cũ.</summary>
public async Task<ReapplyResult> ReapplyAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(domains);
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        var active = RoutedFeatures.Where(_running.ContainsKey).ToList();
        if (domains.Count == 0 && active.Count > 0) return new ReapplyResult(false, true, FeatureError.NoDomains);
        var previous = _routed;
        _routed = domains;
        var failure = await RestartAsync(active).ConfigureAwait(false);
        if (failure is null) return new ReapplyResult(true, false);

        LogReapplyFailed(_logger, failure.Feature, failure.Error);
        _routed = previous;
        var restoreFailure = await RestartAsync(active).ConfigureAwait(false);
        return new ReapplyResult(false, restoreFailure is null, failure.Error);
    }
    finally
    {
        _gate.Release();
    }
}

/// <summary>Tắt rồi bật lại từng tính năng; trả kết quả lỗi đầu tiên, null nếu ổn. Người dùng đã đồng ý ghi đè proxy khi bật lần đầu.</summary>
private async Task<FeatureResult?> RestartAsync(IReadOnlyList<Feature> features)
{
    foreach (var feature in features)
    {
        if (_running.ContainsKey(feature))
        {
            var off = await DisableCoreAsync(feature).ConfigureAwait(false);
            if (off.Outcome != FeatureOutcome.Disabled) return off;
        }
        var on = await EnableCoreAsync(feature, allowProxyOverride: true).ConfigureAwait(false);
        if (on.Outcome != FeatureOutcome.Enabled) return on;
    }
    return null;
}

[LoggerMessage(Level = LogLevel.Warning, Message = "Áp danh sách tên miền mới lỗi ở {Feature} ({Error}); quay về danh sách cũ.")]
private static partial void LogReapplyFailed(ILogger logger, Feature feature, FeatureError error);
```

Chuyển **nguyên văn** thân `EnableAsync` cũ (phần trong `try`, từ `if (_running.ContainsKey(feature))` tới `return new(feature, FeatureOutcome.Enabled);`) sang `private async Task<FeatureResult> EnableCoreAsync(Feature feature, bool allowProxyOverride)`. Thêm ở đầu:

```csharp
if (_running.ContainsKey(feature)) return new(feature, FeatureOutcome.Unchanged);
if (feature != Feature.PubgMode && _routed.Count == 0) return new(feature, FeatureOutcome.Failed, FeatureError.NoDomains);
```

`StartService` và `ApplySystem`: thay `FeatureDomains.Routed` (hoặc `FeatureDomains.Routed(new DomainSelection())` tạm của Task 5) bằng `_routed`.

`FeatureResult` có thuộc tính `Feature` và `Error`: kiểm tên thật trong `src/LunaDns.Core/Features/FeatureResult.cs`. Nếu `DisableCoreAsync` trả outcome khác `Disabled` cho trường hợp thành công thì sửa điều kiện so sánh theo đúng giá trị đó.

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Core.Tests`
Expected: PASS toàn bộ, gồm `FeatureSetTests` và `FeatureSetWatchdogTests` cũ.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core tests/LunaDns.Core.Tests
git commit -m "feat(core): FeatureSet.ReapplyAsync áp danh sách tên miền mới, lỗi thì quay về"
```

---

### Task 8: App Windows: controller, `AppServices.ApplyDomainsAsync`, câu báo lỗi

**Files:**
- Create: `src/LunaDns.App/Features/DomainApplyResult.cs`
- Modify: `src/LunaDns.App/Features/IFeatureController.cs`, `src/LunaDns.App/Features/LocalFeatureController.cs`, `src/LunaDns.App/AppServices.cs`, `src/LunaDns.App/ErrorMessages.cs`, `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs`
- Modify (tạm): `src/LunaDns.App/MacOS/DaemonFeatureController.cs`. Tạm cài hai hàm mới trả "chưa hỗ trợ"; Task 9 làm thật.
- Test: `tests/LunaDns.App.Tests/AppServicesTests.cs`, `tests/LunaDns.App.Tests/ErrorMessagesTests.cs`

**Interfaces:**
- Consumes: `FeatureSet.ReapplyAsync`, `FeatureDomains.Routed`, `AppSettings.Domains`.
- Produces:
  - `sealed record DomainApplyResult(bool Success, string? Message)`;
  - `IFeatureController.GetDomainsAsync() : Task<DomainSelection?>`;
  - `IFeatureController.ApplyDomainsAsync(DomainSelection) : Task<DomainApplyResult>`;
  - `AppServices.GetDomainsAsync() : Task<DomainSelection?>`;
  - `AppServices.ApplyDomainsAsync(DomainSelection) : Task<DomainApplyResult>`: lưu `settings.json` khi controller là `Local` và áp thành công;
  - `ErrorMessages.For(FeatureError) : string`, cho `NoDomains` và các lỗi khác.

- [ ] **Step 1: Viết test hỏng**

Thêm vào `tests/LunaDns.App.Tests/AppServicesTests.cs`. Dựng `AppServices` bản tại chỗ bằng `FakeNetworkConfigurator` và `TempDirectory` như các test hiện có trong file:

```csharp
[Fact]
public async Task Apply_domains_saves_settings_when_local()
{
    await using var services = CreateLocal();   // helper sẵn có trong file; nếu chưa có, dựng như test đầu file
    var selection = new DomainSelection { EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom], Custom = [new("example.org")] };

    var result = await services.ApplyDomainsAsync(selection);

    Assert.True(result.Success);
    Assert.True(services.Settings.Domains.SameAs(selection));
    Assert.True(new JsonFileSettingsStore(services.Paths.SettingsFile).Load().Domains.SameAs(selection));
    Assert.True((await services.GetDomainsAsync())!.SameAs(selection));
}

[Fact]
public async Task Apply_domains_failure_keeps_old_settings()
{
    await using var services = CreateLocal();
    await services.Features.EnableAsync(Feature.DnsRouting);
    var before = services.Settings.Domains;

    var result = await services.ApplyDomainsAsync(new DomainSelection { EnabledGroups = [] });

    Assert.False(result.Success);
    Assert.Equal(ErrorMessages.For(FeatureError.NoDomains), result.Message);
    Assert.True(services.Settings.Domains.SameAs(before));
}
```

Thêm vào `ErrorMessagesTests.cs`:

```csharp
[Fact]
public void No_domains_has_a_vietnamese_message() =>
    Assert.Contains("Chưa có tên miền", ErrorMessages.For(FeatureError.NoDomains), StringComparison.Ordinal);
```

Nếu `ErrorMessagesTests` đã có test duyệt mọi giá trị `FeatureError`, giá trị mới sẽ tự được kiểm.

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.App.Tests` (Windows; trên Mac thì chạy được phần build `net8.0`)
Expected: lỗi biên dịch.

- [ ] **Step 3: Cài đặt**

```csharp
// src/LunaDns.App/Features/DomainApplyResult.cs
namespace LunaDns.App.Features;

/// <summary>Kết quả của nút Lưu/công tắc nhóm (spec KH8 5.2). Message là câu tiếng Việt khi Success = false.</summary>
internal sealed record DomainApplyResult(bool Success, string? Message);
```

`IFeatureController`, thêm:

```csharp
/// <summary>Lựa chọn tên miền nơi giữ thật đang dùng. null nếu không hỏi được (dịch vụ nền không trả lời).</summary>
Task<DomainSelection?> GetDomainsAsync();

/// <summary>Áp danh sách mới cho DNS/SNI đang chạy. Bản daemon tự lưu; bản Local để AppServices lưu settings.json.</summary>
Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection);
```

`LocalFeatureController`, trong constructor truyền `routedDomains: FeatureDomains.Routed(settings().Domains)` vào `new FeatureSet(...)`, giữ `_settings = settings`:

```csharp
public Task<DomainSelection?> GetDomainsAsync() => Task.FromResult<DomainSelection?>(_settings().Domains);

public async Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection)
{
    ArgumentNullException.ThrowIfNull(selection);
    var result = await Features.ReapplyAsync(FeatureDomains.Routed(selection)).ConfigureAwait(false);
    return result.Success ? new(true, null) : new(false, ErrorMessages.For(result.Error));
}
```

`AppServices`:

```csharp
public Task<DomainSelection?> GetDomainsAsync() => Features.GetDomainsAsync();

/// <summary>Áp rồi mới lưu (spec KH8 3.1): settings.json chỉ ghi khi máy đã chạy theo danh sách mới.</summary>
public async Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection)
{
    ArgumentNullException.ThrowIfNull(selection);
    var result = await Features.ApplyDomainsAsync(selection).ConfigureAwait(false);
    if (result.Success && Features.Availability == ControllerAvailability.Local)
        Settings = Settings with { Domains = selection };
    return result;
}
```

Sau `_settings = _settingsStore.Load();` trong constructor `AppServices`, thêm:

```csharp
if (_settingsStore.DroppedDomains > 0) LogDroppedDomains(_logger, _settingsStore.DroppedDomains);
```

`LogDroppedDomains` là `[LoggerMessage(Level = Warning, Message = "settings.json có {Count} tên miền tuỳ chỉnh không hợp lệ, đã bỏ qua.")]`. Nếu `AppServices` chưa có `_logger` thì tạo từ `loggerFactory.CreateLogger<AppServices>()`; muốn dùng `LoggerMessage` thì lớp phải là `partial`.

`ErrorMessages`, thêm overload `public static string For(FeatureError error)`:
- trả `Strings.ErrorNoDomains` cho `NoDomains`;
- các giá trị khác thì tái dùng nhánh mà `For(FeatureResult)` đang dùng. Nếu nhánh đó nằm trong `For(FeatureResult)` thì tách ra hàm riêng rồi để `For(FeatureResult)` gọi lại, không chép logic.

`Strings.resx` thêm khoá `ErrorNoDomains` = "Chưa có tên miền nào được chọn. Bật ít nhất một nhóm, hoặc tắt Định tuyến DNS và Proxy SNI trước." và thêm dòng `public static string ErrorNoDomains => Get(nameof(ErrorNoDomains));` vào `Strings.cs`.

`DaemonFeatureController` (tạm, Task 9 thay):

```csharp
public Task<DomainSelection?> GetDomainsAsync() => Task.FromResult<DomainSelection?>(null);

public Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection) =>
    Task.FromResult(new DomainApplyResult(false, Strings.DaemonNeedsUpdate));
```

`DaemonNeedsUpdate` = "Dịch vụ nền cần được cài lại để dùng danh sách tên miền. Bấm Cài dịch vụ nền." (thêm vào `Strings.resx` và `Strings.cs`).

Các fake `IFeatureController` trong `tests/LunaDns.App.Tests` (tìm bằng `grep -rn "IFeatureController" tests`) phải cài hai hàm mới. Fake trả `null` và `new DomainApplyResult(true, null)` là đủ.

- [ ] **Step 4: Chạy test**

Run: `dotnet build -warnaserror && dotnet test tests/LunaDns.App.Tests && dotnet test tests/LunaDns.Core.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests
git commit -m "feat(app): áp và lưu danh sách tên miền qua IFeatureController"
```

---

### Task 9: macOS: dịch vụ nền giữ settings, lệnh `get-domains`/`set-domains`

Spec mục 3.3, 7.

**Files:**
- Modify: `src/LunaDns.MacCli/MacPaths.cs`, `src/LunaDns.MacCli/Engine.cs`
- Modify: `src/LunaDns.Platform.MacOS/Daemon/DaemonProtocol.cs`, `src/LunaDns.Platform.MacOS/Daemon/DaemonCommands.cs`
- Modify: nơi tạo `new DaemonCommands(` (tìm bằng `grep -rn "new DaemonCommands" src tests`)
- Modify: `src/LunaDns.App/MacOS/DaemonFeatureController.cs`
- Test: `tests/LunaDns.Platform.MacOS.Tests/DaemonTests.cs`, `tests/LunaDns.App.Tests/MacOS/` (file test của `DaemonFeatureController` hiện có)

**Interfaces:**
- Consumes: `DomainSelection.FirstInvalidIndex()`, `FeatureSet.ReapplyAsync`, `FeatureDomains.Routed`, `JsonFileSettingsStore`.
- Produces:
  - `DaemonProtocol.GetDomains = "get-domains"`, `DaemonProtocol.SetDomains = "set-domains"`, `MaxLineBytes = 65536`;
  - `DaemonRequest(..., DomainSelection? Domains = null)`, `DaemonResponse(..., DomainSelection? Domains = null)`;
  - constructor `DaemonCommands(FeatureSet features, ChangeJournal journal, string version, JsonFileSettingsStore settings)`;
  - `MacPaths.SettingsFile`.

- [ ] **Step 1: Viết test hỏng**

Thêm vào `DaemonTests.cs`, dựng `FeatureSet` bằng fake như các test hiện có trong file:

```csharp
[Fact]
public async Task Set_domains_validates_applies_and_persists()
{
    using var dir = new TempDirectory();
    var store = new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json"));
    var commands = CreateCommands(store);   // helper: new DaemonCommands(features, journal, "test", store)
    var wanted = new DomainSelection { EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom], Custom = [new("example.org")] };

    var response = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.SetDomains, Domains: wanted), CancellationToken.None);

    Assert.True(response.Ok);
    Assert.True(store.Load().Domains.SameAs(wanted));
    var read = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.GetDomains), CancellationToken.None);
    Assert.True(read.Domains!.SameAs(wanted));
}

[Theory]
[InlineData("com.vn")]
[InlineData("Example.org")]      // chưa chuẩn hoá: daemon không tự sửa dữ liệu từ app
[InlineData("../etc/passwd")]
public async Task Set_domains_rejects_invalid_entries_without_touching_anything(string bad)
{
    using var dir = new TempDirectory();
    var store = new JsonFileSettingsStore(Path.Combine(dir.Path, "settings.json"));
    var commands = CreateCommands(store);

    var response = await commands.HandleAsync(
        new DaemonRequest(DaemonProtocol.SetDomains, Domains: new DomainSelection { Custom = [new("ok.example.org"), new(bad)] }),
        CancellationToken.None);

    Assert.False(response.Ok);
    Assert.Contains("dòng 2", response.Message, StringComparison.Ordinal);
    Assert.False(File.Exists(Path.Combine(dir.Path, "settings.json")));
}

[Fact]
public void Protocol_round_trips_domain_selection_as_json()
{
    var request = new DaemonRequest(DaemonProtocol.SetDomains, Domains: new DomainSelection { EnabledGroups = [DomainGroup.Reddit] });
    var json = JsonSerializer.Serialize(request, DaemonProtocol.Json);

    Assert.Contains("\"reddit\"", json, StringComparison.OrdinalIgnoreCase);
    var back = JsonSerializer.Deserialize<DaemonRequest>(json, DaemonProtocol.Json)!;
    Assert.Equal([DomainGroup.Reddit], back.Domains!.EnabledGroups);
}
```

Thêm vào test của `DaemonFeatureController` (dùng fake `IDaemonTransport` có sẵn):

```csharp
[Fact]
public async Task Apply_domains_sends_set_domains_and_maps_old_daemon_reply()
{
    var transport = new FakeDaemonTransport { Reply = r => new DaemonResponse(false, $"Lệnh lạ: {r.Command}") };
    var controller = CreateController(transport);

    var result = await controller.ApplyDomainsAsync(new DomainSelection());

    Assert.Equal(DaemonProtocol.SetDomains, transport.LastRequest!.Command);
    Assert.False(result.Success);
    Assert.Equal(Strings.DaemonNeedsUpdate, result.Message);
}
```

Tên fake (`FakeDaemonTransport`, `Reply`, `LastRequest`, `CreateController`) lấy theo file test hiện có trong `tests/LunaDns.App.Tests/MacOS/`. Nếu fake chưa có thuộc tính nào trong số đó thì thêm vào fake.

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests --filter DaemonTests`
Expected: lỗi biên dịch.

- [ ] **Step 3: Cài đặt**

`DaemonProtocol.cs`:

```csharp
public const int MaxLineBytes = 65536; // set-domains: 200 tên miền × tối đa 253 ký tự

public const string GetDomains = "get-domains";
public const string SetDomains = "set-domains";

public static JsonSerializerOptions Json { get; } = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
};

public sealed record DaemonRequest(string Command, string? Feature = null, bool AllowProxyOverride = false, DomainSelection? Domains = null);

public sealed record DaemonResponse(
    bool Ok,
    string? Message = null,
    string? Version = null,
    DaemonStatus? Status = null,
    string? Outcome = null,
    string? Error = null,
    DomainSelection? Domains = null);
```

Sửa chú thích đầu file để liệt kê thêm hai lệnh mới.

`DaemonCommands.cs`: đổi constructor, thêm trường settings và hai nhánh `case`:

```csharp
public sealed class DaemonCommands(FeatureSet features, ChangeJournal journal, string version, JsonFileSettingsStore settingsStore)
{
    private AppSettings _settings = settingsStore.Load();

    // trong switch:
    case DaemonProtocol.GetDomains:
        return Ok() with { Domains = _settings.Domains };
    case DaemonProtocol.SetDomains:
        return await SetDomainsAsync(request.Domains, cancellationToken).ConfigureAwait(false);

    /// <summary>Không tin dữ liệu từ giao diện (quyền user): kiểm lại toàn bộ, áp, rồi mới ghi file (spec KH8 3.3).</summary>
    private async Task<DaemonResponse> SetDomainsAsync(DomainSelection? wanted, CancellationToken cancellationToken)
    {
        if (wanted is null) return new DaemonResponse(false, "Thiếu danh sách tên miền.");
        if (wanted.FirstInvalidIndex() is { } bad)
            return new DaemonResponse(false, bad >= DomainPolicy.MaxCustomDomains
                ? $"Tối đa {DomainPolicy.MaxCustomDomains} tên miền."
                : $"Tên miền không hợp lệ ở dòng {bad + 1}.");
        var result = await features.ReapplyAsync(FeatureDomains.Routed(wanted), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return Ok() with { Ok = false, Error = result.Error.ToString(), Message = DescribeReapply(result) };
        _settings = _settings with { Domains = wanted };
        settingsStore.Save(_settings);
        return Ok() with { Domains = wanted };
    }

    private static string DescribeReapply(ReapplyResult result) => result.Error switch
    {
        FeatureError.NoDomains => "Chưa có tên miền nào được chọn. Bật ít nhất một nhóm, hoặc tắt Định tuyến DNS và Proxy SNI trước.",
        _ when result.PreviousRestored => $"Không áp được danh sách mới ({result.Error}); đã giữ danh sách cũ.",
        _ => $"Không áp được danh sách mới ({result.Error}) và không khôi phục được; chạy: sudo lunadns cleanup",
    };
}
```

`MacPaths.cs`: `public string SettingsFile => Path.Combine(Root, "settings.json");`

`Engine.cs`: thay `var settings = new AppSettings();` bằng:

```csharp
SettingsStore = new JsonFileSettingsStore(paths.SettingsFile);
var settings = SettingsStore.Load();
```

Truyền `routedDomains: FeatureDomains.Routed(settings.Domains)` vào `new FeatureSet(...)` và thêm thuộc tính `public JsonFileSettingsStore SettingsStore { get; }`. Chỗ tạo `DaemonCommands` truyền `engine.SettingsStore`. File nằm trong `/Library/Application Support/LunaDNS`, thư mục `EnsureCreated` đã khoá cho root, nên không cần đổi quyền.

`DaemonFeatureController`, thay hai hàm tạm của Task 8:

```csharp
public async Task<DomainSelection?> GetDomainsAsync() =>
    (await SendAsync(new DaemonRequest(DaemonProtocol.GetDomains)).ConfigureAwait(false))?.Domains;

public async Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection)
{
    ArgumentNullException.ThrowIfNull(selection);
    var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetDomains, Domains: selection)).ConfigureAwait(false);
    if (response is null) return new(false, ErrorMessages.For(FeatureError.ServiceUnavailable));
    if (response.Ok) return new(true, null);
    if (response.Message?.StartsWith("Lệnh lạ:", StringComparison.Ordinal) == true) return new(false, Strings.DaemonNeedsUpdate);
    return new(false, response.Message);
}
```

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests && dotnet test tests/LunaDns.App.Tests --filter Daemon`
Expected: PASS.

- [ ] **Step 5: Thử tay trên Mac** (cần quyền admin; dùng `build/run-macos.sh` hoặc cách đang dùng để chạy dịch vụ nền bản dev)

```bash
echo '{"command":"set-domains","domains":{"enabledGroups":["steam","reddit"],"custom":[]}}' | nc -U /var/run/lunadns.sock
ls /etc/resolver | grep -c reddit      # sau khi bật Định tuyến DNS: >0
sudo cat "/Library/Application Support/LunaDNS/settings.json"
```

Expected: trả `"ok":true`; file settings có `"Domains"`.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.MacCli src/LunaDns.Platform.MacOS src/LunaDns.App/MacOS tests
git commit -m "feat(macos): dịch vụ nền giữ danh sách tên miền, lệnh get-domains/set-domains"
```

---

### Task 10: Kiểm tra theo nhóm và kết luận cho từng tên miền

Spec mục 5.1 (kiểm tra theo nhóm) và 5.3.

**Files:**
- Modify: `src/LunaDns.Core/Probing/ProbeTargets.cs`, `src/LunaDns.Core/Features/ProbeSummary.cs`
- Modify: `src/LunaDns.App/ProbeRunner.cs`, `src/LunaDns.App/AppServices.cs`
- Create: `src/LunaDns.App/DomainProbeText.cs`
- Modify: `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs`
- Test: `tests/LunaDns.Core.Tests/Probing/ProbeTargetsTests.cs` (tạo nếu chưa có), `tests/LunaDns.Core.Tests/Features/ProbeSummaryTests.cs`, `tests/LunaDns.App.Tests/DomainProbeTextTests.cs`

**Interfaces:**
- Consumes: `DomainSelection`, `Prober.RunAsync(IEnumerable<string>, CancellationToken)`.
- Produces:
  - `ProbeTargets.For(DomainSelection) : IReadOnlyList<string>`: lấy host mẫu của nhóm bật, cộng tối đa 5 dòng Tuỳ chỉnh đang bật;
  - `ProbeTargets.GroupOf(string host, DomainSelection) : DomainGroup`;
  - `ProbeSummary.Create(IReadOnlyList<ProbeResult>, DateTimeOffset, Func<string, DomainGroup> groupOf)`. Overload cũ hai tham số giữ nguyên hành vi;
  - `ProbeRunner.RunAsync(IEnumerable<string> hosts, CancellationToken)`, `ProbeRunner.RunAsync(DomainSelection, CancellationToken)`;
  - `AppServices.ProbeDomainsAsync(IReadOnlyList<string> hosts, CancellationToken) : Task<IReadOnlyList<ProbeResult>>`;
  - `static class DomainProbeText`: `string For(ProbeVerdict)`, `string Failed`, `Tone ToneFor(ProbeVerdict)`.

- [ ] **Step 1: Viết test hỏng**

```csharp
// tests/LunaDns.Core.Tests/Probing/ProbeTargetsTests.cs
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

public sealed class ProbeTargetsTests
{
    [Fact]
    public void Default_selection_probes_only_steam() =>
        Assert.Equal(ProbeTargets.Steam, ProbeTargets.For(new DomainSelection()));

    [Fact]
    public void Selection_adds_group_targets_and_first_five_enabled_custom_domains()
    {
        var custom = Enumerable.Range(0, 7).Select(i => new CustomDomain($"d{i}.example.org", Enabled: i != 0)).ToArray();
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom], Custom = custom };

        var targets = ProbeTargets.For(selection);

        Assert.Equal([.. ProbeTargets.For(DomainGroup.Reddit), "d1.example.org", "d2.example.org", "d3.example.org", "d4.example.org", "d5.example.org"], targets);
    }

    [Fact]
    public void Group_of_maps_hosts_back_to_their_group()
    {
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Steam, DomainGroup.Reddit, DomainGroup.Custom], Custom = [new("example.org")] };

        Assert.Equal(DomainGroup.Steam, ProbeTargets.GroupOf("store.steampowered.com", selection));
        Assert.Equal(DomainGroup.Reddit, ProbeTargets.GroupOf("i.redd.it", selection));
        Assert.Equal(DomainGroup.Custom, ProbeTargets.GroupOf("example.org", selection));
    }
}
```

Thêm vào `ProbeSummaryTests.cs`, dựng `ProbeResult` bằng helper sẵn có trong file:

```csharp
[Fact]
public void Create_with_group_map_keeps_groups_in_first_seen_order()
{
    var results = new[] { Result("store.steampowered.com", ProbeVerdict.Ok), Result("www.reddit.com", ProbeVerdict.SniFiltered) };

    var summary = ProbeSummary.Create(results, DateTimeOffset.UnixEpoch,
        host => host.Contains("reddit", StringComparison.Ordinal) ? DomainGroup.Reddit : DomainGroup.Steam);

    Assert.Equal([DomainGroup.Steam, DomainGroup.Reddit], summary.Groups.Select(g => g.Group));
    Assert.Equal(ProbeVerdict.SniFiltered, summary.Groups[1].Worst);
}
```

```csharp
// tests/LunaDns.App.Tests/DomainProbeTextTests.cs
using LunaDns.App;
using LunaDns.Core.Probing;

namespace LunaDns.App.Tests;

public sealed class DomainProbeTextTests
{
    [Theory]
    [InlineData(ProbeVerdict.Ok, "Không bị chặn, không cần LunaDNS")]
    [InlineData(ProbeVerdict.DnsPoisoned, "Giúp được (cần Định tuyến DNS)")]
    [InlineData(ProbeVerdict.SniFiltered, "Giúp được (cần Proxy SNI)")]
    [InlineData(ProbeVerdict.Unreachable, "Chặn theo IP hoặc cách khác, LunaDNS không giúp được")]
    public void Verdict_text_matches_spec(ProbeVerdict verdict, string expected) =>
        Assert.Equal(expected, DomainProbeText.For(verdict));

    [Fact]
    public void Failure_text_matches_spec() =>
        Assert.Equal("Không kiểm tra được, thử lại", DomainProbeText.Failed);
}
```

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "ProbeTargetsTests|ProbeSummaryTests"`
Expected: lỗi biên dịch.

- [ ] **Step 3: Cài đặt**

`ProbeTargets.cs`, thêm:

```csharp
public const int MaxCustomTargets = 5;

public static IReadOnlyList<string> For(DomainSelection selection)
{
    ArgumentNullException.ThrowIfNull(selection);
    var targets = DomainCatalog.Selectable.Where(selection.IsEnabled).SelectMany(For).ToList();
    if (selection.IsEnabled(DomainGroup.Custom))
        targets.AddRange(selection.Custom.Where(c => c.Enabled).Take(MaxCustomTargets).Select(c => c.Domain));
    return targets;
}

public static DomainGroup GroupOf(string host, DomainSelection selection)
{
    ArgumentNullException.ThrowIfNull(selection);
    foreach (var group in DomainCatalog.Selectable)
        if (DomainMatcher.Matches(host, DomainCatalog.For(group))) return group;
    if (DomainMatcher.Matches(host, DomainCatalog.For(DomainGroup.Pubg))) return DomainGroup.Pubg;
    return DomainGroup.Custom;
}
```

`ProbeSummary.cs`, thêm overload. Overload cũ giữ nguyên thân hiện có:

```csharp
public static ProbeSummary Create(IReadOnlyList<ProbeResult> results, DateTimeOffset checkedAt, Func<string, DomainGroup> groupOf)
{
    ArgumentNullException.ThrowIfNull(results);
    ArgumentNullException.ThrowIfNull(groupOf);
    var groups = results.GroupBy(r => groupOf(r.Host)).Select(g => new ProbeGroupSummary(g.Key, [.. g])).ToList();
    return new ProbeSummary(groups, checkedAt);
}
```

`ProbeRunner.cs`: thân của `RunAsync(ct)` hiện tại chuyển sang `RunAsync(IEnumerable<string> hosts, CancellationToken)`, thay `ProbeTargets.For(includePubg: false)` bằng `hosts`. Thêm:

```csharp
public Task<IReadOnlyList<ProbeResult>> RunAsync(DomainSelection selection, CancellationToken cancellationToken) =>
    RunAsync(ProbeTargets.For(selection), cancellationToken);
```

Xoá `RunAsync(CancellationToken)` cũ và sửa chỗ gọi trong `AppServices.ProbeAsync`:
- lấy `var selection = await GetDomainsAsync().ConfigureAwait(false) ?? Settings.Domains;`
- chạy `_probes.RunAsync(selection, ct)`;
- dựng summary bằng `ProbeSummary.Create(results, now, h => ProbeTargets.GroupOf(h, selection))`.

`PendingSteamRemedies` vẫn chỉ xét nhóm Steam (spec không đổi `ProbeAdvisor`).

Thêm vào `AppServices`:

```csharp
public Task<IReadOnlyList<ProbeResult>> ProbeDomainsAsync(IReadOnlyList<string> hosts, CancellationToken cancellationToken) =>
    _probes.RunAsync(hosts, cancellationToken);
```

```csharp
// src/LunaDns.App/DomainProbeText.cs
using LunaDns.App.ViewModels;
using LunaDns.Core.Probing;

namespace LunaDns.App;

/// <summary>Kết luận kiểm tra cho một tên miền (spec KH8 5.3).</summary>
internal static class DomainProbeText
{
    public static string Failed => Strings.DomainProbeFailed;

    public static string For(ProbeVerdict verdict) => verdict switch
    {
        ProbeVerdict.Ok => Strings.DomainProbeOk,
        ProbeVerdict.DnsPoisoned => Strings.DomainProbeNeedsDns,
        ProbeVerdict.SniFiltered => Strings.DomainProbeNeedsSni,
        ProbeVerdict.Unreachable => Strings.DomainProbeUnreachable,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null),
    };

    public static Tone ToneFor(ProbeVerdict verdict) => verdict switch
    {
        ProbeVerdict.Ok => Tone.Warning,
        ProbeVerdict.Unreachable => Tone.Error,
        _ => Tone.Success,
    };
}
```

Kiểm tên các giá trị trong `src/LunaDns.App/ViewModels/Tone.cs` và dùng tên thật (ví dụ `Tone.Warn` thay cho `Tone.Warning`).

`Strings.resx` và `Strings.cs` thêm 5 khoá:

| Khoá | Giá trị |
|---|---|
| `DomainProbeOk` | Không bị chặn, không cần LunaDNS |
| `DomainProbeNeedsDns` | Giúp được (cần Định tuyến DNS) |
| `DomainProbeNeedsSni` | Giúp được (cần Proxy SNI) |
| `DomainProbeUnreachable` | Chặn theo IP hoặc cách khác, LunaDNS không giúp được |
| `DomainProbeFailed` | Không kiểm tra được, thử lại |

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Core.Tests && dotnet test tests/LunaDns.App.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: kiểm tra theo nhóm tên miền và kết luận cho từng tên miền"
```

---

### Task 11: `CustomDomainsViewModel`: thêm, sửa, xoá, hoàn tác, nhập/xuất, lưu

Spec mục 5.2.

**Files:**
- Create: `src/LunaDns.App/ViewModels/CustomDomainRowViewModel.cs`, `src/LunaDns.App/ViewModels/CustomDomainsViewModel.cs`
- Modify: `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs`
- Test: `tests/LunaDns.App.Tests/CustomDomainsViewModelTests.cs`

**Interfaces:**
- Consumes: `DomainPolicy.Check`, `DomainPolicy.SplitInput`, `DomainSelection`, `DomainApplyResult`, `DomainProbeText`, `ObservableObject` (có sẵn).
- Produces:
  - `CustomDomainRowViewModel`:
    - thuộc tính `string Domain`, `bool Enabled`, `bool IsSelected`, `bool IsEditing`, `string EditText`;
    - kết quả kiểm tra: `string? ProbeText`, `Tone? ProbeTone`, `string? ProbeDetail`;
  - `CustomDomainsViewModel(DomainSelection initial, Func<DomainSelection, Task<DomainApplyResult>> apply, Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ProbeResult>>> probe, TimeProvider time)`, gồm:
    - danh sách và trạng thái: `ObservableCollection<CustomDomainRowViewModel> Rows`, `string AddText`, `IReadOnlyList<string> AddErrors`, `string CountText`, `bool IsDirty`, `bool IsBusy`, `string? ErrorMessage`;
    - hoàn tác xoá: `string? UndoText`, `string? UndoSecondsText` (đếm ngược từng giây), `void Undo()`, `void Delete(CustomDomainRowViewModel)`;
    - màn trống: `bool IsEmpty`;
    - thêm và sửa: `void Add()`, `bool CommitEdit(CustomDomainRowViewModel)`;
    - nhập/xuất: `string Import(string fileText)` (trả câu tổng kết), `string Export()`;
    - kiểm tra: `Task ProbeAsync(bool selectedOnly, CancellationToken)`;
    - lưu: `Task<bool> SaveAsync()` (true = đóng hộp thoại), `DomainSelection Current`.

- [ ] **Step 1: Viết test hỏng**

```csharp
// tests/LunaDns.App.Tests/CustomDomainsViewModelTests.cs
using System.Net;
using LunaDns.App;
using LunaDns.App.Features;
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests;

public sealed class CustomDomainsViewModelTests
{
    private readonly FakeTimeProvider _time = new();
    private DomainSelection? _applied;
    private DomainApplyResult _applyResult = new(true, null);

    private CustomDomainsViewModel Create(params string[] domains) => new(
        new DomainSelection { EnabledGroups = [DomainGroup.Steam, DomainGroup.Custom], Custom = [.. domains.Select(d => new CustomDomain(d))] },
        s => { _applied = s; return Task.FromResult(_applyResult); },
        (hosts, _) => Task.FromResult<IReadOnlyList<ProbeResult>>(
            [.. hosts.Select(h => new ProbeResult(h, h.StartsWith("bad", StringComparison.Ordinal) ? ProbeVerdict.Unreachable : ProbeVerdict.SniFiltered, [], [], false))]),
        _time);

    [Fact]
    public void Add_accepts_many_lines_and_keeps_rejected_text()
    {
        var vm = Create();
        vm.AddText = "https://www.reddit.com/r/x\nredd.it, com.vn\nstore.steampowered.com";

        vm.Add();

        Assert.Equal(["reddit.com", "redd.it"], vm.Rows.Select(r => r.Domain));
        Assert.Equal("com.vn\nstore.steampowered.com", vm.AddText);
        Assert.Equal(2, vm.AddErrors.Count);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Edit_revalidates_and_rejects_duplicates()
    {
        var vm = Create("reddit.com", "redd.it");
        var row = vm.Rows[1];
        row.IsEditing = true;

        row.EditText = "reddit.com";
        Assert.False(vm.CommitEdit(row));
        Assert.Equal("redd.it", row.Domain);

        row.EditText = "https://i.redd.it/abc";
        Assert.True(vm.CommitEdit(row));
        Assert.Equal("i.redd.it", row.Domain);
        Assert.False(row.IsEditing);
    }

    [Fact]
    public void Delete_can_be_undone_within_six_seconds()
    {
        var vm = Create("reddit.com", "redd.it");

        vm.Delete(vm.Rows[0]);
        Assert.Equal(["redd.it"], vm.Rows.Select(r => r.Domain));
        Assert.NotNull(vm.UndoText);

        vm.Undo();
        Assert.Equal(["reddit.com", "redd.it"], vm.Rows.Select(r => r.Domain));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Undo_offer_expires_after_six_seconds()
    {
        var vm = Create("reddit.com");
        vm.Delete(vm.Rows[0]);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("còn 4 giây", vm.UndoSecondsText);

        _time.Advance(TimeSpan.FromSeconds(4.1));

        Assert.Null(vm.UndoText);
        vm.Undo();
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Import_merges_and_reports_counts_and_export_round_trips()
    {
        var vm = Create("reddit.com");

        var report = vm.Import("# danh sách\nreddit.com\nredd.it\ncom\n# off: example.org\n");

        Assert.Equal(["reddit.com", "redd.it", "example.org"], vm.Rows.Select(r => r.Domain));
        Assert.False(vm.Rows[2].Enabled);
        Assert.Contains("2", report, StringComparison.Ordinal);   // đã thêm 2
        Assert.Contains("2", report[report.IndexOf(',', StringComparison.Ordinal)..], StringComparison.Ordinal); // bỏ qua 2 (trùng + lỗi)
        Assert.Equal("reddit.com\nredd.it\n# off: example.org\n", vm.Export());
    }

    [Fact]
    public async Task Probe_fills_each_row_with_spec_text()
    {
        var vm = Create("reddit.com", "bad.example.org");

        await vm.ProbeAsync(selectedOnly: false, CancellationToken.None);

        Assert.Equal(DomainProbeText.For(ProbeVerdict.SniFiltered), vm.Rows[0].ProbeText);
        Assert.Equal(DomainProbeText.For(ProbeVerdict.Unreachable), vm.Rows[1].ProbeText);
    }

    [Fact]
    public async Task Save_applies_and_closes_on_success()
    {
        var vm = Create();
        vm.AddText = "reddit.com";
        vm.Add();

        Assert.True(await vm.SaveAsync());
        Assert.Equal([new CustomDomain("reddit.com")], _applied!.Custom);
        Assert.Equal([DomainGroup.Steam, DomainGroup.Custom], _applied.EnabledGroups);
    }

    [Fact]
    public async Task Save_failure_keeps_dialog_open_with_message()
    {
        _applyResult = new DomainApplyResult(false, "lỗi giả lập");
        var vm = Create();
        vm.AddText = "reddit.com";
        vm.Add();

        Assert.False(await vm.SaveAsync());
        Assert.Equal("lỗi giả lập", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Count_text_shows_enabled_over_total_and_limit()
    {
        var vm = Create("reddit.com", "redd.it");
        vm.Rows[1].Enabled = false;

        Assert.Equal("1/2 đang bật · tối đa 200", vm.CountText);
    }
}
```

Câu tổng kết của `Import` có dạng "Đã thêm {0}, bỏ qua {1} (trùng hoặc không hợp lệ)." Hai khẳng định số trong test dựa trên dạng này. Nếu đổi câu thì sửa test cho khớp.

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.App.Tests --filter CustomDomainsViewModelTests`
Expected: lỗi biên dịch.

- [ ] **Step 3: Cài đặt**

```csharp
// src/LunaDns.App/ViewModels/CustomDomainRowViewModel.cs
namespace LunaDns.App.ViewModels;

internal sealed class CustomDomainRowViewModel(string domain, bool enabled) : ObservableObject
{
    private string _domain = domain;
    private bool _enabled = enabled;
    private bool _isSelected;
    private bool _isEditing;
    private string _editText = domain;
    private string? _probeText;
    private Tone? _probeTone;
    private string? _probeDetail;

    public event EventHandler? Changed;

    public string Domain { get => _domain; set { if (Set(ref _domain, value)) Changed?.Invoke(this, EventArgs.Empty); } }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) Changed?.Invoke(this, EventArgs.Empty); } }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsEditing { get => _isEditing; set { if (Set(ref _isEditing, value) && value) EditText = Domain; } }
    public string EditText { get => _editText; set => Set(ref _editText, value); }
    public string? ProbeText { get => _probeText; set => Set(ref _probeText, value); }
    public Tone? ProbeTone { get => _probeTone; set => Set(ref _probeTone, value); }
    public string? ProbeDetail { get => _probeDetail; set => Set(ref _probeDetail, value); }
}
```

Kiểm chữ ký helper trong `ObservableObject.cs` (20 dòng). Nếu helper không tên `Set`, không trả `bool`, hoặc không nhận `ref`, thì viết theo đúng helper đó.

```csharp
// src/LunaDns.App/ViewModels/CustomDomainsViewModel.cs
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using LunaDns.App.Features;
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.App.ViewModels;

/// <summary>Hộp thoại "Tên miền tuỳ chỉnh" (spec KH8 5.2). Chỉ áp dụng khi SaveAsync; Huỷ không đổi gì trên máy.</summary>
internal sealed class CustomDomainsViewModel : ObservableObject
{
    private const string OffPrefix = "# off: ";
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(6);

    private readonly DomainSelection _initial;
    private readonly Func<DomainSelection, Task<DomainApplyResult>> _apply;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ProbeResult>>> _probe;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<string> _enabledCatalog;
    private string _addText = "";
    private IReadOnlyList<string> _addErrors = [];
    private bool _isBusy;
    private string? _errorMessage;
    private (int Index, CustomDomainRowViewModel Row)? _lastDeleted;
    private ITimer? _undoTimer;
    private int _undoLeft;

    public CustomDomainsViewModel(
        DomainSelection initial,
        Func<DomainSelection, Task<DomainApplyResult>> apply,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ProbeResult>>> probe,
        TimeProvider time)
    {
        _initial = initial ?? throw new ArgumentNullException(nameof(initial));
        _apply = apply;
        _probe = probe;
        _time = time;
        _enabledCatalog = [.. DomainCatalog.Selectable.Where(initial.IsEnabled).SelectMany(DomainCatalog.For)];
        Rows = [.. initial.Custom.Select(c => Track(new CustomDomainRowViewModel(c.Domain, c.Enabled)))];
        Rows.CollectionChanged += (_, _) => RaiseListChanged();
    }

    public ObservableCollection<CustomDomainRowViewModel> Rows { get; }

    public string AddText { get => _addText; set => Set(ref _addText, value); }
    public IReadOnlyList<string> AddErrors { get => _addErrors; private set => Set(ref _addErrors, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public string? ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public string? UndoText => _lastDeleted is { } d ? string.Format(CultureInfo.CurrentCulture, Strings.CustomDeletedFormat, d.Row.Domain) : null;

    public string? UndoSecondsText => _lastDeleted is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.CustomUndoSecondsFormat, _undoLeft);

    /// <summary>Danh sách đang rỗng: hộp thoại hiện màn trống (mockup 2.1).</summary>
    public bool IsEmpty => Rows.Count == 0;

    public string CountText => string.Format(CultureInfo.CurrentCulture, Strings.CustomCountFormat,
        Rows.Count(r => r.Enabled), Rows.Count, DomainPolicy.MaxCustomDomains);

    public DomainSelection Current => _initial with { Custom = [.. Rows.Select(r => new CustomDomain(r.Domain, r.Enabled))] };

    public bool IsDirty => !Current.SameAs(_initial);

    public void Add()
    {
        var rejected = new List<string>();
        var errors = new List<string>();
        foreach (var item in DomainPolicy.SplitInput(AddText))
        {
            var check = DomainPolicy.Check(item, [.. Rows.Select(r => r.Domain)], _enabledCatalog);
            if (check.Domain is null) continue;
            if (check.Ok) Rows.Add(Track(new CustomDomainRowViewModel(check.Domain, true)));
            else { rejected.Add(item); errors.Add($"{item}: {Explain(check)}"); }
        }
        AddText = string.Join('\n', rejected);
        AddErrors = errors;
    }

    public bool CommitEdit(CustomDomainRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var others = Rows.Where(r => !ReferenceEquals(r, row)).Select(r => r.Domain).ToList();
        var check = DomainPolicy.Check(row.EditText, others, _enabledCatalog);
        if (!check.Ok)
        {
            ErrorMessage = $"{row.EditText}: {Explain(check)}";
            return false;
        }
        ErrorMessage = null;
        row.Domain = check.Domain!;
        row.ProbeText = null;
        row.ProbeTone = null;
        row.IsEditing = false;
        return true;
    }

    public void Delete(CustomDomainRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var index = Rows.IndexOf(row);
        if (index < 0) return;
        Rows.RemoveAt(index);
        _lastDeleted = (index, row);
        _undoTimer?.Dispose();
        _undoLeft = (int)UndoWindow.TotalSeconds;
        _undoTimer = _time.CreateTimer(_ => TickUndo(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        RaisePropertyChanged(nameof(UndoText));
        RaisePropertyChanged(nameof(UndoSecondsText));
    }

    public void Undo()
    {
        if (_lastDeleted is not { } d) return;
        Rows.Insert(Math.Min(d.Index, Rows.Count), d.Row);
        ClearUndo();
    }

    /// <summary>Gộp file .txt vào danh sách (không thay thế). Trả câu tổng kết.</summary>
    public string Import(string fileText)
    {
        ArgumentNullException.ThrowIfNull(fileText);
        int added = 0, skipped = 0;
        foreach (var rawLine in fileText.Split('\n'))
        {
            var line = rawLine.Trim();
            var enabled = !line.StartsWith(OffPrefix, StringComparison.Ordinal);
            if (!enabled) line = line[OffPrefix.Length..];
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var check = DomainPolicy.Check(line, [.. Rows.Select(r => r.Domain)], _enabledCatalog);
            if (check.Ok) { Rows.Add(Track(new CustomDomainRowViewModel(check.Domain!, enabled))); added++; }
            else if (check.Domain is not null) skipped++;
        }
        return string.Format(CultureInfo.CurrentCulture, Strings.CustomImportReportFormat, added, skipped);
    }

    public string Export()
    {
        var text = new StringBuilder();
        foreach (var row in Rows) text.Append(row.Enabled ? "" : OffPrefix).Append(row.Domain).Append('\n');
        return text.ToString();
    }

    public async Task ProbeAsync(bool selectedOnly, CancellationToken cancellationToken)
    {
        var rows = Rows.Where(r => !selectedOnly || r.IsSelected).ToList();
        if (rows.Count == 0) return;
        IsBusy = true;
        try
        {
            var results = await _probe([.. rows.Select(r => r.Domain)], cancellationToken);
            foreach (var row in rows)
            {
                var result = results.FirstOrDefault(r => string.Equals(r.Host, row.Domain, StringComparison.Ordinal));
                row.ProbeText = result is null ? DomainProbeText.Failed : DomainProbeText.For(result.Verdict);
                row.ProbeTone = result is null ? Tone.Error : DomainProbeText.ToneFor(result.Verdict);
                row.ProbeDetail = result is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.CustomProbeDetailFormat,
                    string.Join(", ", result.SystemAddresses), string.Join(", ", result.DohAddresses));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ranh giới của nút bấm: lỗi mạng hay huỷ đều thành "Không kiểm tra được" (spec KH8 5.3).
            foreach (var row in rows) { row.ProbeText = DomainProbeText.Failed; row.ProbeTone = Tone.Error; }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SaveAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _apply(Current);
            if (!result.Success) ErrorMessage = result.Message;
            return result.Success;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private CustomDomainRowViewModel Track(CustomDomainRowViewModel row)
    {
        row.Changed += (_, _) => RaiseListChanged();
        return row;
    }

    private void TickUndo()
    {
        _undoLeft--;
        if (_undoLeft <= 0) ClearUndo();
        else RaisePropertyChanged(nameof(UndoSecondsText));
    }

    private void ClearUndo()
    {
        _undoTimer?.Dispose();
        _undoTimer = null;
        _lastDeleted = null;
        RaisePropertyChanged(nameof(UndoText));
        RaisePropertyChanged(nameof(UndoSecondsText));
    }

    private void RaiseListChanged()
    {
        RaisePropertyChanged(nameof(CountText));
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(IsEmpty));
    }

    private static string Explain(DomainCheck check) => check.Rejection switch
    {
        DomainRejection.Syntax => Strings.DomainRejectSyntax,
        DomainRejection.PublicSuffix => string.Format(CultureInfo.CurrentCulture, Strings.DomainRejectPublicSuffixFormat, check.Domain),
        DomainRejection.LocalName => Strings.DomainRejectLocal,
        DomainRejection.Denied => string.Format(CultureInfo.CurrentCulture, Strings.DomainRejectDeniedFormat, check.Detail),
        DomainRejection.Duplicate => string.Format(CultureInfo.CurrentCulture, Strings.DomainRejectDuplicateFormat, check.Detail),
        DomainRejection.Limit => string.Format(CultureInfo.CurrentCulture, Strings.DomainRejectLimitFormat, DomainPolicy.MaxCustomDomains),
        _ => "",
    };
}
```

Bộ đếm hoàn tác chạy trên luồng của `TimeProvider`. Khi `TimeProvider.System` dùng trong app, `ClearUndo` phải chạy trên luồng UI: bọc lời gọi bằng `Avalonia.Threading.Dispatcher.UIThread.Post(ClearUndo)`, cùng cách `MainViewModel` đang chuyển sự kiện watchdog về luồng UI. Test dùng `FakeTimeProvider` nên gọi thẳng, nhưng phải giữ được. Cách gọn: nhận thêm tham số `Action<Action> post`, mặc định `a => a()`, và app truyền `Dispatcher.UIThread.Post`.

`RaisePropertyChanged` là tên giả định; dùng đúng tên trong `ObservableObject`. Bắt `Exception` ở `ProbeAsync` phải theo mẫu ranh giới nút bấm mà `MainViewModel` đang dùng; nếu analyzer `CA1031` báo, thêm `SuppressMessage` kèm lý do giống chỗ có sẵn.

`Strings.resx` và `Strings.cs` thêm:

| Khoá | Giá trị |
|---|---|
| `CustomDeletedFormat` | Đã xoá {0} |
| `CustomUndoSecondsFormat` | còn {0} giây |
| `CustomCountFormat` | {0}/{1} đang bật · tối đa {2} |
| `CustomImportReportFormat` | Đã thêm {0}, bỏ qua {1} (trùng hoặc không hợp lệ). |
| `CustomProbeDetailFormat` | DNS hệ thống: {0} · DoH: {1} |
| `DomainRejectSyntax` | Không phải tên miền hợp lệ. |
| `DomainRejectPublicSuffixFormat` | Tên miền quá rộng, sẽ ảnh hưởng mọi trang dưới .{0}. |
| `DomainRejectLocal` | Tên miền nội bộ hoặc quá rộng, LunaDNS không định tuyến. |
| `DomainRejectDeniedFormat` | Tên miền này bị chặn vì lý do an toàn: {0}. |
| `DomainRejectDuplicateFormat` | Đã có trong danh sách ({0}). |
| `DomainRejectLimitFormat` | Tối đa {0} tên miền. |

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.App.Tests`
Expected: PASS, kể cả `StringsTests` (khoá mới có trong resx).

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests
git commit -m "feat(app): ViewModel hộp thoại tên miền tuỳ chỉnh"
```

---

### Task 12: Giao diện: khu "Tên miền", hộp thoại Tuỳ chỉnh và danh sách nhóm

Spec mục 5.1, 5.2.

**Files:**
- Create: `src/LunaDns.App/ViewModels/DomainGroupViewModel.cs`
- Modify: `src/LunaDns.App/ViewModels/MainViewModel.cs`, `src/LunaDns.App/ViewModels/IUserPrompts.cs`
- Create: `src/LunaDns.App/Views/CustomDomainsWindow.axaml`, `CustomDomainsWindow.axaml.cs`, `DomainListWindow.axaml`, `DomainListWindow.axaml.cs`
- Modify: `src/LunaDns.App/Views/MainWindow.axaml` (chèn trước dòng chú thích `<!-- Chân trang: …`, hiện ở dòng 161), `MainWindow.axaml.cs` (cài `IUserPrompts` mới)
- Modify: `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs`
- Modify: `src/LunaDns.App/Assets/Icons.axaml` (thêm `Icon.Delete`)
- Modify: `src/LunaDns.Core/Settings/AppSettings.cs` (thêm `DomainsIntroSeen`)
- Test: `tests/LunaDns.App.Tests/DomainGroupViewModelTests.cs`, `tests/LunaDns.App.Tests/StringsTests.cs`

**Interfaces:**
- Consumes: `AppServices.GetDomainsAsync`, `ApplyDomainsAsync`, `ProbeDomainsAsync`; `CustomDomainsViewModel`; `ProbeSummary.Groups`.
- Produces:
  - `DomainGroupViewModel`:
    - thuộc tính `DomainGroup Group`, `string Title`, `string CountText`, `bool IsEnabled` (setter gọi apply), `string? StatusText`, `Tone? StatusTone`, `bool IsCustom`;
    - `Task SetEnabledAsync(bool)`;
  - `MainViewModel.DomainGroups : ObservableCollection<DomainGroupViewModel>`, `MainViewModel.LoadDomainsAsync()`, `MainViewModel.OpenGroupAsync(DomainGroupViewModel)`;
  - `IUserPrompts.ShowDomainListAsync(string title, IReadOnlyList<string> domains)` và `IUserPrompts.EditCustomDomainsAsync(CustomDomainsViewModel vm) : Task<bool>`.

- [ ] **Step 1: Viết test hỏng**

```csharp
// tests/LunaDns.App.Tests/DomainGroupViewModelTests.cs
using LunaDns.App.Features;
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;

namespace LunaDns.App.Tests;

public sealed class DomainGroupViewModelTests
{
    [Fact]
    public async Task Toggling_a_group_applies_new_selection()
    {
        DomainSelection? applied = null;
        var selection = new DomainSelection();
        var vm = new DomainGroupViewModel(DomainGroup.Reddit, () => selection,
            s => { applied = s; selection = s; return Task.FromResult(new DomainApplyResult(true, null)); }, _ => { });

        await vm.SetEnabledAsync(true);

        Assert.True(applied!.IsEnabled(DomainGroup.Reddit));
        Assert.True(vm.IsEnabled);
    }

    [Fact]
    public async Task Failed_toggle_reverts_switch_and_reports_message()
    {
        string? shown = null;
        var vm = new DomainGroupViewModel(DomainGroup.Steam, () => new DomainSelection(),
            _ => Task.FromResult(new DomainApplyResult(false, "lỗi giả lập")), m => shown = m);

        await vm.SetEnabledAsync(false);

        Assert.True(vm.IsEnabled);
        Assert.Equal("lỗi giả lập", shown);
    }

    [Fact]
    public void Count_text_for_custom_shows_enabled_over_total()
    {
        var selection = new DomainSelection { Custom = [new("a.example.org"), new("b.example.org", Enabled: false)] };
        var vm = new DomainGroupViewModel(DomainGroup.Custom, () => selection, _ => Task.FromResult(new DomainApplyResult(true, null)), _ => { });

        Assert.Equal("1/2 đang bật", vm.CountText);
    }
}
```

- [ ] **Step 2: Chạy, xác nhận hỏng**

Run: `dotnet test tests/LunaDns.App.Tests --filter DomainGroupViewModelTests`
Expected: lỗi biên dịch.

- [ ] **Step 3: Cài đặt ViewModel**

```csharp
// src/LunaDns.App/ViewModels/DomainGroupViewModel.cs
using System.Globalization;
using LunaDns.App.Features;
using LunaDns.Core.Domains;

namespace LunaDns.App.ViewModels;

/// <summary>Một dòng của khu "Tên miền" (spec KH8 5.1). Mỗi lần bật/tắt là một lần áp trọn vẹn.</summary>
internal sealed class DomainGroupViewModel(
    DomainGroup group,
    Func<DomainSelection> current,
    Func<DomainSelection, Task<DomainApplyResult>> apply,
    Action<string> showError) : ObservableObject
{
    private string? _statusText;
    private Tone? _statusTone;
    private bool _busy;

    public DomainGroup Group { get; } = group;

    public bool IsCustom => Group == DomainGroup.Custom;

    public string Title => Group switch
    {
        DomainGroup.Steam => Strings.GroupSteam,
        DomainGroup.OtherGames => Strings.GroupOtherGames,
        DomainGroup.Reddit => Strings.GroupReddit,
        DomainGroup.Custom => Strings.GroupCustom,
        _ => Group.ToString(),
    };

    public bool IsEnabled
    {
        get => current().IsEnabled(Group);
        set => _ = SetEnabledAsync(value);
    }

    public string CountText => IsCustom
        ? string.Format(CultureInfo.CurrentCulture, Strings.GroupCustomCountFormat, current().Custom.Count(c => c.Enabled), current().Custom.Count)
        : string.Format(CultureInfo.CurrentCulture, Strings.GroupCountFormat, DomainCatalog.For(Group).Count);

    public string? StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public Tone? StatusTone { get => _statusTone; set => Set(ref _statusTone, value); }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_busy || enabled == IsEnabled) return;
        _busy = true;
        try
        {
            var result = await apply(current().WithGroup(Group, enabled));
            if (!result.Success && result.Message is not null) showError(result.Message);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    public void Refresh()
    {
        RaisePropertyChanged(nameof(IsEnabled));
        RaisePropertyChanged(nameof(CountText));
    }
}
```

`Strings` thêm:

| Khoá | Giá trị |
|---|---|
| `DomainsSectionTitle` | Tên miền |
| `GroupSteam` | Steam |
| `GroupOtherGames` | Nền tảng game khác |
| `GroupReddit` | Reddit |
| `GroupCustom` | Tuỳ chỉnh |
| `GroupCountFormat` | {0} tên miền |
| `GroupCustomCountFormat` | {0}/{1} đang bật |
| `GroupViewButton` | Xem |
| `GroupEditButton` | Sửa… |
| `CustomDialogTitle` | Tên miền tuỳ chỉnh |
| `CustomAddWatermark` | Dán một hoặc nhiều tên miền / đường link… |
| `CustomAddButton` | Thêm |
| `CustomProbeSelected` | Kiểm tra đã chọn |
| `CustomProbeAll` | Kiểm tra tất cả |
| `CustomImport` | Nhập .txt |
| `CustomExport` | Xuất .txt |
| `CustomUndo` | Hoàn tác |
| `CustomSave` | Lưu |
| `CustomCancel` | Huỷ |
| `CustomApplying` | Đang áp dụng… |
| `CustomDiscardQuestion` | Bỏ các thay đổi chưa lưu? |
| `CustomNotChecked` | chưa kiểm tra |
| `DomainsNewBadge` | MỚI |
| `DomainsRoutedCountFormat` | {0} tên miền đang định tuyến |
| `DomainsFootnote` | Chỉ các tên miền đã chọn đi qua LunaDNS. Trang bị chặn theo IP thì LunaDNS không giúp được. |
| `GroupNotChecked` | Chưa kiểm tra |
| `GroupCustomEmpty` | Chưa có tên miền |
| `GroupKeptBack` | Đã giữ lại |
| `GroupNotApplied` | Chưa áp được |
| `CustomAddLabel` | Thêm tên miền |
| `CustomAddHint` | Mỗi dòng một tên miền, hoặc cách nhau bằng dấu phẩy. Luôn gồm cả tên miền con. |
| `CustomAddedFormat` | Đã thêm {0} tên miền. {1} dòng còn lại trong ô để bạn sửa. |
| `CustomEmptyTitle` | Chưa có tên miền tuỳ chỉnh |
| `CustomEmptyBody` | Dán tên miền hoặc đường link vào ô ở trên, hoặc nhập từ file .txt. Tên miền đã có trong nhóm dựng sẵn đang bật thì không cần thêm. |
| `CustomApplyingDetailFormat` | Tắt rồi bật lại Định tuyến DNS và Proxy SNI với {0} tên miền. Lỗi thì LunaDNS tự quay về danh sách cũ. |
| `CustomSaveFailedTitle` | Chưa lưu được |
| `CustomSaveFailedSuffix` | Máy vẫn đang chạy theo danh sách cũ; sửa xong bấm Lưu lại. |
| `CustomDiscardBody` | Bạn đã thêm, sửa hoặc xoá tên miền nhưng chưa bấm Lưu. Bỏ thì danh sách trở lại như lúc mở hộp thoại. |
| `CustomDiscardKeepEditing` | Tiếp tục sửa |
| `CustomDiscardConfirm` | Bỏ thay đổi |
| `CustomEditLabel` | Sửa tên miền |
| `CustomEditHintFormat` | Enter để nhận · Esc để huỷ · sẽ lưu thành {0} |
| `CustomProbeSelectedFormat` | Kiểm tra đã chọn ({0}) |
| `GroupListTitleFormat` | Nhóm {0} |
| `GroupListHeaderFormat` | {0} · {1} tên miền |
| `GroupListSubtitle` | Nhóm dựng sẵn, đã thử trên mạng Việt Nam. Mỗi tên miền gồm cả tên miền con. |
| `GroupListNote` | Danh sách này do LunaDNS cập nhật theo mỗi bản phát hành, không sửa được. Muốn bỏ một tên miền: tắt cả nhóm, rồi thêm phần cần dùng vào Tuỳ chỉnh. |
| `CloseButton` | Đóng |
| `ProbeCustomSummaryFormat` | {0} tên miền giúp được, {1} tên miền LunaDNS không giúp được |
| `ProbeCustomHint` | Tuỳ chỉnh chỉ kiểm 5 tên miền đầu đang bật. Kiểm từng tên miền trong hộp thoại Sửa…. |

Thẻ Kiểm tra (mockup 1.2): nhóm dựng sẵn giữ dòng `ProbeLineViewModel` như hiện nay; nhóm Tuỳ chỉnh dùng `ProbeCustomSummaryFormat` (đếm kết quả `DnsPoisoned`/`SniFiltered` là "giúp được", `Unreachable` là "không giúp được") và hiện `ProbeCustomHint` bên dưới khi nhóm Tuỳ chỉnh có hơn 0 dòng đang bật.

**Sửa chữ đang có** (mockup đã dùng câu mới; câu cũ chỉ nhắc Steam):

| Khoá | Giá trị cũ | Giá trị mới |
|---|---|---|
| `HeaderSubtitle` | Sửa lỗi vào Steam khi nhà mạng chặn DNS hoặc lọc SNI. | Vào lại Steam, Reddit… khi nhà mạng chặn DNS hoặc lọc SNI. |
| `FeatureDesc_DnsRouting` | Giúp đăng nhập Steam: hỏi tên miền Steam qua DNS mã hoá (DoH) thay cho DNS của nhà mạng. | Hỏi các tên miền đã chọn qua DNS mã hoá (DoH) thay cho DNS của nhà mạng. |
| `FeatureDesc_SniProxy` | Giúp mở Store và Community: cắt gói mở kết nối để qua được bộ lọc SNI. | Cắt gói mở kết nối để các tên miền đã chọn qua được bộ lọc SNI. |
| `NotCheckedYet` | Chưa kiểm tra. Bấm "Kiểm tra" để xem Steam có bị chặn không. | Chưa kiểm tra. Bấm "Kiểm tra" để xem các nhóm tên miền đang bật có bị chặn không. |

`StringsTests`: nếu có test khẳng định nội dung câu cũ thì sửa theo câu mới. Thêm test `HeaderSubtitle` không còn chứa "Sửa lỗi vào Steam".

**Icon mới `Icon.Delete`:** thêm `StreamGeometry` vào `Icons.axaml`, lấy icon "Delete" bản Regular 24 cùng bộ Fluent UI System Icons (MIT) mà các icon hiện có đang dùng. Kiểm nguồn và giấy phép trong `docs/THIRD-PARTY-NOTICES.md`; nếu bộ hiện tại không phải Fluent thì lấy icon xoá của đúng bộ đó. Nút xoá trên mỗi dòng dùng `PathIcon Data="{DynamicResource Icon.Delete}"`. Không thêm icon cho Nhập/Xuất `.txt`; hai nút này chỉ có chữ.

**Huy hiệu "MỚI":** `AppSettings` thêm `public bool DomainsIntroSeen { get; init; }`, mặc định `false`. Khu Tên miền hiện huy hiệu `DomainsNewBadge` và viền màu `Brush.Warning` khi `DomainsIntroSeen = false`. Lần đầu user bấm Xem/Sửa hoặc bật/tắt một nhóm thì lưu `DomainsIntroSeen = true` (qua `SaveSettings` sẵn có). Trên macOS đây là settings của giao diện, không gửi cho dịch vụ nền.

`MainViewModel`:
- thêm `public ObservableCollection<DomainGroupViewModel> DomainGroups { get; } = [];`;
- giữ trường `private DomainSelection _domains = new();`;
- thêm `public string RoutedCountText`: `DomainsRoutedCountFormat` với `FeatureDomains.Routed(_domains).Count`, tính lại sau mỗi lần áp thành công;
- thêm `public bool ShowDomainsIntro => !_services.Settings.DomainsIntroSeen;`;
- dòng nhóm có trạng thái tạm: nhóm cuối bị từ chối vì `NoDomains` thì `StatusText = Strings.GroupKeptBack` (tone lỗi); áp lỗi vì dịch vụ nền cũ thì `StatusText = Strings.GroupNotApplied` (tone cảnh báo). Hai trạng thái này mất ở lần Kiểm tra hoặc lần áp thành công kế tiếp. Dòng chưa kiểm tra hiện `GroupNotChecked`; dòng Tuỳ chỉnh rỗng hiện `GroupCustomEmpty`;
- `LoadDomainsAsync()`:
  - gọi `_services.GetDomainsAsync()`; kết quả `null` thì dùng `_services.Settings.Domains`;
  - dựng 4 dòng, theo `DomainCatalog.Selectable` rồi `DomainGroup.Custom`;
  - truyền `current: () => _domains`;
  - `apply` là hàm gọi `_services.ApplyDomainsAsync`, thành công thì gán `_domains` mới;
  - `showError` dùng banner/thông báo lỗi sẵn có của `MainViewModel`. Đọc cách `FeatureViewModel` báo lỗi để làm giống.
- gọi `LoadDomainsAsync()` ở chỗ `MainViewModel` đang chạy `RefreshAsync` lần đầu.
- sau mỗi lần Kiểm tra, với mỗi `ProbeGroupSummary` trong `ProbeSummary.Groups`, gán `StatusText = DomainProbeText.For(group.Worst)` và `StatusTone = DomainProbeText.ToneFor(group.Worst)` cho dòng có `Group` trùng.
- `OpenGroupAsync(DomainGroupViewModel row)`:
  - nhóm dựng sẵn: gọi `_prompts.ShowDomainListAsync(row.Title, DomainCatalog.For(row.Group))`;
  - nhóm Tuỳ chỉnh: dựng `new CustomDomainsViewModel(_domains, apply, _services.ProbeDomainsAsync, TimeProvider.System)`, trong đó `apply` vừa gọi `ApplyDomainsAsync` vừa gán `_domains` khi thành công; sau đó `await _prompts.EditCustomDomainsAsync(vm)` rồi `row.Refresh()`.

`IUserPrompts`, thêm hai hàm như ở Interfaces. Mọi fake `IUserPrompts` trong tests phải cài thêm (trả `Task.CompletedTask` / `Task.FromResult(false)`).

- [ ] **Step 4: Chạy test ViewModel**

Run: `dotnet test tests/LunaDns.App.Tests`
Expected: PASS.

- [ ] **Step 5: View**

Chèn vào `MainWindow.axaml`, ngay trước `<!-- Chân trang: … -->`:

```xml
<!-- Tên miền (spec KH8 5.1): hiện trên cả Windows và macOS -->
<StackPanel Spacing="6">
  <TextBlock Text="{x:Static app:Strings.DomainsSectionTitle}" FontWeight="SemiBold" />
  <ItemsControl ItemsSource="{Binding DomainGroups}">
    <ItemsControl.ItemTemplate>
      <DataTemplate x:DataType="vm:DomainGroupViewModel">
        <Grid ColumnDefinitions="Auto,*,Auto,Auto" Margin="0,2">
          <CheckBox Grid.Column="0" IsChecked="{Binding IsEnabled}" />
          <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
            <TextBlock Text="{Binding Title}" />
            <TextBlock Text="{Binding CountText}" Opacity="0.7" />
          </StackPanel>
          <TextBlock Grid.Column="2" Text="{Binding StatusText}" Margin="8,0" VerticalAlignment="Center"
                     Foreground="{Binding StatusTone, Converter={StaticResource ToneBrush}}" />
          <Button Grid.Column="3"
                  Command="{Binding $parent[Window].((vm:MainViewModel)DataContext).OpenGroupCommand}"
                  CommandParameter="{Binding}">
            <TextBlock Text="{Binding IsCustom, Converter={StaticResource CustomButtonText}}" />
          </Button>
        </Grid>
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>
</StackPanel>
```

Tên tiền tố `vm:` và `app:`, tên converter màu theo `Tone` (`ToneBrush` là tên giả định; xem `Views/ToneConverters.cs`) và kiểu `AsyncCommand` lấy đúng theo `MainWindow.axaml` hiện có.
- `OpenGroupCommand` là `AsyncCommand` bọc `OpenGroupAsync`, theo mẫu các command trong `MainViewModel`.
- Thêm vào `ToneConverters.cs` converter `CustomButtonText`: `true` → `Strings.GroupEditButton`, `false` → `Strings.GroupViewButton`.

`DomainListWindow.axaml`: cửa sổ con chỉ đọc theo mockup 3.1. `Title` = `GroupListTitleFormat`; đầu cửa sổ có `GroupListHeaderFormat` và `GroupListSubtitle`; giữa là `ListBox` các tên miền (font đơn cách); dưới là khung `banner` (info) chứa `GroupListNote`; cuối là nút `CloseButton`.

`CustomDomainsWindow.axaml` theo mockup spec 5.2:
- trên cùng: nhãn `CustomAddLabel`, `TextBox` nhiều dòng `AcceptsReturn="True"` bind `AddText` (watermark `CustomAddWatermark`), nút Thêm gọi `Add()` (mờ khi ô rỗng), dòng gợi ý `CustomAddHint`, `ItemsControl` hiện `AddErrors` trong khung lỗi, rồi dòng `CustomAddedFormat` sau mỗi lần Thêm. Ô nhập có viền đỏ khi còn dòng lỗi;
- khi `IsEmpty`: thay danh sách bằng màn trống (mockup 2.1) gồm icon `Icon.Dns` 40px màu `Brush.TextSecondary`, `CustomEmptyTitle`, `CustomEmptyBody` và nút `CustomImport`;
- giữa: `ListBox SelectionMode="Multiple"` bind `Rows`. Mỗi dòng có:
  - `CheckBox` bind `Enabled`;
  - khi `IsEditing = false`, `TextBlock` hiện `Domain`; bấm đúp đặt `IsEditing = true`;
  - khi `IsEditing = true`: nhãn `CustomEditLabel`, `TextBox` bind `EditText` viền `#00E5FF`, dòng `CustomEditHintFormat` với tên đã chuẩn hoá (`DomainPolicy.Normalize(EditText)`). Enter gọi `CommitEdit`, Esc đặt `IsEditing = false`;
  - kết luận kiểm tra hiện bằng `PathIcon.tone` (hình theo mức: good/warning/error) cộng chữ, không dùng chấm màu;
  - `TextBlock` hiện `ProbeText` (mặc định `Strings.CustomNotChecked`), `ToolTip.Tip="{Binding ProbeDetail}"`;
  - nút xoá chỉ có icon `Icon.Delete`, `AutomationProperties.Name` = "Xoá {tên miền}", gọi `Delete`;
- thanh hoàn tác hiện khi `UndoText != null`: `UndoText`, `UndoSecondsText` (chữ phụ) và nút `CustomUndo` gọi `Undo()`;
- hàng nút dưới: Kiểm tra đã chọn, Kiểm tra tất cả, Nhập .txt, Xuất .txt, `CountText`, Huỷ, Lưu. Nút Kiểm tra đã chọn dùng `CustomProbeSelectedFormat` khi có dòng được chọn. Khi `IsBusy`: mọi nút, ô nhập và nút đóng cửa sổ bị khoá, danh sách mờ đi, hiện khung `banner` (info) có `CustomApplying` và `CustomApplyingDetailFormat`, nút Lưu đổi thành `ProgressBar IsIndeterminate` nhỏ cộng chữ `CustomApplying` (dùng `ProgressBar` như thẻ công tắc, không thêm icon xoay). `ErrorMessage` hiện trong khung `banner error` có tiêu đề `CustomSaveFailedTitle`, nội dung là câu lỗi nối với `CustomSaveFailedSuffix`;
- Nhập/Xuất dùng `TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync` / `SaveFilePickerAsync` với bộ lọc `*.txt`; đọc/ghi UTF-8 rồi gọi `Import(text)` / `Export()`. Câu tổng kết `Import` hiện ở thanh dưới;
- Lưu: `if (await vm.SaveAsync()) Close(true);`;
- Huỷ hoặc đóng cửa sổ khi `IsDirty`: hỏi bằng `ConfirmDialog` có sẵn (`Views/ConfirmDialog.cs`), tiêu đề `CustomDiscardQuestion`, nội dung `CustomDiscardBody`, hai nút `CustomDiscardKeepEditing` (mặc định) và `CustomDiscardConfirm` (màu `Brush.Error`). Nếu `ConfirmDialog` chưa nhận chữ nút tuỳ ý thì thêm tham số cho nó.
- Thẻ công tắc, ô trạng thái, banner và chân trang giữ nguyên style sẵn có trong `Theme.axaml`; khu Tên miền dùng `Border.card` và `TextBlock.section` ("TÊN MIỀN") như thẻ Kiểm tra.

`MainWindow.axaml.cs` cài:
- `ShowDomainListAsync`: `new DomainListWindow(title, domains).ShowDialog(this)`;
- `EditCustomDomainsAsync`: `await new CustomDomainsWindow { DataContext = vm }.ShowDialog<bool>(this)`.

- [ ] **Step 6: Chạy app và thử tay** (dùng skill `run` hoặc cách chạy dev đang dùng: Windows `Chay-tu-ma-nguon.cmd`, macOS `build/run-macos.sh`)

Kiểm từng mục:
1. Màn hình chính có khu "Tên miền" với 4 dòng, Steam và Tuỳ chỉnh được tích.
2. Bật Reddit khi Định tuyến DNS đang bật: Windows thì `Get-DnsClientNrptRule` có `.reddit.com`; macOS thì `ls /etc/resolver` có `reddit.com`.
3. Tắt hết nhóm khi DNS đang bật: hiện câu `ErrorNoDomains`, công tắc nhóm trở lại như cũ.
4. Hộp thoại Tuỳ chỉnh: dán `https://www.reddit.com/r/x`, `com.vn`, `dns.google` thì 1 dòng được thêm, 2 lỗi hiện đúng câu. Xoá rồi Hoàn tác được. Nhập/xuất file `.txt` được. Kiểm tra tất cả ra kết luận trên từng dòng. Huỷ khi có thay đổi thì bị hỏi lại. Lưu thì áp thật.
5. Đóng app bằng "Thoát & khôi phục": luật NRPT hoặc file resolver của tên miền tuỳ chỉnh bị gỡ hết.
6. So từng màn với mockup (11 màn): chữ, icon (`Icon.Delete`, icon trạng thái theo mức), huy hiệu MỚI biến mất sau lần đầu bấm Xem/Sửa, đếm ngược hoàn tác chạy từng giây, màn trống, khung đang áp dụng, hộp hỏi bỏ thay đổi.

- [ ] **Step 7: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests
git commit -m "feat(app): khu Tên miền và hộp thoại tên miền tuỳ chỉnh"
```

---

### Task 13: Thử thật nhóm dựng sẵn trên mạng VN, chốt danh sách và tên miền anti-cheat

Spec mục 4.4 và 6. Task này chặn việc phát hành nhóm "Nền tảng game khác" và "Reddit".

**Files:**
- Create: `docs/spikes/2026-11-preset-groups-field-test.md`
- Modify: `src/LunaDns.Core/Domains/DomainCatalog.cs`, `src/LunaDns.Core/Domains/DomainDenyList.cs`, `src/LunaDns.Core/Probing/ProbeTargets.cs`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainCatalogTests.cs`, `DomainPolicyTests.cs`

- [ ] **Step 1: Tra tên miền anti-cheat** của Riot Vanguard, EA Javelin và Blizzard/Battle.net (tài liệu chính thức hoặc danh sách firewall mà hãng công bố). Ghi nguồn cho từng tên miền.

- [ ] **Step 2: Thêm vào `DomainDenyList.Domains`** với lý do `AntiCheat`. Thêm test:

```csharp
[Theory]
[InlineData("<tên miền anti-cheat vừa tra>")]
public void Researched_anticheat_domains_are_denied(string host) =>
    Assert.Equal(DomainRejection.Denied, DomainPolicy.Validate(host));
```

Thay `InlineData` bằng tên miền thật vừa tra, mỗi tên một dòng. Không commit dòng còn để chữ trong ngoặc nhọn.

- [ ] **Step 3: Chạy `Routable_catalog_groups_never_hit_the_policy`.** Tên nào trong `DomainCatalog` là cha của một tên anti-cheat sẽ làm test này đỏ. Khi đó thay tên đó bằng các tên miền con cụ thể (store, launcher, CDN) rồi chạy lại cho tới khi xanh.

- [ ] **Step 4: Thử thật** trên ít nhất hai nhà mạng VN (ví dụ Viettel, VNPT hoặc FPT), bản build của nhánh này. Với mỗi nhóm:
  1. tắt hết công tắc rồi bấm Kiểm tra, ghi kết luận từng host mẫu;
  2. bật nhóm cùng Định tuyến DNS và Proxy SNI;
  3. mở trình duyệt vào trang chính của dịch vụ, ghi vào được hay không;
  4. ghi các tên miền phụ mà trang tải bị lỗi (DevTools → Network). Tên thuộc dịch vụ thì thêm vào nhóm.

  Ghi bảng vào `docs/spikes/2026-11-preset-groups-field-test.md`:

```markdown
| Nhóm | Nhà mạng | Kết luận trước khi bật | Vào được sau khi bật | Tên miền thêm | Ghi chú |
|---|---|---|---|---|---|
```

- [ ] **Step 5: Chốt.** Nhóm nào trên cả hai nhà mạng đều "LunaDNS không giúp được" hoặc vẫn không vào được thì bỏ khỏi `DomainCatalog.Selectable`. Code nhóm vẫn giữ để thử lại sau. Cập nhật `DomainCatalogTests.Selectable_groups_are_steam_other_games_reddit` cho khớp và báo PO.

- [ ] **Step 6: Chạy test, commit**

```bash
dotnet test tests/LunaDns.Core.Tests
git add src/LunaDns.Core tests/LunaDns.Core.Tests docs/spikes/2026-11-preset-groups-field-test.md
git commit -m "feat(core): chốt nhóm dựng sẵn và tên miền anti-cheat sau khi thử trên mạng VN"
```

---

### Task 14: Tài liệu, khẩu hiệu README, ghi chú spec v1

Spec mục 2 (E5, E6) và 9.

**Files:**
- Modify: `README.md`, `README.vi.md`
- Create: `docs/guide/10-ten-mien.md`
- Modify: `docs/guide/03-ba-cong-tac.md`, `docs/superpowers/specs/2026-11-13-lunadns-design.md` (D1 và mục 8)
- Create: `docs/release-notes/v0.10.0.md`

- [ ] **Step 1: README**

Câu mở đầu `README.vi.md` đổi thành: "LunaDNS là ứng dụng Windows và macOS giúp vào lại **Steam, Reddit…** khi nhà mạng chặn DNS hoặc lọc SNI." Bản tiếng Anh: "LunaDNS is a Windows and macOS app that gets you back into **Steam, Reddit and more** when your ISP blocks DNS or filters SNI."

Thay dòng "App chỉ can thiệp tên miền Steam, không đụng phần còn lại của mạng." bằng: "App chỉ can thiệp các tên miền bạn chọn (nhóm dựng sẵn hoặc danh sách Tuỳ chỉnh), không đụng phần còn lại của mạng. Trang bị chặn theo IP thì LunaDNS không giúp được." Sửa câu tương ứng trong bản tiếng Anh, và thêm mục "Tên miền" vào danh sách hướng dẫn ở cả hai README.

- [ ] **Step 2: `docs/guide/10-ten-mien.md`** gồm các mục:
  - nhóm dựng sẵn: nhóm nào, chỉ bật/tắt được, vì sao không sửa được;
  - Tuỳ chỉnh: thêm (dán link được), sửa, xoá và hoàn tác, nhập/xuất `.txt` (cú pháp `# off: `), giới hạn 200;
  - bảng câu báo lỗi (spec 4.2) và bảng kết luận Kiểm tra (spec 5.3);
  - những gì LunaDNS không làm được: chặn theo IP, thoại UDP, toàn bộ lưu lượng như VPN.

  `03-ba-cong-tac.md`: chỗ nào nói "tên miền Steam" thì đổi thành "các tên miền đã chọn (xem [Tên miền](10-ten-mien.md))".

- [ ] **Step 3: Ghi chú spec v1.** Dưới bảng D1 và đầu mục 8 thêm: "> 2026-11-21: phần nhắc LunaVoShop đã lỗi thời. LunaDNS phát hành độc lập qua GitHub Releases (spec KH8, E6)."

- [ ] **Step 4: `docs/release-notes/v0.10.0.md`**, theo khuôn `v0.9.0.md`. Nội dung chính:
  - tính năng mới: nhóm tên miền, danh sách Tuỳ chỉnh, Kiểm tra từng tên miền;
  - macOS: dịch vụ nền phải cài lại để dùng danh sách tên miền (nút Cài dịch vụ nền).

- [ ] **Step 5: Topics GitHub.** Việc này đổi cấu hình công khai của repo, nên **hỏi PO trước khi chạy**:

```bash
gh repo edit --add-topic dns-over-https --add-topic sni --add-topic dpi-bypass --add-topic vietnam --add-topic steam
```

- [ ] **Step 6: Commit**

```bash
git add README.md README.vi.md docs
git commit -m "docs: hướng dẫn tên miền, khẩu hiệu README, ghi chú spec v1"
```

---

## Kiểm tra cuối (sau Task 14)

- [ ] `dotnet build -warnaserror` sạch trên Mac và Windows.
- [ ] `dotnet test` cho cả 4 project test (Platform.Windows và App phải chạy trên Windows).
- [ ] Thử tay Task 12, Step 6 trên Windows 10/11 và macOS arm64.
- [ ] Dọn khi app chết: bật DNS với 3 tên miền tuỳ chỉnh rồi kill tiến trình.
  - Windows: chạy `LunaDNS.exe --cleanup`; NRPT phải sạch.
  - macOS: kill dịch vụ nền rồi để launchd chạy lại; `/etc/resolver` phải sạch các file có dấu LunaDNS.
- [ ] Nâng cấp từ 0.9.0: cài 0.9.0, bật DNS, cài bản mới đè lên. Settings cũ phải ra mặc định Steam và công tắc vẫn chạy.
