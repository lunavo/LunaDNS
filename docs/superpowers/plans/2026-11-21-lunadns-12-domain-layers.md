# D4: Cập nhật danh sách tên miền theo lớp — Kế hoạch triển khai

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nhóm tên miền dựng sẵn nhận thêm tên miền từ file `domains.json` có chữ ký ECDSA trên GitHub Releases (lớp 2), không cần phát hành app. Lớp 1 (đóng kèm) luôn có. Lớp 3 (Tuỳ chỉnh) không bao giờ bị ghi đè. Chạy trên cả Windows và macOS.

**Architecture:** Core có `DomainListVerifier` (hàm thuần: byte → bản cập nhật hoặc mã lỗi), `EffectiveCatalog` (lớp 1 ∪ lớp 2, bất biến), `DomainListStore` (file trạng thái, xác minh lại khi nạp) và `DomainListService`. `DomainListService` là chỗ chạy chung: xác minh, gộp, reapply qua `FeatureSet.ReapplyAsync`, rồi mới ghi. Nó có cổng riêng, và `set-domains`/Lưu Tuỳ chỉnh tính `Routed` bên trong cổng đó. Windows: `LocalFeatureController` giữ service trong tiến trình app (Admin). macOS: daemon giữ service; app gửi nguyên byte qua `set-domain-list`, và daemon xác minh lại từ đầu. `DomainListUpdater` (App) hẹn giờ và tải. `tools/DomainListSigner` ký trên máy PO.

**Tech Stack:** .NET 8, C# 12, `System.Security.Cryptography.ECDsa` (P-256, SHA-256, IEEE P1363), System.Text.Json source generator, xUnit, `Microsoft.Extensions.TimeProvider.Testing` (FakeTimeProvider), Avalonia 11.3.23.

**Spec:** [2026-11-21-lunadns-domain-layers-design.md](../specs/2026-11-21-lunadns-domain-layers-design.md)

## Global Constraints

- Lớp 2 **chỉ thêm** tên miền vào nhóm dựng sẵn. Không xoá lớp 1, không tạo nhóm. PUBG không bao giờ nhận lớp 2: `HostsEditor` giữ `DomainCatalog.All`.
- Ký: ECDSA P-256 / SHA-256, chữ ký dạng `DSASignatureFormat.IeeeP1363FixedFieldConcatenation` (đúng **64 byte**). Khoá công khai là SubjectPublicKeyInfo base64, nằm trong `DomainListKeys.Production`. Khoá bí mật chỉ nằm trên máy PO. CI không giữ khoá.
- File phát hành tối đa **65 536 byte**. Mỗi nhóm tối đa **200** tên miền, toàn file tối đa **1000**. `format` = **1**. Chỉ nhận `serial > highestSerial`.
- Hẹn giờ: chạy lần đầu sau khi mở app **30 giây**; kiểm lại mỗi **24 giờ**; lỗi mạng thì thử lại sau **1 giờ**. HTTP timeout **15 giây**.
- URL: `https://github.com/{AppInfo.Repository}/releases/download/domain-list/domains.json`.
- Thứ tự luôn là **áp trước, ghi sau**: file trạng thái và settings chỉ được ghi sau khi `ReapplyAsync` thành công.
- File trạng thái `domain-list.json` nằm trong thư mục dữ liệu sẵn có (`AppPaths.DataDirectory` trên Windows, `MacPaths.Root` trên macOS). Không tạo thư mục mới, không đặt ACL riêng.
- macOS: lệnh `get-domain-list`, `set-domain-list`, `set-domain-list-enabled`. `DaemonProtocol.MaxLineBytes` = **131 072**. Daemon cũ trả "Lệnh lạ:" thì app ẩn dòng mới. Dây JSON của các lệnh cũ giữ nguyên.
- Build: `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`. CI build `-c Release -warnaserror`. Code mới không được sinh cảnh báo.
- Chú thích và chuỗi giao diện bằng tiếng Việt, theo giọng các file xung quanh. Commit và PR bằng tiếng Anh, **không** thêm `Co-Authored-By` hay dòng "Generated with Claude Code".
- `dotnet` có thể không nằm trong PATH: `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet`.
- Trên Mac, 52 test chỉ dành cho Windows (33 trong `LunaDns.Platform.Windows.Tests`, 19 trong `LunaDns.App.Tests`) hỏng sẵn từ trước, không phải lỗi mới.

## Nhánh và thứ tự

Làm ở worktree riêng, nhánh `feat/d4-domain-layers` tách từ `main`, **sau khi** PR chứa spec và kế hoạch này đã merge. Merge code qua PR để CI chạy cả Windows và macOS. Không merge thẳng vào `main`.

- Task 1–4 chỉ đụng Core, làm tuần tự.
- Task 5 (công cụ ký) cần Task 1.
- Task 6 (daemon) và Task 7 (controller) đều cần Task 4.
- Task 8 cần Task 7. Task 9 cần Task 7 và 8.
- Task 10 là tài liệu, việc của PO và PR, làm cuối.

## Điều chỉnh so với spec

Kế hoạch chốt các chi tiết spec còn để mở. Spec được sửa theo trong cùng commit với kế hoạch này:

1. **File trạng thái** lưu envelope dạng base64 của **nguyên byte file đã tải** (`"envelope": "<base64>"`), không lưu dạng object, để xác minh lại đúng byte đã nhận.
2. **`get-domain-list`** trả đủ danh sách tên miền lớp 2 theo nhóm (`Added: { "steam": [...] }`), không chỉ số đếm, vì hộp [Xem] trên macOS cần từng dòng. Tối đa 1000 dòng, vừa với `MaxLineBytes` mới.
3. **Không có lớp sink riêng.** `IFeatureController` có thêm `GetDomainListAsync`, `OfferDomainListAsync`, `SetDomainListEnabledAsync`. `DomainListUpdater` chỉ phụ thuộc interface hẹp `IDomainListSink`, mà `IFeatureController` kế thừa. Bỏ hai file `LocalDomainListSink`/`DaemonDomainListSink` của spec.
4. **Khoá JSON trùng** trong `groups` theo hành vi của System.Text.Json (bản sau ghi đè bản trước). File đã có chữ ký của PO, nên không kiểm riêng. Bỏ ý "không có nhóm trùng" ở bước 5 của spec.
5. **Kiểm `PeerCredentials`** đã nằm ở `DaemonServer.ServeAsync` cho mọi lệnh, nên ba lệnh mới tự có, không cần code thêm.
6. **`DomainListKeys.Production`** để rỗng cho tới Task 10, khi PO chạy `keygen`. Trước đó mọi file đều ra `UnknownKey`, và app chạy với lớp 1. Test dùng cặp khoá sinh trong test.

## Bản đồ file

```
src/LunaDns.Core/Domains/
  DomainListKeys.cs           (mới)  khoá công khai nhúng sẵn
  DomainListVerifier.cs       (mới)  Verify, hằng số giới hạn, tên nhóm trong file
  DomainListModels.cs         (mới)  DomainListOutcome, DomainListUpdate, DomainListVerification, DomainListState, DomainListOfferResult
  DomainListJson.cs           (mới)  envelope/payload/file trạng thái + DomainListJsonContext
  DomainListSigning.cs        (mới)  Sign/SignSource, kiểm file nguồn (dùng trong test và công cụ ký)
  EffectiveCatalog.cs         (mới)  lớp 1 ∪ lớp 2, CatalogEntry, CatalogSource
  DomainListStore.cs          (mới)  đọc/ghi domain-list.json
  DomainListService.cs        (mới)  chỗ chạy chung
src/LunaDns.Core/Features/FeatureDomains.cs   (sửa)  Routed(selection, catalog)
src/LunaDns.Core/Probing/ProbeTargets.cs      (sửa)  GroupOf(host, selection, catalog)
src/LunaDns.Core/Settings/AppSettings.cs      (sửa)  DomainListUpdates
src/LunaDns.Core/Diagnostics/DiagnosticInput.cs, DiagnosticReport.cs (sửa)  khối "Danh sách tên miền cập nhật"
tools/DomainListSigner/       (mới)  console: keygen, sign, verify, verify-source
data/domain-list.json         (mới)  nguồn lớp 2
src/LunaDns.Platform.MacOS/Daemon/DaemonProtocol.cs, DaemonCommands.cs (sửa)
src/LunaDns.MacCli/MacPaths.cs, Engine.cs, Program.cs (sửa)
src/LunaDns.App/AppPaths.cs, AppServices.cs, ReportWriter.cs (sửa)
src/LunaDns.App/Features/IFeatureController.cs, LocalFeatureController.cs (sửa); IDomainListSink.cs (mới)
src/LunaDns.App/MacOS/DaemonFeatureController.cs (sửa)
src/LunaDns.App/DomainList/DomainListUpdater.cs (mới)
src/LunaDns.App/ViewModels/DomainListRowViewModel.cs (mới); MainViewModel.cs, DomainGroupViewModel.cs, CustomDomainsViewModel.cs, IUserPrompts.cs (sửa)
src/LunaDns.App/Views/MainWindow.axaml, DomainListWindow.axaml(.cs), ConfirmDialog.cs (sửa)
src/LunaDns.App/Strings.resx, Strings.cs (sửa)
docs/dev/releasing.md, releasing.vi.md, docs/guide/09-macos.md, docs/guide/10-ten-mien.md (hoặc trang tên miền hiện có) (sửa)
.github/workflows/ci.yml (sửa)  bước verify-source
```

---

### Task 1: `DomainListVerifier`, các model và JSON

**Files:**
- Create: `src/LunaDns.Core/Domains/DomainListKeys.cs`, `DomainListModels.cs`, `DomainListJson.cs`, `DomainListVerifier.cs`, `DomainListSigning.cs`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainListVerifierTests.cs`, `tests/LunaDns.Core.Tests/Domains/TestDomainListKey.cs`

**Interfaces:**
- Produces:
  - `enum DomainListOutcome { Accepted, NotNewer, Downgrade, TooLarge, Malformed, UnknownKey, BadSignature, UnknownFormat, Disabled, ReapplyFailed, SaveFailed, Unavailable }`
  - `sealed record DomainListUpdate(long Serial, DateTimeOffset Issued, IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>> Groups, byte[] File)`
  - `sealed record DomainListVerification(DomainListOutcome Outcome, DomainListUpdate? Update = null, int Dropped = 0)`
  - `static class DomainListVerifier { const int MaxBytes = 65536, MaxPerGroup = 200, MaxTotal = 1000, SupportedFormat = 1, SignatureBytes = 64; static DomainListVerification Verify(ReadOnlySpan<byte> file, IReadOnlyDictionary<string,string> keys, long highestSerial, bool requireNewer = true); static string GroupKey(DomainGroup); static bool TryParseGroup(string key, out DomainGroup group) }`
  - `static class DomainListKeys { static IReadOnlyDictionary<string,string> Production }`
  - `static class DomainListSigning { static byte[] Sign(DomainListSource source, long serial, DateTimeOffset issued, ECDsa key, string keyId); static string PublicKey(ECDsa key) }`
  - `sealed record DomainListSource(Dictionary<string, List<string>> Groups)`: dạng của `data/domain-list.json`

- [ ] **Step 1: Khoá test và test lỗi**

`tests/LunaDns.Core.Tests/Domains/TestDomainListKey.cs`:
```csharp
using System.Security.Cryptography;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

/// <summary>Cặp khoá P-256 sinh một lần cho cả lượt test; không liên quan tới khoá thật của PO.</summary>
internal static class TestDomainListKey
{
    public const string KeyId = "test";

