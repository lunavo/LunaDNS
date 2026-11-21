# D2: Thêm máy chủ DoH và đo độ trễ — Kế hoạch triển khai

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thêm Quad9, AdGuard và URL DoH tự nhập vào ô "Máy chủ DoH", cùng nút "Đo" chọn máy nhanh nhất trên mạng người dùng.

**Architecture:** Lựa chọn là `enum DohProvider` lưu trong `AppSettings.Doh` (nullable; `null` thì suy từ `PreferGoogleDoh` cũ). `DohCatalog` giữ 4 máy dựng sẵn và dựng chuỗi 3 máy. URL tự nhập là `CustomDoh` (URL + IP tay + IP đã phân giải); `CustomDohParser` (hàm thuần) kiểm URL/IP và dựng `DohServer`; `CustomDohBootstrap` lấy IP qua chuỗi DoH dựng sẵn, không bao giờ hỏi DNS hệ thống. `DohLatencyMeter` đo song song bằng một `IDnsUpstream` riêng cho mỗi máy. `AppSettings.DohServers()` vẫn đồng bộ, nên `DohClient`, `CoreFeatureServices`, daemon macOS không đổi.

**Tech Stack:** .NET 8, C# 12, xUnit, Avalonia 11.3.23, System.Text.Json source generator (`SettingsJsonContext`).

**Spec:** [2026-11-21-lunadns-doh-providers-design.md](../specs/2026-11-21-lunadns-doh-providers-design.md)

## Global Constraints

- Không bao giờ phân giải tên máy chủ DoH qua DNS hệ thống. Mọi kết nối DoH đi qua `BootstrapAddresses`; TLS vẫn kiểm chứng chỉ theo host của URL.
- `settings.json` cũ (không có `Doh`) phải giữ đúng lựa chọn cũ: `PreferGoogleDoh = true` → Google, còn lại → Cloudflare. Ghi `Doh` thì đặt luôn `PreferGoogleDoh = (Doh == Google)`.
- Chuỗi DoH tối đa **3** máy: máy chính + 2 máy dựng sẵn đầu tiên theo thứ tự danh mục (Cloudflare, Google, Quad9, AdGuard), bỏ máy trùng host.
- Hai máy mới dùng biến thể không lọc: Quad9 `https://dns.quad9.net/dns-query` (9.9.9.9, 149.112.112.112), AdGuard `https://unfiltered.adguard-dns.com/dns-query` (94.140.14.140, 94.140.14.141).
- Đo: 1 truy vấn khởi động + 3 truy vấn A cho `store.steampowered.com`; điểm = trung vị 3 truy vấn sau. Kết quả đo không ghi vào `settings.json`.
- Giao diện chỉ hiện trên Windows (mục Nâng cao ẩn trên macOS qua `PlatformInfo.HasAdvancedSettings()`). Không sửa giao thức daemon macOS (`DaemonProtocol`). Core dùng chung cho cả hai nền tảng.
- Build: `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended` (Directory.Build.props). Code mới không được sinh cảnh báo.
- Chú thích và chuỗi giao diện bằng tiếng Việt, theo giọng các file xung quanh. Commit và PR bằng tiếng Anh, **không** thêm `Co-Authored-By` hay dòng "Generated with Claude Code".
- `dotnet` có thể không nằm trong PATH của shell agent: `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet`.

## Nhánh và thứ tự

Làm ở worktree riêng, nhánh `feat/doh-providers` tách từ `main` (D1 đã merge). Nhánh `docs/d2-doh-providers` chỉ chứa spec và kế hoạch này; merge nó trước hoặc cherry-pick vào nhánh code. Merge qua PR để CI chạy cả Windows và macOS (D1 từng hỏng CI Windows vì merge thẳng).

## Điều chỉnh so với spec

Kế hoạch chốt ba chi tiết spec để mở; spec đã được sửa theo, cùng commit với kế hoạch này:

1. `CustomDoh` dùng thuộc tính `init` có mặc định `[]` thay vì record positional, để JSON thiếu trường không ra `null`.
2. `DohCatalog.Choices` nhận `CustomDoh?` (không nhận `AppSettings`) và trả `DohChoice(DohProvider Provider, DohServer Server)`; `AppSettings.DohChoices()` gọi nó. Tránh để `Core.Dns` phụ thuộc `Core.Settings`.
3. `AppSettings` không có logger, nên `Doh = Custom` mà URL hỏng không ghi log; dòng trạng thái của giao diện báo "đang dùng Cloudflare".

## Bản đồ file

| File | Trách nhiệm | Task |
|---|---|---|
| `src/LunaDns.Core/Dns/CustomDoh.cs` (mới) | URL tự nhập + IP tay + IP đã phân giải | 1 |
| `src/LunaDns.Core/Dns/CustomDohParser.cs` (mới) | Kiểm URL, IP; dựng `DohServer` | 1 |
| `src/LunaDns.Core/Dns/DohProvider.cs` (mới) | Enum lựa chọn + `DohChoice` | 2 |
| `src/LunaDns.Core/Dns/DohServer.cs` | Thêm `Quad9`, `AdGuard` | 2 |
| `src/LunaDns.Core/Dns/DohCatalog.cs` (mới) | Danh mục, `Chain`, `Choices` | 2 |
| `src/LunaDns.Core/Dns/DohClient.cs` | `ConnectToBootstrapAsync` so cả `DnsSafeHost` (host IPv6 literal) | 2 |
| `src/LunaDns.Core/Settings/AppSettings.cs`, `SettingsJsonContext.cs` | `Doh`, `CustomDoh`, `WithDoh`, `DohServers()`, `BootstrapDohServers()`, `DohChoices()` | 3 |
| `src/LunaDns.Core/Dns/CustomDohBootstrap.cs` (mới) | Lấy IP cho URL tự nhập qua `IHostResolver` | 4 |
| `src/LunaDns.Core/Dns/DohLatencyMeter.cs` (mới) | Đo độ trễ, `DohLatency` | 5 |
| `src/LunaDns.App/AppServices.cs` | `ResolveCustomDohAsync`, `MeasureDohAsync` | 6 |
| `src/LunaDns.App/ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml`, `Strings.cs`, `Strings.resx` | Giao diện | 7 |
| `docs/guide/05-nang-cao.md` | Hướng dẫn | 7 |

Lệnh test dùng trong mọi task (chạy ở gốc repo):

```bash
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet
dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~<TênLớpTest>"
```

---

### Task 1: `CustomDoh` và `CustomDohParser`

**Files:**
- Create: `src/LunaDns.Core/Dns/CustomDoh.cs`
- Create: `src/LunaDns.Core/Dns/CustomDohParser.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/CustomDohParserTests.cs`

**Interfaces:**
- Consumes: `DohServer(string Name, Uri Endpoint, IReadOnlyList<IPAddress> BootstrapAddresses)` (đã có).
- Produces:
  - `sealed record CustomDoh { string Endpoint; IReadOnlyList<string> ManualAddresses; IReadOnlyList<string> ResolvedAddresses; }` (init, mặc định `""`, `[]`, `[]`).
  - `static class CustomDohParser`:
    - `const string ServerName = "Tự nhập"`
    - `bool TryParseEndpoint(string? text, [NotNullWhen(true)] out Uri? endpoint)`
    - `bool TryParseAddresses(string? text, out IReadOnlyList<IPAddress> addresses)` (ô trống → `true`, danh sách rỗng)
    - `IPAddress? LiteralAddress(Uri endpoint)`
    - `DohServer? ToServer(CustomDoh? custom)`

- [ ] **Step 1: Viết test (đỏ)**

