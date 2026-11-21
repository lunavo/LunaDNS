# D5: Bốn khu Kiểm tra / Trạng thái / Cập nhật / Gỡ & dọn — kế hoạch thực thi

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sắp lại cửa sổ chính thành 4 khu theo việc người dùng muốn làm, không thêm chức năng.

**Architecture:** Thay đổi gần như chỉ ở `Views/MainWindow.axaml` (đổi chỗ khối, thêm tiêu đề khu) cùng một kiểu chữ
trong `Styles/Theme.axaml`. Hai dòng chữ mới (phiên bản, gợi ý gỡ) lấy từ `Strings.resx`. Logic chữ phiên bản nằm ở
`Maintenance.VersionText` để test. Thứ tự khu được giữ bằng một test đọc axaml như XML.

**Tech Stack:** .NET 8, Avalonia 11, xUnit.

Spec: [2026-11-21-lunadns-maintenance-zones-design.md](../specs/2026-11-21-lunadns-maintenance-zones-design.md).

## Global Constraints

- Chữ giao diện chỉ nằm trong `src/LunaDns.App/Strings.resx`, không hardcode trong axaml.
- Không đổi tên journal op, tên feature, lệnh nào trong VM, menu khay.
- Commit tiếng Anh, không trailer `Co-Authored-By`.
- CI dùng SDK .NET 10: đối số log không được gọi hàm/thuộc tính tính toán (CA1873).
- Build: `dotnet build LunaDns.sln -c Release -warnaserror`.
- Test App hai target: `dotnet test tests/LunaDns.App.Tests -c Release --no-build -f net8.0-windows` và `-f net8.0`
  (trên Windows, target net8.0 có sẵn 5 test `MacOS/*` trượt vì `SetUnixFileMode`/đường dẫn `/`; mốc là đúng 5 test đó).

---

### Task 1: Chuỗi mới, `Maintenance.VersionText`, thuộc tính VM

**Files:**
- Modify: `src/LunaDns.App/Strings.resx`, `src/LunaDns.App/Strings.cs`, `src/LunaDns.App/Maintenance.cs`,
  `src/LunaDns.App/ViewModels/MainViewModel.cs`
- Test: `tests/LunaDns.App.Tests/StringsTests.cs`, `tests/LunaDns.App.Tests/MaintenanceTests.cs`

**Interfaces:**
- Produces: `Strings.ZoneCheck/ZoneStatus/ZoneUpdate/ZoneCleanup/VersionFormat/UninstallHint` (string);
  `static string Maintenance.VersionText(Version current)`; `MainViewModel.VersionText` (string, get-only);
  `Strings.CheckSectionTitle` bị xoá.

- [ ] **Step 1: Test hỏng**

`MaintenanceTests.cs` thêm:

```csharp
[Theory]
[InlineData(0, 9, 0, 0, "Đang dùng LunaDNS 0.9.0")]
[InlineData(1, 2, -1, -1, "Đang dùng LunaDNS 1.2.0")]
public void Version_text_shows_three_part_version(int major, int minor, int build, int revision, string expected)
{
    var version = build < 0 ? new Version(major, minor) : new Version(major, minor, build, revision);

    Assert.Equal(expected, Maintenance.VersionText(version));
}
```

`StringsTests.Every_static_string_resolves`: `Assert.Equal(202, …)` → `Assert.Equal(207, …)`. Thêm:

```csharp
/// <summary>D5: gợi ý gỡ khác nhau giữa bản portable Windows và macOS.</summary>
[Fact]
public void Uninstall_hint_points_to_platform_way()
{
    Assert.Contains("--uninstall", Strings.Lookup(nameof(Strings.UninstallHint), macOS: false), StringComparison.Ordinal);
    Assert.Contains("sudo lunadns uninstall", Strings.Lookup(nameof(Strings.UninstallHint), macOS: true), StringComparison.Ordinal);
}
```

- [ ] **Step 2: Chạy, thấy hỏng biên dịch** (`VersionText`, `UninstallHint` chưa có).

Run: `dotnet build tests/LunaDns.App.Tests -c Release` — Expected: lỗi CS0117.

- [ ] **Step 3: Cài đặt**

`Strings.resx`: xoá `CheckSectionTitle`; thêm

```xml
<data name="ZoneCheck" xml:space="preserve"><value>Kiểm tra</value></data>
<data name="ZoneStatus" xml:space="preserve"><value>Trạng thái</value></data>
<data name="ZoneUpdate" xml:space="preserve"><value>Cập nhật</value></data>
<data name="ZoneCleanup" xml:space="preserve"><value>Gỡ &amp; dọn</value></data>
<data name="VersionFormat" xml:space="preserve"><value>Đang dùng LunaDNS {0}</value></data>
<data name="UninstallHint" xml:space="preserve"><value>Bản portable: thoát bằng Thoát &amp; khôi phục ở khay, chạy LunaDNS.exe --uninstall trong PowerShell Admin rồi xoá thư mục. Xem hướng dẫn mục 7.</value></data>
<data name="UninstallHint_Mac" xml:space="preserve"><value>Gỡ bằng lệnh sudo lunadns uninstall trong Terminal. Xem hướng dẫn mục 9.</value></data>
```

