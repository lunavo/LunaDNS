# KH17: Journal hỏng không để sót thay đổi (B5 + F7)

Spec: [2026-11-22-lunadns-orphan-sweep-design.md](../specs/2026-11-22-lunadns-orphan-sweep-design.md).

Lệnh kiểm (máy Mac):

```bash
~/.dotnet/dotnet build -warnaserror
~/.dotnet/dotnet test tests/LunaDns.Core.Tests
~/.dotnet/dotnet test tests/LunaDns.Platform.MacOS.Tests
~/.dotnet/dotnet test tests/LunaDns.App.Tests -f net8.0
```

Phần Windows (NRPT, `CleanupCommand`) chỉ build và chạy test trên CI Windows.

## T1. Core: `Load` không ném

1. Test trước (`JsonFileJournalStoreTests`): file là `null`, `[null]`, mục thiếu `Op` → rỗng và cách ly; file không đọc
   được (thư mục trùng tên, quyền 000 trên Unix) → rỗng, không ném; cách ly hai lần trong một giây không ném (`-1`).
2. Code: `JsonFileJournalStore(path, ILogger? logger = null)`.

## T2. Core: `JournalCoverage`, `HostsEditor.RemoveOrphans`, `OrphanSweeper`

1. Test trước: coverage lấy marker/URL/dòng hosts từ mục còn lại, snapshot hỏng → loại đó `Unknown`;
   `RemoveOrphans` gỡ đúng dòng LunaDNS không thuộc coverage, giữ dòng lạ (`1.2.3.4 x # LunaDNS`, `# LunaDNS` giữa
   dòng, CRLF/LF, UTF-16 bỏ qua); `OrphanSweeper.Sweep` gọi configurator với coverage đúng, có gỡ thì xả cache.
2. Code: `INetworkConfigurator.SweepOrphans(JournalCoverage)`, `OrphanSweepResult`, `OrphanSweeper`.
   Fake trong Core.Tests và App.Tests ghi lại lần gọi.

## T3. macOS: resolver, PAC, hosts

1. Test trước (`ResolverDnsRouterTests` thư mục tạm, `NetworkSetupAutoProxyTests` với `FakeNetworkSetup`,
   `MacNetworkConfiguratorTests`): file mang dấu lạ id bị xoá, dấu thuộc coverage giữ, file không dấu/dấu phần mềm khác
   giữ, symlink giữ; service PAC đúng URL LunaDNS bị tắt, URL thuộc coverage giữ, URL loopback khác cổng/đường dẫn giữ,
   service đã tắt auto-proxy không đụng; một loại lỗi không chặn loại khác.
2. Code: `ResolverDnsRouter.SweepOrphans`, `NetworkSetupAutoProxy.SweepOrphans`, `MacNetworkConfigurator.SweepOrphans`.

## T4. Windows: NRPT, hosts

1. Test trước (`NrptDnsRouterTests` với store giả): luật `LunaDNS <id>` lạ bị xoá, luật thuộc coverage giữ, luật không
   Comment (Tailscale) giữ, Comment đúng bằng `LunaDNS ` giữ.
2. Code: `NrptDnsRouter.SweepOrphans`, `WindowsNetworkConfigurator.SweepOrphans` (PAC không quét).

## T5. Nối vào khởi động

1. Test trước: `LocalFeatureController` quét sau hoàn tác, trước `ResumeAsync`; journal hỏng vẫn dựng được và quét;
   `CleanupCommand` quét cả khi không có journal, quét lỗi → mã 1; `Engine` (thử qua `DaemonTests` nếu dựng được).
2. Code: `Engine`, `LocalFeatureController`, `CleanupCommand.RollbackJournal`.

## T6. Tài liệu

`docs/guide/07-go-va-don-dep.md`, `08-su-co.md`, `09-macos.md` và bản `en/`; `06-dong-lenh.md` (dòng `--cleanup`
không dọn NRPT mồ côi).
