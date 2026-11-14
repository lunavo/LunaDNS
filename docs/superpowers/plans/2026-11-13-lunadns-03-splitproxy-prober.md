# LunaDNS — Kế hoạch 3: SplitProxy (CONNECT + PAC) và Prober

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Viết hai vùng còn thiếu của `LunaDns.Core` trước khi đụng tới hệ điều hành: `Proxy/SplitProxy` (proxy HTTP CONNECT nghe `127.0.0.1:18080–18090`, phục vụ `/proxy.pac`, cắt gói ClientHello, từ chối host ngoài `GameDomains`; spec 4.3) và `Probing/Prober` (phân loại mỗi host thành `Ok` / `DnsPoisoned` / `SniFiltered` / `Unreachable`; spec 4.1).

**Architecture:** Cả hai vùng dùng chung hai mảnh nhỏ. (1) `IHostResolver` ("tên → danh sách IPv4") có hai bản: `SystemHostResolver` (DNS hệ thống) và `DohHostResolver` (đi qua `IDnsUpstream` của KH2). Prober so sánh hai bản; proxy dùng bản DoH để nối tới máy chủ thật, nên proxy không phụ thuộc NRPT. (2) `SplitWriter` ghi một ClientHello thành nhiều lần `Write` theo `HelloSplitter` (KH1); proxy dùng nó cho kết nối của client, còn `TlsProbe` dùng nó qua `SplittingStream` để tự bắt tay TLS cắt gói. Mọi I/O thật nằm sau interface (`IUpstreamConnector`, `ITlsProbe`, `IHostResolver`), nên test mặc định chạy bằng fake hoặc loopback, không cần Admin và không cần Internet.

**Tech Stack:** .NET 8 SDK 8.0.428, C# 12, `System.Net.Sockets`, `System.Net.Security.SslStream`, `Microsoft.Extensions.Logging.Abstractions` 8.0.2 (đã có), xUnit 2.5. Không thêm package nào.

**Spec:** `docs/superpowers/specs/2026-11-13-lunadns-design.md` (mục 4.1, 4.3, 6, 9). **Kế hoạch trước:** `docs/superpowers/plans/2026-11-13-lunadns-02-doh-stub.md`. **Spike:** `docs/spikes/2026-11-spike-results.md`. **Lộ trình:** bảng ở đầu `docs/superpowers/plans/2026-11-13-lunadns-01-core-foundation.md` (KH3 = SplitProxy + Prober; KH4 = `Platform.Windows`; KH5 = `FeatureSet` + App + watchdog).

## Global Constraints

- Target `net8.0` (SDK 8.0.428). `Directory.Build.props` đã bật `Nullable`, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`. Không đổi file đó.
- `LunaDns.Core` **không** được tham chiếu `Microsoft.Win32`, P/Invoke hay package nào riêng của Windows. Socket, `SslStream`, `System.Net.Dns` của BCL thì được dùng.
- **Không thêm package nào** ở cả `src/` lẫn `tests/`.
- Không có `catch { }` rỗng (spec mục 6). Catch nào cũng phải ném lại, ghi log, hoặc đổi thành giá trị trả về có nghĩa. Log dùng `[LoggerMessage]` như KH2.
- Không chép mã từ SteamAccessFix (quyết định D4).
- Analyzer báo lỗi thì sửa code, không hạ mức cảnh báo. Ngoại lệ duy nhất đã có tiền lệ: thêm dòng tắt rule vào `tests/.editorconfig` nếu rule chỉ vướng ở test. Ghi mọi lệch kế hoạch vào báo cáo task.
- Hằng mảng truyền làm đối số (CA1861) thì đưa lên thành `static readonly`. Cắt mảng trong test dùng `AsSpan(...)`, không dùng `[..^n]` (CA1832, đã gặp ở KH2).
- Trong namespace `LunaDns.Core.Dns` và `LunaDns.Core.Tests.Dns`, tên `Dns` trỏ vào namespace chứ không phải class `System.Net.Dns`. Gọi DNS hệ thống phải viết đủ `System.Net.Dns.GetHostAddressesAsync(...)`.
- Proxy và mọi listener chỉ bind `127.0.0.1`, với `ExclusiveAddressUse = true`. KH3 **không** đụng registry, `AutoConfigURL`, NRPT hay file hosts; việc đó ở KH4.
- Không kết nối nào của KH3 được đi qua proxy hệ thống: `TcpUpstreamConnector` và `TlsProbe` dùng socket trần, DoH giữ `UseProxy = false` (KH2).
- Không bao giờ giải mã TLS và không cài chứng chỉ (spec 4.3). Proxy chỉ chuyển byte.
- Log không ghi tên host nằm ngoài `GameDomains` (báo cáo chẩn đoán ở KH5 chép 200 dòng log cuối, khách gửi qua chat).
- Thông báo cho người dùng (message của exception) viết tiếng Việt có dấu. Tên định danh trong code viết tiếng Anh.
- Lệnh kiểm tra (PowerShell):
  `$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"; dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
- Repo chỉ ở local và **không commit**: không `git add`, `git commit`, `git switch`, không tạo nhánh hay remote. Thay đổi để ở working tree; phiên chính review bằng `git diff` và `git status`. Các mục untracked của KH1/KH2 (`spikes/`, `docs/spikes/`, `src/LunaDns.Core/Dns/`…) để nguyên, chỉ sửa đúng file task nêu.

## Gợi ý chia việc cho sub-agent (Sonnet)

Dự kiến 4 agent, đều `model: "sonnet"`, chạy tuần tự vì agent sau dùng tên và kiểu của agent trước.

| Agent | Task | Lý do gộp |
|---|---|---|
| A | 1, 2 | Vùng `Dns`: đọc/dựng wire và hai resolver |
| B | 3, 4, 5 | Các mảnh thuần của proxy: parser, PAC, `SplitWriter`, connector |
| C | 6 | `SplitProxy`: socket thật, nhiều test nhất, đáng một lượt review riêng |
| D | 7, 8, 9, 10 | Vùng `Probing` và test mạng |

Sau mỗi agent, phiên chính tự review bằng `git diff` và chạy lại lệnh kiểm tra. Sonnet trượt verify 2 lần thì giao lại Opus một lần.

## Cấu trúc file của kế hoạch 3

```
src/LunaDns.Core/
├── Dns/
│   ├── DnsWire.cs                      # (sửa, T1) + TypeA, TypeAaaa, BuildQuery, ReadAddresses
│   ├── IHostResolver.cs                # T2  "tên → danh sách IPv4"
│   ├── SystemHostResolver.cs           # T2  DNS hệ thống
│   └── DohHostResolver.cs              # T2  qua IDnsUpstream (+ DnsCache tuỳ chọn)
├── Tls/
│   └── SplitWriter.cs                  # T4  ghi ClientHello thành nhiều Write
├── Proxy/
│   ├── PacBuilder.cs                   # (sửa, T4) chỉ https:/wss: đi qua proxy
│   ├── ProxyRequest.cs                 # T3
│   ├── ProxyRequestParser.cs           # T3  dòng yêu cầu HTTP của proxy
│   ├── SplitProxyOptions.cs            # T5
│   ├── ProxyStartError.cs              # T5
│   ├── SplitProxyException.cs          # T5
│   ├── IUpstreamConnector.cs           # T5
│   ├── TcpUpstreamConnector.cs         # T5  resolver → socket NoDelay
│   └── SplitProxy.cs                   # T6  listener, PAC, CONNECT, 403, cắt gói, relay
└── Probing/
    ├── ProbeVerdict.cs                 # T7
    ├── ProbeResult.cs                  # T7
    ├── ProbeRemedies.cs                # T7
    ├── ProbeAdvisor.cs                 # T7  verdict → biện pháp đề xuất
    ├── ProbeTargets.cs                 # T7  host mẫu của spec 4.1
    ├── KnownBlockIps.cs                # T7  dải IP không thể là máy chủ game
    ├── ITlsProbe.cs                    # T8
    ├── Prober.cs                       # T8
    ├── SplittingStream.cs              # T9  internal
    └── TlsProbe.cs                     # T9  SslStream nguyên vẹn / cắt gói
tests/LunaDns.Core.Tests/
├── Dns/
│   ├── TestDnsMessage.cs               # (sửa, T1) + ResponseWithAddresses
│   ├── DnsWireAddressTests.cs          # T1
│   ├── HostResolverTests.cs            # T2
│   └── FakeResolver.cs                 # T5
├── Tls/
│   ├── RecordingStream.cs              # T4
│   └── SplitWriterTests.cs             # T4
├── Proxy/
│   ├── PacBuilderTests.cs              # (sửa, T4)
│   ├── ProxyRequestParserTests.cs      # T3
│   ├── SplitProxyOptionsTests.cs       # T5
│   ├── TcpUpstreamConnectorTests.cs    # T5
│   ├── FakeConnector.cs                # T6
│   ├── EchoOrigin.cs                   # T6
│   ├── ProxyTestClient.cs              # T6
│   ├── SplitProxyTests.cs              # T6
│   └── SplitProxyNetworkTests.cs       # T10 [NetworkFact]
└── Probing/
    ├── KnownBlockIpsTests.cs           # T7
    ├── ProbeAdvisorTests.cs            # T7
    ├── ProbeTargetsTests.cs            # T7
    ├── FakeTlsProbe.cs                 # T8
    ├── ProberTests.cs                  # T8
    ├── SplittingStreamTests.cs         # T9
    ├── LoopbackTlsServer.cs            # T9
    ├── TlsProbeTests.cs                # T9
    └── ProberNetworkTests.cs           # T10 [NetworkFact]
```

Số test mong đợi sau từng task (lúc bắt đầu: 115 xanh + 2 skipped): T1 127 · T2 135 · T3 153 · T4 160 · T5 171 · T6 194 · T7 221 · T8 234 · T9 242 · T10 242 + 4 skipped. Nếu lệch vài test thì vẫn được; điều bắt buộc là `Failed: 0`.

Mọi lệnh `dotnet` dưới đây chạy ở gốc repo, sau khi đã đặt `$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"`.

---

### Task 1: `DnsWire.BuildQuery` và `DnsWire.ReadAddresses`

**Files:**
- Modify: `src/LunaDns.Core/Dns/DnsWire.cs` (thêm hằng và hai hàm; không sửa hàm cũ)
- Modify: `tests/LunaDns.Core.Tests/Dns/TestDnsMessage.cs` (thêm một hàm)
- Test: `tests/LunaDns.Core.Tests/Dns/DnsWireAddressTests.cs`

**Interfaces:**
- Consumes (KH2, cùng file): `DnsWire.HeaderLength`, `DnsWire.TrySkipName(ReadOnlySpan<byte>, ref int)`, hàm private `Read16(ReadOnlySpan<byte>, int)`, `DnsWire.TryParseQuery`, `DomainMatcher.Normalize(string)`.
- Produces (trong `static class DnsWire`):
  - `public const ushort TypeA = 1`, `public const ushort TypeAaaa = 28`
  - `public static byte[] BuildQuery(string name, ushort type)`: truy vấn 1 question, ID = 0, RD = 1, class IN. Tên sai (rỗng, label rỗng, label > 63, ký tự ngoài ASCII in được, dài > 253) thì ném `ArgumentException`.
  - `public static IReadOnlyList<IPAddress> ReadAddresses(ReadOnlySpan<byte> response)`: mọi bản ghi A/AAAA class IN ở phần answer, theo đúng thứ tự. Gói sai định dạng thì trả danh sách rỗng, không ném.
  - Test: `TestDnsMessage.ResponseWithAddresses(string name, ushort id, params IPAddress[] addresses)`.

- [ ] **Step 1: Thêm builder vào `TestDnsMessage`**

Thêm `using System.Net;` ở đầu file `tests/LunaDns.Core.Tests/Dns/TestDnsMessage.cs`, rồi thêm hàm này ngay sau `NxDomain(...)`:

```csharp
    /// <summary>Response NOERROR, mỗi địa chỉ một bản ghi A hoặc AAAA, TTL 300.</summary>
    public static byte[] ResponseWithAddresses(string name, ushort id, params IPAddress[] addresses)
    {
        var m = new List<byte>();
        Header(m, id, 0x8180, qd: 1, an: addresses.Length, ns: 0, ar: 0);
        Question(m, name, TypeA);
        foreach (var address in addresses)
        {
            var bytes = address.GetAddressBytes();
            m.Add(0xC0);
            m.Add(0x0C);
            U16(m, bytes.Length == 4 ? TypeA : TypeAaaa);
            U16(m, 1);
            U32(m, 300);
            U16(m, (ushort)bytes.Length);
            m.AddRange(bytes);
        }
        return [.. m];
    }
```

- [ ] **Step 2: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Dns/DnsWireAddressTests.cs
using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DnsWireAddressTests
{
    private const string Name = "store.steampowered.com";

    [Fact]
    public void BuildQuery_is_accepted_by_TryParseQuery()
    {
        var q = DnsWire.BuildQuery(Name, DnsWire.TypeA);

        Assert.True(DnsWire.TryParseQuery(q, out var parsed));
        Assert.Equal(0, parsed.Id);
        Assert.Equal(new DnsQuestion(Name, DnsWire.TypeA, 1), parsed.Question);
        Assert.Equal(0x01, q[2] & 0x01); // RD = 1
    }

    [Fact]
    public void BuildQuery_normalizes_case_and_trailing_dot() =>
        Assert.Equal(DnsWire.BuildQuery(Name, DnsWire.TypeAaaa), DnsWire.BuildQuery("Store.SteamPowered.com.", DnsWire.TypeAaaa));

    [Theory]
    [InlineData("")]
    [InlineData("a..com")]
    [InlineData("has space.com")]
    public void BuildQuery_rejects_bad_names(string bad) =>
        Assert.Throws<ArgumentException>(() => DnsWire.BuildQuery(bad, DnsWire.TypeA));

    [Fact]
    public void BuildQuery_rejects_label_longer_than_63() =>
        Assert.Throws<ArgumentException>(() => DnsWire.BuildQuery(new string('a', 64) + ".com", DnsWire.TypeA));

    [Fact]
    public void ReadAddresses_returns_every_A_record()
    {
        var addresses = DnsWire.ReadAddresses(TestDnsMessage.Response(Name, 0, 300, 300));

        Assert.Equal(2, addresses.Count);
        Assert.All(addresses, a => Assert.Equal(IPAddress.Parse("93.184.216.34"), a));
    }

    [Fact]
    public void ReadAddresses_keeps_order_and_reads_AAAA()
    {
        IPAddress[] expected = [IPAddress.Parse("23.1.2.3"), IPAddress.Parse("2a02:26f0::1"), IPAddress.Parse("23.1.2.4")];

        Assert.Equal(expected, DnsWire.ReadAddresses(TestDnsMessage.ResponseWithAddresses(Name, 0, expected)));
    }

    [Fact]
    public void ReadAddresses_ignores_OPT_in_additional() =>
        Assert.Single(DnsWire.ReadAddresses(TestDnsMessage.ResponseWithOpt(Name, 0, 300)));

    [Fact]
    public void ReadAddresses_is_empty_for_NXDOMAIN() =>
        Assert.Empty(DnsWire.ReadAddresses(TestDnsMessage.NxDomain(Name, 0, 60)));

    [Fact]
    public void ReadAddresses_is_empty_for_truncated_message()
    {
        var r = TestDnsMessage.Response(Name, 0, 300);

        Assert.Empty(DnsWire.ReadAddresses(r.AsSpan(0, r.Length - 3)));
    }

    [Fact]
    public void ReadAddresses_is_empty_for_too_short_message() =>
        Assert.Empty(DnsWire.ReadAddresses(new byte[5]));
}
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsWireAddressTests`
Expected: FAIL khi biên dịch, báo `'DnsWire' does not contain a definition for 'BuildQuery'`.

- [ ] **Step 4: Thêm vào class `DnsWire`**

Thêm `using System.Net;` ở đầu file. Đặt hai hằng cạnh `TypeOpt`, hai hàm ngay sau `IsResponseTo`:

```csharp
    public const ushort TypeA = 1;
    public const ushort TypeAaaa = 28;