`Strings.cs`: xoá dòng `CheckSectionTitle`; thêm sáu thuộc tính `public static string X => Get(nameof(X));` cạnh
`CheckUpdateButton` / `UninstallButton`.

`Maintenance.cs`:

```csharp
/// <summary>Dòng "Đang dùng LunaDNS x.y.z" ở khu Cập nhật (spec D5); ba số như hộp thoại "bản mới nhất".</summary>
public static string VersionText(Version current) =>
    Strings.Format(nameof(Strings.VersionFormat), UpdateChecker.Normalize(current).ToString());
```

`MainViewModel.cs`, cạnh `CanUninstall`:

```csharp
/// <summary>Khu Cập nhật (spec D5).</summary>
public string VersionText { get; } = Maintenance.VersionText(AppInfo.Version);
```

- [ ] **Step 4: Chạy test** — xanh trừ axaml còn tham chiếu `CheckSectionTitle` (Task 2 bỏ). Nếu build App lỗi vì
  axaml, làm Task 1 và Task 2 trong một commit.

- [ ] **Step 5: Commit** — `feat(app): strings and version text for the D5 zones`

### Task 2: Bố cục 4 khu + test thứ tự

**Files:**
- Modify: `src/LunaDns.App/Views/MainWindow.axaml`, `src/LunaDns.App/Styles/Theme.axaml`,
  `tests/LunaDns.App.Tests/LunaDns.App.Tests.csproj`
- Create: `tests/LunaDns.App.Tests/MainWindowLayoutTests.cs`

**Interfaces:**
- Consumes: `Strings.Zone*`, `Strings.UninstallHint`, `MainViewModel.VersionText`, `MainViewModel.CanUninstall`.

- [ ] **Step 1: Test hỏng**

csproj thêm:

```xml
<ItemGroup>
  <None Include="..\..\src\LunaDns.App\Views\MainWindow.axaml" Link="Layout\MainWindow.axaml" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`MainWindowLayoutTests.cs`:

```csharp
using System.Xml.Linq;

namespace LunaDns.App.Tests;

/// <summary>Spec D5: bốn khu theo thứ tự, mỗi lệnh nằm trong khu của nó. Đọc axaml như XML, không cần Avalonia.</summary>
public sealed class MainWindowLayoutTests
{
    private static readonly List<XElement> Elements =
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Layout", "MainWindow.axaml")).Descendants().ToList();

    private static int IndexOf(Func<XElement, bool> match)
    {
        var index = Elements.FindIndex(e => match(e));
        Assert.True(index >= 0, "Không thấy phần tử trong MainWindow.axaml");
        return index;
    }

    private static int Attr(string text) => IndexOf(e => e.Attributes().Any(a => a.Value.Contains(text, StringComparison.Ordinal)));

    private static int Zone(string key) => Attr($"Strings.{key}}}");

    [Fact]
    public void Zones_are_in_order()
    {
        Assert.True(Zone("ZoneCheck") < Zone("ZoneStatus"));
        Assert.True(Zone("ZoneStatus") < Zone("ZoneUpdate"));
        Assert.True(Zone("ZoneUpdate") < Zone("ZoneCleanup"));
    }

    [Theory]
    [InlineData("{Binding CheckCommand}", "ZoneCheck", "ZoneStatus")]
    [InlineData("{Binding TuneCommand}", "ZoneCheck", "ZoneStatus")]
    [InlineData("{Binding DiagnosticsCommand}", "ZoneCheck", "ZoneStatus")]
    [InlineData("{Binding HasAdvancedSettings}", "ZoneCheck", "ZoneStatus")]
    [InlineData("{Binding Features}", "ZoneStatus", "ZoneUpdate")]
    [InlineData("{Binding DomainGroups}", "ZoneStatus", "ZoneUpdate")]
    [InlineData("{Binding CheckUpdateCommand}", "ZoneUpdate", "ZoneCleanup")]
    [InlineData("{Binding VersionText}", "ZoneUpdate", "ZoneCleanup")]
    [InlineData("{Binding DomainList.ToggleCommand}", "ZoneUpdate", "ZoneCleanup")]
    [InlineData("{Binding DisableAllCommand}", "ZoneCleanup", null)]
    [InlineData("{Binding UninstallAppCommand}", "ZoneCleanup", null)]
    [InlineData("Strings.UninstallHint}", "ZoneCleanup", null)]
    public void Control_sits_in_its_zone(string binding, string zone, string? nextZone)
    {
        var index = Attr(binding);

        Assert.True(index > Zone(zone), $"{binding} phải nằm sau {zone}");
        if (nextZone is not null) Assert.True(index < Zone(nextZone), $"{binding} phải nằm trước {nextZone}");
    }

