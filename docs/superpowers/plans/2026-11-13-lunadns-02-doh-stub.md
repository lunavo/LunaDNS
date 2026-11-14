# LunaDNS — Kế hoạch 2: DoH client + stub resolver 127.0.0.1:53

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Viết vùng `LunaDns.Core/Dns`: đọc/ghi wire format DNS, cache theo TTL, DoH client (RFC 8484, Cloudflare rồi dự phòng Google) và stub `DohStubServer` nghe UDP + TCP trên `127.0.0.1:53`, chỉ trả lời domain trong `GameDomains`, domain khác nhận `REFUSED` (spec 4.2).

**Architecture:** Bốn lớp xếp chồng, mỗi lớp test riêng được. `DnsWire` (hàm tĩnh, thuần byte) → `DnsCache` (TTL, `TimeProvider`) → `DnsQueryHandler` (quyết định REFUSED/cache/DoH/cắt cụt, không biết UDP hay TCP) → `DohStubServer` (socket). Phía upstream là interface `IDnsUpstream`, bản thật là `DohClient`. DoH nối thẳng tới IP bootstrap (1.1.1.1, 8.8.8.8…) nên không phải hỏi DNS hệ thống, vốn có thể đã bị đầu độc. TLS vẫn kiểm tra theo tên `cloudflare-dns.com`/`dns.google`. Mọi test chạy không cần Admin: stub bind `127.0.0.1:0` (cổng ngẫu nhiên). Test cần Internet chỉ chạy khi đặt `LUNADNS_NETWORK_TESTS=1`.

**Tech Stack:** .NET 8 SDK 8.0.428, C# 12, `System.Net.Sockets`, `System.Net.Http` (`SocketsHttpHandler`), `Microsoft.Extensions.Logging.Abstractions` 8.0.2 (chỉ dùng `ILogger` + `[LoggerMessage]`), xUnit 2.5, `Microsoft.Extensions.TimeProvider.Testing` (đã có trong project test).

**Spec:** `docs/superpowers/specs/2026-11-13-lunadns-design.md` (mục 4.2, 6, 9). **Kế hoạch trước:** `docs/superpowers/plans/2026-11-13-lunadns-01-core-foundation.md`. **Spike:** `docs/spikes/2026-11-spike-results.md`.

## Global Constraints

- Target `net8.0` (SDK 8.0.428). `Directory.Build.props` đã bật `Nullable`, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`. Không đổi file đó.
- `LunaDns.Core` **không** được tham chiếu `Microsoft.Win32`, P/Invoke hay package nào riêng của Windows. Socket và `HttpClient` của BCL thì được dùng.
- Package mới duy nhất được thêm: `Microsoft.Extensions.Logging.Abstractions` 8.0.2, vào `src/LunaDns.Core`. Không thêm package nào khác.
- Không có `catch { }` rỗng (spec mục 6). Catch nào cũng phải ném lại, ghi log, hoặc đổi thành giá trị trả về có nghĩa.
- Không chép mã từ SteamAccessFix (quyết định D4).
- Analyzer báo lỗi thì sửa code, không hạ mức cảnh báo. Có hai ngoại lệ đã có tiền lệ ở KH1, và chỉ được dùng đúng hai cách này: (1) thêm dòng tắt rule vào `tests/.editorconfig` nếu rule chỉ vướng ở test (như CA1707, CA2007); (2) `[SuppressMessage(..., Justification = "...")]` đặt trên **đúng một method** là ranh giới vòng nghe của `DohStubServer`, nếu CA1031 báo catch `Exception`. Ghi mọi lệch kế hoạch vào báo cáo task.
- Hằng mảng truyền làm đối số (CA1861) thì đưa lên thành `static readonly`, giống KH1.
- Stub chỉ bind địa chỉ loopback. KH2 **không** đụng NRPT, registry hay DNS của card mạng; NRPT làm ở KH4 và KH4 chỉ được sửa quy tắc của LunaDNS (spike: máy có sẵn quy tắc Tailscale).
- `DohClient` đặt `UseProxy = false`: DoH không bao giờ đi qua proxy hệ thống, kể cả PAC của chính LunaDNS (KH3).
- Thông báo cho người dùng (message của exception) viết tiếng Việt có dấu. Tên định danh trong code viết tiếng Anh.
- Lệnh kiểm tra (PowerShell):
  `$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"; dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
- Repo chỉ ở local và **không commit**: không `git add`, `git commit`, `git switch`, không tạo nhánh hay remote. Thay đổi để ở working tree; phiên chính review bằng `git diff` và `git status`. Hai mục spike untracked (`spikes/`, `docs/spikes/`) để nguyên.

## Gợi ý chia việc cho sub-agent (Sonnet)

| Agent | Task | Lý do gộp |
|---|---|---|
| A | 1, 2, 3 | Cùng làm `DnsWire` + `DnsCache`, thuần byte, không I/O |
| B | 4, 5 | `DohClient` và `DnsQueryHandler` dùng chung `IDnsUpstream` |
| C | 6, 7 | Socket thật và test mạng |

Chạy tuần tự A → B → C, vì mỗi agent dùng tên và kiểu của agent trước. Sau mỗi agent, phiên chính tự review bằng `git diff` và chạy lại lệnh kiểm tra.

## Cấu trúc file của kế hoạch 2

```
src/LunaDns.Core/
├── LunaDns.Core.csproj                 # (sửa, Task 4) + Microsoft.Extensions.Logging.Abstractions 8.0.2
└── Dns/
    ├── DnsRcode.cs                     # T1  mã RCODE
    ├── DnsQuestion.cs                  # T1  (tên đã chuẩn hoá, type, class)
    ├── DnsQuery.cs                     # T1  truy vấn đã đọc: id, opcode, question, cỡ UDP
    ├── DnsWire.cs                      # T1 đọc query/dựng response lỗi; T2 TTL, đổi ID, khớp response
    ├── DnsCache.cs                     # T3  cache theo TTL, trần 15 phút
    ├── IDnsUpstream.cs                 # T4  "gửi wire query, nhận wire response"
    ├── DohServer.cs                    # T4  endpoint + IP bootstrap, Cloudflare/Google
    ├── DohException.cs                 # T4
    ├── DohClient.cs                    # T4  RFC 8484 POST, dự phòng, timeout
    ├── DnsTransport.cs                 # T5  Udp | Tcp
    ├── DnsQueryHandler.cs              # T5  REFUSED / cache / DoH / SERVFAIL / TC
    ├── DnsStubError.cs                 # T6
    ├── DnsStubException.cs             # T6
    └── DohStubServer.cs                # T6  UDP + TCP, ExclusiveAddressUse
tests/LunaDns.Core.Tests/
├── NetworkFactAttribute.cs             # T4  [NetworkFact]: bỏ qua nếu thiếu LUNADNS_NETWORK_TESTS=1
└── Dns/
    ├── TestDnsMessage.cs               # T1  dựng gói DNS mẫu
    ├── DnsWireTests.cs                 # T1
    ├── DnsWireRecordTests.cs           # T2
    ├── DnsCacheTests.cs                # T3
    ├── FakeHttpHandler.cs              # T4
    ├── DohClientTests.cs               # T4
    ├── DohClientNetworkTests.cs        # T4  [NetworkFact]
    ├── FakeUpstream.cs                 # T5
    ├── DnsQueryHandlerTests.cs         # T5
    ├── DnsTestClient.cs                # T6  client UDP/TCP cho test
    ├── DohStubServerTests.cs           # T6
    └── DohStubNetworkTests.cs          # T7  [NetworkFact]
```

Số test mong đợi sau từng task (lúc bắt đầu: 48 xanh): T1 62 · T2 75 · T3 84 · T4 92 + 1 skipped · T5 104 · T6 112 · T7 112 + 2 skipped. Nếu lệch vài test thì vẫn được; điều bắt buộc là `Failed: 0`.

---

### Task 1: `DnsWire` đọc truy vấn và dựng response chỉ có header

**Files:**
- Create: `src/LunaDns.Core/Dns/DnsRcode.cs`, `src/LunaDns.Core/Dns/DnsQuestion.cs`, `src/LunaDns.Core/Dns/DnsQuery.cs`, `src/LunaDns.Core/Dns/DnsWire.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/TestDnsMessage.cs`, `tests/LunaDns.Core.Tests/Dns/DnsWireTests.cs`

**Interfaces:**
- Consumes: `DomainMatcher.Normalize(string)` (KH1, `LunaDns.Core.Domains`).
- Produces (namespace `LunaDns.Core.Dns`):
  - `enum DnsRcode { NoError = 0, FormErr = 1, ServFail = 2, NxDomain = 3, NotImp = 4, Refused = 5 }`
  - `sealed record DnsQuestion(string Name, ushort Type, ushort Class)`. `Name` đã chuẩn hoá (chữ thường, không có dấu chấm cuối), nên so sánh bằng `==` của record là đủ.
  - `sealed record DnsQuery(ushort Id, int Opcode, DnsQuestion Question, int MaxUdpPayload)`
  - `static class DnsWire`:
    - `const int HeaderLength = 12`, `const int MinUdpPayload = 512`, `const int MaxUdpPayload = 4096`
    - `ushort ReadId(ReadOnlySpan<byte>)`, `DnsRcode ReadRcode(ReadOnlySpan<byte>)`
    - `bool TryParseQuery(ReadOnlySpan<byte>, [NotNullWhen(true)] out DnsQuery?)`: QR=0, đúng 1 question, AN=NS=0. Đọc OPT ở phần additional để lấy cỡ UDP, kẹp trong [512, 4096].
    - `byte[] BuildHeaderOnlyResponse(ReadOnlySpan<byte> query, DnsRcode rcode, bool truncated = false)`: header + question của truy vấn; bỏ OPT; QR=1, RA=1, giữ opcode và RD.
    - `internal bool TryReadQuestion(ReadOnlySpan<byte>, ref int offset, out DnsQuestion?)`, `internal bool TrySkipName(ReadOnlySpan<byte>, ref int offset)` (Task 2 dùng lại)
  - Test: `TestDnsMessage.Query(...)`, `.Response(...)`, `.ResponseWithOpt(...)`, `.NxDomain(...)`, `.U16(byte[], int)`, `.TypeA`, `.TypeAaaa`. Task 2–7 đều dùng.

- [ ] **Step 1: Viết builder gói mẫu cho test**