```

```csharp
    /// <summary>Dựng truy vấn một question, ID = 0, RD = 1, class IN. Dùng cho resolver nội bộ (Prober, proxy).</summary>
    public static byte[] BuildQuery(string name, ushort type)
    {
        ArgumentNullException.ThrowIfNull(name);
        var host = DomainMatcher.Normalize(name);
        if (host.Length is 0 or > 253)
            throw new ArgumentException($"Tên miền không hợp lệ: '{name}'", nameof(name));

        var msg = new byte[HeaderLength + host.Length + 2 + 4];
        msg[2] = 0x01; // RD
        msg[5] = 1;    // QDCOUNT
        var pos = HeaderLength;
        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > 63)
                throw new ArgumentException($"Tên miền không hợp lệ: '{name}'", nameof(name));
            msg[pos++] = (byte)label.Length;
            foreach (var c in label)
            {
                if (c <= 0x20 || c >= 0x7F)
                    throw new ArgumentException($"Tên miền không hợp lệ: '{name}'", nameof(name));
                msg[pos++] = (byte)c;
            }
        }
        msg[pos++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(pos), type);
        BinaryPrimitives.WriteUInt16BigEndian(msg.AsSpan(pos + 2), 1);
        return msg;
    }

    /// <summary>Địa chỉ trong các bản ghi A/AAAA (class IN) của phần answer. Gói sai định dạng thì trả rỗng.</summary>
    public static IReadOnlyList<IPAddress> ReadAddresses(ReadOnlySpan<byte> response)
    {
        if (response.Length < HeaderLength) return [];
        var offset = HeaderLength;
        for (var i = Read16(response, 4); i > 0; i--)
        {
            if (!TrySkipName(response, ref offset) || offset + 4 > response.Length) return [];
            offset += 4;
        }

        var result = new List<IPAddress>();
        for (var i = Read16(response, 6); i > 0; i--)
        {
            if (!TrySkipName(response, ref offset) || offset + 10 > response.Length) return [];
            var type = Read16(response, offset);
            var cls = Read16(response, offset + 2);
            int length = Read16(response, offset + 8);
            offset += 10;
            if (offset + length > response.Length) return [];
            if (cls == 1 && ((type == TypeA && length == 4) || (type == TypeAaaa && length == 16)))
                result.Add(new IPAddress(response.Slice(offset, length)));
            offset += length;
        }
        return result;
    }
```

Ghi chú kích thước: phần tên trên wire dài `host.Length + 2` byte (mỗi dấu chấm thành một byte độ dài, thêm byte độ dài của label đầu và byte 0 kết thúc).

- [ ] **Step 5: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsWireAddressTests`
Expected: PASS, 12 test.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 127, Skipped: 2`.

---

### Task 2: `IHostResolver`, `SystemHostResolver`, `DohHostResolver`

**Files:**
- Create: `src/LunaDns.Core/Dns/IHostResolver.cs`, `src/LunaDns.Core/Dns/SystemHostResolver.cs`, `src/LunaDns.Core/Dns/DohHostResolver.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/HostResolverTests.cs`

**Interfaces:**
- Consumes: Task 1 (`DnsWire.BuildQuery`, `ReadAddresses`, `TypeA`); KH2: `IDnsUpstream.QueryAsync(ReadOnlyMemory<byte>, CancellationToken)` (ném `DohException` khi mọi máy chủ lỗi), `DnsCache.TryGet(DnsQuestion, ushort, out byte[]?)`, `DnsCache.Store(DnsQuestion, ReadOnlySpan<byte>)`, `DnsWire.IsResponseTo`, `DnsWire.ReadRcode`, `DnsRcode`, `DohException(string)`; test: `FakeUpstream` (`Respond`, `Calls`), `TestDnsMessage`.
- Produces (namespace `LunaDns.Core.Dns`):
  - `interface IHostResolver { Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken); }`. Chỉ IPv4. Tên không tồn tại hoặc không có bản ghi A thì trả danh sách rỗng.
  - `sealed class SystemHostResolver(ILogger<SystemHostResolver>? logger = null) : IHostResolver`. Lỗi DNS hệ thống (`SocketException`) thành danh sách rỗng và một dòng log.
  - `sealed class DohHostResolver(IDnsUpstream upstream, DnsCache? cache = null) : IHostResolver`. Ném `DohException` khi upstream lỗi, trả lời sai câu hỏi, hoặc RCODE khác NOERROR/NXDOMAIN.

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Dns/HostResolverTests.cs
using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class HostResolverTests
{
    private const string Name = "store.steampowered.com";
    private static readonly IPAddress Sample = IPAddress.Parse("93.184.216.34");

    private readonly FakeUpstream _upstream = new();

    [Fact]
    public async Task Doh_returns_addresses_from_answer()
    {
        var resolver = new DohHostResolver(_upstream);

        var addresses = await resolver.ResolveAsync("Store.SteamPowered.com.", CancellationToken.None);

        Assert.Equal(Sample, Assert.Single(addresses));
    }

    [Fact]
    public async Task Doh_returns_empty_for_NXDOMAIN()
    {
        _upstream.Respond = _ => TestDnsMessage.NxDomain(Name, 0, 60);

        Assert.Empty(await new DohHostResolver(_upstream).ResolveAsync(Name, CancellationToken.None));
    }

    [Fact]
    public async Task Doh_throws_on_SERVFAIL()
    {
        _upstream.Respond = q => DnsWire.BuildHeaderOnlyResponse(q, DnsRcode.ServFail);

        await Assert.ThrowsAsync<DohException>(() => new DohHostResolver(_upstream).ResolveAsync(Name, CancellationToken.None));
    }

    [Fact]
    public async Task Doh_throws_when_answer_is_for_another_name()
    {
        _upstream.Respond = _ => TestDnsMessage.Response("other.example", 0, 300);

        await Assert.ThrowsAsync<DohException>(() => new DohHostResolver(_upstream).ResolveAsync(Name, CancellationToken.None));
    }

    [Fact]
    public async Task Doh_with_cache_asks_upstream_once()
    {
        var resolver = new DohHostResolver(_upstream, new DnsCache());

        await resolver.ResolveAsync(Name, CancellationToken.None);
        var second = await resolver.ResolveAsync(Name, CancellationToken.None);

        Assert.Equal(1, _upstream.Calls);
        Assert.Equal(Sample, Assert.Single(second));
    }

    [Fact]
    public async Task Doh_without_cache_asks_every_time()
    {
        var resolver = new DohHostResolver(_upstream);

        await resolver.ResolveAsync(Name, CancellationToken.None);
        await resolver.ResolveAsync(Name, CancellationToken.None);

        Assert.Equal(2, _upstream.Calls);
    }

    [Fact]
    public async Task System_resolves_localhost_to_loopback() =>
        Assert.Contains(IPAddress.Loopback, await new SystemHostResolver().ResolveAsync("localhost", CancellationToken.None));

    [Fact]
    public async Task System_returns_ip_literal_as_is() =>
        Assert.Equal(Sample, Assert.Single(await new SystemHostResolver().ResolveAsync("93.184.216.34", CancellationToken.None)));
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~HostResolverTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'DohHostResolver' could not be found`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Dns/IHostResolver.cs
using System.Net;

namespace LunaDns.Core.Dns;

/// <summary>
/// "Tên → danh sách IPv4". Tên không tồn tại hoặc không có bản ghi A thì trả danh sách rỗng.
/// v1 chỉ dùng IPv4: Prober so sánh hai nguồn trên cùng một họ địa chỉ, và proxy nối ra bằng IPv4.
/// </summary>
public interface IHostResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
```

```csharp
// src/LunaDns.Core/Dns/SystemHostResolver.cs
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>Hỏi DNS của hệ điều hành, tức là đúng thứ game và trình duyệt đang thấy (có thể đã bị đầu độc).</summary>
public sealed partial class SystemHostResolver : IHostResolver
{
    private readonly ILogger _logger;

    public SystemHostResolver(ILogger<SystemHostResolver>? logger = null) =>
        _logger = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            // Viết đủ System.Net.Dns: trong namespace này "Dns" là LunaDns.Core.Dns.
            return await System.Net.Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            // NXDOMAIN, SERVFAIL hay DNS không trả lời: với Prober đều là "DNS hệ thống không cho IP dùng được".
            LogFailed(_logger, host, ex);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DNS hệ thống không phân giải được {Host}")]
    private static partial void LogFailed(ILogger logger, string host, Exception exception);
}
```

```csharp
// src/LunaDns.Core/Dns/DohHostResolver.cs
using System.Net;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Dns;

/// <summary>Phân giải qua DoH (hoặc bất kỳ <see cref="IDnsUpstream"/> nào), không đụng DNS hệ thống.</summary>
public sealed class DohHostResolver : IHostResolver
{
    private readonly IDnsUpstream _upstream;
    private readonly DnsCache? _cache;

    public DohHostResolver(IDnsUpstream upstream, DnsCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        _upstream = upstream;
        _cache = cache;
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        var query = DnsWire.BuildQuery(host, DnsWire.TypeA);
        var question = new DnsQuestion(DomainMatcher.Normalize(host), DnsWire.TypeA, 1);
        if (_cache is not null && _cache.TryGet(question, 0, out var cached))
            return DnsWire.ReadAddresses(cached);

        var response = await _upstream.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        if (!DnsWire.IsResponseTo(response, question))
            throw new DohException($"Máy chủ DoH trả lời sai câu hỏi cho {question.Name}.");
        var rcode = DnsWire.ReadRcode(response);
        if (rcode is not (DnsRcode.NoError or DnsRcode.NxDomain))
            throw new DohException($"Máy chủ DoH trả mã lỗi {rcode} cho {question.Name}.");

        _cache?.Store(question, response);
        return DnsWire.ReadAddresses(response);
    }
}
```

- [ ] **Step 4: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~HostResolverTests`
Expected: PASS, 8 test.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 135, Skipped: 2`.

---

### Task 3: `ProxyRequestParser`

**Files:**
- Create: `src/LunaDns.Core/Proxy/ProxyRequest.cs`, `src/LunaDns.Core/Proxy/ProxyRequestParser.cs`
- Test: `tests/LunaDns.Core.Tests/Proxy/ProxyRequestParserTests.cs`

**Interfaces:**
- Consumes: `DomainMatcher.Normalize(string)` (KH1).
- Produces (namespace `LunaDns.Core.Proxy`):
  - `sealed record ProxyRequest(string Method, string Target, string? ConnectHost, int ConnectPort)`. `ConnectHost` khác null khi và chỉ khi `Method == "CONNECT"`; khi đó host đã chuẩn hoá (chữ thường, không dấu chấm cuối) và `ConnectPort` trong 1–65535.
  - `static class ProxyRequestParser`:
    - `const int MaxHeadLength = 8192`
    - `int FindHeadEnd(ReadOnlySpan<byte> data)`: vị trí ngay sau `\r\n\r\n` đầu tiên, hoặc -1.
    - `bool TryParse(ReadOnlySpan<byte> head, [NotNullWhen(true)] out ProxyRequest? request)`: chỉ đọc dòng đầu. Từ chối: ký tự ngoài ASCII in được, không đúng 3 phần cách nhau một dấu cách, version khác `HTTP/1.1`/`HTTP/1.0`, method không phải chữ hoa, CONNECT thiếu cổng, cổng ngoài 1–65535, host có ký tự ngoài `[a-z0-9.-]` (nên IPv6 literal `[::1]` bị loại).

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Proxy/ProxyRequestParserTests.cs
using System.Text;
using LunaDns.Core.Proxy;

namespace LunaDns.Core.Tests.Proxy;

public class ProxyRequestParserTests
{
    private static byte[] Head(string requestLine) =>
        Encoding.ASCII.GetBytes(requestLine + "\r\nHost: x\r\n\r\n");

    [Fact]
    public void Parses_CONNECT_host_and_port()
    {
        Assert.True(ProxyRequestParser.TryParse(Head("CONNECT store.steampowered.com:443 HTTP/1.1"), out var r));

        Assert.Equal("CONNECT", r.Method);
        Assert.Equal("store.steampowered.com", r.ConnectHost);
        Assert.Equal(443, r.ConnectPort);
    }

    [Fact]
    public void Normalizes_CONNECT_host()
    {
        Assert.True(ProxyRequestParser.TryParse(Head("CONNECT Store.SteamPowered.COM.:443 HTTP/1.1"), out var r));

        Assert.Equal("store.steampowered.com", r.ConnectHost);
    }

    [Fact]
    public void Parses_GET_without_connect_host()
    {
        Assert.True(ProxyRequestParser.TryParse(Head("GET /proxy.pac HTTP/1.1"), out var r));

        Assert.Equal("GET", r.Method);
        Assert.Equal("/proxy.pac", r.Target);
        Assert.Null(r.ConnectHost);
    }

    [Fact]
    public void Accepts_HTTP_1_0() =>
        Assert.True(ProxyRequestParser.TryParse(Head("CONNECT store.steampowered.com:443 HTTP/1.0"), out _));

    [Theory]
    [InlineData("CONNECT store.steampowered.com HTTP/1.1")]
    [InlineData("CONNECT store.steampowered.com:0 HTTP/1.1")]
    [InlineData("CONNECT store.steampowered.com:70000 HTTP/1.1")]
    [InlineData("CONNECT store.steampowered.com:+443 HTTP/1.1")]
    [InlineData("CONNECT [::1]:443 HTTP/1.1")]
    [InlineData("CONNECT :443 HTTP/1.1")]
    [InlineData("GET /proxy.pac")]
    [InlineData("GET  /proxy.pac HTTP/1.1")]
    [InlineData("GET /proxy.pac HTTP/2")]
    [InlineData("get /proxy.pac HTTP/1.1")]
    public void Rejects_malformed_request_line(string line) =>
        Assert.False(ProxyRequestParser.TryParse(Head(line), out _));

    [Fact]
    public void Rejects_control_and_non_ascii_bytes()
    {
        byte[] head = [.. "GET /"u8, 0x01, 0xC3, 0xA9, .. " HTTP/1.1\r\n\r\n"u8];

        Assert.False(ProxyRequestParser.TryParse(head, out _));
    }

    [Fact]
    public void Rejects_head_without_line_ending() =>
        Assert.False(ProxyRequestParser.TryParse("GET /proxy.pac HTTP/1.1"u8, out _));

    [Fact]
    public void FindHeadEnd_points_just_after_blank_line()
    {
        var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\nEXTRA");

        Assert.Equal(data.Length - 5, ProxyRequestParser.FindHeadEnd(data));
    }

    [Fact]
    public void FindHeadEnd_is_minus_one_when_incomplete() =>
        Assert.Equal(-1, ProxyRequestParser.FindHeadEnd("GET / HTTP/1.1\r\nHost: x\r\n"u8));
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~ProxyRequestParserTests`
Expected: FAIL khi biên dịch, báo `The name 'ProxyRequestParser' does not exist in the current context`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Proxy/ProxyRequest.cs
namespace LunaDns.Core.Proxy;

