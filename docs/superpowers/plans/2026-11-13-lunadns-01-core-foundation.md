# LunaDNS — Kế hoạch 1: Nền móng Core + Spike

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dựng solution .NET 8 và các khối thuần logic của `LunaDns.Core` (danh sách domain, bộ tách ClientHello, bộ cắt gói, sinh PAC, Journal thay đổi), có unit test đầy đủ. Song song đó chạy 3 spike để chốt các rủi ro ở mục 10 của spec.

**Architecture:** `LunaDns.Core` là thư viện thuần .NET 8, không gọi API riêng của Windows. Mỗi file làm một việc. Mọi thứ trong kế hoạch này test được bằng xUnit mà không cần quyền Admin. Kế hoạch 2 trở đi mới dùng các khối này cho DNS stub, proxy và nền tảng Windows.

**Tech Stack:** .NET 8 SDK, C# 12, xUnit 2.9, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-11-13-lunadns-design.md`

## Global Constraints

- Target `net8.0`. Bật `Nullable` và `TreatWarningsAsErrors`.
- `LunaDns.Core` **không** được tham chiếu `Microsoft.Win32`, P/Invoke hay bất kỳ package nào riêng của Windows.
- Không có `catch { }` rỗng (spec mục 6).
- Không chép mã từ SteamAccessFix (quyết định D4).
- Thông báo cho người dùng viết tiếng Việt có dấu. Tên định danh trong code viết tiếng Anh.
- Lệnh kiểm tra: `dotnet build -warnaserror` · `dotnet test tests/LunaDns.Core.Tests`.

## Lộ trình các kế hoạch (mỗi kế hoạch một phiên)

| # | Kế hoạch | Phụ thuộc | Mục spec |
|---|---|---|---|
| **1** | **Nền móng Core + Spike** (tài liệu này) | — | 3, 4.3 (phần thuần), 4.5 (phần thuần), 10 |
| 2 | DoH client + stub resolver 127.0.0.1:53 | 1 | 4.2 |
| 3 | SplitProxy (CONNECT, PAC server, cổng exclusive) + Prober | 1, 2 | 4.1, 4.3 |
| 4 | `LunaDns.Platform.Windows`: NRPT, AutoConfigURL, hosts, ACL, task dọn dẹp | 1, kết quả spike | 3, 4.5–4.7 |
| 5 | `FeatureSet` + App Avalonia (giao diện, khay, `--cleanup`, `--report`) | 2, 3, 4 | 4.4, 4.8, 4.9, 5, 6 |
| 6 | Cập nhật có chữ ký + phát hành (publish, ký Authenticode, GitHub Releases) | 5 | 4.10, 8 |
| 7 | Trang `/lunadns` trên LunaVoShop (repo lunavo-assistant) | 6 | 8 |

Kế hoạch 2 đến 7 sẽ được viết chi tiết sau khi kế hoạch trước làm xong, để dùng tên và kiểu dữ liệu có thật.

## Cấu trúc file của kế hoạch 1

```
D:\Projects\lunadns\
├── Directory.Build.props                 # cấu hình chung: net8.0, nullable, warnaserror
├── LunaDns.sln
├── src/LunaDns.Core/
│   ├── LunaDns.Core.csproj
│   ├── Domains/DomainGroup.cs            # enum nhóm domain
│   ├── Domains/GameDomains.cs            # danh sách domain theo nhóm
│   ├── Domains/DomainMatcher.cs          # so khớp host với domain/subdomain
│   ├── Tls/SniLocation.cs                # vị trí SNI trong ClientHello
│   ├── Tls/ClientHelloParser.cs          # đọc độ dài record, tìm SNI
│   ├── Tls/SplitMode.cs
│   ├── Tls/HelloSplitter.cs              # tính điểm cắt và chia mảnh
│   ├── Proxy/PacBuilder.cs               # sinh nội dung proxy.pac
│   └── Journal/
│       ├── JournalEntry.cs
│       ├── IJournalStore.cs
│       ├── IChangeReverter.cs
│       ├── RollbackResult.cs
│       ├── ChangeJournal.cs              # ghi, xoá, hoàn tác LIFO
│       └── JsonFileJournalStore.cs       # lưu file nguyên tử, xử lý file hỏng
├── tests/LunaDns.Core.Tests/
│   ├── LunaDns.Core.Tests.csproj
│   ├── Domains/DomainMatcherTests.cs
│   ├── Tls/TestClientHello.cs            # builder sinh ClientHello mẫu cho test
│   ├── Tls/ClientHelloParserTests.cs
│   ├── Tls/HelloSplitterTests.cs
│   ├── Proxy/PacBuilderTests.cs
│   └── Journal/
│       ├── FakeReverter.cs
│       ├── InMemoryJournalStore.cs
│       ├── ChangeJournalTests.cs
│       └── JsonFileJournalStoreTests.cs
└── docs/spikes/2026-11-spike-results.md  # kết quả 3 spike
```

---

### Task 0: Cài .NET 8 SDK (PO làm, ~5 phút)

Máy hiện chưa có lệnh `dotnet`.

- [ ] **Step 1: Cài SDK** (PowerShell, chạy bằng user thường):

```bash
winget install --id Microsoft.DotNet.SDK.8 --source winget
```

- [ ] **Step 2: Mở terminal mới và kiểm tra**

Run: `dotnet --list-sdks`
Expected: có ít nhất một dòng bắt đầu bằng `8.0.`

---

### Task 1: Dựng solution và project

**Files:**
- Create: `Directory.Build.props`, `LunaDns.sln`, `src/LunaDns.Core/LunaDns.Core.csproj`, `tests/LunaDns.Core.Tests/LunaDns.Core.Tests.csproj`

**Interfaces:**
- Produces: namespace gốc `LunaDns.Core`. Project test tham chiếu `LunaDns.Core` và thấy được `internal` qua `InternalsVisibleTo`.

- [ ] **Step 1: Tạo solution và project**

```bash
cd /d/Projects/lunadns
dotnet new sln -n LunaDns
dotnet new classlib -n LunaDns.Core -o src/LunaDns.Core -f net8.0
dotnet new xunit -n LunaDns.Core.Tests -o tests/LunaDns.Core.Tests -f net8.0
dotnet sln add src/LunaDns.Core/LunaDns.Core.csproj tests/LunaDns.Core.Tests/LunaDns.Core.Tests.csproj
dotnet add tests/LunaDns.Core.Tests reference src/LunaDns.Core
rm src/LunaDns.Core/Class1.cs tests/LunaDns.Core.Tests/UnitTest1.cs
```