```csharp
// tests/LunaDns.Core.Tests/Dns/TestDnsMessage.cs
using System.Buffers.Binary;
using System.Text;

namespace LunaDns.Core.Tests.Dns;

/// <summary>Dựng gói DNS mẫu cho test. Tên ở answer/authority nén bằng con trỏ 0xC00C về question.</summary>
internal static class TestDnsMessage
{
    public const ushort TypeA = 1;
    public const ushort TypeAaaa = 28;

    public static byte[] Query(string name, ushort type = TypeA, ushort id = 0x1234, int? ednsUdpSize = null, ushort flags = 0x0100)
    {
        var m = new List<byte>();
        Header(m, id, flags, qd: 1, an: 0, ns: 0, ar: ednsUdpSize is null ? 0 : 1);
        Question(m, name, type);
        if (ednsUdpSize is int size) Opt(m, (ushort)size);
        return [.. m];
    }

    /// <summary>Response NOERROR, mỗi TTL một bản ghi A.</summary>
    public static byte[] Response(string name, ushort id, params uint[] ttls) => Build(name, id, withOpt: false, ttls);

    /// <summary>Như <see cref="Response"/> nhưng có thêm OPT (11 byte) ở cuối, như DoH thật hay trả.</summary>
    public static byte[] ResponseWithOpt(string name, ushort id, params uint[] ttls) => Build(name, id, withOpt: true, ttls);

    /// <summary>NXDOMAIN kèm SOA ở authority, giống DoH trả cho tên không tồn tại.</summary>
    public static byte[] NxDomain(string name, ushort id, uint soaTtl)
    {
        var m = new List<byte>();
        Header(m, id, 0x8183, qd: 1, an: 0, ns: 1, ar: 0);
        Question(m, name, TypeA);
        m.Add(0xC0);
        m.Add(0x0C);
        U16(m, 6);
        U16(m, 1);
        U32(m, soaTtl);
        U16(m, 22);
        m.Add(0); // MNAME = root
        m.Add(0); // RNAME = root
        for (var i = 0; i < 5; i++) U32(m, 60);
        return [.. m];
    }

    public static ushort U16(byte[] msg, int at) => BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(at));

    private static byte[] Build(string name, ushort id, bool withOpt, uint[] ttls)
    {
        var m = new List<byte>();
        Header(m, id, 0x8180, qd: 1, an: ttls.Length, ns: 0, ar: withOpt ? 1 : 0);
        Question(m, name, TypeA);
        foreach (var ttl in ttls)
        {
            m.Add(0xC0);
            m.Add(0x0C);
            U16(m, TypeA);
            U16(m, 1);
            U32(m, ttl);
            U16(m, 4);
            U32(m, 0x5DB8D822); // 93.184.216.34
        }
        if (withOpt) Opt(m, 1232);
        return [.. m];
    }

    private static void Header(List<byte> m, ushort id, ushort flags, int qd, int an, int ns, int ar)
    {
        U16(m, id);
        U16(m, flags);
        U16(m, (ushort)qd);
        U16(m, (ushort)an);
        U16(m, (ushort)ns);
        U16(m, (ushort)ar);
    }

    private static void Question(List<byte> m, string name, ushort type)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            m.Add((byte)label.Length);
            m.AddRange(Encoding.ASCII.GetBytes(label));
        }
        m.Add(0);
        U16(m, type);
        U16(m, 1);
    }

    private static void Opt(List<byte> m, ushort udpSize)
    {
        m.Add(0);
        U16(m, 41);
        U16(m, udpSize);
        U32(m, 0);
        U16(m, 0);
    }

    private static void U16(List<byte> m, ushort v)
    {
        m.Add((byte)(v >> 8));
        m.Add((byte)v);
    }

    private static void U32(List<byte> m, uint v)
    {
        U16(m, (ushort)(v >> 16));
        U16(m, (ushort)v);
    }
}
```