/// <summary>Dòng yêu cầu HTTP gửi tới proxy. <see cref="ConnectHost"/> khác null khi và chỉ khi Method là CONNECT.</summary>
public sealed record ProxyRequest(string Method, string Target, string? ConnectHost, int ConnectPort);
```

```csharp
// src/LunaDns.Core/Proxy/ProxyRequestParser.cs
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Proxy;

public static class ProxyRequestParser
{
    public const int MaxHeadLength = 8192;

    /// <summary>Vị trí ngay sau dòng trống kết thúc head, hoặc -1 nếu chưa nhận đủ.</summary>
    public static int FindHeadEnd(ReadOnlySpan<byte> data)
    {
        var index = data.IndexOf("\r\n\r\n"u8);
        return index < 0 ? -1 : index + 4;
    }

    /// <summary>Đọc dòng đầu của head. Header phía sau không được dùng tới.</summary>
    public static bool TryParse(ReadOnlySpan<byte> head, [NotNullWhen(true)] out ProxyRequest? request)
    {
        request = null;
        var lineEnd = head.IndexOf("\r\n"u8);
        if (lineEnd <= 0) return false;
        var line = head[..lineEnd];
        foreach (var b in line)
        {
            if (b < 0x20 || b >= 0x7F) return false;
        }

        var parts = Encoding.ASCII.GetString(line).Split(' ');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0) return false;
        if (parts[2] is not ("HTTP/1.1" or "HTTP/1.0")) return false;
        var method = parts[0];
        foreach (var c in method)
        {
            if (c is < 'A' or > 'Z') return false;
        }

        if (method != "CONNECT")
        {
            request = new ProxyRequest(method, parts[1], null, 0);
            return true;
        }

        var authority = parts[1];
        var colon = authority.LastIndexOf(':');
        if (colon <= 0) return false;
        var host = DomainMatcher.Normalize(authority[..colon]);
        if (host.Length == 0) return false;
        foreach (var c in host)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-')) return false;
        }
        if (!int.TryParse(authority.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port)) return false;
        if (port is < 1 or > 65535) return false;

        request = new ProxyRequest(method, authority, host, port);
        return true;
    }
}
```

- [ ] **Step 4: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~ProxyRequestParserTests`
Expected: PASS, 18 test (4 + 10 + 4).

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 153, Skipped: 2`.

---

### Task 4: PAC chỉ đẩy `https:`/`wss:` qua proxy, và `SplitWriter`

Proxy của spec 4.3 chỉ hiểu `CONNECT`. PAC của KH1 lại trả `PROXY` cho mọi URL tới domain game, kể cả `http://`; trình duyệt khi đó gửi `GET http://host/…` tới proxy, nhận lỗi và **không** tự chuyển sang `DIRECT` (chỉ chuyển khi không nối được tới proxy). Task này sửa PAC để URL không mã hoá đi thẳng, và thêm hàm ghi cắt gói dùng chung cho proxy (Task 6) và `TlsProbe` (Task 9).

**Files:**
- Modify: `src/LunaDns.Core/Proxy/PacBuilder.cs` (thêm đúng một dòng `sb.Append`)
- Modify: `tests/LunaDns.Core.Tests/Proxy/PacBuilderTests.cs` (thêm 2 test)
- Create: `src/LunaDns.Core/Tls/SplitWriter.cs`
- Test: `tests/LunaDns.Core.Tests/Tls/RecordingStream.cs`, `tests/LunaDns.Core.Tests/Tls/SplitWriterTests.cs`

**Interfaces:**
- Consumes (KH1): `HelloSplitter.Split(ReadOnlyMemory<byte>, SplitMode)` trả `IReadOnlyList<ReadOnlyMemory<byte>>`; `ClientHelloParser.TryFindSni(ReadOnlySpan<byte>, out SniLocation)`; `SniLocation(int Offset, int Length, string Host)`; `SplitMode { FirstByte, MidSni }`; test: `TestClientHello.Build(string? sni, int paddingLength = 0)`.
- Produces:
  - `PacBuilder.Build(int port, IEnumerable<string> domains)`: chữ ký giữ nguyên; PAC có thêm dòng kiểm tra scheme ở đầu hàm.
  - `LunaDns.Core.Tls.SplitWriter`:
    - `static readonly TimeSpan MaxDelay` = 20 ms
    - `static Task WriteAsync(Stream destination, ReadOnlyMemory<byte> hello, SplitMode mode, TimeSpan delay, CancellationToken cancellationToken)`: mỗi mảnh một lần `WriteAsync` + `FlushAsync`; `delay` (0–20 ms) chờ giữa hai mảnh. `delay` ngoài khoảng thì ném `ArgumentOutOfRangeException`.
  - Test: `RecordingStream` (namespace `LunaDns.Core.Tests.Tls`): `IReadOnlyList<byte[]> Writes` (mỗi lần ghi một phần tử), `int TotalBytes`, đọc thì treo tới khi stream bị `Dispose` hoặc token huỷ. Task 6 và 9 dùng lại.

- [ ] **Step 1: Viết `RecordingStream`**

```csharp
// tests/LunaDns.Core.Tests/Tls/RecordingStream.cs
namespace LunaDns.Core.Tests.Tls;

/// <summary>Stream giả: ghi lại từng lần Write thành một mảng riêng; Read treo tới khi Dispose hoặc token huỷ.</summary>
internal sealed class RecordingStream : Stream
{
    private readonly TaskCompletionSource<int> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<byte[]> _writes = [];

    public IReadOnlyList<byte[]> Writes
    {
        get
        {
            lock (_writes) return [.. _writes];
        }
    }

    public int TotalBytes
    {
        get
        {
            lock (_writes) return _writes.Sum(w => w.Length);
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        new(_closed.Task.WaitAsync(cancellationToken));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        lock (_writes) _writes.Add(buffer.AsSpan(offset, count).ToArray());
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        lock (_writes) _writes.Add(buffer.ToArray());
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        _closed.TrySetResult(0);
        base.Dispose(disposing);
    }
}
```

- [ ] **Step 2: Viết test lỗi**

Thêm vào cuối class `PacBuilderTests` (file đã có sẵn 5 test, giữ nguyên):

```csharp
    [Fact]
    public void Sends_only_https_and_wss_through_proxy()
    {
        var pac = PacBuilder.Build(18080, ["steampowered.com"]);

        Assert.Contains("if (url.substring(0, 6) != \"https:\" && url.substring(0, 4) != \"wss:\") return \"DIRECT\";", pac);
    }

    [Fact]
    public void Checks_scheme_before_matching_domains()
    {
        var pac = PacBuilder.Build(18080, ["steampowered.com"]);

        Assert.True(pac.IndexOf("url.substring(0, 6)", StringComparison.Ordinal) < pac.IndexOf("var d = [", StringComparison.Ordinal));
    }
```

```csharp
// tests/LunaDns.Core.Tests/Tls/SplitWriterTests.cs
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class SplitWriterTests
{
    private static readonly byte[] Hello = TestClientHello.Build("store.steampowered.com");

    [Fact]
    public async Task FirstByte_writes_one_byte_then_the_rest()
    {
        using var stream = new RecordingStream();

        await SplitWriter.WriteAsync(stream, Hello, SplitMode.FirstByte, TimeSpan.Zero, CancellationToken.None);

        var writes = stream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello, writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task MidSni_cuts_inside_the_host_name()
    {
        using var stream = new RecordingStream();
        Assert.True(ClientHelloParser.TryFindSni(Hello, out var sni));

        await SplitWriter.WriteAsync(stream, Hello, SplitMode.MidSni, TimeSpan.Zero, CancellationToken.None);

        var writes = stream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Equal(sni.Offset + sni.Length / 2, writes[0].Length);
        Assert.Equal(Hello, writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task Delay_between_pieces_keeps_bytes_intact()
    {
        using var stream = new RecordingStream();

        await SplitWriter.WriteAsync(stream, Hello, SplitMode.FirstByte, TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.Equal(Hello, stream.Writes.SelectMany(w => w).ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task Rejects_delay_outside_0_to_20_ms(int milliseconds)
    {
        using var stream = new RecordingStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SplitWriter.WriteAsync(stream, Hello, SplitMode.FirstByte, TimeSpan.FromMilliseconds(milliseconds), CancellationToken.None));
    }
}
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplitWriterTests|FullyQualifiedName~PacBuilderTests"`
Expected: FAIL khi biên dịch, báo `The name 'SplitWriter' does not exist in the current context`.

- [ ] **Step 4: Viết code**

Trong `src/LunaDns.Core/Proxy/PacBuilder.cs`, thêm một dòng ngay sau dòng `sb.Append("function FindProxyForURL(url, host) {\n");`:

```csharp
        // Proxy chỉ hiểu CONNECT (spec 4.3): URL không mã hoá phải đi thẳng, nếu không trình duyệt sẽ kẹt ở lỗi của proxy.
        sb.Append("  if (url.substring(0, 6) != \"https:\" && url.substring(0, 4) != \"wss:\") return \"DIRECT\";\n");
```

```csharp
// src/LunaDns.Core/Tls/SplitWriter.cs
namespace LunaDns.Core.Tls;

/// <summary>
/// Ghi ClientHello thành nhiều lần Write theo <see cref="HelloSplitter"/>. Với socket bật NoDelay, mỗi lần Write
/// thành một TCP segment riêng, nên bộ lọc SNI không thấy trọn tên host trong một gói (spec 4.3).
/// </summary>
public static class SplitWriter
{
    /// <summary>Độ trễ lớn nhất giữa hai mảnh (mục Nâng cao, spec 4.3).</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(20);

    public static async Task WriteAsync(Stream destination, ReadOnlyMemory<byte> hello, SplitMode mode, TimeSpan delay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, MaxDelay);

        var pieces = HelloSplitter.Split(hello, mode);
        for (var i = 0; i < pieces.Count; i++)
        {
            if (i > 0 && delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(pieces[i], cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 5: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplitWriterTests|FullyQualifiedName~PacBuilderTests"`
Expected: PASS. `SplitWriterTests` 5 test; `PacBuilderTests` nhiều hơn trước đúng 2 test.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 160, Skipped: 2`.

---

### Task 5: Tuỳ chọn, lỗi khởi động và `TcpUpstreamConnector`

**Files:**
- Create: `src/LunaDns.Core/Proxy/SplitProxyOptions.cs`, `src/LunaDns.Core/Proxy/ProxyStartError.cs`, `src/LunaDns.Core/Proxy/SplitProxyException.cs`, `src/LunaDns.Core/Proxy/IUpstreamConnector.cs`, `src/LunaDns.Core/Proxy/TcpUpstreamConnector.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/FakeResolver.cs`, `tests/LunaDns.Core.Tests/Proxy/SplitProxyOptionsTests.cs`, `tests/LunaDns.Core.Tests/Proxy/TcpUpstreamConnectorTests.cs`

**Interfaces:**
- Consumes: `IHostResolver` (Task 2), `SplitWriter.MaxDelay` (Task 4), `SplitMode` (KH1), `DohException(string)` (KH2).
- Produces (namespace `LunaDns.Core.Proxy`):
  - `sealed record SplitProxyOptions`: `required IReadOnlyList<string> Domains`; `int FirstPort = 18080`; `int LastPort = 18090`; `SplitMode SplitMode = FirstByte`; `TimeSpan SplitDelay = Zero`; `const int DefaultFirstPort = 18080`, `DefaultLastPort = 18090`; `internal void Validate()` ném `ArgumentException` nếu cổng ngoài 0–65535, `FirstPort > LastPort`, hoặc `SplitDelay` ngoài 0–20 ms. Cổng 0 (để hệ điều hành chọn) chỉ dành cho test.
  - `enum ProxyStartError { Other, PortInUse, AccessDenied }`
  - `sealed class SplitProxyException : Exception` có `ProxyStartError Error { get; }` và constructor `(ProxyStartError error, string message, Exception innerException)`.
  - `interface IUpstreamConnector { Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken); }`. Không nối được thì ném (`IOException`, `DohException`…); người gọi huỷ thì `OperationCanceledException`.
  - `sealed class TcpUpstreamConnector(IHostResolver resolver) : IUpstreamConnector`: phân giải, thử tối đa 3 địa chỉ đầu, trả `NetworkStream` giữ socket có `NoDelay = true`.
  - Test: `FakeResolver` (namespace `LunaDns.Core.Tests.Dns`): `Func<string, IReadOnlyList<IPAddress>> Respond` (mặc định trả `[127.0.0.1]`), `TimeSpan Delay`, `IReadOnlyList<string> Hosts`. Task 8 dùng lại.

- [ ] **Step 1: Viết `FakeResolver`**

```csharp
// tests/LunaDns.Core.Tests/Dns/FakeResolver.cs
using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

internal sealed class FakeResolver : IHostResolver
{
    private readonly List<string> _hosts = [];

    public Func<string, IReadOnlyList<IPAddress>> Respond { get; set; } = _ => [IPAddress.Loopback];

    public TimeSpan Delay { get; set; }

    public IReadOnlyList<string> Hosts
    {
        get
        {
            lock (_hosts) return [.. _hosts];
        }
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        lock (_hosts) _hosts.Add(host);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        return Respond(host);
    }
}
```

- [ ] **Step 2: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Proxy/SplitProxyOptionsTests.cs
using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Proxy;

public class SplitProxyOptionsTests
{
    [Fact]
    public void Defaults_follow_spec_4_3()
    {
        var options = new SplitProxyOptions { Domains = GameDomains.All };

        Assert.Equal(18080, options.FirstPort);
        Assert.Equal(18090, options.LastPort);
        Assert.Equal(SplitMode.FirstByte, options.SplitMode);
        Assert.Equal(TimeSpan.Zero, options.SplitDelay);
        options.Validate();
    }

    [Theory]
    [InlineData(18090, 18080, 0)]
    [InlineData(18080, 70000, 0)]
    [InlineData(-1, 18090, 0)]
    [InlineData(18080, 18090, 21)]
    [InlineData(18080, 18090, -1)]
    public void Validate_rejects_bad_values(int firstPort, int lastPort, int delayMs)
    {
        var options = new SplitProxyOptions
        {
            Domains = GameDomains.All,
            FirstPort = firstPort,
            LastPort = lastPort,
            SplitDelay = TimeSpan.FromMilliseconds(delayMs),
        };

        Assert.Throws<ArgumentException>(options.Validate);
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Proxy/TcpUpstreamConnectorTests.cs
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Dns;

namespace LunaDns.Core.Tests.Proxy;

public sealed class TcpUpstreamConnectorTests : IDisposable
{
    private const string Host = "store.steampowered.com";
    private static readonly byte[] Ping = [42];

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly FakeResolver _resolver = new();
    private readonly int _port;

    public TcpUpstreamConnectorTests()
    {
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Dispose() => _listener.Stop();

    [Fact]
    public async Task Connects_to_resolved_address_with_nagle_off()
    {
        var connector = new TcpUpstreamConnector(_resolver);

        await using var stream = await connector.ConnectAsync(Host, _port, CancellationToken.None);
        using var accepted = await _listener.AcceptTcpClientAsync();
        await stream.WriteAsync(Ping);
        var got = new byte[1];
        await accepted.GetStream().ReadExactlyAsync(got);

        Assert.Equal(Ping, got);
        Assert.True(Assert.IsType<NetworkStream>(stream).Socket.NoDelay);
        Assert.Equal(Host, Assert.Single(_resolver.Hosts));
    }

    [Fact]
    public async Task Falls_back_to_next_address()
    {
        // 127.0.0.2 không có ai nghe (listener chỉ bind 127.0.0.1); Windows mất khoảng 2 giây mới báo từ chối.
        _resolver.Respond = _ => [IPAddress.Parse("127.0.0.2"), IPAddress.Loopback];

        await using var stream = await new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None);

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task Throws_IOException_when_name_has_no_address()
    {
        _resolver.Respond = _ => [];

        await Assert.ThrowsAsync<IOException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));
    }

    [Fact]
    public async Task Throws_IOException_when_nothing_listens()
    {
        _listener.Stop();

        var ex = await Assert.ThrowsAsync<IOException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));

        Assert.IsType<SocketException>(ex.InnerException);
    }

    [Fact]
    public async Task Lets_resolver_failure_through()
    {
        _resolver.Respond = _ => throw new DohException("DoH chết");

        await Assert.ThrowsAsync<DohException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));
    }
}
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplitProxyOptionsTests|FullyQualifiedName~TcpUpstreamConnectorTests"`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'SplitProxyOptions' could not be found`.

- [ ] **Step 4: Viết code**

```csharp
// src/LunaDns.Core/Proxy/SplitProxyOptions.cs
using LunaDns.Core.Tls;