- [ ] **Step 2: Tạo `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Thêm `InternalsVisibleTo` vào `src/LunaDns.Core/LunaDns.Core.csproj`**, bên trong thẻ `<Project>`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="LunaDns.Core.Tests" />
  </ItemGroup>
```

- [ ] **Step 4: Build**

Run: `dotnet build -warnaserror`
Expected: `Build succeeded.` và `0 Warning(s)`. Nếu analyzer báo lỗi trên file template do `dotnet new` sinh ra thì sửa cho hết lỗi, không được hạ mức cảnh báo.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: dựng solution LunaDns với Core và Core.Tests"
```

---

### Task 2: Danh sách domain game và bộ so khớp

**Files:**
- Create: `src/LunaDns.Core/Domains/DomainGroup.cs`, `src/LunaDns.Core/Domains/GameDomains.cs`, `src/LunaDns.Core/Domains/DomainMatcher.cs`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainMatcherTests.cs`

**Interfaces:**
- Produces:
  - `enum DomainGroup { Steam, Pubg, Other }`
  - `static class GameDomains { IReadOnlyList<string> For(DomainGroup group); IReadOnlyList<string> All { get; } }`
  - `static class DomainMatcher { string Normalize(string host); bool Matches(string host, IEnumerable<string> domains); }`

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Domains/DomainMatcherTests.cs
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

public class DomainMatcherTests
{
    private static readonly string[] Domains = ["steampowered.com", "steamcommunity.com"];

    [Theory]
    [InlineData("steampowered.com")]
    [InlineData("store.steampowered.com")]
    [InlineData("STORE.SteamPowered.COM")]
    [InlineData("store.steampowered.com.")]
    [InlineData("a.b.steamcommunity.com")]
    public void Matches_domain_and_subdomains(string host) =>
        Assert.True(DomainMatcher.Matches(host, Domains));

    [Theory]
    [InlineData("evilsteampowered.com")]
    [InlineData("steampowered.com.evil.net")]
    [InlineData("google.com")]
    [InlineData("")]
    [InlineData(".")]
    public void Does_not_match_lookalikes_or_others(string host) =>
        Assert.False(DomainMatcher.Matches(host, Domains));

    [Fact]
    public void Normalize_lowercases_and_trims_trailing_dot() =>
        Assert.Equal("store.steampowered.com", DomainMatcher.Normalize(" Store.SteamPowered.com. "));

    [Fact]
    public void Steam_group_contains_core_domains()
    {
        var steam = GameDomains.For(DomainGroup.Steam);
        Assert.Contains("steampowered.com", steam);
        Assert.Contains("steamcommunity.com", steam);
        Assert.Contains("steamstatic.com", steam);
    }

    [Fact]
    public void No_group_contains_anti_cheat_domains()
    {
        Assert.DoesNotContain(GameDomains.All, d => d.Contains("xenuine", StringComparison.Ordinal));
        Assert.DoesNotContain("accounts.pubg.com", GameDomains.All);
    }

    [Fact]
    public void All_is_union_of_groups_without_duplicates()
    {
        var expected = Enum.GetValues<DomainGroup>().SelectMany(GameDomains.For).Distinct().Count();
        Assert.Equal(expected, GameDomains.All.Count);
    }
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DomainMatcherTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'Domains' does not exist`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Domains/DomainGroup.cs
namespace LunaDns.Core.Domains;

public enum DomainGroup
{
    Steam,
    Pubg,
    Other,
}
```

```csharp
// src/LunaDns.Core/Domains/GameDomains.cs
namespace LunaDns.Core.Domains;

/// <summary>Domain được định tuyến DNS/proxy, theo nhóm. Không bao giờ chứa domain anti-cheat (spec 4.4).</summary>
public static class GameDomains
{
    private static readonly string[] Steam =
    [
        "steampowered.com", "steamcommunity.com", "steamstatic.com", "steam-chat.com",
        "steamusercontent.com", "steamgames.com", "steamcontent.com", "steamserver.net",
        "counter-strike.net", "dota2.com", "valvesoftware.com",
    ];

    // Chỉ là lobby/web; *xenuine* và accounts.pubg.com cố ý không có (spec 4.4).
    private static readonly string[] Pubg =
    [
        "playbattlegrounds.com", "pubg.com", "krafton.com", "pubgapp.com", "pubgesports.com",
    ];

    private static readonly string[] Other =
    [
        "narakathegame.com", "24entertainment.net", "playdeltaforce.com", "levelinfinite.com",
    ];

    public static IReadOnlyList<string> For(DomainGroup group) => group switch
    {
        DomainGroup.Steam => Steam,
        DomainGroup.Pubg => Pubg,
        DomainGroup.Other => Other,
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    public static IReadOnlyList<string> All { get; } = [.. Steam.Concat(Pubg).Concat(Other).Distinct()];
}
```

`accounts.pubg.com` là subdomain của `pubg.com`, nên `DomainMatcher` vẫn khớp nó. Việc loại trừ thật sự cho chế độ PUBG (hosts + 443) làm ở kế hoạch 5, bằng danh sách riêng. Test ở Step 1 chỉ đảm bảo `GameDomains` không liệt kê đích danh domain anti-cheat.

```csharp
// src/LunaDns.Core/Domains/DomainMatcher.cs
namespace LunaDns.Core.Domains;

public static class DomainMatcher
{
    public static string Normalize(string host) =>
        host.Trim().TrimEnd('.').ToLowerInvariant();

    public static bool Matches(string host, IEnumerable<string> domains)
    {
        var h = Normalize(host);
        if (h.Length == 0) return false;
        foreach (var d in domains)
        {
            if (h == d) return true;
            if (h.Length > d.Length && h.EndsWith(d, StringComparison.Ordinal) && h[h.Length - d.Length - 1] == '.')
                return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Chạy để thấy đạt**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DomainMatcherTests`
Expected: PASS, 14 test.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Domains tests/LunaDns.Core.Tests/Domains
git commit -m "feat(core): danh sách domain game và DomainMatcher"
```

---

### Task 3: Bộ tách ClientHello (tìm SNI)

**Files:**
- Create: `src/LunaDns.Core/Tls/SniLocation.cs`, `src/LunaDns.Core/Tls/ClientHelloParser.cs`
- Test: `tests/LunaDns.Core.Tests/Tls/TestClientHello.cs`, `tests/LunaDns.Core.Tests/Tls/ClientHelloParserTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct SniLocation(int Offset, int Length, string Host)`: `Offset` là chỉ số byte đầu tiên của tên host trong buffer.
  - `static class ClientHelloParser { int RecordLength(ReadOnlySpan<byte> data); bool TryFindSni(ReadOnlySpan<byte> data, out SniLocation sni); }`. `RecordLength` trả `5 + độ dài record` nếu buffer bắt đầu bằng TLS handshake record (`0x16`), ngược lại trả `-1`.
  - Helper cho test: `static class TestClientHello { byte[] Build(string? sni, int paddingLength = 0); }`

- [ ] **Step 1: Viết builder cho test**

```csharp
// tests/LunaDns.Core.Tests/Tls/TestClientHello.cs
using System.Text;

namespace LunaDns.Core.Tests.Tls;

/// <summary>Sinh ClientHello TLS tối giản nhưng đúng cấu trúc, để test parser và splitter.</summary>
internal static class TestClientHello
{
    public static byte[] Build(string? sni, int paddingLength = 0)
    {
        var body = new List<byte>();
        body.AddRange([0x03, 0x03]);                 // client_version
        body.AddRange(new byte[32]);                 // random
        body.Add(32); body.AddRange(new byte[32]);   // session_id
        body.AddRange([0x00, 0x02, 0x13, 0x01]);     // cipher_suites
        body.AddRange([0x01, 0x00]);                 // compression_methods

        var ext = new List<byte>();
        if (sni is not null)
        {
            var name = Encoding.ASCII.GetBytes(sni);
            ext.AddRange([0x00, 0x00]);              // type server_name
            ext.AddRange(U16(name.Length + 5));      // extension length
            ext.AddRange(U16(name.Length + 3));      // server_name_list length
            ext.Add(0x00);                           // name_type host_name
            ext.AddRange(U16(name.Length));
            ext.AddRange(name);
        }
        if (paddingLength > 0)
        {
            ext.AddRange([0x00, 0x15]);              // type padding
            ext.AddRange(U16(paddingLength));
            ext.AddRange(new byte[paddingLength]);
        }
        body.AddRange(U16(ext.Count));
        body.AddRange(ext);

        var hs = new List<byte> { 0x01, (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)body.Count };
        hs.AddRange(body);

        var rec = new List<byte> { 0x16, 0x03, 0x01 };
        rec.AddRange(U16(hs.Count));
        rec.AddRange(hs);
        return [.. rec];
    }