    public static ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static IReadOnlyDictionary<string, string> Keys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal) { [KeyId] = DomainListSigning.PublicKey(Key) };

    public static byte[] Signed(long serial, Dictionary<string, List<string>> groups, DateTimeOffset? issued = null) =>
        DomainListSigning.Sign(new DomainListSource(groups), serial, issued ?? new DateTimeOffset(2026, 11, 21, 14, 2, 0, TimeSpan.Zero), Key, KeyId);

    /// <summary>Envelope tự dựng, để test các trường hỏng mà Sign không bao giờ sinh ra.</summary>
    public static byte[] Envelope(string payloadJson, string keyId = KeyId, byte[]? signature = null)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(payloadJson);
        var sig = signature ?? Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return System.Text.Encoding.UTF8.GetBytes(
            $$"""{"keyId":"{{keyId}}","payload":"{{Convert.ToBase64String(payload)}}","sig":"{{Convert.ToBase64String(sig)}}"}""");
    }
}
```

`tests/LunaDns.Core.Tests/Domains/DomainListVerifierTests.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using LunaDns.Core.Domains;
using static LunaDns.Core.Tests.Domains.TestDomainListKey;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainListVerifierTests
{
    private static DomainListVerification Verify(byte[] file, long highest = 0) =>
        DomainListVerifier.Verify(file, Keys, highest);

    [Fact]
    public void Valid_file_is_accepted_with_groups()
    {
        var file = Signed(202611211402, new() { ["steam"] = ["steamcdn-a.akamaihd.net"], ["reddit"] = ["redditblog.com"] });

        var result = Verify(file);

        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Equal(202611211402, result.Update!.Serial);
        Assert.Equal(["steamcdn-a.akamaihd.net"], result.Update.Groups[DomainGroup.Steam]);
        Assert.Equal(["redditblog.com"], result.Update.Groups[DomainGroup.Reddit]);
        Assert.Equal(file, result.Update.File);
        Assert.Equal(0, result.Dropped);
    }

    [Fact]
    public void Too_large_file_is_rejected_before_parsing() =>
        Assert.Equal(DomainListOutcome.TooLarge, Verify(new byte[DomainListVerifier.MaxBytes + 1]).Outcome);

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"keyId":"test","payload":"e30="}""")]
    [InlineData("""{"keyId":"test","payload":"%%%","sig":"AAAA"}""")]
    public void Broken_envelope_is_malformed(string text) =>
        Assert.Equal(DomainListOutcome.Malformed, Verify(Encoding.UTF8.GetBytes(text)).Outcome);

    [Fact]
    public void Unknown_key_id_is_rejected() =>
        Assert.Equal(DomainListOutcome.UnknownKey, Verify(Envelope("""{"format":1}""", keyId: "other")).Outcome);

    [Fact]
    public void Der_signature_is_malformed()
    {
        var payload = Encoding.UTF8.GetBytes("""{"format":1}""");
        var der = Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.Equal(DomainListOutcome.Malformed, Verify(Envelope("""{"format":1}""", signature: der)).Outcome);
    }

    [Fact]
    public void Changed_payload_fails_signature()
    {
        var payload = Encoding.UTF8.GetBytes("""{"format":1,"serial":1}""");
        var sig = Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.Equal(DomainListOutcome.BadSignature, Verify(Envelope("""{"format":1,"serial":2}""", signature: sig)).Outcome);
    }

    [Fact]
    public void Signature_from_other_key_fails()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Encoding.UTF8.GetBytes("""{"format":1}""");
        var sig = other.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.Equal(DomainListOutcome.BadSignature, Verify(Envelope("""{"format":1}""", signature: sig)).Outcome);
    }

    [Fact]
    public void Newer_format_is_unknown() =>
        Assert.Equal(DomainListOutcome.UnknownFormat,
            Verify(Envelope("""{"format":2,"serial":5,"issued":"2026-11-21T00:00:00Z","groups":{}}""")).Outcome);

    [Theory]
    [InlineData("""{"format":1,"issued":"2026-11-21T00:00:00Z","groups":{}}""")]
    [InlineData("""{"format":1,"serial":5,"groups":{}}""")]
    [InlineData("""{"format":1,"serial":5,"issued":"2026-11-21T00:00:00Z"}""")]
    [InlineData("""{"format":1,"serial":5,"issued":"2026-11-21T00:00:00Z","groups":{"steam":null}}""")]
    [InlineData("""{"format":1,"serial":"5","issued":"2026-11-21T00:00:00Z","groups":{}}""")]
    public void Missing_or_wrong_fields_are_malformed(string payload) =>
        Assert.Equal(DomainListOutcome.Malformed, Verify(Envelope(payload)).Outcome);

    [Fact]
    public void Same_serial_is_not_newer_and_lower_is_downgrade()
    {
        var file = Signed(10, new() { ["steam"] = [] });
        Assert.Equal(DomainListOutcome.NotNewer, Verify(file, highest: 10).Outcome);
        Assert.Equal(DomainListOutcome.Downgrade, Verify(file, highest: 11).Outcome);
    }

    [Fact]
    public void Serial_check_can_be_skipped_when_reloading_store() =>
        Assert.Equal(DomainListOutcome.Accepted, DomainListVerifier.Verify(Signed(10, new()), Keys, 10, requireNewer: false).Outcome);

    [Fact]
    public void Group_over_limit_is_malformed()
    {
        var many = Enumerable.Range(0, DomainListVerifier.MaxPerGroup + 1).Select(i => $"cdn{i}.example.net").ToList();
        Assert.Equal(DomainListOutcome.Malformed, Verify(Signed(1, new() { ["steam"] = many })).Outcome);
    }

    [Fact]
    public void Total_over_limit_is_malformed()
    {
        List<string> Block(string g) => [.. Enumerable.Range(0, 200).Select(i => $"{g}{i}.example.net")];
        var groups = new Dictionary<string, List<string>>
        {
            ["steam"] = Block("a"), ["reddit"] = Block("b"), ["othergames"] = Block("c"), ["x1"] = Block("d"), ["x2"] = Block("e"),
            ["x3"] = ["f.example.net"],
        };
        Assert.Equal(DomainListOutcome.Malformed, Verify(Signed(1, groups)).Outcome);
    }

    [Fact]
    public void Bad_entries_are_dropped_not_the_file()
    {
        var file = Signed(1, new()
        {
            ["steam"] =
            [
                "good.example.net",
                "com",                    // hậu tố công cộng
                "easyanticheat.net",      // danh sách cấm
                "Upper.Example.net",      // chưa chuẩn hoá
                "steampowered.com",       // trùng lớp 1
                "store.steampowered.com", // nằm dưới lớp 1
                "good.example.net",       // trùng trong file
            ],
        });

        var result = Verify(file);

        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Equal(["good.example.net"], result.Update!.Groups[DomainGroup.Steam]);
        Assert.Equal(6, result.Dropped);
    }

    [Fact]
    public void Pubg_custom_and_unknown_groups_are_ignored()
    {
        var file = Signed(1, new() { ["pubg"] = ["x.example.net"], ["custom"] = ["y.example.net"], ["epic"] = ["z.example.net"], ["othergames"] = ["w.example.net"] });

        var result = Verify(file);

        Assert.Equal([DomainGroup.OtherGames], result.Update!.Groups.Keys);
    }
}
```

- [ ] **Step 2: Chạy, thấy lỗi biên dịch**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainListVerifierTests`
Expected: FAIL (biên dịch): `DomainListVerifier`, `DomainListSigning` chưa có.

- [ ] **Step 3: Viết model, JSON, khoá, verifier, signing**

`src/LunaDns.Core/Domains/DomainListModels.cs`:
```csharp
using LunaDns.Core.Features;

namespace LunaDns.Core.Domains;

/// <summary>Kết quả xác minh hoặc áp một file lớp 2 (spec D4). Tên dạng chuỗi đi qua socket daemon.</summary>
public enum DomainListOutcome
{
    Accepted,
    /// <summary>Cùng serial với bản đang giữ: lượt kiểm bình thường.</summary>
    NotNewer,
    Downgrade,
    TooLarge,
    Malformed,
    UnknownKey,
    BadSignature,
    UnknownFormat,
    /// <summary>Công tắc "Tự cập nhật danh sách" đang tắt.</summary>
    Disabled,
    ReapplyFailed,
    SaveFailed,
    /// <summary>Phía app: dịch vụ nền không trả lời hoặc là bản cũ.</summary>
    Unavailable,
}

/// <summary>Bản lớp 2 đã qua xác minh. <see cref="File"/> là nguyên byte đã tải, để lưu và xác minh lại khi nạp.</summary>
public sealed record DomainListUpdate(
    long Serial,
    DateTimeOffset Issued,
    IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>> Groups,
    byte[] File);

public sealed record DomainListVerification(DomainListOutcome Outcome, DomainListUpdate? Update = null, int Dropped = 0);

/// <summary>Trạng thái hiển thị: công tắc, bản đang dùng, lần kiểm thành công gần nhất, tên miền lớp 2 đang có hiệu lực.</summary>
public sealed record DomainListState(
    bool Enabled,
    long Serial,
    DateTimeOffset? Issued,
    DateTimeOffset? CheckedAt,
    IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>> Added)
{
    public static DomainListState Empty(bool enabled) => new(enabled, 0, null, null, new Dictionary<DomainGroup, IReadOnlyList<string>>());
}

/// <summary>Kết quả OfferAsync/SetEnabledAsync. Added: số tên miền lớp 2 mới có hiệu lực so với trước.</summary>
public sealed record DomainListOfferResult(
    DomainListOutcome Outcome,
    int Added = 0,
    int Dropped = 0,
    FeatureError Error = FeatureError.None,
    bool PreviousRestored = true)
{
    public bool Success => Outcome is DomainListOutcome.Accepted or DomainListOutcome.NotNewer;
}
```

`src/LunaDns.Core/Domains/DomainListJson.cs`:
```csharp
using System.Text.Json.Serialization;

namespace LunaDns.Core.Domains;

/// <summary>Vỏ của domains.json: payload là base64 của JSON UTF-8, sig ký trên đúng các byte đó.</summary>
internal sealed record DomainListEnvelope(string? KeyId, string? Payload, string? Sig);

internal sealed record DomainListPayload(int Format, long? Serial, DateTimeOffset? Issued, Dictionary<string, List<string>?>? Groups);

/// <summary>Nguồn lớp 2 trong repo (data/domain-list.json): chỉ có groups, serial/issued do lệnh sign đặt.</summary>
public sealed record DomainListSource(Dictionary<string, List<string>> Groups);

/// <summary>domain-list.json trong thư mục dữ liệu. Envelope là base64 của nguyên file đã tải.</summary>
internal sealed record DomainListFile(string? Envelope, long HighestSerial, DateTimeOffset? CheckedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(DomainListEnvelope))]
[JsonSerializable(typeof(DomainListPayload))]
[JsonSerializable(typeof(DomainListSource))]
[JsonSerializable(typeof(DomainListFile))]
internal sealed partial class DomainListJsonContext : JsonSerializerContext
{
}
```

`src/LunaDns.Core/Domains/DomainListKeys.cs`:
```csharp
namespace LunaDns.Core.Domains;

/// <summary>
/// Khoá công khai ký danh sách lớp 2 (spec D4): keyId → SubjectPublicKeyInfo DER base64. Khoá bí mật chỉ ở máy PO.
/// Đổi khoá: thêm khoá mới ở một bản app, ký bằng khoá mới từ bản sau. Rỗng thì mọi file ra UnknownKey (chạy với lớp 1).
/// </summary>
public static class DomainListKeys
{
    public static IReadOnlyDictionary<string, string> Production { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
```

`src/LunaDns.Core/Domains/DomainListVerifier.cs`:
```csharp
using System.Security.Cryptography;
using System.Text.Json;

namespace LunaDns.Core.Domains;

/// <summary>
/// Xác minh domains.json (spec D4, "Định dạng và thứ tự kiểm"). Chữ ký kiểm trên byte trước khi parse payload.
/// Tên miền không qua DomainPolicy chỉ bị bỏ dòng, để app mới (danh sách cấm dài hơn) vẫn đọc được file cũ.
/// Nhóm lạ, pubg, custom bị bỏ qua; othergames được giữ (EffectiveCatalog chỉ áp nhóm Selectable).
/// </summary>
public static class DomainListVerifier
{
    public const int MaxBytes = 65536;
    public const int MaxPerGroup = 200;
    public const int MaxTotal = 1000;
    public const int SupportedFormat = 1;
    public const int SignatureBytes = 64;

    private static readonly Dictionary<string, DomainGroup> Groups = new(StringComparer.Ordinal)
    {
        ["steam"] = DomainGroup.Steam,
        ["othergames"] = DomainGroup.OtherGames,
        ["reddit"] = DomainGroup.Reddit,
    };

    public static string GroupKey(DomainGroup group) =>
        Groups.First(kv => kv.Value == group).Key;

    public static bool TryParseGroup(string key, out DomainGroup group) => Groups.TryGetValue(key, out group);

    public static DomainListVerification Verify(ReadOnlySpan<byte> file, IReadOnlyDictionary<string, string> keys, long highestSerial, bool requireNewer = true)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (file.Length > MaxBytes) return new(DomainListOutcome.TooLarge);

        DomainListEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(file, DomainListJsonContext.Default.DomainListEnvelope);
        }
        catch (JsonException)
        {
            return new(DomainListOutcome.Malformed);
        }
        if (envelope?.KeyId is null || envelope.Payload is null || envelope.Sig is null) return new(DomainListOutcome.Malformed);
        if (!keys.TryGetValue(envelope.KeyId, out var publicKey)) return new(DomainListOutcome.UnknownKey);

        byte[] payload, signature;
        try
        {
            payload = Convert.FromBase64String(envelope.Payload);
            signature = Convert.FromBase64String(envelope.Sig);
        }
        catch (FormatException)
        {
            return new(DomainListOutcome.Malformed);
        }
        if (signature.Length != SignatureBytes) return new(DomainListOutcome.Malformed);
        if (!SignatureValid(publicKey, payload, signature)) return new(DomainListOutcome.BadSignature);

        DomainListPayload? body;
        try
        {
            body = JsonSerializer.Deserialize(payload, DomainListJsonContext.Default.DomainListPayload);
        }
        catch (JsonException)
        {
            return new(DomainListOutcome.Malformed);
        }
        if (body is null) return new(DomainListOutcome.Malformed);
        if (body.Format != SupportedFormat) return new(DomainListOutcome.UnknownFormat);
        if (body.Serial is not > 0 || body.Issued is null || body.Groups is null || body.Groups.Values.Any(g => g is null))
            return new(DomainListOutcome.Malformed);
        if (requireNewer && body.Serial == highestSerial) return new(DomainListOutcome.NotNewer);
        if (requireNewer && body.Serial < highestSerial) return new(DomainListOutcome.Downgrade);
        if (body.Groups.Values.Any(g => g!.Count > MaxPerGroup) || body.Groups.Values.Sum(g => g!.Count) > MaxTotal)
            return new(DomainListOutcome.Malformed);

        var groups = new Dictionary<DomainGroup, IReadOnlyList<string>>();
        var dropped = 0;
        foreach (var (key, list) in body.Groups)
        {
            if (!Groups.TryGetValue(key, out var group)) continue;
            var builtin = DomainCatalog.For(group);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<string>();
            foreach (var domain in list!)
            {
                var ok = domain is not null
                    && DomainPolicy.Normalize(domain) == domain
                    && DomainPolicy.Validate(domain) == DomainRejection.None
                    && !DomainMatcher.Matches(domain, builtin)
                    && seen.Add(domain);
                if (ok) kept.Add(domain!);
                else dropped++;
            }
            groups[group] = kept;
        }
        return new(DomainListOutcome.Accepted, new DomainListUpdate(body.Serial.Value, body.Issued.Value, groups, file.ToArray()), dropped);
    }

    private static bool SignatureValid(string publicKey, byte[] payload, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
```

`src/LunaDns.Core/Domains/DomainListSigning.cs`:
```csharp
using System.Security.Cryptography;
using System.Text.Json;

namespace LunaDns.Core.Domains;

/// <summary>Ký domains.json (công cụ DomainListSigner và test). Không chạm khoá bí mật nào ngoài khoá được truyền vào.</summary>
public static class DomainListSigning
{
    public static string PublicKey(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public static byte[] Sign(DomainListSource source, long serial, DateTimeOffset issued, ECDsa key, string keyId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new DomainListPayload(DomainListVerifier.SupportedFormat, serial, issued, source.Groups.ToDictionary(kv => kv.Key, kv => (List<string>?)kv.Value, StringComparer.Ordinal)),
            DomainListJsonContext.Default.DomainListPayload);
        var signature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.SerializeToUtf8Bytes(
            new DomainListEnvelope(keyId, Convert.ToBase64String(payload), Convert.ToBase64String(signature)),
            DomainListJsonContext.Default.DomainListEnvelope);
    }
}
```

Nếu analyzer báo CA1819 (thuộc tính trả mảng) cho `DomainListUpdate.File`, đổi kiểu sang `ReadOnlyMemory<byte>` và sửa các chỗ dùng (`.ToArray()` khi ghi base64). Nếu báo CA2227 cho collection trong record JSON, thêm `#pragma warning disable` có lý do, theo cách `DomainSelection.cs` đang làm.

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainListVerifierTests`
Expected: PASS. `Bad_entries_are_dropped_not_the_file` có `Dropped == 6`: `com`, `easyanticheat.net`, `Upper…`, `steampowered.com`, `store.steampowered.com`, và một bản trùng. Nếu `DomainPolicy.Validate("easyanticheat.net")` không trả `Denied`, kiểm lại `DomainDenyList` trước khi sửa test.

- [ ] **Step 5: Build Release và commit**

Run: `dotnet build -c Release -warnaserror`
Expected: 0 warning.
```bash
git add src/LunaDns.Core/Domains/DomainList*.cs tests/LunaDns.Core.Tests/Domains/DomainListVerifierTests.cs tests/LunaDns.Core.Tests/Domains/TestDomainListKey.cs
git commit -m "feat(core): verify signed layer-2 domain list"
```

---

### Task 2: `EffectiveCatalog`, `Routed`/`GroupOf` theo catalog, `AppSettings.DomainListUpdates`

**Files:**
- Create: `src/LunaDns.Core/Domains/EffectiveCatalog.cs`
- Modify: `src/LunaDns.Core/Features/FeatureDomains.cs`, `src/LunaDns.Core/Probing/ProbeTargets.cs:36-43`, `src/LunaDns.Core/Settings/AppSettings.cs:34-37`
- Test: `tests/LunaDns.Core.Tests/Domains/EffectiveCatalogTests.cs`; thêm vào `tests/LunaDns.Core.Tests/Features/FeatureDomainsTests.cs`, `tests/LunaDns.Core.Tests/Settings/JsonFileSettingsStoreTests.cs`

**Interfaces:**
- Consumes: `DomainListUpdate`, `DomainCatalog`, `DomainPolicy`, `DomainMatcher`.
- Produces:
  - `enum CatalogSource { Builtin, Update }`, `sealed record CatalogEntry(string Domain, CatalogSource Source)`
  - `sealed class EffectiveCatalog { static EffectiveCatalog Builtin; static EffectiveCatalog From(IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>>? added, DateTimeOffset? issued); DateTimeOffset? Issued; IReadOnlyList<string> For(DomainGroup); IReadOnlyList<string> Added(DomainGroup); IReadOnlyList<CatalogEntry> Entries(DomainGroup); IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>> AllAdded }`
  - `FeatureDomains.Routed(DomainSelection selection, EffectiveCatalog catalog)`; overload cũ `Routed(selection)` = `Routed(selection, EffectiveCatalog.Builtin)`.
  - `ProbeTargets.GroupOf(string host, DomainSelection selection, EffectiveCatalog catalog)`; overload cũ giữ nguyên, gọi với `Builtin`.
  - `AppSettings.DomainListUpdates : bool = true`.

- [ ] **Step 1: Test lỗi**

`tests/LunaDns.Core.Tests/Domains/EffectiveCatalogTests.cs`:
```csharp
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

public sealed class EffectiveCatalogTests
{
    private static readonly DateTimeOffset Issued = new(2026, 11, 21, 14, 2, 0, TimeSpan.Zero);

    private static EffectiveCatalog With(DomainGroup group, params string[] added) =>
        EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>> { [group] = added }, Issued);

    [Fact]
    public void Builtin_is_layer_one_only()
    {
        Assert.Equal(DomainCatalog.For(DomainGroup.Steam), EffectiveCatalog.Builtin.For(DomainGroup.Steam));
        Assert.Empty(EffectiveCatalog.Builtin.Added(DomainGroup.Steam));
        Assert.Null(EffectiveCatalog.Builtin.Issued);
    }

    [Fact]
    public void Layer_two_comes_after_layer_one_with_source()
    {
        var catalog = With(DomainGroup.Steam, "cdn.example.net");

        Assert.Equal([.. DomainCatalog.For(DomainGroup.Steam), "cdn.example.net"], catalog.For(DomainGroup.Steam));
        Assert.Equal(new CatalogEntry("cdn.example.net", CatalogSource.Update), catalog.Entries(DomainGroup.Steam)[^1]);
        Assert.Equal(CatalogSource.Builtin, catalog.Entries(DomainGroup.Steam)[0].Source);
        Assert.Equal(Issued, catalog.Issued);
    }

    [Fact]
    public void Group_not_selectable_gets_nothing() =>
        Assert.Equal(DomainCatalog.For(DomainGroup.OtherGames), With(DomainGroup.OtherGames, "x.example.net").For(DomainGroup.OtherGames));

    [Fact]
    public void Pubg_never_gets_layer_two() =>
        Assert.Equal(DomainCatalog.For(DomainGroup.Pubg), With(DomainGroup.Pubg, "x.example.net").For(DomainGroup.Pubg));

    [Fact]
    public void Invalid_duplicate_or_builtin_entries_are_filtered_again()
    {
        var catalog = With(DomainGroup.Steam, "cdn.example.net", "cdn.example.net", "steampowered.com", "com", "easyanticheat.net");
        Assert.Equal(["cdn.example.net"], catalog.Added(DomainGroup.Steam));
    }
}
```

Thêm vào `FeatureDomainsTests.cs`:
```csharp
    [Fact]
    public void Routed_includes_layer_two_of_enabled_groups_only()
    {
        var catalog = EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>>
        {
            [DomainGroup.Steam] = ["cdn.example.net"],
            [DomainGroup.Reddit] = ["redditblog.com"],
        }, DateTimeOffset.UnixEpoch);

        var routed = FeatureDomains.Routed(new DomainSelection(), catalog);

        Assert.Contains("cdn.example.net", routed);
        Assert.DoesNotContain("redditblog.com", routed);
    }

    [Fact]
    public void Custom_domain_also_in_layer_two_is_routed_once()
    {
        var catalog = EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>> { [DomainGroup.Steam] = ["cdn.example.net"] }, DateTimeOffset.UnixEpoch);
        var selection = new DomainSelection { Custom = [new CustomDomain("cdn.example.net")] };

        Assert.Single(FeatureDomains.Routed(selection, catalog), d => d == "cdn.example.net");
    }
```

Thêm vào `JsonFileSettingsStoreTests.cs`:
```csharp
    [Fact]
    public void Old_settings_without_domain_list_switch_load_as_enabled()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "SplitMode": "FirstByte" }""");
        Assert.True(new JsonFileSettingsStore(path).Load().DomainListUpdates);
    }

    [Fact]
    public void Domain_list_switch_round_trips()
    {
        var store = new JsonFileSettingsStore(PathOf("settings.json"));
        store.Save(new AppSettings { DomainListUpdates = false });
        Assert.False(store.Load().DomainListUpdates);
    }