- [ ] **Step 2: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Dns/DnsWireTests.cs
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DnsWireTests
{
    [Fact]
    public void Parses_single_question_and_normalizes_name()
    {
        Assert.True(DnsWire.TryParseQuery(TestDnsMessage.Query("Store.SteamPowered.com", TestDnsMessage.TypeAaaa), out var q));
        Assert.Equal(0x1234, q.Id);
        Assert.Equal(0, q.Opcode);
        Assert.Equal(new DnsQuestion("store.steampowered.com", TestDnsMessage.TypeAaaa, 1), q.Question);
        Assert.Equal(DnsWire.MinUdpPayload, q.MaxUdpPayload);
    }

    [Theory]
    [InlineData(1232, 1232)]
    [InlineData(100, 512)]
    [InlineData(65000, 4096)]
    public void Reads_edns_udp_size_clamped(int advertised, int expected)
    {
        Assert.True(DnsWire.TryParseQuery(TestDnsMessage.Query("steampowered.com", ednsUdpSize: advertised), out var q));
        Assert.Equal(expected, q.MaxUdpPayload);
    }

    [Fact]
    public void Reads_opcode()
    {
        Assert.True(DnsWire.TryParseQuery(TestDnsMessage.Query("steampowered.com", flags: 0x0100 | (2 << 11)), out var q));
        Assert.Equal(2, q.Opcode);
    }

    public static TheoryData<string, byte[]> Malformed => new()
    {
        { "ngắn hơn header", new byte[11] },
        { "là response (QR=1)", TestDnsMessage.Response("steampowered.com", 1, 300) },
        { "hai question", WithByte(TestDnsMessage.Query("steampowered.com"), 5, 2) },
        { "tên bị cắt cụt", TestDnsMessage.Query("steampowered.com")[..20] },
        { "label chứa dấu chấm", WithByte(TestDnsMessage.Query("ab.steampowered.com"), 14, (byte)'.') },
        { "con trỏ nén trong question", [.. TestDnsMessage.Query("x.com")[..12], 0xC0, 0x0C, 0, 1, 0, 1] },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Rejects_malformed_query(string because, byte[] packet) =>
        Assert.False(DnsWire.TryParseQuery(packet, out _), because);

    [Fact]
    public void Refused_response_keeps_id_rd_and_question_and_drops_opt()
    {
        var plain = TestDnsMessage.Query("google.com");
        var withOpt = TestDnsMessage.Query("google.com", ednsUdpSize: 1232);

        var r = DnsWire.BuildHeaderOnlyResponse(withOpt, DnsRcode.Refused);

        Assert.Equal(plain.Length, r.Length);
        Assert.Equal(0x1234, DnsWire.ReadId(r));
        Assert.Equal(0x8185, TestDnsMessage.U16(r, 2)); // QR, RD, RA, REFUSED
        Assert.Equal(DnsRcode.Refused, DnsWire.ReadRcode(r));
        Assert.Equal(1, TestDnsMessage.U16(r, 4));
        Assert.Equal(0, TestDnsMessage.U16(r, 6) + TestDnsMessage.U16(r, 8) + TestDnsMessage.U16(r, 10));
        Assert.Equal(plain[12..], r[12..]);
    }

    [Fact]
    public void Truncated_response_sets_tc_bit()
    {
        var r = DnsWire.BuildHeaderOnlyResponse(TestDnsMessage.Query("store.steampowered.com"), DnsRcode.NoError, truncated: true);
        Assert.Equal(0x8380, TestDnsMessage.U16(r, 2)); // QR, TC, RD, RA, NOERROR
    }

    [Fact]
    public void Header_without_readable_question_gives_header_only()
    {
        var junk = new byte[12];
        junk[5] = 1; // QDCOUNT = 1 nhưng không có question

        var r = DnsWire.BuildHeaderOnlyResponse(junk, DnsRcode.FormErr);

        Assert.Equal(12, r.Length);
        Assert.Equal(0, TestDnsMessage.U16(r, 4));
        Assert.Equal(DnsRcode.FormErr, DnsWire.ReadRcode(r));
    }

    private static byte[] WithByte(byte[] message, int at, byte value)
    {
        var copy = (byte[])message.Clone();
        copy[at] = value;
        return copy;
    }
}
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsWireTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'Dns' does not exist in the namespace 'LunaDns.Core'`.

- [ ] **Step 4: Viết code**

```csharp
// src/LunaDns.Core/Dns/DnsRcode.cs
namespace LunaDns.Core.Dns;

public enum DnsRcode
{
    NoError = 0,
    FormErr = 1,
    ServFail = 2,
    NxDomain = 3,
    NotImp = 4,
    Refused = 5,
}
```

```csharp
// src/LunaDns.Core/Dns/DnsQuestion.cs
namespace LunaDns.Core.Dns;

/// <summary>Question của gói DNS. <see cref="Name"/> đã qua DomainMatcher.Normalize.</summary>
public sealed record DnsQuestion(string Name, ushort Type, ushort Class);
```

```csharp
// src/LunaDns.Core/Dns/DnsQuery.cs
namespace LunaDns.Core.Dns;

/// <summary>Truy vấn đã đọc, chỉ gồm các trường stub cần. MaxUdpPayload lấy từ OPT (RFC 6891), mặc định 512.</summary>
public sealed record DnsQuery(ushort Id, int Opcode, DnsQuestion Question, int MaxUdpPayload);
```

```csharp
// src/LunaDns.Core/Dns/DnsWire.cs
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Dns;

/// <summary>Đọc/ghi wire format DNS (RFC 1035) ở mức stub cần: header, question, OPT, TTL.</summary>
public static class DnsWire
{
    public const int HeaderLength = 12;
    public const int MinUdpPayload = 512;
    public const int MaxUdpPayload = 4096;
    internal const ushort TypeOpt = 41;

    private const int FlagResponse = 0x8000;
    private const int FlagTruncated = 0x0200;
    private const int FlagRecursionAvailable = 0x0080;
    private const int KeepOpcodeAndRd = 0x7900;

    public static ushort ReadId(ReadOnlySpan<byte> msg) => BinaryPrimitives.ReadUInt16BigEndian(msg);

    public static DnsRcode ReadRcode(ReadOnlySpan<byte> msg) => (DnsRcode)(msg[3] & 0x0F);

    /// <summary>Đọc truy vấn có đúng một question. Sai định dạng thì trả false.</summary>
    public static bool TryParseQuery(ReadOnlySpan<byte> msg, [NotNullWhen(true)] out DnsQuery? query)
    {
        query = null;
        if (msg.Length < HeaderLength) return false;
        var flags = Read16(msg, 2);
        if ((flags & FlagResponse) != 0) return false;
        if (Read16(msg, 4) != 1 || Read16(msg, 6) != 0 || Read16(msg, 8) != 0) return false;

        var offset = HeaderLength;
        if (!TryReadQuestion(msg, ref offset, out var question)) return false;

        var maxUdp = MinUdpPayload;
        for (var i = Read16(msg, 10); i > 0; i--)
        {
            if (!TrySkipName(msg, ref offset) || offset + 10 > msg.Length) return false;
            var type = Read16(msg, offset);
            var udpSize = Read16(msg, offset + 2); // với OPT, trường CLASS là cỡ UDP tối đa
            offset += 10 + Read16(msg, offset + 8);
            if (offset > msg.Length) return false;
            if (type == TypeOpt) maxUdp = Math.Clamp((int)udpSize, MinUdpPayload, MaxUdpPayload);
        }

        query = new DnsQuery(ReadId(msg), (flags >> 11) & 0xF, question, maxUdp);
        return true;
    }

    /// <summary>Response chỉ gồm header + question của truy vấn (bỏ OPT), dùng cho REFUSED/SERVFAIL/FORMERR/NOTIMP/TC.</summary>
    public static byte[] BuildHeaderOnlyResponse(ReadOnlySpan<byte> query, DnsRcode rcode, bool truncated = false)
    {
        if (query.Length < HeaderLength) throw new ArgumentException("Gói ngắn hơn header DNS.", nameof(query));
        var questionEnd = HeaderLength;
        var hasQuestion = Read16(query, 4) == 1 && TryReadQuestion(query, ref questionEnd, out _);
        if (!hasQuestion) questionEnd = HeaderLength;

        var response = query[..questionEnd].ToArray();
        var flags = (Read16(query, 2) & KeepOpcodeAndRd) | FlagResponse | FlagRecursionAvailable
                    | (truncated ? FlagTruncated : 0) | (int)rcode;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), (ushort)flags);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), (ushort)(hasQuestion ? 1 : 0));
        response.AsSpan(6, 6).Clear();
        return response;
    }

    internal static bool TryReadQuestion(ReadOnlySpan<byte> msg, ref int offset, [NotNullWhen(true)] out DnsQuestion? question)
    {
        question = null;
        var name = new StringBuilder();
        var pos = offset;
        while (true)
        {
            if (pos >= msg.Length) return false;
            int length = msg[pos++];
            if (length == 0) break;
            // > 63 gồm cả con trỏ nén (0xC0): question không được nén.
            if (length > 63 || pos + length > msg.Length) return false;
            if (name.Length > 0) name.Append('.');
            foreach (var b in msg.Slice(pos, length))
            {
                // Chặn dấu chấm và ký tự điều khiển trong label, để "a.steampowered" + "com" không giả được tên game.
                if (b <= 0x20 || b >= 0x7F || b == (byte)'.') return false;
                name.Append((char)b);
            }
            pos += length;
            if (name.Length > 253) return false;
        }
        if (pos + 4 > msg.Length) return false;
        question = new DnsQuestion(DomainMatcher.Normalize(name.ToString()), Read16(msg, pos), Read16(msg, pos + 2));
        offset = pos + 4;
        return true;
    }

    /// <summary>Bỏ qua một tên (label hoặc con trỏ nén). Sai định dạng thì false, offset giữ nguyên.</summary>
    internal static bool TrySkipName(ReadOnlySpan<byte> msg, ref int offset)
    {
        var pos = offset;
        while (pos < msg.Length)
        {
            int length = msg[pos];
            if ((length & 0xC0) == 0xC0)
            {
                if (pos + 2 > msg.Length) return false;
                offset = pos + 2;
                return true;
            }
            if (length > 63) return false;
            pos += 1 + length;
            if (length == 0)
            {
                offset = pos;
                return true;
            }
        }
        return false;
    }

    private static ushort Read16(ReadOnlySpan<byte> msg, int at) => BinaryPrimitives.ReadUInt16BigEndian(msg[at..]);
}
```

- [ ] **Step 5: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsWireTests`
Expected: PASS, 14 test.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 62`.

---

### Task 2: `DnsWire` đọc TTL, đổi ID và khớp response với question

**Files:**
- Modify: `src/LunaDns.Core/Dns/DnsWire.cs` (thêm 4 method vào class `DnsWire`)
- Test: `tests/LunaDns.Core.Tests/Dns/DnsWireRecordTests.cs`

**Interfaces:**
- Consumes: `DnsWire.TrySkipName`, `TryReadQuestion`, `TypeOpt`, `HeaderLength` (Task 1); `TestDnsMessage` (Task 1).
- Produces (thêm vào `DnsWire`):
  - `uint? GetMinTtl(ReadOnlySpan<byte> response)`: TTL nhỏ nhất của mọi bản ghi ở answer/authority/additional, bỏ qua OPT. Trả `null` nếu không có bản ghi nào hoặc gói sai định dạng, nghĩa là **không cache**.
  - `byte[] RewriteForClient(ReadOnlySpan<byte> response, ushort id, uint elapsedSeconds, uint maxTtlSeconds = uint.MaxValue)`: trả bản sao, ghi ID của client, mỗi TTL thành `max(0, min(ttl, maxTtlSeconds) − elapsedSeconds)`; OPT giữ nguyên.
  - `bool IsResponseTo(ReadOnlySpan<byte> response, DnsQuestion question)`: QR=1, QDCOUNT=1, question khớp (không phân biệt hoa thường).
  - `internal bool TryGetTtlOffsets(ReadOnlySpan<byte>, List<int> offsets)`: vị trí 4 byte TTL của từng bản ghi trừ OPT.

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Dns/DnsWireRecordTests.cs
using System.Buffers.Binary;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DnsWireRecordTests
{
    private const string Name = "store.steampowered.com";
    private static readonly DnsQuestion StoreA = new(Name, TestDnsMessage.TypeA, 1);

    [Fact]
    public void Min_ttl_is_smallest_record_ttl() =>
        Assert.Equal(60u, DnsWire.GetMinTtl(TestDnsMessage.Response(Name, 0, 300, 60, 900)));

    [Fact]
    public void Opt_record_is_ignored_for_ttl() =>
        Assert.Equal(300u, DnsWire.GetMinTtl(TestDnsMessage.ResponseWithOpt(Name, 0, 300)));

    [Fact]
    public void Nxdomain_uses_soa_ttl() =>
        Assert.Equal(120u, DnsWire.GetMinTtl(TestDnsMessage.NxDomain(Name, 0, 120)));

    [Fact]
    public void Response_without_records_has_no_ttl() =>
        Assert.Null(DnsWire.GetMinTtl(TestDnsMessage.Response(Name, 0)));

    [Fact]
    public void Malformed_response_has_no_ttl() =>
        Assert.Null(DnsWire.GetMinTtl(TestDnsMessage.Response(Name, 0, 300)[..^3]));

    [Fact]
    public void Rewrite_sets_client_id_and_subtracts_elapsed_without_touching_original()
    {
        var original = TestDnsMessage.Response(Name, 0, 300, 60);

        var rewritten = DnsWire.RewriteForClient(original, 0xBEEF, elapsedSeconds: 100);

        Assert.Equal(0xBEEF, DnsWire.ReadId(rewritten));
        var ttls = Ttls(rewritten);
        Assert.Equal(200u, ttls[0]);
        Assert.Equal(0u, ttls[1]);
        Assert.Equal(300u, Ttls(original)[0]);
        Assert.Equal(0, DnsWire.ReadId(original));
    }

    [Fact]
    public void Rewrite_clamps_to_max_before_subtracting() =>
        Assert.Equal(800u, Ttls(DnsWire.RewriteForClient(TestDnsMessage.Response(Name, 0, 3600), 1, 100, maxTtlSeconds: 900))[0]);

    [Fact]
    public void Rewrite_leaves_opt_record_untouched()
    {
        var original = TestDnsMessage.ResponseWithOpt(Name, 0, 300);
        var rewritten = DnsWire.RewriteForClient(original, 1, 100);
        Assert.Equal(original[^11..], rewritten[^11..]);
    }

    [Fact]
    public void Matching_response_is_recognized() =>
        Assert.True(DnsWire.IsResponseTo(TestDnsMessage.Response(Name, 0, 300), StoreA));

    [Fact]
    public void Response_name_match_ignores_case() =>
        Assert.True(DnsWire.IsResponseTo(TestDnsMessage.Response("STORE.SteamPowered.com", 0, 300), StoreA));

    [Fact]
    public void Response_for_other_name_does_not_match() =>
        Assert.False(DnsWire.IsResponseTo(TestDnsMessage.Response("other.com", 0, 300), StoreA));

    [Fact]
    public void Query_is_not_a_response() =>
        Assert.False(DnsWire.IsResponseTo(TestDnsMessage.Query(Name), StoreA));

    [Fact]
    public void Response_for_other_type_does_not_match() =>
        Assert.False(DnsWire.IsResponseTo(TestDnsMessage.Response(Name, 0, 300), StoreA with { Type = TestDnsMessage.TypeAaaa }));

    private static uint[] Ttls(byte[] msg)
    {
        var offsets = new List<int>();
        Assert.True(DnsWire.TryGetTtlOffsets(msg, offsets));
        return offsets.Select(at => BinaryPrimitives.ReadUInt32BigEndian(msg.AsSpan(at))).ToArray();
    }
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsWireRecordTests`
Expected: FAIL khi biên dịch, báo `'DnsWire' does not contain a definition for 'GetMinTtl'`.

- [ ] **Step 3: Thêm vào class `DnsWire`** (đặt sau `BuildHeaderOnlyResponse`)

```csharp
    /// <summary>TTL nhỏ nhất (giây) của các bản ghi, bỏ qua OPT. Không có bản ghi hoặc sai định dạng thì null: không cache.</summary>
    public static uint? GetMinTtl(ReadOnlySpan<byte> response)
    {
        var offsets = new List<int>();
        if (!TryGetTtlOffsets(response, offsets) || offsets.Count == 0) return null;
        var min = uint.MaxValue;
        foreach (var at in offsets) min = Math.Min(min, BinaryPrimitives.ReadUInt32BigEndian(response[at..]));
        return min;
    }

    /// <summary>Bản sao response mang ID của client; mỗi TTL = max(0, min(ttl, maxTtlSeconds) − elapsedSeconds).</summary>
    public static byte[] RewriteForClient(ReadOnlySpan<byte> response, ushort id, uint elapsedSeconds, uint maxTtlSeconds = uint.MaxValue)
    {
        if (response.Length < HeaderLength) throw new ArgumentException("Gói ngắn hơn header DNS.", nameof(response));
        var copy = response.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(copy, id);
        var offsets = new List<int>();
        if (!TryGetTtlOffsets(copy, offsets)) return copy;
        foreach (var at in offsets)
        {
            var ttl = Math.Min(BinaryPrimitives.ReadUInt32BigEndian(copy.AsSpan(at)), maxTtlSeconds);
            BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(at), ttl > elapsedSeconds ? ttl - elapsedSeconds : 0);
        }
        return copy;
    }

    /// <summary>Đúng là response (QR=1) cho đúng một question này.</summary>
    public static bool IsResponseTo(ReadOnlySpan<byte> response, DnsQuestion question)
    {
        if (response.Length < HeaderLength || (Read16(response, 2) & FlagResponse) == 0 || Read16(response, 4) != 1) return false;
        var offset = HeaderLength;
        return TryReadQuestion(response, ref offset, out var echoed) && echoed == question;
    }

    internal static bool TryGetTtlOffsets(ReadOnlySpan<byte> msg, List<int> offsets)
    {
        if (msg.Length < HeaderLength) return false;
        var offset = HeaderLength;
        for (var i = Read16(msg, 4); i > 0; i--)
        {
            if (!TrySkipName(msg, ref offset) || offset + 4 > msg.Length) return false;
            offset += 4;
        }
        var records = Read16(msg, 6) + Read16(msg, 8) + Read16(msg, 10);
        for (var i = 0; i < records; i++)
        {
            if (!TrySkipName(msg, ref offset) || offset + 10 > msg.Length) return false;
            if (Read16(msg, offset) != TypeOpt) offsets.Add(offset + 4);
            offset += 10 + Read16(msg, offset + 8);
            if (offset > msg.Length) return false;
        }
        return true;
    }
```

