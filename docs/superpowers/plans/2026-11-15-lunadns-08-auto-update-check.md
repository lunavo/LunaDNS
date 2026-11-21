# KH8 — Tự kiểm tra cập nhật khi mở app

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mỗi lần mở app, khoảng 10 giây sau khởi động, LunaDNS kiểm GitHub Releases một lần; có bản mới thì hiện dải báo trong cửa sổ (nút Tải, nút ✕), còn lại im lặng.

**Architecture:** Dùng lại `UpdateChecker` (KH7 K10). `Maintenance` thêm biến thể im lặng `CheckSilentlyAsync`. Trạng thái dải báo nằm trong lớp mới `UpdateBannerViewModel` (test được, không phụ thuộc Dispatcher); `MainViewModel` chỉ nối dây và gọi lúc `Start()`.

**Tech Stack:** .NET 8, Avalonia 11.3 (compiled bindings), xUnit.

**Spec:** [2026-11-15-auto-update-check-design.md](../specs/2026-11-15-auto-update-check-design.md)

## Global Constraints

- Chỉ sửa trong `src/LunaDns.App` và `tests/LunaDns.App.Tests`; không đụng Core, Platform, `AppSettings`.
- `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`: build phải sạch cảnh báo.
- Chữ giao diện tiếng Việt có dấu, nằm trong `Strings.resx` + property trong `Strings.cs`.
- Commit không có dòng `Co-Authored-By` Claude; tác giả Luna Vo.
- Nếu LunaDNS đang chạy (khoá DLL trong `bin`), thêm `-p:BaseOutputPath=<scratchpad>/bin/` vào mọi lệnh `dotnet build/test`.

## Điều chỉnh so với spec

- Spec dự kiến `AvailableUpdate`/`OpenUpdateCommand`/`DismissUpdateCommand` nằm thẳng trong `MainViewModel`. `MainViewModel` chưa có test và cần Dispatcher, nên gom vào `UpdateBannerViewModel` (đúng nhánh dự phòng spec đã cho phép). Bỏ `UpdateStartupDelay` dạng internal vì không có test VM dùng tới.
- Bỏ log Debug khi `Failed` (spec, Hành vi mục 3): `UpdateChecker` không trả lý do lỗi, một dòng "Failed" không giúp chẩn đoán. Sửa spec trong commit của Task 4.

## File Structure

| File | Trách nhiệm |
|---|---|
| `src/LunaDns.App/Updates/UpdateChecker.cs` | Repo chưa có release hợp lệ → `UpToDate`; `Normalize` thành `internal` để dùng chung. |
| `src/LunaDns.App/Maintenance.cs` | `CheckForUpdateAsync` trả kết quả; thêm `CheckSilentlyAsync`. |
| `src/LunaDns.App/ViewModels/UpdateBannerViewModel.cs` (mới) | Trạng thái + lệnh của dải báo. |
| `src/LunaDns.App/Strings.resx`, `Strings.cs` | 3 chuỗi mới. |
| `src/LunaDns.App/ViewModels/MainViewModel.cs` | Tạo banner, tự kiểm ở `Start()`, nút thủ công cũng đẩy kết quả vào banner. |
| `src/LunaDns.App/Views/MainWindow.axaml` | `Border` dải báo. |

---

### Task 1: UpdateChecker — repo chưa có release thì "đang dùng bản mới nhất"

**Files:**
- Modify: `src/LunaDns.App/Updates/UpdateChecker.cs`
- Test: `tests/LunaDns.App.Tests/UpdateCheckerTests.cs`

**Interfaces:**
- Produces: `internal static Version UpdateChecker.Normalize(Version v)` (trước là `private`), dùng ở Task 2, 3. `CheckAsync` trả `new UpdateCheckResult(UpdateStatus.UpToDate, null, null)` khi HTTP 200 mà không có release hợp lệ.