```

- [ ] **Step 2: Chạy, thấy lỗi biên dịch**

Run: `dotnet test tests/LunaDns.Core.Tests --filter "EffectiveCatalogTests|FeatureDomainsTests|JsonFileSettingsStoreTests"`
Expected: FAIL: `EffectiveCatalog`, `DomainListUpdates` chưa có.

- [ ] **Step 3: Viết code**

`src/LunaDns.Core/Domains/EffectiveCatalog.cs`:
```csharp
namespace LunaDns.Core.Domains;

public enum CatalogSource
{
    Builtin,
    Update,
}

public sealed record CatalogEntry(string Domain, CatalogSource Source);

/// <summary>
/// Nhóm dựng sẵn sau khi gộp lớp 1 (DomainCatalog) với lớp 2 (spec D4 "Gộp ba lớp"). Bất biến.
/// Chỉ nhóm trong DomainCatalog.Selectable nhận lớp 2; PUBG không bao giờ. Lọc lại mọi dòng (DomainPolicy, trùng lớp 1)
/// vì app macOS dựng catalog từ dữ liệu daemon gửi sang.
/// </summary>
public sealed class EffectiveCatalog
{
    private readonly Dictionary<DomainGroup, IReadOnlyList<string>> _added;

    private EffectiveCatalog(Dictionary<DomainGroup, IReadOnlyList<string>> added, DateTimeOffset? issued)
    {
        _added = added;
        Issued = issued;
    }

    public static EffectiveCatalog Builtin { get; } = new([], null);

    public DateTimeOffset? Issued { get; }

    public IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>> AllAdded => _added;

    public static EffectiveCatalog From(IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>>? added, DateTimeOffset? issued)
    {
        if (added is null || added.Count == 0) return issued is null ? Builtin : new([], issued);
        var kept = new Dictionary<DomainGroup, IReadOnlyList<string>>();
        foreach (var group in DomainCatalog.Selectable)
        {
            if (!added.TryGetValue(group, out var list)) continue;
            var builtin = DomainCatalog.For(group);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var clean = list.Where(d => DomainPolicy.Normalize(d) == d
                    && DomainPolicy.Validate(d) == DomainRejection.None
                    && !DomainMatcher.Matches(d, builtin)
                    && seen.Add(d))
                .ToList();
            if (clean.Count > 0) kept[group] = clean;
        }
        return new(kept, issued);
    }

    public IReadOnlyList<string> Added(DomainGroup group) => _added.TryGetValue(group, out var list) ? list : [];

    public IReadOnlyList<string> For(DomainGroup group) => [.. DomainCatalog.For(group), .. Added(group)];

    public IReadOnlyList<CatalogEntry> Entries(DomainGroup group) =>
    [
        .. DomainCatalog.For(group).Select(d => new CatalogEntry(d, CatalogSource.Builtin)),
        .. Added(group).Select(d => new CatalogEntry(d, CatalogSource.Update)),
    ];
}
```

`FeatureDomains.cs`: đổi thân `Routed` thành overload hai tham số, và giữ overload cũ:
```csharp
    /// <summary>Tên miền mà Định tuyến DNS và Proxy SNI áp dụng, chỉ lớp 1 (spec KH8 3.1).</summary>
    public static IReadOnlyList<string> Routed(DomainSelection selection) => Routed(selection, EffectiveCatalog.Builtin);

    /// <summary>Như trên, nhóm dựng sẵn lấy từ <paramref name="catalog"/> (lớp 1 ∪ lớp 2, spec D4).</summary>
    public static IReadOnlyList<string> Routed(DomainSelection selection, EffectiveCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(catalog);
        var groups = selection.EnabledGroups.Where(DomainCatalog.Selectable.Contains).Distinct();
        var custom = selection.IsEnabled(DomainGroup.Custom)
            ? selection.Custom.Where(c => c.Enabled).Select(c => c.Domain)
            : [];
        var all = groups.SelectMany(catalog.For).Concat(custom)
            .Select(DomainMatcher.Normalize)
            .Where(DomainPolicy.IsRoutable)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return [.. all.Where(d => !all.Any(p => p != d && DomainMatcher.Matches(d, [p])))];
    }
```

`ProbeTargets.cs`: thay `GroupOf` hiện có bằng:
```csharp
    public static DomainGroup GroupOf(string host, DomainSelection selection) => GroupOf(host, selection, EffectiveCatalog.Builtin);

    public static DomainGroup GroupOf(string host, DomainSelection selection, EffectiveCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var group in DomainCatalog.Selectable)
            if (DomainMatcher.Matches(host, catalog.For(group))) return group;
        if (DomainMatcher.Matches(host, DomainCatalog.For(DomainGroup.Pubg))) return DomainGroup.Pubg;
        return DomainGroup.Custom;
    }
```

`AppSettings.cs`: thêm sau `DomainsIntroSeen`:
```csharp
    /// <summary>D4: công tắc "Tự cập nhật danh sách tên miền". Tắt = ngừng tải và bỏ lớp 2 khỏi định tuyến.</summary>
    public bool DomainListUpdates { get; init; } = true;
```
Source generator gán giá trị mặc định của bool cho thuộc tính vắng trong JSON? **Không**: với `init` có giá trị khởi tạo, System.Text.Json giữ `true` khi JSON thiếu trường. Test `Old_settings_without_domain_list_switch_load_as_enabled` chốt điều này. Nếu test đỏ, bù trong `JsonFileSettingsStore.Load` giống cách bù `CustomDoh`.

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Core.Tests`
Expected: PASS, kể cả các test cũ của `FeatureDomains` và `ProbeTargets`.

- [ ] **Step 5: Commit**
```bash
git add src/LunaDns.Core tests/LunaDns.Core.Tests
git commit -m "feat(core): merge layer-2 domains into an effective catalog"
```

---

### Task 3: `DomainListStore`