namespace LunaDns.Core.Proxy;

public sealed record SplitProxyOptions
{
    public const int DefaultFirstPort = 18080;
    public const int DefaultLastPort = 18090;

    /// <summary>Domain được phép CONNECT và có mặt trong PAC. Host khác nhận 403 (spec 4.3).</summary>
    public required IReadOnlyList<string> Domains { get; init; }

    /// <summary>Proxy thử lần lượt từ <see cref="FirstPort"/> tới <see cref="LastPort"/>. Cổng 0 chỉ dùng trong test.</summary>
    public int FirstPort { get; init; } = DefaultFirstPort;

    public int LastPort { get; init; } = DefaultLastPort;

    public SplitMode SplitMode { get; init; } = SplitMode.FirstByte;

    /// <summary>Chờ giữa hai mảnh ClientHello, 0–20 ms. Mặc định 0: chỉ cần NoDelay và hai lần Write.</summary>
    public TimeSpan SplitDelay { get; init; } = TimeSpan.Zero;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Domains);
        if (FirstPort is < 0 or > 65535 || LastPort is < 0 or > 65535 || FirstPort > LastPort)
            throw new ArgumentException($"Khoảng cổng proxy không hợp lệ: {FirstPort}–{LastPort}.");
        if (SplitDelay < TimeSpan.Zero || SplitDelay > SplitWriter.MaxDelay)
            throw new ArgumentException("Độ trễ cắt gói phải nằm trong khoảng 0–20 ms.");
    }
}
```

```csharp
// src/LunaDns.Core/Proxy/ProxyStartError.cs
namespace LunaDns.Core.Proxy;

public enum ProxyStartError
{
    Other,
    PortInUse,
    AccessDenied,
}
```

```csharp
// src/LunaDns.Core/Proxy/SplitProxyException.cs
namespace LunaDns.Core.Proxy;

/// <summary>Proxy không bật được. <see cref="Error"/> để giao diện (KH5) chọn thông báo.</summary>
public sealed class SplitProxyException : Exception
{
    public SplitProxyException()
    {
    }

    public SplitProxyException(string message) : base(message)
    {
    }

    public SplitProxyException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public SplitProxyException(ProxyStartError error, string message, Exception innerException) : base(message, innerException) =>
        Error = error;

    public ProxyStartError Error { get; } = ProxyStartError.Other;
}
```

```csharp
// src/LunaDns.Core/Proxy/IUpstreamConnector.cs
namespace LunaDns.Core.Proxy;

/// <summary>Mở kết nối tới máy chủ thật cho một yêu cầu CONNECT. Không nối được thì ném; người gọi đổi thành 502.</summary>
public interface IUpstreamConnector
{
    Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken);
}
```

```csharp
// src/LunaDns.Core/Proxy/TcpUpstreamConnector.cs
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Proxy;

/// <summary>
/// Nối thẳng bằng socket (không bao giờ qua proxy hệ thống), NoDelay bật sẵn để hai mảnh ClientHello thành hai segment.
/// Nên đưa vào <see cref="DohHostResolver"/>: proxy khi đó không phụ thuộc DNS hệ thống hay NRPT.
/// </summary>
public sealed class TcpUpstreamConnector : IUpstreamConnector
{
    private const int MaxAddresses = 3;
    private readonly IHostResolver _resolver;

    public TcpUpstreamConnector(IHostResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var addresses = await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0)
            throw new IOException($"Không phân giải được {host}.");

        SocketException? last = null;
        foreach (var address in addresses.Take(MaxAddresses))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new IOException($"Không kết nối được tới {host}:{port}.", last);
    }
}
```

- [ ] **Step 5: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplitProxyOptionsTests|FullyQualifiedName~TcpUpstreamConnectorTests"`
Expected: PASS, 11 test (6 + 5). Hai test có địa chỉ chết mất khoảng 2 giây mỗi cái.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 171, Skipped: 2`.

---

### Task 6: `SplitProxy`

**Files:**
- Create: `src/LunaDns.Core/Proxy/SplitProxy.cs`
- Test: `tests/LunaDns.Core.Tests/Proxy/FakeConnector.cs`, `tests/LunaDns.Core.Tests/Proxy/EchoOrigin.cs`, `tests/LunaDns.Core.Tests/Proxy/ProxyTestClient.cs`, `tests/LunaDns.Core.Tests/Proxy/SplitProxyTests.cs`

**Interfaces:**
- Consumes: `ProxyRequestParser.FindHeadEnd/TryParse/MaxHeadLength`, `ProxyRequest` (Task 3); `PacBuilder.Build(int, IEnumerable<string>)`, `SplitWriter.WriteAsync(Stream, ReadOnlyMemory<byte>, SplitMode, TimeSpan, CancellationToken)` (Task 4); `SplitProxyOptions` (+ `Validate()`), `ProxyStartError`, `SplitProxyException`, `IUpstreamConnector` (Task 5); KH1: `DomainMatcher.Matches(string, IEnumerable<string>)`, `ClientHelloParser.RecordLength(ReadOnlySpan<byte>)` (độ dài cả record gồm 5 byte header; -1 nếu chưa đủ 5 byte hoặc không phải record handshake); test: `RecordingStream`, `TestClientHello`.
- Produces (`LunaDns.Core.Proxy.SplitProxy`, `sealed`, `IAsyncDisposable`):
  - `const int TlsPort = 443`
  - `static SplitProxy Start(SplitProxyOptions options, IUpstreamConnector connector, ILogger<SplitProxy>? logger = null)`: bind `127.0.0.1` exclusive ở cổng trống đầu tiên trong khoảng. Hết cổng thì ném `SplitProxyException` (`PortInUse` nếu có cổng báo đang dùng, còn lại `AccessDenied`).
  - `IPEndPoint LocalEndPoint { get; }`, `int Port { get; }`, `Uri PacUrl { get; }` (`http://127.0.0.1:<cổng>/proxy.pac`; KH4 ghi giá trị này vào `AutoConfigURL`)
  - `int ActiveConnections { get; }` (khu vực trạng thái ở KH5)
  - `Task Completion { get; }`: xong khi vòng nghe dừng; faulted nếu socket lỗi bất ngờ. Watchdog ở KH5 chờ task này (spec 6).
  - `ValueTask DisposeAsync()`
- Bảng hành vi (mỗi kết nối một yêu cầu, proxy luôn đóng kết nối sau khi trả lời lỗi hoặc PAC):

| Yêu cầu | Trả lời |
|---|---|
| `GET /proxy.pac` | `200`, `Content-Type: application/x-ns-proxy-autoconfig`, thân là `PacBuilder.Build(Port, Domains)` |
| `GET` đường dẫn khác | `404` |
| Method khác `GET`/`CONNECT` | `405` |
| Target dạng tuyệt đối (`GET http://host/…`) | `403` (không làm proxy HTTP thường) |
| Dòng yêu cầu sai | `400` |
| Head tới 8192 byte mà chưa có dòng trống | `431` |
| `CONNECT` host ngoài `Domains`, hoặc cổng khác 443 | `403`, không gọi connector |
| `CONNECT` hợp lệ nhưng connector ném hoặc quá 5 giây | `502` |
| `CONNECT` hợp lệ | `200 Connection Established`, đọc trọn TLS record đầu, ghi cắt gói, rồi chuyển byte hai chiều |

Dữ liệu đầu tiên không phải record handshake TLS (byte đầu khác `0x16`) hoặc record khai dài hơn 16389 byte thì chuyển nguyên, không cắt.

- [ ] **Step 1: Viết các helper của test**

```csharp
// tests/LunaDns.Core.Tests/Proxy/FakeConnector.cs
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Tls;

namespace LunaDns.Core.Tests.Proxy;

internal sealed class FakeConnector : IUpstreamConnector
{
    private int _calls;

    /// <summary>Mặc định trả một <see cref="RecordingStream"/> mới. Ném ở đây để giả lập máy chủ không nối được.</summary>
    public Func<string, int, Stream> Connect { get; set; } = (_, _) => new RecordingStream();

    public int Calls => Volatile.Read(ref _calls);

    public Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Connect(host, port));
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Proxy/EchoOrigin.cs
using System.Net;
using System.Net.Sockets;

namespace LunaDns.Core.Tests.Proxy;

/// <summary>Máy chủ TCP loopback trả lại đúng byte nhận được, đóng vai máy chủ thật sau proxy.</summary>
internal sealed class EchoOrigin : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public EchoOrigin()
    {
        _listener.Start();
        _loop = AcceptLoopAsync();
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public Stream Connect()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        socket.Connect(IPAddress.Loopback, Port);
        return new NetworkStream(socket, ownsSocket: true);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            _ = EchoAsync(client);
        }
    }

    private async Task EchoAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                await stream.CopyToAsync(stream, _stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                // Proxy đóng kết nối hoặc test kết thúc: hết việc của kết nối này.
                return;
            }
        }
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Proxy/ProxyTestClient.cs
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LunaDns.Core.Tests.Proxy;

internal static class ProxyTestClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task<NetworkStream> ConnectAsync(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(IPAddress.Loopback, port);
        return new NetworkStream(socket, ownsSocket: true);
    }

    public static async Task SendAsync(Stream stream, string text) =>
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text));

    /// <summary>Gửi một yêu cầu rồi đọc tới khi proxy đóng kết nối.</summary>
    public static async Task<string> RequestAsync(int port, string head)
    {
        await using var stream = await ConnectAsync(port);
        await SendAsync(stream, head);
        using var timeout = new CancellationTokenSource(Timeout);
        using var all = new MemoryStream();
        await stream.CopyToAsync(all, timeout.Token);
        return Encoding.ASCII.GetString(all.ToArray());
    }

    /// <summary>Đọc từng byte tới hết dòng trống, để không nuốt mất dữ liệu của tunnel phía sau.</summary>
    public static async Task<string> ReadHeadAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var bytes = new List<byte>();
        var one = new byte[1];
        while (!EndsWithBlankLine(bytes))
        {
            if (await stream.ReadAsync(one, timeout.Token) == 0) break;
            bytes.Add(one[0]);
        }
        return Encoding.ASCII.GetString([.. bytes]);
    }

    public static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, timeout.Token);
        return buffer;
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Hết 5 giây mà điều kiện chưa đạt.");
            await Task.Delay(10);
        }
    }

    private static bool EndsWithBlankLine(List<byte> bytes) =>
        bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n';
}
```

- [ ] **Step 2: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Proxy/SplitProxyTests.cs
using System.Net;
using System.Net.Sockets;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Tls;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Proxy;

public sealed class SplitProxyTests : IAsyncLifetime
{
    private const string Host = "store.steampowered.com";
    private static readonly byte[] Hello = TestClientHello.Build(Host);

    private readonly FakeConnector _connector = new();
    private SplitProxy _proxy = null!;

    public Task InitializeAsync()
    {
        _proxy = Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _proxy.DisposeAsync();

    private SplitProxy Start(SplitMode mode = SplitMode.FirstByte, int firstPort = 0, int lastPort = 0) =>
        SplitProxy.Start(
            new SplitProxyOptions { Domains = GameDomains.All, FirstPort = firstPort, LastPort = lastPort, SplitMode = mode },
            _connector);

    private RecordingStream UseRecordingUpstream()
    {
        var upstream = new RecordingStream();
        _connector.Connect = (_, _) => upstream;
        return upstream;
    }

    private static async Task<NetworkStream> OpenTunnelAsync(SplitProxy proxy, string authority = Host + ":443")
    {
        var stream = await ProxyTestClient.ConnectAsync(proxy.Port);
        await ProxyTestClient.SendAsync(stream, $"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n\r\n");
        var head = await ProxyTestClient.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 200 ", head, StringComparison.Ordinal);
        return stream;
    }

    private static Socket OccupyLoopbackPort()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Listen();
        return socket;
    }

    // ---- khởi động ----

    [Fact]
    public void Listens_on_loopback_and_exposes_pac_url()
    {
        Assert.Equal(IPAddress.Loopback, _proxy.LocalEndPoint.Address);
        Assert.Equal(_proxy.LocalEndPoint.Port, _proxy.Port);
        Assert.Equal(new Uri($"http://127.0.0.1:{_proxy.Port}/proxy.pac"), _proxy.PacUrl);
    }

    [Fact]
    public async Task Takes_next_port_when_first_is_busy()
    {
        // Nếu tình cờ cổng busy + 1 cũng đang có app khác giữ thì test này lỗi; chạy lại là qua.
        using var occupier = OccupyLoopbackPort();
        var busy = ((IPEndPoint)occupier.LocalEndPoint!).Port;

        await using var proxy = Start(firstPort: busy, lastPort: busy + 1);

        Assert.Equal(busy + 1, proxy.Port);
    }

    [Fact]
    public void Throws_PortInUse_when_whole_range_is_busy()
    {
        using var occupier = OccupyLoopbackPort();
        var busy = ((IPEndPoint)occupier.LocalEndPoint!).Port;

        var ex = Assert.Throws<SplitProxyException>(() => Start(firstPort: busy, lastPort: busy));

        Assert.Equal(ProxyStartError.PortInUse, ex.Error);
    }

    [Fact]
    public async Task Dispose_completes_the_accept_loop()
    {
        var proxy = Start();

        await proxy.DisposeAsync();
        await proxy.DisposeAsync(); // gọi hai lần không lỗi

        Assert.True(proxy.Completion.IsCompletedSuccessfully);
    }

    // ---- PAC và yêu cầu cục bộ ----