    private static byte[] U16(int v) => [(byte)(v >> 8), (byte)v];
}
```

- [ ] **Step 2: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Tls/ClientHelloParserTests.cs
using System.Text;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class ClientHelloParserTests
{
    [Fact]
    public void Finds_sni_host_and_offset()
    {
        var hello = TestClientHello.Build("store.steampowered.com");

        Assert.True(ClientHelloParser.TryFindSni(hello, out var sni));
        Assert.Equal("store.steampowered.com", sni.Host);
        Assert.Equal(sni.Host.Length, sni.Length);
        Assert.Equal(sni.Host, Encoding.ASCII.GetString(hello, sni.Offset, sni.Length));
    }

    [Fact]
    public void Finds_sni_in_hello_longer_than_one_tcp_segment()
    {
        var hello = TestClientHello.Build("steamcommunity.com", paddingLength: 2000);

        Assert.True(hello.Length > 1460);
        Assert.True(ClientHelloParser.TryFindSni(hello, out var sni));
        Assert.Equal("steamcommunity.com", sni.Host);
    }

    [Fact]
    public void Returns_false_without_sni() =>
        Assert.False(ClientHelloParser.TryFindSni(TestClientHello.Build(null, paddingLength: 16), out _));

    [Fact]
    public void Returns_false_for_non_handshake() =>
        Assert.False(ClientHelloParser.TryFindSni("GET / HTTP/1.1\r\n\r\n"u8, out _));

    [Fact]
    public void Returns_false_for_truncated_buffer()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        for (var len = 0; len < hello.Length; len++)
            Assert.False(ClientHelloParser.TryFindSni(hello.AsSpan(0, len), out _), $"len={len}");
    }

    [Fact]
    public void Record_length_reads_header()
    {
        var hello = TestClientHello.Build("a.com");
        Assert.Equal(hello.Length, ClientHelloParser.RecordLength(hello));
        Assert.Equal(hello.Length, ClientHelloParser.RecordLength(hello.AsSpan(0, 5)));
    }

    [Fact]
    public void Record_length_is_minus_one_for_non_tls()
    {
        Assert.Equal(-1, ClientHelloParser.RecordLength("GET /"u8));
        Assert.Equal(-1, ClientHelloParser.RecordLength([0x16, 0x03]));
    }
}
```

`Returns_false_for_truncated_buffer` chạy tới `hello.Length - 1`: ClientHello mẫu không có extension nào phía sau SNI, nên mọi độ dài ngắn hơn bản đầy đủ đều thiếu ít nhất byte cuối của tên host, và parser phải trả `false`.

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~ClientHelloParserTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'Tls' does not exist`.

- [ ] **Step 4: Viết code**

```csharp
// src/LunaDns.Core/Tls/SniLocation.cs
namespace LunaDns.Core.Tls;

public readonly record struct SniLocation(int Offset, int Length, string Host);
```

```csharp
// src/LunaDns.Core/Tls/ClientHelloParser.cs
using System.Text;

namespace LunaDns.Core.Tls;

public static class ClientHelloParser
{
    private const byte HandshakeRecord = 0x16;
    private const byte ClientHelloType = 0x01;

    public static int RecordLength(ReadOnlySpan<byte> data) =>
        data.Length >= 5 && data[0] == HandshakeRecord ? 5 + ((data[3] << 8) | data[4]) : -1;

    public static bool TryFindSni(ReadOnlySpan<byte> data, out SniLocation sni)
    {
        sni = default;
        if (data.Length < 9 || data[0] != HandshakeRecord || data[5] != ClientHelloType) return false;

        var p = 5 + 4 + 2 + 32;                                   // header record + handshake + version + random
        if (!Skip(data, ref p, 1)) return false;                  // session_id
        if (!Skip(data, ref p, 2)) return false;                  // cipher_suites
        if (!Skip(data, ref p, 1)) return false;                  // compression_methods
        if (!ReadU16(data, p, out var extLen)) return false;
        p += 2;
        var extEnd = p + extLen;

        while (p + 4 <= extEnd)
        {
            if (!ReadU16(data, p, out var type) || !ReadU16(data, p + 2, out var len)) return false;
            var body = p + 4;
            if (type == 0)
            {
                // server_name_list(2) + name_type(1) + name_length(2) + name
                if (body + 5 > data.Length || data[body + 2] != 0) return false;
                if (!ReadU16(data, body + 3, out var nameLen)) return false;
                var offset = body + 5;
                if (nameLen == 0 || offset + nameLen > data.Length) return false;
                sni = new SniLocation(offset, nameLen, Encoding.ASCII.GetString(data.Slice(offset, nameLen)));
                return true;
            }
            p = body + len;
        }
        return false;
    }