**Files:**
- Create: `src/LunaDns.Core/Domains/DomainListStore.cs`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainListStoreTests.cs`

**Interfaces:**
- Consumes: `DomainListVerifier.Verify(..., requireNewer: false)`, `DomainListFile`, `DomainListJsonContext`.
- Produces:
  - `sealed record DomainListStoreData(DomainListUpdate? Current, long HighestSerial, DateTimeOffset? CheckedAt) { static DomainListStoreData Empty }`
  - `sealed class DomainListStore(string path, IReadOnlyDictionary<string,string> keys) { DomainListStoreData Load(); void Save(DomainListStoreData data); }`. `Save` ném `IOException`/`UnauthorizedAccessException`.

- [ ] **Step 1: Test lỗi**

```csharp
using LunaDns.Core.Domains;
using static LunaDns.Core.Tests.Domains.TestDomainListKey;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainListStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-domainlist-").FullName;
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 7, 0, 0, TimeSpan.Zero);

    private string FilePath => Path.Combine(_dir, "domain-list.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static DomainListUpdate Update(long serial) =>
        DomainListVerifier.Verify(Signed(serial, new() { ["steam"] = ["cdn.example.net"] }), Keys, 0).Update!;

    [Fact]
    public void Missing_file_is_empty() =>
        Assert.Equal(DomainListStoreData.Empty, new DomainListStore(FilePath, Keys).Load());

    [Fact]
    public void Save_then_load_round_trips_and_reverifies()
    {
        var store = new DomainListStore(FilePath, Keys);
        store.Save(new DomainListStoreData(Update(7), 7, Now));

        var loaded = store.Load();

        Assert.Equal(7, loaded.Current!.Serial);
        Assert.Equal(["cdn.example.net"], loaded.Current.Groups[DomainGroup.Steam]);
        Assert.Equal(7, loaded.HighestSerial);
        Assert.Equal(Now, loaded.CheckedAt);
    }

    [Fact]
    public void Envelope_with_unknown_key_loads_without_current_but_keeps_highest_serial()
    {
        new DomainListStore(FilePath, Keys).Save(new DomainListStoreData(Update(7), 9, Now));

        var loaded = new DomainListStore(FilePath, new Dictionary<string, string>()).Load();

        Assert.Null(loaded.Current);
        Assert.Equal(9, loaded.HighestSerial);
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_empty()
    {
        File.WriteAllText(FilePath, "{ not json");

        Assert.Equal(DomainListStoreData.Empty, new DomainListStore(FilePath, Keys).Load());
        Assert.Single(Directory.GetFiles(_dir, "domain-list.json.corrupt-*"));
    }

    [Fact]
    public void Save_without_current_keeps_serial_and_check_time()
    {
        var store = new DomainListStore(FilePath, Keys);
        store.Save(new DomainListStoreData(null, 3, Now));
        Assert.Equal(new DomainListStoreData(null, 3, Now), store.Load());
    }
}
```

- [ ] **Step 2: Chạy, thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainListStoreTests`
Expected: FAIL (biên dịch).

- [ ] **Step 3: Viết store**

```csharp
using System.Globalization;
using System.Text.Json;

namespace LunaDns.Core.Domains;

public sealed record DomainListStoreData(DomainListUpdate? Current, long HighestSerial, DateTimeOffset? CheckedAt)
{
    public static DomainListStoreData Empty { get; } = new(null, 0, null);
}

/// <summary>
/// domain-list.json trong thư mục dữ liệu đã có ACL (spec D4 "Lưu trữ và quyền"). Lúc nạp luôn xác minh lại chữ ký;
/// sai thì chạy với lớp 1 nhưng vẫn giữ HighestSerial (chống hạ cấp). Ghi: file tạm rồi move, như JsonFileSettingsStore.
/// </summary>
public sealed class DomainListStore(string path, IReadOnlyDictionary<string, string> keys)
{
    public DomainListStoreData Load()
    {
        if (!File.Exists(path)) return DomainListStoreData.Empty;
        DomainListFile? file;
        try
        {
            using var stream = File.OpenRead(path);
            file = JsonSerializer.Deserialize(stream, DomainListJsonContext.Default.DomainListFile);
        }
        catch (JsonException)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return DomainListStoreData.Empty;
        }
        if (file is null) return DomainListStoreData.Empty;

        DomainListUpdate? current = null;
        if (file.Envelope is { } envelope)
        {
            try
            {
                var verified = DomainListVerifier.Verify(Convert.FromBase64String(envelope), keys, file.HighestSerial, requireNewer: false);
                if (verified.Outcome == DomainListOutcome.Accepted) current = verified.Update;
            }
            catch (FormatException)
            {
                // Base64 hỏng: coi như không có bản lớp 2.
            }
        }
        return new DomainListStoreData(current, file.HighestSerial, file.CheckedAt);
    }

    public void Save(DomainListStoreData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var file = new DomainListFile(data.Current is { } c ? Convert.ToBase64String(c.File) : null, data.HighestSerial, data.CheckedAt);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(file, DomainListJsonContext.Default.DomainListFile));
        File.Move(temp, path, overwrite: true);
    }
}
```
`DomainListStoreData` có `DomainListUpdate` chứa `byte[]` và dictionary, nên phép so sánh record mặc định chỉ đúng khi `Current` là null. Các test so sánh bằng `Assert.Equal` trên cả record đều có `Current = null`, nên vẫn dùng được.

- [ ] **Step 4: Chạy test**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainListStoreTests`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add src/LunaDns.Core/Domains/DomainListStore.cs tests/LunaDns.Core.Tests/Domains/DomainListStoreTests.cs
git commit -m "feat(core): persist the verified domain list and reverify on load"
```

---

### Task 4: `DomainListService`

**Files:**
- Create: `src/LunaDns.Core/Domains/DomainListService.cs`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainListServiceTests.cs`

**Interfaces:**
- Consumes: `DomainListStore`, `DomainListVerifier`, `EffectiveCatalog`, `FeatureDomains.Routed(selection, catalog)`, `ReapplyResult`.
- Produces:
```csharp
public sealed partial class DomainListService
{
    public DomainListService(DomainListStore store, IReadOnlyDictionary<string, string> keys, bool enabled,
        Func<DomainSelection> selection, Func<IReadOnlyList<string>, CancellationToken, Task<ReapplyResult>> reapply,
        TimeProvider? time = null, ILogger? logger = null);
    public EffectiveCatalog Catalog { get; }
    public DomainListState State { get; }
    public Task<DomainListOfferResult> OfferAsync(byte[] file, CancellationToken cancellationToken = default);
    public Task<DomainListOfferResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    public Task<T> WithCatalogAsync<T>(Func<EffectiveCatalog, Task<T>> action, CancellationToken cancellationToken = default);
}
```
- Hợp đồng: `reapply` chỉ được gọi khi `Routed` thật sự đổi. `FeatureSet.ReapplyAsync` không có tính năng nào đang chạy thì chỉ đổi danh sách và trả thành công, nên service không cần biết tính năng nào đang bật. Ghi file chỉ sau khi reapply thành công. Mọi thao tác, kể cả `WithCatalogAsync`, chạy tuần tự qua một `SemaphoreSlim`.

- [ ] **Step 1: Test lỗi**

```csharp
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using Microsoft.Extensions.Time.Testing;
using static LunaDns.Core.Tests.Domains.TestDomainListKey;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainListServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-domainlist-svc-").FullName;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 7, 0, 0, TimeSpan.Zero));
    private readonly List<IReadOnlyList<string>> _reapplied = [];
    private DomainSelection _selection = new();
    private ReapplyResult _next = new(true, false);

    private string FilePath => Path.Combine(_dir, "domain-list.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private DomainListService Create(bool enabled = true) => new(new DomainListStore(FilePath, Keys), Keys, enabled, () => _selection,
        (domains, _) => { _reapplied.Add(domains); return Task.FromResult(_next); }, _time);

    private static byte[] Steam(long serial, params string[] domains) => Signed(serial, new() { ["steam"] = [.. domains] });

    [Fact]
    public async Task Accepted_list_reapplies_then_saves()
    {
        var service = Create();

        var result = await service.OfferAsync(Steam(5, "cdn.example.net"));

        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Equal(1, result.Added);
        Assert.Contains("cdn.example.net", Assert.Single(_reapplied));
        Assert.Contains("cdn.example.net", service.Catalog.For(DomainGroup.Steam));
        Assert.Equal(5, new DomainListStore(FilePath, Keys).Load().HighestSerial);
        Assert.Equal(_time.GetUtcNow(), service.State.CheckedAt);
    }

    [Fact]
    public async Task Failed_reapply_keeps_old_list_and_writes_nothing()
    {
        var service = Create();
        _next = new ReapplyResult(false, true, FeatureError.ServiceStartFailed);

        var result = await service.OfferAsync(Steam(5, "cdn.example.net"));

        Assert.Equal(DomainListOutcome.ReapplyFailed, result.Outcome);
        Assert.Equal(FeatureError.ServiceStartFailed, result.Error);
        Assert.Empty(service.Catalog.Added(DomainGroup.Steam));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task List_for_disabled_group_saves_without_reapply()
    {
        var service = Create();

        var result = await service.OfferAsync(Signed(5, new() { ["reddit"] = ["redditblog.com"] }));

        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Empty(_reapplied);
        Assert.Equal(5, service.State.Serial);
    }

    [Fact]
    public async Task Same_serial_only_updates_check_time()
    {
        var service = Create();
        await service.OfferAsync(Steam(5, "cdn.example.net"));
        _reapplied.Clear();
        _time.Advance(TimeSpan.FromHours(25));

        var result = await service.OfferAsync(Steam(5, "cdn.example.net"));

        Assert.Equal(DomainListOutcome.NotNewer, result.Outcome);
        Assert.Empty(_reapplied);
        Assert.Equal(_time.GetUtcNow(), new DomainListStore(FilePath, Keys).Load().CheckedAt);
    }

    [Fact]
    public async Task Disabled_service_refuses_offers()
    {
        var service = Create(enabled: false);
        Assert.Equal(DomainListOutcome.Disabled, (await service.OfferAsync(Steam(5, "cdn.example.net"))).Outcome);
    }

    [Fact]
    public async Task Switching_off_drops_layer_two_and_on_restores_without_download()
    {
        var service = Create();
        await service.OfferAsync(Steam(5, "cdn.example.net"));
        _reapplied.Clear();

        Assert.Equal(DomainListOutcome.Accepted, (await service.SetEnabledAsync(false)).Outcome);
        Assert.DoesNotContain("cdn.example.net", Assert.Single(_reapplied));
        Assert.Empty(service.Catalog.Added(DomainGroup.Steam));
        Assert.False(service.State.Enabled);

        await service.SetEnabledAsync(true);
        Assert.Contains("cdn.example.net", service.Catalog.For(DomainGroup.Steam));
    }

    [Fact]
    public async Task Switch_failure_rolls_back_the_flag()
    {
        var service = Create();
        await service.OfferAsync(Steam(5, "cdn.example.net"));
        _next = new ReapplyResult(false, true, FeatureError.ServiceStartFailed);

        Assert.Equal(DomainListOutcome.ReapplyFailed, (await service.SetEnabledAsync(false)).Outcome);
        Assert.True(service.State.Enabled);
    }

    [Fact]
    public async Task Turning_back_on_does_not_accept_older_serial()
    {
        var service = Create();
        await service.OfferAsync(Steam(9, "cdn.example.net"));
        await service.SetEnabledAsync(false);
        await service.SetEnabledAsync(true);

        Assert.Equal(DomainListOutcome.Downgrade, (await service.OfferAsync(Steam(8, "old.example.net"))).Outcome);
    }

    [Fact]
    public async Task Custom_entries_are_never_touched()
    {
        _selection = new DomainSelection { Custom = [new CustomDomain("cdn.example.net")] };
        var service = Create();

        await service.OfferAsync(Steam(5, "cdn.example.net"));

        Assert.Equal([new CustomDomain("cdn.example.net")], _selection.Custom);
    }

    [Fact]
    public async Task Service_loads_stored_list_on_start()
    {
        await Create().OfferAsync(Steam(5, "cdn.example.net"));
        Assert.Contains("cdn.example.net", Create().Catalog.For(DomainGroup.Steam));
    }

    [Fact]
    public async Task With_catalog_runs_inside_the_gate()
    {
        var service = Create();
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var held = service.WithCatalogAsync(async _ => { inside.SetResult(); await release.Task; return 0; });
        await inside.Task;

        var offer = service.OfferAsync(Steam(5, "cdn.example.net"));
        Assert.False(offer.IsCompleted);

        release.SetResult();
        await held;
        Assert.Equal(DomainListOutcome.Accepted, (await offer).Outcome);
    }
}
```
`FeatureError.ServiceStartFailed`: dùng một giá trị có thật của `FeatureError` (xem `src/LunaDns.Core/Features/FeatureError.cs`). Nếu tên khác thì đổi cả bốn chỗ trong test.

- [ ] **Step 2: Chạy, thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainListServiceTests`
Expected: FAIL (biên dịch).

- [ ] **Step 3: Viết service**

```csharp
using LunaDns.Core.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Domains;

/// <summary>
/// Chỗ chạy chung của lớp 2 (spec D4): Windows trong tiến trình app, macOS trong daemon. Xác minh, gộp, reapply khi
/// Routed đổi, rồi mới ghi file. Mọi thao tác chạy tuần tự qua một cổng; set-domains/Lưu Tuỳ chỉnh dùng WithCatalogAsync
/// để Routed tính bằng catalog đúng lúc áp. Công tắc lưu ở settings.json do bên gọi giữ; service chỉ giữ giá trị đang dùng.
/// </summary>
public sealed partial class DomainListService
{
    private readonly DomainListStore _store;
    private readonly IReadOnlyDictionary<string, string> _keys;
    private readonly Func<DomainSelection> _selection;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<ReapplyResult>> _reapply;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile DomainListStoreData _data;
    private volatile bool _enabled;

    public DomainListService(DomainListStore store, IReadOnlyDictionary<string, string> keys, bool enabled,
        Func<DomainSelection> selection, Func<IReadOnlyList<string>, CancellationToken, Task<ReapplyResult>> reapply,
        TimeProvider? time = null, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _reapply = reapply ?? throw new ArgumentNullException(nameof(reapply));
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _enabled = enabled;
        _data = store.Load();
    }

    public EffectiveCatalog Catalog => CatalogFor(_data, _enabled);

    public DomainListState State
    {
        get
        {
            var data = _data;
            var catalog = CatalogFor(data, _enabled);
            return new DomainListState(_enabled, data.Current?.Serial ?? 0, data.Current?.Issued, data.CheckedAt, catalog.AllAdded);
        }
    }

    public async Task<DomainListOfferResult> OfferAsync(byte[] file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_enabled) return new(DomainListOutcome.Disabled);
            var verified = DomainListVerifier.Verify(file, _keys, _data.HighestSerial);
            var now = _time.GetUtcNow();
            if (verified.Outcome == DomainListOutcome.NotNewer)
            {
                TrySave(_data with { CheckedAt = now });
                return new(DomainListOutcome.NotNewer);
            }
            if (verified.Outcome != DomainListOutcome.Accepted)
            {
                LogRejected(_logger, verified.Outcome);
                return new(verified.Outcome);
            }
            if (verified.Dropped > 0) LogDropped(_logger, verified.Dropped, verified.Update!.Serial);

            var next = new DomainListStoreData(verified.Update, verified.Update!.Serial, now);
            var before = CatalogFor(_data, true);
            var after = CatalogFor(next, true);
            var applied = await ApplyAsync(before, after, cancellationToken).ConfigureAwait(false);
            if (!applied.Success) return Failed(applied);
            try
            {
                _store.Save(next);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogSaveFailed(_logger, ex);
                var back = await ApplyAsync(after, before, CancellationToken.None).ConfigureAwait(false);
                return new(DomainListOutcome.SaveFailed, PreviousRestored: back.Success);
            }
            _data = next;
            return new(DomainListOutcome.Accepted, CountAdded(before, after), verified.Dropped);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DomainListOfferResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (enabled == _enabled) return new(DomainListOutcome.Accepted);
            var before = CatalogFor(_data, _enabled);
            var after = CatalogFor(_data, enabled);
            var applied = await ApplyAsync(before, after, cancellationToken).ConfigureAwait(false);
            if (!applied.Success) return Failed(applied);
            _enabled = enabled;
            return new(DomainListOutcome.Accepted, CountAdded(before, after));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> WithCatalogAsync<T>(Func<EffectiveCatalog, Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(Catalog).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static EffectiveCatalog CatalogFor(DomainListStoreData data, bool enabled) =>
        enabled && data.Current is { } current ? EffectiveCatalog.From(current.Groups, current.Issued) : EffectiveCatalog.Builtin;

    private async Task<ReapplyResult> ApplyAsync(EffectiveCatalog before, EffectiveCatalog after, CancellationToken cancellationToken)
    {
        var selection = _selection();
        var routed = FeatureDomains.Routed(selection, after);
        if (routed.SequenceEqual(FeatureDomains.Routed(selection, before))) return new ReapplyResult(true, false);
        return await _reapply(routed, cancellationToken).ConfigureAwait(false);
    }

    private static int CountAdded(EffectiveCatalog before, EffectiveCatalog after) =>
        DomainCatalog.Selectable.Sum(g => after.Added(g).Except(before.Added(g), StringComparer.Ordinal).Count());

    private DomainListOfferResult Failed(ReapplyResult result)
    {
        LogReapplyFailed(_logger, result.Error);
        return new(DomainListOutcome.ReapplyFailed, Error: result.Error, PreviousRestored: result.PreviousRestored);
    }

    private void TrySave(DomainListStoreData data)
    {
        try
        {
            _store.Save(data);
            _data = data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bỏ danh sách tên miền tải về: {Outcome}.")]
    private static partial void LogRejected(ILogger logger, DomainListOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Danh sách tên miền bản {Serial}: bỏ {Count} dòng không hợp lệ.")]
    private static partial void LogDropped(ILogger logger, int count, long serial);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không áp được danh sách tên miền mới: {Error}.")]
    private static partial void LogReapplyFailed(ILogger logger, FeatureError error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không ghi được domain-list.json.")]
    private static partial void LogSaveFailed(ILogger logger, Exception ex);
}
```
Kiểm thêm: Core đã tham chiếu `Microsoft.Extensions.Logging.Abstractions` (FeatureSet dùng `NullLogger`), nên không cần thêm package.

- [ ] **Step 4: Chạy toàn bộ Core**

Run: `dotnet test tests/LunaDns.Core.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add src/LunaDns.Core/Domains/DomainListService.cs tests/LunaDns.Core.Tests/Domains/DomainListServiceTests.cs
git commit -m "feat(core): apply layer-2 domain lists through one serialized service"
```

---

### Task 5: Công cụ ký `tools/DomainListSigner`, `data/domain-list.json`, bước CI

**Files:**
- Create: `tools/DomainListSigner/DomainListSigner.csproj`, `tools/DomainListSigner/Program.cs`, `data/domain-list.json`
- Modify: `src/LunaDns.Core/Domains/DomainListSigning.cs` (thêm `CheckSource`), `LunaDNS.sln` (thêm project vào thư mục `tools`), `.github/workflows/ci.yml`
- Test: `tests/LunaDns.Core.Tests/Domains/DomainListSigningTests.cs`

**Interfaces:**
- Consumes: `DomainListSigning.Sign`, `DomainListVerifier.Verify`, `DomainListKeys.Production`.
- Produces: `DomainListSigning.CheckSource(ReadOnlySpan<byte> json) : IReadOnlyList<string>` (danh sách lỗi; rỗng = hợp lệ). Các lệnh CLI được ghi trong `docs/dev/releasing.md` (Task 10).

- [ ] **Step 1: Test lỗi cho `CheckSource` và vòng ký → xác minh**

```csharp
using System.Text;
using LunaDns.Core.Domains;
using static LunaDns.Core.Tests.Domains.TestDomainListKey;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainListSigningTests
{
    [Fact]
    public void Signed_file_verifies_with_its_public_key() =>
        Assert.Equal(DomainListOutcome.Accepted, DomainListVerifier.Verify(Signed(1, new() { ["steam"] = ["cdn.example.net"] }), Keys, 0).Outcome);

    [Fact]
    public void Valid_source_has_no_errors() =>
        Assert.Empty(DomainListSigning.CheckSource(Encoding.UTF8.GetBytes("""{"groups":{"steam":["cdn.example.net"],"reddit":[]}}""")));

    [Theory]
    [InlineData("""{"groups":{"steam":["com"]}}""", "steam: com")]
    [InlineData("""{"groups":{"steam":["steampowered.com"]}}""", "steam: steampowered.com")]
    [InlineData("""{"groups":{"epic":["x.example.net"]}}""", "epic")]
    [InlineData("""{"groups":{"pubg":["x.example.net"]}}""", "pubg")]
    [InlineData("not json", "JSON")]
    public void Bad_source_reports_the_offending_entry(string json, string expected) =>
        Assert.Contains(DomainListSigning.CheckSource(Encoding.UTF8.GetBytes(json)), e => e.Contains(expected, StringComparison.Ordinal));
}
```

- [ ] **Step 2: Chạy, thấy lỗi**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainListSigningTests`
Expected: FAIL: `CheckSource` chưa có.

- [ ] **Step 3: Viết `CheckSource`**

Thêm vào `DomainListSigning`:
```csharp
    /// <summary>
    /// Kiểm data/domain-list.json trước khi ký (CI chạy qua lệnh verify-source). Nghiêm hơn Verify: nhóm lạ, pubg, custom
    /// và mọi dòng mà Verify sẽ bỏ đều là lỗi, để PO không phát hành dòng không bao giờ có hiệu lực.
    /// </summary>
    public static IReadOnlyList<string> CheckSource(ReadOnlySpan<byte> json)
    {
        DomainListSource? source;
        try
        {
            source = JsonSerializer.Deserialize(json, DomainListJsonContext.Default.DomainListSource);
        }
        catch (JsonException ex)
        {
            return [$"JSON hỏng: {ex.Message}"];
        }
        if (source?.Groups is null) return ["JSON thiếu \"groups\"."];
        var errors = new List<string>();
        var total = 0;
        foreach (var (key, list) in source.Groups)
        {
            if (!DomainListVerifier.TryParseGroup(key, out var group))
            {
                errors.Add($"{key}: nhóm không nhận lớp 2.");
                continue;
            }
            if (list.Count > DomainListVerifier.MaxPerGroup) errors.Add($"{key}: quá {DomainListVerifier.MaxPerGroup} tên miền.");
            total += list.Count;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var domain in list)
            {
                if (DomainPolicy.Normalize(domain) != domain) errors.Add($"{key}: {domain} chưa chuẩn hoá.");
                else if (DomainPolicy.Validate(domain) is var r and not DomainRejection.None) errors.Add($"{key}: {domain} bị từ chối ({r}).");
                else if (DomainMatcher.Matches(domain, DomainCatalog.For(group))) errors.Add($"{key}: {domain} đã có ở lớp 1.");
                else if (!seen.Add(domain)) errors.Add($"{key}: {domain} lặp lại.");
            }
        }
        if (total > DomainListVerifier.MaxTotal) errors.Add($"Quá {DomainListVerifier.MaxTotal} tên miền.");
        return errors;
    }
```

- [ ] **Step 4: Project công cụ ký**

`tools/DomainListSigner/DomainListSigner.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LunaDns.Core\LunaDns.Core.csproj" />
  </ItemGroup>
</Project>
```

`tools/DomainListSigner/Program.cs`:
```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LunaDns.Core.Domains;

// Ký danh sách lớp 2 trên máy PO (spec D4). Khoá bí mật: PEM PKCS#8 có mật khẩu, không bao giờ đưa vào repo hay CI.
// Mật khẩu đọc từ biến môi trường LUNADNS_KEY_PASSWORD, hoặc nhập khi được hỏi.
return args switch
{
    ["keygen", "--out", var pem] => KeyGen(pem),
    ["verify-source", var source] => VerifySource(source),
    ["sign", "--key", var pem, "--key-id", var keyId, "--in", var source, "--out", var output] => Sign(pem, keyId, source, output),
    ["verify", var file] => Verify(file),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("""
        DomainListSigner keygen --out <khoá.pem>
        DomainListSigner verify-source <data/domain-list.json>
        DomainListSigner sign --key <khoá.pem> --key-id <id> --in <data/domain-list.json> --out <domains.json>
        DomainListSigner verify <domains.json>
        """);
    return 2;
}

static string Password()
{
    var fromEnv = Environment.GetEnvironmentVariable("LUNADNS_KEY_PASSWORD");
    if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
    Console.Error.Write("Mật khẩu khoá: ");
    return Console.ReadLine() ?? string.Empty;
}