- [ ] **Step 1: Sửa test.** Trong `Errors_and_empty_lists_fail_softly`, xoá dòng `[InlineData(HttpStatusCode.OK, "[]")]` và đổi tên test thành `Errors_fail_softly`. Thêm test mới ngay sau nó:

```csharp
    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"tag_name":"v9.9.9","html_url":"https://github.com/owner/lunadns/releases/tag/v9.9.9","draft":true,"prerelease":false}]""")]
    [InlineData("""[{"tag_name":"nightly","html_url":"https://github.com/owner/lunadns/releases/tag/nightly","draft":false,"prerelease":false}]""")]
    public async Task No_valid_release_means_up_to_date(string body)
    {
        var result = await Checker("0.9.0", HttpStatusCode.OK, body).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Null(result.Latest);
        Assert.Null(result.Url);
    }

    [Fact]
    public void Normalize_fills_missing_build_and_drops_revision()
    {
        Assert.Equal(new Version(0, 9, 0), UpdateChecker.Normalize(new Version(0, 9)));
        Assert.Equal(new Version(0, 9, 1), UpdateChecker.Normalize(new Version(0, 9, 1, 7)));
    }
```

- [ ] **Step 2: Chạy, phải đỏ** (3 ca `No_valid_release…` nhận `Failed`; `Normalize` không truy cập được → lỗi biên dịch).

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter FullyQualifiedName~UpdateCheckerTests`
Expected: FAIL (lỗi biên dịch CS0122 ở `Normalize`).

- [ ] **Step 3: Sửa `UpdateChecker.cs`.**

Thay:
```csharp
        // MaxBy trên bộ giá trị (struct) ném khi rỗng, nên kiểm trước.
        if (candidates.Count == 0) return UpdateCheckResult.Failed;
```
bằng:
```csharp
        // Repo chưa có release hợp lệ: bản đang chạy coi là mới nhất. MaxBy trên bộ giá trị (struct) ném khi rỗng, nên kiểm trước.
        if (candidates.Count == 0) return new UpdateCheckResult(UpdateStatus.UpToDate, null, null);
```
Thay `private static Version Normalize(Version v)` bằng:
```csharp
    /// <summary>Phiên bản 3 phần như tag: thiếu Build thì 0, bỏ Revision.</summary>
    internal static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