```csharp
using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public sealed class CustomDohParserTests
{
    [Theory]
    [InlineData("https://dns.example.net/dns-query")]
    [InlineData("  https://dns.example.net:8443/q  ")]
    [InlineData("https://9.9.9.9/dns-query")]
    [InlineData("https://[2620:fe::fe]/dns-query")]
    public void Accepts_https_urls(string text) =>
        Assert.True(CustomDohParser.TryParseEndpoint(text, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dns.example.net/dns-query")]
    [InlineData("http://dns.example.net/dns-query")]
    [InlineData("https://user:pass@dns.example.net/dns-query")]
    [InlineData("https://dns.example.net/dns-query#x")]
    [InlineData("ftp://dns.example.net/")]
    public void Rejects_bad_urls(string? text) =>
        Assert.False(CustomDohParser.TryParseEndpoint(text, out _));

    [Fact]
    public void Addresses_split_on_commas_and_whitespace()
    {
        Assert.True(CustomDohParser.TryParseAddresses("1.2.3.4, 5.6.7.8\n2620:fe::fe", out var addresses));
        Assert.Equal(["1.2.3.4", "5.6.7.8", "2620:fe::fe"], addresses.Select(a => a.ToString()));
    }

    [Fact]
    public void Empty_address_box_is_valid_and_empty()
    {
        Assert.True(CustomDohParser.TryParseAddresses("  ", out var addresses));
        Assert.Empty(addresses);
    }

    [Theory]
    [InlineData("1.2.3.4, abc")]
    [InlineData("1")]          // IPAddress.TryParse nhận "1" thành 0.0.0.1: phải từ chối
    [InlineData("300.1.1.1")]
    public void Any_bad_address_rejects_the_whole_box(string text) =>
        Assert.False(CustomDohParser.TryParseAddresses(text, out _));

    [Fact]
    public void Literal_address_comes_from_ip_host()
    {
        Assert.Equal(IPAddress.Parse("9.9.9.9"), CustomDohParser.LiteralAddress(new Uri("https://9.9.9.9/dns-query")));
        Assert.Equal(IPAddress.Parse("2620:fe::fe"), CustomDohParser.LiteralAddress(new Uri("https://[2620:fe::fe]/dns-query")));
        Assert.Null(CustomDohParser.LiteralAddress(new Uri("https://dns.quad9.net/dns-query")));
    }

    [Fact]
    public void ToServer_prefers_manual_then_literal_then_resolved()
    {
        var manual = new CustomDoh { Endpoint = "https://9.9.9.9/q", ManualAddresses = ["1.1.1.1"], ResolvedAddresses = ["2.2.2.2"] };
        var literal = manual with { ManualAddresses = [] };
        var resolved = new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["2.2.2.2"] };

        Assert.Equal("1.1.1.1", Assert.Single(CustomDohParser.ToServer(manual)!.BootstrapAddresses).ToString());
        Assert.Equal("9.9.9.9", Assert.Single(CustomDohParser.ToServer(literal)!.BootstrapAddresses).ToString());
        var server = CustomDohParser.ToServer(resolved)!;
        Assert.Equal("2.2.2.2", Assert.Single(server.BootstrapAddresses).ToString());
        Assert.Equal(CustomDohParser.ServerName, server.Name);
        Assert.Equal(new Uri("https://dns.example.net/q"), server.Endpoint);
    }

    [Fact]
    public void ToServer_is_null_without_valid_url_or_any_address()
    {
        Assert.Null(CustomDohParser.ToServer(null));
        Assert.Null(CustomDohParser.ToServer(new CustomDoh { Endpoint = "http://x/q", ResolvedAddresses = ["1.1.1.1"] }));
        Assert.Null(CustomDohParser.ToServer(new CustomDoh { Endpoint = "https://dns.example.net/q" }));
    }

    [Fact]
    public void ToServer_skips_hand_edited_garbage_and_nulls()
    {
        var custom = new CustomDoh { Endpoint = "https://dns.example.net/q", ManualAddresses = null!, ResolvedAddresses = ["x", "2.2.2.2"] };

        Assert.Equal("2.2.2.2", Assert.Single(CustomDohParser.ToServer(custom)!.BootstrapAddresses).ToString());
    }
}
```

- [ ] **Step 2: Chạy test, thấy lỗi biên dịch** (`CustomDoh`/`CustomDohParser` chưa có).

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~CustomDohParserTests"`

- [ ] **Step 3: Viết `CustomDoh.cs`**

```csharp
namespace LunaDns.Core.Dns;

/// <summary>
/// URL DoH tự nhập (D2). IP lưu dạng chuỗi cho gọn JSON; <see cref="CustomDohParser.ToServer"/> parse lúc dựng máy chủ.
/// <see cref="ResolvedAddresses"/> lấy qua DoH dựng sẵn lúc bấm Lưu hoặc Đo, không bao giờ qua DNS hệ thống.
/// </summary>
public sealed record CustomDoh
{
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Ô "IP (tuỳ chọn)". Có thì dùng, không hỏi DoH.</summary>
    public IReadOnlyList<string> ManualAddresses { get; init; } = [];

    public IReadOnlyList<string> ResolvedAddresses { get; init; } = [];
}
```

- [ ] **Step 4: Viết `CustomDohParser.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace LunaDns.Core.Dns;

/// <summary>Kiểm URL và IP của DoH tự nhập, dựng <see cref="DohServer"/>. Hàm thuần, không đụng mạng.</summary>
public static class CustomDohParser
{
    public const string ServerName = "Tự nhập";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    public static bool TryParseEndpoint(string? text, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)) return false;
        if (uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return false;
        endpoint = uri;
        return true;
    }

    /// <summary>IPv4/IPv6 ngăn bằng dấu phẩy, chấm phẩy hoặc khoảng trắng. Một giá trị sai thì lỗi cả ô.</summary>
    public static bool TryParseAddresses(string? text, out IReadOnlyList<IPAddress> addresses)
    {
        var parsed = new List<IPAddress>();
        addresses = parsed;
        foreach (var token in (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Parse(token) is not { } address) return false;
            parsed.Add(address);
        }
        return true;
    }

    public static IPAddress? LiteralAddress(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? IPAddress.Parse(endpoint.DnsSafeHost)
            : null;
    }

    /// <summary>IP tay → IP literal của host → IP đã phân giải. URL hỏng hoặc không có IP nào thì <c>null</c>.</summary>
    public static DohServer? ToServer(CustomDoh? custom)
    {
        if (custom is null || !TryParseEndpoint(custom.Endpoint, out var endpoint)) return null;
        IReadOnlyList<IPAddress> addresses = ParseAll(custom.ManualAddresses);
        if (addresses.Count == 0 && LiteralAddress(endpoint) is { } literal) addresses = [literal];
        if (addresses.Count == 0) addresses = ParseAll(custom.ResolvedAddresses);
        return addresses.Count == 0 ? null : new DohServer(ServerName, endpoint, addresses);
    }

    /// <summary>Dữ liệu từ settings.json (có thể sửa tay): bỏ giá trị hỏng thay vì lỗi.</summary>
    private static List<IPAddress> ParseAll(IReadOnlyList<string>? values) =>
        [.. (values ?? []).Select(Parse).OfType<IPAddress>()];

    /// <summary><see cref="IPAddress.TryParse(string, out IPAddress)"/> nhận cả "1" (0.0.0.1): bắt buộc có '.' hoặc ':'.</summary>
    private static IPAddress? Parse(string? token) =>
        token is not null && token.AsSpan().IndexOfAny('.', ':') >= 0
        && IPAddress.TryParse(token, out var address)
        && address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
            ? address
            : null;
}
```