    [Fact]
    public async Task Serves_pac_built_for_its_own_port()
    {
        var response = await ProxyTestClient.RequestAsync(_proxy.Port, "GET /proxy.pac HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", response, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/x-ns-proxy-autoconfig\r\n", response, StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\n" + PacBuilder.Build(_proxy.Port, GameDomains.All), response, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET / HTTP/1.1", "404")]
    [InlineData("GET /proxy.pac?x=1 HTTP/1.1", "404")]
    [InlineData("POST /proxy.pac HTTP/1.1", "405")]
    [InlineData("GET http://store.steampowered.com/ HTTP/1.1", "403")]
    [InlineData("GET /proxy.pac HTTP/9", "400")]
    public async Task Answers_other_requests_with_status(string requestLine, string status)
    {
        var response = await ProxyTestClient.RequestAsync(_proxy.Port, requestLine + "\r\n\r\n");

        Assert.StartsWith($"HTTP/1.1 {status} ", response, StringComparison.Ordinal);
        Assert.Contains("Connection: close\r\n", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_head_gets_431()
    {
        // Gửi đúng 8192 byte, không có dòng trống: proxy đã đọc hết nên trả lời không bị RST nuốt mất.
        var response = await ProxyTestClient.RequestAsync(_proxy.Port, new string('a', ProxyRequestParser.MaxHeadLength));

        Assert.StartsWith("HTTP/1.1 431 ", response, StringComparison.Ordinal);
    }

    // ---- CONNECT bị từ chối ----

    [Theory]
    [InlineData("example.com:443")]
    [InlineData("evilsteampowered.com:443")]
    [InlineData("store.steampowered.com:80")]
    [InlineData("127.0.0.1:443")]
    public async Task Refuses_connect_outside_game_domains_or_port_443(string authority)
    {
        var response = await ProxyTestClient.RequestAsync(_proxy.Port, $"CONNECT {authority} HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403 ", response, StringComparison.Ordinal);
        Assert.Equal(0, _connector.Calls);
    }

    [Fact]
    public async Task Upstream_failure_becomes_502()
    {
        _connector.Connect = (_, _) => throw new IOException("máy chủ không nối được");

        var response = await ProxyTestClient.RequestAsync(_proxy.Port, $"CONNECT {Host}:443 HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 502 ", response, StringComparison.Ordinal);
    }

    // ---- cắt gói ----

    [Fact]
    public async Task Splits_client_hello_after_first_byte()
    {
        var upstream = UseRecordingUpstream();
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(Hello);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == Hello.Length);

        var writes = upstream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello, writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task MidSni_mode_cuts_inside_host_name()
    {
        await using var proxy = Start(SplitMode.MidSni);
        var upstream = UseRecordingUpstream();
        Assert.True(ClientHelloParser.TryFindSni(Hello, out var sni));
        await using var tunnel = await OpenTunnelAsync(proxy);

        await tunnel.WriteAsync(Hello);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == Hello.Length);

        Assert.Equal(sni.Offset + sni.Length / 2, upstream.Writes[0].Length);
    }

    [Fact]
    public async Task Waits_for_whole_record_before_splitting()
    {
        var upstream = UseRecordingUpstream();
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(Hello.AsMemory(0, 3));
        await Task.Delay(50);
        await tunnel.WriteAsync(Hello.AsMemory(3, 40));
        await Task.Delay(50);
        await tunnel.WriteAsync(Hello.AsMemory(43));
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == Hello.Length);

        var writes = upstream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello.Length - 1, writes[1].Length);
    }

    [Fact]
    public async Task Forwards_bytes_that_follow_the_record()
    {
        var upstream = UseRecordingUpstream();
        byte[] sent = [.. Hello, 1, 2, 3, 4];
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(sent);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == sent.Length);

        var writes = upstream.Writes;
        Assert.Single(writes[0]);
        Assert.Equal(Hello.Length - 1, writes[1].Length);
        Assert.Equal(sent, writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task Forwards_non_tls_payload_unsplit()
    {
        var upstream = UseRecordingUpstream();
        var payload = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n");
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(payload);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == payload.Length);

        Assert.Equal(payload, Assert.Single(upstream.Writes));
    }

    // ---- chuyển tiếp ----

    [Fact]
    public async Task Relays_both_directions()
    {
        await using var origin = new EchoOrigin();
        _connector.Connect = (_, _) => origin.Connect();
        byte[] sent = [.. Hello, .. "ping"u8];
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(sent);

        Assert.Equal(sent, await ProxyTestClient.ReadExactAsync(tunnel, sent.Length));
    }

    [Fact]
    public async Task Counts_open_tunnels()
    {
        UseRecordingUpstream();
        var tunnel = await OpenTunnelAsync(_proxy);
        await ProxyTestClient.WaitUntilAsync(() => _proxy.ActiveConnections == 1);

        await tunnel.DisposeAsync();

        await ProxyTestClient.WaitUntilAsync(() => _proxy.ActiveConnections == 0);
    }
}
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~SplitProxyTests`
Expected: FAIL khi biên dịch, báo `The name 'SplitProxy' does not exist in the current context`.

- [ ] **Step 4: Viết code**

```csharp
// src/LunaDns.Core/Proxy/SplitProxy.cs
using System.Net;
using System.Net.Sockets;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Proxy;

/// <summary>
/// Proxy HTTP CONNECT trên 127.0.0.1 (spec 4.3): phục vụ /proxy.pac, chỉ nhận CONNECT tới domain game ở cổng 443,
/// cắt ClientHello thành nhiều segment rồi chuyển byte hai chiều. Không giải mã TLS.
/// Giới hạn đã đo ở spike: chỉ phần CEF của Steam (Store, Community) đọc PAC; kết nối CM không đi qua proxy này.
/// </summary>
public sealed partial class SplitProxy : IAsyncDisposable
{
    public const int TlsPort = 443;
    private const int MaxConnections = 256;
    private const int MaxRecordLength = 5 + 16384;
    private const int BufferSize = 32 * 1024;
    private static readonly TimeSpan FirstBytesTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private static readonly byte[] Established = "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray();
    private static readonly byte[] BadRequest = Status("400 Bad Request");
    private static readonly byte[] Forbidden = Status("403 Forbidden");
    private static readonly byte[] NotFound = Status("404 Not Found");
    private static readonly byte[] MethodNotAllowed = Status("405 Method Not Allowed");
    private static readonly byte[] HeadTooLarge = Status("431 Request Header Fields Too Large");
    private static readonly byte[] BadGateway = Status("502 Bad Gateway");

    private readonly SplitProxyOptions _options;
    private readonly IUpstreamConnector _connector;
    private readonly ILogger _logger;
    private readonly Socket _listener;
    private readonly byte[] _pacResponse;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(MaxConnections);
    private Task _loop = Task.CompletedTask;
    private int _active;

    private SplitProxy(Socket listener, SplitProxyOptions options, IUpstreamConnector connector, ILogger logger)
    {
        _listener = listener;
        _options = options;
        _connector = connector;
        _logger = logger;
        LocalEndPoint = (IPEndPoint)listener.LocalEndPoint!;
        PacUrl = new Uri($"http://127.0.0.1:{LocalEndPoint.Port}/proxy.pac");

        var pac = Encoding.ASCII.GetBytes(PacBuilder.Build(LocalEndPoint.Port, options.Domains));
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\n" +
            $"Content-Length: {pac.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        _pacResponse = [.. head, .. pac];
    }

    public IPEndPoint LocalEndPoint { get; }

    public int Port => LocalEndPoint.Port;

    /// <summary>Địa chỉ PAC để KH4 ghi vào AutoConfigURL.</summary>
    public Uri PacUrl { get; }

    public int ActiveConnections => Volatile.Read(ref _active);

    /// <summary>Xong khi vòng nghe dừng; faulted nếu socket lỗi bất ngờ. Watchdog (KH5) chờ task này.</summary>
    public Task Completion => _loop;

    public static SplitProxy Start(SplitProxyOptions options, IUpstreamConnector connector, ILogger<SplitProxy>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connector);
        options.Validate();