```
Trong summary của lớp, sau câu "Lỗi mạng hay dữ liệu lạ đều thành Failed, không ném." thêm: "Không có release hợp lệ nào thì UpToDate, Latest/Url null."

- [ ] **Step 4: Chạy, phải xanh.**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter FullyQualifiedName~UpdateCheckerTests`
Expected: PASS hết.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.App/Updates/UpdateChecker.cs tests/LunaDns.App.Tests/UpdateCheckerTests.cs
git commit -m "fix(app): repo chưa có release thì báo đang dùng bản mới nhất"
```

---

### Task 2: Maintenance — kiểm im lặng và trả kết quả

**Files:**
- Modify: `src/LunaDns.App/Maintenance.cs`
- Test: `tests/LunaDns.App.Tests/MaintenanceTests.cs`

**Interfaces:**
- Consumes: `UpdateChecker.Normalize(Version)` (Task 1).
- Produces:
  - `public async Task<UpdateCheckResult> CheckForUpdateAsync(Func<CancellationToken, Task<UpdateCheckResult>> check, Version current, CancellationToken cancellationToken)` — hành vi hộp thoại giữ nguyên, nay trả về kết quả của `check`.
  - `public async Task<UpdateCheckResult?> CheckSilentlyAsync(Func<CancellationToken, Task<UpdateCheckResult>> check, CancellationToken cancellationToken)` — khác null chỉ khi `NewerAvailable`; mọi ngoại lệ (trừ huỷ do người gọi) → null; không gọi `prompts`/`shell`.

- [ ] **Step 1: Thêm test** vào `MaintenanceTests` (sau `Failure_informs`):

```csharp
    [Fact]
    public async Task Manual_check_returns_result()
    {
        _prompts.Answer = false;
        var result = new UpdateCheckResult(UpdateStatus.NewerAvailable, new Version(0, 9, 1), Url);

        Assert.Same(result, await Create().CheckForUpdateAsync(Returns(result), new Version(0, 9, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Silent_check_returns_newer_without_prompting()
    {
        var result = new UpdateCheckResult(UpdateStatus.NewerAvailable, new Version(0, 9, 1), Url);

        Assert.Same(result, await Create().CheckSilentlyAsync(Returns(result), CancellationToken.None));
        Assert.Empty(_prompts.Confirms);
        Assert.Empty(_prompts.InformTitles);
        Assert.Empty(_shell.Urls);
    }

    [Fact]
    public async Task Silent_check_ignores_up_to_date_and_failure()
    {
        Assert.Null(await Create().CheckSilentlyAsync(Returns(new UpdateCheckResult(UpdateStatus.UpToDate, null, null)), CancellationToken.None));
        Assert.Null(await Create().CheckSilentlyAsync(Returns(UpdateCheckResult.Failed), CancellationToken.None));
        Assert.Empty(_prompts.InformTitles);
    }

    [Fact]
    public async Task Silent_check_swallows_exceptions()
    {
        Assert.Null(await Create().CheckSilentlyAsync(_ => throw new HttpRequestException("x"), CancellationToken.None));
        Assert.Empty(_prompts.InformTitles);
    }

    [Fact]
    public async Task Silent_check_lets_caller_cancellation_through()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create().CheckSilentlyAsync(ct => Task.FromCanceled<UpdateCheckResult>(ct), cts.Token));
    }
```

- [ ] **Step 2: Chạy, phải đỏ.**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter FullyQualifiedName~MaintenanceTests`
Expected: FAIL (lỗi biên dịch: `CheckSilentlyAsync` chưa có, `CheckForUpdateAsync` trả `Task`).

- [ ] **Step 3: Sửa `Maintenance.cs`.** Thay toàn bộ phương thức `CheckForUpdateAsync` bằng:

```csharp
    public async Task<UpdateCheckResult> CheckForUpdateAsync(Func<CancellationToken, Task<UpdateCheckResult>> check, Version current, CancellationToken cancellationToken)
    {
        var result = await check(cancellationToken).ConfigureAwait(true);
        var shown = UpdateChecker.Normalize(current).ToString();
        switch (result.Status)
        {
            case UpdateStatus.NewerAvailable:
                var message = Strings.Format(nameof(Strings.UpdateAvailableFormat), result.Latest, shown);
                if (await prompts.ConfirmAsync(Strings.UpdateAvailableTitle, message).ConfigureAwait(true)) shell.OpenUrl(result.Url!);
                break;
            case UpdateStatus.UpToDate:
                await prompts.InformAsync(Strings.UpToDateTitle, Strings.Format(nameof(Strings.UpToDateFormat), shown)).ConfigureAwait(true);
                break;
            default:
                await prompts.InformAsync(Strings.CheckUpdateButton, Strings.UpdateCheckFailed).ConfigureAwait(true);
                break;
        }

        return result;
    }

    /// <summary>Tự kiểm khi mở app (spec 2026-11-15): không hộp thoại; null khi không có bản mới hay kiểm lỗi.</summary>
    public async Task<UpdateCheckResult?> CheckSilentlyAsync(Func<CancellationToken, Task<UpdateCheckResult>> check, CancellationToken cancellationToken)
    {
        try
        {
            var result = await check(cancellationToken).ConfigureAwait(true);
            return result.Status == UpdateStatus.NewerAvailable ? result : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
```

Sửa summary của lớp: `/// <summary>Logic của nút "Kiểm tra cập nhật" và tự kiểm khi mở app (spec 4.10, 2026-11-15) và "Gỡ khỏi máy" (KH7 K4), tách khỏi VM để test.</summary>`

- [ ] **Step 4: Chạy, phải xanh.** Build cả app vì `MainViewModel` gọi `CheckForUpdateAsync` (vẫn biên dịch được: `Task<T>` gán vào lambda trả `Task`).

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter FullyQualifiedName~MaintenanceTests`
Expected: PASS hết.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.App/Maintenance.cs tests/LunaDns.App.Tests/MaintenanceTests.cs
git commit -m "feat(app): Maintenance kiểm cập nhật im lặng"
```

---

### Task 3: UpdateBannerViewModel và chuỗi

**Files:**
- Create: `src/LunaDns.App/ViewModels/UpdateBannerViewModel.cs`
- Modify: `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs`
- Test: Create `tests/LunaDns.App.Tests/UpdateBannerViewModelTests.cs`; Modify `tests/LunaDns.App.Tests/StringsTests.cs`

**Interfaces:**
- Consumes: `UpdateChecker.Normalize`, `ISystemShell.OpenUrl(string)`, `AsyncCommand(Func<Task>, Action<Exception>)`, `ObservableObject.OnPropertyChanged`.
- Produces (Task 4 dùng):
  - `internal sealed class UpdateBannerViewModel(ISystemShell shell, Version current, Action<Exception> onError) : ObservableObject`
  - `bool IsVisible`, `string Text`, `AsyncCommand OpenCommand`, `AsyncCommand DismissCommand`
  - `void Show(UpdateCheckResult result)` — chỉ nhận `NewerAvailable`, các trạng thái khác bỏ qua (không ẩn banner đang hiện).
  - `Strings.UpdateBannerFormat`, `Strings.UpdateDownloadButton`, `Strings.UpdateDismissTooltip`.

- [ ] **Step 1: Viết test** `tests/LunaDns.App.Tests/UpdateBannerViewModelTests.cs`:

```csharp
using LunaDns.App.Updates;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Tests;

public sealed class UpdateBannerViewModelTests
{
    private const string Url = "https://github.com/o/r/releases/tag/v0.9.1";

    private readonly FakeShell _shell = new();
    private readonly List<Exception> _errors = [];

    private UpdateBannerViewModel Create() => new(_shell, new Version(0, 9, 0, 0), _errors.Add);

    private static UpdateCheckResult Newer() => new(UpdateStatus.NewerAvailable, new Version(0, 9, 1), Url);

    [Fact]
    public void Hidden_until_shown()
    {
        var banner = Create();

        Assert.False(banner.IsVisible);
        Assert.Equal(string.Empty, banner.Text);
    }

    [Fact]
    public void Newer_version_shows_text_with_both_versions()
    {
        var banner = Create();
        var changed = new List<string?>();
        banner.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        banner.Show(Newer());

        Assert.True(banner.IsVisible);
        Assert.Equal("Đã có LunaDNS 0.9.1 (bạn đang dùng 0.9.0).", banner.Text);
        Assert.Contains(nameof(UpdateBannerViewModel.IsVisible), changed);
        Assert.Contains(nameof(UpdateBannerViewModel.Text), changed);
    }

    [Fact]
    public void Up_to_date_and_failure_do_not_show_or_hide()
    {
        var banner = Create();
        banner.Show(new UpdateCheckResult(UpdateStatus.UpToDate, null, null));
        banner.Show(UpdateCheckResult.Failed);
        Assert.False(banner.IsVisible);

        banner.Show(Newer());
        banner.Show(UpdateCheckResult.Failed);
        Assert.True(banner.IsVisible);
    }

    [Fact]
    public void Open_opens_release_page_and_keeps_banner()
    {
        var banner = Create();
        banner.Show(Newer());

        banner.OpenCommand.Execute(null);

        Assert.Equal([Url], _shell.Urls);
        Assert.True(banner.IsVisible);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Dismiss_hides_and_manual_check_can_show_again()
    {
        var banner = Create();
        banner.Show(Newer());

        banner.DismissCommand.Execute(null);
        Assert.False(banner.IsVisible);

        banner.Show(Newer());
        Assert.True(banner.IsVisible);
    }

    private sealed class FakeShell : ISystemShell
    {
        public List<string> Urls { get; } = [];

        public void OpenUrl(string url) => Urls.Add(url);

        public void StartProcess(string path) { }
    }
}
```

Trong `StringsTests.Every_static_string_resolves` đổi `Assert.Equal(74, properties.Count);` thành `Assert.Equal(77, properties.Count);`.

- [ ] **Step 2: Chạy, phải đỏ.**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter "FullyQualifiedName~UpdateBannerViewModelTests|FullyQualifiedName~StringsTests"`
Expected: FAIL (lỗi biên dịch: `UpdateBannerViewModel` chưa có).

- [ ] **Step 3: Thêm chuỗi.** Trong `Strings.resx`, ngay sau dòng `UpdateCheckFailed`:

```xml
  <data name="UpdateBannerFormat" xml:space="preserve"><value>Đã có LunaDNS {0} (bạn đang dùng {1}).</value></data>
  <data name="UpdateDownloadButton" xml:space="preserve"><value>Tải</value></data>
  <data name="UpdateDismissTooltip" xml:space="preserve"><value>Ẩn đến lần mở app sau</value></data>
```

Trong `Strings.cs`, ngay sau `public static string UpdateCheckFailed => Get(nameof(UpdateCheckFailed));`:

```csharp
    public static string UpdateBannerFormat => Get(nameof(UpdateBannerFormat));
    public static string UpdateDownloadButton => Get(nameof(UpdateDownloadButton));
    public static string UpdateDismissTooltip => Get(nameof(UpdateDismissTooltip));
```

- [ ] **Step 4: Tạo `src/LunaDns.App/ViewModels/UpdateBannerViewModel.cs`:**

```csharp
using LunaDns.App.Updates;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Dải báo "có bản mới" (spec 2026-11-15). Hiện khi tự kiểm lúc mở app hay nút "Kiểm tra cập nhật" thấy bản mới;
/// UpToDate/Failed không đổi gì. Tải mở trang Release (URL đã lọc trong UpdateChecker), ✕ ẩn đến lần mở app sau.
/// </summary>
internal sealed class UpdateBannerViewModel : ObservableObject
{
    private readonly ISystemShell _shell;
    private readonly string _current;
    private UpdateCheckResult? _update;

    public UpdateBannerViewModel(ISystemShell shell, Version current, Action<Exception> onError)
    {
        _shell = shell;
        _current = UpdateChecker.Normalize(current).ToString();
        OpenCommand = new AsyncCommand(OpenAsync, onError);
        DismissCommand = new AsyncCommand(DismissAsync, onError);
    }

    public AsyncCommand OpenCommand { get; }

    public AsyncCommand DismissCommand { get; }

    public bool IsVisible => _update is not null;

    public string Text => _update is null ? string.Empty : Strings.Format(nameof(Strings.UpdateBannerFormat), _update.Latest, _current);

    public void Show(UpdateCheckResult result)
    {
        if (result.Status == UpdateStatus.NewerAvailable) Set(result);
    }

    private Task OpenAsync()
    {
        if (_update?.Url is { } url) _shell.OpenUrl(url);
        return Task.CompletedTask;
    }

    private Task DismissAsync()
    {
        Set(null);
        return Task.CompletedTask;
    }

    private void Set(UpdateCheckResult? update)
    {
        _update = update;
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(Text));
    }
}
```

- [ ] **Step 5: Chạy, phải xanh.**

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows --filter "FullyQualifiedName~UpdateBannerViewModelTests|FullyQualifiedName~StringsTests"`
Expected: PASS hết.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.App/ViewModels/UpdateBannerViewModel.cs src/LunaDns.App/Strings.resx src/LunaDns.App/Strings.cs tests/LunaDns.App.Tests/UpdateBannerViewModelTests.cs tests/LunaDns.App.Tests/StringsTests.cs
git commit -m "feat(app): dải báo có bản mới"
```

---

### Task 4: Nối vào MainViewModel và MainWindow

**Files:**
- Modify: `src/LunaDns.App/ViewModels/MainViewModel.cs`
- Modify: `src/LunaDns.App/Views/MainWindow.axaml` (sau `Border` thanh thông báo, khoảng dòng 80)
- Modify: `docs/superpowers/specs/2026-11-15-auto-update-check-design.md` (ghi hai điều chỉnh ở đầu kế hoạch này)

**Interfaces:**
- Consumes: `UpdateBannerViewModel` (Task 3), `Maintenance.CheckSilentlyAsync` và `CheckForUpdateAsync` trả `Task<UpdateCheckResult>` (Task 2).
- Produces: `public UpdateBannerViewModel UpdateBanner { get; }` trên `MainViewModel`, XAML bind `UpdateBanner.*`.

- [ ] **Step 1: Sửa `MainViewModel.cs`.**

Sau `private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);` thêm:
```csharp
    /// <summary>Tự kiểm cập nhật sau khi khởi động xong, để không tranh mạng với lần kiểm kết nối đầu.</summary>
    private static readonly TimeSpan UpdateStartupDelay = TimeSpan.FromSeconds(10);