    // Bỏ qua trường vector: đọc độ dài kích thước prefixBytes (1 hoặc 2) rồi nhảy qua nội dung.
    private static bool Skip(ReadOnlySpan<byte> d, ref int p, int prefixBytes)
    {
        if (p + prefixBytes > d.Length) return false;
        var len = prefixBytes == 1 ? d[p] : (d[p] << 8) | d[p + 1];
        p += prefixBytes + len;
        return p <= d.Length;
    }

    private static bool ReadU16(ReadOnlySpan<byte> d, int p, out int value)
    {
        value = 0;
        if (p < 0 || p + 2 > d.Length) return false;
        value = (d[p] << 8) | d[p + 1];
        return true;
    }
}
```

- [ ] **Step 5: Chạy để thấy đạt**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~ClientHelloParserTests`
Expected: PASS, 7 test.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.Core/Tls tests/LunaDns.Core.Tests/Tls
git commit -m "feat(core): ClientHelloParser tìm SNI và độ dài record TLS"
```

---

### Task 4: Bộ cắt gói ClientHello

**Files:**
- Create: `src/LunaDns.Core/Tls/SplitMode.cs`, `src/LunaDns.Core/Tls/HelloSplitter.cs`
- Test: `tests/LunaDns.Core.Tests/Tls/HelloSplitterTests.cs`

**Interfaces:**
- Consumes: `ClientHelloParser.TryFindSni(ReadOnlySpan<byte>, out SniLocation)` (Task 3), `TestClientHello.Build` (Task 3).
- Produces:
  - `enum SplitMode { FirstByte, MidSni }`
  - `static class HelloSplitter { int[] CutPoints(ReadOnlySpan<byte> hello, SplitMode mode); IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> hello, SplitMode mode); }`. Điểm cắt luôn tăng dần và nằm trong khoảng mở `(0, hello.Length)`.

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Tls/HelloSplitterTests.cs
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class HelloSplitterTests
{
    [Fact]
    public void FirstByte_cuts_after_first_byte()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        Assert.Equal(new[] { 1 }, HelloSplitter.CutPoints(hello, SplitMode.FirstByte));
    }

    [Fact]
    public void MidSni_cuts_inside_host_name()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        ClientHelloParser.TryFindSni(hello, out var sni);

        var cut = Assert.Single(HelloSplitter.CutPoints(hello, SplitMode.MidSni));
        Assert.InRange(cut, sni.Offset + 1, sni.Offset + sni.Length - 1);
    }

    [Fact]
    public void MidSni_falls_back_to_first_byte_without_sni()
    {
        var hello = TestClientHello.Build(null, paddingLength: 8);
        Assert.Equal(new[] { 1 }, HelloSplitter.CutPoints(hello, SplitMode.MidSni));
    }

    [Fact]
    public void One_byte_input_has_no_cut() =>
        Assert.Empty(HelloSplitter.CutPoints([0x16], SplitMode.FirstByte));

    [Theory]
    [InlineData(SplitMode.FirstByte)]
    [InlineData(SplitMode.MidSni)]
    public void Pieces_concatenate_to_original(SplitMode mode)
    {
        var hello = TestClientHello.Build("steamcommunity.com", paddingLength: 100);

        var pieces = HelloSplitter.Split(hello, mode);

        Assert.Equal(2, pieces.Count);
        Assert.Equal(hello, pieces.SelectMany(p => p.ToArray()).ToArray());
    }
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~HelloSplitterTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'SplitMode' could not be found`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Tls/SplitMode.cs
namespace LunaDns.Core.Tls;

public enum SplitMode
{
    /// <summary>Cắt sau byte đầu tiên của record (mặc định, spec 4.3).</summary>
    FirstByte,
    /// <summary>Cắt giữa tên host trong SNI; không có SNI thì dùng FirstByte.</summary>
    MidSni,
}
```

```csharp
// src/LunaDns.Core/Tls/HelloSplitter.cs
namespace LunaDns.Core.Tls;

public static class HelloSplitter
{
    public static int[] CutPoints(ReadOnlySpan<byte> hello, SplitMode mode)
    {
        var cut = 1;
        if (mode == SplitMode.MidSni && ClientHelloParser.TryFindSni(hello, out var sni) && sni.Length > 1)
            cut = sni.Offset + sni.Length / 2;
        return cut > 0 && cut < hello.Length ? [cut] : [];
    }

    public static IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> hello, SplitMode mode)
    {
        var pieces = new List<ReadOnlyMemory<byte>>();
        var start = 0;
        foreach (var cut in CutPoints(hello.Span, mode))
        {
            pieces.Add(hello[start..cut]);
            start = cut;
        }
        pieces.Add(hello[start..]);
        return pieces;
    }
}
```

- [ ] **Step 4: Chạy để thấy đạt**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~HelloSplitterTests`
Expected: PASS, 6 test.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Tls tests/LunaDns.Core.Tests/Tls/HelloSplitterTests.cs
git commit -m "feat(core): HelloSplitter cắt ClientHello theo FirstByte/MidSni"
```

---

### Task 5: Sinh file PAC

**Files:**
- Create: `src/LunaDns.Core/Proxy/PacBuilder.cs`
- Test: `tests/LunaDns.Core.Tests/Proxy/PacBuilderTests.cs`

**Interfaces:**
- Consumes: `GameDomains.For(DomainGroup)` (Task 2), chỉ dùng trong test.
- Produces: `static class PacBuilder { string Build(int port, IEnumerable<string> domains); }`. Hàm ném `ArgumentException` nếu domain chứa ký tự ngoài `[a-z0-9.-]` (chống chèn mã vào JS), và ném `ArgumentOutOfRangeException` nếu port ngoài khoảng 1–65535.

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Proxy/PacBuilderTests.cs
using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;

namespace LunaDns.Core.Tests.Proxy;

public class PacBuilderTests
{
    [Fact]
    public void Contains_proxy_directive_with_port_and_direct_fallback()
    {
        var pac = PacBuilder.Build(18080, ["steampowered.com"]);

        Assert.Contains("function FindProxyForURL(url, host)", pac);
        Assert.Contains("\"PROXY 127.0.0.1:18080; DIRECT\"", pac);
        Assert.EndsWith("return \"DIRECT\";\n}\n", pac);
    }

    [Fact]
    public void Lists_every_domain_quoted()
    {
        var steam = GameDomains.For(DomainGroup.Steam);
        var pac = PacBuilder.Build(18080, steam);
        foreach (var d in steam) Assert.Contains($"\"{d}\"", pac);
    }

    [Fact]
    public void Uses_suffix_match_with_leading_dot()
    {
        var pac = PacBuilder.Build(18080, ["steampowered.com"]);
        Assert.Contains("dnsDomainIs(host, \".\" + d[i])", pac);
        Assert.Contains("host == d[i]", pac);
    }

    [Theory]
    [InlineData("evil.com\"); alert(1); //")]
    [InlineData("Steam Powered.com")]
    [InlineData("")]
    public void Rejects_unsafe_domains(string bad) =>
        Assert.Throws<ArgumentException>(() => PacBuilder.Build(18080, [bad]));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Rejects_invalid_port(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PacBuilder.Build(port, ["a.com"]));
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~PacBuilderTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'Proxy' does not exist`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Proxy/PacBuilder.cs
using System.Text;
using System.Text.RegularExpressions;