        var listener = BindFirstFreePort(options.FirstPort, options.LastPort);
        SplitProxy proxy;
        try
        {
            listener.Listen(MaxConnections);
            proxy = new SplitProxy(listener, options, connector, (ILogger?)logger ?? NullLogger.Instance);
        }
        catch
        {
            listener.Dispose();
            throw;
        }
        proxy._loop = Task.Run(proxy.AcceptLoopAsync);
        return proxy;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Dispose();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Vòng nghe đã chết từ trước (Completion faulted); Dispose không ném lại.
            LogLoopFaulted(_logger, ex);
        }
        // Cố ý không Dispose _stop và _slots: kết nối đang xử lý dở vẫn đọc token và trả slot.
    }

    private static byte[] Status(string statusLine) =>
        Encoding.ASCII.GetBytes($"HTTP/1.1 {statusLine}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private static Socket BindFirstFreePort(int firstPort, int lastPort)
    {
        SocketException? last = null;
        var error = ProxyStartError.AccessDenied;
        for (var port = firstPort; port <= lastPort; port++)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                return socket;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                // Cổng đang có app khác giữ (Windows có khi báo AccessDenied thay vì AddressAlreadyInUse): thử cổng kế.
                socket.Dispose();
                last = ex;
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) error = ProxyStartError.PortInUse;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new SplitProxyException(ProxyStartError.Other, $"Không mở được cổng proxy 127.0.0.1:{port}.", ex);
            }
        }
        throw new SplitProxyException(error, $"Không còn cổng trống trong khoảng {firstPort}–{lastPort} cho proxy.", last!);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            if (!_slots.Wait(0))
            {
                LogBusy(_logger);
                client.Dispose();
                continue;
            }
            client.NoDelay = true;
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(Socket client)
    {
        Interlocked.Increment(ref _active);
        var stream = new NetworkStream(client, ownsSocket: true);
        try
        {
            var buffer = new byte[BufferSize];
            var (request, count) = await ReadRequestAsync(stream, buffer).ConfigureAwait(false);
            if (request is null) return;
            if (request.ConnectHost is null)
                await stream.WriteAsync(LocalResponse(request), _stop.Token).ConfigureAwait(false);
            else
                await TunnelAsync(stream, request.ConnectHost, request.ConnectPort, buffer, count).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Ranh giới của một kết nối: lỗi ở đây chỉ đóng kết nối này, không làm chết proxy.
            // Client im lặng quá 10 giây (OperationCanceledException) là đóng bình thường.
            if (ex is not OperationCanceledException && !_stop.IsCancellationRequested) LogConnectionFailed(_logger, ex);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            Interlocked.Decrement(ref _active);
            _slots.Release();
        }
    }

    /// <summary>
    /// Đọc và phân tích request head. Trả request null nếu client đóng sớm hoặc đã trả lỗi 400/431.
    /// Count là số byte client gửi liền sau head, đã được dời về đầu buffer.
    /// </summary>
    private async Task<(ProxyRequest? Request, int Count)> ReadRequestAsync(NetworkStream stream, byte[] buffer)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(FirstBytesTimeout);
        var count = 0;
        int headEnd;
        while ((headEnd = ProxyRequestParser.FindHeadEnd(buffer.AsSpan(0, count))) < 0)
        {
            if (count >= ProxyRequestParser.MaxHeadLength)
            {
                await stream.WriteAsync(HeadTooLarge, _stop.Token).ConfigureAwait(false);
                return (null, 0);
            }
            var read = await stream.ReadAsync(buffer.AsMemory(count, ProxyRequestParser.MaxHeadLength - count), timeout.Token).ConfigureAwait(false);
            if (read == 0) return (null, 0);
            count += read;
        }

        if (!ProxyRequestParser.TryParse(buffer.AsSpan(0, headEnd), out var request))
        {
            await stream.WriteAsync(BadRequest, _stop.Token).ConfigureAwait(false);
            return (null, 0);
        }
        buffer.AsSpan(headEnd, count - headEnd).CopyTo(buffer);
        return (request, count - headEnd);
    }

    private byte[] LocalResponse(ProxyRequest request)
    {
        // Target dạng tuyệt đối (GET http://host/...) là xin làm proxy HTTP thường: từ chối như host ngoài danh sách.
        if (request.Target.Contains("://", StringComparison.Ordinal)) return Forbidden;
        if (request.Method != "GET") return MethodNotAllowed;
        return request.Target == "/proxy.pac" ? _pacResponse : NotFound;
    }

    private async Task TunnelAsync(NetworkStream client, string host, int port, byte[] buffer, int count)
    {
        if (port != TlsPort || !DomainMatcher.Matches(host, _options.Domains))
        {
            // Không ghi tên host: log có thể nằm trong báo cáo chẩn đoán gửi qua chat.
            LogRefused(_logger);
            await client.WriteAsync(Forbidden, _stop.Token).ConfigureAwait(false);
            return;
        }

        Stream upstream;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            connectTimeout.CancelAfter(ConnectTimeout);
            upstream = await _connector.ConnectAsync(host, port, connectTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            LogUpstreamFailed(_logger, host, ex);
            await client.WriteAsync(BadGateway, _stop.Token).ConfigureAwait(false);
            return;
        }

        try
        {
            await client.WriteAsync(Established, _stop.Token).ConfigureAwait(false);
            await ForwardFirstRecordAsync(client, upstream, buffer, count).ConfigureAwait(false);
            await RelayAsync(client, upstream).ConfigureAwait(false);
        }
        finally
        {
            await upstream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Đọc trọn TLS record đầu tiên của client rồi ghi cắt gói sang máy chủ. Dữ liệu không phải record handshake,
    /// hoặc record khai dài quá cỡ, thì chuyển nguyên.
    /// </summary>
    private async Task ForwardFirstRecordAsync(Stream client, Stream upstream, byte[] buffer, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(FirstBytesTimeout);

        count = await FillAsync(client, buffer, count, 5, timeout.Token).ConfigureAwait(false);
        var recordLength = ClientHelloParser.RecordLength(buffer.AsSpan(0, count));
        if (recordLength < 0 || recordLength > MaxRecordLength)
        {
            await upstream.WriteAsync(buffer.AsMemory(0, count), _stop.Token).ConfigureAwait(false);
            return;
        }

        count = await FillAsync(client, buffer, count, recordLength, timeout.Token).ConfigureAwait(false);
        var helloLength = Math.Min(count, recordLength);
        await SplitWriter.WriteAsync(upstream, buffer.AsMemory(0, helloLength), _options.SplitMode, _options.SplitDelay, _stop.Token).ConfigureAwait(false);
        if (count > helloLength)
            await upstream.WriteAsync(buffer.AsMemory(helloLength, count - helloLength), _stop.Token).ConfigureAwait(false);
    }

    /// <summary>Đọc tới khi buffer có ít nhất <paramref name="needed"/> byte. Client đóng sớm thì trả số byte đang có.</summary>
    private static async Task<int> FillAsync(Stream source, byte[] buffer, int count, int needed, CancellationToken cancellationToken)
    {
        while (count < needed)
        {
            var read = await source.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        return count;
    }

    /// <summary>Chuyển byte hai chiều tới khi một bên đóng, rồi dừng chiều còn lại.</summary>
    private async Task RelayAsync(Stream client, Stream upstream)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var up = client.CopyToAsync(upstream, linked.Token);
        var down = upstream.CopyToAsync(client, linked.Token);
        await Task.WhenAny(up, down).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(up, down).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            LogRelayEnded(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quá 256 kết nối tới proxy, đóng kết nối mới")]
    private static partial void LogBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Từ chối CONNECT: host ngoài danh sách domain game hoặc cổng khác 443")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không nối được tới {Host}, trả 502")]
    private static partial void LogUpstreamFailed(ILogger logger, string host, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kết nối tới proxy lỗi")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tunnel đã đóng")]
    private static partial void LogRelayEnded(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vòng nghe của proxy đã dừng vì lỗi")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
```

Ghi chú cho người làm:
- `LogUpstreamFailed` chỉ được gọi sau khi host đã qua `DomainMatcher.Matches`, nên tên host trong log luôn là domain game.
- Buffer 32 KB chứa đủ phần dư sau head (tối đa 8192 byte) cộng một record 16389 byte.
- `RelayAsync` đóng cả hai chiều ngay khi một chiều kết thúc. Với TLS như vậy là đủ; không làm half-close.

- [ ] **Step 5: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~SplitProxyTests`
Expected: PASS, 23 test. Chạy lệnh này 3 lần liên tiếp, cả 3 lần đều phải xanh (test có socket thật).

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 194, Skipped: 2`.

---

### Task 7: Kiểu dữ liệu của Probing, `KnownBlockIps`, `ProbeAdvisor`

**Files:**
- Create: `src/LunaDns.Core/Probing/ProbeVerdict.cs`, `ProbeResult.cs`, `ProbeRemedies.cs`, `ProbeAdvisor.cs`, `ProbeTargets.cs`, `KnownBlockIps.cs` (cùng thư mục `src/LunaDns.Core/Probing/`)
- Test: `tests/LunaDns.Core.Tests/Probing/KnownBlockIpsTests.cs`, `ProbeAdvisorTests.cs`, `ProbeTargetsTests.cs`

**Interfaces:**
- Consumes: `GameDomains.All`, `GameDomains.For(DomainGroup)`, `DomainMatcher.Matches` (KH1, chỉ trong test).
- Produces (namespace `LunaDns.Core.Probing`):
  - `enum ProbeVerdict { Ok, DnsPoisoned, SniFiltered, Unreachable }`
  - `sealed record ProbeResult(string Host, ProbeVerdict Verdict, IReadOnlyList<IPAddress> SystemAddresses, IReadOnlyList<IPAddress> DohAddresses, bool SystemAddressBlocked)`. Hai danh sách địa chỉ để KH5 đưa vào báo cáo chẩn đoán (spec 4.9).
  - `[Flags] enum ProbeRemedies { None = 0, DnsRouting = 1, SniProxy = 2 }`
  - `static class ProbeAdvisor { static ProbeRemedies Recommend(IEnumerable<ProbeResult> results); }`: có `SniFiltered` thì `DnsRouting | SniProxy`; có `DnsPoisoned` thì `DnsRouting`; `Ok` và `Unreachable` không thêm gì (bảng spec 4.1).
  - `static class ProbeTargets`: `IReadOnlyList<string> Steam` (4 host của spec 4.1), `IReadOnlyList<string> Pubg` (1 host), `IReadOnlyList<string> For(bool includePubg)`.
  - `static class KnownBlockIps { static bool IsBlockAddress(IPAddress address); }`: true với địa chỉ mà máy chủ game công khai không bao giờ có (0.0.0.0/8, 10/8, 100.64/10, 127/8, 169.254/16, 172.16/12, 192.168/16, 224.0.0.0/3; IPv6 loopback, link-local, unique-local, `::`).

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Probing/KnownBlockIpsTests.cs
using System.Net;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

public class KnownBlockIpsTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void Flags_addresses_no_public_game_server_can_have(string address) =>
        Assert.True(KnownBlockIps.IsBlockAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("23.50.60.70")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("172.32.0.1")]
    [InlineData("103.10.124.5")]
    [InlineData("2606:4700::1111")]
    public void Leaves_public_addresses_alone(string address) =>
        Assert.False(KnownBlockIps.IsBlockAddress(IPAddress.Parse(address)));
}
```

```csharp
// tests/LunaDns.Core.Tests/Probing/ProbeAdvisorTests.cs
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

public class ProbeAdvisorTests
{
    private static ProbeResult Result(ProbeVerdict verdict) => new("store.steampowered.com", verdict, [], [], false);

    [Fact]
    public void Nothing_to_do_when_everything_is_ok() =>
        Assert.Equal(ProbeRemedies.None, ProbeAdvisor.Recommend([Result(ProbeVerdict.Ok), Result(ProbeVerdict.Ok)]));

    [Fact]
    public void Poisoned_dns_needs_dns_routing_only() =>
        Assert.Equal(ProbeRemedies.DnsRouting, ProbeAdvisor.Recommend([Result(ProbeVerdict.Ok), Result(ProbeVerdict.DnsPoisoned)]));

    [Fact]
    public void Sni_filtering_needs_dns_routing_and_proxy() =>
        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, ProbeAdvisor.Recommend([Result(ProbeVerdict.SniFiltered)]));

    [Fact]
    public void Unreachable_alone_recommends_nothing() =>
        Assert.Equal(ProbeRemedies.None, ProbeAdvisor.Recommend([Result(ProbeVerdict.Unreachable)]));

    [Fact]
    public void Mixed_verdicts_are_combined() =>
        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy,
            ProbeAdvisor.Recommend([Result(ProbeVerdict.DnsPoisoned), Result(ProbeVerdict.Unreachable), Result(ProbeVerdict.SniFiltered)]));
}
```

```csharp
// tests/LunaDns.Core.Tests/Probing/ProbeTargetsTests.cs
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

public class ProbeTargetsTests
{
    private static readonly string[] SpecSteamHosts =
    [
        "store.steampowered.com", "steamcommunity.com", "api.steampowered.com", "cdn.akamai.steamstatic.com",
    ];

    [Fact]
    public void Steam_hosts_are_the_ones_in_spec_4_1() =>
        Assert.Equal(SpecSteamHosts, ProbeTargets.Steam);

    [Fact]
    public void Pubg_host_is_included_only_on_request()
    {
        string[] withPubg = [.. SpecSteamHosts, "prod-live-front.playbattlegrounds.com"];

        Assert.Equal(SpecSteamHosts, ProbeTargets.For(includePubg: false));
        Assert.Equal(withPubg, ProbeTargets.For(includePubg: true));
    }

    [Fact]
    public void Every_target_is_a_game_domain() =>
        Assert.All(ProbeTargets.For(includePubg: true), host => Assert.True(DomainMatcher.Matches(host, GameDomains.All)));
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~LunaDns.Core.Tests.Probing`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'Probing' does not exist in the namespace 'LunaDns.Core'`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Probing/ProbeVerdict.cs
namespace LunaDns.Core.Probing;

/// <summary>Kết luận cho một host (bảng ở spec 4.1).</summary>
public enum ProbeVerdict
{
    /// <summary>DNS hệ thống đúng và TLS nguyên vẹn thành công.</summary>
    Ok,
    /// <summary>DNS hệ thống trả IP sai (hoặc không trả), còn TLS tới IP của DoH thì thành công.</summary>
    DnsPoisoned,
    /// <summary>TLS nguyên vẹn lỗi, TLS cắt gói thành công.</summary>
    SniFiltered,
    /// <summary>Cả hai cách đều lỗi, hoặc DoH không cho được IP nào.</summary>
    Unreachable,
}
```

```csharp
// src/LunaDns.Core/Probing/ProbeResult.cs
using System.Net;

namespace LunaDns.Core.Probing;

/// <summary>Kết quả kiểm tra một host. Hai danh sách địa chỉ dùng cho báo cáo chẩn đoán (spec 4.9).</summary>
public sealed record ProbeResult(
    string Host,
    ProbeVerdict Verdict,
    IReadOnlyList<IPAddress> SystemAddresses,
    IReadOnlyList<IPAddress> DohAddresses,
    bool SystemAddressBlocked);
```

```csharp
// src/LunaDns.Core/Probing/ProbeRemedies.cs
namespace LunaDns.Core.Probing;

/// <summary>Biện pháp đề xuất cho nút "Áp dụng đề xuất" (spec 4.8).</summary>
[Flags]
public enum ProbeRemedies
{
    None = 0,
    DnsRouting = 1,
    SniProxy = 2,
}
```

```csharp
// src/LunaDns.Core/Probing/ProbeAdvisor.cs
namespace LunaDns.Core.Probing;

public static class ProbeAdvisor
{
    /// <summary>Gộp verdict của các host thành biện pháp cần bật (cột "Biện pháp đề xuất" ở spec 4.1).</summary>
    public static ProbeRemedies Recommend(IEnumerable<ProbeResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var remedies = ProbeRemedies.None;
        foreach (var result in results)
        {
            remedies |= result.Verdict switch
            {
                ProbeVerdict.DnsPoisoned => ProbeRemedies.DnsRouting,
                ProbeVerdict.SniFiltered => ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy,
                _ => ProbeRemedies.None,
            };
        }
        return remedies;
    }
}
```

```csharp
// src/LunaDns.Core/Probing/ProbeTargets.cs
namespace LunaDns.Core.Probing;

/// <summary>Host mẫu để kiểm tra (spec 4.1).</summary>
public static class ProbeTargets
{
    public static IReadOnlyList<string> Steam { get; } =
    [
        "store.steampowered.com", "steamcommunity.com", "api.steampowered.com", "cdn.akamai.steamstatic.com",
    ];

    public static IReadOnlyList<string> Pubg { get; } = ["prod-live-front.playbattlegrounds.com"];

    public static IReadOnlyList<string> For(bool includePubg)
    {
        if (!includePubg) return Steam;
        return [.. Steam, .. Pubg];
    }
}
```

```csharp
// src/LunaDns.Core/Probing/KnownBlockIps.cs
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace LunaDns.Core.Probing;

/// <summary>
/// Địa chỉ mà một máy chủ game công khai không bao giờ có. DNS hệ thống trả một địa chỉ như vậy cho domain game
/// thì chắc chắn câu trả lời đã bị sửa. IP trang chặn riêng của từng nhà mạng chưa có số liệu thực địa nên chưa
/// liệt kê; Prober vẫn bắt được chúng nhờ bước kiểm tra chứng chỉ.
/// </summary>
public static class KnownBlockIps
{
    private static readonly (uint Network, int PrefixLength)[] Ranges =
    [
        (0x00000000, 8),  // 0.0.0.0/8
        (0x0A000000, 8),  // 10.0.0.0/8
        (0x64400000, 10), // 100.64.0.0/10 (CGNAT)
        (0x7F000000, 8),  // 127.0.0.0/8
        (0xA9FE0000, 16), // 169.254.0.0/16
        (0xAC100000, 12), // 172.16.0.0/12
        (0xC0A80000, 16), // 192.168.0.0/16
        (0xE0000000, 3),  // 224.0.0.0/3 (multicast và dành riêng)
    ];

    public static bool IsBlockAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6Any);

        Span<byte> bytes = stackalloc byte[4];
        ip.TryWriteBytes(bytes, out _);
        var value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        foreach (var (network, prefixLength) in Ranges)
        {
            if ((value & (uint.MaxValue << (32 - prefixLength))) == network) return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~LunaDns.Core.Tests.Probing`
Expected: PASS, 27 test (19 + 5 + 3).

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 221, Skipped: 2`.

---

### Task 8: `Prober`

**Files:**
- Create: `src/LunaDns.Core/Probing/ITlsProbe.cs`, `src/LunaDns.Core/Probing/Prober.cs`
- Test: `tests/LunaDns.Core.Tests/Probing/FakeTlsProbe.cs`, `tests/LunaDns.Core.Tests/Probing/ProberTests.cs`

**Interfaces:**
- Consumes: `IHostResolver` (Task 2), `DohException` (KH2), `ProbeVerdict`, `ProbeResult`, `KnownBlockIps.IsBlockAddress` (Task 7); test: `FakeResolver` (Task 5: `Respond`, `Delay`).
- Produces (namespace `LunaDns.Core.Probing`):
  - `interface ITlsProbe { Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken); }`: true khi bắt tay TLS tới `address` với SNI `host` thành công **và chứng chỉ hợp lệ cho `host`**. Lỗi mạng hay chứng chỉ thì false. Chỉ ném `OperationCanceledException` khi token bị huỷ.
  - `sealed class Prober`:
    - `static readonly TimeSpan DefaultCheckTimeout` = 3 giây (spec 4.1)
    - `Prober(IHostResolver systemResolver, IHostResolver dohResolver, ITlsProbe tls, TimeSpan? checkTimeout = null, ILogger<Prober>? logger = null)`
    - `Task<ProbeResult> ProbeAsync(string host, CancellationToken cancellationToken)`
    - `Task<IReadOnlyList<ProbeResult>> RunAsync(IEnumerable<string> hosts, CancellationToken cancellationToken)`: các host chạy song song, kết quả giữ thứ tự đầu vào.
- Thuật toán cho một host (`S` = IP từ DNS hệ thống, `D` = IP từ DoH; mỗi phép kiểm tra có hạn riêng `checkTimeout`, quá hạn tính là lỗi):

| Bước | Điều kiện | Verdict |
|---|---|---|
| 1 | `D` rỗng (DoH lỗi, quá hạn, hoặc tên không tồn tại) | `Unreachable` |
| 2 | TLS nguyên vẹn tới `D[0]` thành công, và `S` rỗng hoặc có địa chỉ thuộc `KnownBlockIps` | `DnsPoisoned` |
| 3 | TLS nguyên vẹn tới `D[0]` thành công, và `S` có chung ít nhất một IP với `D` | `Ok` |
| 4 | TLS nguyên vẹn tới `D[0]` thành công, `S` không chung IP nào với `D` | TLS nguyên vẹn tới `S[0]`: thành công thì `Ok` (CDN trả IP khác vùng), lỗi thì `DnsPoisoned` |
| 5 | TLS nguyên vẹn tới `D[0]` lỗi, TLS cắt gói tới `D[0]` thành công | `SniFiltered` |
| 6 | cả hai đều lỗi | `Unreachable` |

Bước 4 thay cho "bảng ASN nhúng sẵn" của spec 4.1; lý do nằm ở mục "Quyết định thiết kế mới" cuối tài liệu.

- [ ] **Step 1: Viết `FakeTlsProbe`**

```csharp
// tests/LunaDns.Core.Tests/Probing/FakeTlsProbe.cs
using System.Net;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

internal sealed class FakeTlsProbe : ITlsProbe
{
    private readonly List<(IPAddress Address, string Host, bool Split)> _calls = [];

    /// <summary>Mặc định: mọi lần bắt tay đều thành công.</summary>
    public Func<IPAddress, string, bool, CancellationToken, Task<bool>> RespondAsync { get; set; } =
        (_, _, _, _) => Task.FromResult(true);

    public IReadOnlyList<(IPAddress Address, string Host, bool Split)> Calls
    {
        get
        {
            lock (_calls) return [.. _calls];
        }
    }

    public Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken)
    {
        lock (_calls) _calls.Add((address, host, split));
        return RespondAsync(address, host, split, cancellationToken);
    }
}
```

- [ ] **Step 2: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Probing/ProberTests.cs
using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Tests.Dns;

namespace LunaDns.Core.Tests.Probing;

public class ProberTests
{
    private const string Host = "store.steampowered.com";
    private static readonly IPAddress Real = IPAddress.Parse("23.50.60.70");
    private static readonly IPAddress OtherEdge = IPAddress.Parse("118.69.10.20");
    private static readonly IPAddress Fake = IPAddress.Parse("203.0.113.9");
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Forever = TimeSpan.FromSeconds(30);

    private readonly FakeResolver _system = new() { Respond = _ => [Real] };
    private readonly FakeResolver _doh = new() { Respond = _ => [Real] };
    private readonly FakeTlsProbe _tls = new();

    private Prober Create(TimeSpan? checkTimeout = null) => new(_system, _doh, _tls, checkTimeout);

    private void Tls(Func<IPAddress, bool, bool> answer) =>
        _tls.RespondAsync = (address, _, split, _) => Task.FromResult(answer(address, split));

    [Fact]
    public async Task Ok_when_system_dns_agrees_with_doh()
    {
        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.Ok, result.Verdict);
        Assert.Equal(Host, result.Host);
        Assert.Equal(Real, Assert.Single(result.SystemAddresses));
        Assert.Equal(Real, Assert.Single(result.DohAddresses));
        Assert.Equal((Real, Host, false), Assert.Single(_tls.Calls));
    }

    [Fact]
    public async Task Ok_when_system_ip_differs_but_presents_valid_certificate()
    {
        _system.Respond = _ => [OtherEdge];

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.Ok, result.Verdict);
        Assert.Contains((OtherEdge, Host, false), _tls.Calls);
    }

    [Fact]
    public async Task DnsPoisoned_when_system_ip_fails_tls()
    {
        _system.Respond = _ => [Fake];
        Tls((address, _) => address.Equals(Real));

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.DnsPoisoned, result.Verdict);
        Assert.False(result.SystemAddressBlocked);
    }

    [Fact]
    public async Task DnsPoisoned_without_handshake_when_system_ip_is_a_block_address()
    {
        _system.Respond = _ => [IPAddress.Loopback];

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.DnsPoisoned, result.Verdict);
        Assert.True(result.SystemAddressBlocked);
        Assert.DoesNotContain(_tls.Calls, call => call.Address.Equals(IPAddress.Loopback));
    }

    [Fact]
    public async Task DnsPoisoned_when_system_dns_returns_nothing()
    {
        _system.Respond = _ => [];

        Assert.Equal(ProbeVerdict.DnsPoisoned, (await Create().ProbeAsync(Host, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task SniFiltered_when_only_split_handshake_works()
    {
        Tls((_, split) => split);

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.SniFiltered, result.Verdict);
        (IPAddress, string, bool)[] expected = [(Real, Host, false), (Real, Host, true)];
        Assert.Equal(expected, _tls.Calls);
    }

    [Fact]
    public async Task Unreachable_when_both_handshakes_fail()
    {
        Tls((_, _) => false);

        Assert.Equal(ProbeVerdict.Unreachable, (await Create().ProbeAsync(Host, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task Unreachable_without_handshake_when_doh_has_no_address()
    {
        _doh.Respond = _ => [];

        var result = await Create().ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.Unreachable, result.Verdict);
        Assert.Empty(_tls.Calls);
    }

    [Fact]
    public async Task Unreachable_when_doh_fails()
    {
        _doh.Respond = _ => throw new DohException("mọi máy chủ DoH đều lỗi");

        Assert.Equal(ProbeVerdict.Unreachable, (await Create().ProbeAsync(Host, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task Handshake_that_exceeds_check_timeout_counts_as_failure()
    {
        _tls.RespondAsync = async (_, _, _, ct) =>
        {
            await Task.Delay(Forever, ct);
            return true;
        };

        Assert.Equal(ProbeVerdict.Unreachable, (await Create(Short).ProbeAsync(Host, CancellationToken.None)).Verdict);
    }

    [Fact]
    public async Task System_dns_that_exceeds_check_timeout_counts_as_no_answer()
    {
        _system.Delay = Forever;

        var result = await Create(Short).ProbeAsync(Host, CancellationToken.None);

        Assert.Equal(ProbeVerdict.DnsPoisoned, result.Verdict);
        Assert.Empty(result.SystemAddresses);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        _tls.RespondAsync = async (_, _, _, ct) =>
        {
            await Task.Delay(Forever, ct);
            return true;
        };
        using var cts = new CancellationTokenSource(Short);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().ProbeAsync(Host, cts.Token));
    }

    [Fact]
    public async Task RunAsync_probes_hosts_in_parallel_and_keeps_order()
    {
        string[] hosts = ["a.steampowered.com", "b.steampowered.com", "c.steampowered.com"];
        var entered = 0;
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Mỗi lần bắt tay chờ tới khi cả ba host cùng vào. Nếu Prober chạy tuần tự thì lần đầu sẽ quá hạn và verdict khác Ok.
        _tls.RespondAsync = async (_, _, _, ct) =>
        {
            if (Interlocked.Increment(ref entered) == hosts.Length) allEntered.TrySetResult();
            await allEntered.Task.WaitAsync(ct);
            return true;
        };

        var results = await Create(TimeSpan.FromSeconds(2)).RunAsync(hosts, CancellationToken.None);

        Assert.Equal(hosts, results.Select(r => r.Host));
        Assert.All(results, r => Assert.Equal(ProbeVerdict.Ok, r.Verdict));
    }
}
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~ProberTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'ITlsProbe' could not be found`.