```

Trong constructor, ngay sau `_maintenance = new Maintenance(prompts, shell);` thêm:
```csharp
        UpdateBanner = new UpdateBannerViewModel(shell, AppInfo.Version, OnError);
```

Sau `public AsyncCommand InstallServiceCommand { get; }` thêm:
```csharp

    public UpdateBannerViewModel UpdateBanner { get; }
```

Trong `Start()`, sau `CheckCommand.Execute(null);` thêm:
```csharp
        _ = CheckUpdateSilentlyAsync();
```

Thay:
```csharp
    private Task CheckUpdateAsync() =>
        _maintenance.CheckForUpdateAsync(ct => new UpdateChecker(UpdateHttp, AppInfo.Repository, AppInfo.Version).CheckAsync(ct), AppInfo.Version, CancellationToken.None);
```
bằng:
```csharp
    private async Task CheckUpdateAsync() =>
        UpdateBanner.Show(await _maintenance.CheckForUpdateAsync(FetchUpdateAsync, AppInfo.Version, CancellationToken.None).ConfigureAwait(true));

    /// <summary>Chạy một lần mỗi lần mở app; CheckSilentlyAsync không ném nên bỏ Task không sợ mất lỗi.</summary>
    private async Task CheckUpdateSilentlyAsync()
    {
        await Task.Delay(UpdateStartupDelay).ConfigureAwait(true);
        if (await _maintenance.CheckSilentlyAsync(FetchUpdateAsync, CancellationToken.None).ConfigureAwait(true) is { } update) UpdateBanner.Show(update);
    }

    private static Task<UpdateCheckResult> FetchUpdateAsync(CancellationToken cancellationToken) =>
        new UpdateChecker(UpdateHttp, AppInfo.Repository, AppInfo.Version).CheckAsync(cancellationToken);