static int KeyGen(string pem)
{
    if (File.Exists(pem)) { Console.Error.WriteLine($"{pem} đã có, không ghi đè."); return 1; }
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var encrypted = key.ExportEncryptedPkcs8PrivateKeyPem(Password(),
        new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
    File.WriteAllText(pem, encrypted);
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pem, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.WriteLine($"Khoá công khai (dán vào DomainListKeys.Production):\n{DomainListSigning.PublicKey(key)}");
    return 0;
}

static int VerifySource(string source)
{
    var errors = DomainListSigning.CheckSource(File.ReadAllBytes(source));
    foreach (var e in errors) Console.Error.WriteLine(e);
    return errors.Count == 0 ? 0 : 1;
}

static int Sign(string pem, string keyId, string source, string output)
{
    var bytes = File.ReadAllBytes(source);
    if (VerifySource(source) != 0) return 1;
    using var key = ECDsa.Create();
    key.ImportFromEncryptedPem(File.ReadAllText(pem), Password());
    if (!DomainListKeys.Production.TryGetValue(keyId, out var embedded) || embedded != DomainListSigning.PublicKey(key))
    {
        Console.Error.WriteLine($"Khoá {keyId} chưa nhúng trong DomainListKeys.Production của bản build này.");
        return 1;
    }
    var now = DateTimeOffset.UtcNow;
    var serial = long.Parse(now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(bytes)!;
    var signed = DomainListSigning.Sign(new DomainListSource(parsed["groups"]), serial, now, key, keyId);
    var check = DomainListVerifier.Verify(signed, DomainListKeys.Production, 0);
    if (check.Outcome != DomainListOutcome.Accepted || check.Dropped != 0)
    {
        Console.Error.WriteLine($"Tự kiểm lỗi: {check.Outcome}, bỏ {check.Dropped} dòng.");
        return 1;
    }
    File.WriteAllBytes(output, signed);
    Console.WriteLine($"Đã ký serial {serial}, {signed.Length} byte → {output}");
    return 0;
}

static int Verify(string file)
{
    var result = DomainListVerifier.Verify(File.ReadAllBytes(file), DomainListKeys.Production, 0);
    Console.WriteLine(result.Update is { } u
        ? $"{result.Outcome}: serial {u.Serial}, {u.Groups.Sum(g => g.Value.Count)} tên miền, bỏ {result.Dropped}"
        : result.Outcome.ToString());
    return result.Outcome == DomainListOutcome.Accepted ? 0 : 1;
}
```
`JsonSerializer.Deserialize<T>` không dùng source generator, nên có thể sinh cảnh báo trim/AOT (IL2026/IL3050) nếu project bật analyzer trim. Công cụ không publish trimmed. Nếu build `-warnaserror` báo lỗi, thay bằng `DomainListSigning.ReadSource(bytes)` (public, dùng `DomainListJsonContext.Default.DomainListSource`) và thêm hàm đó vào `DomainListSigning`.

`data/domain-list.json`:
```json
{
  "groups": {
    "steam": [],
    "reddit": []
  }
}
```

Thêm project vào solution:
```bash
dotnet sln LunaDNS.sln add tools/DomainListSigner/DomainListSigner.csproj --solution-folder tools
```
(Kiểm tên file `.sln` thật bằng `ls *.sln`.)

`.github/workflows/ci.yml`: trong **cả hai** job (Windows và macOS), thêm ngay sau bước `dotnet build -c Release -warnaserror`:
```yaml
      - run: dotnet run --project tools/DomainListSigner -c Release --no-build -- verify-source data/domain-list.json
```

- [ ] **Step 5: Chạy**

Run: `dotnet test tests/LunaDns.Core.Tests --filter DomainListSigningTests && dotnet build -c Release -warnaserror && dotnet run --project tools/DomainListSigner -c Release --no-build -- verify-source data/domain-list.json`
Expected: test PASS, build 0 warning, `verify-source` thoát mã 0.

Thử tay (không commit khoá): `dotnet run --project tools/DomainListSigner -- keygen --out "$TMPDIR/k.pem"` in ra một chuỗi base64. Lệnh `sign` với khoá này báo "chưa nhúng", đúng như mong đợi.

- [ ] **Step 6: Commit**
```bash
git add tools/DomainListSigner data/domain-list.json src/LunaDns.Core/Domains/DomainListSigning.cs tests/LunaDns.Core.Tests/Domains/DomainListSigningTests.cs *.sln .github/workflows/ci.yml
git commit -m "feat(tools): add domain list signer and verify the source in CI"
```

---

### Task 6: Daemon macOS: `get-domain-list`, `set-domain-list`, `set-domain-list-enabled`

**Files:**
- Modify: `src/LunaDns.Platform.MacOS/Daemon/DaemonProtocol.cs`, `src/LunaDns.Platform.MacOS/Daemon/DaemonCommands.cs`, `src/LunaDns.MacCli/MacPaths.cs`, `src/LunaDns.MacCli/Engine.cs`, `src/LunaDns.MacCli/Program.cs:196,236`
- Test: `tests/LunaDns.Platform.MacOS.Tests/DaemonTests.cs` (sửa 3 chỗ tạo `DaemonCommands`), `tests/LunaDns.Platform.MacOS.Tests/DaemonDomainListTests.cs` (mới)

**Interfaces:**
- Consumes: `DomainListService`, `DomainListVerifier.GroupKey/TryParseGroup`, `SettingsHolder`.
- Produces (dây JSON):
  - `DaemonProtocol.GetDomainList = "get-domain-list"`, `SetDomainList = "set-domain-list"`, `SetDomainListEnabled = "set-domain-list-enabled"`, `MaxLineBytes = 131072`.
  - `DaemonRequest` thêm `string? Envelope = null` (base64 của nguyên file) và `bool? Enabled = null`. Hai trường mới đặt **cuối** danh sách tham số.
  - `DaemonResponse` thêm `DaemonDomainList? DomainList = null`, `DaemonDomainListResult? DomainListResult = null`, cũng đặt cuối.
  - `sealed record DaemonDomainList(bool Enabled, long Serial, DateTimeOffset? Issued, DateTimeOffset? CheckedAt, Dictionary<string, string[]> Added)`, có `static From(DomainListState)` và `DomainListState ToState()`.
  - `sealed record DaemonDomainListResult(string Outcome, int Added, int Dropped, string? Error, bool PreviousRestored)`, có `static From(DomainListOfferResult)` và `DomainListOfferResult ToResult()`.
  - `DaemonCommands` ctor nhận thêm `DomainListService domainList` (tham số bắt buộc, đặt trước `logger`).

- [ ] **Step 1: Test lỗi**

`tests/LunaDns.Platform.MacOS.Tests/DaemonDomainListTests.cs`. Dựng `FeatureSet` giống `DaemonTests` (xem `DaemonTests.cs:160-200` để biết `_features`, `_journal` được tạo thế nào; chép đúng helper đó). Khoá test sinh tại chỗ, vì project này không thấy `TestDomainListKey` của Core.Tests:
```csharp
using System.Security.Cryptography;
using LunaDns.Core.Domains;
using LunaDns.Core.Settings;
using LunaDns.Platform.MacOS.Daemon;

namespace LunaDns.Platform.MacOS.Tests;

public sealed class DaemonDomainListTests : IAsyncLifetime
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly IReadOnlyDictionary<string, string> Keys =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["t"] = DomainListSigning.PublicKey(Key) };

    private readonly TempDirectory _tmp = new();
    private DaemonHarness _h = null!; // xem Step 3: helper dựng FeatureSet + SettingsHolder + DomainListService + DaemonCommands

    public Task InitializeAsync() { _h = DaemonHarness.Create(_tmp, Keys); return Task.CompletedTask; }

    public async Task DisposeAsync() { await _h.DisposeAsync(); _tmp.Dispose(); }

    private static string Envelope(long serial, params string[] steam) =>
        Convert.ToBase64String(DomainListSigning.Sign(new DomainListSource(new() { ["steam"] = [.. steam] }), serial, DateTimeOffset.UnixEpoch, Key, "t"));

    private Task<DaemonResponse> Send(DaemonRequest r) => _h.Commands.HandleAsync(r, CancellationToken.None);

    [Fact]
    public async Task Set_domain_list_verifies_applies_and_reports_state()
    {
        var set = await Send(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: Envelope(5, "cdn.example.net")));
        Assert.True(set.Ok);
        Assert.Equal("Accepted", set.DomainListResult!.Outcome);

        var get = await Send(new DaemonRequest(DaemonProtocol.GetDomainList));
        Assert.Equal(5, get.DomainList!.Serial);
        Assert.Equal(["cdn.example.net"], get.DomainList.Added["steam"]);
    }

    [Fact]
    public async Task Tampered_envelope_is_rejected_by_the_daemon()
    {
        var bytes = Convert.FromBase64String(Envelope(5, "cdn.example.net"));
        bytes[^5] ^= 1;
        var set = await Send(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: Convert.ToBase64String(bytes)));
        Assert.False(set.Ok);
        Assert.NotEqual("Accepted", set.DomainListResult!.Outcome);
    }

    [Fact]
    public async Task Missing_or_bad_base64_is_refused()
    {
        Assert.False((await Send(new DaemonRequest(DaemonProtocol.SetDomainList))).Ok);
        Assert.False((await Send(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: "%%%"))).Ok);
    }

    [Fact]
    public async Task Switch_is_saved_without_touching_domains_or_tuning()
    {
        var before = _h.Settings.Current;
        var off = await Send(new DaemonRequest(DaemonProtocol.SetDomainListEnabled, Enabled: false));

        Assert.True(off.Ok);
        var saved = new JsonFileSettingsStore(_h.SettingsPath).Load();
        Assert.False(saved.DomainListUpdates);
        Assert.Equal(before.Domains, saved.Domains);
        Assert.Equal(before.SplitMode, saved.SplitMode);
        Assert.False((await Send(new DaemonRequest(DaemonProtocol.GetDomainList))).DomainList!.Enabled);
    }

    [Fact]
    public async Task Set_domains_uses_layer_two()
    {
        await Send(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: Envelope(5, "cdn.example.net")));
        await Send(new DaemonRequest(DaemonProtocol.SetDomains, Domains: new DomainSelection()));
        Assert.Contains("cdn.example.net", _h.Features.RoutedDomains);
    }

    [Fact]
    public void Max_line_holds_a_full_size_list() =>
        Assert.True(DaemonProtocol.MaxLineBytes >= 4 * (DomainListVerifier.MaxBytes / 3 + 1) + 1024);
}
```
Thêm vào `DaemonTests.cs` một test đọc dòng dài qua socket thật, dùng cách gửi/nhận mà các test socket hiện có trong file đang dùng:
```csharp
    [Fact]
    public async Task Line_of_100_kilobytes_is_read()
    {
        // Envelope base64 ~88 KB: trước D4 bị cắt ở 65 536 byte và trả "Yêu cầu rỗng hoặc quá dài."
        var request = new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: new string('A', 100_000));
        var response = await SendOverSocketAsync(request); // helper socket hiện có trong DaemonTests
        Assert.NotEqual("Yêu cầu rỗng hoặc quá dài.", response.Message);
    }
```

- [ ] **Step 2: Chạy, thấy lỗi**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests --filter "DaemonDomainListTests|Line_of_100"`
Expected: FAIL (biên dịch).

- [ ] **Step 3: Protocol, commands, harness**

`DaemonProtocol.cs`:
```csharp
    public const int MaxLineBytes = 131072; // set-domain-list: envelope 64 KB → base64 ~88 KB + JSON
    public const string GetDomainList = "get-domain-list";
    public const string SetDomainList = "set-domain-list";
    public const string SetDomainListEnabled = "set-domain-list-enabled";
```
Thêm `string? Envelope = null, bool? Enabled = null` vào **cuối** `DaemonRequest`, và `DaemonDomainList? DomainList = null, DaemonDomainListResult? DomainListResult = null` vào **cuối** `DaemonResponse`. Thêm `[JsonSerializable(typeof(DaemonDomainList))]` và `[JsonSerializable(typeof(DaemonDomainListResult))]` vào `DaemonJsonContext`.

```csharp
/// <summary>Trạng thái lớp 2 của daemon (D4). Added theo khoá nhóm của file ("steam"), để không phụ thuộc cách ghi enum.</summary>
public sealed record DaemonDomainList(bool Enabled, long Serial, DateTimeOffset? Issued, DateTimeOffset? CheckedAt, Dictionary<string, string[]> Added)
{
    public static DaemonDomainList From(DomainListState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.Enabled, state.Serial, state.Issued, state.CheckedAt,
            state.Added.ToDictionary(kv => DomainListVerifier.GroupKey(kv.Key), kv => kv.Value.ToArray(), StringComparer.Ordinal));
    }

    public DomainListState ToState()
    {
        var added = new Dictionary<DomainGroup, IReadOnlyList<string>>();
        foreach (var (key, list) in Added ?? [])
            if (DomainListVerifier.TryParseGroup(key, out var group) && list is not null) added[group] = list;
        return new(Enabled, Serial, Issued, CheckedAt, added);
    }
}

/// <summary>Kết quả set-domain-list / set-domain-list-enabled. Outcome, Error là tên enum dạng chuỗi.</summary>
public sealed record DaemonDomainListResult(string Outcome, int Added, int Dropped, string? Error, bool PreviousRestored)
{
    public static DaemonDomainListResult From(DomainListOfferResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new(r.Outcome.ToString(), r.Added, r.Dropped, r.Error == FeatureError.None ? null : r.Error.ToString(), r.PreviousRestored);
    }

    public DomainListOfferResult ToResult() => new(
        Enum.TryParse<DomainListOutcome>(Outcome, out var o) ? o : DomainListOutcome.Unavailable,
        Added, Dropped,
        Enum.TryParse<FeatureError>(Error, out var e) ? e : FeatureError.None,
        PreviousRestored);
}
```

`DaemonCommands.cs`:
- Thêm field `private readonly DomainListService _domainList;`. Cả hai ctor nhận `DomainListService domainList` ngay trước `ILogger? logger`.
- Trong `HandleAsync`:
```csharp
            case DaemonProtocol.GetDomainList:
                return Ok() with { DomainList = DaemonDomainList.From(_domainList.State) };
            case DaemonProtocol.SetDomainList:
                return await SetDomainListAsync(request.Envelope, cancellationToken).ConfigureAwait(false);
            case DaemonProtocol.SetDomainListEnabled:
                return await SetDomainListEnabledAsync(request.Enabled, cancellationToken).ConfigureAwait(false);
```
- Hai hàm mới:
```csharp
    /// <summary>D4: không tin app. Daemon xác minh lại chữ ký, serial, DomainPolicy bằng khoá nhúng trong chính nó.</summary>
    private async Task<DaemonResponse> SetDomainListAsync(string? envelope, CancellationToken cancellationToken)
    {
        if (envelope is null) return Refused("Thiếu danh sách tên miền.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(envelope);
        }
        catch (FormatException)
        {
            return Refused("Danh sách tên miền không đọc được.");
        }
        var result = await _domainList.OfferAsync(bytes, cancellationToken).ConfigureAwait(false);
        return Ok() with
        {
            Ok = result.Success,
            DomainListResult = DaemonDomainListResult.From(result),
            DomainList = DaemonDomainList.From(_domainList.State),
        };
    }

    /// <summary>Áp trước, lưu settings.json sau; lưu lỗi thì bật/tắt ngược lại để tính năng khớp file.</summary>
    private async Task<DaemonResponse> SetDomainListEnabledAsync(bool? enabled, CancellationToken cancellationToken)
    {
        if (enabled is not { } value) return Refused("Thiếu giá trị công tắc.");
        var result = await _domainList.SetEnabledAsync(value, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return Ok() with { Ok = false, DomainListResult = DaemonDomainListResult.From(result), DomainList = DaemonDomainList.From(_domainList.State) };
        try
        {
            _settings.Save(_settings.Current with { DomainListUpdates = value });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _domainList.SetEnabledAsync(!value, CancellationToken.None).ConfigureAwait(false);
            return Ok() with { Ok = false, Message = $"Không lưu được settings.json ({ex.Message}); giữ công tắc cũ." };
        }
        return Ok() with { DomainListResult = DaemonDomainListResult.From(result), DomainList = DaemonDomainList.From(_domainList.State) };
    }
```
- `SetDomainsAsync`: bọc đoạn từ `ReapplyAsync` tới hết khối lưu trong `_domainList.WithCatalogAsync(async catalog => { ... }, cancellationToken)`, và thay cả hai `FeatureDomains.Routed(x)` bằng `FeatureDomains.Routed(x, catalog)`. Phần kiểm hình dạng và kiểm dòng vẫn nằm ngoài cổng.

`MacPaths.cs`: `public string DomainListFile => Path.Combine(Root, "domain-list.json");`

`Engine.cs`: tạo service trước `FeatureSet`, và tính `routedDomains` bằng catalog của service:
```csharp
        DomainList = new DomainListService(new DomainListStore(paths.DomainListFile, DomainListKeys.Production), DomainListKeys.Production,
            Settings.Current.DomainListUpdates, () => Settings.Current.Domains,
            (domains, ct) => Features.ReapplyAsync(domains, ct), logger: loggers.CreateLogger<DomainListService>());
        Features = new FeatureSet(Journal, new SystemChanges(Journal, configurator), services,
            () => Settings.Current.Tuning(), logger: loggers.CreateLogger<FeatureSet>(),
            routedDomains: FeatureDomains.Routed(Settings.Current.Domains, DomainList.Catalog));
```
Thêm `public DomainListService DomainList { get; }`. Lambda đọc `Features` lúc chạy, sau khi ctor đã gán.

`Program.cs:196,236`: truyền `engine.DomainList` vào `new DaemonCommands(...)`.

Harness cho test (đặt trong `DaemonDomainListTests.cs` hoặc file riêng `DaemonHarness.cs`): dựng `ChangeJournal` + `FeatureSet` đúng như `DaemonTests` (`FakeConfigurator`/`FakeFeatureServices` của project test macOS), `SettingsHolder(new JsonFileSettingsStore(SettingsPath))`, `DomainListService(new DomainListStore(Path.Combine(dir, "domain-list.json"), keys), keys, true, () => settings.Current.Domains, (d, ct) => features.ReapplyAsync(d, ct))`, rồi `DaemonCommands(features, journal, "test", settings, domainList)`. Lộ ra `Commands`, `Features`, `Settings`, `SettingsPath`. Ba chỗ tạo `DaemonCommands` trong `DaemonTests.cs` (dòng ~167, 199, 451) truyền thêm `DomainListService` có store trong thư mục tạm của test và `Keys` rỗng.

- [ ] **Step 4: Chạy**

Run: `dotnet test tests/LunaDns.Platform.MacOS.Tests && dotnet build -c Release -warnaserror`
Expected: PASS, 0 warning. Các test cũ của `DaemonTests` (status, set-domains, set-tuning) vẫn xanh. Đó là bằng chứng dây JSON cũ không đổi.

- [ ] **Step 5: Commit**
```bash
git add src/LunaDns.Platform.MacOS src/LunaDns.MacCli tests/LunaDns.Platform.MacOS.Tests
git commit -m "feat(macos): let the daemon verify and apply the layer-2 domain list"
```