- [ ] **Step 4: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsWire`
Expected: PASS, 27 test (14 + 13).

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 75`.

---

### Task 3: `DnsCache` theo TTL, trần 15 phút

**Files:**
- Create: `src/LunaDns.Core/Dns/DnsCache.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/DnsCacheTests.cs`

**Interfaces:**
- Consumes: `DnsQuestion`, `DnsWire.GetMinTtl`, `DnsWire.RewriteForClient` (Task 1–2).
- Produces: `sealed class DnsCache`
  - `const uint MaxTtlSeconds = 900` (spec 4.2: tối đa 15 phút)
  - `DnsCache(TimeProvider? time = null, int capacity = 2048)`
  - `void Store(DnsQuestion question, ReadOnlySpan<byte> response)`: không lưu nếu `GetMinTtl` là `null` hoặc `0`. Hết hạn sau `min(ttl, 900)` giây. Đầy thì bỏ mục hết hạn sớm nhất. Lưu bản sao của response.
  - `bool TryGet(DnsQuestion question, ushort id, [NotNullWhen(true)] out byte[]? response)`: trả bản đã đổi ID và trừ TTL theo thời gian đã nằm trong cache. Mục hết hạn thì xoá và trả `false`.
  - `int Count { get; }`
  - Thread-safe (dùng `lock`), vì stub gọi cache từ nhiều task cùng lúc.

- [ ] **Step 1: Viết test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Dns/DnsCacheTests.cs
using System.Buffers.Binary;
using LunaDns.Core.Dns;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Dns;

public class DnsCacheTests
{
    private const string Name = "store.steampowered.com";
    private static readonly DnsQuestion StoreA = new(Name, TestDnsMessage.TypeA, 1);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    private static uint FirstTtl(byte[] msg)
    {
        var offsets = new List<int>();
        Assert.True(DnsWire.TryGetTtlOffsets(msg, offsets));
        return BinaryPrimitives.ReadUInt32BigEndian(msg.AsSpan(offsets[0]));
    }

    [Fact]
    public void Hit_returns_response_with_client_id()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 300));

        Assert.True(cache.TryGet(StoreA, 0x4321, out var r));
        Assert.Equal(0x4321, DnsWire.ReadId(r));
        Assert.Equal(300u, FirstTtl(r));
    }

    [Fact]
    public void Ttl_counts_down_with_time()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 300));
        _time.Advance(TimeSpan.FromSeconds(100));

        Assert.True(cache.TryGet(StoreA, 1, out var r));
        Assert.Equal(200u, FirstTtl(r));
    }

    [Fact]
    public void Expired_entry_is_removed()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 300));
        _time.Advance(TimeSpan.FromSeconds(300));

        Assert.False(cache.TryGet(StoreA, 1, out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Ttl_is_capped_at_15_minutes()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 3600));

        Assert.True(cache.TryGet(StoreA, 1, out var r));
        Assert.Equal(DnsCache.MaxTtlSeconds, FirstTtl(r));
        _time.Advance(TimeSpan.FromSeconds(DnsCache.MaxTtlSeconds));
        Assert.False(cache.TryGet(StoreA, 1, out _));
    }

    [Fact]
    public void Zero_ttl_is_not_cached()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 0));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Response_without_records_is_not_cached()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Key_includes_record_type()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 300));
        Assert.False(cache.TryGet(StoreA with { Type = TestDnsMessage.TypeAaaa }, 1, out _));
    }

    [Fact]
    public void Full_cache_evicts_entry_expiring_first()
    {
        var cache = new DnsCache(_time, capacity: 2);
        var a = new DnsQuestion("a.steampowered.com", TestDnsMessage.TypeA, 1);
        var b = new DnsQuestion("b.steampowered.com", TestDnsMessage.TypeA, 1);
        var c = new DnsQuestion("c.steampowered.com", TestDnsMessage.TypeA, 1);

        cache.Store(a, TestDnsMessage.Response(a.Name, 0, 600));
        cache.Store(b, TestDnsMessage.Response(b.Name, 0, 60));
        cache.Store(c, TestDnsMessage.Response(c.Name, 0, 300));

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet(b, 1, out _));
        Assert.True(cache.TryGet(a, 1, out _));
        Assert.True(cache.TryGet(c, 1, out _));
    }

    [Fact]
    public void Stored_bytes_are_copied()
    {
        var cache = new DnsCache(_time);
        var response = TestDnsMessage.Response(Name, 0, 300);
        cache.Store(StoreA, response);
        Array.Clear(response);

        Assert.True(cache.TryGet(StoreA, 1, out var r));
        Assert.Equal(300u, FirstTtl(r));
    }
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsCacheTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'DnsCache' could not be found`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Dns/DnsCache.cs
using System.Diagnostics.CodeAnalysis;

namespace LunaDns.Core.Dns;

/// <summary>Cache response DoH theo (tên, type, class). TTL lấy từ bản ghi, tối đa 15 phút (spec 4.2).</summary>
public sealed class DnsCache
{
    public const uint MaxTtlSeconds = 15 * 60;

    private readonly TimeProvider _time;
    private readonly int _capacity;
    private readonly Dictionary<DnsQuestion, Entry> _entries = [];
    private readonly object _gate = new();

    public DnsCache(TimeProvider? time = null, int capacity = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _time = time ?? TimeProvider.System;
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    public void Store(DnsQuestion question, ReadOnlySpan<byte> response)
    {
        var ttl = DnsWire.GetMinTtl(response);
        if (ttl is null or 0) return;
        var now = _time.GetUtcNow();
        var entry = new Entry(response.ToArray(), now, now.AddSeconds(Math.Min(ttl.Value, MaxTtlSeconds)));
        lock (_gate)
        {
            if (_entries.Count >= _capacity && !_entries.ContainsKey(question))
                _entries.Remove(_entries.MinBy(e => e.Value.ExpiresAt).Key);
            _entries[question] = entry;
        }
    }

    public bool TryGet(DnsQuestion question, ushort id, [NotNullWhen(true)] out byte[]? response)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_entries.TryGetValue(question, out var entry))
            {
                if (now < entry.ExpiresAt)
                {
                    var elapsed = (uint)(now - entry.StoredAt).TotalSeconds;
                    response = DnsWire.RewriteForClient(entry.Response, id, elapsed, MaxTtlSeconds);
                    return true;
                }
                _entries.Remove(question);
            }
        }
        response = null;
        return false;
    }

    private sealed record Entry(byte[] Response, DateTimeOffset StoredAt, DateTimeOffset ExpiresAt);
}
```

- [ ] **Step 4: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsCacheTests`
Expected: PASS, 9 test.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 84`.

---

### Task 4: `DohClient` (RFC 8484, Cloudflare rồi Google, nối qua IP bootstrap)

**Files:**
- Modify: `src/LunaDns.Core/LunaDns.Core.csproj` (thêm package)
- Create: `src/LunaDns.Core/Dns/IDnsUpstream.cs`, `src/LunaDns.Core/Dns/DohServer.cs`, `src/LunaDns.Core/Dns/DohException.cs`, `src/LunaDns.Core/Dns/DohClient.cs`
- Test: `tests/LunaDns.Core.Tests/NetworkFactAttribute.cs`, `tests/LunaDns.Core.Tests/Dns/FakeHttpHandler.cs`, `tests/LunaDns.Core.Tests/Dns/DohClientTests.cs`, `tests/LunaDns.Core.Tests/Dns/DohClientNetworkTests.cs`

**Interfaces:**
- Consumes: `DnsWire.TryParseQuery`, `DnsWire.IsResponseTo`, `DnsWire.ReadRcode`, `DnsWire.GetMinTtl` (Task 1–2).
- Produces:
  - `interface IDnsUpstream { Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken); }`: mọi máy chủ đều lỗi thì ném `DohException`; người gọi huỷ thì ném `OperationCanceledException`.
  - `sealed record DohServer(string Name, Uri Endpoint, IReadOnlyList<IPAddress> BootstrapAddresses)` với `static DohServer Cloudflare`, `static DohServer Google`, `static IReadOnlyList<DohServer> Defaults` (= Cloudflare, Google).
  - `sealed class DohException : Exception` (đủ 3 constructor chuẩn).
  - `sealed partial class DohClient : IDnsUpstream, IDisposable`
    - `const string MediaType = "application/dns-message"`, `static readonly TimeSpan DefaultTimeout` = 3 giây cho mỗi máy chủ
    - `DohClient(HttpMessageHandler handler, IReadOnlyList<DohServer> servers, TimeSpan? timeout = null, ILogger<DohClient>? logger = null)`: client giữ quyền sở hữu `handler`
    - `static DohClient Create(IReadOnlyList<DohServer> servers, ILogger<DohClient>? logger = null)`: bản thật dùng `SocketsHttpHandler`, `UseProxy = false`, `ConnectCallback` nối tới `BootstrapAddresses`
    - Gửi `POST` với body là truy vấn có **ID = 0** (RFC 8484 §4.1). Response trả về giữ ID 0; đổi sang ID của client là việc của Task 5.

- [ ] **Step 1: Thêm package**

```bash
dotnet add src/LunaDns.Core package Microsoft.Extensions.Logging.Abstractions --version 8.0.2
```

Expected: `LunaDns.Core.csproj` có `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />`. Nếu restore báo NU1605 (hạ phiên bản) thì nâng lên đúng bản mà NuGet yêu cầu, vẫn trong dòng 8.0.x, rồi ghi vào báo cáo.

- [ ] **Step 2: Viết helper test và test lỗi**