```

- [ ] **Step 2: Sửa `MainWindow.axaml`.** Ngay sau thẻ đóng `</Border>` của "Thanh thông báo theo mức" (Border có `IsVisible="{Binding HasMessage}"`), thêm:

```xml

      <!-- Có bản mới: tự kiểm khi mở app hoặc nút Kiểm tra cập nhật (spec 2026-11-15) -->
      <Border Classes="banner" IsVisible="{Binding UpdateBanner.IsVisible}">
        <Grid ColumnDefinitions="Auto,*,Auto,Auto">
          <PathIcon Classes="tone info" VerticalAlignment="Center" Margin="0,0,8,0" />
          <TextBlock Grid.Column="1" Text="{Binding UpdateBanner.Text}" TextWrapping="Wrap" VerticalAlignment="Center" />
          <Button Grid.Column="2" Classes="accent" Margin="8,0,0,0" VerticalAlignment="Center"
                  Command="{Binding UpdateBanner.OpenCommand}">
            <TextBlock Text="{x:Static app:Strings.UpdateDownloadButton}" />
          </Button>
          <Button Grid.Column="3" Margin="8,0,0,0" Padding="4" VerticalAlignment="Center" Background="Transparent"
                  Command="{Binding UpdateBanner.DismissCommand}"
                  ToolTip.Tip="{x:Static app:Strings.UpdateDismissTooltip}"
                  AutomationProperties.Name="{x:Static app:Strings.UpdateDismissTooltip}">
            <PathIcon Data="{DynamicResource Icon.Close}" Width="12" Height="12" />
          </Button>
        </Grid>
      </Border>
