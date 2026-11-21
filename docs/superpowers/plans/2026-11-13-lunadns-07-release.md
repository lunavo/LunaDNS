# LunaDNS KH7: Phát hành "1 click" — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Người dùng tải `LunaDNS-Setup-x.y.z.exe` (hoặc zip portable) từ GitHub Releases, bấm một lần là chạy, không cài .NET, không gõ lệnh; gỡ qua Settings → Apps thì máy sạch; người clone repo bấm một file `.cmd` là build và chạy.

**Architecture:** Một script `build/publish.ps1` là nguồn duy nhất sinh bản self-contained `win-x64` (exe single-file + vài DLL native cạnh nó, không trim), zip portable, Setup (Inno Setup) và `SHA256SUMS.txt`; máy PO, GitHub Actions và script clone đều gọi nó. Phần "chạm hệ thống" của bản cài nằm trong app (`--install-system`, `--uninstall`, ACL thư mục dữ liệu mỗi lần chạy), có test; Setup chỉ gọi các lệnh đó. Cập nhật là nút "Kiểm tra cập nhật" đọc GitHub Releases rồi mở trang tải; nâng cấp là chạy Setup mới, Setup hoàn tác bằng exe cũ trước khi ghi đè.

**Tech Stack:** .NET 8 SDK 8.0.428, Avalonia 11.3.23, xUnit 2.5.3, Inno Setup 6 (`ISCC.exe`), PowerShell 5.1+/7, `schtasks.exe`, GitHub Actions (`windows-latest`), Windows Sandbox.

## Global Constraints

- Đích duy nhất: `win-x64`, Windows 10 1809+ (`MinVersion=10.0.17763`). Không ARM64, không x86.
- Bản build: `SelfContained=true`, `PublishSingleFile=true`, `EnableCompressionInSingleFile=true`, `PublishTrimmed=false`, `IncludeNativeLibrariesForSelfExtract=false`.
- Exe giữ manifest `requireAdministrator` (đã có trong `src/LunaDns.App/app.manifest`). Không thêm `.cmd`/`.ps1` để xin quyền cho người dùng cuối.
- `--install-system` chỉ chạy khi exe nằm dưới `%ProgramFiles%`; ngược lại trả mã 3 và không đổi gì.
- Task SYSTEM: tên `\LunaDNS\Cleanup`, chạy `"<thư mục cài>\LunaDNS.exe" --cleanup`, trigger lúc khởi động máy và lúc có người đăng nhập.
- ACL `%ProgramData%\LunaDNS`: bỏ kế thừa; SYSTEM và BUILTIN\Administrators FullControl; BUILTIN\Users ReadAndExecute; owner là Administrators; áp xuống mọi file con.
- Mã thoát của exe: 0 = xong, 1 = còn việc chưa xong (xem log), 2 = tham số sai, 3 = `--install-system` sai vị trí, 4 = `--uninstall` khi app đang chạy.
- Không để exe/zip đã build trong git. `artifacts/` nằm trong `.gitignore`.
- Mọi file mã nguồn, chuỗi giao diện và log theo quy ước KH5 (chuỗi giao diện trong `Strings.resx`, log không chứa đường dẫn có tên user).
- `dotnet build -warnaserror` phải 0 cảnh báo sau mỗi task.
- Không commit `cleanup-task.xml` hay file tạm nào; XML của task chỉ được ghi trong `%ProgramData%\LunaDNS` (đã ACL), không ghi vào `%TEMP%`.

---

## Bối cảnh và quyết định (PO duyệt 13/11/2026)

Bối cảnh: KH1–KH5 đã code xong, T8 của KH5 đạt, chưa commit (branch `spike/plan1-task8`). Repo chưa có remote. Chỉ có bản Debug. KH6 (UI/UX + icon) đã duyệt, chưa code. KH5 để lại cho "KH6 cũ": publish trimmed/single-file, cảnh báo trim của Journal (quyết định 16), kiểm tra cập nhật (spec 4.10), chặn phát hành khi KH4b chưa xong (quyết định 12), ký số. KH7 nhận hết phần đó; KH6 giữ đúng UI/UX + icon.

Quyết định (K1–K4 PO chọn trực tiếp; K5–K14 là mặc định đề xuất trong thiết kế, PO duyệt cả gói):

1. **K1 — Hai hình thức:** `LunaDNS-Setup-x.y.z.exe` (đường chính, cài vào `%ProgramFiles%\LunaDNS`) và `LunaDNS-x.y.z-portable.zip` (giải nén, bấm đúp; không có task SYSTEM).
2. **K2 — GitHub public, mã nguồn mở.** CI là GitHub Actions; bản phát hành là Release asset; kiểm tra cập nhật qua API Releases.
3. **K3 — v1 chưa ký số.** Phát kèm `SHA256SUMS.txt` và hướng dẫn SmartScreen. PO nộp đơn SignPath Foundation song song (T11); khi được duyệt, bước ký thêm vào `release.yml` ở một plan nhỏ sau (cần ID tổ chức/dự án SignPath, chưa có).
4. **K4 — KH7 thay KH4b phần hệ thống:** app có `--install-system` (ACL + task SYSTEM) và `--uninstall`; Setup gọi chúng. Việc đo đăng xuất và hook `WM_QUERYENDSESSION` vẫn ở KH4b. Với K4, quyết định 12 của KH5 (không phát trước khi có ACL) được thoả bằng T2 + T4.
5. **K5 — Không trim ở v1.** Lý do: khi `PublishTrimmed=true`, .NET 8 đặt `JsonSerializer.IsReflectionEnabledByDefault=false`, nên `ChangeJournal.Record<T>` (`JsonSerializer.SerializeToElement(snapshot)`, reflection) ném `InvalidOperationException` lúc chạy — không chỉ là cảnh báo. `Microsoft.Management.Infrastructure` (CIM) cũng không thân thiện với trim. Quyết định 16 của KH5 đóng bằng ghi chú này; trim là việc sau (journal sinh serializer sẵn).
   **Cập nhật 21/11/2026 (nhánh `perf/trim-macos`):** journal (`JournalJsonContext`, `SnapshotJsonContext`) và giao thức daemon (`DaemonJsonContext`) đã dùng serializer sinh sẵn, định dạng file/dây giữ nguyên (`JournalFormatTests`, `Protocol_wire_format_is_stable`). Bản **macOS** publish `PublishTrimmed=true` từ đây; Core, Platform.MacOS, MacCli và App (net8.0) bật `EnableTrimAnalyzer` nên cảnh báo trim là lỗi ngay khi build. Bản **Windows** vẫn không trim cho tới khi kiểm xong `Microsoft.Management.Infrastructure` (CIM) và COM.
6. **K6 — DLL native để cạnh exe, không tự giải nén.** `IncludeNativeLibrariesForSelfExtract=true` giải nén DLL native vào `%TEMP%\.net\...`, thư mục người dùng ghi được, rồi tiến trình Admin nạp chúng: chỗ cấy DLL. Bản cài nằm trong Program Files (chỉ Admin ghi được) nên DLL cạnh exe là an toàn.
7. **K7 — Task SYSTEM chỉ cho bản cài.** Task chạy bằng SYSTEM; exe ở thư mục người dùng ghi được (Downloads, thư mục giải nén zip) thì ai tráo exe là có SYSTEM. Vì vậy `--install-system` từ chối ngoài `%ProgramFiles%`, và bản portable không có task.
8. **K8 — Task có hai trigger: khởi động máy và đăng nhập.** Lúc khởi động, hive `HKEY_USERS\<SID>` của người dùng chưa nạp nên mục `AutoConfigURL` hoàn tác không được (giữ trong journal, mã 1); trigger đăng nhập chạy lại khi hive đã nạp. Mục NRPT và hosts hoàn tác được ngay lúc khởi động.
9. **K9 — ACL áp ở mỗi lần chạy exe** (mọi lệnh, cả portable), trước khi đọc journal. Đây là bước "kiểm ACL trong luồng chính" (spec 5). Lỗi thì ghi log cảnh báo, không chặn. Users được đọc (để mở thư mục log từ Explorer như guide 08), không được ghi: journal là đầu vào của SYSTEM, ai ghi được journal là điều khiển được việc SYSTEM ghi registry/hosts.
10. **K10 — Cập nhật thủ công:** nút "Kiểm tra cập nhật" (spec 4.10) đọc `GET /repos/<repo>/releases`, lấy tag `vX.Y.Z` lớn nhất không phải draft (kể cả prerelease, vì v0.9.x là prerelease), so với phiên bản app, rồi hỏi mở trang Release. Không tự tải, không kiểm lúc mở app. Chỉ mở URL bắt đầu bằng `https://github.com/<repo>/releases/`.
11. **K11 — Nâng cấp hoàn tác bằng exe cũ:** trước khi ghi đè, Setup chạy `"<thư mục cài>\LunaDNS.exe" --cleanup` của bản đang cài; lỗi thì dừng cài. Nhờ vậy không cần giữ định dạng journal tương thích giữa các phiên bản.
12. **K12 — Clone repo:** không để exe trong repo. Gốc repo có `Chay-tu-ma-nguon.cmd`: cần .NET 8 SDK (thiếu thì in lệnh `winget` và link Releases), gọi `build\publish.ps1 -SkipZip -SkipInstaller`, mở exe vừa build. README ghi ngay đầu: "Không phải dev? Tải ở Releases."
13. **K13 — Release là nháp:** tag `v*` chạy `release.yml`, tạo GitHub Release **draft**; PO thử trong Windows Sandbox rồi tự bấm Publish. Tag đầu tiên `v0.9.0`, đánh dấu prerelease (mọi `0.x` là prerelease). `release.yml` dừng nếu tag khác `<Version>` trong csproj hoặc thiếu `docs/release-notes/vX.Y.Z.md`.
14. **K14 — Setup dùng giao diện tiếng Anh có sẵn của Inno Setup** (Inno 6 không có bản tiếng Việt chính thức); các thông báo riêng của LunaDNS trong Setup viết tiếng Việt bằng `[CustomMessages]`.
15. **Thứ tự với KH6:** T1–T8 không đụng UI, làm được song song với KH6. T9 (nút trên giao diện) làm **sau** khi KH6 xong, vì KH6 sửa `MainWindow.axaml` và `MainViewModel`. Phát hành `v0.9.0` (T11) chỉ sau khi KH6 và T9 xong.

## Việc không thuộc KH7

- Trim và AOT (cần journal dùng `JsonSerializerContext`, kiểm CIM).
- Tự tải và tự cài bản mới; kênh beta/stable riêng.
- Tích hợp ký SignPath vào `release.yml` (plan nhỏ sau khi SignPath duyệt; bước ký phải chạy trên exe **trước** khi Inno đóng gói, rồi ký cả Setup).
- winget/Scoop manifest, ARM64, bản MSI.
- Đo đăng xuất và hook `WM_QUERYENDSESSION` (KH4b).
- GitLab mirror.

## Cấu trúc file

```
.gitignore                                            # T1  + artifacts/
Chay-tu-ma-nguon.cmd                                  # T7  bấm đúp khi clone
LICENSE                                               # T0  PO chọn (đề xuất MIT)
build/
├── publish.ps1                                       # T1 (portable) + T6 (Setup, SHA256)
├── installer/LunaDNS.iss                             # T6  Inno Setup
└── sandbox/LunaDNS.wsb                               # T6  Windows Sandbox để thử Setup
.github/workflows/
├── ci.yml                                            # T8  build + test cho push/PR
└── release.yml                                       # T8  tag v* → Release nháp
docs/release-notes/v0.9.0.md                          # T8
src/LunaDns.App/
├── LunaDns.App.csproj                                # T0 repo, T5 AssemblyMetadata, T11 Version
├── Properties/PublishProfiles/win-x64.pubxml         # T1
├── CommandLine.cs                                    # T4  + --install-system, --uninstall
├── CleanupCommand.cs                                 # T4  tách RollbackJournal
├── InstallLocation.cs                                # T4  path guard, IsInstalled
├── InstallCommand.cs                                 # T4
├── UninstallCommand.cs                               # T4
├── Program.cs                                        # T4  nối lệnh mới, ACL mỗi lần chạy
├── AppPaths.cs                                       # T4  sửa doc comment
├── AppInfo.cs                                        # T5  phiên bản + repo
├── Updates/UpdateChecker.cs                          # T5
├── Updates/GitHubRelease.cs                          # T5  DTO + JsonSerializerContext
├── Maintenance.cs                                    # T9  logic nút cập nhật/gỡ, test được
├── ISystemShell.cs                                   # T9  mở URL, chạy uninstaller
├── ViewModels/MainViewModel.cs                       # T9
├── Views/MainWindow.axaml                            # T9
├── Strings.cs, Strings.resx                          # T9
src/LunaDns.Platform.Windows/Install/
├── DataDirectoryAcl.cs                               # T2
├── CleanupTaskDefinition.cs                          # T3  XML thuần
└── SchtasksCleanupTaskStore.cs                       # T3  gọi schtasks.exe
tests/LunaDns.Platform.Windows.Tests/Install/
├── DataDirectoryAclTests.cs                          # T2
├── CleanupTaskDefinitionTests.cs                     # T3
└── SchtasksCleanupTaskStoreAdminTests.cs             # T3
tests/LunaDns.App.Tests/
├── CommandLineTests.cs                               # T4  thêm case
├── InstallLocationTests.cs                           # T4
├── InstallCommandTests.cs                            # T4
├── UninstallCommandTests.cs                          # T4
├── FakeInstallParts.cs                               # T4  fake ACL + task store
├── UpdateCheckerTests.cs                             # T5
└── MaintenanceTests.cs                               # T9
README.md, docs/guide/01,02,06,07,08                  # T10
```