- [ ] **Step 5: Chạy test, thấy xanh.** Cùng lệnh Step 2. Expected: PASS hết.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.Core/Dns/CustomDoh.cs src/LunaDns.Core/Dns/CustomDohParser.cs tests/LunaDns.Core.Tests/Dns/CustomDohParserTests.cs
git commit -m "feat(core): parse custom DoH URL and bootstrap addresses"
```

---

### Task 2: Danh mục DoH (`DohProvider`, `DohServer`, `DohCatalog`)

**Files:**
- Create: `src/LunaDns.Core/Dns/DohProvider.cs`
- Create: `src/LunaDns.Core/Dns/DohCatalog.cs`
- Modify: `src/LunaDns.Core/Dns/DohServer.cs`
- Modify: `src/LunaDns.Core/Dns/DohClient.cs` (`ConnectToBootstrapAsync`, dòng tìm `server`)
- Test: `tests/LunaDns.Core.Tests/Dns/DohCatalogTests.cs`
- Modify test: `tests/LunaDns.Core.Tests/Dns/DohClientNetworkTests.cs`

**Interfaces:**
- Consumes: `CustomDoh`, `CustomDohParser.ToServer` (Task 1).
- Produces:
  - `enum DohProvider { Cloudflare, Google, Quad9, AdGuard, Custom }`
  - `sealed record DohChoice(DohProvider Provider, DohServer Server)`
  - `DohServer.Quad9`, `DohServer.AdGuard` (`DohServer.Defaults` giữ nguyên: Cloudflare, Google)
  - `static class DohCatalog`:
    - `const int ChainLength = 3`
    - `IReadOnlyList<DohChoice> BuiltIn`
    - `DohServer Get(DohProvider provider)` (ném `ArgumentOutOfRangeException` với `Custom`)
    - `IReadOnlyList<DohServer> Chain(DohServer primary)`
    - `IReadOnlyList<DohChoice> Choices(CustomDoh? custom)`

- [ ] **Step 1: Viết test (đỏ)**

```csharp
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public sealed class DohCatalogTests
{
    private static string Names(IEnumerable<DohServer> servers) => string.Join(",", servers.Select(s => s.Name));

    [Fact]
    public void Built_in_order_is_cloudflare_google_quad9_adguard()
    {
        Assert.Equal([DohProvider.Cloudflare, DohProvider.Google, DohProvider.Quad9, DohProvider.AdGuard],
            DohCatalog.BuiltIn.Select(c => c.Provider));
        Assert.Equal(new Uri("https://dns.quad9.net/dns-query"), DohServer.Quad9.Endpoint);
        Assert.Equal(new Uri("https://unfiltered.adguard-dns.com/dns-query"), DohServer.AdGuard.Endpoint);
    }

    [Theory]
    [InlineData(DohProvider.Cloudflare, "Cloudflare,Google,Quad9")]
    [InlineData(DohProvider.Google, "Google,Cloudflare,Quad9")]
    [InlineData(DohProvider.Quad9, "Quad9,Cloudflare,Google")]
    [InlineData(DohProvider.AdGuard, "AdGuard,Cloudflare,Google")]
    public void Chain_is_primary_then_first_two_other_built_ins(DohProvider provider, string expected) =>
        Assert.Equal(expected, Names(DohCatalog.Chain(DohCatalog.Get(provider))));

    [Fact]
    public void Chain_for_custom_puts_it_first()
    {
        var custom = CustomDohParser.ToServer(new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] })!;

        Assert.Equal("Tự nhập,Cloudflare,Google", Names(DohCatalog.Chain(custom)));
    }

    [Fact]
    public void Chain_drops_built_in_with_same_host_as_custom()
    {
        var custom = CustomDohParser.ToServer(new CustomDoh { Endpoint = "https://CLOUDFLARE-DNS.com/dns-query", ManualAddresses = ["1.1.1.1"] })!;

        Assert.Equal("Tự nhập,Google,Quad9", Names(DohCatalog.Chain(custom)));
    }

    [Fact]
    public void Get_rejects_custom() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DohCatalog.Get(DohProvider.Custom));

    [Fact]
    public void Choices_add_custom_only_when_it_has_an_address()
    {
        Assert.Equal(4, DohCatalog.Choices(null).Count);
        Assert.Equal(4, DohCatalog.Choices(new CustomDoh { Endpoint = "https://dns.example.net/q" }).Count);

        var choices = DohCatalog.Choices(new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] });
        Assert.Equal(DohProvider.Custom, choices[^1].Provider);
        Assert.Equal(5, choices.Count);
    }
}
```

Sửa `DohClientNetworkTests` để chạy qua cả danh mục:

```csharp
    [NetworkFact]
    public async Task Each_catalog_server_answers_via_bootstrap_ip()
    {
        foreach (var server in DohCatalog.BuiltIn.Select(c => c.Server))
        {
            using var client = DohClient.Create([server]);
            var response = await client.QueryAsync(TestDnsMessage.Query("store.steampowered.com"), CancellationToken.None);

            Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(response));
            Assert.NotNull(DnsWire.GetMinTtl(response));
        }
    }
```

(thay cho `Each_default_server_answers_via_bootstrap_ip`).

- [ ] **Step 2: Chạy test, thấy lỗi biên dịch.**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~DohCatalogTests"`

- [ ] **Step 3: Viết `DohProvider.cs`**

```csharp
namespace LunaDns.Core.Dns;

/// <summary>Máy chủ DoH chính người dùng chọn (D2). D3 duyệt các giá trị này bằng <c>settings.WithDoh(x)</c>.</summary>
public enum DohProvider
{
    Cloudflare,
    Google,
    Quad9,
    AdGuard,
    Custom,
}

/// <summary>Một lựa chọn đo / thử được: giá trị lưu vào settings cùng máy chủ tương ứng.</summary>
public sealed record DohChoice(DohProvider Provider, DohServer Server);
```

- [ ] **Step 4: Thêm hai máy vào `DohServer.cs`** (sau `Google`, giữ `Defaults` như cũ):

```csharp
    /// <summary>Biến thể không lọc của Quad9 (D2).</summary>
    public static DohServer Quad9 { get; } = new("Quad9", new Uri("https://dns.quad9.net/dns-query"),
        [IPAddress.Parse("9.9.9.9"), IPAddress.Parse("149.112.112.112")]);

    /// <summary>Biến thể không lọc của AdGuard (D2), để không vô tình chặn tên miền game.</summary>
    public static DohServer AdGuard { get; } = new("AdGuard", new Uri("https://unfiltered.adguard-dns.com/dns-query"),
        [IPAddress.Parse("94.140.14.140"), IPAddress.Parse("94.140.14.141")]);
```

Sửa chú thích của `Defaults` thành: `/// <summary>Cloudflare trước, dự phòng Google (spec 4.2). Danh mục đầy đủ ở <see cref="DohCatalog"/>.</summary>`.

- [ ] **Step 5: Viết `DohCatalog.cs`**

```csharp
namespace LunaDns.Core.Dns;

/// <summary>Các máy DoH dựng sẵn theo thứ tự ưu tiên, và cách dựng chuỗi dự phòng (D2).</summary>
public static class DohCatalog
{
    /// <summary>Máy chính + 2 dự phòng: thời gian chờ xấu nhất vẫn là 3 × <see cref="DohClient.DefaultTimeout"/>.</summary>
    public const int ChainLength = 3;

    public static IReadOnlyList<DohChoice> BuiltIn { get; } =
    [
        new(DohProvider.Cloudflare, DohServer.Cloudflare),
        new(DohProvider.Google, DohServer.Google),
        new(DohProvider.Quad9, DohServer.Quad9),
        new(DohProvider.AdGuard, DohServer.AdGuard),
    ];

    public static DohServer Get(DohProvider provider) =>
        BuiltIn.FirstOrDefault(c => c.Provider == provider)?.Server
        ?? throw new ArgumentOutOfRangeException(nameof(provider), provider, "Chỉ máy dựng sẵn mới lấy từ danh mục.");

    /// <summary>
    /// <paramref name="primary"/> rồi các máy dựng sẵn đầu tiên theo thứ tự danh mục, bỏ máy trùng host
    /// (<see cref="DohClient"/> tìm IP bootstrap theo host, hai máy cùng host sẽ lẫn nhau).
    /// </summary>
    public static IReadOnlyList<DohServer> Chain(DohServer primary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        var chain = new List<DohServer>(ChainLength) { primary };
        foreach (var server in BuiltIn.Select(c => c.Server))
        {
            if (chain.Count == ChainLength) break;
            if (chain.Any(s => string.Equals(s.Endpoint.Host, server.Endpoint.Host, StringComparison.OrdinalIgnoreCase))) continue;
            chain.Add(server);
        }
        return chain;
    }

    /// <summary>Lựa chọn đo / thử được: 4 máy dựng sẵn, thêm Tự nhập nếu đã có IP.</summary>
    public static IReadOnlyList<DohChoice> Choices(CustomDoh? custom) =>
        CustomDohParser.ToServer(custom) is { } server ? [.. BuiltIn, new DohChoice(DohProvider.Custom, server)] : BuiltIn;
}
```

- [ ] **Step 6: Cho `ConnectToBootstrapAsync` nhận host IPv6 literal.** `Uri.Host` của `https://[2620:fe::fe]/` có ngoặc vuông, còn `DnsEndPoint.Host` có thể không. Trong `DohClient.ConnectToBootstrapAsync`, thay dòng tìm `server` bằng:

```csharp
        var server = servers.FirstOrDefault(s => string.Equals(s.Endpoint.Host, host, StringComparison.OrdinalIgnoreCase)
                                                 || string.Equals(s.Endpoint.DnsSafeHost, host, StringComparison.OrdinalIgnoreCase))
                     ?? throw new DohException($"Host DoH lạ: {host}.");
```

- [ ] **Step 7: Chạy test, thấy xanh.**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~DohCatalogTests|FullyQualifiedName~DohClientTests"`
Expected: PASS. Nếu có mạng, chạy thêm `LUNADNS_NETWORK_TESTS=1 dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~DohClientNetworkTests"` để kiểm IP của Quad9 và AdGuard; lỗi thì kiểm lại IP trên trang nhà cung cấp trước khi sửa bảng.

- [ ] **Step 8: Commit**

```bash
git add src/LunaDns.Core/Dns/DohProvider.cs src/LunaDns.Core/Dns/DohCatalog.cs src/LunaDns.Core/Dns/DohServer.cs src/LunaDns.Core/Dns/DohClient.cs tests/LunaDns.Core.Tests/Dns/DohCatalogTests.cs tests/LunaDns.Core.Tests/Dns/DohClientNetworkTests.cs
git commit -m "feat(core): add Quad9 and AdGuard to a DoH catalog with 3-server chains"
```

---

### Task 3: `AppSettings` chọn DoH, tương thích `PreferGoogleDoh`

