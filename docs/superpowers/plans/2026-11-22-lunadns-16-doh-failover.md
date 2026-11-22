# KH16: DoH chọn máy khoẻ, nối IP so le (B3 + F4 + B9)

Spec: [2026-11-22-lunadns-doh-failover-design.md](../specs/2026-11-22-lunadns-doh-failover-design.md).

Lệnh kiểm (máy Mac):

```bash
~/.dotnet/dotnet build -warnaserror
~/.dotnet/dotnet test tests/LunaDns.Core.Tests
~/.dotnet/dotnet test tests/LunaDns.Platform.MacOS.Tests
~/.dotnet/dotnet test tests/LunaDns.App.Tests -f net8.0
```

(App.Tests có 19 test chỉ chạy trên Windows, đã biết là đỏ trên Mac.)

## T1. Core: `StaggeredRace`

1. Test trước (`StaggeredRaceTests`, `FakeTimeProvider`): lượt 0 thắng thì không mở lượt 1; lượt 0 treo quá `stagger`
   thì mở lượt 1, lượt 1 thắng, lượt 0 bị huỷ; lượt 0 lỗi thì mở lượt 1 ngay; mọi lượt lỗi → danh sách lỗi;
   lượt thua có kết quả thì `discard`; bên gọi huỷ → `OperationCanceledException`; lỗi không dự kiến ném lại.
2. Code: `src/LunaDns.Core/StaggeredRace.cs`.

## T2. Core: `DohClient` chạy đua và nhớ máy

1. Test trước (`DohClientFailoverTests`): Cloudflare treo → Google được hỏi sau 500 ms, trả lời đúng; truy vấn kế đi thẳng
   Google; sau 5 phút Cloudflare lại được hỏi trước; Cloudflare lỗi nhanh → Google ngay; Google mở sau mà Cloudflare thắng
   → Google không bị đánh dấu; `Activity` (null → Cloudflare → Google dự phòng). Test cũ trong `DohClientTests` vẫn xanh.
2. Code: `DohClient` thêm `TimeProvider`, `HedgeDelay`, `Cooldown`, `Activity`; `DohActivity`.

## T3. Core: `TcpUpstreamConnector` so le (B9)

1. Test trước: hàm nối giả (seam internal) cho IP đầu treo mãi → sau 1 giây nối IP hai, trả stream của IP hai;
   socket thắng muộn bị đóng. Test mạng cũ vẫn xanh.
2. Code: `TcpUpstreamConnector` dùng `StaggeredRace`.

## T4. Trạng thái: Core → daemon → giao diện

1. Test trước: `FeatureSetTests` (`GetStatus().Doh` từ dịch vụ DNS); `DaemonTests` (`status` có `dohServer`,
   `dohFallback`; JSON thiếu hai trường vẫn đọc được); `DaemonFeatureControllerTests` (map sang `FeatureStatus.Doh`);
   `FeatureViewModelTests` (dòng "Đang bật · DoH: …").
2. Code: `IRunningService.Doh`, `FeatureStatus.Doh`, `CoreFeatureServices`, `DaemonStatus`, `DaemonCommands.Ok`,
   `DaemonFeatureController`, `FeatureViewModel`, `Strings.resx`, `MacCli/Program.cs PrintStatus`.

## T5. Tài liệu và kiểm máy thật

1. `docs/guide/03-ba-cong-tac.md`, `09-macos.md` và bản `en/`.
2. Kiểm máy thật theo spec (luật `pfctl` do user gõ sudo; gỡ sau khi kiểm).