---

### Task 7: `IFeatureController`, controller Windows/macOS, `AppServices`

**Files:**
- Create: `src/LunaDns.App/Features/IDomainListSink.cs`
- Modify: `src/LunaDns.App/Features/IFeatureController.cs`, `LocalFeatureController.cs`, `src/LunaDns.App/MacOS/DaemonFeatureController.cs`, `src/LunaDns.App/AppServices.cs`, `src/LunaDns.App/AppPaths.cs`
- Test: `tests/LunaDns.App.Tests/Features/LocalFeatureControllerDomainListTests.cs` (mới), `tests/LunaDns.App.Tests/MacOS/DaemonFeatureControllerTests.cs` (thêm), `tests/LunaDns.App.Tests/AppServicesTests.cs` (thêm). Nếu test nằm ở đường dẫn khác, đặt theo chỗ test hiện có của từng lớp.

**Interfaces:**
- Consumes: `DomainListService`, `DaemonDomainList`, `DaemonDomainListResult`.
- Produces:
```csharp
internal interface IDomainListSink
{
    /// <summary>null: dịch vụ nền không trả lời hoặc là bản cũ (ẩn dòng "Tự cập nhật danh sách").</summary>
    Task<DomainListState?> GetDomainListAsync();
    Task<DomainListOfferResult> OfferDomainListAsync(byte[] file);
}
internal interface IFeatureController : IDomainListSink, IAsyncDisposable
{
    // ... như cũ, thêm:
    Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled);
}
// AppServices:
public Task<DomainListState?> GetDomainListAsync();
public Task<DomainListOfferResult> OfferDomainListAsync(byte[] file);
public Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled);   // Local: lưu Settings.DomainListUpdates sau khi áp
public async Task<EffectiveCatalog> CatalogAsync();                           // từ GetDomainListAsync, null → Builtin
// AppPaths:
public string DomainListFile => Path.Combine(DataDirectory, "domain-list.json");
```

- [ ] **Step 1: Test lỗi**

Windows (`LocalFeatureControllerDomainListTests`): dựng `AppServices(paths, new FakeConfigurator(), loggers)` như các test `AppServices` hiện có. Vì `DomainListKeys.Production` rỗng, test chỉ kiểm được đường từ chối và công tắc:
```csharp
    [Fact]
    public async Task Unsigned_list_is_refused_and_nothing_is_written()
    {
        await using var services = CreateServices();
        var result = await services.OfferDomainListAsync("""{"keyId":"x","payload":"e30=","sig":"AA=="}"""u8.ToArray());
        Assert.False(result.Success);
        Assert.False(File.Exists(services.Paths.DomainListFile));
    }

    [Fact]
    public async Task Switch_is_saved_to_settings()
    {
        await using var services = CreateServices();
        Assert.True((await services.SetDomainListEnabledAsync(false)).Success);
        Assert.False(services.Settings.DomainListUpdates);
        Assert.False((await services.GetDomainListAsync())!.Enabled);
    }

    [Fact]
    public async Task Catalog_without_list_is_builtin()
    {
        await using var services = CreateServices();
        Assert.Same(EffectiveCatalog.Builtin, await services.CatalogAsync());
    }
```
Đường thành công trên Windows đã có test ở `DomainListServiceTests` (Core). Controller chỉ nối dây.

macOS (`DaemonFeatureControllerTests`, dùng socket giả hiện có của file đó):
- `get-domain-list` trả `DomainList` thì ra `DomainListState` đúng nhóm;
- trả "Lệnh lạ: get-domain-list" thì ra `null`;
- `set-domain-list` gửi đúng base64 của byte;
- daemon không trả lời thì `OfferDomainListAsync` ra `Outcome = Unavailable`.

- [ ] **Step 2: Chạy, thấy lỗi**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 --filter "DomainList"`
Expected: FAIL (biên dịch).

- [ ] **Step 3: Viết code**

`IDomainListSink.cs` như phần Interfaces. `IFeatureController` kế thừa `IDomainListSink` và thêm `SetDomainListEnabledAsync`.

`LocalFeatureController` ctor: tạo service **trước** `FeatureSet`:
```csharp
        DomainList = new DomainListService(new DomainListStore(paths.DomainListFile, DomainListKeys.Production), DomainListKeys.Production,
            settings().DomainListUpdates, () => settings().Domains, (domains, ct) => Features.ReapplyAsync(domains, ct),
            clock, loggerFactory.CreateLogger<DomainListService>());
        var services = new CoreFeatureServices(() => settings().DohServers(), loggerFactory);
        Features = new FeatureSet(Journal, new SystemChanges(Journal, configurator), services,
            () => settings().Tuning(), clock, loggerFactory.CreateLogger<FeatureSet>(),
            routedDomains: FeatureDomains.Routed(settings().Domains, DomainList.Catalog));
```
Thêm `public DomainListService DomainList { get; }` và:
```csharp
    public Task<DomainListState?> GetDomainListAsync() => Task.FromResult<DomainListState?>(DomainList.State);

    public Task<DomainListOfferResult> OfferDomainListAsync(byte[] file) => DomainList.OfferAsync(file);

    public Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled) => DomainList.SetEnabledAsync(enabled);
```
`ApplyDomainsAsync` đổi thành:
```csharp
        var result = await DomainList.WithCatalogAsync(c => Features.ReapplyAsync(FeatureDomains.Routed(selection, c))).ConfigureAwait(false);
```

`DaemonFeatureController`: theo mẫu `GetTuningAsync`/`ApplyTuningAsync` hiện có trong file:
```csharp
    public async Task<DomainListState?> GetDomainListAsync()
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.GetDomainList)).ConfigureAwait(false);
        return response?.DomainList?.ToState();   // "Lệnh lạ:" hay không trả lời → null
    }

    public async Task<DomainListOfferResult> OfferDomainListAsync(byte[] file)
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: Convert.ToBase64String(file))).ConfigureAwait(false);
        return response?.DomainListResult?.ToResult() ?? new DomainListOfferResult(DomainListOutcome.Unavailable);
    }

    public async Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled)
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetDomainListEnabled, Enabled: enabled)).ConfigureAwait(false);
        return response?.DomainListResult?.ToResult() ?? new DomainListOfferResult(DomainListOutcome.Unavailable);
    }
```
Kiểm `SendAsync` hiện có trả gì khi daemon không trả lời, hoặc trả "Lệnh lạ:" (xem `DaemonFeatureController.cs:97-110,183`). Theo đúng cách `GetTuningAsync` đang xử lý hai trường hợp đó, để ra `null`/`Unavailable`.

`AppServices`:
```csharp
    public Task<DomainListState?> GetDomainListAsync() => Features.GetDomainListAsync();

    public Task<DomainListOfferResult> OfferDomainListAsync(byte[] file) => Features.OfferDomainListAsync(file);

    /// <summary>Áp rồi mới lưu: Windows lưu DomainListUpdates; daemon tự lưu. Lưu lỗi thì bật/tắt ngược lại.</summary>
    public async Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled)
    {
        var result = await Features.SetDomainListEnabledAsync(enabled).ConfigureAwait(false);
        if (!result.Success || Features.Availability != ControllerAvailability.Local) return result;
        try
        {
            Settings = Settings with { DomainListUpdates = enabled };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Features.SetDomainListEnabledAsync(!enabled).ConfigureAwait(false);
            return new DomainListOfferResult(DomainListOutcome.SaveFailed);
        }
        return result;
    }

    public async Task<EffectiveCatalog> CatalogAsync() =>
        await GetDomainListAsync().ConfigureAwait(false) is { } state ? EffectiveCatalog.From(state.Added, state.Issued) : EffectiveCatalog.Builtin;
```
`ProbeAsync` (dòng ~243): `ProbeTargets.GroupOf(h, selection, catalog)`, với `catalog = await CatalogAsync()`.

Mọi lớp giả của `IFeatureController` trong `tests/LunaDns.App.Tests` (tìm bằng `grep -rn ": IFeatureController" tests`) phải có thêm ba phương thức mới, trả `DomainListState.Empty(true)` và `new(DomainListOutcome.Accepted)`.

- [ ] **Step 4: Chạy**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 && dotnet build -c Release -warnaserror`
Expected: PASS trừ 19 test chỉ dành cho Windows đã hỏng sẵn trên Mac, build 0 warning. Trên máy Windows (hoặc CI) chạy thêm `-f net8.0-windows`.

- [ ] **Step 5: Commit**
```bash
git add src/LunaDns.App tests/LunaDns.App.Tests
git commit -m "feat(app): expose the layer-2 domain list through feature controllers"
```

---

### Task 8: `DomainListUpdater`

**Files:**
- Create: `src/LunaDns.App/DomainList/DomainListUpdater.cs`
- Test: `tests/LunaDns.App.Tests/DomainList/DomainListUpdaterTests.cs`

**Interfaces:**
- Consumes: `IDomainListSink`, `DomainListVerifier.MaxBytes`, `AppInfo.Repository`, `AppInfo.Version`.
- Produces:
```csharp
internal enum DomainListCheckKind { NetworkError, TooLarge, Offered, Skipped }
internal sealed record DomainListCheck(DateTimeOffset At, DomainListCheckKind Kind, DomainListOfferResult? Offer = null);
internal sealed partial class DomainListUpdater
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30), Interval = TimeSpan.FromHours(24), RetryDelay = TimeSpan.FromHours(1);
    public DomainListUpdater(HttpClient http, Uri url, IDomainListSink sink, TimeProvider time, ILogger logger);
    public static Uri DefaultUrl(string repository);            // https://github.com/{repo}/releases/download/domain-list/domains.json
    public DomainListCheck? LastCheck { get; }                   // trong bộ nhớ, kể cả lượt lỗi
    public bool IsChecking { get; }
    public int ReapplyFailures { get; }                          // số lượt liên tiếp ReapplyFailed/SaveFailed
    public event EventHandler? Changed;                          // gọi từ luồng nền
    public Task RunAsync(CancellationToken cancellationToken);   // vòng hẹn giờ, hết khi huỷ
    public Task<DomainListCheck> CheckNowAsync(CancellationToken cancellationToken = default);
    internal Task<TimeSpan> TickAsync(CancellationToken cancellationToken);   // một vòng: kiểm nếu đến hạn, trả thời gian chờ
    internal static TimeSpan NextDelay(DomainListCheck check, int reapplyFailures);
}
```

- [ ] **Step 1: Test lỗi**

```csharp
using System.Net;
using LunaDns.App.DomainList;
using LunaDns.App.Features;
using LunaDns.Core.Domains;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests.DomainList;

public sealed class DomainListUpdaterTests
{
    private static readonly Uri Url = DomainListUpdater.DefaultUrl("owner/repo");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 7, 0, 0, TimeSpan.Zero));
    private readonly FakeSink _sink = new();
    private readonly FakeHandler _http = new();

    private DomainListUpdater Create() => new(new HttpClient(_http), Url, _sink, _time, NullLogger.Instance);

    [Fact]
    public void Default_url_points_at_the_domain_list_release() =>
        Assert.Equal("https://github.com/owner/repo/releases/download/domain-list/domains.json", Url.ToString());

    [Fact]
    public async Task Never_checked_downloads_and_offers()
    {
        _http.Body = [1, 2, 3];
        var wait = await Create().TickAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3], Assert.Single(_sink.Offered));
        Assert.Equal(DomainListUpdater.Interval, wait);
    }

    [Fact]
    public async Task Checked_recently_waits_until_due()
    {
        _sink.State = _sink.State with { CheckedAt = _time.GetUtcNow() - TimeSpan.FromHours(20) };
        var wait = await Create().TickAsync(CancellationToken.None);

        Assert.Empty(_sink.Offered);
        Assert.Equal(TimeSpan.FromHours(4), wait);
    }

    [Fact]
    public async Task Disabled_switch_never_downloads()
    {
        _sink.State = DomainListState.Empty(enabled: false);
        await Create().TickAsync(CancellationToken.None);
        Assert.Equal(0, _http.Calls);
    }

    [Fact]
    public async Task Old_daemon_never_downloads()
    {
        _sink.State = null;
        Assert.Equal(DomainListUpdater.RetryDelay, await Create().TickAsync(CancellationToken.None));
        Assert.Equal(0, _http.Calls);
    }

    [Fact]
    public async Task Network_error_retries_in_an_hour_and_is_remembered()
    {
        _http.Status = HttpStatusCode.ServiceUnavailable;
        var updater = Create();

        Assert.Equal(DomainListUpdater.RetryDelay, await updater.TickAsync(CancellationToken.None));
        Assert.Equal(DomainListCheckKind.NetworkError, updater.LastCheck!.Kind);
        Assert.Empty(_sink.Offered);
    }

    [Fact]
    public async Task Oversized_body_is_cut_and_not_offered()
    {
        _http.Body = new byte[DomainListVerifier.MaxBytes + 1];
        var check = await Create().CheckNowAsync();
        Assert.Equal(DomainListCheckKind.TooLarge, check.Kind);
        Assert.Empty(_sink.Offered);
    }

    [Fact]
    public async Task Check_now_ignores_the_24_hour_mark()
    {
        _sink.State = _sink.State with { CheckedAt = _time.GetUtcNow() };
        await Create().CheckNowAsync();
        Assert.Single(_sink.Offered);
    }

    [Theory]
    [InlineData(DomainListOutcome.Accepted, 0, 24)]
    [InlineData(DomainListOutcome.NotNewer, 0, 24)]
    [InlineData(DomainListOutcome.BadSignature, 0, 24)]
    [InlineData(DomainListOutcome.UnknownFormat, 0, 24)]
    [InlineData(DomainListOutcome.ReapplyFailed, 1, 1)]
    [InlineData(DomainListOutcome.ReapplyFailed, 2, 24)]
    [InlineData(DomainListOutcome.Unavailable, 0, 1)]
    public void Next_delay_follows_the_spec(DomainListOutcome outcome, int failures, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours),
            DomainListUpdater.NextDelay(new DomainListCheck(DateTimeOffset.UnixEpoch, DomainListCheckKind.Offered, new(outcome)), failures));

    [Fact]
    public async Task Run_waits_30_seconds_before_the_first_check()
    {
        using var cts = new CancellationTokenSource();
        var run = Create().RunAsync(cts.Token);
        _time.Advance(TimeSpan.FromSeconds(29));
        await Task.Yield();
        Assert.Equal(0, _http.Calls);
        _time.Advance(TimeSpan.FromSeconds(1));
        await Eventually.TrueAsync(() => _http.Calls == 1); // helper chờ có sẵn trong App.Tests; nếu chưa có thì vòng chờ 2 giây, mỗi lần 10 ms
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private sealed class FakeSink : IDomainListSink
    {
        public DomainListState? State { get; set; } = DomainListState.Empty(enabled: true);
        public List<byte[]> Offered { get; } = [];
        public DomainListOutcome Next { get; set; } = DomainListOutcome.Accepted;
        public Task<DomainListState?> GetDomainListAsync() => Task.FromResult(State);
        public Task<DomainListOfferResult> OfferDomainListAsync(byte[] file) { Offered.Add(file); return Task.FromResult(new DomainListOfferResult(Next)); }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public byte[] Body { get; set; } = [];
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) });
        }
    }
}
```

- [ ] **Step 2: Chạy, thấy lỗi**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 --filter DomainListUpdaterTests`
Expected: FAIL (biên dịch).

- [ ] **Step 3: Viết updater**

```csharp
using System.Net.Http.Headers;
using LunaDns.App.Features;
using LunaDns.Core.Domains;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.DomainList;

internal enum DomainListCheckKind
{
    NetworkError,
    TooLarge,
    Offered,
    /// <summary>Công tắc tắt hoặc dịch vụ nền không dùng được: không tải.</summary>
    Skipped,
}

internal sealed record DomainListCheck(DateTimeOffset At, DomainListCheckKind Kind, DomainListOfferResult? Offer = null);