- [ ] **Step 4: Viết code**

```csharp
// src/LunaDns.Core/Probing/ITlsProbe.cs
using System.Net;

namespace LunaDns.Core.Probing;

/// <summary>
/// Một lần bắt tay TLS thử. True khi bắt tay tới <paramref name="address"/> với SNI <paramref name="host"/> thành công
/// và chứng chỉ hợp lệ cho host đó. Lỗi mạng hay chứng chỉ thì false; chỉ ném OperationCanceledException khi token bị huỷ.
/// </summary>
public interface ITlsProbe
{
    Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken);
}
```

```csharp
// src/LunaDns.Core/Probing/Prober.cs
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Probing;

/// <summary>Kiểm tra trước khi can thiệp (spec 4.1): phân loại từng host để chỉ bật đúng biện pháp cần.</summary>
public sealed partial class Prober
{
    public static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromSeconds(3);

    private readonly IHostResolver _system;
    private readonly IHostResolver _doh;
    private readonly ITlsProbe _tls;
    private readonly TimeSpan _checkTimeout;
    private readonly ILogger _logger;

    public Prober(IHostResolver systemResolver, IHostResolver dohResolver, ITlsProbe tls, TimeSpan? checkTimeout = null, ILogger<Prober>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(systemResolver);
        ArgumentNullException.ThrowIfNull(dohResolver);
        ArgumentNullException.ThrowIfNull(tls);
        _system = systemResolver;
        _doh = dohResolver;
        _tls = tls;
        _checkTimeout = checkTimeout ?? DefaultCheckTimeout;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Kiểm tra các host song song. Kết quả giữ thứ tự của <paramref name="hosts"/>.</summary>
    public async Task<IReadOnlyList<ProbeResult>> RunAsync(IEnumerable<string> hosts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        return await Task.WhenAll(hosts.Select(host => ProbeAsync(host, cancellationToken))).ConfigureAwait(false);
    }

    public async Task<ProbeResult> ProbeAsync(string host, CancellationToken cancellationToken)
    {
        var systemTask = ResolveAsync(_system, host, cancellationToken);
        var dohTask = ResolveAsync(_doh, host, cancellationToken);
        var system = await systemTask.ConfigureAwait(false);
        var doh = await dohTask.ConfigureAwait(false);
        var blocked = system.Any(KnownBlockIps.IsBlockAddress);

        var verdict = await ClassifyAsync(host, system, doh, blocked, cancellationToken).ConfigureAwait(false);
        LogVerdict(_logger, host, verdict);
        return new ProbeResult(host, verdict, system, doh, blocked);
    }

    private async Task<ProbeVerdict> ClassifyAsync(
        string host, IReadOnlyList<IPAddress> system, IReadOnlyList<IPAddress> doh, bool blocked, CancellationToken cancellationToken)
    {
        if (doh.Count == 0) return ProbeVerdict.Unreachable;

        if (await HandshakeAsync(doh[0], host, split: false, cancellationToken).ConfigureAwait(false))
        {
            if (system.Count == 0 || blocked) return ProbeVerdict.DnsPoisoned;
            if (system.Any(address => doh.Contains(address))) return ProbeVerdict.Ok;
            // CDN hay trả IP khác nhau theo resolver, nên "khác IP" chưa phải là bị đầu độc.
            // IP của DNS hệ thống đưa được chứng chỉ hợp lệ cho host thì đó là máy chủ thật.
            return await HandshakeAsync(system[0], host, split: false, cancellationToken).ConfigureAwait(false)
                ? ProbeVerdict.Ok
                : ProbeVerdict.DnsPoisoned;
        }

        return await HandshakeAsync(doh[0], host, split: true, cancellationToken).ConfigureAwait(false)
            ? ProbeVerdict.SniFiltered
            : ProbeVerdict.Unreachable;
    }

    private async Task<IReadOnlyList<IPAddress>> ResolveAsync(IHostResolver resolver, string host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_checkTimeout);
        try
        {
            return await resolver.ResolveAsync(host, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogResolveFailed(_logger, host, ex);
            return [];
        }
        catch (Exception ex) when (ex is DohException or SocketException or IOException)
        {
            LogResolveFailed(_logger, host, ex);
            return [];
        }
    }

    private async Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_checkTimeout);
        try
        {
            return await _tls.HandshakeAsync(address, host, split, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogHandshakeTimedOut(_logger, host, split, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Kiểm tra {Host}: {Verdict}")]
    private static partial void LogVerdict(ILogger logger, string host, ProbeVerdict verdict);

    [LoggerMessage(Level = LogLevel.Information, Message = "Kiểm tra {Host}: một nguồn DNS không trả lời được")]
    private static partial void LogResolveFailed(ILogger logger, string host, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Kiểm tra {Host}: bắt tay TLS (cắt gói: {Split}) quá hạn")]
    private static partial void LogHandshakeTimedOut(ILogger logger, string host, bool split, Exception exception);
}
```

- [ ] **Step 5: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~ProberTests`
Expected: PASS, 13 test.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 234, Skipped: 2`.

---

### Task 9: `SplittingStream` và `TlsProbe`

**Files:**
- Create: `src/LunaDns.Core/Probing/SplittingStream.cs`, `src/LunaDns.Core/Probing/TlsProbe.cs`
- Test: `tests/LunaDns.Core.Tests/Probing/SplittingStreamTests.cs`, `tests/LunaDns.Core.Tests/Probing/LoopbackTlsServer.cs`, `tests/LunaDns.Core.Tests/Probing/TlsProbeTests.cs`

**Interfaces:**
- Consumes: `ITlsProbe` (Task 8); `SplitWriter.WriteAsync` (Task 4); `HelloSplitter.Split`, `SplitMode` (KH1); test: `RecordingStream` (Task 4), `TestClientHello` (KH1).
- Produces (namespace `LunaDns.Core.Probing`):
  - `internal sealed class SplittingStream(Stream inner, SplitMode mode) : Stream`: lần ghi **đầu tiên** được cắt theo `HelloSplitter` (đó là ClientHello do `SslStream` ghi); các lần sau chuyển thẳng. Đọc chuyển thẳng. `Dispose` đóng luôn `inner`.
  - `sealed class TlsProbe : ITlsProbe`:
    - `public TlsProbe(SplitMode splitMode = SplitMode.FirstByte, ILogger<TlsProbe>? logger = null)`: cổng 443, kiểm tra chứng chỉ theo mặc định của hệ điều hành.
    - `internal TlsProbe(int port, SplitMode splitMode, RemoteCertificateValidationCallback? validation, ILogger<TlsProbe>? logger)`: chỉ cho test.
    - `HandshakeAsync`: socket trần tới `address:port` (không qua proxy), `SslStream` với `TargetHost = host`. `SocketException`, `IOException`, `AuthenticationException` thành `false`.

- [ ] **Step 1: Viết máy chủ TLS loopback cho test**

```csharp
// tests/LunaDns.Core.Tests/Probing/LoopbackTlsServer.cs
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LunaDns.Core.Tests.Probing;

/// <summary>Máy chủ TLS trên 127.0.0.1 với chứng chỉ tự ký cho một host, để test TlsProbe không cần Internet.</summary>
internal sealed class LoopbackTlsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public LoopbackTlsServer(string host)
    {
        Certificate = CreateCertificate(host);
        _listener.Start();
        _loop = AcceptLoopAsync();
    }

    public X509Certificate2 Certificate { get; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Callback kiểm tra chứng chỉ cho test: chỉ tin đúng chứng chỉ của máy chủ này.</summary>
    public bool Trusts(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
        certificate is not null && certificate.GetCertHashString() == Certificate.GetCertHashString();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
        Certificate.Dispose();
    }

    private static X509Certificate2 CreateCertificate(string host)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel không dùng được khoá chỉ nằm trong bộ nhớ: xuất PFX rồi nạp lại để khoá có chỗ chứa.
        return new X509Certificate2(ephemeral.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.UserKeySet);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            using var ssl = new SslStream(client.GetStream());
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Certificate }, _stop.Token);
                _ = await ssl.ReadAsync(new byte[1], _stop.Token); // giữ kết nối tới khi client đóng
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException)
            {
                // Client từ chối chứng chỉ hoặc đã đóng kết nối: đúng kịch bản của test.
                return;
            }
        }
    }
}
```

- [ ] **Step 2: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Probing/SplittingStreamTests.cs
using LunaDns.Core.Probing;
using LunaDns.Core.Tests.Tls;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Probing;

public class SplittingStreamTests
{
    private static readonly byte[] Hello = TestClientHello.Build("store.steampowered.com");
    private static readonly byte[] Later = [9, 8, 7];

    [Fact]
    public async Task Splits_only_the_first_async_write()
    {
        var inner = new RecordingStream();
        await using var stream = new SplittingStream(inner, SplitMode.FirstByte);

        await stream.WriteAsync(Hello);
        await stream.WriteAsync(Later);

        var writes = inner.Writes;
        Assert.Equal(3, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello.Length - 1, writes[1].Length);
        Assert.Equal(Later, writes[2]);
    }

    [Fact]
    public void Splits_only_the_first_sync_write()
    {
        var inner = new RecordingStream();
        using var stream = new SplittingStream(inner, SplitMode.FirstByte);

        stream.Write(Hello, 0, Hello.Length);
        stream.Write(Later, 0, Later.Length);

        var writes = inner.Writes;
        Assert.Equal(3, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello, writes[0].Concat(writes[1]).ToArray());
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Probing/TlsProbeTests.cs
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Probing;

public sealed class TlsProbeTests : IAsyncLifetime
{
    private const string Host = "store.steampowered.com";
    private LoopbackTlsServer _server = null!;

    public Task InitializeAsync()
    {
        _server = new LoopbackTlsServer(Host);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private TlsProbe Trusting(int? port = null) => new(port ?? _server.Port, SplitMode.FirstByte, _server.Trusts, null);

    [Fact]
    public async Task Intact_handshake_succeeds_with_trusted_certificate() =>
        Assert.True(await Trusting().HandshakeAsync(IPAddress.Loopback, Host, split: false, CancellationToken.None));

    [Fact]
    public async Task Split_handshake_is_reassembled_by_the_server() =>
        Assert.True(await Trusting().HandshakeAsync(IPAddress.Loopback, Host, split: true, CancellationToken.None));

    [Fact]
    public async Task Untrusted_certificate_is_a_failure()
    {
        // Không truyền callback: dùng kiểm tra mặc định của hệ điều hành, chứng chỉ tự ký bị từ chối.
        var probe = new TlsProbe(_server.Port, SplitMode.FirstByte, null, null);

        Assert.False(await probe.HandshakeAsync(IPAddress.Loopback, Host, split: false, CancellationToken.None));
    }

    [Fact]
    public async Task Refused_connection_is_a_failure()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var deadPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.False(await Trusting(deadPort).HandshakeAsync(IPAddress.Loopback, Host, split: false, CancellationToken.None));
    }

    [Fact]
    public async Task Connection_closed_by_peer_is_a_failure()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            var handshake = Trusting(((IPEndPoint)listener.LocalEndpoint).Port)
                .HandshakeAsync(IPAddress.Loopback, Host, split: false, CancellationToken.None);
            (await accept).Dispose(); // giống thiết bị lọc SNI: nhận ClientHello rồi cắt kết nối

            Assert.False(await handshake);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Cancellation_is_thrown_not_swallowed()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); // nhận kết nối vào hàng chờ nhưng không bao giờ trả lời
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var probe = Trusting(((IPEndPoint)listener.LocalEndpoint).Port);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                probe.HandshakeAsync(IPAddress.Loopback, Host, split: false, cts.Token));
        }
        finally
        {
            listener.Stop();
        }
    }
}
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplittingStreamTests|FullyQualifiedName~TlsProbeTests"`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'SplittingStream' could not be found`.

- [ ] **Step 4: Viết code**