namespace LunaDns.Core.Proxy;

public static partial class PacBuilder
{
    [GeneratedRegex("^[a-z0-9.-]+$")]
    private static partial Regex SafeDomain();

    public static string Build(int port, IEnumerable<string> domains)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var list = new List<string>();
        foreach (var d in domains)
        {
            if (!SafeDomain().IsMatch(d))
                throw new ArgumentException($"Domain không hợp lệ cho PAC: '{d}'", nameof(domains));
            list.Add($"\"{d}\"");
        }

        var sb = new StringBuilder();
        sb.Append("function FindProxyForURL(url, host) {\n");
        sb.Append("  host = host.toLowerCase();\n");
        sb.Append("  var d = [").Append(string.Join(", ", list)).Append("];\n");
        sb.Append("  for (var i = 0; i < d.length; i++) {\n");
        sb.Append("    if (host == d[i] || dnsDomainIs(host, \".\" + d[i])) return \"PROXY 127.0.0.1:")
          .Append(port).Append("; DIRECT\";\n");
        sb.Append("  }\n");
        sb.Append("  return \"DIRECT\";\n");
        sb.Append("}\n");
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Chạy để thấy đạt**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~PacBuilderTests`
Expected: PASS, 8 test.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Proxy tests/LunaDns.Core.Tests/Proxy
git commit -m "feat(core): PacBuilder sinh proxy.pac chỉ cho domain game"
```

---

### Task 6: ChangeJournal (ghi và hoàn tác LIFO)

**Files:**
- Create: `src/LunaDns.Core/Journal/JournalEntry.cs`, `IJournalStore.cs`, `IChangeReverter.cs`, `RollbackResult.cs`, `ChangeJournal.cs`
- Test: `tests/LunaDns.Core.Tests/Journal/FakeReverter.cs`, `InMemoryJournalStore.cs`, `ChangeJournalTests.cs`

**Interfaces:**
- Produces:
  - `sealed record JournalEntry(Guid Id, string Feature, string Op, JsonElement Snapshot, DateTimeOffset AppliedAt)`
  - `interface IJournalStore { IReadOnlyList<JournalEntry> Load(); void Save(IReadOnlyList<JournalEntry> entries); }`
  - `interface IChangeReverter { string Op { get; } void Revert(JsonElement snapshot); }`. `Revert` phải idempotent (chạy hai lần vẫn đúng).
  - `sealed record RollbackFailure(JournalEntry Entry, Exception Error)`
  - `sealed record RollbackResult(int Reverted, IReadOnlyList<RollbackFailure> Failed)`, kèm `bool Success => Failed.Count == 0`.
  - `sealed class ChangeJournal`, với:
    - `ChangeJournal(IJournalStore store, IEnumerable<IChangeReverter> reverters, TimeProvider? time = null)`
    - `IReadOnlyList<JournalEntry> Entries`
    - `JournalEntry Record<T>(string feature, string op, T snapshot)`: lưu xuống store **trước khi** trả về.
    - `void Remove(Guid id)`
    - `RollbackResult RollbackAll()`
    - `RollbackResult Rollback(string feature)`
- Hợp đồng sử dụng (kế hoạch 4 và 5 phải tuân theo): gọi `Record` **trước**, sau đó mới đổi hệ thống. Nếu đổi hệ thống lỗi thì gọi `Rollback(feature)`.

- [ ] **Step 1: Viết hai helper giả cho test**

```csharp
// tests/LunaDns.Core.Tests/Journal/InMemoryJournalStore.cs
using LunaDns.Core.Journal;

namespace LunaDns.Core.Tests.Journal;

internal sealed class InMemoryJournalStore : IJournalStore
{
    public List<JournalEntry> Saved { get; private set; } = [];
    public int SaveCount { get; private set; }

    public IReadOnlyList<JournalEntry> Load() => [.. Saved];

    public void Save(IReadOnlyList<JournalEntry> entries)
    {
        Saved = [.. entries];
        SaveCount++;
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Journal/FakeReverter.cs
using System.Text.Json;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Tests.Journal;

/// <summary>Ghi lại giá trị đã hoàn tác. FailTimes > 0 thì ném lỗi đúng số lần đó rồi mới chạy được.</summary>
internal sealed class FakeReverter(string op) : IChangeReverter
{
    public string Op { get; } = op;
    public List<string> Reverted { get; } = [];
    public int FailTimes { get; set; }

    public void Revert(JsonElement snapshot)
    {
        if (FailTimes > 0)
        {
            FailTimes--;
            throw new InvalidOperationException("giả lập lỗi");
        }
        Reverted.Add(snapshot.GetProperty("value").GetString()!);
    }
}
```

- [ ] **Step 2: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Journal/ChangeJournalTests.cs
using LunaDns.Core.Journal;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Journal;

public class ChangeJournalTests
{
    private readonly InMemoryJournalStore _store = new();
    private readonly FakeReverter _dns = new("dns-route");
    private readonly FakeReverter _pac = new("auto-proxy");

    private ChangeJournal NewJournal() => new(_store, [_dns, _pac]);