## Giao việc

| Agent | Task | Model | Ghi chú |
|---|---|---|---|
| — | T0 | PO | commit KH1–KH5, tạo repo, LICENSE |
| A | T1 | Sonnet | publish profile + script portable, đo kích thước |
| B | T2, T3 | Sonnet | Platform.Windows/Install |
| C | T4, T5 | Sonnet | lệnh App + UpdateChecker (cần B xong) |
| D | T6, T7, T8 | Sonnet | Inno, script clone, CI (cần A, C xong) |
| E | T9 | Sonnet | sau KH6 |
| — | T10 | phiên chính | docs, PO duyệt chữ |
| — | T11 | PO | test tay + phát hành + SignPath |

Dự kiến 5 sub-agent Sonnet. Sonnet trượt verify 2 lần ⇒ giao Opus một lần. Gate QA theo `canopi-gate` tier T2.

---

### Task 0 (PO): Chuẩn bị repo

**Files:** `LICENSE`, `src/LunaDns.App/LunaDns.App.csproj` (chỉ dòng repo)

- [ ] **Step 1:** Commit KH1–KH5 theo kế hoạch commit của KH5 (memory `lunadns-plan5`), trên branch hiện tại, rồi merge vào `main`.
- [ ] **Step 2:** Tạo repo GitHub **public**, ghi tên `<owner>/<repo>` vào memory KH7. Thêm `LICENSE` (đề xuất MIT; SignPath Foundation yêu cầu license OSI).
- [ ] **Step 3:** Thêm vào `PropertyGroup` đầu tiên của `src/LunaDns.App/LunaDns.App.csproj`, thay `<owner>/<repo>` bằng tên thật ở Step 2:

```xml
    <LunaDnsRepository>&lt;owner&gt;/&lt;repo&gt;</LunaDnsRepository>
```

(Ví dụ đã điền: `<LunaDnsRepository>lunavo/lunadns</LunaDnsRepository>`.)

- [ ] **Step 4:** `git remote add origin https://github.com/<owner>/<repo>.git` rồi `git push -u origin main`.

---

### Task 1: Publish profile và bản portable

**Files:**
- Create: `src/LunaDns.App/Properties/PublishProfiles/win-x64.pubxml`
- Create: `build/publish.ps1`
- Modify: `.gitignore` (thêm `artifacts/`)

**Interfaces:**
- Produces: `build/publish.ps1 [-Version <x.y.z>] [-SkipZip] [-SkipInstaller]`; sinh `artifacts/publish/win-x64/` (thư mục chạy được), `artifacts/LunaDNS-<v>-portable.zip`. T6 thêm Setup và SHA256 vào cùng script. `-SkipInstaller` ở T1 chưa có tác dụng (T6 mới có bước Setup) nhưng tham số có sẵn để T7 gọi.

- [x] **Step 1: Tạo publish profile**

`src/LunaDns.App/Properties/PublishProfiles/win-x64.pubxml`:

```xml
<Project>
  <!-- KH7: self-contained, single-file, không trim (K5), DLL native cạnh exe (K6). -->
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <IncludeNativeLibrariesForSelfExtract>false</IncludeNativeLibrariesForSelfExtract>
    <PublishTrimmed>false</PublishTrimmed>
    <PublishReadyToRun>false</PublishReadyToRun>
    <DebugType>embedded</DebugType>
  </PropertyGroup>
</Project>
```

- [x] **Step 2: Viết `build/publish.ps1` (phần portable)**

```powershell
<#
.SYNOPSIS
  Build LunaDNS bản phát hành (KH7). Nguồn duy nhất cho máy PO, GitHub Actions và Chay-tu-ma-nguon.cmd.
.OUTPUTS
  artifacts/publish/win-x64/          thư mục chạy được (exe + DLL native)
  artifacts/LunaDNS-<v>-portable.zip  trừ khi -SkipZip
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipZip,
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/LunaDns.App/LunaDns.App.csproj'
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish/win-x64'

if (-not $Version) {
    $Version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version phải dạng x.y.z, nhận được '$Version'." }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project -p:PublishProfile=win-x64 -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish lỗi ($LASTEXITCODE)." }

# Kiểm hình dạng bản build: exe single-file, không còn DLL managed của LunaDNS, không .pdb (DebugType=embedded).
$exe = Join-Path $publishDir 'LunaDNS.exe'
if (-not (Test-Path $exe)) { throw 'Thiếu LunaDNS.exe.' }
$stray = Get-ChildItem $publishDir -File | Where-Object { $_.Name -like 'LunaDns.*.dll' -or $_.Extension -eq '.pdb' }
if ($stray) { throw "Bản build không phải single-file: $($stray.Name -join ', ')" }
Get-ChildItem $publishDir -File | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }

if (-not $SkipZip) {
    $zip = Join-Path $artifacts "LunaDNS-$Version-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $staging = Join-Path $artifacts "LunaDNS-$Version"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    Copy-Item $publishDir $staging -Recurse
    Compress-Archive -Path $staging -DestinationPath $zip
    Remove-Item $staging -Recurse -Force
    '{0} {1:N1} MB' -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB)
}
```

- [x] **Step 3: Thêm `artifacts/` vào `.gitignore`**

Thêm một dòng `artifacts/` vào cuối `.gitignore` ở gốc repo (tạo file nếu chưa có).

- [x] **Step 4: Chạy và đo**

Run: `pwsh -NoProfile -File build/publish.ps1`
Expected: in danh sách file gồm `LunaDNS.exe` và vài DLL native (dự kiến `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`, có thể thêm DLL native của `Microsoft.Management.Infrastructure`), không có `LunaDns.*.dll`, có `artifacts/LunaDNS-0.5.0-portable.zip`. Ghi danh sách file và hai kích thước (thư mục, zip) vào memory KH7.

- [ ] **Step 5: Chạy thử bản publish**

Bấm đúp `artifacts/publish/win-x64/LunaDNS.exe` trên máy **chưa đặt** `DOTNET_ROOT` riêng (hoặc chạy trong Windows Sandbox, nơi không có .NET): UAC hiện, cửa sổ mở, bật/tắt Định tuyến DNS được, "Thoát & khôi phục" sạch. Rồi `artifacts/publish/win-x64/LunaDNS.exe --cleanup` trong PowerShell Admin trả `$LASTEXITCODE` 0.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.App/Properties/PublishProfiles/win-x64.pubxml build/publish.ps1 .gitignore
git commit -m "build: publish win-x64 self-contained single-file, zip portable"
```

---

### Task 2: ACL thư mục dữ liệu

**Files:**
- Create: `src/LunaDns.Platform.Windows/Install/DataDirectoryAcl.cs`
- Test: `tests/LunaDns.Platform.Windows.Tests/Install/DataDirectoryAclTests.cs`

**Interfaces:**
- Produces (namespace `LunaDns.Platform.Windows.Install`):
  - `public interface IDataDirectoryAcl { void Apply(string directory); bool IsCompliant(string directory); }`
  - `public sealed class DataDirectoryAcl : IDataDirectoryAcl` với `public static DirectorySecurity Build()`.

- [x] **Step 1: Viết test**

`tests/LunaDns.Platform.Windows.Tests/Install/DataDirectoryAclTests.cs`:

```csharp
using System.Security.AccessControl;
using System.Security.Principal;
using LunaDns.Platform.Windows.Install;

namespace LunaDns.Platform.Windows.Tests.Install;

public sealed class DataDirectoryAclTests
{
    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";
    private const string UsersSid = "S-1-5-32-545";