```csharp
// src/LunaDns.Core/Probing/SplittingStream.cs
using LunaDns.Core.Tls;

namespace LunaDns.Core.Probing;

/// <summary>
/// Bọc một stream: lần ghi đầu tiên (ClientHello do SslStream ghi) được cắt theo <see cref="HelloSplitter"/>,
/// các lần sau chuyển thẳng. Nhờ vậy Prober thử được đúng cách cắt gói mà proxy sẽ dùng.
/// </summary>
internal sealed class SplittingStream : Stream
{
    private readonly Stream _inner;
    private readonly SplitMode _mode;
    private bool _first = true;

    public SplittingStream(Stream inner, SplitMode mode)
    {
        _inner = inner;
        _mode = mode;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_first)
        {
            _inner.Write(buffer, offset, count);
            return;
        }
        _first = false;
        foreach (var piece in HelloSplitter.Split(buffer.AsMemory(offset, count), _mode))
        {
            _inner.Write(piece.Span);
            _inner.Flush();
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_first)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            return;
        }
        _first = false;
        await SplitWriter.WriteAsync(_inner, buffer, _mode, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
```

`RecordingStream` (Task 4) không override `Write(ReadOnlySpan<byte>)`; bản mặc định của `Stream` chép span ra mảng rồi gọi `Write(byte[], int, int)`, nên test đồng bộ vẫn ghi nhận đúng từng mảnh.

```csharp
// src/LunaDns.Core/Probing/TlsProbe.cs
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Probing;

/// <summary>
/// Bắt tay TLS thật bằng SslStream trên socket trần (không bao giờ qua proxy hệ thống). Chứng chỉ được kiểm tra
/// theo tên host, nên trang chặn hay máy chủ giả không thể qua. Không gửi dữ liệu ứng dụng nào.
/// </summary>
public sealed partial class TlsProbe : ITlsProbe
{
    private readonly int _port;
    private readonly SplitMode _splitMode;
    private readonly RemoteCertificateValidationCallback? _validation;
    private readonly ILogger _logger;

    public TlsProbe(SplitMode splitMode = SplitMode.FirstByte, ILogger<TlsProbe>? logger = null)
        : this(443, splitMode, null, logger)
    {
    }

    /// <summary>Cho test: cổng và cách kiểm tra chứng chỉ tuỳ chọn.</summary>
    internal TlsProbe(int port, SplitMode splitMode, RemoteCertificateValidationCallback? validation, ILogger<TlsProbe>? logger)
    {
        _port = port;
        _splitMode = splitMode;
        _validation = validation;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, _port), cancellationToken).ConfigureAwait(false);
            Stream transport = new NetworkStream(socket, ownsSocket: false);
            if (split) transport = new SplittingStream(transport, _splitMode);
            using var ssl = new SslStream(transport, leaveInnerStreamOpen: false);
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                RemoteCertificateValidationCallback = _validation,
            };
            await ssl.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException)
        {
            LogFailed(_logger, host, split, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bắt tay TLS tới {Host} (cắt gói: {Split}) không thành công")]
    private static partial void LogFailed(ILogger logger, string host, bool split, Exception exception);
}
```

Nếu `LoopbackTlsServer` không bắt tay được trên máy chạy test (SChannel báo thiếu credential dù đã nạp lại PFX): **dừng lại và báo cáo nguyên văn lỗi**, không tự đổi cách tạo chứng chỉ quá một lần và không nới lỏng `TlsProbe` để test qua.

- [ ] **Step 5: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~SplittingStreamTests|FullyQualifiedName~TlsProbeTests"`
Expected: PASS, 8 test (2 + 6). Test cổng chết mất khoảng 2 giây.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 242, Skipped: 2`.

---

### Task 10: Test với mạng thật (tắt mặc định)

**Files:**
- Test: `tests/LunaDns.Core.Tests/Probing/ProberNetworkTests.cs`, `tests/LunaDns.Core.Tests/Proxy/SplitProxyNetworkTests.cs`

**Interfaces:**
- Consumes: `NetworkFactAttribute` (KH2: bỏ qua nếu thiếu `LUNADNS_NETWORK_TESTS=1`); `DohClient.Create(IReadOnlyList<DohServer>)`, `DohServer.Defaults`, `DnsCache` (KH2); `SystemHostResolver`, `DohHostResolver` (Task 2); `TcpUpstreamConnector`, `SplitProxyOptions` (Task 5); `SplitProxy` (Task 6); `ProbeTargets`, `ProbeAdvisor` (Task 7); `Prober` (Task 8); `TlsProbe` (Task 9).
- Produces: không có API mới. Đây là bằng chứng Prober và proxy chạy đúng trên mạng thật của PO, trước khi KH4 ghi `AutoConfigURL` và KH5 nối vào giao diện.

- [ ] **Step 1: Viết hai test**

```csharp
// tests/LunaDns.Core.Tests/Probing/ProberNetworkTests.cs
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using Xunit.Abstractions;

namespace LunaDns.Core.Tests.Probing;

public class ProberNetworkTests(ITestOutputHelper output)
{
    [NetworkFact]
    public async Task Classifies_real_hosts_on_this_network()
    {
        using var doh = DohClient.Create(DohServer.Defaults);
        var prober = new Prober(new SystemHostResolver(), new DohHostResolver(doh), new TlsProbe());

        var results = await prober.RunAsync(ProbeTargets.For(includePubg: true), CancellationToken.None);

        foreach (var r in results)
        {
            output.WriteLine($"{r.Host}: {r.Verdict} | DNS hệ thống: {string.Join(", ", r.SystemAddresses)} | DoH: {string.Join(", ", r.DohAddresses)}");
        }
        output.WriteLine($"Đề xuất: {ProbeAdvisor.Recommend(results)}");
        Assert.Equal(5, results.Count);
        // Chỉ đòi DoH trả được IP cho 4 host Steam. Verdict phụ thuộc nhà mạng nên chỉ in ra, không assert.
        Assert.All(results.Take(4), r => Assert.NotEmpty(r.DohAddresses));
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Proxy/SplitProxyNetworkTests.cs
using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;

namespace LunaDns.Core.Tests.Proxy;

public class SplitProxyNetworkTests
{
    [NetworkFact]
    public async Task Https_to_steam_goes_through_and_other_hosts_are_refused()
    {
        using var doh = DohClient.Create(DohServer.Defaults);
        var connector = new TcpUpstreamConnector(new DohHostResolver(doh, new DnsCache()));
        await using var proxy = SplitProxy.Start(
            new SplitProxyOptions { Domains = GameDomains.All, FirstPort = 0, LastPort = 0 }, connector);
        using var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy(new Uri($"http://127.0.0.1:{proxy.Port}")),
            UseProxy = true,
            AllowAutoRedirect = false,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        using var response = await http.GetAsync(new Uri("https://store.steampowered.com/"));

        Assert.True((int)response.StatusCode < 500, $"Steam trả {(int)response.StatusCode}");
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(new Uri("https://example.com/")));
    }
}
```

- [ ] **Step 2: Chạy mặc định (không có mạng)**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 242, Skipped: 4`.

- [ ] **Step 3: Chạy với mạng thật trên máy PO**

Run (PowerShell, user thường):
`$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"; $env:LUNADNS_NETWORK_TESTS="1"; dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~NetworkTests" --logger "console;verbosity=detailed"; Remove-Item Env:LUNADNS_NETWORK_TESTS`
Expected: `Passed: 4` (2 của KH2 + `ProberNetworkTests` + `SplitProxyNetworkTests`). Chép 5 dòng verdict và dòng "Đề xuất" vào báo cáo task, kèm tên nhà mạng và đang dùng Wi-Fi hay 4G.

Nếu fail: chép nguyên dòng lỗi. `SplitProxyNetworkTests` fail ở bước Steam trong khi `ProberNetworkTests` báo `SniFiltered` nghĩa là cách cắt `FirstByte` không đủ với nhà mạng này: ghi lại để KH5 quyết định mặc định của mục Nâng cao, **không** tự đổi mặc định trong KH3.

---

## Đối chiếu spec (tự rà soát)

| Mục spec | Kế hoạch 3 | Phần còn lại ở |
|---|---|---|
| 4.1 chạy song song, mỗi phép kiểm tra hạn 3 giây | Task 8 (`RunAsync`, `DefaultCheckTimeout`) | — |
| 4.1 host mẫu, PUBG chỉ khi bật nhóm | Task 7 (`ProbeTargets.For`) | KH5 truyền `includePubg` theo công tắc |
| 4.1 bước 1: DNS hệ thống và DoH (Cloudflare, dự phòng Google) | Task 2 (`SystemHostResolver`, `DohHostResolver` trên `DohClient` của KH2) | KH5 dựng `DohClient` theo lựa chọn ở Nâng cao |
| 4.1 bước 2–3: TLS nguyên vẹn, rồi TLS cắt gói | Task 9 (`TlsProbe`, `SplittingStream`) | — |
| 4.1 bảng 4 verdict và biện pháp đề xuất | Task 8 (`Prober`), Task 7 (`ProbeAdvisor`) | KH5: nút "Áp dụng đề xuất", hướng dẫn khi `Unreachable` |
| 4.1 "khớp IP": cùng tập IP DoH hoặc cùng ASN | Task 8 bước 3–4: cùng tập IP, hoặc IP hệ thống qua được kiểm tra chứng chỉ | **Lệch spec, xem quyết định 1** |
| 4.1 `Core/Probing/KnownBlockIps.cs` | Task 7 (dải địa chỉ không thể là máy chủ game) | IP trang chặn riêng của nhà mạng: thêm khi có số liệu (quyết định 1) |
| 4.3 nghe `127.0.0.1`, cổng trống trong 18080–18090, `ExclusiveAddressUse` | Task 5 (`SplitProxyOptions`), Task 6 (`BindFirstFreePort`) | KH5: thông báo khi hết cổng (`SplitProxyException.Error`) |
| 4.3 phục vụ `/proxy.pac` chỉ gồm `GameDomains` | Task 6 (`_pacResponse`, `PacUrl`), Task 4 (PAC chỉ `https:`/`wss:`) | KH4 ghi `PacUrl` vào `AutoConfigURL` qua Journal |
| 4.3 CONNECT tới `GameDomains`: đọc trọn record đầu, cắt byte đầu hoặc giữa SNI, nối hai chiều | Task 6 (`ForwardFirstRecordAsync`, `RelayAsync`), Task 4 (`SplitWriter`) | KH5: chọn điểm cắt trong Nâng cao (`SplitProxyOptions.SplitMode`) |
| 4.3 host ngoài danh sách trả `403` | Task 6 (`TunnelAsync`, `LocalResponse`) | — |
| 4.3 không `Thread.Sleep(20)`: `NoDelay` + hai lần `Write`; độ trễ 0–20 ms chỉnh được | Task 4 (`SplitWriter`, `MaxDelay`), Task 5 (`TcpUpstreamConnector` bật `NoDelay`, `SplitDelay`) | KH5: thanh chỉnh độ trễ trong Nâng cao |
| 4.3 không giải mã TLS, không cài chứng chỉ | Global Constraints; proxy chỉ chuyển byte | — |
| 4.8 khu vực trạng thái: cổng proxy, số kết nối | Task 6 (`Port`, `ActiveConnections`) | KH5 hiển thị |
| 4.9 báo cáo có kết quả probe | Task 7 (`ProbeResult` giữ hai danh sách IP) | KH5 ghi báo cáo, che IP LAN |
| 6. không `catch {}` rỗng, log | Global Constraints, `[LoggerMessage]` | KH5: sink log xoay vòng |
| 6. watchdog khởi động lại proxy ≤ 3 lần/phút | `SplitProxy.Completion` (Task 6) | KH5 |
| 7. proxy không mở cho mọi host; `ExclusiveAddressUse` | Task 6 (403, cổng 443, bind exclusive) | — |
| 9. Prober: transport giả, đủ 4 verdict | Task 8 (13 test) | — |
| 9. PAC: domain ngoài đi DIRECT, `evilsteampowered.com` không khớp | KH1 + Task 4; Task 6 thử lại ở phía proxy (403) | — |
| Spike: CM của Steam không đọc PAC | Ghi trong doc comment của `SplitProxy` | KH4: namespace NRPT cho CM; KH5: dòng mô tả công tắc Proxy SNI; KH7: FAQ |
| KH2 → KH3: hàm đọc bản ghi A/AAAA | Task 1 (`ReadAddresses`) | — |

**Quyết định thiết kế mới trong KH3 (khác hoặc thêm so với spec). PO đã duyệt cả 8 điểm ngày 2026-11-13:**

1. **"Khớp IP" dùng kiểm tra chứng chỉ thay cho bảng ASN.** Spec 4.1 định dùng một bảng ASN nhỏ cho Valve và Akamai. Akamai có hàng nghìn dải IP và đặt máy cache ngay trong mạng của nhà mạng (IP thuộc ASN của nhà mạng), nên bảng nhỏ sẽ báo nhầm `DnsPoisoned` cho mạng đang bình thường, trái tiêu chí "chỉ can thiệp khi cần". KH3 thay bằng: IP của DNS hệ thống khác IP của DoH thì bắt tay TLS tới chính IP đó; chứng chỉ hợp lệ cho host nghĩa là máy chủ thật. Hệ quả: không có bảng ASN nào trong mã, và `KnownBlockIps` chỉ chứa các dải địa chỉ riêng/loopback. **Chưa có** danh sách IP trang chặn riêng của Viettel/VNPT/FPT vì không có số liệu thực địa; các IP đó vẫn bị bắt nhờ bước chứng chỉ. Khi có số liệu (từ báo cáo chẩn đoán của khách) thì thêm vào `KnownBlockIps`.
2. **DNS hệ thống không trả lời (lỗi, quá 3 giây, NXDOMAIN) được tính là `DnsPoisoned`** nếu TLS tới IP của DoH thành công. Spec chỉ nói "IP không khớp"; KH3 coi "không có IP" cũng là không khớp, vì biện pháp sửa giống nhau (Định tuyến DNS).
3. **CONNECT chỉ được tới cổng 443.** Spec 4.3 chỉ giới hạn theo host. Cắt gói chỉ có nghĩa với TLS, và cho mọi cổng thì app khác trên máy mượn proxy để nối tới cổng bất kỳ của domain game. Nếu sau này cần cổng khác thì mở theo danh sách, có test.
4. **PAC chỉ đẩy `https:`/`wss:` qua proxy** (sửa `PacBuilder` của KH1, thêm một dòng). Không sửa thì mọi URL `http://` tới domain game sẽ kẹt ở lỗi của proxy.
5. **Proxy phân giải tên máy chủ qua DoH** (`TcpUpstreamConnector` + `DohHostResolver`), không qua DNS hệ thống. Nhờ vậy Proxy SNI chạy đúng cả khi Định tuyến DNS (NRPT, KH4) không bật hoặc không dùng được. KH5 là nơi dựng: `new TcpUpstreamConnector(new DohHostResolver(dohClient, new DnsCache()))`.
6. **Chỉ IPv4** cho cả Prober và kết nối ra của proxy. Máy chỉ có IPv6 không được hỗ trợ ở v1.
7. **Prober không tự dò độ trễ cắt gói.** Spec 4.3 viết "nếu probe cho thấy mạng cần độ trễ thì chỉnh trong Nâng cao". KH3 để `TlsProbe` thử cắt gói với độ trễ 0; độ trễ là tuỳ chọn tay của người dùng (`SplitProxyOptions.SplitDelay`), không có bước dò tự động.
8. **`RelayAsync` đóng cả hai chiều khi một bên đóng** (không half-close), và mỗi kết nối tới proxy chỉ phục vụ một yêu cầu.

**Việc không thuộc KH3:** ghi `AutoConfigURL` và gọi `InternetSetOption` (KH4); watchdog, `FeatureSet`, giao diện, mục Nâng cao, báo cáo chẩn đoán (KH5); listener `127.0.0.1:443` của Chế độ PUBG (KH5, spec 4.4) — có thể dùng lại `SplitWriter` và `TcpUpstreamConnector`.