```csharp
// tests/LunaDns.Core.Tests/NetworkFactAttribute.cs
namespace LunaDns.Core.Tests;

/// <summary>Test cần Internet thật: chỉ chạy khi đặt LUNADNS_NETWORK_TESTS=1, để lệnh kiểm tra mặc định không phụ thuộc mạng.</summary>
public sealed class NetworkFactAttribute : FactAttribute
{
    public NetworkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUNADNS_NETWORK_TESTS") != "1")
            Skip = "Đặt LUNADNS_NETWORK_TESTS=1 để chạy test cần mạng.";
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Dns/FakeHttpHandler.cs
using System.Net;
using System.Net.Http.Headers;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

internal sealed record RecordedRequest(Uri Uri, HttpMethod Method, string? ContentType, string Accept, byte[] Body);

internal sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.RequestUri!, request.Method,
            request.Content?.Headers.ContentType?.MediaType, request.Headers.Accept.ToString(), body));
        return await respond(request, cancellationToken);
    }

    public static HttpResponseMessage DnsMessage(byte[] body, string mediaType = DohClient.MediaType)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);
}
```

```csharp
// tests/LunaDns.Core.Tests/Dns/DohClientTests.cs
using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DohClientTests
{
    private const string Name = "store.steampowered.com";
    private static readonly byte[] StoreQuery = TestDnsMessage.Query(Name);

    private static byte[] Answer() => TestDnsMessage.Response(Name, 0, 300);

    private static DohClient Client(FakeHttpHandler handler, TimeSpan? timeout = null) =>
        new(handler, DohServer.Defaults, timeout);

    /// <summary>Cloudflare chạy theo <paramref name="cloudflare"/>, Google luôn trả lời đúng.</summary>
    private static FakeHttpHandler Route(Func<CancellationToken, Task<HttpResponseMessage>> cloudflare) =>
        new((request, ct) => request.RequestUri!.Host == DohServer.Cloudflare.Endpoint.Host
            ? cloudflare(ct)
            : Task.FromResult(FakeHttpHandler.DnsMessage(Answer())));

    [Fact]
    public async Task Posts_wire_query_with_zero_id_and_dns_message_headers()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.DnsMessage(Answer())));
        using var client = Client(handler);

        var response = await client.QueryAsync(StoreQuery, CancellationToken.None);

        Assert.Equal(Answer(), response);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(DohServer.Cloudflare.Endpoint, request.Uri);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(DohClient.MediaType, request.ContentType);
        Assert.Contains(DohClient.MediaType, request.Accept, StringComparison.Ordinal);
        Assert.Equal(0, DnsWire.ReadId(request.Body));
        Assert.Equal(StoreQuery[2..], request.Body[2..]);
    }

    [Fact]
    public async Task Falls_back_to_google_on_http_error()
    {
        var handler = Route(_ => Task.FromResult(FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable)));
        using var client = Client(handler);

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(DohServer.Google.Endpoint, handler.Requests[1].Uri);
    }

    [Fact]
    public async Task Falls_back_to_google_on_timeout()
    {
        var handler = Route(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeHttpHandler.Status(HttpStatusCode.OK);
        });
        using var client = Client(handler, TimeSpan.FromMilliseconds(200));

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Falls_back_when_response_is_for_another_question()
    {
        var handler = Route(_ => Task.FromResult(FakeHttpHandler.DnsMessage(TestDnsMessage.Response("other.com", 0, 300))));
        using var client = Client(handler);

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Falls_back_on_wrong_content_type()
    {
        var handler = Route(_ => Task.FromResult(FakeHttpHandler.DnsMessage(Answer(), "text/html")));
        using var client = Client(handler);

        Assert.Equal(Answer(), await client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Throws_DohException_when_every_server_fails()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Status(HttpStatusCode.InternalServerError)));
        using var client = Client(handler);

        await Assert.ThrowsAsync<DohException>(() => client.QueryAsync(StoreQuery, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_fallback()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.DnsMessage(Answer())));
        using var client = Client(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.QueryAsync(StoreQuery, cts.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Rejects_malformed_query()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.DnsMessage(Answer())));
        using var client = Client(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.QueryAsync(new byte[5], CancellationToken.None));
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Dns/DohClientNetworkTests.cs
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DohClientNetworkTests
{
    [NetworkFact]
    public async Task Each_default_server_answers_via_bootstrap_ip()
    {
        foreach (var server in DohServer.Defaults)
        {
            using var client = DohClient.Create([server]);
            var response = await client.QueryAsync(TestDnsMessage.Query("store.steampowered.com"), CancellationToken.None);

            Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(response));
            Assert.NotNull(DnsWire.GetMinTtl(response));
        }
    }
}
```

- [ ] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DohClient`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'DohClient' could not be found`.

- [ ] **Step 4: Viết code**

```csharp
// src/LunaDns.Core/Dns/IDnsUpstream.cs
namespace LunaDns.Core.Dns;

/// <summary>Nơi stub hỏi tiếp. Mọi máy chủ đều lỗi thì ném DohException; người gọi huỷ thì ném OperationCanceledException.</summary>
public interface IDnsUpstream
{
    Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken);
}
```

```csharp
// src/LunaDns.Core/Dns/DohServer.cs
using System.Net;

namespace LunaDns.Core.Dns;

/// <summary>
/// Máy chủ DoH (RFC 8484). Kết nối đi thẳng tới <see cref="BootstrapAddresses"/> để khỏi hỏi DNS hệ thống,
/// vì DNS đó có thể đã bị đầu độc. TLS vẫn kiểm tra chứng chỉ theo host của <see cref="Endpoint"/>.
/// </summary>
public sealed record DohServer(string Name, Uri Endpoint, IReadOnlyList<IPAddress> BootstrapAddresses)
{
    public static DohServer Cloudflare { get; } = new("Cloudflare", new Uri("https://cloudflare-dns.com/dns-query"),
        [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.0.0.1")]);

    public static DohServer Google { get; } = new("Google", new Uri("https://dns.google/dns-query"),
        [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("8.8.4.4")]);

    /// <summary>Cloudflare trước, dự phòng Google (spec 4.2). KH5 cho đổi thứ tự trong mục Nâng cao.</summary>
    public static IReadOnlyList<DohServer> Defaults { get; } = [Cloudflare, Google];
}
```

```csharp
// src/LunaDns.Core/Dns/DohException.cs
namespace LunaDns.Core.Dns;

public sealed class DohException : Exception
{
    public DohException()
    {
    }

    public DohException(string message) : base(message)
    {
    }

    public DohException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
```

```csharp
// src/LunaDns.Core/Dns/DohClient.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>DoH theo RFC 8484 (POST, application/dns-message). Thử lần lượt từng máy chủ, mỗi máy chủ có timeout riêng.</summary>
public sealed partial class DohClient : IDnsUpstream, IDisposable
{
    public const string MediaType = "application/dns-message";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    private const int MaxResponseBytes = ushort.MaxValue;

    private readonly HttpMessageInvoker _http;
    private readonly IReadOnlyList<DohServer> _servers;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;

    public DohClient(HttpMessageHandler handler, IReadOnlyList<DohServer> servers, TimeSpan? timeout = null, ILogger<DohClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(servers);
        if (servers.Count == 0) throw new ArgumentException("Cần ít nhất một máy chủ DoH.", nameof(servers));
        _http = new HttpMessageInvoker(handler, disposeHandler: true);
        _servers = servers;
        _timeout = timeout ?? DefaultTimeout;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Client thật: không qua proxy hệ thống, nối thẳng tới IP bootstrap của từng máy chủ.</summary>
    public static DohClient Create(IReadOnlyList<DohServer> servers, ILogger<DohClient>? logger = null)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = DefaultTimeout,
            ConnectCallback = (context, ct) => ConnectToBootstrapAsync(servers, context, ct),
        };
        return new DohClient(handler, servers, logger: logger);
    }

    public async Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
    {
        if (!DnsWire.TryParseQuery(query.Span, out var parsed))
            throw new ArgumentException("Truy vấn DNS sai định dạng.", nameof(query));
        var body = query.ToArray();
        body[0] = 0; // RFC 8484 §4.1: ID = 0 để cache HTTP dùng chung được
        body[1] = 0;

        var errors = new List<Exception>();
        foreach (var server in _servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                var response = await SendAsync(server, body, timeout.Token).ConfigureAwait(false);
                if (!DnsWire.IsResponseTo(response, parsed.Question))
                    throw new DohException($"{server.Name} trả response không khớp truy vấn.");
                return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or DohException or IOException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                LogServerFailed(_logger, server.Name, ex);
                errors.Add(ex);
            }
        }
        throw new DohException("Mọi máy chủ DoH đều lỗi.", new AggregateException(errors));
    }

    public void Dispose() => _http.Dispose();

    private async Task<byte[]> SendAsync(DohServer server, byte[] body, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(MediaType);
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new DohException($"{server.Name} trả HTTP {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentType?.MediaType != MediaType)
            throw new DohException($"{server.Name} trả Content-Type lạ.");
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new DohException($"{server.Name} trả response quá lớn.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxResponseBytes)
            throw new DohException($"{server.Name} trả response quá lớn.");
        return bytes;
    }

    private static async ValueTask<Stream> ConnectToBootstrapAsync(
        IReadOnlyList<DohServer> servers, SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var server = servers.FirstOrDefault(s => string.Equals(s.Endpoint.Host, host, StringComparison.OrdinalIgnoreCase))
                     ?? throw new DohException($"Host DoH lạ: {host}.");
        SocketException? last = null;
        foreach (var address in server.BootstrapAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
        }
        var message = $"Không nối được tới {server.Name}.";
        throw last is null ? new DohException(message) : new DohException(message, last);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DoH {Server} lỗi, chuyển sang máy chủ kế")]
    private static partial void LogServerFailed(ILogger logger, string server, Exception exception);
}
```

- [ ] **Step 5: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DohClient`
Expected: PASS 8, Skipped 1 (test mạng).

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 92, Skipped: 1`.

Có mạng thì chạy thêm (không bắt buộc ở bước này, Task 7 sẽ chạy lại):
`$env:LUNADNS_NETWORK_TESTS="1"; dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DohClientNetworkTests; Remove-Item Env:LUNADNS_NETWORK_TESTS`
Expected: PASS 1.

---

### Task 5: `DnsQueryHandler` (REFUSED, cache, DoH, SERVFAIL, cắt cụt UDP)