```

- [ ] **Step 3: Sửa spec.** Trong `docs/superpowers/specs/2026-11-15-auto-update-check-design.md`:
  - Hành vi mục 3: thay "Chỉ ghi log mức Debug khi `Failed`." bằng "Không ghi log (UpdateChecker không trả lý do lỗi)."
  - Bảng "Thay đổi code", dòng `MainViewModel.cs`: thay nội dung cột 2 bằng "`UpdateBanner` (`UpdateBannerViewModel` mới, giữ `IsVisible`, `Text`, `OpenCommand`, `DismissCommand`, `Show`); tự kiểm sau 10 giây trong `Start()`; nút thủ công đẩy kết quả vào banner." và thêm dòng `src/LunaDns.App/ViewModels/UpdateBannerViewModel.cs` | "Trạng thái dải báo, test được không cần Dispatcher."
  - Mục Test, gạch "VM (nếu…)": đổi thành "`UpdateBannerViewModelTests`: ẩn lúc đầu; có bản mới thì hiện đúng chữ; UpToDate/Failed không đổi; Tải gọi `OpenUrl` đúng URL và giữ banner; ✕ ẩn, kiểm lại hiện lại."

- [ ] **Step 4: Verify toàn bộ.**

Run: `dotnet build -c Release -warnaserror`
Expected: Build succeeded, 0 Warning(s).

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0-windows`
Expected: 0 Failed; tổng số ca tăng 13 so với trước KH8 (Task 1 +3, Task 2 +5, Task 3 +5).