    [Fact]
    public void Record_persists_before_returning()
    {
        var j = NewJournal();

        var e = j.Record("dns", "dns-route", new { value = "a" });

        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(e.Id, Assert.Single(_store.Saved).Id);
        Assert.Equal("a", e.Snapshot.GetProperty("value").GetString());
    }

    [Fact]
    public void Record_uses_time_provider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
        var j = new ChangeJournal(_store, [_dns], time);

        Assert.Equal(time.GetUtcNow(), j.Record("dns", "dns-route", new { value = "a" }).AppliedAt);
    }

    [Fact]
    public void RollbackAll_reverts_in_reverse_order_and_clears()
    {
        var j = NewJournal();
        j.Record("dns", "dns-route", new { value = "1" });
        j.Record("proxy", "auto-proxy", new { value = "2" });
        j.Record("dns", "dns-route", new { value = "3" });

        var r = j.RollbackAll();

        Assert.True(r.Success);
        Assert.Equal(3, r.Reverted);
        Assert.Equal(new[] { "3", "1" }, _dns.Reverted);
        Assert.Equal(new[] { "2" }, _pac.Reverted);
        Assert.Empty(j.Entries);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Failed_entries_stay_in_journal_and_others_still_revert()
    {
        var j = NewJournal();
        j.Record("dns", "dns-route", new { value = "1" });
        j.Record("proxy", "auto-proxy", new { value = "2" });
        _pac.FailTimes = 1;

        var r = j.RollbackAll();

        Assert.False(r.Success);
        Assert.Equal(1, r.Reverted);
        Assert.Equal("auto-proxy", Assert.Single(r.Failed).Entry.Op);
        Assert.Equal("auto-proxy", Assert.Single(_store.Saved).Op);

        var again = j.RollbackAll();
        Assert.True(again.Success);
        Assert.Equal(new[] { "2" }, _pac.Reverted);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Unknown_op_is_a_failure_not_a_crash()
    {
        var j = NewJournal();
        j.Record("x", "unknown-op", new { value = "1" });

        var r = j.RollbackAll();

        Assert.IsType<KeyNotFoundException>(Assert.Single(r.Failed).Error);
        Assert.Single(j.Entries);
    }

    [Fact]
    public void Rollback_feature_only_touches_that_feature()
    {
        var j = NewJournal();
        j.Record("dns", "dns-route", new { value = "1" });
        j.Record("proxy", "auto-proxy", new { value = "2" });

        var r = j.Rollback("dns");

        Assert.Equal(1, r.Reverted);
        Assert.Equal(new[] { "1" }, _dns.Reverted);
        Assert.Empty(_pac.Reverted);
        Assert.Equal("proxy", Assert.Single(j.Entries).Feature);
    }

    [Fact]
    public void Remove_deletes_entry_and_persists()
    {
        var j = NewJournal();
        var e = j.Record("dns", "dns-route", new { value = "1" });

        j.Remove(e.Id);

        Assert.Empty(j.Entries);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Loads_existing_entries_from_store_on_construction()
    {
        NewJournal().Record("dns", "dns-route", new { value = "1" });   // phiên trước "crash"

        var r = NewJournal().RollbackAll();                              // phiên mới

        Assert.Equal(1, r.Reverted);
        Assert.Equal(new[] { "1" }, _dns.Reverted);
    }
}
```

Thêm package cho `FakeTimeProvider`:

```bash
dotnet add tests/LunaDns.Core.Tests package Microsoft.Extensions.TimeProvider.Testing
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~ChangeJournalTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'Journal' does not exist`.

- [ ] **Step 4: Viết code**

```csharp
// src/LunaDns.Core/Journal/JournalEntry.cs
using System.Text.Json;

namespace LunaDns.Core.Journal;

public sealed record JournalEntry(Guid Id, string Feature, string Op, JsonElement Snapshot, DateTimeOffset AppliedAt);
```

```csharp
// src/LunaDns.Core/Journal/IJournalStore.cs
namespace LunaDns.Core.Journal;

public interface IJournalStore
{
    IReadOnlyList<JournalEntry> Load();
    void Save(IReadOnlyList<JournalEntry> entries);
}
```

```csharp
// src/LunaDns.Core/Journal/IChangeReverter.cs
using System.Text.Json;

namespace LunaDns.Core.Journal;

/// <summary>Hoàn tác một loại thay đổi hệ thống. Revert phải idempotent.</summary>
public interface IChangeReverter
{
    string Op { get; }
    void Revert(JsonElement snapshot);
}
```

```csharp
// src/LunaDns.Core/Journal/RollbackResult.cs
namespace LunaDns.Core.Journal;

public sealed record RollbackFailure(JournalEntry Entry, Exception Error);

public sealed record RollbackResult(int Reverted, IReadOnlyList<RollbackFailure> Failed)
{
    public bool Success => Failed.Count == 0;
}
```

```csharp
// src/LunaDns.Core/Journal/ChangeJournal.cs
using System.Text.Json;

namespace LunaDns.Core.Journal;

/// <summary>
/// Nhật ký thay đổi hệ thống (spec 4.5). Gọi Record TRƯỚC khi đổi hệ thống;
/// hoàn tác chạy LIFO, mục lỗi được giữ lại để lần sau thử tiếp.
/// </summary>
public sealed class ChangeJournal
{
    private readonly IJournalStore _store;
    private readonly Dictionary<string, IChangeReverter> _reverters;
    private readonly TimeProvider _time;
    private readonly List<JournalEntry> _entries;
    private readonly object _gate = new();

    public ChangeJournal(IJournalStore store, IEnumerable<IChangeReverter> reverters, TimeProvider? time = null)
    {
        _store = store;
        _reverters = reverters.ToDictionary(r => r.Op, StringComparer.Ordinal);
        _time = time ?? TimeProvider.System;
        _entries = [.. store.Load()];
    }

    public IReadOnlyList<JournalEntry> Entries
    {
        get { lock (_gate) return [.. _entries]; }
    }

    public JournalEntry Record<T>(string feature, string op, T snapshot)
    {
        var entry = new JournalEntry(Guid.NewGuid(), feature, op, JsonSerializer.SerializeToElement(snapshot), _time.GetUtcNow());
        lock (_gate)
        {
            _entries.Add(entry);
            _store.Save(_entries);
        }
        return entry;
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Id == id) > 0) _store.Save(_entries);
        }
    }

    public RollbackResult RollbackAll() => RollbackWhere(_ => true);

    public RollbackResult Rollback(string feature) =>
        RollbackWhere(e => string.Equals(e.Feature, feature, StringComparison.Ordinal));