**Files:**
- Create: `src/LunaDns.Core/Dns/DnsTransport.cs`, `src/LunaDns.Core/Dns/DnsQueryHandler.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/FakeUpstream.cs`, `tests/LunaDns.Core.Tests/Dns/DnsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `DnsWire.*` (Task 1–2), `DnsCache` (Task 3), `IDnsUpstream`, `DohException` (Task 4), `DomainMatcher.Matches`, `GameDomains.All` (KH1).
- Produces:
  - `enum DnsTransport { Udp, Tcp }`
  - `sealed partial class DnsQueryHandler(IReadOnlyList<string> domains, IDnsUpstream upstream, DnsCache cache, ILogger<DnsQueryHandler>? logger = null)`
    - `Task<byte[]?> HandleAsync(ReadOnlyMemory<byte> packet, DnsTransport transport, CancellationToken cancellationToken)`. Thứ tự xử lý:
      1. Gói ngắn hơn 12 byte hoặc có QR=1 thì trả `null` (im lặng).
      2. Đọc không được thì `FORMERR`. Opcode khác 0 thì `NOTIMP`.
      3. Tên không thuộc `domains` thì `REFUSED`, không hỏi DoH.
      4. Có trong cache thì trả bản trong cache. Không có thì hỏi upstream, lưu cache, đổi ID. Nếu bị `DohException` thì `SERVFAIL`.
      5. Qua UDP mà response dài hơn `MaxUdpPayload` thì trả header + question với `TC=1`, để client hỏi lại qua TCP. Qua TCP thì giới hạn là 65535 byte.
    - `domains` do người gọi chọn (KH5 đưa vào các nhóm người dùng bật). Test dùng `GameDomains.All`.
  - Test: `FakeUpstream : IDnsUpstream` có `Respond`, `Calls`, và `static Func<byte[], byte[]> Answer(int records = 1, uint ttl = 300)`. Task 6 dùng lại.

- [ ] **Step 1: Viết upstream giả và test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Dns/FakeUpstream.cs
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

internal sealed class FakeUpstream : IDnsUpstream
{
    private int _calls;

    public Func<byte[], byte[]> Respond { get; set; } = Answer();

    public int Calls => Volatile.Read(ref _calls);

    public Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Respond(query.ToArray()));
    }

    /// <summary>Trả lời đúng tên được hỏi bằng <paramref name="records"/> bản ghi A, ID = 0 như DoH.</summary>
    public static Func<byte[], byte[]> Answer(int records = 1, uint ttl = 300) => query =>
    {
        Assert.True(DnsWire.TryParseQuery(query, out var parsed));
        return TestDnsMessage.Response(parsed.Question.Name, 0, Enumerable.Repeat(ttl, records).ToArray());
    };
}
```

```csharp
// tests/LunaDns.Core.Tests/Dns/DnsQueryHandlerTests.cs
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Dns;

public class DnsQueryHandlerTests
{
    private const string Name = "store.steampowered.com";
    private readonly FakeUpstream _upstream = new();
    private readonly DnsQueryHandler _handler;

    public DnsQueryHandlerTests() => _handler = new DnsQueryHandler(GameDomains.All, _upstream, new DnsCache());

    private Task<byte[]?> Handle(byte[] packet, DnsTransport transport = DnsTransport.Udp) =>
        _handler.HandleAsync(packet, transport, CancellationToken.None);

    private async Task<byte[]> HandleOk(byte[] packet, DnsTransport transport = DnsTransport.Udp)
    {
        var response = await Handle(packet, transport);
        Assert.NotNull(response);
        return response;
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("evilsteampowered.com")]
    public async Task Refuses_names_outside_game_domains_without_asking_upstream(string name)
    {
        var r = await HandleOk(TestDnsMessage.Query(name));

        Assert.Equal(DnsRcode.Refused, DnsWire.ReadRcode(r));
        Assert.Equal(0x1234, DnsWire.ReadId(r));
        Assert.Equal(0, _upstream.Calls);
    }

    [Fact]
    public async Task Forwards_game_domain_and_restores_client_id()
    {
        var r = await HandleOk(TestDnsMessage.Query(Name, id: 0x0A0B));

        Assert.Equal(0x0A0B, DnsWire.ReadId(r));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(r));
        Assert.Equal(1, TestDnsMessage.U16(r, 6));
        Assert.Equal(1, _upstream.Calls);
    }

    [Fact]
    public async Task Second_query_is_served_from_cache()
    {
        await HandleOk(TestDnsMessage.Query(Name, id: 1));
        var r = await HandleOk(TestDnsMessage.Query(Name, id: 2));

        Assert.Equal(2, DnsWire.ReadId(r));
        Assert.Equal(1, _upstream.Calls);
    }

    [Fact]
    public async Task Upstream_failure_returns_servfail()
    {
        _upstream.Respond = _ => throw new DohException("hỏng");

        var r = await HandleOk(TestDnsMessage.Query(Name));

        Assert.Equal(DnsRcode.ServFail, DnsWire.ReadRcode(r));
    }

    [Fact]
    public async Task Malformed_query_returns_formerr()
    {
        var packet = TestDnsMessage.Query(Name);
        packet[5] = 2; // QDCOUNT = 2

        var r = await HandleOk(packet);

        Assert.Equal(DnsRcode.FormErr, DnsWire.ReadRcode(r));
        Assert.Equal(0, _upstream.Calls);
    }

    [Fact]
    public async Task Packet_shorter_than_header_is_dropped() =>
        Assert.Null(await Handle(new byte[5]));

    [Fact]
    public async Task Stray_response_is_dropped() =>
        Assert.Null(await Handle(TestDnsMessage.Response(Name, 1, 300)));

    [Fact]
    public async Task Non_query_opcode_returns_notimp()
    {
        var r = await HandleOk(TestDnsMessage.Query(Name, flags: 0x0100 | (2 << 11)));
        Assert.Equal(DnsRcode.NotImp, DnsWire.ReadRcode(r));
    }

    [Fact]
    public async Task Large_answer_over_plain_udp_is_truncated()
    {
        _upstream.Respond = FakeUpstream.Answer(records: 40);

        var r = await HandleOk(TestDnsMessage.Query(Name));

        Assert.Equal(0x02, r[2] & 0x02);
        Assert.Equal(0, TestDnsMessage.U16(r, 6));
    }

    [Fact]
    public async Task Large_answer_over_tcp_is_complete()
    {
        _upstream.Respond = FakeUpstream.Answer(records: 40);

        var r = await HandleOk(TestDnsMessage.Query(Name), DnsTransport.Tcp);

        Assert.Equal(0, r[2] & 0x02);
        Assert.Equal(40, TestDnsMessage.U16(r, 6));
    }

    [Fact]
    public async Task Large_answer_fits_when_edns_allows()
    {
        _upstream.Respond = FakeUpstream.Answer(records: 40);

        var r = await HandleOk(TestDnsMessage.Query(Name, ednsUdpSize: 1232));

        Assert.Equal(40, TestDnsMessage.U16(r, 6));
    }
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsQueryHandlerTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'DnsQueryHandler' could not be found`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Dns/DnsTransport.cs
namespace LunaDns.Core.Dns;

public enum DnsTransport
{
    Udp,
    Tcp,
}
```

```csharp
// src/LunaDns.Core/Dns/DnsQueryHandler.cs
using LunaDns.Core.Domains;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>Xử lý một gói truy vấn của stub, không phụ thuộc UDP hay TCP (spec 4.2).</summary>
public sealed partial class DnsQueryHandler
{
    private readonly IReadOnlyList<string> _domains;
    private readonly IDnsUpstream _upstream;
    private readonly DnsCache _cache;
    private readonly ILogger _logger;

    /// <param name="domains">Domain được trả lời (domain và subdomain). Domain khác nhận REFUSED.</param>
    public DnsQueryHandler(IReadOnlyList<string> domains, IDnsUpstream upstream, DnsCache cache, ILogger<DnsQueryHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(domains);
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(cache);
        _domains = domains;
        _upstream = upstream;
        _cache = cache;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Response để gửi lại client, hoặc null khi phải im lặng (gói ngắn hơn header, hoặc là response lạc vào).</summary>
    public async Task<byte[]?> HandleAsync(ReadOnlyMemory<byte> packet, DnsTransport transport, CancellationToken cancellationToken)
    {
        if (packet.Length < DnsWire.HeaderLength || (packet.Span[2] & 0x80) != 0) return null;
        if (!DnsWire.TryParseQuery(packet.Span, out var query))
            return DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.FormErr);
        if (query.Opcode != 0)
            return DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.NotImp);
        if (!DomainMatcher.Matches(query.Question.Name, _domains))
        {
            LogRefused(_logger, query.Question.Name);
            return DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.Refused);
        }

        if (!_cache.TryGet(query.Question, query.Id, out var response))
        {
            byte[] fresh;
            try
            {
                fresh = await _upstream.QueryAsync(packet, cancellationToken).ConfigureAwait(false);
            }
            catch (DohException ex)
            {
                LogUpstreamFailed(_logger, query.Question.Name, ex);
                return DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.ServFail);
            }
            _cache.Store(query.Question, fresh);
            response = DnsWire.RewriteForClient(fresh, query.Id, elapsedSeconds: 0);
        }

        var limit = transport == DnsTransport.Udp ? query.MaxUdpPayload : ushort.MaxValue;
        return response.Length <= limit
            ? response
            : DnsWire.BuildHeaderOnlyResponse(packet.Span, DnsRcode.NoError, truncated: true);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Từ chối {Name}: không thuộc danh sách domain game")]
    private static partial void LogRefused(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DoH lỗi khi hỏi {Name}, trả SERVFAIL")]
    private static partial void LogUpstreamFailed(ILogger logger, string name, Exception exception);
}
```

- [ ] **Step 4: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DnsQueryHandlerTests`
Expected: PASS, 12 test.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 104, Skipped: 1`.

---

### Task 6: `DohStubServer` nghe UDP + TCP, bind exclusive

**Files:**
- Create: `src/LunaDns.Core/Dns/DnsStubError.cs`, `src/LunaDns.Core/Dns/DnsStubException.cs`, `src/LunaDns.Core/Dns/DohStubServer.cs`
- Test: `tests/LunaDns.Core.Tests/Dns/DnsTestClient.cs`, `tests/LunaDns.Core.Tests/Dns/DohStubServerTests.cs`

**Interfaces:**
- Consumes: `DnsQueryHandler.HandleAsync`, `DnsTransport` (Task 5), `DnsWire.HeaderLength` (Task 1); test dùng `FakeUpstream`, `TestDnsMessage`.
- Produces:
  - `enum DnsStubError { Other, PortInUse, AccessDenied }`
  - `sealed class DnsStubException : Exception` có `DnsStubError Error { get; }` và constructor `(DnsStubError error, string message, Exception innerException)`, cùng 3 constructor chuẩn (khi đó `Error = Other`).
  - `sealed partial class DohStubServer : IAsyncDisposable`
    - `static readonly IPEndPoint DefaultEndPoint` = `127.0.0.1:53`
    - `static DohStubServer Start(IPEndPoint endPoint, DnsQueryHandler handler, ILogger<DohStubServer>? logger = null)`: bind UDP rồi TCP cùng một cổng, cả hai đặt `ExclusiveAddressUse = true`. Cổng 0 nghĩa là chọn ngẫu nhiên; khi đó TCP dùng đúng cổng mà UDP đã nhận. Bind lỗi thì ném `DnsStubException` (`PortInUse` hoặc `AccessDenied`) và giải phóng socket đã mở.
    - `IPEndPoint LocalEndPoint { get; }`: cổng thật sau khi bind.
    - `Task Completion { get; }`: xong khi vòng nghe dừng. Nếu socket lỗi bất ngờ thì task này faulted. Watchdog ở KH5 chờ task này (spec 6).
    - `DisposeAsync()`: dừng vòng nghe, đóng socket, không ném lỗi.
  - Test: `DnsTestClient.UdpAsync(IPEndPoint, byte[])`, `.ConnectTcpAsync(IPEndPoint)`, `.TcpAsync(NetworkStream, byte[])`. Task 7 dùng lại.
  - Lưu ý Windows: nếu tiến trình khác đang giữ `0.0.0.0:53` (ví dụ dịch vụ Internet Connection Sharing), bind exclusive `127.0.0.1:53` có thể báo `AccessDenied` thay vì `PortInUse`. Vì vậy KH5 phải báo cho người dùng cả hai mã đó như "cổng 53 đang bị chiếm".

- [ ] **Step 1: Viết client test và test lỗi**

```csharp
// tests/LunaDns.Core.Tests/Dns/DnsTestClient.cs
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace LunaDns.Core.Tests.Dns;