    [Fact]
    public void Build_is_protected_and_has_exactly_three_rules()
    {
        var security = DataDirectoryAcl.Build();

        Assert.True(security.AreAccessRulesProtected);
        var rules = Rules(security);
        Assert.Equal([AdministratorsSid, SystemSid, UsersSid], rules.Select(r => r.IdentityReference.Value).Order());
        Assert.All(rules, r => Assert.Equal(AccessControlType.Allow, r.AccessControlType));
        Assert.All(rules, r => Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, r.InheritanceFlags));
    }

    [Fact]
    public void Build_grants_full_control_only_to_system_and_administrators()
    {
        var rules = Rules(DataDirectoryAcl.Build());

        Assert.Equal(FileSystemRights.FullControl, Single(rules, SystemSid).FileSystemRights);
        Assert.Equal(FileSystemRights.FullControl, Single(rules, AdministratorsSid).FileSystemRights);
        Assert.Equal(FileSystemRights.ReadAndExecute, Single(rules, UsersSid).FileSystemRights & ~FileSystemRights.Synchronize);
    }

    [Fact]
    public void Build_sets_owner_to_administrators()
    {
        Assert.Equal(AdministratorsSid, DataDirectoryAcl.Build().GetOwner(typeof(SecurityIdentifier))!.Value);
    }

    [Fact]
    public void Temp_directory_with_default_acl_is_not_compliant()
    {
        using var temp = new TempDirectory();

        Assert.False(new DataDirectoryAcl().IsCompliant(temp.Path));
    }

    [Fact]
    public void Missing_directory_is_not_compliant()
    {
        using var temp = new TempDirectory();

        Assert.False(new DataDirectoryAcl().IsCompliant(temp.PathOf("missing")));
    }

    [AdminFact]
    public void Apply_makes_directory_and_existing_children_compliant()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), $"LunaDNS.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        File.WriteAllText(Path.Combine(dir, "journal.json"), "[]");
        File.WriteAllText(Path.Combine(dir, "logs", "a.log"), "x");
        try
        {
            var acl = new DataDirectoryAcl();
            Assert.False(acl.IsCompliant(dir));

            acl.Apply(dir);

            Assert.True(acl.IsCompliant(dir));
            File.WriteAllText(Path.Combine(dir, "new.json"), "{}");
            Assert.True(acl.IsCompliant(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static List<FileSystemAccessRule> Rules(FileSystemSecurity security) =>
        security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();

    private static FileSystemAccessRule Single(List<FileSystemAccessRule> rules, string sid) =>
        Assert.Single(rules, r => r.IdentityReference.Value == sid);
}
```

- [x] **Step 2: Chạy test để thấy lỗi**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests --filter FullyQualifiedName~DataDirectoryAclTests`
Expected: build lỗi, `DataDirectoryAcl` không tồn tại.

- [x] **Step 3: Cài đặt**

`src/LunaDns.Platform.Windows/Install/DataDirectoryAcl.cs`:

```csharp
using System.Security.AccessControl;
using System.Security.Principal;

namespace LunaDns.Platform.Windows.Install;

/// <summary>ACL của %ProgramData%\LunaDNS (spec 4.6, KH7 K9).</summary>
public interface IDataDirectoryAcl
{
    /// <summary>Đặt ACL cho thư mục và đưa mọi file, thư mục con về chỉ kế thừa. Cần quyền Admin.</summary>
    void Apply(string directory);

    /// <summary>Thư mục tồn tại, không kế thừa, và không ai ngoài SYSTEM/Administrators ghi được nó hay thứ bên trong.</summary>
    bool IsCompliant(string directory);
}

/// <summary>
/// Journal là đầu vào của task dọn dẹp chạy bằng SYSTEM (KH7 K7), nên chỉ SYSTEM và Administrators được ghi.
/// Users được đọc để mở thư mục log từ Explorer. Owner là Administrators: owner luôn đổi được DACL.
/// </summary>
public sealed class DataDirectoryAcl : IDataDirectoryAcl
{
    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    // Quyền cho phép ghi, xoá, đổi ACL hoặc owner; cộng GENERIC_ALL (0x10000000) và GENERIC_WRITE (0x40000000) chưa ánh xạ.
    private const FileSystemRights DangerousRights = FileSystemRights.Write | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership
        | (FileSystemRights)0x50000000;

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    public static DirectorySecurity Build()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, Inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    public void Apply(string directory)
    {
        var root = new DirectoryInfo(directory);
        root.SetAccessControl(Build());
        foreach (var child in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            switch (child)
            {
                case FileInfo file:
                    file.SetAccessControl(InheritOnly(new FileSecurity()));
                    break;
                case DirectoryInfo dir:
                    dir.SetAccessControl(InheritOnly(new DirectorySecurity()));
                    break;
            }
        }
    }

    public bool IsCompliant(string directory)
    {
        var root = new DirectoryInfo(directory);
        if (!root.Exists) return false;
        var security = root.GetAccessControl();
        if (!security.AreAccessRulesProtected || !IsSafe(security)) return false;
        foreach (var child in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            FileSystemSecurity childSecurity = child switch
            {
                FileInfo file => file.GetAccessControl(),
                DirectoryInfo dir => dir.GetAccessControl(),
                _ => throw new InvalidOperationException(child.GetType().Name),
            };
            if (!IsSafe(childSecurity)) return false;
        }
        return true;
    }

    /// <summary>DACL rỗng, không bảo vệ: chỉ còn quyền kế thừa từ thư mục cha. Owner đưa về Administrators.</summary>
    private static T InheritOnly<T>(T security) where T : FileSystemSecurity
    {
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
        security.SetOwner(Administrators);
        return security;
    }

    private static bool IsSafe(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (owner != LocalSystem && owner != Administrators) return false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference == LocalSystem || rule.IdentityReference == Administrators) continue;
            if ((rule.FileSystemRights & DangerousRights) != 0) return false;
        }
        return true;
    }
}
```

- [x] **Step 4: Chạy test** — (14/11: phần không-Admin xanh; test Admin chưa chạy: cần Admin)

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests --filter FullyQualifiedName~DataDirectoryAclTests`
Expected: 5 PASS, 1 skipped (AdminFact).

Run (PowerShell Admin): `$env:LUNADNS_ADMIN_TESTS=1; dotnet test tests/LunaDns.Platform.Windows.Tests --filter FullyQualifiedName~DataDirectoryAclTests`
Expected: 6 PASS. Nếu `Apply` ném `InvalidOperationException` khi đặt owner của file con thì đổi `InheritOnly` để không gọi `SetOwner` cho file con đã thuộc Administrators, rồi ghi lệch vào memory.

- [ ] **Step 5: Build sạch và commit**

Run: `dotnet build -warnaserror` → 0 cảnh báo.

```bash
git add src/LunaDns.Platform.Windows/Install/DataDirectoryAcl.cs tests/LunaDns.Platform.Windows.Tests/Install/DataDirectoryAclTests.cs
git commit -m "feat(platform): ACL %ProgramData%\LunaDNS chỉ SYSTEM/Admin ghi"
```

---

### Task 3: Task dọn dẹp chạy bằng SYSTEM

**Files:**
- Create: `src/LunaDns.Platform.Windows/Install/CleanupTaskDefinition.cs`
- Create: `src/LunaDns.Platform.Windows/Install/SchtasksCleanupTaskStore.cs`
- Test: `tests/LunaDns.Platform.Windows.Tests/Install/CleanupTaskDefinitionTests.cs`
- Test: `tests/LunaDns.Platform.Windows.Tests/Install/SchtasksCleanupTaskStoreAdminTests.cs`

**Interfaces:**
- Produces (namespace `LunaDns.Platform.Windows.Install`):
  - `public static class CleanupTaskDefinition { public const string TaskName = @"\LunaDNS\Cleanup"; public static string BuildXml(string exePath); }`
  - `public interface ICleanupTaskStore { bool Exists(); void Register(string exePath); void Delete(); }` — `Delete` idempotent; lỗi ném `InvalidOperationException`.
  - `public sealed class SchtasksCleanupTaskStore(string xmlDirectory, string taskName = CleanupTaskDefinition.TaskName) : ICleanupTaskStore`

- [x] **Step 1: Viết test XML**

`tests/LunaDns.Platform.Windows.Tests/Install/CleanupTaskDefinitionTests.cs`:

```csharp
using System.Xml.Linq;
using LunaDns.Platform.Windows.Install;

namespace LunaDns.Platform.Windows.Tests.Install;

public sealed class CleanupTaskDefinitionTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private const string Exe = @"C:\Program Files\LunaDNS\LunaDNS.exe";

    private static XElement Root() => XDocument.Parse(CleanupTaskDefinition.BuildXml(Exe)).Root!;

    [Fact]
    public void Declares_utf16_for_schtasks()
    {
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", CleanupTaskDefinition.BuildXml(Exe));
    }

    [Fact]
    public void Runs_as_system()
    {
        var principal = Root().Element(Ns + "Principals")!.Element(Ns + "Principal")!;
        Assert.Equal("S-1-5-18", principal.Element(Ns + "UserId")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")!.Value);
    }

    [Fact]
    public void Triggers_at_boot_and_logon()
    {
        var triggers = Root().Element(Ns + "Triggers")!.Elements().Select(e => e.Name.LocalName);
        Assert.Equal(["BootTrigger", "LogonTrigger"], triggers);
    }

    [Fact]
    public void Executes_quoted_exe_with_cleanup()
    {
        var exec = Root().Element(Ns + "Actions")!.Element(Ns + "Exec")!;
        Assert.Equal($"\"{Exe}\"", exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--cleanup", exec.Element(Ns + "Arguments")!.Value);
    }

    [Fact]
    public void Runs_on_battery_and_is_time_limited()
    {
        var settings = Root().Element(Ns + "Settings")!;
        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
        Assert.Equal("PT5M", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
        Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
    }

    [Fact]
    public void Rejects_relative_path()
    {
        Assert.Throws<ArgumentException>(() => CleanupTaskDefinition.BuildXml("LunaDNS.exe"));
    }
}
```

- [x] **Step 2: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests --filter FullyQualifiedName~CleanupTaskDefinitionTests`
Expected: build lỗi, `CleanupTaskDefinition` không tồn tại.

- [x] **Step 3: Cài đặt `CleanupTaskDefinition`**

`src/LunaDns.Platform.Windows/Install/CleanupTaskDefinition.cs`:

```csharp
using System.Xml.Linq;

namespace LunaDns.Platform.Windows.Install;

/// <summary>
/// XML của task \LunaDNS\Cleanup (spec 4.7, KH7 K7–K8): SYSTEM chạy "LunaDNS.exe --cleanup" lúc khởi động máy
/// và lúc có người đăng nhập (hive HKU\&lt;SID&gt; chỉ có khi đã đăng nhập, cần cho mục AutoConfigURL).
/// </summary>
public static class CleanupTaskDefinition
{
    public const string TaskName = @"\LunaDNS\Cleanup";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string BuildXml(string exePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(exePath);
        if (!Path.IsPathFullyQualified(exePath)) throw new ArgumentException("Cần đường dẫn tuyệt đối.", nameof(exePath));

        var task = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "Hoàn tác thay đổi mạng LunaDNS còn sót khi máy khởi động hoặc có người đăng nhập.")),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "BootTrigger", new XElement(Ns + "Enabled", "true")),
                new XElement(Ns + "LogonTrigger", new XElement(Ns + "Enabled", "true"))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", "S-1-5-18"),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "StartWhenAvailable", "true"),
                new XElement(Ns + "ExecutionTimeLimit", "PT5M"),
                new XElement(Ns + "Enabled", "true")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", $"\"{exePath}\""),
                    new XElement(Ns + "Arguments", "--cleanup"))));

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine + task;
    }
}
```

- [x] **Step 4: Chạy test XML**

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests --filter FullyQualifiedName~CleanupTaskDefinitionTests`
Expected: 6 PASS.

- [x] **Step 5: Viết test Admin cho store**

`tests/LunaDns.Platform.Windows.Tests/Install/SchtasksCleanupTaskStoreAdminTests.cs`:

```csharp
using LunaDns.Platform.Windows.Install;

namespace LunaDns.Platform.Windows.Tests.Install;

/// <summary>Đăng ký task thật dưới tên riêng của test để chắc schtasks nhận XML. Không bao giờ dùng \LunaDNS\Cleanup.</summary>
public sealed class SchtasksCleanupTaskStoreAdminTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SchtasksCleanupTaskStore _store;

    public SchtasksCleanupTaskStoreAdminTests()
    {
        _store = new SchtasksCleanupTaskStore(_temp.Path, $@"\LunaDNS.Tests\Cleanup-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        _store.Delete();
        _temp.Dispose();
    }

    [AdminFact]
    public void Register_then_delete_round_trips()
    {
        Assert.False(_store.Exists());

        _store.Register(Path.Combine(Environment.SystemDirectory, "whoami.exe"));

        Assert.True(_store.Exists());
        Assert.Empty(Directory.EnumerateFiles(_temp.Path));
        _store.Delete();
        Assert.False(_store.Exists());
    }

    [AdminFact]
    public void Register_twice_replaces()
    {
        var exe = Path.Combine(Environment.SystemDirectory, "whoami.exe");
        _store.Register(exe);
        _store.Register(exe);

        Assert.True(_store.Exists());
    }

    [AdminFact]
    public void Delete_missing_task_is_noop()
    {
        _store.Delete();
        Assert.False(_store.Exists());
    }
}
```

- [x] **Step 6: Cài đặt `SchtasksCleanupTaskStore`**

`src/LunaDns.Platform.Windows/Install/SchtasksCleanupTaskStore.cs`:

```csharp
using System.Diagnostics;
using System.Text;

namespace LunaDns.Platform.Windows.Install;

/// <summary>Đăng ký, kiểm và xoá task dọn dẹp (spec 4.7).</summary>
public interface ICleanupTaskStore
{
    bool Exists();

    /// <summary>Tạo hoặc thay task chạy exePath --cleanup bằng SYSTEM. Lỗi ném InvalidOperationException.</summary>
    void Register(string exePath);

    /// <summary>Xoá nếu có; không có thì không làm gì. Lỗi ném InvalidOperationException.</summary>
    void Delete();
}

/// <summary>
/// Gọi %SystemRoot%\System32\schtasks.exe (đường dẫn tuyệt đối, không tra PATH). XML tạm ghi trong xmlDirectory —
/// phải là %ProgramData%\LunaDNS đã ACL (KH7 K9), không phải %TEMP%: ai tráo được XML là có task SYSTEM.
/// </summary>
public sealed class SchtasksCleanupTaskStore(string xmlDirectory, string taskName = CleanupTaskDefinition.TaskName) : ICleanupTaskStore
{
    private static readonly string Schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    public bool Exists() => Run("/Query", "/TN", taskName).ExitCode == 0;

    public void Register(string exePath)
    {
        var xmlFile = Path.Combine(xmlDirectory, "cleanup-task.xml");
        File.WriteAllText(xmlFile, CleanupTaskDefinition.BuildXml(exePath), Encoding.Unicode);
        try
        {
            var result = Run("/Create", "/TN", taskName, "/XML", xmlFile, "/F");
            if (result.ExitCode != 0) throw new InvalidOperationException($"schtasks /Create trả {result.ExitCode}: {result.Error}");
        }
        finally
        {
            File.Delete(xmlFile);
        }
    }

    public void Delete()
    {
        if (!Exists()) return;
        var result = Run("/Delete", "/TN", taskName, "/F");
        if (result.ExitCode != 0) throw new InvalidOperationException($"schtasks /Delete trả {result.ExitCode}: {result.Error}");
    }

    private static (int ExitCode, string Error) Run(params string[] args)
    {
        var info = new ProcessStartInfo(Schtasks)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Không chạy được schtasks.exe.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        output.GetAwaiter().GetResult();
        return (process.ExitCode, error.GetAwaiter().GetResult().Trim());
    }
}
```

- [x] **Step 7: Chạy test** — (14/11: test XML xanh; 3 test Admin chưa chạy: cần Admin)

Run: `dotnet test tests/LunaDns.Platform.Windows.Tests --filter FullyQualifiedName~Install`
Expected: 11 PASS, 4 skipped.

Run (PowerShell Admin): `$env:LUNADNS_ADMIN_TESTS=1; dotnet test tests/LunaDns.Platform.Windows.Tests --filter FullyQualifiedName~Install`
Expected: 15 PASS. Sau đó `schtasks /Query /TN \LunaDNS.Tests\` không còn task nào (nếu còn thư mục rỗng `\LunaDNS.Tests` thì chấp nhận).

- [ ] **Step 8: Build sạch, commit**

```bash
git add src/LunaDns.Platform.Windows/Install tests/LunaDns.Platform.Windows.Tests/Install
git commit -m "feat(platform): task \LunaDNS\Cleanup chạy --cleanup bằng SYSTEM"
```

---

### Task 4: Lệnh `--install-system`, `--uninstall` và ACL mỗi lần chạy

**Files:**
- Modify: `src/LunaDns.App/CommandLine.cs`
- Modify: `src/LunaDns.App/CleanupCommand.cs`
- Modify: `src/LunaDns.App/Program.cs`
- Modify: `src/LunaDns.App/AppPaths.cs` (doc comment)
- Create: `src/LunaDns.App/InstallLocation.cs`, `src/LunaDns.App/InstallCommand.cs`, `src/LunaDns.App/UninstallCommand.cs`
- Test: `tests/LunaDns.App.Tests/CommandLineTests.cs` (thêm), `InstallLocationTests.cs`, `InstallCommandTests.cs`, `UninstallCommandTests.cs`, `FakeInstallParts.cs`

**Interfaces:**
- Consumes: `IDataDirectoryAcl`, `DataDirectoryAcl` (T2); `ICleanupTaskStore`, `SchtasksCleanupTaskStore` (T3).
- Produces:
  - `CommandKind.InstallSystem`, `CommandKind.Uninstall`; `CommandLine.InstallSystemSwitch = "--install-system"`, `CommandLine.UninstallSwitch = "--uninstall"`.
  - `internal static int CleanupCommand.RollbackJournal(AppPaths, Func<INetworkConfigurator>, ILogger)` — không lấy mutex.
  - `internal static class InstallLocation { bool IsUnder(string path, string directory); bool IsInstalled(string exePath, string programFiles); }`
  - `internal static partial class InstallCommand { const int WrongLocation = 3; int Run(AppPaths, string exePath, string programFiles, IDataDirectoryAcl, ICleanupTaskStore, ILogger); }`
  - `internal static partial class UninstallCommand { const int AppRunning = 4; int Run(AppPaths, Func<INetworkConfigurator>, ICleanupTaskStore, ILogger, string mutexName = SingleInstance.DefaultName); bool TryDeleteData(AppPaths); }`

**Bẫy cần biết:** `SingleInstance.TryAcquire` dùng cờ `createdNew`. Trong cùng tiến trình, khi đã giữ mutex mà gọi `TryAcquire` lần nữa thì lần đó trả `null`. Vì thế `UninstallCommand` (giữ mutex) không được gọi `CleanupCommand.Run` (lấy mutex lần nữa sẽ tưởng app đang chạy và trả 0 mà không hoàn tác gì). Phải gọi `RollbackJournal`.

- [x] **Step 1: Viết test**

Thêm vào `tests/LunaDns.App.Tests/CommandLineTests.cs` (theo kiểu test sẵn có trong file):

```csharp
    [Theory]
    [InlineData("--install-system", CommandKind.InstallSystem)]
    [InlineData("--INSTALL-SYSTEM", CommandKind.InstallSystem)]
    [InlineData("--uninstall", CommandKind.Uninstall)]
    public void Parses_install_switches(string arg, CommandKind expected)
    {
        Assert.Equal(expected, CommandLine.Parse([arg]));
    }

    [Fact]
    public void Install_switch_with_extra_argument_is_invalid()
    {
        Assert.Equal(CommandKind.Invalid, CommandLine.Parse(["--install-system", "x"]));
    }
```

`tests/LunaDns.App.Tests/FakeInstallParts.cs`:

```csharp
using LunaDns.Platform.Windows.Install;

namespace LunaDns.App.Tests;

internal sealed class FakeDataDirectoryAcl : IDataDirectoryAcl
{
    public List<string> Applied { get; } = [];

    public Exception? ApplyError { get; set; }

    public void Apply(string directory)
    {
        if (ApplyError is not null) throw ApplyError;
        Applied.Add(directory);
    }

    public bool IsCompliant(string directory) => Applied.Contains(directory);
}

internal sealed class FakeCleanupTaskStore : ICleanupTaskStore
{
    public string? RegisteredExe { get; private set; }

    public Exception? Error { get; set; }

    public List<string> Calls { get; } = [];

    public bool Exists() => RegisteredExe is not null;

    public void Register(string exePath)
    {
        Calls.Add("Register");
        if (Error is not null) throw Error;
        RegisteredExe = exePath;
    }

    public void Delete()
    {
        Calls.Add("Delete");
        if (Error is not null) throw Error;
        RegisteredExe = null;
    }
}
```

`tests/LunaDns.App.Tests/InstallLocationTests.cs`:

```csharp
namespace LunaDns.App.Tests;

public sealed class InstallLocationTests
{
    [Theory]
    [InlineData(@"C:\Program Files\LunaDNS\LunaDNS.exe", @"C:\Program Files", true)]
    [InlineData(@"c:\program files\lunadns\LunaDNS.exe", @"C:\Program Files\", true)]
    [InlineData(@"C:\Program Files Evil\LunaDNS.exe", @"C:\Program Files", false)]
    [InlineData(@"C:\Users\a\Downloads\LunaDNS\LunaDNS.exe", @"C:\Program Files", false)]
    [InlineData(@"C:\Program Files\..\Users\a\LunaDNS.exe", @"C:\Program Files", false)]
    public void IsUnder_compares_full_paths_with_separator(string path, string directory, bool expected)
    {
        Assert.Equal(expected, InstallLocation.IsUnder(path, directory));
    }

    [Fact]
    public void IsInstalled_needs_uninstaller_next_to_exe()
    {
        using var temp = new TempDirectory();
        var exe = temp.PathOf(@"LunaDNS\LunaDNS.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");

        Assert.False(InstallLocation.IsInstalled(exe, temp.PathOf("")));
        File.WriteAllText(temp.PathOf(@"LunaDNS\unins000.exe"), "");
        Assert.True(InstallLocation.IsInstalled(exe, temp.PathOf("")));
    }
}
```

(Nếu `TempDirectory` của App.Tests không có `PathOf` nhận chuỗi rỗng, dùng `temp.Path` cho đối số thứ hai — kiểm API trong `tests/LunaDns.App.Tests/TempDirectory.cs` trước.)

`tests/LunaDns.App.Tests/InstallCommandTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class InstallCommandTests : IDisposable
{
    private const string ProgramFiles = @"C:\Program Files";
    private const string InstalledExe = @"C:\Program Files\LunaDNS\LunaDNS.exe";
    private readonly TestApp _app = new();
    private readonly FakeDataDirectoryAcl _acl = new();
    private readonly FakeCleanupTaskStore _tasks = new();

    public void Dispose() => _app.Dispose();

    private int Run(string exe) => InstallCommand.Run(_app.Paths, exe, ProgramFiles, _acl, _tasks, NullLogger.Instance);

    [Fact]
    public void Refuses_outside_program_files_and_changes_nothing()
    {
        Assert.Equal(InstallCommand.WrongLocation, Run(@"C:\Users\a\Downloads\LunaDNS\LunaDNS.exe"));
        Assert.Empty(_acl.Applied);
        Assert.Empty(_tasks.Calls);
    }

    [Fact]
    public void Applies_acl_before_registering_task()
    {
        Assert.Equal(0, Run(InstalledExe));
        Assert.Equal([_app.Paths.DataDirectory], _acl.Applied);
        Assert.Equal(InstalledExe, _tasks.RegisteredExe);
    }

    [Fact]
    public void Acl_failure_returns_1_without_task()
    {
        _acl.ApplyError = new UnauthorizedAccessException();

        Assert.Equal(1, Run(InstalledExe));
        Assert.Empty(_tasks.Calls);
    }

    [Fact]
    public void Task_failure_returns_1()
    {
        _tasks.Error = new InvalidOperationException("schtasks");

        Assert.Equal(1, Run(InstalledExe));
    }
}
```

`tests/LunaDns.App.Tests/UninstallCommandTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class UninstallCommandTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly FakeCleanupTaskStore _tasks = new();

    public void Dispose() => _app.Dispose();

    private int Run() => UninstallCommand.Run(_app.Paths, () => _app.Configurator, _tasks, NullLogger.Instance, _app.MutexName);

    [Fact]
    public void Refuses_while_app_is_running()
    {
        _app.SeedJournal();
        using var running = SingleInstance.TryAcquire(_app.MutexName);

        Assert.Equal(UninstallCommand.AppRunning, Run());
        Assert.Equal(1, _app.JournalCount());
        Assert.Empty(_tasks.Calls);
    }

    [Fact]
    public void Reverts_journal_then_deletes_task()
    {
        _app.SeedJournal();
        _tasks.Register(@"C:\Program Files\LunaDNS\LunaDNS.exe");

        Assert.Equal(0, Run());
        Assert.Equal(1, _app.Configurator.Reverts);
        Assert.Equal(0, _app.JournalCount());
        Assert.False(_tasks.Exists());
    }

    [Fact]
    public void Failed_revert_keeps_journal_and_task()
    {
        _app.SeedJournal();
        _tasks.Register(@"C:\Program Files\LunaDNS\LunaDNS.exe");
        _app.Configurator.FailReverts = true;

        Assert.Equal(1, Run());
        Assert.Equal(1, _app.JournalCount());
        Assert.True(_tasks.Exists());
    }

    [Fact]
    public void Task_delete_failure_returns_1()
    {
        _tasks.Error = new InvalidOperationException("schtasks");

        Assert.Equal(1, Run());
    }

    [Fact]
    public void Without_journal_or_task_succeeds()
    {
        Assert.Equal(0, Run());
    }

    [Fact]
    public void TryDeleteData_removes_data_directory()
    {
        _app.SeedJournal();

        Assert.True(UninstallCommand.TryDeleteData(_app.Paths));
        Assert.False(Directory.Exists(_app.Paths.DataDirectory));
        Assert.True(UninstallCommand.TryDeleteData(_app.Paths));
    }
}
```

(`FakeNetworkConfigurator` cần có cờ làm revert lỗi. Kiểm `tests/LunaDns.App.Tests/FakeNetworkConfigurator.cs` và `CleanupCommandTests` xem cờ đó tên gì — dùng đúng tên có sẵn; nếu chưa có thì thêm `public bool FailReverts { get; set; }` làm mọi `Revert*` ném `IOException`.)

- [ ] **Step 2: Chạy để thấy lỗi** (bỏ qua bước đỏ, viết mã rồi chạy xanh)

Run: `dotnet test tests/LunaDns.App.Tests`
Expected: build lỗi (`CommandKind.InstallSystem`, `InstallLocation`, `InstallCommand`, `UninstallCommand` chưa có).

- [x] **Step 3: Sửa `CommandLine.cs`**

```csharp
namespace LunaDns.App;

internal enum CommandKind
{
    Gui,
    Cleanup,
    Report,
    InstallSystem,
    Uninstall,
    Invalid,
}

/// <summary>
/// LunaDNS.exe | --cleanup | --report (spec 4.5, 4.9) | --install-system | --uninstall (KH7, Setup gọi). Không phân biệt hoa thường.
/// </summary>
internal static class CommandLine
{
    public const string CleanupSwitch = "--cleanup";
    public const string ReportSwitch = "--report";
    public const string InstallSystemSwitch = "--install-system";
    public const string UninstallSwitch = "--uninstall";

    public static CommandKind Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args switch
        {
            [] => CommandKind.Gui,
            [var only] when Is(only, CleanupSwitch) => CommandKind.Cleanup,
            [var only] when Is(only, ReportSwitch) => CommandKind.Report,
            [var only] when Is(only, InstallSystemSwitch) => CommandKind.InstallSystem,
            [var only] when Is(only, UninstallSwitch) => CommandKind.Uninstall,
            _ => CommandKind.Invalid,
        };
    }

    private static bool Is(string arg, string expected) => string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 4: Tách `RollbackJournal` trong `CleanupCommand.cs`**

Thay thân `Run` bằng:

```csharp
    public static int Run(AppPaths paths, Func<INetworkConfigurator> configurator, ILogger logger, string mutexName = SingleInstance.DefaultName)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(logger);
        using var instance = SingleInstance.TryAcquire(mutexName);
        if (instance is null)
        {
            LogAppRunning(logger);
            return 0;
        }
        return RollbackJournal(paths, configurator, logger);
    }

    /// <summary>Hoàn tác journal, không lấy mutex: bên gọi phải đang giữ mutex (UninstallCommand). Mã như Run.</summary>
    internal static int RollbackJournal(AppPaths paths, Func<INetworkConfigurator> configurator, ILogger logger)
    {
        if (!File.Exists(paths.JournalFile))
        {
            LogNothingToDo(logger);
            return 0;
        }

        var journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile), SystemChanges.Reverters(configurator()));
        if (journal.Entries.Count == 0)
        {
            LogNothingToDo(logger);
            return 0;
        }
        var result = journal.RollbackAll();
        foreach (var failure in result.Failed) LogRevertFailed(logger, failure.Entry.Feature, failure.Entry.Op, failure.Error);
        LogDone(logger, result.Reverted, result.Failed.Count);
        return result.Success ? 0 : 1;
    }
```

- [x] **Step 5: Tạo `InstallLocation.cs`**

```csharp
namespace LunaDns.App;

/// <summary>Exe có nằm trong thư mục chỉ Admin ghi được không (KH7 K7). So đường dẫn đầy đủ, có dấu phân cách cuối.</summary>
internal static class InstallLocation
{
    public static bool IsUnder(string path, string directory)
    {
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bản do Setup cài: dưới Program Files và có unins000.exe (Inno Setup) cạnh exe.</summary>
    public static bool IsInstalled(string exePath, string programFiles) =>
        IsUnder(exePath, programFiles) && File.Exists(Path.Combine(Path.GetDirectoryName(exePath)!, "unins000.exe"));
}
```

- [x] **Step 6: Tạo `InstallCommand.cs`**

```csharp
using System.ComponentModel;
using LunaDns.Platform.Windows.Install;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// LunaDNS.exe --install-system (Setup gọi sau khi chép file): ACL thư mục dữ liệu, rồi đăng ký task SYSTEM (spec 4.6–4.7).
/// Mã thoát: 0 = xong; 1 = lỗi (xem log); 3 = exe không nằm dưới Program Files, không đổi gì (KH7 K7).
/// </summary>
internal static partial class InstallCommand
{
    public const int WrongLocation = 3;

    public static int Run(AppPaths paths, string exePath, string programFiles, IDataDirectoryAcl acl, ICleanupTaskStore tasks, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(acl);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(logger);
        if (!InstallLocation.IsUnder(exePath, programFiles))
        {
            LogWrongLocation(logger);
            return WrongLocation;
        }
        try
        {
            // ACL trước: XML của task được ghi vào chính thư mục này.
            acl.Apply(paths.DataDirectory);
            tasks.Register(exePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            LogFailed(logger, ex);
            return 1;
        }
        LogInstalled(logger);
        return 0;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "--install-system: exe không nằm trong Program Files, bỏ qua.")]
    private static partial void LogWrongLocation(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--install-system: không cài được phần hệ thống.")]
    private static partial void LogFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "--install-system: đã đặt ACL và đăng ký task dọn dẹp.")]
    private static partial void LogInstalled(ILogger logger);
}
```

- [x] **Step 7: Tạo `UninstallCommand.cs`**

```csharp
using System.ComponentModel;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Install;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// LunaDNS.exe --uninstall (trình gỡ của Setup gọi; bản portable chạy tay): hoàn tác journal, xoá task SYSTEM.
/// Hoàn tác lỗi thì giữ journal và task để lần sau làm tiếp. Xoá thư mục dữ liệu là bước riêng (TryDeleteData),
/// chạy sau khi đóng logger vì log nằm trong thư mục đó.
/// Mã thoát: 0 = xong; 1 = còn việc chưa xong (xem log); 4 = LunaDNS đang chạy.
/// </summary>
internal static partial class UninstallCommand
{
    public const int AppRunning = 4;

    public static int Run(AppPaths paths, Func<INetworkConfigurator> configurator, ICleanupTaskStore tasks, ILogger logger, string mutexName = SingleInstance.DefaultName)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(logger);
        using var instance = SingleInstance.TryAcquire(mutexName);
        if (instance is null)
        {
            LogAppRunning(logger);
            return AppRunning;
        }
        // Không gọi CleanupCommand.Run: nó lấy lại mutex đang giữ, nhận null và bỏ qua (xem SingleInstance).
        if (CleanupCommand.RollbackJournal(paths, configurator, logger) != 0)
        {
            LogKept(logger);
            return 1;
        }
        try
        {
            tasks.Delete();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception)
        {
            LogTaskFailed(logger, ex);
            return 1;
        }
        LogDone(logger);
        return 0;
    }

    public static bool TryDeleteData(AppPaths paths)
    {
        try
        {
            if (Directory.Exists(paths.DataDirectory)) Directory.Delete(paths.DataDirectory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "--uninstall: LunaDNS đang chạy, không gỡ.")]
    private static partial void LogAppRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--uninstall: còn mục chưa hoàn tác, giữ journal và task dọn dẹp.")]
    private static partial void LogKept(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--uninstall: không xoá được task dọn dẹp.")]
    private static partial void LogTaskFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "--uninstall: đã hoàn tác và xoá task dọn dẹp.")]
    private static partial void LogDone(ILogger logger);
}
```

- [x] **Step 8: Nối vào `Program.cs`**

Sửa doc comment mã thoát và `Main`; thêm hai hàm `Run*` và ACL mỗi lần chạy:

```csharp
    /// <summary>
    /// Mã thoát: 0 = xong, 1 = còn việc chưa xong (xem log), 2 = tham số sai (WinExe không có console để in cách dùng),
    /// 3 = --install-system ngoài Program Files, 4 = --uninstall khi app đang chạy.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = AppPaths.Default();
        paths.EnsureCreated();
        // Trước mọi lệnh và trước khi đọc journal (KH7 K9). Lỗi không chặn; giao diện ghi log.
        var aclError = TryProtectDataDirectory(paths);
        return CommandLine.Parse(args) switch
        {
            CommandKind.Gui => RunGui(paths, args, aclError),
            CommandKind.Cleanup => RunCleanup(paths),
            CommandKind.Report => RunReport(paths),
            CommandKind.InstallSystem => RunInstallSystem(paths),
            CommandKind.Uninstall => RunUninstall(paths),
            _ => 2,
        };
    }

    private static Exception? TryProtectDataDirectory(AppPaths paths)
    {
        try
        {
            new DataDirectoryAcl().Apply(paths.DataDirectory);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PrivilegeNotHeldException)
        {
            return ex;
        }
    }

    private static int RunInstallSystem(AppPaths paths)
    {
        using var loggerFactory = AppServices.CreateLoggerFactory(paths);
        return InstallCommand.Run(paths, Environment.ProcessPath!, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            new DataDirectoryAcl(), new SchtasksCleanupTaskStore(paths.DataDirectory), loggerFactory.CreateLogger(nameof(InstallCommand)));
    }

    private static int RunUninstall(AppPaths paths)
    {
        int code;
        using (var loggerFactory = AppServices.CreateLoggerFactory(paths))
        {
            code = UninstallCommand.Run(paths, () => WindowsNetworkConfigurator.CreateDefault(loggerFactory),
                new SchtasksCleanupTaskStore(paths.DataDirectory), loggerFactory.CreateLogger(nameof(UninstallCommand)));
        }
        // Log nằm trong thư mục dữ liệu: đóng logger rồi mới xoá.
        if (code == 0 && !UninstallCommand.TryDeleteData(paths)) code = 1;
        return code;
    }
```

Trong `RunGui`, đổi chữ ký thành `RunGui(AppPaths paths, string[] args, Exception? aclError)` và ngay sau `var services = AppServices.CreateDefault(paths);` thêm:

```csharp
        if (aclError is not null) LogAclFailed(services.LoggerFactory.CreateLogger(nameof(Program)), aclError);
```

Thêm vào cuối lớp:

```csharp
    [LoggerMessage(Level = LogLevel.Warning, Message = "Không đặt được ACL cho thư mục dữ liệu; chạy lại Setup hoặc LunaDNS bằng quyền Admin.")]
    private static partial void LogAclFailed(ILogger logger, Exception error);
```

Thêm `using System.Security.AccessControl;` (cho `PrivilegeNotHeldException`) và `using LunaDns.Platform.Windows.Install;`.

**Lưu ý thứ tự:** `AppServices.CreateDefault` hoàn tác journal còn sót, nên ACL phải xong trước nó — `Main` đã đảm bảo.

- [x] **Step 9: Sửa doc comment `AppPaths.cs`**

Thay hai dòng đầu doc comment bằng:

```csharp
/// <summary>
/// Journal, settings và log nằm trong %ProgramData%\LunaDNS (spec 4.5). Task dọn dẹp chạy bằng SYSTEM (KH7) đọc cùng chỗ này.
/// Mỗi lần chạy, Program đặt ACL chỉ SYSTEM/Admin ghi được (KH7 K9). Báo cáo ghi ra Desktop.
/// </summary>
```

- [x] **Step 10: Chạy test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.App.Tests`
Expected: 0 cảnh báo; mọi test PASS (cũ + 20 mới).

- [ ] **Step 11: Thử thật (PowerShell Admin, bản Debug)** (chưa chạy: cần Admin)

```powershell
$exe = "src\LunaDns.App\bin\Debug\net8.0-windows\LunaDNS.exe"
& $exe --install-system; $LASTEXITCODE        # 3: bản Debug không nằm trong Program Files
(Get-Acl $env:ProgramData\LunaDNS).Access | Format-Table IdentityReference,FileSystemRights,IsInherited
```

Expected: mã 3; ACL chỉ còn `NT AUTHORITY\SYSTEM` FullControl, `BUILTIN\Administrators` FullControl, `BUILTIN\Users` ReadAndExecute, không dòng nào `IsInherited=True` ở thư mục gốc. (ACL được đặt dù mã 3, vì `Main` áp ACL trước mọi lệnh.)

- [ ] **Step 12: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests
git commit -m "feat(app): --install-system, --uninstall; ACL thư mục dữ liệu mỗi lần chạy"
```

---

### Task 5: Kiểm tra cập nhật (logic)

**Files:**
- Modify: `src/LunaDns.App/LunaDns.App.csproj`
- Create: `src/LunaDns.App/AppInfo.cs`
- Create: `src/LunaDns.App/Updates/GitHubRelease.cs`
- Create: `src/LunaDns.App/Updates/UpdateChecker.cs`
- Test: `tests/LunaDns.App.Tests/UpdateCheckerTests.cs`

**Interfaces:**
- Consumes: `<LunaDnsRepository>` (T0).
- Produces:
  - `internal static class AppInfo { Version Version; string Repository; }`
  - `internal enum UpdateStatus { UpToDate, NewerAvailable, Failed }`
  - `internal sealed record UpdateCheckResult(UpdateStatus Status, Version? Latest, string? Url)`
  - `internal sealed class UpdateChecker(HttpClient http, string repository, Version current) { Task<UpdateCheckResult> CheckAsync(CancellationToken); static Version? ParseTag(string); }`

- [x] **Step 1: Thêm metadata vào csproj**

Trong `src/LunaDns.App/LunaDns.App.csproj`, thêm vào `ItemGroup` có `InternalsVisibleTo`:

```xml
    <AssemblyMetadata Include="GitHubRepository" Value="$(LunaDnsRepository)" />
```

- [x] **Step 2: Viết test**

`tests/LunaDns.App.Tests/UpdateCheckerTests.cs`:

```csharp
using System.Net;
using System.Text;
using LunaDns.App.Updates;

namespace LunaDns.App.Tests;

public sealed class UpdateCheckerTests
{
    private const string Repo = "owner/lunadns";

    private static UpdateChecker Checker(string current, HttpStatusCode status, string body, List<HttpRequestMessage>? seen = null) =>
        new(new HttpClient(new StubHandler(status, body, seen)), Repo, Version.Parse(current));

    private static string Release(string tag, bool draft = false, bool prerelease = false) =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/{{Repo}}/releases/tag/{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}}}""";

    [Theory]
    [InlineData("v0.9.1", 0, 9, 1)]
    [InlineData("v10.0.0", 10, 0, 0)]
    public void ParseTag_accepts_v_major_minor_patch(string tag, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), UpdateChecker.ParseTag(tag));
    }

    [Theory]
    [InlineData("0.9.1")]
    [InlineData("v0.9")]
    [InlineData("v0.9.1-beta")]
    [InlineData("nightly")]
    public void ParseTag_rejects_other_shapes(string tag)
    {
        Assert.Null(UpdateChecker.ParseTag(tag));
    }

    [Fact]
    public async Task Newer_prerelease_counts_and_drafts_are_ignored()
    {
        var body = $"[{Release("v0.9.3", draft: true)},{Release("v0.9.2", prerelease: true)},{Release("v0.9.0")}]";

        var result = await Checker("0.9.0", HttpStatusCode.OK, body).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.NewerAvailable, result.Status);
        Assert.Equal(new Version(0, 9, 2), result.Latest);
        Assert.Equal($"https://github.com/{Repo}/releases/tag/v0.9.2", result.Url);
    }

    [Fact]
    public async Task Same_version_is_up_to_date()
    {
        var result = await Checker("0.9.0", HttpStatusCode.OK, $"[{Release("v0.9.0")}]").CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Four_part_assembly_version_compares_as_three_parts()
    {
        var result = await Checker("0.9.0.0", HttpStatusCode.OK, $"[{Release("v0.9.0")}]").CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Url_outside_repo_releases_is_rejected()
    {
        var body = """[{"tag_name":"v1.0.0","html_url":"https://evil.example/x","draft":false,"prerelease":false}]""";

        var result = await Checker("0.9.0", HttpStatusCode.OK, body).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "[]")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "[]")]
    public async Task Errors_and_empty_lists_fail_softly(HttpStatusCode status, string body)
    {
        var result = await Checker("0.9.0", status, body).CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Sends_user_agent_to_releases_endpoint()
    {
        var seen = new List<HttpRequestMessage>();

        await Checker("0.9.0", HttpStatusCode.OK, "[]", seen).CheckAsync(CancellationToken.None);

        var request = Assert.Single(seen);
        Assert.Equal($"https://api.github.com/repos/{Repo}/releases?per_page=20", request.RequestUri!.ToString());
        Assert.Contains("LunaDNS", request.Headers.UserAgent.ToString());
    }

    private sealed class StubHandler(HttpStatusCode status, string body, List<HttpRequestMessage>? seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen?.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
```

- [ ] **Step 3: Chạy để thấy lỗi** (bỏ qua bước đỏ, viết mã rồi chạy xanh)

Run: `dotnet test tests/LunaDns.App.Tests --filter FullyQualifiedName~UpdateCheckerTests`
Expected: build lỗi, `LunaDns.App.Updates` không tồn tại.

- [x] **Step 4: Cài đặt**

`src/LunaDns.App/AppInfo.cs`:

```csharp
using System.Reflection;

namespace LunaDns.App;

/// <summary>Phiên bản (từ &lt;Version&gt; của csproj) và repo GitHub (&lt;LunaDnsRepository&gt;, KH7 T0).</summary>
internal static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static Version Version { get; } = Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string Repository { get; } =
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "GitHubRepository").Value
        ?? throw new InvalidOperationException("Thiếu <LunaDnsRepository> trong csproj.");
}
```

`src/LunaDns.App/Updates/GitHubRelease.cs`:

```csharp
using System.Text.Json.Serialization;

namespace LunaDns.App.Updates;

/// <summary>Các trường cần của GET /repos/{repo}/releases.</summary>
internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("draft")] bool Draft);

/// <summary>Serializer sinh sẵn như settings.json (KH5 quyết định 16), để không thêm reflection mới.</summary>
[JsonSerializable(typeof(GitHubRelease[]))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
```

`src/LunaDns.App/Updates/UpdateChecker.cs`:

```csharp
using System.Text.Json;

namespace LunaDns.App.Updates;

internal enum UpdateStatus
{
    UpToDate,
    NewerAvailable,
    Failed,
}

internal sealed record UpdateCheckResult(UpdateStatus Status, Version? Latest, string? Url)
{
    public static readonly UpdateCheckResult Failed = new(UpdateStatus.Failed, null, null);
}

/// <summary>
/// Spec 4.10, KH7 K10: lấy tag vX.Y.Z lớn nhất không phải draft (kể cả prerelease) trong 20 Release mới nhất.
/// Chỉ trả URL nằm dưới https://github.com/&lt;repo&gt;/releases/. Lỗi mạng hay dữ liệu lạ đều thành Failed, không ném.
/// </summary>
internal sealed class UpdateChecker(HttpClient http, string repository, Version current)
{
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        GitHubRelease[] releases;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=20");
            request.Headers.UserAgent.ParseAdd($"LunaDNS/{Normalize(current)}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return UpdateCheckResult.Failed;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            releases = await JsonSerializer.DeserializeAsync(stream, UpdateJsonContext.Default.GitHubReleaseArray, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return UpdateCheckResult.Failed;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout của HttpClient; huỷ do người gọi thì để OperationCanceledException đi tiếp.
            return UpdateCheckResult.Failed;
        }

        var candidates = releases
            .Where(r => !r.Draft)
            .Select(r => (Version: ParseTag(r.TagName), Release: r))
            .Where(x => x.Version is not null)
            .ToList();
        // MaxBy trên bộ giá trị (struct) ném khi rỗng, nên kiểm trước (sửa 14/11 khi thực thi).
        if (candidates.Count == 0) return UpdateCheckResult.Failed;
        var latest = candidates.MaxBy(x => x.Version);
        if (!latest.Release.HtmlUrl.StartsWith($"https://github.com/{repository}/releases/", StringComparison.Ordinal)) return UpdateCheckResult.Failed;

        return latest.Version > Normalize(current)
            ? new UpdateCheckResult(UpdateStatus.NewerAvailable, latest.Version, latest.Release.HtmlUrl)
            : new UpdateCheckResult(UpdateStatus.UpToDate, latest.Version, latest.Release.HtmlUrl);
    }

    /// <summary>Chỉ nhận đúng "vX.Y.Z".</summary>
    public static Version? ParseTag(string tag) =>
        tag.Length > 1 && tag[0] == 'v' && tag.Count(c => c == '.') == 2 && Version.TryParse(tag[1..], out var version) ? version : null;

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
```

Ghi chú: `Version.TryParse("0.9.1-beta")` trả false nên tag có hậu tố bị loại.

- [x] **Step 5: Chạy test**

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.App.Tests --filter FullyQualifiedName~UpdateCheckerTests`
Expected: 0 cảnh báo, 14 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/LunaDns.App/LunaDns.App.csproj src/LunaDns.App/AppInfo.cs src/LunaDns.App/Updates tests/LunaDns.App.Tests/UpdateCheckerTests.cs
git commit -m "feat(app): UpdateChecker đọc GitHub Releases"
```

---

### Task 6: Setup (Inno Setup), SHA256 và Windows Sandbox

**Files:**
- Create: `build/installer/LunaDNS.iss` (lưu UTF-8 **có BOM**, để Inno đọc đúng tiếng Việt)
- Modify: `build/publish.ps1` (thêm bước Setup + SHA256)
- Create: `build/sandbox/LunaDNS.wsb`

**Interfaces:**
- Consumes: `--install-system` (mã 0/1/3), `--uninstall` (mã 0/1/4), `--cleanup` (T4); mutex `Global\LunaDNS`.
- Produces: `artifacts/LunaDNS-Setup-<v>.exe`, `artifacts/SHA256SUMS.txt`.

- [x] **Step 1: Cài Inno Setup 6 trên máy dev** (đã có sẵn ISCC.exe, không cần cài)

Run: `winget install --id JRSoftware.InnoSetup -e` (runner `windows-latest` của GitHub đã có sẵn). Kiểm: `Test-Path "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"` → `True`.

- [x] **Step 2: Viết `build/installer/LunaDNS.iss`**

```iss
; LunaDNS Setup (KH7). Gọi từ build/publish.ps1:
;   ISCC.exe /DAppVersion=0.9.0 /DPublishDir=<artifacts\publish\win-x64> /O<artifacts> LunaDNS.iss
#ifndef AppVersion
  #error Thiếu /DAppVersion
#endif
#ifndef PublishDir
  #error Thiếu /DPublishDir
#endif

[Setup]
AppId={{6F3C2B9E-1D4A-4E7B-9C55-5A0E8D2F7B31}
AppName=LunaDNS
AppVersion={#AppVersion}
AppVerName=LunaDNS {#AppVersion}
AppPublisher=LunaDNS
AppPublisherURL=https://github.com/
DefaultDirName={autopf}\LunaDNS
; Thư mục cố định trong Program Files: --install-system từ chối chỗ khác (K7).
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex=LunaDNS,Global\LunaDNS
CloseApplications=no
OutputBaseFilename=LunaDNS-Setup-{#AppVersion}
SetupIconFile=..\..\src\LunaDns.App\Assets\lunadns.ico
UninstallDisplayIcon={app}\LunaDNS.exe
UninstallDisplayName=LunaDNS
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
en.OldCleanupFailed=Không khôi phục được cấu hình mạng của bản LunaDNS đang cài (mã %1). Mở LunaDNS, bấm "Tắt hết & khôi phục", rồi chạy lại Setup. Chi tiết: %ProgramData%\LunaDNS\logs.
en.InstallSystemFailed=LunaDNS đã cài, nhưng chưa đăng ký được tác vụ dọn dẹp lúc khởi động máy (mã %1). App vẫn chạy được. Chạy lại Setup để thử lại. Chi tiết: %ProgramData%\LunaDNS\logs.
en.UninstallFailed=Chưa khôi phục được hết cấu hình mạng (mã %1), nên chưa gỡ LunaDNS. Xem hướng dẫn "Gỡ và dọn" (docs/guide/07) rồi thử lại.
en.LaunchApp=Mở LunaDNS

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\LunaDNS"; Filename: "{app}\LunaDNS.exe"

[Run]
Filename: "{app}\LunaDNS.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait skipifsilent

[Code]
function RunLuna(const Exe, Args: String): Integer;
var
  Code: Integer;
begin
  if not Exec(Exe, Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Result := Code;
end;

{ K11: hoàn tác bằng exe cũ trước khi ghi đè. Không có bản cũ thì bỏ qua. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  OldExe: String;
  Code: Integer;
begin
  Result := '';
  OldExe := ExpandConstant('{app}\LunaDNS.exe');
  if FileExists(OldExe) then
  begin
    Code := RunLuna(OldExe, '--cleanup');
    if Code <> 0 then
      Result := FmtMessage(CustomMessage('OldCleanupFailed'), [IntToStr(Code)]);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    Code := RunLuna(ExpandConstant('{app}\LunaDNS.exe'), '--install-system');
    if Code <> 0 then
      SuppressibleMsgBox(FmtMessage(CustomMessage('InstallSystemFailed'), [IntToStr(Code)]), mbError, MB_OK, IDOK);
  end;
end;

{ Hoàn tác + xoá task + xoá %ProgramData%\LunaDNS trước khi xoá file. Lỗi thì dừng gỡ, giữ exe để thử lại. }
function InitializeUninstall(): Boolean;
var
  Code: Integer;
begin
  Result := True;
  Code := RunLuna(ExpandConstant('{app}\LunaDNS.exe'), '--uninstall');
  if Code <> 0 then
  begin
    MsgBox(FmtMessage(CustomMessage('UninstallFailed'), [IntToStr(Code)]), mbError, MB_OK);
    Result := False;
  end;
end;
```

Ghi chú: `AppPublisherURL` để `https://github.com/` cho tới T0; sau T0 đổi thành `https://github.com/<owner>/<repo>`. `CloseApplications=no` vì app có mutex: `AppMutex` buộc người dùng tự thoát (để app hoàn tác bằng "Thoát & khôi phục"), Setup không tự giết app.

- [x] **Step 3: Thêm bước Setup và SHA256 vào cuối `build/publish.ps1`**

```powershell
if (-not $SkipInstaller) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($cmd) { $cmd.Source } else { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if (-not (Test-Path $iscc)) { throw 'Không tìm thấy ISCC.exe (Inno Setup 6). Cài: winget install --id JRSoftware.InnoSetup -e' }
    & $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/O$artifacts" (Join-Path $PSScriptRoot 'installer/LunaDNS.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC lỗi ($LASTEXITCODE)." }
}

$outputs = Get-ChildItem $artifacts -File | Where-Object { $_.Name -like "LunaDNS-*$Version*" -and $_.Extension -in '.exe', '.zip' }
if ($outputs) {
    $outputs | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
        Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    Get-Content (Join-Path $artifacts 'SHA256SUMS.txt')
}
```

Script phải chạy được bằng cả `pwsh` 7 và `powershell` 5.1 (máy không có `pwsh` thì `.cmd` của T7 dùng 5.1), nên không dùng `?.`, `??` hay toán tử ba ngôi.

- [x] **Step 4: Tạo `build/sandbox/LunaDNS.wsb`**

```xml
<!-- Mở bằng bấm đúp (cần bật Windows Sandbox). Sửa HostFolder nếu repo không ở D:\Projects\lunadns. -->
<Configuration>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>D:\Projects\lunadns\artifacts</HostFolder>
      <SandboxFolder>C:\Users\WDAGUtilityAccount\Desktop\artifacts</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <Networking>Enable</Networking>
</Configuration>
```

- [x] **Step 5: Build Setup**

Run: `pwsh -NoProfile -File build/publish.ps1`
Expected: có `artifacts/LunaDNS-Setup-0.5.0.exe`, `artifacts/LunaDNS-0.5.0-portable.zip`, `artifacts/SHA256SUMS.txt` có 2 dòng.

- [ ] **Step 6: Thử trong Windows Sandbox** (bật một lần: `Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM`, khởi động lại) — chưa làm: cấm mở Sandbox/chạy Setup trong phiên này, PO thử tay (T11)

Mở `build/sandbox/LunaDNS.wsb`. Trong Sandbox, chép Setup từ `Desktop\artifacts` ra Desktop, gắn Mark-of-the-Web giả lập rồi chạy:

```powershell
Copy-Item .\Desktop\artifacts\LunaDNS-Setup-*.exe .\Desktop\
Set-Content .\Desktop\LunaDNS-Setup-*.exe -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"
```

Kiểm lần lượt:
1. Bấm đúp Setup: SmartScreen hiện (More info → Run anyway), UAC "Unknown publisher", cài xong, ô "Mở LunaDNS" mở được app.
2. `schtasks /Query /TN \LunaDNS\Cleanup /V /FO LIST` → `Run As User: SYSTEM`, có hai trigger.
3. `(Get-Acl $env:ProgramData\LunaDNS).Access` đúng ba dòng như T4 Step 11.
4. Bật Định tuyến DNS, rồi `Stop-Process -Name LunaDNS -Force`; `schtasks /Run /TN \LunaDNS\Cleanup`; đợi 10 giây; `Get-DnsClientNrptRule | Where-Object Comment -like '*LunaDNS*'` rỗng.
5. Chạy Setup lần hai khi app đang mở: Setup báo app đang chạy (AppMutex). Thoát app, cài lại: thành công (nhánh nâng cấp, `--cleanup` bản cũ trả 0).
6. Settings → Apps → LunaDNS → Uninstall: xong; không còn `C:\Program Files\LunaDNS`, `%ProgramData%\LunaDNS`, task `\LunaDNS\Cleanup`, shortcut Start Menu, luật NRPT có Comment LunaDNS, `AutoConfigURL` của LunaDNS.

Ghi kết quả từng mục vào memory KH7.

- [ ] **Step 7: Commit**

```bash
git add build/installer/LunaDNS.iss build/publish.ps1 build/sandbox/LunaDNS.wsb
git commit -m "build: Setup Inno gọi --install-system/--uninstall, SHA256SUMS"
```

---

### Task 7: Bấm một lần khi clone repo

**Files:**
- Create: `Chay-tu-ma-nguon.cmd` (gốc repo; lưu UTF-8 **không BOM**, CRLF)

**Interfaces:**
- Consumes: `build/publish.ps1 -SkipZip -SkipInstaller` (T1, T6).

- [x] **Step 1: Viết script**

```bat
@echo off
rem LunaDNS: build tu ma nguon roi mo app (KH7 K12). Can .NET 8 SDK. Nguoi dung thuong: tai ban da build o GitHub Releases.
chcp 65001 >nul
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1 || goto :nosdk
dotnet --list-sdks | findstr /b /c:"8." >nul || goto :nosdk

set "PS=pwsh"
where pwsh >nul 2>&1 || set "PS=powershell"
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\publish.ps1" -SkipZip -SkipInstaller
if errorlevel 1 (
  echo.
  echo Build lỗi. Xem thông báo ở trên.
  pause
  exit /b 1
)
start "" "%~dp0artifacts\publish\win-x64\LunaDNS.exe"
exit /b 0

:nosdk
echo Cần .NET 8 SDK để build từ mã nguồn.
echo   Cài: winget install Microsoft.DotNet.SDK.8
echo   Hoặc tải bản đã build sẵn (không cần .NET) ở trang Releases của repo trên GitHub.
pause
exit /b 1
```

- [ ] **Step 2: Thử** — chưa chạy: bước mở exe không tách được, chỉ đọc lại; thử tay (T11)

1. Bấm đúp `Chay-tu-ma-nguon.cmd` trên máy dev: build xong, UAC hiện, app mở.
2. Trong Windows Sandbox (không có .NET): chép thư mục repo vào, bấm đúp → in hướng dẫn cài SDK, chờ phím, không lỗi font (tiếng Việt hiển thị đúng).

- [ ] **Step 3: Commit**

```bash
git add Chay-tu-ma-nguon.cmd
git commit -m "build: Chay-tu-ma-nguon.cmd build và mở app khi clone"
```

---

### Task 8: GitHub Actions

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`
- Create: `docs/release-notes/v0.9.0.md`

**Interfaces:**
- Consumes: `build/publish.ps1` (T1, T6); test của 3 project.

- [x] **Step 1: `ci.yml`**

```yaml
name: ci
on:
  push:
    branches: [main]
  pull_request:

permissions:
  contents: read

jobs:
  build-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - run: dotnet build -c Release -warnaserror
      # Test Admin/mạng thật tự skip vì không đặt LUNADNS_ADMIN_TESTS / LUNADNS_NETWORK_TESTS.
      - run: dotnet test tests/LunaDns.Core.Tests -c Release --no-build
      - run: dotnet test tests/LunaDns.Platform.Windows.Tests -c Release --no-build
      - run: dotnet test tests/LunaDns.App.Tests -c Release --no-build
```

- [x] **Step 2: `release.yml`**

```yaml
name: release
on:
  push:
    tags: ['v*']

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Kiểm tag, phiên bản và release notes
        id: version
        shell: pwsh
        run: |
          $tag = '${{ github.ref_name }}'
          if ($tag -notmatch '^v(\d+\.\d+\.\d+)$') { throw "Tag '$tag' không phải vX.Y.Z." }
          $version = $Matches[1]
          $csproj = ([xml](Get-Content src/LunaDns.App/LunaDns.App.csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
          if ($csproj -ne $version) { throw "Tag $version khác <Version> $csproj trong csproj." }
          if (-not (Test-Path "docs/release-notes/$tag.md")) { throw "Thiếu docs/release-notes/$tag.md." }
          "version=$version" >> $env:GITHUB_OUTPUT
          "prerelease=$(if ($version.StartsWith('0.')) { 'true' } else { 'false' })" >> $env:GITHUB_OUTPUT

      - run: dotnet build -c Release -warnaserror
      - run: dotnet test tests/LunaDns.Core.Tests -c Release --no-build
      - run: dotnet test tests/LunaDns.Platform.Windows.Tests -c Release --no-build
      - run: dotnet test tests/LunaDns.App.Tests -c Release --no-build

      - name: Publish
        shell: pwsh
        run: ./build/publish.ps1 -Version ${{ steps.version.outputs.version }}

      # Bước ký SignPath thêm vào đây sau khi SignPath duyệt (xem "Việc không thuộc KH7"): ký exe trước Inno, rồi ký Setup.

      - name: Tạo Release nháp
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          $v = '${{ steps.version.outputs.version }}'
          $flags = @('--draft', '--title', "LunaDNS $v", '--notes-file', "docs/release-notes/v$v.md")
          if ('${{ steps.version.outputs.prerelease }}' -eq 'true') { $flags += '--prerelease' }
          gh release create "v$v" @flags "artifacts/LunaDNS-Setup-$v.exe" "artifacts/LunaDNS-$v-portable.zip" artifacts/SHA256SUMS.txt
```

- [x] **Step 3: `docs/release-notes/v0.9.0.md`**

```markdown
# LunaDNS 0.9.0 (bản thử)

Bản phát hành đầu tiên. Giúp vào Steam khi nhà mạng chặn DNS hoặc lọc SNI.

## Tải

- **LunaDNS-Setup-0.9.0.exe** — khuyên dùng. Cài vào Program Files, gỡ qua Settings → Apps.
- **LunaDNS-0.9.0-portable.zip** — giải nén rồi bấm đúp `LunaDNS.exe`. Không có tác vụ dọn dẹp lúc khởi động máy.
- Không cần cài .NET.

## Lưu ý

- Bản chưa ký số: Windows hiện "Windows protected your PC" → **More info → Run anyway**, và UAC ghi "Unknown publisher". Kiểm file bằng `SHA256SUMS.txt` (xem hướng dẫn cài đặt).
- Windows 11 bật **Smart App Control** sẽ chặn hẳn bản chưa ký.
- Cần quyền Admin.

Hướng dẫn: docs/guide/01-cai-dat.md.
```

- [ ] **Step 4: Kiểm cú pháp** — chưa làm: cần push/PR, chưa có remote; đã đọc kỹ YAML

Run: `gh workflow view` chưa dùng được trước khi push; thay vào đó đẩy branch và mở PR nháp: `ci` phải xanh trên PR. `release.yml` chỉ thử thật ở T11.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows docs/release-notes/v0.9.0.md
git commit -m "ci: build/test cho PR, tag v* tạo Release nháp"
```

---

### Task 9: Nút "Kiểm tra cập nhật" và "Gỡ khỏi máy" (sau KH6)

**Làm sau khi KH6 đã merge.** Đọc lại `MainViewModel` và `MainWindow.axaml` sau KH6 trước khi sửa: chèn hai nút vào mục Cài đặt (hoặc tên tương ứng sau KH6), giữ style KH6 (Fluent icons, không đánh số).

**Files:**
- Create: `src/LunaDns.App/ISystemShell.cs`, `src/LunaDns.App/Maintenance.cs`
- Modify: `src/LunaDns.App/ViewModels/MainViewModel.cs`, `src/LunaDns.App/Views/MainWindow.axaml`, `src/LunaDns.App/App.axaml.cs` (chỗ dựng `MainViewModel`), `src/LunaDns.App/Strings.cs`, `src/LunaDns.App/Strings.resx`
- Test: `tests/LunaDns.App.Tests/MaintenanceTests.cs`

**Interfaces:**
- Consumes: `UpdateChecker`, `UpdateCheckResult`, `UpdateStatus` (T5); `InstallLocation.IsInstalled` (T4); `IUserPrompts` (KH5); `AppInfo` (T5).
- Produces:
  - `internal interface ISystemShell { void OpenUrl(string url); void StartProcess(string path); }`
  - `internal sealed class Maintenance(IUserPrompts prompts, ISystemShell shell)` với `Task CheckForUpdateAsync(Func<CancellationToken, Task<UpdateCheckResult>> check, Version current, CancellationToken ct)` và `Task<bool> UninstallAsync(string uninstallerPath)` (true = đã mở uninstaller, VM cần thoát).

- [x] **Step 1: Chuỗi giao diện**

Thêm vào `Strings.resx` (cạnh `DiagnosticsButton`):

```xml
  <data name="CheckUpdateButton" xml:space="preserve"><value>Kiểm tra cập nhật</value></data>
  <data name="UpdateAvailableTitle" xml:space="preserve"><value>Có bản mới</value></data>
  <data name="UpdateAvailableFormat" xml:space="preserve"><value>Đã có LunaDNS {0} (bạn đang dùng {1}). Mở trang tải?</value></data>
  <data name="UpToDateTitle" xml:space="preserve"><value>Đang dùng bản mới nhất</value></data>
  <data name="UpToDateFormat" xml:space="preserve"><value>Bạn đang dùng LunaDNS {0}, bản mới nhất.</value></data>
  <data name="UpdateCheckFailed" xml:space="preserve"><value>Không kiểm tra được cập nhật. Thử lại sau, hoặc mở trang Releases của LunaDNS trên GitHub.</value></data>
  <data name="UninstallButton" xml:space="preserve"><value>Gỡ khỏi máy</value></data>
  <data name="UninstallConfirmTitle" xml:space="preserve"><value>Gỡ LunaDNS</value></data>
  <data name="UninstallConfirmMessage" xml:space="preserve"><value>LunaDNS sẽ tắt mọi tính năng, khôi phục cấu hình mạng rồi mở trình gỡ cài đặt. Tiếp tục?</value></data>
```

Thêm vào `Strings.cs` các thuộc tính cùng kiểu `public static string X => Get(nameof(X));` cho 9 khoá trên.

- [x] **Step 2: Viết test**

`tests/LunaDns.App.Tests/MaintenanceTests.cs`:

```csharp
using LunaDns.App.Updates;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Tests;

public sealed class MaintenanceTests
{
    private readonly FakePrompts _prompts = new();
    private readonly FakeShell _shell = new();

    private Maintenance Create() => new(_prompts, _shell);

    private static Func<CancellationToken, Task<UpdateCheckResult>> Returns(UpdateCheckResult result) => _ => Task.FromResult(result);

    [Fact]
    public async Task Newer_version_confirmed_opens_release_page()
    {
        _prompts.Answer = true;
        var result = new UpdateCheckResult(UpdateStatus.NewerAvailable, new Version(0, 9, 1), "https://github.com/o/r/releases/tag/v0.9.1");

        await Create().CheckForUpdateAsync(Returns(result), new Version(0, 9, 0), CancellationToken.None);

        Assert.Equal(["https://github.com/o/r/releases/tag/v0.9.1"], _shell.Urls);
        Assert.Contains("0.9.1", _prompts.Confirms.Single());
        Assert.Contains("0.9.0", _prompts.Confirms.Single());
    }

    [Fact]
    public async Task Newer_version_declined_opens_nothing()
    {
        _prompts.Answer = false;
        var result = new UpdateCheckResult(UpdateStatus.NewerAvailable, new Version(0, 9, 1), "https://github.com/o/r/releases/tag/v0.9.1");

        await Create().CheckForUpdateAsync(Returns(result), new Version(0, 9, 0), CancellationToken.None);

        Assert.Empty(_shell.Urls);
    }

    [Fact]
    public async Task Up_to_date_informs()
    {
        var result = new UpdateCheckResult(UpdateStatus.UpToDate, new Version(0, 9, 0), "https://github.com/o/r/releases/tag/v0.9.0");

        await Create().CheckForUpdateAsync(Returns(result), new Version(0, 9, 0), CancellationToken.None);

        Assert.Equal([Strings.UpToDateTitle], _prompts.InformTitles);
        Assert.Empty(_shell.Urls);
    }

    [Fact]
    public async Task Failure_informs()
    {
        await Create().CheckForUpdateAsync(Returns(UpdateCheckResult.Failed), new Version(0, 9, 0), CancellationToken.None);

        Assert.Equal([Strings.UpdateCheckFailed], _prompts.InformMessages);
    }

    [Fact]
    public async Task Uninstall_confirmed_starts_uninstaller()
    {
        _prompts.Answer = true;

        Assert.True(await Create().UninstallAsync(@"C:\Program Files\LunaDNS\unins000.exe"));
        Assert.Equal([@"C:\Program Files\LunaDNS\unins000.exe"], _shell.Processes);
    }

    [Fact]
    public async Task Uninstall_declined_does_nothing()
    {
        _prompts.Answer = false;

        Assert.False(await Create().UninstallAsync(@"C:\Program Files\LunaDNS\unins000.exe"));
        Assert.Empty(_shell.Processes);
    }

    private sealed class FakePrompts : IUserPrompts
    {
        public bool Answer { get; set; }

        public List<string> Confirms { get; } = [];

        public List<string> InformTitles { get; } = [];

        public List<string> InformMessages { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message)
        {
            Confirms.Add(message);
            return Task.FromResult(Answer);
        }

        public Task InformAsync(string title, string message)
        {
            InformTitles.Add(title);
            InformMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeShell : ISystemShell
    {
        public List<string> Urls { get; } = [];

        public List<string> Processes { get; } = [];

        public void OpenUrl(string url) => Urls.Add(url);

        public void StartProcess(string path) => Processes.Add(path);
    }
}
```

- [x] **Step 3: Chạy để thấy lỗi**

Run: `dotnet test tests/LunaDns.App.Tests --filter FullyQualifiedName~MaintenanceTests`
Expected: build lỗi, `Maintenance` / `ISystemShell` không tồn tại.

- [x] **Step 4: Cài đặt**

`src/LunaDns.App/ISystemShell.cs`:

```csharp
using System.Diagnostics;

namespace LunaDns.App;

/// <summary>Mở URL và chạy chương trình ngoài, để VM test được không cần Process thật.</summary>
internal interface ISystemShell
{
    void OpenUrl(string url);

    void StartProcess(string path);
}

internal sealed class SystemShell : ISystemShell
{
    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    public void StartProcess(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
```

`src/LunaDns.App/Maintenance.cs`:

```csharp
using System.Globalization;
using LunaDns.App.Updates;
using LunaDns.App.ViewModels;

namespace LunaDns.App;

/// <summary>Logic của nút "Kiểm tra cập nhật" (spec 4.10) và "Gỡ khỏi máy" (KH7 K4), tách khỏi VM để test.</summary>
internal sealed class Maintenance(IUserPrompts prompts, ISystemShell shell)
{
    public async Task CheckForUpdateAsync(Func<CancellationToken, Task<UpdateCheckResult>> check, Version current, CancellationToken cancellationToken)
    {
        var result = await check(cancellationToken).ConfigureAwait(true);
        var shown = new Version(current.Major, current.Minor, Math.Max(current.Build, 0)).ToString();
        switch (result.Status)
        {
            case UpdateStatus.NewerAvailable:
                var message = string.Format(CultureInfo.CurrentCulture, Strings.UpdateAvailableFormat, result.Latest, shown);
                if (await prompts.ConfirmAsync(Strings.UpdateAvailableTitle, message).ConfigureAwait(true)) shell.OpenUrl(result.Url!);
                break;
            case UpdateStatus.UpToDate:
                await prompts.InformAsync(Strings.UpToDateTitle, string.Format(CultureInfo.CurrentCulture, Strings.UpToDateFormat, shown)).ConfigureAwait(true);
                break;
            default:
                await prompts.InformAsync(Strings.CheckUpdateButton, Strings.UpdateCheckFailed).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>true khi đã mở trình gỡ: bên gọi phải thoát app (hoàn tác) để AppMutex của Setup cho gỡ tiếp.</summary>
    public async Task<bool> UninstallAsync(string uninstallerPath)
    {
        if (!await prompts.ConfirmAsync(Strings.UninstallConfirmTitle, Strings.UninstallConfirmMessage).ConfigureAwait(true)) return false;
        shell.StartProcess(uninstallerPath);
        return true;
    }
}
```

- [x] **Step 5: Nối vào VM và giao diện**

Trong `MainViewModel` (theo ctor hiện tại sau KH6), thêm tham số `ISystemShell shell` và:

```csharp
    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Maintenance _maintenance;

    // trong ctor, sau DiagnosticsCommand:
        _maintenance = new Maintenance(prompts, shell);
        CheckUpdateCommand = new AsyncCommand(CheckUpdateAsync, OnError);
        UninstallAppCommand = new AsyncCommand(UninstallAppAsync, OnError);

    public AsyncCommand CheckUpdateCommand { get; }

    public AsyncCommand UninstallAppCommand { get; }

    /// <summary>Chỉ bản do Setup cài có nút gỡ; bản portable gỡ theo guide 07.</summary>
    public bool CanUninstall { get; } = InstallLocation.IsInstalled(Environment.ProcessPath!, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    private Task CheckUpdateAsync() =>
        _maintenance.CheckForUpdateAsync(ct => new UpdateChecker(UpdateHttp, AppInfo.Repository, AppInfo.Version).CheckAsync(ct), AppInfo.Version, CancellationToken.None);

    private async Task UninstallAppAsync()
    {
        var uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (await _maintenance.UninstallAsync(uninstaller).ConfigureAwait(true)) await QuitCommand.ExecuteAsync().ConfigureAwait(true);
    }
```

(Nếu `AsyncCommand` không có `ExecuteAsync`, gọi thẳng delegate `quit` đã nhận ở ctor — lưu nó vào field `_quit`.)

Ở chỗ dựng `MainViewModel` (`App.axaml.cs` hoặc `MainWindow.axaml.cs`), truyền `new SystemShell()`.

Trong `MainWindow.axaml`, cạnh nút Chẩn đoán:

```xml
        <Button Content="{x:Static app:Strings.CheckUpdateButton}" Command="{Binding CheckUpdateCommand}" />
        <Button Content="{x:Static app:Strings.UninstallButton}" Command="{Binding UninstallAppCommand}"
                IsVisible="{Binding CanUninstall}" />
```

(Nếu Expander chỉ chứa một phần tử con, bọc các nút trong `StackPanel Spacing="8"` theo style KH6.)

- [ ] **Step 6: Chạy test và thử** (build/test đã xanh 82 pass, 0 warning; phần thử tay chưa chạy: cần PO thử tay)

Run: `dotnet build -warnaserror; dotnet test tests/LunaDns.App.Tests`
Expected: 0 cảnh báo; mọi test PASS (gồm `StringsTests` với 9 khoá mới, 6 `MaintenanceTests`).

Thử: bản Debug → "Kiểm tra cập nhật" báo lỗi mềm hoặc "bản mới nhất" (tuỳ repo đã có Release chưa); nút "Gỡ khỏi máy" không hiện. Bản Setup trong Sandbox (T6 Step 6) → nút hiện, bấm → app thoát và trình gỡ mở.

- [ ] **Step 7: Commit**

```bash
git add src/LunaDns.App tests/LunaDns.App.Tests/MaintenanceTests.cs
git commit -m "feat(app): nút Kiểm tra cập nhật và Gỡ khỏi máy"
```

---

### Task 10: Tài liệu (phiên chính, PO duyệt chữ)

**Files:** `README.md`, `docs/guide/01-cai-dat.md`, `docs/guide/02-chay-va-quyen-admin.md`, `docs/guide/06-dong-lenh.md`, `docs/guide/07-go-va-don-dep.md`, `docs/guide/08-su-co.md`

- [x] **Step 1: README** — đầu file: "**Không phải dev?** Tải `LunaDNS-Setup-x.y.z.exe` ở Releases, bấm đúp, xong." Mục "Cài nhanh (từ mã nguồn)" đổi thành: bấm đúp `Chay-tu-ma-nguon.cmd` (cần .NET 8 SDK), hoặc `pwsh build/publish.ps1`. Mục "Các lệnh" thêm `--install-system`, `--uninstall` và bảng mã thoát 0–4. Mục "Những gì chưa có": bỏ "bộ cài/publish", thêm "chưa ký số, chưa tự cập nhật".
- [x] **Step 2: guide 01** — viết lại: (a) Setup: tải, SmartScreen "More info → Run anyway", UAC "Unknown publisher", kiểm SHA-256 bằng `Get-FileHash .\LunaDNS-Setup-0.9.0.exe -Algorithm SHA256` so với `SHA256SUMS.txt`; (b) portable: chuột phải zip → Properties → Unblock **trước khi** giải nén (Explorer truyền Mark-of-the-Web sang mọi file), giải nén vào thư mục riêng, bấm đúp; khác biệt: không có tác vụ dọn dẹp lúc khởi động; (c) Smart App Control trên Windows 11 chặn bản chưa ký; (d) build từ mã nguồn; (e) nâng cấp: chạy Setup mới, Setup tự khôi phục cấu hình của bản cũ trước. Bỏ mục ".NET 8 Desktop Runtime".
- [x] **Step 3: guide 02** — bấm đúp là có UAC (manifest), không cần "Run as administrator".
- [x] **Step 4: guide 06** — `--install-system`, `--uninstall`, mã thoát 3 và 4.
- [x] **Step 5: guide 07** — bản cài: Settings → Apps → LunaDNS → Uninstall (hoặc nút "Gỡ khỏi máy"); gỡ dừng lại nếu khôi phục chưa xong và cần làm gì. Bản portable: "Thoát & khôi phục", chạy `LunaDNS.exe --uninstall` trong PowerShell Admin, rồi xoá thư mục. Task `\LunaDNS\Cleanup`: là gì, chạy lúc nào, xoá tay bằng `schtasks /Delete /TN \LunaDNS\Cleanup /F`.
- [x] **Step 6: guide 08** — "Setup báo không khôi phục được bản cũ", "Setup báo chưa đăng ký được tác vụ dọn dẹp", "thư mục log mở được nhưng không sửa được (đúng thiết kế: chỉ Admin ghi)".
- [ ] **Step 7:** (14/11: PO đã duyệt chữ Step 1–6 + guide 05; chưa commit theo ràng buộc PO) PO duyệt chữ, rồi commit `docs: hướng dẫn cài bằng Setup, portable, gỡ`.

---

### Task 11 (PO): Thử tay, phát hành v0.9.0, nộp SignPath

**Điều kiện vào:** KH6 đã merge; T1–T10 xong, CI xanh trên `main`.

- [ ] **Step 1:** Đổi `<Version>` trong `src/LunaDns.App/LunaDns.App.csproj` thành `0.9.0`; `AppPublisherURL` trong `.iss` thành URL repo; commit `chore: v0.9.0`, push.
- [ ] **Step 2:** `git tag v0.9.0; git push origin v0.9.0` → `release.yml` xanh, có Release **nháp** với 3 file.
- [ ] **Step 3:** Tải 3 file từ Release nháp (đăng nhập GitHub), chạy lại toàn bộ T6 Step 6 trong Windows Sandbox với file **tải về thật** (Mark-of-the-Web thật), cộng bản portable: Unblock zip, giải nén, bấm đúp, bật/tắt, `--uninstall`.
- [ ] **Step 4: Trigger thật trên máy thật hoặc VM** (Sandbox không khởi động lại được): cài Setup, bật Định tuyến DNS và Proxy SNI, `Stop-Process -Name LunaDNS -Force`, khởi động lại máy, đăng nhập. Kiểm: không còn luật NRPT có Comment LunaDNS, `AutoConfigURL` của user đã về như trước, `journal.json` trống; log có dòng `--cleanup: đã hoàn tác`.
- [ ] **Step 5: Nâng cấp:** bản 0.9.0 đang cài + bật một tính năng + thoát bằng kill → chạy Setup 0.9.1 thử (build tay `pwsh build/publish.ps1 -Version 0.9.1`) → Setup hoàn tác bằng exe cũ, cài xong, app mở sạch.
- [ ] **Step 6:** Đạt hết thì bấm **Publish release** trên GitHub. Ghi kết quả từng bước vào memory KH7.
- [ ] **Step 7:** Nộp đơn SignPath Foundation (https://signpath.org) cho repo. Khi được duyệt, mở plan nhỏ "ký số trong release.yml".

---

## Đối chiếu yêu cầu → task

| Câu hỏi của PO | Trả lời | Task |
|---|---|---|
| Publish self-contained single-file `win-x64`, cảnh báo trim của Journal (QĐ 16) | Không trim (K5); single-file + DLL native cạnh exe (K6) | T1 |
| Tự xin quyền Admin | Manifest `requireAdministrator` sẵn có; Setup `PrivilegesRequired=admin` | T1, T6 |
| Clone về chạy 1 click | `Chay-tu-ma-nguon.cmd` (K12); exe không vào repo | T7 |
| SmartScreen / Mark-of-the-Web, ký số | Setup chỉ một lần SmartScreen; Unblock zip; SHA256; SignPath sau (K3) | T6, T10, T11 |
| Cập nhật | Nút kiểm tra (K10); Setup mới hoàn tác bằng exe cũ (K11) | T5, T6, T9 |
| Gỡ cài và dọn (`--cleanup`, task SYSTEM KH4b) | `--install-system`, `--uninstall`, ACL mỗi lần chạy (K4, K7–K9) | T2, T3, T4, T6, T9 |
| CI build tự động | `ci.yml` + `release.yml` → Release nháp (K13) | T8 |
| Chặn phát hành khi KH4b chưa xong (QĐ 12) | Phần hệ thống KH4b nằm trong KH7; T11 cần KH6 + T1–T10 | T11 |