    [Fact]
    public void Check_card_title_is_gone() => Assert.DoesNotContain(Elements,
        e => e.Attributes().Any(a => a.Value.Contains("CheckSectionTitle", StringComparison.Ordinal)));
}
```

- [ ] **Step 2: Chạy, thấy hỏng** — `dotnet test tests/LunaDns.App.Tests -c Release -f net8.0-windows --filter MainWindowLayoutTests`
  Expected: FAIL "Không thấy phần tử".

- [ ] **Step 3: Cài đặt**

`Theme.axaml`, sau `TextBlock.section`:

```xml
<!-- Tiêu đề khu (spec D5): to hơn tiêu đề thẻ, màu chữ mặc định -->
<Style Selector="TextBlock.zone">
  <Setter Property="FontSize" Value="14" />
  <Setter Property="FontWeight" Value="SemiBold" />
  <Setter Property="Margin" Value="2,8,0,-8" />
</Style>
```

`MainWindow.axaml` (trong StackPanel chính, sau ba banner), theo thứ tự:

1. Hàng tiêu đề khu Kiểm tra: `Grid ColumnDefinitions="Auto,*"` gồm `TextBlock.zone` `ZoneCheck` và `ProbeStatus`
   (secondary, căn phải). Thẻ Kiểm tra bỏ Grid tiêu đề cũ, giữ nguyên phần còn lại. Cuối thẻ thêm nút phẳng Chẩn đoán
   (`Background="Transparent"`, `HorizontalAlignment="Left"`, icon `Icon.Diagnostics`). Sau thẻ: Expander Nâng cao
   (chuyển nguyên khối từ cuối trang lên).
2. `TextBlock.zone` `ZoneStatus`; ItemsControl Features; thẻ Tên miền bỏ Grid "Tự cập nhật danh sách".
3. `TextBlock.zone` `ZoneUpdate`; thẻ `Border.card` chứa `StackPanel Spacing="8"`: `Grid ColumnDefinitions="*,Auto"`
   (`TextBlock` `{Binding VersionText}` + nút Kiểm tra cập nhật), rồi Grid "Tự cập nhật danh sách" (chuyển nguyên khối,
   bỏ `Margin="0,8,0,0"`).
4. `TextBlock.zone` `ZoneCleanup`; thẻ `Border.card` chứa `StackPanel Spacing="8"`: `WrapPanel` gồm nút Tắt hết &
   khôi phục và nút Gỡ khỏi máy (`IsVisible="{Binding CanUninstall}"`, Margin trái 8); `TextBlock` secondary
   `UninstallHint`, `IsVisible="{Binding !CanUninstall}"`, wrap.
5. Xoá hai Grid chân trang cũ.

- [ ] **Step 4: Chạy** — build `-warnaserror`, App.Tests cả hai target. Expected: xanh (net8.0 chỉ còn 5 test MacOS mốc).

- [ ] **Step 5: Thử tay** — `dotnet run --project src/LunaDns.App -c Release -f net8.0-windows` (cần Admin): đủ 4 khu,
  bấm Kiểm tra / Chẩn đoán / Kiểm tra cập nhật; kéo hẹp 460 px không vỡ hàng.

- [ ] **Step 6: Commit** — `feat(app): arrange the main window into check, status, update and cleanup zones`

### Task 3: Guide vi + en

**Files:** `docs/guide/{01,03,04,05,07,09}-*.md`, `docs/guide/en/{01,03,04,05,07,09}-*.md`.

- [ ] **Step 1:** Mỗi cặp file sửa cùng ý:
  - 01: "Nút **Kiểm tra cập nhật** ở khu **Cập nhật**" (thay "ở mục Nâng cao").
  - 03: thêm một đoạn đầu về 4 khu (bảng ngắn khu → nội dung); mục "Tự cập nhật danh sách" ghi dòng này nằm ở khu
    **Cập nhật**; mục "Tắt" ghi nút ở khu **Gỡ & dọn**.
  - 04: nút **Chẩn đoán** ở cuối thẻ Kiểm tra; mục Nâng cao ngay dưới thẻ.
  - 05: mục Nâng cao nằm cuối khu **Kiểm tra**; chuyển gạch đầu dòng "Kiểm tra cập nhật" thành một câu nói nút ở khu
    **Cập nhật** kèm dòng "Đang dùng LunaDNS x.y.z".
  - 07: Gỡ bản Setup — nút **Gỡ khỏi máy** ở khu **Gỡ & dọn**; Gỡ bản portable — khu Gỡ & dọn hiện gợi ý thay cho nút.
  - 09: mục Giao diện — 4 khu, không có Nâng cao, khu Gỡ & dọn hiện gợi ý `sudo lunadns uninstall`.
- [ ] **Step 2:** `git grep -n "mục Nâng cao" docs/guide` và `git grep -n "Advanced section" docs/guide/en` không còn nói
  Kiểm tra cập nhật thuộc Nâng cao.
- [ ] **Step 3: Commit** — `docs(guide): describe the four zones in vi and en guides`

### Task 4: Merge, push, CI

- [ ] Build + bốn bộ test ở Release xanh (mốc 5 test MacOS ở net8.0 trên Windows).
- [ ] `git switch main && git merge --no-ff feat/d5-maintenance-zones && git push origin main`.
- [ ] `gh run watch` cho workflow CI của commit merge; job Windows và macOS đều xanh. Đỏ thì sửa trên main.