/// <summary>
/// Tải domains.json theo lịch của spec D4: 30 giây sau khi mở app, rồi 24 giờ/lần theo checkedAt của nơi giữ thật;
/// lỗi mạng thì 1 giờ. Không tự xác minh: byte đi nguyên vẹn tới IDomainListSink (Windows: app; macOS: daemon).
/// Lượt lỗi chỉ nằm trong bộ nhớ. Một lượt tải tại một thời điểm.
/// </summary>
internal sealed partial class DomainListUpdater(HttpClient http, Uri url, IDomainListSink sink, TimeProvider time, ILogger logger)
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _single = new(1, 1);
    private int _reapplyFailures;
    private volatile DomainListCheck? _last;
    private volatile bool _checking;

    public static Uri DefaultUrl(string repository) => new($"https://github.com/{repository}/releases/download/domain-list/domains.json");

    public DomainListCheck? LastCheck => _last;

    public bool IsChecking => _checking;

    public int ReapplyFailures => _reapplyFailures;

    public event EventHandler? Changed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(StartupDelay, time, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var wait = await TickAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<TimeSpan> TickAsync(CancellationToken cancellationToken)
    {
        var state = await sink.GetDomainListAsync().ConfigureAwait(false);
        if (state is null || !state.Enabled) return RetryDelay;
        if (state.CheckedAt is { } checkedAt)
        {
            var due = checkedAt + Interval - time.GetUtcNow();
            if (due > TimeSpan.Zero) return due;
        }
        var check = await CheckNowAsync(cancellationToken).ConfigureAwait(false);
        return NextDelay(check, _reapplyFailures);
    }

    public async Task<DomainListCheck> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        await _single.WaitAsync(cancellationToken).ConfigureAwait(false);
        _checking = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var check = await CheckCoreAsync(cancellationToken).ConfigureAwait(false);
            _reapplyFailures = check.Offer?.Outcome is DomainListOutcome.ReapplyFailed or DomainListOutcome.SaveFailed ? _reapplyFailures + 1 : 0;
            _last = check;
            return check;
        }
        finally
        {
            _checking = false;
            _single.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static TimeSpan NextDelay(DomainListCheck check, int reapplyFailures) => check switch
    {
        { Kind: DomainListCheckKind.NetworkError or DomainListCheckKind.TooLarge or DomainListCheckKind.Skipped } => RetryDelay,
        { Offer.Outcome: DomainListOutcome.ReapplyFailed or DomainListOutcome.SaveFailed } => reapplyFailures >= 2 ? Interval : RetryDelay,
        { Offer.Outcome: DomainListOutcome.Unavailable or DomainListOutcome.Disabled } => RetryDelay,
        _ => Interval,
    };

    private async Task<DomainListCheck> CheckCoreAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        byte[]? body;
        try
        {
            body = await DownloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogDownloadFailed(logger, ex.GetType().Name);
            return new(now, DomainListCheckKind.NetworkError);
        }
        if (body is null) return new(now, DomainListCheckKind.TooLarge);
        var offer = await sink.OfferDomainListAsync(body).ConfigureAwait(false);
        return new(now, DomainListCheckKind.Offered, offer);
    }

    /// <summary>Đọc tối đa MaxBytes + 1 byte; vượt thì null. Không tin Content-Length.</summary>
    private async Task<byte[]?> DownloadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LunaDNS", AppInfo.Version.ToString()));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[DomainListVerifier.MaxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0)
            total += read;
        return total > DomainListVerifier.MaxBytes ? null : buffer[..total];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Không tải được danh sách tên miền ({Reason}); thử lại sau.")]
    private static partial void LogDownloadFailed(ILogger logger, string reason);
}
```
`EnsureSuccessStatusCode` ném `HttpRequestException`, nên HTTP ≠ 200 rơi vào nhánh `NetworkError`. Kiểm `AppInfo.Version` có kiểu `Version` (xem `AppInfo.cs`); nếu là chuỗi thì bỏ `.ToString()`.

- [ ] **Step 4: Chạy**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 --filter DomainListUpdaterTests`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add src/LunaDns.App/DomainList tests/LunaDns.App.Tests/DomainList
git commit -m "feat(app): download the domain list on a 24-hour schedule"
```

---

### Task 9: Giao diện: dòng "Tự cập nhật danh sách", nguồn trong [Xem], số đếm, ghi chú Tuỳ chỉnh, chẩn đoán

**Files:**
- Create: `src/LunaDns.App/ViewModels/DomainListRowViewModel.cs`
- Modify: `src/LunaDns.App/ViewModels/MainViewModel.cs` (khoảng dòng 48, 100, 410-440, 616-673), `DomainGroupViewModel.cs:61-69`, `CustomDomainsViewModel.cs:50`, `IUserPrompts.cs:11`, `src/LunaDns.App/Views/ConfirmDialog.cs:65`, `DomainListWindow.axaml(.cs)`, `MainWindow.axaml` (khu Tên miền), `Strings.resx`, `Strings.cs`, `src/LunaDns.Core/Diagnostics/DiagnosticInput.cs`, `DiagnosticReport.cs`, `src/LunaDns.App/ReportWriter.cs`
- Test: `tests/LunaDns.App.Tests/ViewModels/DomainListRowViewModelTests.cs` (mới); thêm vào các test hiện có của `DomainGroupViewModel`, `CustomDomainsViewModel`, `MainViewModel`, `StringsTests`; `tests/LunaDns.Core.Tests/Diagnostics/DiagnosticReportTests.cs`

**Interfaces:**
- Consumes: `DomainListUpdater` (`LastCheck`, `IsChecking`, `Changed`, `CheckNowAsync`), `AppServices.GetDomainListAsync/SetDomainListEnabledAsync/CatalogAsync`, `EffectiveCatalog`, `CatalogEntry`.
- Produces:
```csharp
internal sealed class DomainListRowViewModel : ObservableObject
{
    public DomainListRowViewModel(Func<Task<DomainListState?>> state, Func<bool, Task<DomainListOfferResult>> setEnabled,
        Func<CancellationToken, Task<DomainListCheck>> checkNow, Func<DomainListCheck?> lastCheck, Func<bool> isChecking,
        Func<int> reapplyFailures, Func<bool> busyElsewhere, TimeProvider time, Action<Action>? post = null, Action<Exception>? onError = null);
    public bool IsVisible { get; }          // false khi state == null (daemon cũ)
    public bool IsEnabled { get; }          // công tắc
    public AsyncCommand ToggleCommand { get; }
    public AsyncCommand CheckNowCommand { get; }
    public string StatusText { get; }
    public Tone? StatusTone { get; }        // Error cho "Không áp được…"
    public string? ResultText { get; }      // dòng tạm 6 giây
    public event EventHandler? CatalogChanged;   // MainViewModel dựng lại catalog và các dòng nhóm
    public Task RefreshAsync();
    public Task OnBackgroundCheckAsync();   // sau mỗi lượt tải nền (DomainListUpdater.Changed)
}
// IUserPrompts:
Task ShowDomainListAsync(string title, IReadOnlyList<CatalogEntry> entries, DateTimeOffset? issued);
// DomainGroupViewModel: thêm tham số Func<EffectiveCatalog> catalog (sau current)
// CustomDomainsViewModel: thêm tham số EffectiveCatalog catalog (sau initial); CustomDomainRowViewModel.AlsoInGroup : string?
// DiagnosticInput: thêm DomainListState? DomainList = null (cuối, sau Labels)
```

- [ ] **Step 1: Chuỗi (resx + Strings.cs) và test chuỗi**

Thêm vào `Strings.resx` (kèm thuộc tính tương ứng trong `Strings.cs`, theo mẫu `GroupCountFormat`):

| Khoá | Giá trị |
|---|---|
| `DomainListSwitch` | Tự cập nhật danh sách tên miền |
| `DomainListCheckNow` | Cập nhật ngay |
| `DomainListOff` | Đang dùng danh sách có sẵn |
| `DomainListNone` | Chưa tải bản nào |
| `DomainListChecking` | Đang kiểm… |
| `DomainListOkFormat` | Bản {0:dd/MM} · kiểm lúc {1:HH:mm} |
| `DomainListFailedKeepFormat` | Không kiểm được lúc {0:HH:mm}, giữ bản {1:dd/MM} |
| `DomainListFailedBuiltinFormat` | Không kiểm được lúc {0:HH:mm}, dùng danh sách có sẵn |
| `DomainListReapplyFailedFormat` | Không áp được bản {0:dd/MM}, xem Chẩn đoán |
| `DomainListAddedFormat` | Đã thêm {0} tên miền |
| `DomainListUpToDate` | Đã là bản mới nhất |
| `DomainListBadSignature` | Chữ ký không hợp lệ, giữ bản cũ |
| `DomainListNeedsNewerApp` | Cần bản LunaDNS mới hơn để đọc danh sách này |
| `DomainListDownloadFailed` | Không tải được, thử lại sau |
| `GroupCountWithUpdateFormat` | {0} tên miền ({1} từ bản cập nhật) |
| `DomainSourceBuiltin` | Có sẵn |
| `DomainSourceUpdateFormat` | Bản cập nhật {0:dd/MM} |
| `CustomAlsoInGroupFormat` | Cũng có trong nhóm {0} |

`StringsTests`: thêm từng khoá vào danh sách khoá phải có giá trị (theo mẫu test hiện có).

- [ ] **Step 2: Test lỗi cho `DomainListRowViewModel`**

```csharp
using LunaDns.App.DomainList;
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests.ViewModels;

public sealed class DomainListRowViewModelTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 7, 0, 0, TimeSpan.Zero));
    private DomainListState? _state = DomainListState.Empty(true);
    private DomainListCheck? _last;
    private DomainListCheck _nextCheck = new(DateTimeOffset.UnixEpoch, DomainListCheckKind.Offered, new(DomainListOutcome.NotNewer));
    private readonly List<bool> _switched = [];
    private int _failures;

    private DomainListRowViewModel Create() => new(
        () => Task.FromResult(_state),
        on => { _switched.Add(on); _state = _state! with { Enabled = on }; return Task.FromResult(new DomainListOfferResult(DomainListOutcome.Accepted)); },
        _ => Task.FromResult(_last = _nextCheck),
        () => _last, () => false, () => _failures, () => false, _time);

    [Fact]
    public async Task Old_daemon_hides_the_row()
    {
        _state = null;
        var vm = Create();
        await vm.RefreshAsync();
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public async Task Never_downloaded_says_so()
    {
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.DomainListNone, vm.StatusText);
    }

    [Fact]
    public async Task Switched_off_says_builtin_list()
    {
        _state = DomainListState.Empty(false);
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.DomainListOff, vm.StatusText);
    }

    [Fact]
    public async Task Ok_state_shows_issue_date_and_check_time()
    {
        var local = _time.GetLocalNow();
        _state = new DomainListState(true, 5, local.AddDays(-1), local, new Dictionary<DomainGroup, IReadOnlyList<string>>());
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.Format(nameof(Strings.DomainListOkFormat), local.AddDays(-1), local), vm.StatusText);
    }

    [Fact]
    public async Task Failed_check_after_success_keeps_old_version()
    {
        var local = _time.GetLocalNow();
        _state = new DomainListState(true, 5, local.AddDays(-1), local.AddHours(-30), new Dictionary<DomainGroup, IReadOnlyList<string>>());
        _last = new DomainListCheck(local.ToUniversalTime(), DomainListCheckKind.NetworkError);
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.Format(nameof(Strings.DomainListFailedKeepFormat), local, local.AddDays(-1)), vm.StatusText);
    }

    [Theory]
    [InlineData(DomainListOutcome.NotNewer, nameof(Strings.DomainListUpToDate))]
    [InlineData(DomainListOutcome.BadSignature, nameof(Strings.DomainListBadSignature))]
    [InlineData(DomainListOutcome.UnknownFormat, nameof(Strings.DomainListNeedsNewerApp))]
    public async Task Check_now_shows_a_temporary_result(DomainListOutcome outcome, string key)
    {
        _nextCheck = new(DateTimeOffset.UnixEpoch, DomainListCheckKind.Offered, new(outcome));
        var vm = Create();
        await vm.RefreshAsync();
        await vm.CheckNowCommand.ExecuteAsync();
        Assert.Equal((string)typeof(Strings).GetProperty(key)!.GetValue(null)!, vm.ResultText);
        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(vm.ResultText);
    }

    [Fact]
    public async Task Accepted_check_reports_count_and_raises_catalog_changed()
    {
        _nextCheck = new(DateTimeOffset.UnixEpoch, DomainListCheckKind.Offered, new(DomainListOutcome.Accepted, Added: 3));
        var vm = Create();
        var raised = 0;
        vm.CatalogChanged += (_, _) => raised++;
        await vm.RefreshAsync();
        await vm.CheckNowCommand.ExecuteAsync();
        Assert.Equal(Strings.Format(nameof(Strings.DomainListAddedFormat), 3), vm.ResultText);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Toggle_calls_the_service_and_raises_catalog_changed()
    {
        var vm = Create();
        var raised = 0;
        vm.CatalogChanged += (_, _) => raised++;
        await vm.RefreshAsync();
        await vm.ToggleCommand.ExecuteAsync();
        Assert.Equal([false], _switched);
        Assert.False(vm.IsEnabled);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Two_reapply_failures_show_the_red_line()
    {
        var local = _time.GetLocalNow();
        _state = new DomainListState(true, 5, local, local, new Dictionary<DomainGroup, IReadOnlyList<string>>());
        _failures = 2;
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.Format(nameof(Strings.DomainListReapplyFailedFormat), local), vm.StatusText);
        Assert.Equal(Tone.Error, vm.StatusTone);
    }

    [Fact]
    public async Task Check_now_is_disabled_while_switched_off()
    {
        _state = DomainListState.Empty(false);
        var vm = Create();
        await vm.RefreshAsync();
        Assert.False(vm.CheckNowCommand.CanExecute(null));
    }
}
```
(`AsyncCommand.ExecuteAsync`: dùng đúng tên phương thức có sẵn trong `AsyncCommand.cs`. Giờ địa phương luôn đổi qua `TimeProvider.LocalTimeZone`, nên test với `FakeTimeProvider` (UTC) không phụ thuộc múi giờ của máy.)

- [ ] **Step 3: Chạy, thấy lỗi**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0 --filter DomainListRowViewModelTests`
Expected: FAIL (biên dịch).

- [ ] **Step 4: Viết `DomainListRowViewModel`**

```csharp
using LunaDns.App.DomainList;
using LunaDns.Core.Domains;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Dòng "Tự cập nhật danh sách tên miền" ở khu Tên miền (spec D4 "Giao diện"). Windows và macOS.
/// Trạng thái lấy từ nơi giữ thật (state) cộng lượt kiểm trong bộ nhớ (lastCheck). Kết quả "Cập nhật ngay" hiện 6 giây.
/// </summary>
internal sealed class DomainListRowViewModel : ObservableObject
{
    private static readonly TimeSpan ResultDuration = TimeSpan.FromSeconds(6);

    private readonly Func<Task<DomainListState?>> _state;
    private readonly Func<bool, Task<DomainListOfferResult>> _setEnabled;
    private readonly Func<CancellationToken, Task<DomainListCheck>> _checkNow;
    private readonly Func<DomainListCheck?> _lastCheck;
    private readonly Func<bool> _isChecking;
    private readonly Func<int> _reapplyFailures;
    private readonly Func<bool> _busyElsewhere;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private DomainListState? _current;
    private string? _result;
    private ITimer? _resultTimer;

    public DomainListRowViewModel(Func<Task<DomainListState?>> state, Func<bool, Task<DomainListOfferResult>> setEnabled,
        Func<CancellationToken, Task<DomainListCheck>> checkNow, Func<DomainListCheck?> lastCheck, Func<bool> isChecking,
        Func<int> reapplyFailures, Func<bool> busyElsewhere, TimeProvider time, Action<Action>? post = null, Action<Exception>? onError = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
        _checkNow = checkNow ?? throw new ArgumentNullException(nameof(checkNow));
        _lastCheck = lastCheck ?? throw new ArgumentNullException(nameof(lastCheck));
        _isChecking = isChecking ?? throw new ArgumentNullException(nameof(isChecking));
        _reapplyFailures = reapplyFailures ?? throw new ArgumentNullException(nameof(reapplyFailures));
        _busyElsewhere = busyElsewhere ?? throw new ArgumentNullException(nameof(busyElsewhere));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _post = post ?? (a => a());
        onError ??= _ => { };
        ToggleCommand = new AsyncCommand(ToggleAsync, onError);
        CheckNowCommand = new AsyncCommand(CheckNowAsync, onError, () => IsEnabled && !_isChecking() && !_busyElsewhere());
    }

    public event EventHandler? CatalogChanged;

    public AsyncCommand ToggleCommand { get; }

    public AsyncCommand CheckNowCommand { get; }

    public bool IsVisible => _current is not null;

    public bool IsEnabled => _current?.Enabled ?? false;

    public string? ResultText => _result;

    /// <summary>Chạy lại tính năng lỗi 2 lượt liên tiếp: dòng đỏ, chờ 24 giờ (spec D4 "Áp dụng bản mới").</summary>
    private bool IsReapplyStuck => _reapplyFailures() >= 2;

    public Tone? StatusTone => IsEnabled && IsReapplyStuck ? Tone.Error : null;

    public string StatusText
    {
        get
        {
            if (_current is not { } s) return string.Empty;
            if (!s.Enabled) return Strings.DomainListOff;
            if (_isChecking()) return Strings.DomainListChecking;
            if (IsReapplyStuck && s.Issued is { } stuck) return Strings.Format(nameof(Strings.DomainListReapplyFailedFormat), Local(stuck));
            if (_lastCheck() is { } last && Failed(last) && (s.CheckedAt is null || last.At > s.CheckedAt))
                return s.Issued is { } kept
                    ? Strings.Format(nameof(Strings.DomainListFailedKeepFormat), Local(last.At), Local(kept))
                    : Strings.Format(nameof(Strings.DomainListFailedBuiltinFormat), Local(last.At));
            if (s.Issued is { } issued && s.CheckedAt is { } checkedAt)
                return Strings.Format(nameof(Strings.DomainListOkFormat), Local(issued), Local(checkedAt));
            return Strings.DomainListNone;
        }
    }

    public async Task RefreshAsync()
    {
        var state = await _state().ConfigureAwait(true);
        _post(() =>
        {
            _current = state;
            RaiseAll();
        });
    }

    /// <summary>Gọi trên luồng UI khi lượt tải nền xong (DomainListUpdater.Changed).</summary>
    public async Task OnBackgroundCheckAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        if (_lastCheck()?.Offer?.Outcome == DomainListOutcome.Accepted) CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ToggleAsync()
    {
        if (_current is null) return;
        var result = await _setEnabled(!_current.Enabled).ConfigureAwait(true);
        if (!result.Success) ShowResult(Describe(result));
        await RefreshAsync().ConfigureAwait(true);
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task CheckNowAsync()
    {
        var check = await _checkNow(CancellationToken.None).ConfigureAwait(true);
        ShowResult(check.Kind == DomainListCheckKind.Offered ? Describe(check.Offer!) : Strings.DomainListDownloadFailed);
        await RefreshAsync().ConfigureAwait(true);
        if (check.Offer?.Outcome == DomainListOutcome.Accepted) CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _time.LocalTimeZone);

    private static bool Failed(DomainListCheck check) =>
        check.Kind is DomainListCheckKind.NetworkError or DomainListCheckKind.TooLarge || check.Offer?.Success == false;

    private static string Describe(DomainListOfferResult result) => result.Outcome switch
    {
        DomainListOutcome.Accepted => Strings.Format(nameof(Strings.DomainListAddedFormat), result.Added),
        DomainListOutcome.NotNewer => Strings.DomainListUpToDate,
        DomainListOutcome.UnknownFormat => Strings.DomainListNeedsNewerApp,
        DomainListOutcome.Malformed or DomainListOutcome.UnknownKey or DomainListOutcome.BadSignature or DomainListOutcome.Downgrade
            => Strings.DomainListBadSignature,
        DomainListOutcome.ReapplyFailed or DomainListOutcome.SaveFailed => ErrorMessages.For(result.Error),
        _ => Strings.DomainListDownloadFailed,
    };

    private void ShowResult(string text)
    {
        _resultTimer?.Dispose();
        _result = text;
        OnPropertyChanged(nameof(ResultText));
        _resultTimer = _time.CreateTimer(_ => _post(() =>
        {
            _result = null;
            OnPropertyChanged(nameof(ResultText));
        }), null, ResultDuration, Timeout.InfiniteTimeSpan);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTone));
        CheckNowCommand.RaiseCanExecuteChanged();
    }
}
```
Tên `OnPropertyChanged`, `RaiseCanExecuteChanged` và tham số `canExecute` của `AsyncCommand` lấy theo `ObservableObject.cs`/`AsyncCommand.cs` hiện có. `ErrorMessages.For` là hàm `LocalFeatureController` đang dùng. `DomainListRowViewModel` giữ `ITimer`: nếu analyzer đòi `IDisposable` (CA1001) thì cho lớp implement `IDisposable`, và `MainViewModel` dispose nó cùng lúc với các VM khác.