internal static class DnsTestClient
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public static async Task<byte[]> UdpAsync(IPEndPoint server, byte[] query)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Connect(server);
        await udp.SendAsync(query);
        using var cts = new CancellationTokenSource(Limit);
        return (await udp.ReceiveAsync(cts.Token)).Buffer;
    }

    public static async Task<NetworkStream> ConnectTcpAsync(IPEndPoint server)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(server);
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>Gửi một truy vấn có tiền tố độ dài 2 byte (RFC 1035 §4.2.2) và đọc một response.</summary>
    public static async Task<byte[]> TcpAsync(NetworkStream stream, byte[] query)
    {
        using var cts = new CancellationTokenSource(Limit);
        var framed = new byte[2 + query.Length];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Length);
        query.CopyTo(framed, 2);
        await stream.WriteAsync(framed, cts.Token);

        var prefix = new byte[2];
        await stream.ReadExactlyAsync(prefix, cts.Token);
        var response = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
        await stream.ReadExactlyAsync(response, cts.Token);
        return response;
    }
}
```

```csharp
// tests/LunaDns.Core.Tests/Dns/DohStubServerTests.cs
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Dns;

public sealed class DohStubServerTests : IAsyncLifetime
{
    private const string Name = "store.steampowered.com";
    private static readonly byte[] Junk = [1, 2, 3];

    private readonly FakeUpstream _upstream = new();
    private DohStubServer _server = null!;

    public Task InitializeAsync()
    {
        _server = StartOn(0, _upstream);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private static DohStubServer StartOn(int port, IDnsUpstream upstream) =>
        DohStubServer.Start(new IPEndPoint(IPAddress.Loopback, port),
            new DnsQueryHandler(GameDomains.All, upstream, new DnsCache()));

    private static Socket ExclusiveSocket(SocketType type, ProtocolType protocol, int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, type, protocol) { ExclusiveAddressUse = true };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        return socket;
    }

    [Fact]
    public async Task Udp_answers_game_domain_with_client_id()
    {
        var r = await DnsTestClient.UdpAsync(_server.LocalEndPoint, TestDnsMessage.Query(Name, id: 0x0A0B));

        Assert.Equal(0x0A0B, DnsWire.ReadId(r));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(r));
        Assert.Equal(1, TestDnsMessage.U16(r, 6));
    }

    [Fact]
    public async Task Udp_refuses_other_domains()
    {
        var r = await DnsTestClient.UdpAsync(_server.LocalEndPoint, TestDnsMessage.Query("google.com"));

        Assert.Equal(DnsRcode.Refused, DnsWire.ReadRcode(r));
        Assert.Equal(0, _upstream.Calls);
    }

    [Fact]
    public async Task Tcp_answers_several_queries_on_one_connection()
    {
        await using var stream = await DnsTestClient.ConnectTcpAsync(_server.LocalEndPoint);

        var first = await DnsTestClient.TcpAsync(stream, TestDnsMessage.Query(Name, id: 1));
        var second = await DnsTestClient.TcpAsync(stream, TestDnsMessage.Query("steamcommunity.com", id: 2));

        Assert.Equal(1, DnsWire.ReadId(first));
        Assert.Equal(2, DnsWire.ReadId(second));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(second));
    }

    [Fact]
    public async Task Large_answer_is_truncated_on_udp_and_complete_on_tcp()
    {
        _upstream.Respond = FakeUpstream.Answer(records: 40);
        var query = TestDnsMessage.Query(Name);

        var udp = await DnsTestClient.UdpAsync(_server.LocalEndPoint, query);
        await using var stream = await DnsTestClient.ConnectTcpAsync(_server.LocalEndPoint);
        var tcp = await DnsTestClient.TcpAsync(stream, query);

        Assert.Equal(0x02, udp[2] & 0x02);
        Assert.Equal(0, tcp[2] & 0x02);
        Assert.Equal(40, TestDnsMessage.U16(tcp, 6));
    }

    [Fact]
    public async Task Survives_garbage_and_clients_that_left()
    {
        using (var gone = new UdpClient(AddressFamily.InterNetwork))
        {
            gone.Connect(_server.LocalEndPoint);
            await gone.SendAsync(Junk);
            await gone.SendAsync(TestDnsMessage.Query(Name));
        } // đóng trước khi nhận: gói trả lời làm Windows báo ICMP port unreachable ở phía stub

        var r = await DnsTestClient.UdpAsync(_server.LocalEndPoint, TestDnsMessage.Query("steamcommunity.com", id: 7));

        Assert.Equal(7, DnsWire.ReadId(r));
    }

    [Fact]
    public void Start_fails_with_PortInUse_when_udp_port_is_taken()
    {
        using var blocker = ExclusiveSocket(SocketType.Dgram, ProtocolType.Udp, 0);
        var port = ((IPEndPoint)blocker.LocalEndPoint!).Port;

        var ex = Assert.Throws<DnsStubException>(() => StartOn(port, _upstream));

        Assert.Equal(DnsStubError.PortInUse, ex.Error);
    }

    [Fact]
    public void Failed_start_releases_udp_port_when_tcp_port_is_taken()
    {
        using var blocker = ExclusiveSocket(SocketType.Stream, ProtocolType.Tcp, 0);
        var port = ((IPEndPoint)blocker.LocalEndPoint!).Port;

        Assert.Throws<DnsStubException>(() => StartOn(port, _upstream));

        using var probe = ExclusiveSocket(SocketType.Dgram, ProtocolType.Udp, port); // ném nếu stub còn giữ UDP
    }

    [Fact]
    public async Task Dispose_stops_loops_and_releases_port()
    {
        var server = StartOn(0, _upstream);
        var port = server.LocalEndPoint.Port;

        await server.DisposeAsync();

        Assert.True(server.Completion.IsCompletedSuccessfully);
        using var probe = ExclusiveSocket(SocketType.Dgram, ProtocolType.Udp, port);
    }
}
```

- [ ] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DohStubServerTests`
Expected: FAIL khi biên dịch, báo `The type or namespace name 'DohStubServer' could not be found`.

- [ ] **Step 3: Viết code**

```csharp
// src/LunaDns.Core/Dns/DnsStubError.cs
namespace LunaDns.Core.Dns;

public enum DnsStubError
{
    Other,
    PortInUse,
    AccessDenied,
}
```

```csharp
// src/LunaDns.Core/Dns/DnsStubException.cs
namespace LunaDns.Core.Dns;

/// <summary>Stub không bật được. <see cref="Error"/> để giao diện (KH5) chọn thông báo.</summary>
public sealed class DnsStubException : Exception
{
    public DnsStubException()
    {
    }

    public DnsStubException(string message) : base(message)
    {
    }

    public DnsStubException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DnsStubException(DnsStubError error, string message, Exception innerException) : base(message, innerException) =>
        Error = error;

    public DnsStubError Error { get; } = DnsStubError.Other;
}
```