**Files:**
- Modify: `src/LunaDns.Core/Settings/AppSettings.cs`
- Modify: `src/LunaDns.Core/Settings/SettingsJsonContext.cs`
- Modify test: `tests/LunaDns.Core.Tests/Settings/AppSettingsTests.cs`
- Modify test: `tests/LunaDns.Core.Tests/Settings/JsonFileSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `DohProvider`, `DohCatalog.Get/Chain/Choices`, `CustomDoh`, `CustomDohParser.ToServer`.
- Produces (trên `AppSettings`):
  - `DohProvider? Doh { get; init; }`
  - `CustomDoh? CustomDoh { get; init; }`
  - `DohProvider EffectiveDoh()`
  - `AppSettings WithDoh(DohProvider provider)`
  - `IReadOnlyList<DohServer> DohServers()`
  - `IReadOnlyList<DohServer> BootstrapDohServers()`
  - `IReadOnlyList<DohChoice> DohChoices()`

`EffectiveDoh` là **hàm**, không phải thuộc tính: System.Text.Json ghi mọi thuộc tính get-only ra `settings.json`.

- [ ] **Step 1: Viết test (đỏ).** Trong `AppSettingsTests`, thêm `using LunaDns.Core.Dns;` và thay test `Doh_order_follows_preference` bằng:

```csharp
    [Theory]
    [InlineData(false, DohProvider.Cloudflare, "Cloudflare,Google,Quad9")]
    [InlineData(true, DohProvider.Google, "Google,Cloudflare,Quad9")]
    public void Old_settings_keep_their_doh_choice(bool preferGoogle, DohProvider expected, string chain)
    {
        var settings = new AppSettings { PreferGoogleDoh = preferGoogle };

        Assert.Null(settings.Doh);
        Assert.Equal(expected, settings.EffectiveDoh());
        Assert.Equal(chain, string.Join(",", settings.DohServers().Select(s => s.Name)));
    }

    [Fact]
    public void Doh_overrides_legacy_preference()
    {
        var settings = new AppSettings { PreferGoogleDoh = true, Doh = DohProvider.Quad9 };

        Assert.Equal(DohProvider.Quad9, settings.EffectiveDoh());
        Assert.Equal("Quad9", settings.DohServers()[0].Name);
    }

    [Theory]
    [InlineData(DohProvider.Google, true)]
    [InlineData(DohProvider.Quad9, false)]
    [InlineData(DohProvider.Cloudflare, false)]
    public void WithDoh_keeps_legacy_flag_in_sync(DohProvider provider, bool preferGoogle)
    {
        var settings = new AppSettings { PreferGoogleDoh = !preferGoogle }.WithDoh(provider);

        Assert.Equal(provider, settings.Doh);
        Assert.Equal(preferGoogle, settings.PreferGoogleDoh);
    }

    [Fact]
    public void Custom_without_address_falls_back_to_cloudflare_chain()
    {
        var settings = new AppSettings { Doh = DohProvider.Custom, CustomDoh = new CustomDoh { Endpoint = "https://dns.example.net/q" } };

        Assert.Equal("Cloudflare,Google,Quad9", string.Join(",", settings.DohServers().Select(s => s.Name)));
        Assert.Equal(4, settings.DohChoices().Count);
    }

    [Fact]
    public void Custom_with_address_leads_the_chain()
    {
        var settings = new AppSettings { Doh = DohProvider.Custom, CustomDoh = new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] } };

        Assert.Equal("Tự nhập,Cloudflare,Google", string.Join(",", settings.DohServers().Select(s => s.Name)));
        Assert.Equal(DohProvider.Custom, settings.DohChoices()[^1].Provider);
    }

    [Theory]
    [InlineData(DohProvider.Quad9, "Quad9,Cloudflare,Google")]
    [InlineData(DohProvider.Custom, "Cloudflare,Google,Quad9")]
    public void Bootstrap_servers_never_include_custom(DohProvider provider, string expected)
    {
        var settings = new AppSettings { Doh = provider, CustomDoh = new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] } };

        Assert.Equal(expected, string.Join(",", settings.BootstrapDohServers().Select(s => s.Name)));
    }
```

Trong `Defaults_match_spec` thêm `Assert.Null(settings.Doh);` và `Assert.Null(settings.CustomDoh);`.

Trong `JsonFileSettingsStoreTests` (thêm `using LunaDns.Core.Dns;`):

```csharp
    [Fact]
    public void Old_settings_with_prefer_google_load_as_google()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "PreferGoogleDoh": true }""");

        var settings = new JsonFileSettingsStore(path).Load();

        Assert.Null(settings.Doh);
        Assert.Null(settings.CustomDoh);
        Assert.Equal(DohProvider.Google, settings.EffectiveDoh());
    }

    [Fact]
    public void Doh_choice_and_custom_url_round_trip()
    {
        var store = new JsonFileSettingsStore(PathOf("settings.json"));
        var custom = new CustomDoh { Endpoint = "https://dns.example.net/q", ManualAddresses = ["1.2.3.4"], ResolvedAddresses = ["5.6.7.8"] };

        store.Save(new AppSettings().WithDoh(DohProvider.Custom) with { CustomDoh = custom });
        var loaded = store.Load();

        Assert.Equal(DohProvider.Custom, loaded.Doh);
        Assert.Equal(custom.Endpoint, loaded.CustomDoh!.Endpoint);
        Assert.Equal(custom.ManualAddresses, loaded.CustomDoh.ManualAddresses);
        Assert.Equal(custom.ResolvedAddresses, loaded.CustomDoh.ResolvedAddresses);
        Assert.Contains("\"Custom\"", File.ReadAllText(PathOf("settings.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("EffectiveDoh", File.ReadAllText(PathOf("settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_doh_with_missing_lists_loads_with_empty_lists()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "Doh": "Custom", "CustomDoh": { "Endpoint": "https://dns.example.net/q" } }""");

        var settings = new JsonFileSettingsStore(path).Load();

        Assert.Empty(settings.CustomDoh!.ManualAddresses);
        Assert.Empty(settings.CustomDoh.ResolvedAddresses);
        Assert.Equal("Cloudflare", settings.DohServers()[0].Name);
    }
```

- [ ] **Step 2: Chạy test, thấy lỗi biên dịch.**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~AppSettingsTests|FullyQualifiedName~JsonFileSettingsStoreTests"`

- [ ] **Step 3: Sửa `AppSettings.cs`.** Thay khối `PreferGoogleDoh` và hàm `DohServers()` hiện có bằng:

```csharp
    /// <summary>Trường cũ (trước D2). Chỉ dùng khi <see cref="Doh"/> là <c>null</c>; <see cref="WithDoh"/> giữ nó đồng bộ cho bản app cũ.</summary>
    public bool PreferGoogleDoh { get; init; }

    /// <summary>Máy DoH chính (D2). <c>null</c>: settings.json cũ, suy từ <see cref="PreferGoogleDoh"/>.</summary>
    public DohProvider? Doh { get; init; }

    /// <summary>URL tự nhập, dùng khi <see cref="Doh"/> là <see cref="DohProvider.Custom"/>.</summary>
    public CustomDoh? CustomDoh { get; init; }
```

và (cuối lớp, thay `DohServers()` cũ):

```csharp
    /// <summary>Hàm chứ không phải thuộc tính, để không bị ghi ra settings.json.</summary>
    public DohProvider EffectiveDoh() => Doh ?? (PreferGoogleDoh ? DohProvider.Google : DohProvider.Cloudflare);

    public AppSettings WithDoh(DohProvider provider) =>
        this with { Doh = provider, PreferGoogleDoh = provider == DohProvider.Google };

    /// <summary>Chuỗi DoH thật: máy chính + 2 dự phòng (D2). Tự nhập chưa dùng được thì về chuỗi Cloudflare.</summary>
    public IReadOnlyList<DohServer> DohServers() =>
        DohCatalog.Chain(EffectiveDoh() == DohProvider.Custom
            ? CustomDohParser.ToServer(CustomDoh) ?? DohServer.Cloudflare
            : DohCatalog.Get(EffectiveDoh()));

    /// <summary>Chuỗi chỉ gồm máy dựng sẵn, để phân giải host của URL tự nhập mà không phụ thuộc chính nó.</summary>
    public IReadOnlyList<DohServer> BootstrapDohServers() =>
        DohCatalog.Chain(EffectiveDoh() == DohProvider.Custom ? DohServer.Cloudflare : DohCatalog.Get(EffectiveDoh()));

    public IReadOnlyList<DohChoice> DohChoices() => DohCatalog.Choices(CustomDoh);
```

- [ ] **Step 4: Đăng ký kiểu mới trong `SettingsJsonContext.cs`**, cạnh `[JsonSerializable(typeof(DomainSelection))]`:

```csharp
[JsonSerializable(typeof(CustomDoh))]
```

(thêm `using LunaDns.Core.Dns;` nếu file chưa có).