    private RollbackResult RollbackWhere(Func<JournalEntry, bool> predicate)
    {
        lock (_gate)
        {
            var reverted = 0;
            var failed = new List<RollbackFailure>();
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (!predicate(entry)) continue;
                try
                {
                    if (!_reverters.TryGetValue(entry.Op, out var reverter))
                        throw new KeyNotFoundException($"Không có reverter cho thao tác '{entry.Op}'.");
                    reverter.Revert(entry.Snapshot);
                    _entries.RemoveAt(i);
                    _store.Save(_entries);
                    reverted++;
                }
                catch (Exception ex)
                {
                    failed.Add(new RollbackFailure(entry, ex));
                }
            }
            return new RollbackResult(reverted, failed);
        }
    }
}
```

Lưu sau **mỗi** bước hoàn tác thành công, để nếu crash giữa chừng thì mục đã hoàn tác không bị chạy lại ở lần sau. Mục chưa hoàn tác vẫn còn trong journal.

- [ ] **Step 5: Chạy để thấy đạt**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~ChangeJournalTests`
Expected: PASS, 8 test.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.Core/Journal tests/LunaDns.Core.Tests
git commit -m "feat(core): ChangeJournal ghi trước, hoàn tác LIFO, giữ mục lỗi"
```

---

### Task 7: JsonFileJournalStore (ghi nguyên tử, xử lý file hỏng)

**Files:**
- Create: `src/LunaDns.Core/Journal/JsonFileJournalStore.cs`
- Test: `tests/LunaDns.Core.Tests/Journal/JsonFileJournalStoreTests.cs`

**Interfaces:**
- Consumes: `IJournalStore`, `JournalEntry` (Task 6).
- Produces: `sealed class JsonFileJournalStore(string path) : IJournalStore`.
  - `Load()`: file không tồn tại thì trả rỗng. File hỏng (JSON lỗi) thì đổi tên thành `<path>.corrupt-<yyyyMMddHHmmss>` rồi trả rỗng.
  - `Save()`: ghi ra `<path>.tmp`, sau đó `File.Replace` (hoặc `File.Move` nếu file đích chưa có).
  - Thư mục cha phải tồn tại sẵn: ACL của thư mục do kế hoạch 4 tạo, store không tự tạo thư mục.

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Journal/JsonFileJournalStoreTests.cs
using System.Text.Json;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Tests.Journal;

public sealed class JsonFileJournalStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-test-").FullName;
    private string PathOf(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static JournalEntry Entry(string value) =>
        new(Guid.NewGuid(), "dns", "dns-route", JsonSerializer.SerializeToElement(new { value }),
            new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Load_missing_file_returns_empty() =>
        Assert.Empty(new JsonFileJournalStore(PathOf("journal.json")).Load());

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonFileJournalStore(PathOf("journal.json"));
        var a = Entry("a");
        var b = Entry("b");

        store.Save([a, b]);
        var loaded = new JsonFileJournalStore(PathOf("journal.json")).Load();

        Assert.Equal(new[] { a.Id, b.Id }, loaded.Select(e => e.Id));
        Assert.Equal("b", loaded[1].Snapshot.GetProperty("value").GetString());
        Assert.Equal(a.AppliedAt, loaded[0].AppliedAt);
    }

    [Fact]
    public void Save_overwrites_and_leaves_no_tmp_file()
    {
        var store = new JsonFileJournalStore(PathOf("journal.json"));
        store.Save([Entry("a"), Entry("b")]);
        store.Save([Entry("c")]);

        Assert.Single(store.Load());
        Assert.False(File.Exists(PathOf("journal.json.tmp")));
    }

    [Fact]
    public void Corrupt_file_is_quarantined_and_load_returns_empty()
    {
        File.WriteAllText(PathOf("journal.json"), "{ không phải json");

        var loaded = new JsonFileJournalStore(PathOf("journal.json")).Load();

        Assert.Empty(loaded);
        Assert.False(File.Exists(PathOf("journal.json")));
        Assert.Single(Directory.GetFiles(_dir, "journal.json.corrupt-*"));
    }

    [Fact]
    public void Save_empty_list_writes_empty_array()
    {
        var store = new JsonFileJournalStore(PathOf("journal.json"));
        store.Save([]);
        Assert.Equal("[]", File.ReadAllText(PathOf("journal.json")).Trim());
    }
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~JsonFileJournalStoreTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'JsonFileJournalStore' could not be found`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Journal/JsonFileJournalStore.cs
using System.Globalization;
using System.Text.Json;

namespace LunaDns.Core.Journal;

/// <summary>Lưu journal ra file JSON, ghi nguyên tử (tmp rồi replace). Thư mục cha phải có sẵn.</summary>
public sealed class JsonFileJournalStore(string path) : IJournalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public IReadOnlyList<JournalEntry> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<List<JournalEntry>>(stream, Options) ?? [];
        }
        catch (JsonException)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return [];
        }
    }

    public void Save(IReadOnlyList<JournalEntry> entries)
    {
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, entries, Options);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);
    }
}
```

- [ ] **Step 4: Chạy để thấy đạt**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~JsonFileJournalStoreTests`
Expected: PASS, 5 test.

- [ ] **Step 5: Chạy toàn bộ bộ kiểm tra**

Run: `dotnet build -warnaserror && dotnet test tests/LunaDns.Core.Tests`
Expected: `Build succeeded.` với `0 Warning(s)`, và `Passed!` với 48 test.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.Core/Journal/JsonFileJournalStore.cs tests/LunaDns.Core.Tests/Journal/JsonFileJournalStoreTests.cs
git commit -m "feat(core): JsonFileJournalStore ghi nguyên tử, cách ly file hỏng"
```

---

### Task 8: Ba spike chốt rủi ro (PO cùng làm, cần quyền Admin, ~30 phút)

Spike không theo TDD. Kết quả ghi vào `docs/spikes/2026-11-spike-results.md` để kế hoạch 2, 4 và 5 dựa vào.

**Files:**
- Create: `spikes/nrpt-spike.ps1`, `docs/spikes/2026-11-spike-results.md`

- [ ] **Step 1: Viết `spikes/nrpt-spike.ps1`**

```powershell
# Chạy bằng PowerShell (Admin). Kiểm tra NRPT local + nameserver 127.0.0.1 trên máy hiện tại.
$ErrorActionPreference = 'Stop'
$ns = '.lunadns-spike.test'
"Windows: $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)"