```csharp
// src/LunaDns.Core/Dns/DohStubServer.cs
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>Stub DNS nghe UDP + TCP trên cùng một endpoint (mặc định 127.0.0.1:53), bind exclusive (spec 4.2).</summary>
public sealed partial class DohStubServer : IAsyncDisposable
{
    public static readonly IPEndPoint DefaultEndPoint = new(IPAddress.Loopback, 53);
    private const int MaxTcpConnections = 64;
    private static readonly TimeSpan TcpIdleTimeout = TimeSpan.FromSeconds(10);

    private readonly DnsQueryHandler _handler;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _tcpSlots = new(MaxTcpConnections);
    private Socket? _udp;
    private Socket? _tcp;
    private Task _loops = Task.CompletedTask;

    private DohStubServer(DnsQueryHandler handler, ILogger logger)
    {
        _handler = handler;
        _logger = logger;
    }

    public IPEndPoint LocalEndPoint { get; private set; } = DefaultEndPoint;

    /// <summary>Xong khi vòng nghe dừng; faulted nếu socket lỗi bất ngờ. Watchdog (KH5) chờ task này.</summary>
    public Task Completion => _loops;

    public static DohStubServer Start(IPEndPoint endPoint, DnsQueryHandler handler, ILogger<DohStubServer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(handler);
        var server = new DohStubServer(handler, (ILogger?)logger ?? NullLogger.Instance);
        try
        {
            server.Bind(endPoint);
        }
        catch
        {
            server._udp?.Dispose();
            server._tcp?.Dispose();
            throw;
        }
        server._loops = Task.WhenAll(Task.Run(server.UdpLoopAsync), Task.Run(server.TcpAcceptLoopAsync));
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        _udp?.Dispose();
        _tcp?.Dispose();
        try
        {
            await _loops.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Vòng nghe đã chết từ trước (Completion faulted); Dispose không ném lại.
            LogLoopFaulted(_logger, ex);
        }
        // Cố ý không Dispose _stop và _tcpSlots: gói và kết nối đang xử lý dở vẫn đọc token và trả slot.
    }

    private void Bind(IPEndPoint endPoint)
    {
        _udp = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { ExclusiveAddressUse = true };
        BindOrThrow(_udp, endPoint);
        var bound = new IPEndPoint(endPoint.Address, ((IPEndPoint)_udp.LocalEndPoint!).Port);
        _tcp = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true, NoDelay = true };
        BindOrThrow(_tcp, bound);
        _tcp.Listen(MaxTcpConnections);
        LocalEndPoint = bound;
    }

    private static void BindOrThrow(Socket socket, IPEndPoint endPoint)
    {
        try
        {
            socket.Bind(endPoint);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            throw new DnsStubException(DnsStubError.PortInUse, $"Cổng {endPoint} đang bị ứng dụng khác dùng.", ex);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            throw new DnsStubException(DnsStubError.AccessDenied,
                $"Không mở được cổng {endPoint}: cổng bị chặn hoặc ứng dụng khác đang giữ.", ex);
        }
    }

    private async Task UdpLoopAsync()
    {
        var buffer = new byte[ushort.MaxValue];
        EndPoint anyRemote = new IPEndPoint(
            LocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        while (true)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await _udp!.ReceiveFromAsync(buffer, SocketFlags.None, anyRemote, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Windows báo ICMP "port unreachable" của một gói trả lời trước: client đã đi, nghe tiếp.
                LogUdpReset(_logger);
                continue;
            }
            var packet = buffer.AsSpan(0, received.ReceivedBytes).ToArray();
            _ = RespondUdpAsync(packet, received.RemoteEndPoint);
        }
    }

    private async Task RespondUdpAsync(byte[] packet, EndPoint client)
    {
        try
        {
            var response = await _handler.HandleAsync(packet, DnsTransport.Udp, _stop.Token).ConfigureAwait(false);
            if (response is not null)
                await _udp!.SendToAsync(response, SocketFlags.None, client, _stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Ranh giới của một gói: gói lỗi không được làm chết stub.
            if (!_stop.IsCancellationRequested) LogPacketFailed(_logger, ex);
        }
    }

    private async Task TcpAcceptLoopAsync()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _tcp!.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            if (!_tcpSlots.Wait(0))
            {
                LogTcpBusy(_logger);
                client.Dispose();
                continue;
            }
            _ = ServeTcpAsync(client);
        }
    }

    private async Task ServeTcpAsync(Socket client)
    {
        var stream = new NetworkStream(client, ownsSocket: true);
        try
        {
            var prefix = new byte[2];
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                idle.CancelAfter(TcpIdleTimeout);
                if (!await ReadExactAsync(stream, prefix, idle.Token).ConfigureAwait(false)) break;
                var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
                if (length < DnsWire.HeaderLength) break;
                var packet = new byte[length];
                if (!await ReadExactAsync(stream, packet, idle.Token).ConfigureAwait(false)) break;

                var response = await _handler.HandleAsync(packet, DnsTransport.Tcp, _stop.Token).ConfigureAwait(false);
                if (response is null) break;
                var framed = new byte[2 + response.Length];
                BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)response.Length);
                response.CopyTo(framed, 2);
                await stream.WriteAsync(framed, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Rảnh quá 10 s (OperationCanceledException) là đóng bình thường; lỗi khác chỉ đóng kết nối này.
            if (ex is not OperationCanceledException && !_stop.IsCancellationRequested) LogTcpFailed(_logger, ex);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            _tcpSlots.Release();
        }
    }

    /// <summary>Đọc đủ buffer. Client đóng kết nối giữa chừng thì trả false.</summary>
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "UDP: client đã đóng cổng trước khi nhận trả lời")]
    private static partial void LogUdpReset(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Xử lý gói UDP lỗi")]
    private static partial void LogPacketFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quá 64 kết nối TCP, đóng kết nối mới")]
    private static partial void LogTcpBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kết nối TCP lỗi")]
    private static partial void LogTcpFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vòng nghe của stub đã dừng vì lỗi")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
```

- [ ] **Step 4: Chạy test và build**

Run: `dotnet test tests/LunaDns.Core.Tests --filter FullyQualifiedName~DohStubServerTests`
Expected: PASS, 8 test.

Có hai trường hợp đặc biệt. (1) Nếu `Start_fails_with_PortInUse...` nhận `AccessDenied` thì **không** sửa assertion; báo lại kèm mã `SocketError` thật, phiên chính sẽ quyết. (2) Nếu analyzer báo CA1031 ở `RespondUdpAsync`/`ServeTcpAsync`/`DisposeAsync` thì đặt `[SuppressMessage("Design", "CA1031", Justification = "Ranh giới vòng nghe: một gói hay kết nối lỗi không được làm chết stub.")]` trên đúng các method đó, theo Global Constraints.

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 112, Skipped: 1`. Chạy lệnh test 3 lần liền: cả 3 lần đều xanh, để chắc test socket không chập chờn.

---

### Task 7: Kiểm tra đầu-cuối với DoH thật (PO chạy, cần Internet, không cần Admin)

**Files:**
- Test: `tests/LunaDns.Core.Tests/Dns/DohStubNetworkTests.cs`

**Interfaces:**
- Consumes: `DohClient.Create`, `DohServer.Defaults` (Task 4); `DnsQueryHandler` (Task 5); `DohStubServer.Start`, `DnsTestClient` (Task 6); `GameDomains.All` (KH1).
- Produces: không có API mới. Đây là bằng chứng chuỗi stub → DoH → cache chạy với mạng thật, trước khi KH4 nối NRPT vào.

- [ ] **Step 1: Viết test**

```csharp
// tests/LunaDns.Core.Tests/Dns/DohStubNetworkTests.cs
using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Dns;

public class DohStubNetworkTests
{
    [NetworkFact]
    public async Task Stub_with_real_doh_resolves_steam_over_udp_and_tcp()
    {
        using var doh = DohClient.Create(DohServer.Defaults);
        var handler = new DnsQueryHandler(GameDomains.All, doh, new DnsCache());
        await using var server = DohStubServer.Start(new IPEndPoint(IPAddress.Loopback, 0), handler);

        var udp = await DnsTestClient.UdpAsync(server.LocalEndPoint, TestDnsMessage.Query("store.steampowered.com", id: 0x4242));
        await using var stream = await DnsTestClient.ConnectTcpAsync(server.LocalEndPoint);
        var tcp = await DnsTestClient.TcpAsync(stream, TestDnsMessage.Query("steamcommunity.com", id: 0x4343));
        var refused = await DnsTestClient.UdpAsync(server.LocalEndPoint, TestDnsMessage.Query("google.com"));

        Assert.Equal(0x4242, DnsWire.ReadId(udp));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(udp));
        Assert.True(TestDnsMessage.U16(udp, 6) > 0, "store.steampowered.com phải có ít nhất một bản ghi answer");
        Assert.Equal(0x4343, DnsWire.ReadId(tcp));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(tcp));
        Assert.Equal(DnsRcode.Refused, DnsWire.ReadRcode(refused));
    }
}
```

- [ ] **Step 2: Chạy bộ test mặc định (không mạng)**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.Core.Tests`
Expected: `0 Warning(s)`, `Failed: 0, Passed: 112, Skipped: 2`.

- [ ] **Step 3: PO chạy test mạng trên máy thật**

Run (PowerShell, user thường):
`$env:PATH="$env:ProgramFiles\dotnet;$env:PATH"; $env:LUNADNS_NETWORK_TESTS="1"; dotnet test tests/LunaDns.Core.Tests --filter "FullyQualifiedName~NetworkTests"; Remove-Item Env:LUNADNS_NETWORK_TESTS`
Expected: `Passed: 2` (`DohClientNetworkTests` + `DohStubNetworkTests`).

Nếu fail: chép nguyên dòng lỗi, ghi rõ nhà mạng và đang dùng Wi-Fi hay 4G. Nếu chỉ một máy chủ lỗi (ví dụ nhà mạng chặn 1.1.1.1), `DohClientNetworkTests` sẽ báo tên máy chủ đó. Kết quả này cần cho KH3 (Prober) và mục Nâng cao ở KH5.

---

## Đối chiếu spec (tự rà soát)

| Mục spec | Kế hoạch 2 | Phần còn lại ở |
|---|---|---|
| 4.2 stub nghe 127.0.0.1:53, UDP và TCP | Task 6 (`DefaultEndPoint`, UDP + TCP cùng cổng) | KH5 gọi `Start(DefaultEndPoint, …)` khi bật Định tuyến DNS |
| 4.2 chỉ trả lời `GameDomains`, domain khác `REFUSED` | Task 5 (+ chặn label có dấu chấm ở Task 1) | KH5 chọn nhóm domain đưa vào handler |
| 4.2 DoH RFC 8484, Cloudflare rồi dự phòng Google | Task 4 | KH5: chọn DoH trong mục Nâng cao (đổi thứ tự `DohServer`) |
| 4.2 cache theo TTL, tối đa 15 phút | Task 2–3 | — |
| 4.2 bind `ExclusiveAddressUse` lỗi thì báo, không bật | Task 6 (`DnsStubException.Error`) | KH5: thông báo tiếng Việt, không ghi Journal khi stub không bật được |
| 4.2 NRPT trỏ namespace về 127.0.0.1, không đổi DNS card mạng | — | KH4 (chỉ quy tắc của LunaDNS, theo spike) |
| 6. không `catch {}` rỗng, log | Global Constraints, `[LoggerMessage]` | KH5: sink log xoay vòng ở `%ProgramData%\LunaDNS\logs` |
| 6. watchdog khởi động lại stub ≤ 3 lần/phút | `DohStubServer.Completion` (Task 6) | KH5 |
| 9. DoH: wire format, TTL, REFUSED | Task 1–5 | — |
| Spike: CM của Steam không đọc PAC | `steamserver.net` có trong `GameDomains`, nên stub trả lời được cho CM | KH4 thêm namespace NRPT `.steamserver.net`; KH7 ghi FAQ |
| 4.1 Prober cần IP do DoH trả | `IDnsUpstream`/`DohClient` dùng lại được | KH3: thêm hàm đọc bản ghi A/AAAA từ response |

**Quyết định thiết kế mới trong KH2 (không có trong spec, cần PO biết):**
1. DoH nối thẳng tới IP bootstrap (1.1.1.1/1.0.0.1, 8.8.8.8/8.8.4.4), không hỏi DNS hệ thống để tìm `cloudflare-dns.com`. Chứng chỉ TLS vẫn được kiểm tra theo tên máy chủ.
2. `DohClient` đặt `UseProxy = false`.
3. Thêm package `Microsoft.Extensions.Logging.Abstractions` 8.0.2 vào Core (MIT, của Microsoft, thuần .NET).
4. Test cần Internet tắt mặc định, chỉ bật bằng `LUNADNS_NETWORK_TESTS=1`.