Run: `dotnet test tests/LunaDns.App.Tests -f net8.0`
Expected: 0 Failed.

- [ ] **Step 5: Commit**

```bash
git add src/LunaDns.App/ViewModels/MainViewModel.cs src/LunaDns.App/Views/MainWindow.axaml docs/superpowers/specs/2026-11-15-auto-update-check-design.md
git commit -m "feat(app): tự kiểm tra cập nhật khi mở app"
```

---

### Task 5: Thử tay (PO)

Không có test tự động cho phần nối Avalonia. Cần một Release **Published** trên `lunavo/LunaDNS` (bản nháp không thấy được qua API công khai).

- [ ] Hạ tạm `<Version>` trong `src/LunaDns.App/LunaDns.App.csproj` xuống `0.0.1`, chạy app bằng `Chay-tu-ma-nguon.cmd`.
- [ ] Khoảng 10 giây sau khi cửa sổ mở: dải báo "Đã có LunaDNS X (bạn đang dùng 0.0.1)." hiện dưới header; không có hộp thoại nào.
- [ ] Bấm **Tải**: trình duyệt mở `https://github.com/lunavo/LunaDNS/releases/tag/vX`; dải báo vẫn còn.
- [ ] Bấm **✕**: dải báo ẩn. Bấm "Kiểm tra cập nhật": hộp thoại "Có bản mới" như cũ, chọn Không; dải báo hiện lại.
- [ ] Rút mạng rồi mở lại app: không có dải báo, không có hộp thoại lỗi.
- [ ] Trả `<Version>` về giá trị cũ (`git checkout src/LunaDns.App/LunaDns.App.csproj`).