# 1. Responder DNS tối giản trên 127.0.0.1:53, trả A 10.9.8.7 cho mọi câu hỏi
$job = Start-Job {
    $udp = [System.Net.Sockets.UdpClient]::new([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 53))
    $ep = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
    $q = $udp.Receive([ref]$ep)
    $r = [byte[]]$q.Clone()
    $r[2] = 0x81; $r[3] = 0x80; $r[7] = 1                     # QR=1 RD RA, ANCOUNT=1
    $ans = [byte[]](0xC0,0x0C, 0,1, 0,1, 0,0,0,60, 0,4, 10,9,8,7)
    $udp.Send($r + $ans, $r.Length + $ans.Length, $ep) | Out-Null
    $udp.Close()
}
Start-Sleep -Milliseconds 500

# 2. Thêm quy tắc NRPT trỏ namespace về 127.0.0.1
Add-DnsClientNrptRule -Namespace $ns -NameServers '127.0.0.1' -Comment 'LunaDNS spike' | Out-Null
Clear-DnsClientCache
try {
    $res = Resolve-DnsName "probe$ns" -Type A -DnsOnly -QuickTimeout
    "KẾT QUẢ: $($res.IPAddress)   (mong đợi 10.9.8.7 = NRPT hoạt động)"
} catch { "KẾT QUẢ: LỖI - $($_.Exception.Message)" }
finally {
    Get-DnsClientNrptRule | Where-Object Comment -eq 'LunaDNS spike' | ForEach-Object { Remove-DnsClientNrptRule -Name $_.Name -Force }
    Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force
    "Còn quy tắc spike: $(@(Get-DnsClientNrptRule | Where-Object Comment -eq 'LunaDNS spike').Count) (mong đợi 0)"
}
```

Nếu cổng 53 đã có tiến trình khác dùng, Start-Job sẽ báo lỗi bind. Khi đó ghi lại tên tiến trình bằng lệnh `Get-NetUDPEndpoint -LocalPort 53 | Select OwningProcess`.

- [ ] **Step 2: Chạy spike NRPT** trên máy hiện tại, và nếu có thì thêm một máy hoặc máy ảo **Windows 10/11 Home**:

Run (PowerShell Admin): `powershell -ExecutionPolicy Bypass -File spikes\nrpt-spike.ps1`
Expected: `KẾT QUẢ: 10.9.8.7` và `Còn quy tắc spike: 0`. Nếu ra kết quả khác thì kế hoạch 4 chuyển sang phương án B ở spec mục 10.1.

- [ ] **Step 3: Spike Steam đọc PAC (làm tay)**
  1. Tạo file `C:\Temp\spike.pac` với nội dung `function FindProxyForURL(u,h){ if (dnsDomainIs(h,".steampowered.com")||dnsDomainIs(h,".steamcommunity.com")||dnsDomainIs(h,".steamserver.net")) return "PROXY 127.0.0.1:18999"; return "DIRECT"; }`. Cổng 18999 không có gì lắng nghe, nên app nào đọc PAC sẽ lỗi.
  2. Vào Windows Settings, chọn Proxy, mục "Use setup script", điền `file:///C:/Temp/spike.pac`, rồi Save.
  3. Thoát hẳn Steam và mở lại. Ghi lại: (a) Store và Community trong Steam có vào được không, (b) Steam có đăng nhập được không (kết nối CM).
  4. Tắt "Use setup script".
  - Cách đọc kết quả: (a) lỗi nghĩa là CEF có đọc PAC. (b) vẫn đăng nhập được nghĩa là kết nối CM **không** đọc PAC, nên chỉ có định tuyến DNS mới tác động được tới CM.

- [ ] **Step 4: Spike kích thước Avalonia**

```bash
dotnet new install Avalonia.Templates
dotnet new avalonia.app -o /c/Temp/avsize -n AvSize
dotnet publish /c/Temp/avsize -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true -o /c/Temp/avsize/out
ls -la /c/Temp/avsize/out/*.exe
```

Expected: có một file `AvSize.exe`. Ghi lại kích thước. Nếu lớn hơn 80 MB thì áp dụng spec mục 10.3. Nếu trimming báo lỗi, thử lại không có `-p:PublishTrimmed=true` và ghi cả hai kích thước. Xong thì xoá `C:\Temp\avsize`.

- [ ] **Step 5: Ghi kết quả vào `docs/spikes/2026-11-spike-results.md`**

```markdown
# Kết quả spike — 2026-11

| Spike | Máy / bản Windows | Kết quả | Quyết định cho kế hoạch sau |
|---|---|---|---|
| NRPT local + 127.0.0.1 | <bản Windows từ dòng đầu của script> | <10.9.8.7 hay lỗi> | <giữ NRPT, hoặc phương án B> |
| Steam đọc PAC | Steam <phiên bản> | CEF: <đọc/không>, CM: <đọc/không> | <ghi giới hạn vào FAQ nếu CM không đọc> |
| Kích thước Avalonia | win-x64 | trimmed <MB>, untrimmed <MB> | <single-file, hoặc framework-dependent> |
```

Phần trong ngoặc nhọn là ô để điền kết quả đo được, không phải placeholder của kế hoạch. Mỗi ô phải được điền trước khi commit.

- [ ] **Step 6: Commit**

```bash
git add spikes/nrpt-spike.ps1 docs/spikes/2026-11-spike-results.md
git commit -m "docs: kết quả spike NRPT, PAC của Steam, kích thước Avalonia"
```

---

## Đối chiếu spec (tự rà soát)

| Mục spec | Kế hoạch 1 | Nơi làm phần còn lại |
|---|---|---|
| 3. Cấu trúc solution, quy tắc phụ thuộc | Task 1 (Core, Tests) | KH4 (Platform.Windows), KH5 (App) |
| 4.2 `GameDomains`, bỏ domain anti-cheat | Task 2 | KH2 (stub resolver) |
| 4.3 cắt gói, PAC chỉ domain game | Task 3–5 | KH3 (listener, CONNECT, 403) |
| 4.4 không chuyển hướng xenuine/accounts | Task 2 (danh sách) | KH5 (danh sách riêng cho hosts) |
| 4.5 Journal ghi trước, LIFO, idempotent, ghi nguyên tử | Task 6–7 | KH4 (các reverter thật) |
| 6. không `catch {}` rỗng | Global Constraints | mọi kế hoạch |
| 9. unit test parser, splitter, PAC, journal | Task 2–7 | KH2 (DoH), KH3 (Prober), KH6 (manifest) |
| 10. ba spike | Task 8 | — |
| 4.1, 4.6–4.10, 5, 7, 8 | — | KH2–KH7 theo bảng lộ trình |