- [ ] **Step 5: Chạy test, thấy xanh.** Cùng lệnh Step 2, rồi chạy cả `dotnet test tests/LunaDns.Core.Tests` để chắc không test nào khác hỏng (ví dụ `Save_then_load_round_trips` vẫn so `Equal` được vì `CustomDoh` là `null`).

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.Core/Settings tests/LunaDns.Core.Tests/Settings
git commit -m "feat(core): store DoH provider choice and custom URL in settings"
```

---

### Task 4: `CustomDohBootstrap`

**Files:**
- Create: `src/LunaDns.Core/Dns/CustomDohBootstrap.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/CustomDohBootstrapTests.cs`

**Interfaces:**
- Consumes: `IHostResolver.ResolveAsync(string host, CancellationToken)`, `CustomDohParser.TryParseEndpoint/LiteralAddress`, `DohException(string)`.
- Produces: `static Task<CustomDoh> CustomDohBootstrap.ResolveAsync(CustomDoh custom, IHostResolver resolver, CancellationToken cancellationToken)`.
  - URL hỏng → `ArgumentException`.
  - Có IP tay hoặc host là IP literal → trả nguyên `custom`, không gọi resolver.
  - Kết quả rỗng → `DohException`; lỗi của resolver (`DohException`) để nguyên cho người gọi.

- [ ] **Step 1: Viết test (đỏ)**

```csharp
using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public sealed class CustomDohBootstrapTests
{
    private sealed class FakeResolver(Func<string, IReadOnlyList<IPAddress>> answer) : IHostResolver
    {
        public List<string> Hosts { get; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Hosts.Add(host);
            return Task.FromResult(answer(host));
        }
    }

    [Fact]
    public async Task Manual_addresses_skip_the_resolver()
    {
        var resolver = new FakeResolver(_ => throw new InvalidOperationException());
        var custom = new CustomDoh { Endpoint = "https://dns.example.net/q", ManualAddresses = ["1.2.3.4"] };

        Assert.Same(custom, await CustomDohBootstrap.ResolveAsync(custom, resolver, CancellationToken.None));
        Assert.Empty(resolver.Hosts);
    }

    [Fact]
    public async Task Ip_literal_host_skips_the_resolver()
    {
        var resolver = new FakeResolver(_ => throw new InvalidOperationException());
        var custom = new CustomDoh { Endpoint = "https://9.9.9.9/dns-query" };

        Assert.Same(custom, await CustomDohBootstrap.ResolveAsync(custom, resolver, CancellationToken.None));
    }

    [Fact]
    public async Task Resolved_addresses_replace_old_ones()
    {
        var resolver = new FakeResolver(_ => [IPAddress.Parse("5.6.7.8"), IPAddress.Parse("5.6.7.9")]);
        var custom = new CustomDoh { Endpoint = "https://dns.example.net:8443/q", ResolvedAddresses = ["1.1.1.1"] };

        var resolved = await CustomDohBootstrap.ResolveAsync(custom, resolver, CancellationToken.None);

        Assert.Equal(["5.6.7.8", "5.6.7.9"], resolved.ResolvedAddresses);
        Assert.Equal(["dns.example.net"], resolver.Hosts);
        Assert.Equal(custom.Endpoint, resolved.Endpoint);
    }

    [Fact]
    public async Task Empty_answer_throws_doh_exception()
    {
        var custom = new CustomDoh { Endpoint = "https://dns.example.net/q" };

        await Assert.ThrowsAsync<DohException>(() => CustomDohBootstrap.ResolveAsync(custom, new FakeResolver(_ => []), CancellationToken.None));
    }

    [Fact]
    public async Task Bad_url_throws_argument_exception()
    {
        var custom = new CustomDoh { Endpoint = "http://dns.example.net/q" };

        await Assert.ThrowsAsync<ArgumentException>(() => CustomDohBootstrap.ResolveAsync(custom, new FakeResolver(_ => []), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Chạy test, thấy lỗi biên dịch.**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~CustomDohBootstrapTests"`

- [ ] **Step 3: Viết `CustomDohBootstrap.cs`**

```csharp
namespace LunaDns.Core.Dns;

/// <summary>
/// Lấy IP bootstrap cho URL tự nhập qua một <see cref="IHostResolver"/> DoH (chuỗi dựng sẵn), không bao giờ qua DNS hệ thống.
/// IP sai hay cũ chỉ làm lỗi kết nối: TLS vẫn kiểm chứng chỉ theo host của URL.
/// </summary>
public static class CustomDohBootstrap
{
    public static async Task<CustomDoh> ResolveAsync(CustomDoh custom, IHostResolver resolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custom);
        ArgumentNullException.ThrowIfNull(resolver);
        if (!CustomDohParser.TryParseEndpoint(custom.Endpoint, out var endpoint))
            throw new ArgumentException("URL DoH tự nhập không hợp lệ.", nameof(custom));
        if ((custom.ManualAddresses?.Count ?? 0) > 0 || CustomDohParser.LiteralAddress(endpoint) is not null)
            return custom;

        var host = endpoint.DnsSafeHost;
        var addresses = await resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0) throw new DohException($"Không tìm thấy IP cho {host}.");
        return custom with { ResolvedAddresses = [.. addresses.Select(a => a.ToString())] };
    }
}
```

- [ ] **Step 4: Chạy test, thấy xanh.** Cùng lệnh Step 2.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Dns/CustomDohBootstrap.cs tests/LunaDns.Core.Tests/Dns/CustomDohBootstrapTests.cs
git commit -m "feat(core): resolve custom DoH host through built-in DoH"
```

---

### Task 5: `DohLatencyMeter`

**Files:**
- Create: `src/LunaDns.Core/Dns/DohLatencyMeter.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/DohLatencyMeterTests.cs`

**Interfaces:**
- Consumes: `IDnsUpstream`, `DohClient.Create(IReadOnlyList<DohServer>, ILogger<DohClient>?)`, `DohChoice`, `DnsWire.BuildQuery/ReadRcode/ReadAddresses`, `DnsWire.TypeA`.
- Produces:
  - `sealed record DohLatency(DohProvider Provider, TimeSpan? Median, string? Error)`
  - `sealed class DohLatencyMeter(Func<DohServer, IDnsUpstream> upstreamFor, TimeProvider? time = null)`:
    - `const string ProbeHost = "store.steampowered.com"`
    - `const int MeasuredQueries = 3`
    - `static DohLatencyMeter Create(ILoggerFactory? loggers = null)`
    - `Task<IReadOnlyList<DohLatency>> MeasureAsync(IReadOnlyList<DohChoice> choices, CancellationToken cancellationToken)`: nhanh trước, lỗi sau; bằng nhau thì giữ thứ tự đầu vào; huỷ thì ném `OperationCanceledException`.

- [ ] **Step 1: Viết test (đỏ).** Upstream giả trả `Task` đã xong và tự đẩy đồng hồ giả, nên các máy đo lần lượt, không giẫm đồng hồ của nhau, kết quả tất định.

```csharp
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public sealed class DohLatencyMeterTests
{
    private sealed class SteppingTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(int ms) => Interlocked.Add(ref _ticks, TimeSpan.FromMilliseconds(ms).Ticks);
    }

    /// <summary>Lần gọi thứ i đẩy đồng hồ <c>delaysMs[i]</c>; giá trị âm thì ném DohException.</summary>
    private sealed class ScriptedUpstream(SteppingTime time, int[] delaysMs, byte[]? response = null) : IDnsUpstream, IDisposable
    {
        private int _calls;
        public bool Disposed { get; private set; }

        public Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
        {
            var delay = delaysMs[Math.Min(_calls++, delaysMs.Length - 1)];
            if (delay < 0) throw new DohException("hỏng");
            time.Advance(delay);
            return Task.FromResult(response ?? TestDnsMessage.Response(DohLatencyMeter.ProbeHost, 0, 300));
        }

        public void Dispose() => Disposed = true;
    }

    private static DohChoice Choice(DohProvider provider) => new(provider, DohCatalog.Get(provider));

    [Fact]
    public async Task Ranks_by_median_of_measured_queries_ignoring_warm_up()
    {
        var time = new SteppingTime();
        var upstreams = new Dictionary<string, ScriptedUpstream>
        {
            ["Cloudflare"] = new(time, [500, 30, 10, 40]), // khởi động chậm không tính; trung vị 30
            ["Google"] = new(time, [5, 20, 25, 90]),       // trung vị 25
            ["Quad9"] = new(time, [5, 50, 50, 50]),        // trung vị 50
        };
        var meter = new DohLatencyMeter(s => upstreams[s.Name], time);

        var results = await meter.MeasureAsync([Choice(DohProvider.Cloudflare), Choice(DohProvider.Google), Choice(DohProvider.Quad9)], CancellationToken.None);

        Assert.Equal([DohProvider.Google, DohProvider.Cloudflare, DohProvider.Quad9], results.Select(r => r.Provider));
        Assert.Equal(TimeSpan.FromMilliseconds(25), results[0].Median);
        Assert.Equal(TimeSpan.FromMilliseconds(30), results[1].Median);
        Assert.All(results, r => Assert.Null(r.Error));
        Assert.All(upstreams.Values, u => Assert.True(u.Disposed));
    }

    [Fact]
    public async Task Failures_rank_last_with_error()
    {
        var time = new SteppingTime();
        var servFail = DnsWire.BuildHeaderOnlyResponse(TestDnsMessage.Query(DohLatencyMeter.ProbeHost), DnsRcode.ServFail);
        var noAnswer = TestDnsMessage.Response(DohLatencyMeter.ProbeHost, 0);
        var upstreams = new Dictionary<string, ScriptedUpstream>
        {
            ["Cloudflare"] = new(time, [1, 1, -1]),
            ["Google"] = new(time, [1], servFail),
            ["Quad9"] = new(time, [1], noAnswer),
            ["AdGuard"] = new(time, [1, 70]),
        };
        var meter = new DohLatencyMeter(s => upstreams[s.Name], time);

        var results = await meter.MeasureAsync(DohCatalog.BuiltIn, CancellationToken.None);

        Assert.Equal(DohProvider.AdGuard, results[0].Provider);
        Assert.Equal(TimeSpan.FromMilliseconds(70), results[0].Median);
        Assert.Equal([DohProvider.Cloudflare, DohProvider.Google, DohProvider.Quad9], results.Skip(1).Select(r => r.Provider));
        Assert.All(results.Skip(1), r => { Assert.Null(r.Median); Assert.False(string.IsNullOrEmpty(r.Error)); });
    }

    [Fact]
    public async Task Cancellation_stops_measuring()
    {
        using var cts = new CancellationTokenSource();
        var meter = new DohLatencyMeter(_ => new HangingUpstream());

        var task = meter.MeasureAsync([Choice(DohProvider.Cloudflare)], cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private sealed class HangingUpstream : IDnsUpstream
    {
        public async Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }
}
```

- [ ] **Step 2: Chạy test, thấy lỗi biên dịch.**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~DohLatencyMeterTests"`

- [ ] **Step 3: Viết `DohLatencyMeter.cs`**

```csharp
using Microsoft.Extensions.Logging;

namespace LunaDns.Core.Dns;

/// <summary>Kết quả đo một máy DoH: <see cref="Median"/> có khi đo được, <see cref="Error"/> có khi lỗi.</summary>
public sealed record DohLatency(DohProvider Provider, TimeSpan? Median, string? Error);

/// <summary>
/// Nút "Đo" (D2): mỗi máy một upstream riêng (không rơi sang dự phòng, kết nối mới), 1 truy vấn khởi động
/// (gồm TCP + TLS) rồi <see cref="MeasuredQueries"/> truy vấn A; điểm là trung vị các truy vấn sau, vì lúc dùng thật
/// kết nối được giữ 5 phút.
/// </summary>
public sealed class DohLatencyMeter(Func<DohServer, IDnsUpstream> upstreamFor, TimeProvider? time = null)
{
    public const string ProbeHost = "store.steampowered.com";
    public const int MeasuredQueries = 3;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Đo thật: <see cref="DohClient"/> chỉ chứa một máy, timeout mặc định của nó.</summary>
    public static DohLatencyMeter Create(ILoggerFactory? loggers = null) =>
        new(server => DohClient.Create([server], loggers?.CreateLogger<DohClient>()));

    public async Task<IReadOnlyList<DohLatency>> MeasureAsync(IReadOnlyList<DohChoice> choices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var results = await Task.WhenAll(choices.Select(c => MeasureOneAsync(c, cancellationToken))).ConfigureAwait(false);
        // OrderBy ổn định: máy bằng điểm giữ thứ tự danh mục.
        return [.. results.OrderBy(r => r.Median is null).ThenBy(r => r.Median ?? TimeSpan.Zero)];
    }

    private async Task<DohLatency> MeasureOneAsync(DohChoice choice, CancellationToken cancellationToken)
    {
        var upstream = upstreamFor(choice.Server);
        try
        {
            await QueryAsync(upstream, cancellationToken).ConfigureAwait(false);
            var samples = new TimeSpan[MeasuredQueries];
            for (var i = 0; i < samples.Length; i++)
            {
                var start = _time.GetTimestamp();
                await QueryAsync(upstream, cancellationToken).ConfigureAwait(false);
                samples[i] = _time.GetElapsedTime(start);
            }
            Array.Sort(samples);
            return new DohLatency(choice.Provider, samples[MeasuredQueries / 2], null);
        }
        catch (Exception ex) when (ex is DohException or HttpRequestException or IOException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new DohLatency(choice.Provider, null, ex.Message);
        }
        finally
        {
            (upstream as IDisposable)?.Dispose();
        }
    }

    private static async Task QueryAsync(IDnsUpstream upstream, CancellationToken cancellationToken)
    {
        var response = await upstream.QueryAsync(DnsWire.BuildQuery(ProbeHost, DnsWire.TypeA), cancellationToken).ConfigureAwait(false);
        var rcode = DnsWire.ReadRcode(response);
        if (rcode != DnsRcode.NoError) throw new DohException($"Máy chủ trả mã lỗi {rcode}.");
        if (DnsWire.ReadAddresses(response).Count == 0) throw new DohException("Máy chủ không trả bản ghi A.");
    }
}
```

Nếu analyzer báo CA1031/CA2000 cho các dòng trên, sửa theo cảnh báo (không tắt analyzer); `DohClient` đã dùng đúng mẫu `catch … when` này.

- [ ] **Step 4: Chạy test, thấy xanh.** Cùng lệnh Step 2.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.Core/Dns/DohLatencyMeter.cs tests/LunaDns.Core.Tests/Dns/DohLatencyMeterTests.cs
git commit -m "feat(core): measure DoH latency per server"
```

---

### Task 6: Nối Core vào `AppServices`

**Files:**
- Modify: `src/LunaDns.App/AppServices.cs` (cạnh `ProbeAsync`)

**Interfaces:**
- Consumes: `AppSettings.BootstrapDohServers()`, `DohChoices()`, `CustomDohBootstrap.ResolveAsync`, `DohLatencyMeter.Create`, `DohHostResolver`, `DohClient.Create`.
- Produces (trên `AppServices`):
  - `Task<CustomDoh> ResolveCustomDohAsync(CustomDoh custom, CancellationToken cancellationToken)`: ném `DohException` nếu không phân giải được.
  - `Task<IReadOnlyList<DohLatency>> MeasureDohAsync(CancellationToken cancellationToken)`: phân giải lại Tự nhập nếu có (lỗi thì giữ IP cũ), lưu `CustomDoh` mới vào `Settings`, rồi đo `Settings.DohChoices()`.

- [ ] **Step 1: Thêm hai hàm vào `AppServices`** (thêm `using LunaDns.Core.Dns;` nếu chưa có):

```csharp
    /// <summary>Lấy IP cho URL DoH tự nhập qua chuỗi DoH dựng sẵn (D2), không qua DNS hệ thống.</summary>
    public async Task<CustomDoh> ResolveCustomDohAsync(CustomDoh custom, CancellationToken cancellationToken)
    {
        using var doh = DohClient.Create(Settings.BootstrapDohServers(), LoggerFactory.CreateLogger<DohClient>());
        return await CustomDohBootstrap.ResolveAsync(custom, new DohHostResolver(doh), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Nút "Đo" (D2). Tự nhập phân giải lại trước; lỗi thì đo bằng IP cũ, máy đó có thể ra "lỗi".</summary>
    public async Task<IReadOnlyList<DohLatency>> MeasureDohAsync(CancellationToken cancellationToken)
    {
        if (Settings.CustomDoh is { } custom && CustomDohParser.TryParseEndpoint(custom.Endpoint, out _))
        {
            try
            {
                var resolved = await ResolveCustomDohAsync(custom, cancellationToken).ConfigureAwait(false);
                if (!ReferenceEquals(resolved, custom)) Settings = Settings with { CustomDoh = resolved };
            }
            catch (DohException)
            {
                // Giữ IP cũ; kết quả đo của Tự nhập sẽ cho người dùng thấy máy đó có dùng được không.
            }
        }
        return await DohLatencyMeter.Create(LoggerFactory).MeasureAsync(Settings.DohChoices(), cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 2: Build App.**

Run: `dotnet build src/LunaDns.App`
Expected: build thành công, không cảnh báo. Không có test riêng: logic nằm trong Core (Task 1–5) đã có test; hai hàm này chỉ ghép, giống `ProbeAsync`.

- [ ] **Step 3: Commit**

```bash
git add src/LunaDns.App/AppServices.cs
git commit -m "feat(app): wire custom DoH bootstrap and latency measurement"
```

---

### Task 7: Giao diện mục Nâng cao, chuỗi, hướng dẫn

**Files:**
- Modify: `src/LunaDns.App/ViewModels/MainViewModel.cs` (`DohIndex` ở dòng ~199, khối khởi tạo command ở dòng ~69–77, `Stop()` ở dòng ~253, `SaveSettings` ở dòng ~537)
- Modify: `src/LunaDns.App/Views/MainWindow.axaml` (khối DoH ở dòng ~303–307)
- Modify: `src/LunaDns.App/Strings.cs`, `src/LunaDns.App/Strings.resx` (khoá `DohLabel`… ở dòng ~60–62)
- Modify test: `tests/LunaDns.App.Tests/StringsTests.cs` (số chuỗi)
- Modify: `docs/guide/05-nang-cao.md` (dòng "Máy chủ DoH")

**Interfaces:**
- Consumes: `AppSettings.EffectiveDoh()/WithDoh()/CustomDoh`, `CustomDohParser.TryParseEndpoint/TryParseAddresses/ToServer`, `AppServices.ResolveCustomDohAsync/MeasureDohAsync`, `DohLatency`.
- Produces (binding trên `MainViewModel`): `IReadOnlyList<string> DohItems`, `int DohIndex`, `bool IsCustomDoh`, `string CustomDohUrl`, `string CustomDohAddresses`, `string CustomDohStatus`, `string DohMeasureLabel`, `AsyncCommand MeasureDohCommand`, `AsyncCommand SaveCustomDohCommand`.

- [ ] **Step 1: Chuỗi.** Trong `Strings.resx` xoá `DohCloudflareFirst`, `DohGoogleFirst`, thêm (sau `DohLabel`):

```xml
  <data name="DohCustom" xml:space="preserve"><value>Tự nhập</value></data>
  <data name="DohMeasureButton" xml:space="preserve"><value>Đo</value></data>
  <data name="DohMeasuring" xml:space="preserve"><value>Đang đo…</value></data>
  <data name="DohMeasureChosenFormat" xml:space="preserve"><value>Đã chọn {0} ({1} ms). Có hiệu lực ở lần bật tính năng kế tiếp.</value></data>
  <data name="DohMeasureAllFailed" xml:space="preserve"><value>Không máy chủ DoH nào trả lời. Giữ lựa chọn cũ.</value></data>
  <data name="DohLatencyFormat" xml:space="preserve"><value>{0} — {1} ms</value></data>
  <data name="DohLatencyFailedFormat" xml:space="preserve"><value>{0} — lỗi</value></data>
  <data name="DohCustomUrlLabel" xml:space="preserve"><value>URL DoH</value></data>
  <data name="DohCustomAddressesLabel" xml:space="preserve"><value>IP (tuỳ chọn, ngăn bằng dấu phẩy)</value></data>
  <data name="DohCustomSave" xml:space="preserve"><value>Lưu</value></data>
  <data name="DohCustomResolving" xml:space="preserve"><value>Đang tìm IP qua DoH dựng sẵn…</value></data>
  <data name="DohCustomUsingFormat" xml:space="preserve"><value>Dùng {0}.</value></data>
  <data name="DohCustomNotSaved" xml:space="preserve"><value>Chưa có URL dùng được: đang dùng Cloudflare.</value></data>
  <data name="DohCustomInvalidUrl" xml:space="preserve"><value>URL phải có dạng https://tên-máy/đường-dẫn.</value></data>
  <data name="DohCustomInvalidAddresses" xml:space="preserve"><value>Ô IP có giá trị không phải địa chỉ IPv4/IPv6.</value></data>
  <data name="DohCustomResolveFailed" xml:space="preserve"><value>Không tìm được IP qua DoH dựng sẵn. Hãy điền IP vào ô bên trên.</value></data>
```

Trong `Strings.cs` xoá `DohCloudflareFirst`, `DohGoogleFirst`; thêm sau `DohLabel`:

```csharp
    public static string DohCustom => Get(nameof(DohCustom));
    public static string DohMeasureButton => Get(nameof(DohMeasureButton));
    public static string DohMeasuring => Get(nameof(DohMeasuring));
    public static string DohMeasureChosenFormat => Get(nameof(DohMeasureChosenFormat));
    public static string DohMeasureAllFailed => Get(nameof(DohMeasureAllFailed));
    public static string DohLatencyFormat => Get(nameof(DohLatencyFormat));
    public static string DohLatencyFailedFormat => Get(nameof(DohLatencyFailedFormat));
    public static string DohCustomUrlLabel => Get(nameof(DohCustomUrlLabel));
    public static string DohCustomAddressesLabel => Get(nameof(DohCustomAddressesLabel));
    public static string DohCustomSave => Get(nameof(DohCustomSave));
    public static string DohCustomResolving => Get(nameof(DohCustomResolving));
    public static string DohCustomUsingFormat => Get(nameof(DohCustomUsingFormat));
    public static string DohCustomNotSaved => Get(nameof(DohCustomNotSaved));
    public static string DohCustomInvalidUrl => Get(nameof(DohCustomInvalidUrl));
    public static string DohCustomInvalidAddresses => Get(nameof(DohCustomInvalidAddresses));
    public static string DohCustomResolveFailed => Get(nameof(DohCustomResolveFailed));
```

Trong `StringsTests.Every_static_string_resolves`: `Assert.Equal(151, …)` → `Assert.Equal(165, …)` (+16, −2).

- [ ] **Step 2: Chạy StringsTests, thấy xanh.**

Run: `dotnet test tests/LunaDns.App.Tests --filter "FullyQualifiedName~StringsTests"`
Expected: PASS. Nếu số khác 165, đếm lại số khoá vừa thêm/xoá trước khi sửa con số.

- [ ] **Step 3: ViewModel.** Thêm `using System.Globalization;` và `using LunaDns.Core.Dns;` nếu chưa có. Thay thuộc tính `DohIndex` hiện có bằng:

```csharp
    /// <summary>Thứ tự mục trong combobox "Máy chủ DoH" (D2).</summary>
    private static readonly DohProvider[] DohOrder =
        [DohProvider.Cloudflare, DohProvider.Google, DohProvider.Quad9, DohProvider.AdGuard, DohProvider.Custom];

    private static readonly TimeSpan DohMeasureTimeout = TimeSpan.FromSeconds(30);

    private IReadOnlyDictionary<DohProvider, DohLatency> _dohLatencies = new Dictionary<DohProvider, DohLatency>();
    private IReadOnlyList<string> _dohItems = BuildDohItems(new Dictionary<DohProvider, DohLatency>());
    private bool _isMeasuringDoh;
    private string _customDohUrl = string.Empty;
    private string _customDohAddresses = string.Empty;
    private string _customDohStatus = string.Empty;
    private CancellationTokenSource? _dohCts;

    public IReadOnlyList<string> DohItems
    {
        get => _dohItems;
        private set => SetProperty(ref _dohItems, value);
    }

    public int DohIndex
    {
        get => Array.IndexOf(DohOrder, _services.Settings.EffectiveDoh());
        set
        {
            // ComboBox đặt -1 khi ItemsSource đổi: bỏ qua, không phải lựa chọn của người dùng.
            if (value < 0 || value >= DohOrder.Length) return;
            SaveSettings(s => s.WithDoh(DohOrder[value]));
        }
    }

    public bool IsCustomDoh => _services.Settings.EffectiveDoh() == DohProvider.Custom;

    public string CustomDohUrl
    {
        get => _customDohUrl;
        set => SetProperty(ref _customDohUrl, value ?? string.Empty);
    }

    public string CustomDohAddresses
    {
        get => _customDohAddresses;
        set => SetProperty(ref _customDohAddresses, value ?? string.Empty);
    }

    public string CustomDohStatus
    {
        get => _customDohStatus;
        private set => SetProperty(ref _customDohStatus, value);
    }

    public string DohMeasureLabel => _isMeasuringDoh ? Strings.DohMeasuring : Strings.DohMeasureButton;

    public AsyncCommand MeasureDohCommand { get; }

    public AsyncCommand SaveCustomDohCommand { get; }

    private static string DohName(DohProvider provider) =>
        provider == DohProvider.Custom ? Strings.DohCustom : DohCatalog.Get(provider).Name;

    private static IReadOnlyList<string> BuildDohItems(IReadOnlyDictionary<DohProvider, DohLatency> latencies) =>
        [.. DohOrder.Select(p => latencies.TryGetValue(p, out var l)
            ? l.Median is { } median
                ? string.Format(CultureInfo.CurrentCulture, Strings.DohLatencyFormat, DohName(p), (int)Math.Round(median.TotalMilliseconds))
                : string.Format(CultureInfo.CurrentCulture, Strings.DohLatencyFailedFormat, DohName(p))
            : DohName(p))];

    /// <summary>Dòng trạng thái khối Tự nhập: IP đang dùng, hoặc "đang dùng Cloudflare".</summary>
    private void RefreshCustomDohStatus() =>
        CustomDohStatus = CustomDohParser.ToServer(_services.Settings.CustomDoh) is { } server
            ? string.Format(CultureInfo.CurrentCulture, Strings.DohCustomUsingFormat, string.Join(", ", server.BootstrapAddresses))
            : Strings.DohCustomNotSaved;

    private async Task MeasureDohAsync()
    {
        _isMeasuringDoh = true;
        OnPropertyChanged(nameof(DohMeasureLabel));
        _dohCts = new CancellationTokenSource(DohMeasureTimeout);
        try
        {
            var token = _dohCts.Token;
            var results = await Task.Run(() => _services.MeasureDohAsync(token));
            _dohLatencies = results.ToDictionary(r => r.Provider);
            DohItems = BuildDohItems(_dohLatencies);
            if (results.FirstOrDefault(r => r.Median is not null) is { Median: { } median } best)
            {
                SaveSettings(s => s.WithDoh(best.Provider));
                ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.DohMeasureChosenFormat,
                    DohName(best.Provider), (int)Math.Round(median.TotalMilliseconds)), Tone.Good);
            }
            else
            {
                OnPropertyChanged(nameof(DohIndex)); // ItemsSource vừa đổi: combobox cần đọc lại lựa chọn cũ
                ShowMessage(Strings.DohMeasureAllFailed, Tone.Error);
            }
            if (IsCustomDoh) RefreshCustomDohStatus();
        }
        catch (OperationCanceledException)
        {
            ShowMessage(Strings.DohMeasureAllFailed, Tone.Error);
        }
        finally
        {
            _dohCts.Dispose();
            _dohCts = null;
            _isMeasuringDoh = false;
            OnPropertyChanged(nameof(DohMeasureLabel));
        }
    }

    private async Task SaveCustomDohAsync()
    {
        if (!CustomDohParser.TryParseEndpoint(CustomDohUrl, out _))
        {
            CustomDohStatus = Strings.DohCustomInvalidUrl;
            return;
        }
        if (!CustomDohParser.TryParseAddresses(CustomDohAddresses, out var manual))
        {
            CustomDohStatus = Strings.DohCustomInvalidAddresses;
            return;
        }
        var endpoint = CustomDohUrl.Trim();
        var old = _services.Settings.CustomDoh;
        var draft = new CustomDoh
        {
            Endpoint = endpoint,
            ManualAddresses = [.. manual.Select(a => a.ToString())],
            // Cùng URL thì giữ IP cũ phòng khi phân giải lần này lỗi.
            ResolvedAddresses = old is not null && old.Endpoint == endpoint ? old.ResolvedAddresses : [],
        };
        CustomDohStatus = Strings.DohCustomResolving;
        using var timeout = new CancellationTokenSource(DohMeasureTimeout);
        try
        {
            var resolved = await Task.Run(() => _services.ResolveCustomDohAsync(draft, timeout.Token));
            SaveSettings(s => s with { CustomDoh = resolved });
            RefreshCustomDohStatus();
        }
        catch (Exception ex) when (ex is DohException or OperationCanceledException)
        {
            SaveSettings(s => s with { CustomDoh = draft });
            CustomDohStatus = Strings.DohCustomResolveFailed;
        }
    }
```

Trong constructor, cạnh các `AsyncCommand` khác (sau `InstallServiceCommand = …`):

```csharp
        MeasureDohCommand = new AsyncCommand(MeasureDohAsync, OnError);
        SaveCustomDohCommand = new AsyncCommand(SaveCustomDohAsync, OnError);
        _customDohUrl = _services.Settings.CustomDoh?.Endpoint ?? string.Empty;
        _customDohAddresses = string.Join(", ", _services.Settings.CustomDoh?.ManualAddresses ?? []);
        RefreshCustomDohStatus();
```

Trong `Stop()` thêm `_dohCts?.Cancel();` (huỷ lượt đo khi thoát; `finally` của `MeasureDohAsync` tự dispose).

Trong `SaveSettings`, cạnh `OnPropertyChanged(nameof(DohIndex));` thêm:

```csharp
        OnPropertyChanged(nameof(IsCustomDoh));
```

(Lượt chọn máy nhanh nhất gọi `SaveSettings` *sau* khi đã gán `DohItems`, nên combobox nhận `DohIndex` mới sau khi danh sách đổi.)

- [ ] **Step 4: AXAML.** Trong `MainWindow.axaml`, thay khối `ComboBox SelectedIndex="{Binding DohIndex}"` (cùng hai `ComboBoxItem`) bằng:

```xml
          <Grid ColumnDefinitions="*,Auto">
            <ComboBox ItemsSource="{Binding DohItems}" SelectedIndex="{Binding DohIndex}" HorizontalAlignment="Stretch" />
            <Button Grid.Column="1" Margin="8,0,0,0" Command="{Binding MeasureDohCommand}" Content="{Binding DohMeasureLabel}" />
          </Grid>
          <StackPanel Spacing="6" IsVisible="{Binding IsCustomDoh}">
            <TextBlock Text="{x:Static app:Strings.DohCustomUrlLabel}" />
            <TextBox Text="{Binding CustomDohUrl}" Watermark="https://dns.example.net/dns-query" />
            <TextBlock Text="{x:Static app:Strings.DohCustomAddressesLabel}" />
            <TextBox Text="{Binding CustomDohAddresses}" />
            <Button Command="{Binding SaveCustomDohCommand}" Content="{x:Static app:Strings.DohCustomSave}" />
            <TextBlock Classes="secondary" Text="{Binding CustomDohStatus}" TextWrapping="Wrap" />
          </StackPanel>
```

Giữ nguyên `TextBlock` `DohLabel` phía trên và `AdvancedHint` phía dưới.

- [ ] **Step 5: Hướng dẫn.** Trong `docs/guide/05-nang-cao.md`, thay dòng bảng "Máy chủ DoH" bằng:

```markdown
| **Máy chủ DoH** | *Cloudflare* (mặc định) / *Google* / *Quad9* / *AdGuard* / *Tự nhập* | Máy được chọn hỏi trước, hai máy dựng sẵn khác làm dự phòng. Bấm **Đo** để đo độ trễ trên mạng của bạn và tự chọn máy nhanh nhất. *Tự nhập*: dán URL `https://…`, bấm **Lưu**; app tìm IP của máy đó qua DoH dựng sẵn (không qua DNS của nhà mạng). Nếu không tìm được, điền IP vào ô "IP (tuỳ chọn)". |
```

- [ ] **Step 6: Build và chạy toàn bộ test.**

```bash
dotnet build
dotnet test
```

Expected: build không cảnh báo (compiled bindings bắt sai tên binding lúc build); mọi test xanh, test mạng bị skip.

- [ ] **Step 7: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests/StringsTests.cs docs/guide/05-nang-cao.md
git commit -m "feat(app): DoH provider picker with latency measurement and custom URL"
```

---

### Task 8: Kiểm thử tay, PR

- [ ] **Step 1: Test mạng** (máy có mạng):

```bash
LUNADNS_NETWORK_TESTS=1 dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~Network"
```

Expected: `Each_catalog_server_answers_via_bootstrap_ip` xanh cho cả 4 máy.

- [ ] **Step 2: Push nhánh, mở PR** (tiếng Anh, không có dòng attribution), để CI chạy Windows + macOS:

```bash
git push -u origin feat/doh-providers
gh pr create --title "feat: extra DoH providers, custom DoH URL and latency measurement (D2)" --body "Implements D2 from docs/superpowers/specs/2026-11-21-lunadns-doh-providers-design.md: Quad9 and AdGuard (unfiltered), custom DoH URL bootstrapped through built-in DoH (never system DNS), and a Measure button that picks the fastest server. Old settings.json keeps its PreferGoogleDoh choice. UI is Windows-only; the macOS daemon protocol is unchanged."
```

- [ ] **Step 3: Thử tay trên Windows** (ghi kết quả vào PR):
  1. Mở mục Nâng cao: combobox có 5 mục, mặc định đúng lựa chọn cũ (thử cả `settings.json` có `"PreferGoogleDoh": true`).
  2. Bấm **Đo**: các mục hiện "— N ms" hoặc "— lỗi", app chọn máy nhanh nhất và báo "Đã chọn …".
  3. Chọn **Tự nhập**, nhập `https://dns.quad9.net/dns-query` không kèm IP, bấm Lưu: dòng trạng thái "Dùng 9.9.9.9, …" (hoặc IP khác của Quad9).
  4. Nhập `http://dns.quad9.net/dns-query`: báo "URL phải có dạng https://…", không lưu.
  5. Bật Định tuyến DNS, mở Steam Store và Community.
  6. macOS: mục Nâng cao vẫn ẩn, Kiểm tra và bật tính năng chạy như cũ.

- [ ] **Step 4: Merge qua PR khi CI xanh cả hai nền tảng.** Không merge thẳng vào `main`.