- [ ] **Step 5: Nối vào `MainViewModel`, nhóm, [Xem], Tuỳ chỉnh**

`MainViewModel`:
- Field: `private EffectiveCatalog _catalog = EffectiveCatalog.Builtin;` và `private readonly DomainListUpdater _domainListUpdater;`. Tạo updater trong ctor: `new DomainListUpdater(DomainListHttp, DomainListUpdater.DefaultUrl(AppInfo.Repository), _services.Features, TimeProvider.System, _services.LoggerFactory.CreateLogger<DomainListUpdater>())`, với `private static readonly HttpClient DomainListHttp = new() { Timeout = TimeSpan.FromSeconds(15) };`. `_services.Features` là `IFeatureController`, mà interface này kế thừa `IDomainListSink`.
- `public DomainListRowViewModel DomainList { get; }`. Tạo với `_services.GetDomainListAsync`, `_services.SetDomainListEnabledAsync`, `_domainListUpdater.CheckNowAsync`, `() => _domainListUpdater.LastCheck`, `() => _domainListUpdater.IsChecking`, `() => _domainListUpdater.ReapplyFailures`, `() => IsBusy` (cờ bận chung hiện có của `RunBusyAsync`; dùng đúng tên trong file), `TimeProvider.System`, `a => Dispatcher.UIThread.Post(a)`, `OnError`.
- `DomainList.CatalogChanged += async (_, _) => await ReloadCatalogAsync();`, với:
```csharp
    private async Task ReloadCatalogAsync()
    {
        _catalog = await Task.Run(_services.CatalogAsync);
        foreach (var row in DomainGroups) row.Refresh();
        OnPropertyChanged(nameof(RoutedCountText));
    }
```
- `_domainListUpdater.Changed += (_, _) => Dispatcher.UIThread.Post(() => _ = DomainList.OnBackgroundCheckAsync());`
- Trong `Start()` (cạnh `_ = CheckUpdateSilentlyAsync();`, dòng ~415): gọi `_ = ReloadCatalogAsync(); _ = DomainList.RefreshAsync(); _ = RunDomainListUpdaterAsync();`, với `RunDomainListUpdaterAsync` gọi `_domainListUpdater.RunAsync(_shutdown.Token)` và nuốt `OperationCanceledException`. Dùng `CancellationTokenSource` hiện có khi đóng cửa sổ; nếu chưa có thì thêm `_lifetime` và huỷ trong `Dispose`/đóng app.
- `RoutedCountText`: `FeatureDomains.Routed(_domains, _catalog).Count`.
- Dòng 424: `new DomainGroupViewModel(group, () => _domains, () => _catalog, ApplyDomainsAsync, ...)`.
- Dòng 436: `await _prompts.ShowDomainListAsync(row.Title, _catalog.Entries(row.Group), _catalog.Issued);`
- Dòng 439: `new CustomDomainsViewModel(_domains, _catalog, ApplyDomainsAsync, ...)`.

`DomainGroupViewModel`: thêm tham số `Func<EffectiveCatalog> catalog` sau `current`. `CountText` cho nhóm dựng sẵn:
```csharp
            var c = _catalog();
            var added = c.Added(Group).Count;
            return added == 0
                ? Strings.Format(nameof(Strings.GroupCountFormat), c.For(Group).Count)
                : Strings.Format(nameof(Strings.GroupCountWithUpdateFormat), c.For(Group).Count, added);
```
Test thêm: catalog có 3 tên miền lớp 2 thì `CountText == "14 tên miền (3 từ bản cập nhật)"` (11 + 3, theo danh sách Steam hiện tại).

`CustomDomainsViewModel`: thêm `EffectiveCatalog catalog` sau `initial`. `_enabledCatalog = [.. DomainCatalog.Selectable.Where(initial.IsEnabled).SelectMany(catalog.For)];` để kiểm `Duplicate` có tính cả lớp 2. Mỗi `CustomDomainRowViewModel` có thêm `AlsoInGroup` (string?): tên nhóm dựng sẵn đang bật đầu tiên mà `DomainMatcher.Matches(row.Domain, catalog.For(g))`. Trong XAML, `AlsoInGroup` hiện dưới dòng thành chữ xám (`CustomAlsoInGroupFormat`). Test thêm:
- dòng Tuỳ chỉnh `cdn.example.net` với catalog có `cdn.example.net` ở Steam thì `AlsoInGroup == "Steam"` và dòng vẫn còn;
- thêm mới `cdn.example.net` thì bị báo `Duplicate`.

`IUserPrompts.ShowDomainListAsync(string title, IReadOnlyList<CatalogEntry> entries, DateTimeOffset? issued)`. `ConfirmDialog` truyền xuống `DomainListWindow(title, entries, issued)`. `DomainListWindow` hiện hai cột: tên miền, và nguồn (`DomainSourceBuiltin` hoặc `DomainSourceUpdateFormat` với `issued`). Sửa mọi lớp giả của `IUserPrompts` trong tests.

`MainWindow.axaml`: trong khu Tên miền, ngay sau `ItemsControl` của `DomainGroups`, thêm (dùng style/màu sẵn có của dòng nhóm, theme chỉ-tối):
```xml
<Grid ColumnDefinitions="Auto,*,Auto" IsVisible="{Binding DomainList.IsVisible}" Margin="0,8,0,0">
  <CheckBox Grid.Column="0" Content="{x:Static app:Strings.DomainListSwitch}"
            IsChecked="{Binding DomainList.IsEnabled, Mode=OneWay}" Command="{Binding DomainList.ToggleCommand}" />
  <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
    <TextBlock Text="{Binding DomainList.StatusText}" Classes="muted"
               Classes.error="{Binding DomainList.StatusTone, Converter={x:Static views:ToneConverters.IsError}}" />
    <TextBlock Text="{Binding DomainList.ResultText}" IsVisible="{Binding DomainList.ResultText, Converter={x:Static ObjectConverters.IsNotNull}}" />
  </StackPanel>
  <Button Grid.Column="2" Content="{x:Static app:Strings.DomainListCheckNow}" Command="{Binding DomainList.CheckNowCommand}" />
</Grid>
```
(Tên prefix XAML `app:`/`views:`, class `muted`/`error` và converter lấy đúng theo `MainWindow.axaml` hiện có. Ô tích dùng mẫu OneWay + Command như `DomainGroupViewModel`.)

- [ ] **Step 6: Chẩn đoán**

`DiagnosticInput`: thêm `DomainListState? DomainList = null` vào cuối. `DiagnosticReport.Build`: thêm mục ngay sau "Kết quả kiểm tra":
```csharp
        Section(sb, "Danh sách tên miền cập nhật");
        if (input.DomainList is not { } list) sb.AppendLine("(không có)");
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Công tắc: {(list.Enabled ? "bật" : "tắt")} | serial: {list.Serial} | bản: {list.Issued:O} | kiểm: {list.CheckedAt:O}");
            foreach (var (group, domains) in list.Added) sb.AppendLine(CultureInfo.InvariantCulture, $"{group}: {domains.Count} tên miền lớp 2");
        }
```
Test `DiagnosticReportTests`: có `DomainList` thì báo cáo chứa `"serial: 5"` và `"Steam: 1 tên miền lớp 2"`; không có thì chứa `"(không có)"`.

`ReportWriter.Write`: nhận thêm `DomainListState? domainList = null` và truyền vào `DiagnosticInput`. Nơi gọi (`Maintenance`/`MainViewModel`) truyền `await _services.GetDomainListAsync()`. Lượt tải cuối nằm trong log (`LogDownloadFailed`, `LogRejected`), nên đã có trong "200 dòng log cuối".

- [ ] **Step 7: Chạy toàn bộ**

Run: `dotnet build -c Release -warnaserror && dotnet test tests/LunaDns.Core.Tests && dotnet test tests/LunaDns.App.Tests -f net8.0 && dotnet test tests/LunaDns.Platform.MacOS.Tests`
Expected: 0 warning. Mọi test PASS, trừ 19 test chỉ dành cho Windows đã hỏng sẵn trong `LunaDns.App.Tests` trên Mac (so số lỗi với `main` trước khi đổi: phải đúng 19).

- [ ] **Step 8: Commit**
```bash
git add src tests
git commit -m "feat(app): show layer-2 status, sources and the auto-update switch"
```

---

### Task 10: Tài liệu, việc của PO, thử tay, PR

**Files:**
- Modify: `docs/dev/releasing.md`, `docs/dev/releasing.vi.md`, `docs/guide/09-macos.md`, trang hướng dẫn tên miền (`docs/guide/10-ten-mien.md` nếu đã có từ lớp 3; nếu chưa thì thêm mục vào `03-ba-cong-tac.md`), bản `docs/guide/en/` tương ứng nếu trang đó có bản tiếng Anh
- Modify (PO): `src/LunaDns.Core/Domains/DomainListKeys.cs`

- [ ] **Step 1: `docs/dev/releasing.md` (en) và `releasing.vi.md`: thêm mục "Domain list (layer 2)"**

Nội dung (bản vi; bản en dịch tương ứng):
```markdown
## Danh sách tên miền (lớp 2)

Một lần:
1. `dotnet run --project tools/DomainListSigner -- keygen --out ~/.lunadns/domain-list-2026a.pem`
   (đặt mật khẩu; sao lưu file PEM ngoại tuyến). Dán khoá công khai in ra vào `DomainListKeys.Production`
   với keyId `2026a`, mở PR, merge, phát hành bản app có khoá này.
2. `gh release create domain-list --prerelease --title "Domain list" --notes "Signed domain list for LunaDNS (layer 2)."`
   Không đánh dấu *latest*. Tag không có dạng vX.Y.Z nên trình kiểm cập nhật app bỏ qua.

Mỗi lần cập nhật:
1. Sửa `data/domain-list.json`, mở PR (CI chạy `verify-source`), merge.
2. `dotnet run --project tools/DomainListSigner -- sign --key ~/.lunadns/domain-list-2026a.pem --key-id 2026a --in data/domain-list.json --out artifacts/domains.json`
3. `gh release upload domain-list artifacts/domains.json --clobber`
4. Tải lại và kiểm: `gh release download domain-list -p domains.json -D /tmp/dl --clobber && dotnet run --project tools/DomainListSigner -- verify /tmp/dl/domains.json`

Thu hồi một tên miền thêm nhầm: xoá khỏi `data/domain-list.json` rồi ký và upload lại (serial mới cao hơn).
Lộ khoá: phát hành bản app bỏ keyId cũ và thêm khoá mới.
```

- [ ] **Step 2: Hướng dẫn người dùng**

Trang tên miền: thêm mục "Tự cập nhật danh sách": ba lớp là gì, công tắc, Cập nhật ngay, nghĩa của từng dòng trạng thái, và "tên miền bạn tự thêm không bao giờ bị ghi đè". `09-macos.md`: dòng này có trên macOS, và cần cài lại dịch vụ nền nếu dòng không hiện.

- [ ] **Step 3: Commit tài liệu, mở PR**

```bash
git add docs
git commit -m "docs: describe the signed domain list and how to publish it"
git push -u origin feat/d4-domain-layers
gh pr create --title "D4: signed layer-2 domain list updates" --body "<tóm tắt + test plan bên dưới, tiếng Anh, không có dòng Generated>"
```
Chờ CI xanh trên **cả** Windows và macOS. Không merge thẳng vào `main`.

- [ ] **Step 4: Việc của PO (sau khi PR code đã merge, hoặc ngay trong PR nếu PO muốn có khoá trong cùng bản)**

1. Chạy `keygen` (Step 1 ở trên), và dán khoá vào `DomainListKeys.Production` bằng một PR riêng `chore: embed domain list signing key 2026a`.
2. Tạo release `domain-list`, ký và upload bản đầu tiên.

- [ ] **Step 5: Thử tay**

- Windows 10/11 (sandbox `build/sandbox/LunaDNS.wsb`), bản build có khoá thật:
  - mở app, chờ khoảng 30 giây, rồi xem dòng trạng thái ("Bản dd/MM · kiểm lúc HH:mm");
  - [Xem] Steam có cột nguồn;
  - bật Định tuyến DNS, xem `Get-DnsClientNrptRule` có tên miền lớp 2;
  - tắt công tắc thì luật lớp 2 biến mất;
  - ngắt mạng rồi bấm Cập nhật ngay, thấy "Không tải được, thử lại sau";
  - sửa tay `%ProgramData%\LunaDNS\domain-list.json` (đổi một ký tự trong envelope), mở lại app: chạy với lớp 1, log có cảnh báo.
- macOS arm64:
  - như trên, kiểm `/etc/resolver/<tên miền lớp 2>`;
  - chạy app mới với daemon cũ: dòng bị ẩn, banner "Cần cập nhật dịch vụ nền";
  - `sudo cat "/Library/Application Support/LunaDNS/settings.json"` có `DomainListUpdates`.
- Ghi kết quả vào phần "Test plan" của PR.

## Summary (dùng cho mô tả PR, tiếng Anh)

- Built-in domain groups can now receive extra domains from a signed `domains.json` (ECDSA P-256) published on the `domain-list` GitHub release, without an app release. Layer 1 stays built in, and user-added custom domains are never touched.
- The app downloads the list 30 s after launch and every 24 h. The privileged side (the app on Windows, the root daemon on macOS) verifies the signature, serial and domain policy, reapplies running features, and only then saves.
- New "Auto-update domain list" row with a switch and an "Update now" button in the Domains section, on Windows and macOS. Group view shows where each domain comes from.
- New daemon commands `get-domain-list`, `set-domain-list`, `set-domain-list-enabled`; `MaxLineBytes` raised to 128 KB.
- `tools/DomainListSigner` signs on the maintainer's machine; CI checks `data/domain-list.json`.

## Test plan

- [ ] CI green on Windows and macOS
- [ ] Windows sandbox: status row, group sources, NRPT rules follow the switch, offline "Update now", tampered state file falls back to layer 1
- [ ] macOS arm64: same checks with `/etc/resolver`; old daemon hides the row
